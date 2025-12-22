namespace BetterGit;

public interface IVersionService {
    /* :: :: Contract :: START :: */

    string IncrementVersion(VersionChangeType changeType = VersionChangeType.Patch);
    void SetChannel(string channel);

    /* :: :: Contract :: END :: */
}
