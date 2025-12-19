using LibGit2Sharp;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BetterGit;

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
                var sb = new System.Text.StringBuilder();
                var entries = GetChangesSafe(repo, _repoPath, includeUntracked: true);
                sb.AppendLine($"changes: {entries.Count}");
                sb.AppendLine();
                sb.AppendLine("Files changed in this commit:");

                foreach (var entry in entries) {
                    string stateStr = "modified";
                    string s = entry.status;
                    if (s.Contains("New") || s.Contains("Added")) stateStr = "added";
                    else if (s.Contains("Deleted")) stateStr = "deleted";
                    else if (s.Contains("Renamed")) stateStr = "renamed";

                    sb.AppendLine($"\t{stateStr}:   {entry.path}");
                }
                message = sb.ToString();
            }

            // 3. Update Version
            string version = _versionService.IncrementVersion(changeType);

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
            List<string> betterGitFiles;
            try {
                var opts = new StatusOptions {
                    IncludeUntracked = false,
                    RecurseUntrackedDirs = false
                };
                betterGitFiles = repo.RetrieveStatus(opts)
                    .Where(s => s.FilePath.Replace("\\", "/").StartsWith(".betterGit/"))
                    .Select(s => s.FilePath)
                    .ToList();
            } catch {
                // If status fails (e.g., long path in untracked files), just skip this safety revert.
                betterGitFiles = new List<string>();
            }

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
                archives = new List<object>(),
                warnings = new List<string>()
            });
        }

        var warnings = GetGitmodulesWarnings();
        EmitWarningsToStderr(warnings);

        using (Repository? repo = new Repository(_repoPath)) {
            var changes = GetChangesSafe(repo, _repoPath, includeUntracked: true)
                .Select(s => new { path = s.path, status = s.status })
                .ToList();

            bool hasUpstream = false;
            int aheadBy = 0;
            int behindBy = 0;
            bool isPublishPending = false;
            string? upstream = null;

            try {
                hasUpstream = repo.Head.IsTracking && repo.Head.TrackedBranch != null;
                if (hasUpstream) {
                    upstream = repo.Head.TrackedBranch.FriendlyName;
                    aheadBy = repo.Head.TrackingDetails.AheadBy ?? 0;
                    behindBy = repo.Head.TrackingDetails.BehindBy ?? 0;
                    isPublishPending = aheadBy > 0;
                }
            } catch {
                // ignore tracking failures
            }

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
                archives,
                warnings,
                publish = new {
                    hasUpstream,
                    upstream,
                    aheadBy,
                    behindBy,
                    isPublishPending
                }
            };

            return JsonConvert.SerializeObject(data, Formatting.Indented);
        }
    }

    private static readonly Regex GitmodulesSectionRegex = new(
        "^\\s*\\[\\s*submodule\\s+\"(?<name>[^\"]+)\"\\s*\\]\\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex GitmodulesKeyValueRegex = new(
        "^\\s*(?<key>[A-Za-z0-9\\.\\-_]+)\\s*=\\s*(?<value>.*)\\s*$",
        RegexOptions.Compiled);

    private static string TrimGitmodulesValue(string value) {
        var v = value.Trim();
        if (v.Length >= 2 && ((v.StartsWith('"') && v.EndsWith('"')) || (v.StartsWith('\'') && v.EndsWith('\'')))) {
            v = v.Substring(1, v.Length - 2);
        }
        return v.Trim();
    }

    private static string NormalizeGitmodulesPath(string raw) {
        var p = raw.Trim();
        p = p.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        p = p.TrimEnd(Path.DirectorySeparatorChar);
        return p;
    }

    private List<string> GetGitmodulesWarnings() {
        var warnings = new List<string>();
        var gitmodulesPath = Path.Combine(_repoPath, ".gitmodules");
        if (!File.Exists(gitmodulesPath)) return warnings;

        string[] lines;
        try {
            lines = File.ReadAllLines(gitmodulesPath);
        } catch (Exception ex) {
            warnings.Add($".gitmodules: failed to read file: {ex.Message}");
            return warnings;
        }

        string? currentName = null;
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool parsedAnySection = false;

        void FinalizeCurrent() {
            if (currentName == null) return;
            parsedAnySection = true;

            current.TryGetValue("path", out var rawPath);
            current.TryGetValue("url", out var url);

            if (string.IsNullOrWhiteSpace(rawPath)) {
                warnings.Add($".gitmodules: submodule \"{currentName}\" is missing required key: path");
                return;
            }

            var relPath = NormalizeGitmodulesPath(rawPath);
            if (string.IsNullOrWhiteSpace(relPath)) {
                warnings.Add($".gitmodules: submodule \"{currentName}\" has an empty/invalid path value");
                return;
            }

            string repoRootFull;
            string subAbsFull;
            try {
                repoRootFull = Path.GetFullPath(_repoPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
                subAbsFull = Path.GetFullPath(Path.Combine(_repoPath, relPath));
            } catch (Exception ex) {
                warnings.Add($".gitmodules: submodule \"{currentName}\" path \"{rawPath}\" could not be resolved: {ex.Message}");
                return;
            }

            if (!subAbsFull.StartsWith(repoRootFull, StringComparison.OrdinalIgnoreCase)) {
                warnings.Add($".gitmodules: submodule \"{currentName}\" path \"{rawPath}\" resolves outside repo root (ignored)");
                return;
            }

            if (string.IsNullOrWhiteSpace(url)) {
                warnings.Add($".gitmodules: submodule \"{currentName}\" path \"{rawPath}\" is missing key: url");
            }

            if (!Directory.Exists(subAbsFull)) {
                warnings.Add($".gitmodules: submodule \"{currentName}\" path \"{rawPath}\" not found on disk (not initialized or incorrect)");
                return;
            }

            var dotGit = Path.Combine(subAbsFull, ".git");
            if (!File.Exists(dotGit) && !Directory.Exists(dotGit)) {
                warnings.Add($".gitmodules: submodule \"{currentName}\" path \"{rawPath}\" exists but is not a git repo (missing .git)");
            }
        }

        for (int i = 0; i < lines.Length; i++) {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.Length == 0) continue;
            if (trimmed.StartsWith("#") || trimmed.StartsWith(";")) continue;

            var sec = GitmodulesSectionRegex.Match(line);
            if (sec.Success) {
                FinalizeCurrent();
                currentName = sec.Groups["name"].Value.Trim();
                current.Clear();
                continue;
            }

            var kv = GitmodulesKeyValueRegex.Match(line);
            if (kv.Success) {
                if (currentName == null) {
                    warnings.Add($".gitmodules: key/value outside any [submodule] section at line {i + 1}: {trimmed}");
                    continue;
                }

                var key = kv.Groups["key"].Value.Trim();
                var value = TrimGitmodulesValue(kv.Groups["value"].Value);
                current[key] = value;
                continue;
            }

            warnings.Add($".gitmodules: syntax error at line {i + 1}: {trimmed}");
        }

        FinalizeCurrent();

        if (!parsedAnySection) {
            warnings.Add(".gitmodules: file present but no [submodule \"...\"] sections could be parsed");
        }

        return warnings;
    }

    private static void EmitWarningsToStderr(IEnumerable<string> warnings) {
        foreach (var w in warnings) {
            Console.Error.WriteLine($"[WARN] {w}");
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
            if (IsRepoDirtySafe(repo, _repoPath)) {
                throw new Exception("Unsaved changes detected. You must 'Save' before moving or undoing.");
            }
        }
    }

    private static bool IsPathTooLongError(Exception ex) {
        if (ex is PathTooLongException) return true;
        var msg = ex.Message ?? string.Empty;
        return msg.Contains("path too long", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("PathTooLong", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRepoDirtySafe(Repository repo, string repoPath) {
        try {
            // Avoid untracked recursion which is where long-path failures most often occur.
            var opts = new StatusOptions {
                IncludeUntracked = false,
                RecurseUntrackedDirs = false
            };
            return repo.RetrieveStatus(opts).IsDirty;
        } catch (Exception ex) {
            if (!IsPathTooLongError(ex)) throw;
            // Fallback to git CLI, which can handle long paths if Git is configured appropriately.
            var (exitCode, stdout, _) = RunGit(repoPath, "status --porcelain");
            if (exitCode != 0) return true;
            return !string.IsNullOrWhiteSpace(stdout);
        }
    }

    private static List<(string path, string status)> GetChangesSafe(Repository repo, string repoPath, bool includeUntracked) {
        try {
            var opts = new StatusOptions {
                IncludeUntracked = includeUntracked,
                RecurseUntrackedDirs = includeUntracked
            };

            return repo.RetrieveStatus(opts)
                .Where(s => s.State != FileStatus.Ignored)
                .Select(s => (path: s.FilePath, status: s.State.ToString()))
                .ToList();
        } catch (Exception ex) {
            if (!IsPathTooLongError(ex)) throw;

            // Retry without untracked files (most common cause of long-path issues)
            try {
                var opts = new StatusOptions {
                    IncludeUntracked = false,
                    RecurseUntrackedDirs = false
                };

                return repo.RetrieveStatus(opts)
                    .Where(s => s.State != FileStatus.Ignored)
                    .Select(s => (path: s.FilePath, status: s.State.ToString()))
                    .ToList();
            } catch {
                // Final fallback: git status porcelain
                return GetChangesFromGit(repoPath);
            }
        }
    }

    private static List<(string path, string status)> GetChangesFromGit(string repoPath) {
        var (exitCode, stdout, stderr) = RunGit(repoPath, "status --porcelain");
        if (exitCode != 0) {
            // If git itself errors, surface it as a pseudo change so the UI can still render something.
            return new List<(string path, string status)> { (path: "(git)", status: $"Error: {stderr}".Trim()) };
        }

        var results = new List<(string path, string status)>();
        foreach (var rawLine in stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)) {
            var line = rawLine;
            if (line.Length < 3) continue;

            var code = line.Substring(0, 2);
            var filePart = line.Length > 3 ? line.Substring(3) : string.Empty;
            if (string.IsNullOrWhiteSpace(filePart)) continue;

            // Handle renames like: R  old -> new
            var arrowIdx = filePart.LastIndexOf("->", StringComparison.Ordinal);
            var filePath = arrowIdx >= 0 ? filePart.Substring(arrowIdx + 2).Trim() : filePart.Trim();

            string status;
            if (code == "??") status = "New";
            else if (code.Contains('D')) status = "Deleted";
            else if (code.Contains('A')) status = "New";
            else if (code.Contains('M')) status = "Modified";
            else if (code.Contains('R')) status = "Renamed";
            else status = "Changed";

            results.Add((filePath, status));
        }

        return results;
    }

    private static void RunGitOrThrow(string repoPath, string args) {
        var (exitCode, _, stderr) = RunGit(repoPath, args);
        if (exitCode != 0) {
            throw new Exception(string.IsNullOrWhiteSpace(stderr) ? "git command failed." : stderr.Trim());
        }
    }

    private static (int exitCode, string stdout, string stderr) RunGit(string repoPath, string args) {
        var psi = new ProcessStartInfo("git", args) {
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return (1, string.Empty, "Failed to start git process.");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    private string ExtractVersion(string msg) {
        if (msg.StartsWith("[") && msg.Contains("]")) {
            return msg.Substring(1, msg.IndexOf("]") - 1);
        }
        return "v?"; // no version exists, often means commit made outside BetterGit
    }

    private string ExtractMessage(string msg) {
        if (msg.StartsWith("[") && msg.Contains("]")) {
            return msg.Substring(msg.IndexOf("]") + 1).Trim();
        }
        return msg;
    }

    // --- COMMAND: SCAN-REPOS ---
    private class RepoItem {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public List<RepoItem> Children { get; set; } = new List<RepoItem>();
    }

    public string ScanRepositories(bool includeNested = true) {
        var rootItem = GetRepoTree(_repoPath, _repoPath, includeNested);
        return JsonConvert.SerializeObject(rootItem, Formatting.Indented);
    }

    // Returns ONLY non-submodule nested repos (lazy-load for UI).
    public string ScanNestedRepositories() {
        var nested = GetNestedReposExcludingSubmodules(_repoPath);
        return JsonConvert.SerializeObject(nested, Formatting.Indented);
    }

    private RepoItem GetRepoTree(string currentPath, string rootPath, bool includeNested) {
        string relPath = Path.GetRelativePath(rootPath, currentPath);
        if (relPath == ".") relPath = "";

        var item = new RepoItem {
            Name = Path.GetFileName(currentPath),
            Path = relPath,
            Type = (currentPath == rootPath) ? "root" : "nested"
        };

        var knownPaths = new HashSet<string>();

        // 1. Submodules (Only if it is a repo)
        bool isValidRepo = false;
        try {
            isValidRepo = Repository.IsValid(currentPath);
        } catch { 
            // Ignore permission/ownership errors during validity check
            isValidRepo = false; 
        }

        if (isValidRepo) {
            try {
                using (var repo = new Repository(currentPath)) {
                    foreach (var sm in repo.Submodules) {
                        string fullPath = Path.Combine(currentPath, sm.Path);
                        if (Directory.Exists(fullPath)) {
                            var smItem = GetRepoTree(fullPath, rootPath, includeNested);
                            smItem.Type = "submodule";
                            smItem.Name = sm.Name;
                            item.Children.Add(smItem);
                            
                            knownPaths.Add(Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant());
                        }
                    }
                }
            } catch {}
        }

        // 2. Nested Repos
        if (includeNested) {
            var nestedPaths = new List<string>();
            ScanForNestedRepos(currentPath, nestedPaths, knownPaths, 0);

            foreach (var nestedPath in nestedPaths) {
                var nestedItem = GetRepoTree(nestedPath, rootPath, includeNested);
                nestedItem.Type = "nested";
                item.Children.Add(nestedItem);
            }
        }

        return item;
    }

    private void ScanForNestedRepos(string dir, List<string> results, HashSet<string> knownPaths, int depth) {
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

                    results.Add(d);
                    // Don't recurse into a repo
                    continue;
                }

                // Recurse
                ScanForNestedRepos(d, results, knownPaths, depth + 1);
            }
        } catch { /* Access denied etc */ }
    }

    private List<RepoItem> GetNestedReposExcludingSubmodules(string rootPath) {
        var submoduleRoots = CollectSubmoduleRootsRecursive(rootPath);
        var results = new List<RepoItem>();

        void Recurse(string dir, int depth) {
            if (depth > 50) return;

            try {
                foreach (var d in Directory.GetDirectories(dir)) {
                    var name = Path.GetFileName(d);

                    // Skip common junk
                    if (name.StartsWith(".") && name != ".git") continue;
                    if (name == "node_modules" || name == "bin" || name == "obj" || name == "packages") continue;

                    var full = Path.GetFullPath(d).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    // Don't scan inside submodules
                    if (submoduleRoots.Any(sm => IsSameOrChildPath(full, sm))) {
                        continue;
                    }

                    // Check if this is a git repo
                    string gitPath = Path.Combine(d, ".git");
                    if (Directory.Exists(gitPath) || File.Exists(gitPath)) {
                        // Exclude the root repo itself
                        if (!string.Equals(full, Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) {
                            var rel = Path.GetRelativePath(rootPath, d);
                            if (rel == ".") rel = "";
                            results.Add(new RepoItem {
                                Name = Path.GetFileName(d),
                                Path = rel,
                                Type = "nested",
                                Children = new List<RepoItem>()
                            });
                        }
                        // Don't recurse into a repo
                        continue;
                    }

                    Recurse(d, depth + 1);
                }
            } catch {
                // Access denied etc
            }
        }

        Recurse(rootPath, 0);
        return results;
    }

    private static bool IsSameOrChildPath(string candidate, string root) {
        if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)) return true;
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
        // ensure boundary
        if (candidate.Length == root.Length) return true;
        char c = candidate[root.Length];
        return c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
    }

    private HashSet<string> CollectSubmoduleRootsRecursive(string rootPath) {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Collect(string repoPath) {
            bool isValid;
            try { isValid = Repository.IsValid(repoPath); } catch { isValid = false; }
            if (!isValid) return;

            try {
                using var repo = new Repository(repoPath);
                foreach (var sm in repo.Submodules) {
                    string fullPath = Path.GetFullPath(Path.Combine(repoPath, sm.Path))
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (Directory.Exists(fullPath) && roots.Add(fullPath)) {
                        Collect(fullPath);
                    }
                }
            } catch {
                // ignore
            }
        }

        Collect(rootPath);
        return roots;
    }
}
