namespace BetterGit;

public interface IProjectInitService {
    /* :: :: Contract :: START :: */

    void InitProject(string path, bool isNode = false);

    /* :: :: Contract :: END :: */
}
