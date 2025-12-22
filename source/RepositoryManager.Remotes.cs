using LibGit2Sharp;

using Newtonsoft.Json;

namespace BetterGit;

public partial class RepositoryManager {
    /* :: :: Remote Commands :: START :: */

    /// <summary>
    /// Returns a merged view of Git remotes + BetterGit metadata as JSON.
    /// </summary>
    public string GetRemotesJson() {
        List<RemoteInfo> remotes = ListRemotes();
        return JsonConvert.SerializeObject(remotes.Select(r => new {
            name = r.Name,
            fetchUrl = r.FetchUrl,
            pushUrl = r.PushUrl,
            group = r.Group,
            provider = r.Provider,
            isPublic = r.IsPublic,
            hasGitRemote = r.HasGitRemote,
            hasMetadata = r.HasMetadata,
            isMisconfigured = r.IsMisconfigured
        }), Formatting.Indented);
    }

    /// <summary>
    /// Lists remotes merged from Git config + BetterGit metadata.
    /// </summary>
    public List<RemoteInfo> ListRemotes() {
        if (!IsValidGitRepo()) {
            throw new Exception("Not a valid BetterGit repository. Run 'init' first.");
        }

        using (Repository repo = new Repository(_repoPath)) {
            return _remoteService.ListMergedRemotes(repo);
        }
    }

    /// <summary>
    /// Sets BetterGit metadata for a remote in <c>.betterGit/project.toml</c>.
    /// </summary>
    public void SetRemoteMetadata(string name, string? group, string? provider, bool? isPublic) {
        if (!IsValidGitRepo()) {
            throw new Exception("Not a valid BetterGit repository. Run 'init' first.");
        }
        _remoteService.SetRemoteMetadata(name, group, provider, isPublic);
    }

    /* :: :: Remote Commands :: END :: */
}

