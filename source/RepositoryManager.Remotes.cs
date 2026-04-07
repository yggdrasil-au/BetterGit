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
            branch = r.Branch,
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
    public void SetRemoteMetadata(string name, string? group, string? provider, bool? isPublic, string? branch = null) {
        if (!IsValidGitRepo()) {
            throw new Exception("Not a valid BetterGit repository. Run 'init' first.");
        }
        _remoteService.SetRemoteMetadata(name, group, provider, isPublic, branch);
    }

    /// <summary>
    /// Adds a Git remote and stores the corresponding BetterGit metadata.
    /// </summary>
    public void AddRemote(string name, string url, string? group, string? provider, bool? isPublic, string? branch = null) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Remote name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(url)) {
            throw new ArgumentException("Remote URL is required.", nameof(url));
        }

        if (!IsValidGitRepo()) {
            throw new Exception("Not a valid BetterGit repository. Run 'init' first.");
        }

        using (Repository repo = new Repository(_repoPath)) {
            if (repo.Network.Remotes[name] != null) {
                throw new Exception($"Remote '{name}' already exists.");
            }

            repo.Network.Remotes.Add(name, url);
        }

        string? providerValue = provider;
        if (string.IsNullOrWhiteSpace(providerValue)) {
            providerValue = GuessProviderFromUrl(url);
        }

        _remoteService.SetRemoteMetadata(name, group, providerValue, isPublic, branch);
    }

    private static string GuessProviderFromUrl(string url) {
        string lower = url.ToLowerInvariant();
        if (lower.Contains("github.com")) {
            return "github";
        }
        if (lower.Contains("gitlab.com")) {
            return "gitlab";
        }
        if (lower.Contains("bitbucket.org")) {
            return "bitbucket";
        }
        if (lower.Contains("azure.com") || lower.Contains("visualstudio.com") || lower.Contains("dev.azure.com")) {
            return "azure";
        }

        return "other";
    }

    /* :: :: Remote Commands :: END :: */
}

