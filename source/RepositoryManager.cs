using LibGit2Sharp;
using Newtonsoft.Json;
using BetterGit.Services;

namespace BetterGit.source {
    public class RepositoryManager {
        private readonly string _repoPath;
        private readonly VersionService _versionService;

        public RepositoryManager(string path) {
            _repoPath = path;
            _versionService = new VersionService(path);
        }

        public bool IsValidGitRepo() {
            return Repository.IsValid(_repoPath);
        }

        public static void InitProject(string path, bool isNode = false) {
            ProjectInitService.InitProject(path, isNode);
        }

        // --- COMMAND: SAVE ---
        public void Save(string message, VersionChangeType changeType = VersionChangeType.Patch) {
            if (!IsValidGitRepo()) {
                throw new Exception("Not a valid BetterGit repository. Run 'init' first.");
            }

            using (Repository? repo = new Repository(_repoPath)) {
                // 1. Stage all changes (Automatic "git add .")
                Commands.Stage(repo, "*");

                // 2. Check if there is anything to commit
                RepositoryStatus? status = repo.RetrieveStatus();
                if (!status.IsDirty) {
                    Console.WriteLine("No changes to save.");
                    return;
                }

                // 3. Update Version
                string version = _versionService.IncrementVersion(changeType);

                // Stage the metadata files explicitly to be sure
                Commands.Stage(repo, ".betterGit/meta.toml");
                if (File.Exists(Path.Combine(_repoPath, "package.json"))) {
                    Commands.Stage(repo, "package.json");
                }

                // 4. Commit
                Signature author = repo.Config.BuildSignature(DateTime.Now);
                if (author == null) {
                    author = new Signature(name: "BetterGit User", email: "user@bettergit.local", DateTime.Now);
                }

                repo.Commit($"[{version}] {message}", author, author);

                Console.WriteLine($"Saved successfully: [{version}] {message}");
            }
        }

        // --- COMMAND: UNDO ---
        public void Undo() {
            if (!IsValidGitRepo()) {
                throw new Exception("Not a valid BetterGit repository. Run 'init' first.");
            }

            EnsureSafeState();

            using (Repository? repo = new Repository(_repoPath)) {
                Commit? currentCommit = repo.Head.Tip;

                if (!currentCommit.Parents.Any()) {
                    throw new Exception("Cannot Undo: This is the first commit.");
                }

                Commit? parentCommit = currentCommit.Parents.First();

                string parkBranchName = $"archive/undo_{DateTime.Now:yyyyMMdd_HHmmss}_{currentCommit.Sha.Substring(0, 7)}";
                repo.CreateBranch(parkBranchName, currentCommit);

                repo.Reset(ResetMode.Hard, parentCommit);

                Console.WriteLine($"Undone. Previous state archived at: {parkBranchName}");
            }
        }

        // --- COMMAND: REDO ---
        public void Redo() {
            if (!IsValidGitRepo()) {
                throw new Exception("Not a valid BetterGit repository. Run 'init' first.");
            }

            EnsureSafeState();

            using (Repository? repo = new Repository(_repoPath)) {
                Commit? currentCommit = repo.Head.Tip;

                // Find branches that are "undo" archives and whose tip has currentCommit as a parent
                var candidates = repo.Branches
                    .Where(b => b.FriendlyName.StartsWith("archive/undo_"))
                    .Select(b => new { Branch = b, Commit = b.Tip })
                    .Where(x => x.Commit.Parents.Any(p => p.Sha == currentCommit.Sha))
                    .OrderByDescending(x => x.Branch.FriendlyName) // Newer timestamps first
                    .ToList();

                if (!candidates.Any()) {
                    throw new Exception("Nothing to Redo. No undone states found from this point.");
                }

                // Pick the most recent one
                var target = candidates.First();

                // Restore to that commit
                // We call Restore directly. It will handle parking the current state (which is the parent)
                // into a swapped branch, ensuring we don't lose the "undo" point either.
                Restore(target.Commit.Sha);
            }
        }

        // --- COMMAND: RESTORE / REDO ---
        public void Restore(string targetSha) {
            if (!IsValidGitRepo()) {
                throw new Exception("Not a valid BetterGit repository. Run 'init' first.");
            }

            EnsureSafeState();

            using (Repository? repo = new Repository(_repoPath)) {
                Commit? currentCommit = repo.Head.Tip;
                Commit? targetCommit = repo.Lookup<Commit>(targetSha);

                if (targetCommit == null) {
                    throw new Exception("Target state not found.");
                }

                bool isTargetAncestor = repo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = currentCommit }).Any(c => c.Sha == targetCommit.Sha);

                if (currentCommit.Sha != targetCommit.Sha) {
                    string parkBranchName = $"archive/swapped_{DateTime.Now:yyyyMMdd_HHmmss}_{currentCommit.Sha.Substring(0, 7)}";
                    repo.CreateBranch(parkBranchName, currentCommit);
                    Console.WriteLine($"Current changes parked in {parkBranchName}");
                }

                repo.Reset(ResetMode.Hard, targetCommit);
                Console.WriteLine($"Restored to {targetSha.Substring(0, 7)}");
            }
        }

        // --- COMMAND: PUBLISH ---
        public void Publish() {
            if (!IsValidGitRepo()) {
                throw new Exception("Not a valid BetterGit repository. Run 'init' first.");
            }

            using (Repository repo = new Repository(_repoPath)) {
                var remotes = repo.Network.Remotes;
                if (!remotes.Any()) {
                    Console.WriteLine("No remotes configured. Add a remote using 'git remote add <name> <url>' first.");
                    return;
                }

                foreach (var remote in remotes) {
                    Console.WriteLine($"Publishing to {remote.Name}...");

                    var branchName = repo.Head.FriendlyName;

                    var processInfo = new System.Diagnostics.ProcessStartInfo("git", $"push {remote.Name} {branchName}") {
                        WorkingDirectory = _repoPath,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    var process = System.Diagnostics.Process.Start(processInfo);
                    if (process != null) {
                        process.WaitForExit();

                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();

                        if (process.ExitCode == 0) {
                            Console.WriteLine($"Successfully published to {remote.Name}.");
                            if (!string.IsNullOrWhiteSpace(output)) Console.WriteLine(output);
                        } else {
                            Console.Error.WriteLine($"Failed to publish to {remote.Name}.");
                            Console.Error.WriteLine(error);
                        }
                    }
                }
            }
        }        // --- HELPER: JSON OUTPUT FOR VS CODE ---
        public string GetTreeDataJson() {
            if (!IsValidGitRepo()) {
                return JsonConvert.SerializeObject(new {
                    isInitialized = false,
                    changes = new List<string>(),
                    timeline = new List<object>(),
                    archives = new List<object>()
                });
            }

            using (Repository? repo = new Repository(_repoPath)) {
                List<string>? changes = repo.RetrieveStatus()
                                  .Where(s => s.State != FileStatus.Ignored)
                                  .Select(s => Path.GetFileName(s.FilePath))
                                  .ToList();

                var timeline = repo.Commits
                                   .Take(20)
                                   .Select(c => new {
                                       id = c.Sha,
                                       version = ExtractVersion(c.MessageShort),
                                       message = ExtractMessage(c.MessageShort)
                                   })
                                   .ToList();

                var archives = repo.Branches
                                   .Where(b => b.FriendlyName.StartsWith("archive/"))
                                   .Select(b => new {
                                       name = b.FriendlyName.Replace("archive/", ""),
                                       sha = b.Tip.Sha
                                   })
                                   .OrderByDescending(x => x.name)
                                   .ToList();

                var data = new {
                    isInitialized = true,
                    changes,
                    timeline,
                    archives
                };

                return JsonConvert.SerializeObject(data, Formatting.Indented);
            }
        }

        // --- COMMAND: CAT-FILE ---
        public void CatFile(string sha, string relativePath) {
            if (!IsValidGitRepo()) {
                return;
            }

            using (Repository? repo = new Repository(_repoPath)) {
                Commit? commit;
                if (sha.ToUpper() == "HEAD") {
                    commit = repo.Head.Tip;
                } else {
                    commit = repo.Lookup<Commit>(sha);
                }

                if (commit == null) {
                    throw new Exception("Commit not found.");
                }

                var treeEntry = commit[relativePath];
                if (treeEntry == null) {
                    return;
                }

                var blob = treeEntry.Target as Blob;
                if (blob == null) {
                    return;
                }

                using (var content = blob.GetContentStream())
                using (var reader = new StreamReader(content)) {
                    Console.Write(reader.ReadToEnd());
                }
            }
        }

        // --- PRIVATE HELPERS ---

        private void EnsureSafeState() {
            using (Repository? repo = new Repository(_repoPath)) {
                if (repo.RetrieveStatus().IsDirty) {
                    throw new Exception("Unsaved changes detected. You must 'Save' before moving or undoing.");
                }
            }
        }

        private string ExtractVersion(string msg) {
            if (msg.StartsWith("[") && msg.Contains("]")) {
                return msg.Substring(1, msg.IndexOf("]") - 1);
            }
            return "v?";
        }

        private string ExtractMessage(string msg) {
            if (msg.StartsWith("[") && msg.Contains("]")) {
                return msg.Substring(msg.IndexOf("]") + 1).Trim();
            }
            return msg;
        }
    }
}