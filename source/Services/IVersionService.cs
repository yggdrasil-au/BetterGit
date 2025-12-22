namespace BetterGit;

public interface IVersionService {
    /* :: :: Contract :: START :: */

    string IncrementVersion(VersionChangeType changeType = VersionChangeType.Patch, string? manualVersion = null);
    (long Major, long Minor, long Patch, bool IsAlpha, bool IsBeta) GetCurrentVersion();
    void SetChannel(string channel);

    /* :: :: Contract :: END :: */
}
