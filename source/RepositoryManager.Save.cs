using LibGit2Sharp;

namespace BetterGit;

public partial class RepositoryManager {
    /* :: :: Commands :: START :: */

    // --- COMMAND: SAVE ---
    public void Save(string message, VersionChangeType changeType = VersionChangeType.Patch, string? manualVersion = null) {
        if (!IsValidGitRepo()) {
            throw new Exception("Not a valid BetterGit repository. Run 'init' first.");
        }

        using (Repository? repo = new Repository(_repoPath)) {
            // 1. Stage all changes (Automatic "git add -A")
            // LibGit2Sharp can throw on Windows when long paths exist (often in untracked files).
            try {
                Commands.Stage(repo, "*");
            } catch (Exception ex) {
                if (IsPathTooLongError(ex)) {
                    RunGitOrThrow(_repoPath, "add -A");
                } else {
                    throw;
                }
            }

            // 2. Check if there is anything to commit
            if (!IsRepoDirtySafe(repo, _repoPath)) {
                Console.WriteLine("No changes to save.");
                return;
            }

            // Auto-generate message if empty
            if (string.IsNullOrWhiteSpace(message)) {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                List<(string path, string status)> entries = GetChangesSafe(repo, _repoPath, includeUntracked: true);
                sb.AppendLine($"changes: {entries.Count}");
                sb.AppendLine();
                sb.AppendLine("Files changed in this commit:");

                foreach ((string path, string status) entry in entries) {
                    string stateStr = "modified";
                    string s = entry.status;
                    if (s.Contains("New") || s.Contains("Added")) {
                        stateStr = "added";
                    } else if (s.Contains("Deleted")) {
                        stateStr = "deleted";
                    } else if (s.Contains("Renamed")) {
                        stateStr = "renamed";
                    }

                    sb.AppendLine($"\t{stateStr}:   {entry.path}");
                }
                message = sb.ToString();
            }

            // 3. Update Version
            string version = _versionService.IncrementVersion(changeType, manualVersion);

            // Stage the metadata files explicitly to be sure
            try {
                Commands.Stage(repo, ".betterGit/meta.toml");
            } catch (Exception ex) {
                if (IsPathTooLongError(ex)) {
                    RunGitOrThrow(_repoPath, "add .betterGit/meta.toml");
                } else {
                    throw;
                }
            }
            if (File.Exists(Path.Combine(_repoPath, "package.json"))) {
                try {
                    Commands.Stage(repo, "package.json");
                } catch (Exception ex) {
                    if (IsPathTooLongError(ex)) {
                        RunGitOrThrow(_repoPath, "add package.json");
                    } else {
                        throw;
                    }
                }
            }

            // 4. Commit
            Signature author = repo.Config.BuildSignature(DateTime.Now);
            if (author == null) {
                author = new Signature(name: "BetterGit User", email: "user@bettergit.local", when: DateTime.Now);
            }

            repo.Commit($"[{version}] {message}", author, author);

            Console.WriteLine($"Saved successfully: [{version}] {message}");
        }
    }

    /* :: :: Commands :: END :: */
}
