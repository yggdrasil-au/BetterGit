using LibGit2Sharp;

using System.Diagnostics;

namespace BetterGit;

public partial class RepositoryManager {
    /* :: :: Private Helpers :: START :: */

    // --- PRIVATE HELPERS ---

    private void EnsureSafeState() {
        using (Repository repo = new Repository(_repoPath)) {
            if (IsRepoDirtySafe(repo, _repoPath)) {
                throw new Exception("Unsaved changes detected. You must 'Save' before moving or undoing.");
            }
        }
    }

    private static bool IsPathTooLongError(Exception ex) {
        if (ex is PathTooLongException) {
            return true;
        }
        string msg = ex.Message;
        return msg.Contains("path too long", StringComparison.OrdinalIgnoreCase) || msg.Contains("PathTooLong", StringComparison.OrdinalIgnoreCase);
    }

    // LibGit2Sharp can reject uncommon working-tree entries such as type changes and symbolic-link transitions
    // even when git CLI can still read them. When that happens, we fall back to the git executable so BetterGit
    // stays usable instead of surfacing a hard error.
    private static bool ShouldFallbackToGitCli(Exception ex) {
        if (IsPathTooLongError(ex)) {
            return true;
        }

        string msg = ex.Message;
        return msg.Contains("unexpected ChangeKind", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("ChangeKind", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("TypeChanged", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("SymbolicLink", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("symbolic link", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("symlink", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRepoDirtySafe(Repository repo, string repoPath) {
        try {
            // Avoid untracked recursion which is where long-path failures most often occur.
            StatusOptions opts = new StatusOptions {
                IncludeUntracked = false,
                RecurseUntrackedDirs = false
            };
            return repo.RetrieveStatus(opts).IsDirty;
        } catch (Exception statusEx) {
            if (!ShouldFallbackToGitCli(statusEx)) {
                throw;
            }

            // Fallback to git CLI, which can handle long paths and a few LibGit2Sharp edge cases more gracefully.
            (int exitCode, string stdout, string stderr) = RunGit(repoPath, "status --porcelain");
            if (exitCode != 0) {
                return true;
            }
            return !string.IsNullOrWhiteSpace(stdout);
        }
    }

    private static List<(string path, string status)> GetChangesSafe(Repository repo, string repoPath, bool includeUntracked) {
        try {
            StatusOptions opts = new StatusOptions {
                IncludeUntracked = includeUntracked,
                RecurseUntrackedDirs = includeUntracked
            };

            return repo.RetrieveStatus(opts)
                .Where(s => s.State != FileStatus.Ignored)
                .Select(s => (path: s.FilePath, status: s.State.ToString()))
                .ToList();
        } catch (Exception ex) {
            if (!ShouldFallbackToGitCli(ex)) {
                throw;
            }

            // Retry without untracked files first, then fall back to git CLI if LibGit2Sharp still rejects the tree.
            try {
                StatusOptions opts = new StatusOptions {
                    IncludeUntracked = false,
                    RecurseUntrackedDirs = false
                };

                return repo.RetrieveStatus(opts)
                    .Where(s => s.State != FileStatus.Ignored)
                    .Select(s => (path: s.FilePath, status: s.State.ToString()))
                    .ToList();
            } catch (Exception statusEx) {
                if (!ShouldFallbackToGitCli(statusEx)) {
                    throw;
                }

                // Final fallback: git status porcelain
                return GetChangesFromGit(repoPath);
            }
        }
    }

    private static List<(string path, string status)> GetChangesFromGit(string repoPath) {
        (int exitCode, string stdout, string stderr) = RunGit(repoPath, "status --porcelain");
        if (exitCode != 0) {
            // If git itself errors, surface it as a pseudo change so the UI can still render something.
            return new List<(string path, string status)> { (path: "(git)", status: $"Error: {stderr}".Trim()) };
        }

        List<(string path, string status)> results = new List<(string path, string status)>();
        foreach (string rawLine in stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)) {
            if (rawLine.Length < 3) {
                continue;
            }

            string code = rawLine.Substring(0, 2);
            string filePart = rawLine.Length > 3 ? rawLine.Substring(3) : string.Empty;
            if (string.IsNullOrWhiteSpace(filePart)) {
                continue;
            }

            // Handle renames like: R  old -> new
            int arrowIdx = filePart.LastIndexOf("->", StringComparison.Ordinal);
            string filePath = arrowIdx >= 0 ? filePart.Substring(arrowIdx + 2).Trim() : filePart.Trim();

            string status;
            if (code == "??") {
                status = "New";
            } else if (code.Contains('D')) {
                status = "Deleted";
            } else if (code.Contains('A')) {
                status = "New";
            } else if (code.Contains('M')) {
                status = "Modified";
            } else if (code.Contains('R')) {
                status = "Renamed";
            } else {
                status = "Changed";
            }

            results.Add((filePath, status));
        }

        return results;
    }

    private static void RunGitOrThrow(string repoPath, string args) {
        (int exitCode, _, string stderr) = RunGit(repoPath, args);
        if (exitCode != 0) {
            throw new Exception(string.IsNullOrWhiteSpace(stderr) ? "git command failed." : stderr.Trim());
        }
    }

    /// <summary>
    /// Adds a safe.directory config for the specified path, which is necessary for git CLI operations to work on repos that are owned by other users (for example, when running BetterGit as admin
    /// but the repo is owned by a standard user). This is a static method because users may need to call it before the RepositoryManager instance can be initialized.
    /// </summary>
    private static (int exitCode, string stdout, string stderr) RunGit(string repoPath, string args) {
        ProcessStartInfo psi = new ProcessStartInfo(fileName: "git", arguments: args) {
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process? process = Process.Start(psi);
        if (process == null) {
            return (1, string.Empty, "Failed to start git process.");
        }
        using (process) {
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, stdout, stderr);
        }

    }

    internal string ExtractVersion(string msg) {
        if (msg.StartsWith("[") && msg.Contains("]")) {
            return msg.Substring(1, msg.IndexOf(']') - 1);
        }
        return "v?"; // no version exists, often means commit made outside BetterGit
    }

    private string ExtractMessage(string msg) {
        if (msg.StartsWith("[") && msg.Contains("]")) {
            return msg.Substring(msg.IndexOf(']') + 1).Trim();
        }
        return msg;
    }

    /* :: :: Private Helpers :: END :: */
}
