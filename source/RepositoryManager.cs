using LibGit2Sharp;
using Newtonsoft.Json;

namespace BetterGit;

public partial class RepositoryManager {
    /* :: :: Fields :: START :: */

    private readonly string _repoPath;
    private readonly IVersionService _versionService;

    /* :: :: Fields :: END :: */
    // //
    /* :: :: Constructors :: START :: */

    public RepositoryManager(string path) {
        _repoPath = path;
        _versionService = new VersionService(path);
    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Public API :: START :: */

    public bool IsValidGitRepo() {
        return Repository.IsValid(_repoPath);
    }

    public static void InitProject(string path, bool isNode = false) {
        ProjectInitService.InitProject(path, isNode);
    }

    public void SetChannel(string channel) {
        _versionService.SetChannel(channel);
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
