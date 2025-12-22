namespace BetterGit;

public sealed class ProjectInitServiceAdapter : IProjectInitService {
    /* :: :: Methods :: START :: */

    public void InitProject(string path, bool isNode = false) {
        ProjectInitService.InitProject(path, isNode);
    }

    /* :: :: Methods :: END :: */
}
