using LibGit2Sharp;

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

    /* :: :: Public API :: END :: */
}
