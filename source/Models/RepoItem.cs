namespace BetterGit;

public class RepoItem {
    /* :: :: Properties :: START :: */

    public string Name {
        get; set;
    } = string.Empty;

    public string Path {
        get; set;
    } = string.Empty;

    public string Type {
        get; set;
    } = string.Empty;

    public List<RepoItem> Children {
        get; set;
    } = new List<RepoItem>();


    /* :: :: Properties :: END :: */
}
