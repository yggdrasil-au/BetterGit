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

        public void SetChannel(string channel) {
            _versionService.SetChannel(channel);
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

                // Auto-generate message if empty
                if (string.IsNullOrWhiteSpace(message)) {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"On branch {repo.Head.FriendlyName}");
                    sb.AppendLine();
                    sb.AppendLine("Files changed in this commit:");
                    
                    foreach (var entry in status) {
                        if (entry.State == FileStatus.Ignored) continue;

                        string stateStr = "modified";
                        string s = entry.State.ToString();
                        if (s.Contains("New") || s.Contains("Added")) stateStr = "added";
                        else if (s.Contains("Deleted")) stateStr = "deleted";
                        else if (s.Contains("Renamed")) stateStr = "renamed";
                        
                        sb.AppendLine($"\t{stateStr}:   {entry.FilePath}");
                    }
                    message = sb.ToString();
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

                // Find branches that are "undo" archives
                var candidates = repo.Branches
                    .Where(b => b.FriendlyName.StartsWith("archive/undo_"))
                    .Select(b => new { Branch = b, Commit = b.Tip })
                    // Exclude the current state so we can step through the redo stack
                    .Where(x => x.Commit.Sha != currentCommit.Sha)
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
        }

        // --- COMMAND: MERGE ---
        public void Merge(string sourceSha) {
            if (!IsValidGitRepo()) {
                throw new Exception("Not a valid BetterGit repository. Run 'init' first.");
            }

            EnsureSafeState();

            using (Repository repo = new Repository(_repoPath)) {
                Commit? sourceCommit = repo.Lookup<Commit>(sourceSha);
                if (sourceCommit == null) {
                    throw new Exception($"Commit {sourceSha} not found.");
                }

                Console.WriteLine($"Merging {sourceSha.Substring(0, 7)} into HEAD...");

                Signature author = repo.Config.BuildSignature(DateTime.Now);
                if (author == null) {
                    author = new Signature("BetterGit User", "user@bettergit.local", DateTime.Now);
                }

                // We use NoFastForward to ensure we can stop and review (and because we want to preserve history structure usually)
                // CommitOnSuccess = false allows the user to review changes before saving.
                MergeResult result = repo.Merge(sourceCommit, author, new MergeOptions {
                    CommitOnSuccess = false,
                    FastForwardStrategy = FastForwardStrategy.NoFastForward
                });

                // Fix: Revert changes to .betterGit folder to avoid version conflicts
                // We want to keep the current version, not the one from the merge source.
                var betterGitFiles = repo.RetrieveStatus()
                    .Where(s => s.FilePath.Replace("\\", "/").StartsWith(".betterGit/"))
                    .Select(s => s.FilePath)
                    .ToList();

                if (betterGitFiles.Any()) {
                    // Checkout from HEAD (current state), effectively ignoring the merge for these files
                    repo.CheckoutPaths(repo.Head.Tip.Sha, betterGitFiles, new CheckoutOptions {
                        CheckoutModifiers = CheckoutModifiers.Force
                    });
                }

                if (repo.Index.Conflicts.Any()) {
                    Console.WriteLine("Merge resulted in conflicts. Please resolve them in the editor and then Save.");
                } else if (result.Status == MergeStatus.UpToDate) {
                    Console.WriteLine("Already up to date.");
                } else {
                    Console.WriteLine("Merge staged. Review changes in the list and Save when ready.");
                }
            }
        }

        // --- HELPER: JSON OUTPUT FOR VS CODE ---
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
                                       sha = b.Tip.Sha,
                                       version = ExtractVersion(b.Tip.MessageShort),
                                       message = ExtractMessage(b.Tip.MessageShort)
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

        // --- COMMAND: SCAN-REPOS ---
        public string ScanRepositories() {
            var submodules = new List<object>();
            var nested = new List<object>();
            var knownPaths = new HashSet<string>();

            // 1. Submodules (Only if root is a repo)
            if (IsValidGitRepo()) {
                try {
                    using (var repo = new Repository(_repoPath)) {
                        foreach (var sm in repo.Submodules) {
                            // Requirement: Only show items that exist
                            string fullPath = Path.Combine(_repoPath, sm.Path);
                            if (Directory.Exists(fullPath)) {
                                submodules.Add(new { path = sm.Path, name = sm.Name });
                                knownPaths.Add(Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant());
                            }
                        }
                    }
                } catch { /* Ignore errors reading submodules */ }
            }

            // 2. Nested Repos (Manual Scan)
            // We scan up to depth 50, skipping common ignore folders
            ScanDir(_repoPath, nested, knownPaths, 0);

            return JsonConvert.SerializeObject(new {
                root = _repoPath,
                submodules,
                nested
            }, Formatting.Indented);
        }

        private void ScanDir(string dir, List<object> results, HashSet<string> knownPaths, int depth) {
            if (depth > 50) return;

            try {
                var directories = Directory.GetDirectories(dir);
                foreach (var d in directories) {
                    var name = Path.GetFileName(d);
                    
                    // Skip common junk
                    if (name.StartsWith(".") && name != ".git") continue; // Skip .vscode, .vs, etc.
                    if (name == "node_modules" || name == "bin" || name == "obj" || name == "packages") continue;

                    // Check if this is a git repo
                    string gitPath = Path.Combine(d, ".git");
                    if (Directory.Exists(gitPath) || File.Exists(gitPath)) { 
                        // It's a repo.
                        
                        // Requirement: nested repos list should only show items that dont exist in the submodules file
                        string fullPath = Path.GetFullPath(d).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
                        if (knownPaths.Contains(fullPath)) {
                            continue;
                        }

                        // Calculate relative path
                        string relPath = Path.GetRelativePath(_repoPath, d);
                        results.Add(new { path = relPath, name = name });
                        
                        // Don't recurse into a repo
                        continue;
                    }

                    // Recurse
                    ScanDir(d, results, knownPaths, depth + 1);
                }
            } catch { /* Access denied etc */ }
        }
    }
}