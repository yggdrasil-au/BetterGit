using LibGit2Sharp;
using Newtonsoft.Json;

namespace BetterGit;

public partial class RepositoryManager {
    /* :: :: Fields :: START :: */

    private readonly string _repoPath;
    private readonly IVersionService _versionService;
    private readonly RemoteService _remoteService;

    /* :: :: Fields :: END :: */
    // //
    /* :: :: Constructors :: START :: */

    public RepositoryManager(string path) {
        _repoPath = path;
        _versionService = new VersionService(path);
        _remoteService = new RemoteService(path);
    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Public API :: START :: */

    public bool IsValidGitRepo() {
        return Repository.IsValid(_repoPath);
    }

    public static void InitProject(string path, bool isNode = false) {
        ProjectInitService.InitProject(path, new WebProjectOptions { IsNodeProject = isNode });
    }

    public static void InitProject(string path, WebProjectOptions webProject) {
        ProjectInitService.InitProject(path, webProject);
    }

    public static void AddSafeDirectory(string path) {
        RunGitOrThrow(Path.GetDirectoryName(path) ?? path, $"config --global --add safe.directory \"{path.Replace("\\", "/")}\"");
    }

    public void SetChannel(string channel) {
        _versionService.SetChannel(channel);
    }

    public string GetDiffSummary() {
        if (!IsValidGitRepo()) return "Not a git repository.";

        try {
            using (Repository repo = new Repository(_repoPath)) {
                // Compare HEAD tree to working directory to get the actual patch/diff.
                // Some working-tree entries (for example type changes) are handled better by the git CLI,
                // so we only keep the LibGit2Sharp path when it succeeds cleanly.
                Patch patch = repo.Diff.Compare<Patch>(repo.Head.Tip.Tree, DiffTargets.WorkingDirectory);

                if (patch == null || !patch.Any()) {
                    return "No changes detected.";
                }

                string diffContent = patch.Content;

                // Protect the AI's context window from massive diffs
                if (diffContent.Length > 20000) {
                    diffContent = diffContent.Substring(0, 20000) + "\n... [Diff truncated due to size]";
                }

                return diffContent;
            }
        } catch (Exception ex) {
            if (!ShouldFallbackToGitCli(ex)) {
                throw;
            }

            (int exitCode, string stdout, string stderr) = RunGit(_repoPath, "diff --no-ext-diff --no-renames HEAD");
            if (exitCode != 0) {
                return string.IsNullOrWhiteSpace(stderr) ? "No changes detected." : stderr.Trim();
            }

            string diffContent = stdout.Trim();
            if (string.IsNullOrWhiteSpace(diffContent)) {
                return "No changes detected.";
            }

            if (diffContent.Length > 20000) {
                diffContent = diffContent.Substring(0, 20000) + "\n... [Diff truncated due to size]";
            }

            return diffContent;
        }
    }

    public string GetVersionInfo() {
        var v = _versionService.GetCurrentVersion();
        string current = $"{v.Major}.{v.Minor}.{v.Patch}";
        if (v.IsAlpha) current += "-A";
        else if (v.IsBeta) current += "-B";

        string last = "None";
        if (IsValidGitRepo()) {
            using (Repository repo = new Repository(_repoPath)) {
                var tip = repo.Head.Tip;
                if (tip != null) {
                    last = ExtractVersion(tip.Message);
                    if (string.IsNullOrEmpty(last) || last == "v?") last = "None";
                }
            }
        }

        return JsonConvert.SerializeObject(new { currentVersion = current, lastCommitVersion = last });
    }

    /* :: :: Public API :: END :: */
}
