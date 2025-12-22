using LibGit2Sharp;

using Tomlyn;
using Tomlyn.Model;

namespace BetterGit;

/// <summary>
/// Reads and writes BetterGit remote metadata in <c>.betterGit/project.toml</c>, and merges it with Git remotes from <c>.git/config</c>.
/// </summary>
public sealed class RemoteService {
    private readonly string _repoPath;

    /// <summary>
    /// Creates a new remote service bound to a repository path.
    /// </summary>
    public RemoteService(string repoPath) {
        _repoPath = repoPath;
    }

    /// <summary>
    /// Returns a merged view of Git remotes and BetterGit remote metadata.
    /// </summary>
    public List<RemoteInfo> ListMergedRemotes() {
        using (Repository repo = new Repository(_repoPath)) {
            return ListMergedRemotes(repo);
        }
    }

    /// <summary>
    /// Returns a merged view of Git remotes and BetterGit remote metadata using an existing repository instance.
    /// </summary>
    public List<RemoteInfo> ListMergedRemotes(Repository repo) {
        BetterGitConfigPaths.MigrateMetaTomlToProjectTomlIfNeeded(_repoPath);

        Dictionary<string, RemoteMetadata> metaByName = ReadRemoteMetadataByName();
        Dictionary<string, Remote> gitRemotesByName = new Dictionary<string, Remote>(StringComparer.Ordinal);

        foreach (Remote r in repo.Network.Remotes) {
            gitRemotesByName[r.Name] = r;
        }

        HashSet<string> allNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in metaByName.Keys) {
            allNames.Add(name);
        }
        foreach (string name in gitRemotesByName.Keys) {
            allNames.Add(name);
        }

        List<RemoteInfo> merged = new List<RemoteInfo>();
        foreach (string name in allNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)) {
            Remote? gitRemote;
            gitRemotesByName.TryGetValue(name, out gitRemote);

            RemoteMetadata? meta;
            metaByName.TryGetValue(name, out meta);

            string? fetchUrl = gitRemote?.Url;
            string? pushUrl = gitRemote?.PushUrl;

            string? provider = meta?.Provider;
            if (string.IsNullOrWhiteSpace(provider)) {
                provider = GuessProvider(fetchUrl, pushUrl);
            }
            string providerValue = provider ?? "other";

            string? group = meta?.Group;
            if (string.IsNullOrWhiteSpace(group)) {
                group = "Ungrouped";
            }
            string groupValue = group ?? "Ungrouped";

            bool isPublic = meta?.IsPublic ?? false;
            bool hasGitRemote = gitRemote != null;
            bool hasMetadata = meta != null;

            merged.Add(new RemoteInfo {
                Name = name,
                FetchUrl = fetchUrl,
                PushUrl = pushUrl,
                Provider = providerValue,
                Group = groupValue,
                IsPublic = isPublic,
                HasGitRemote = hasGitRemote,
                HasMetadata = hasMetadata,
                IsMisconfigured = hasMetadata && !hasGitRemote
            });
        }

        return merged;
    }

    /// <summary>
    /// Sets BetterGit metadata for a remote in <c>.betterGit/project.toml</c>. Creates the remote metadata entry if missing.
    /// </summary>
    public void SetRemoteMetadata(string name, string? group, string? provider, bool? isPublic) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Remote name is required.", nameof(name));
        }

        BetterGitConfigPaths.MigrateMetaTomlToProjectTomlIfNeeded(_repoPath);
        BetterGitConfigPaths.EnsureBetterGitDirExists(_repoPath);

        string projectFile = BetterGitConfigPaths.GetProjectTomlPath(_repoPath);
        TomlTable model = ReadProjectTomlModel(projectFile);

        TomlTableArray remotes = GetOrCreateRemoteArray(model);
        TomlTable entry = GetOrCreateRemoteEntry(remotes, name);

        entry["name"] = name;
        if (group != null) {
            entry["group"] = group;
        }
        if (provider != null) {
            entry["provider"] = provider;
        }
        if (isPublic.HasValue) {
            entry["isPublic"] = isPublic.Value;
        }

        File.WriteAllText(projectFile, Toml.FromModel(model));
    }

    private sealed class RemoteMetadata {
        public string Name { get; init; } = string.Empty;
        public string Group { get; init; } = string.Empty;
        public string Provider { get; init; } = string.Empty;
        public bool? IsPublic { get; init; }
    }

    private Dictionary<string, RemoteMetadata> ReadRemoteMetadataByName() {
        string projectFile = BetterGitConfigPaths.GetProjectTomlPath(_repoPath);
        string legacyFile = BetterGitConfigPaths.GetLegacyMetaTomlPath(_repoPath);

        TomlTable model = new TomlTable();
        if (File.Exists(projectFile)) {
            model = ReadProjectTomlModel(projectFile);
        } else if (File.Exists(legacyFile)) {
            model = ReadProjectTomlModel(legacyFile);
        }

        Dictionary<string, RemoteMetadata> result = new Dictionary<string, RemoteMetadata>(StringComparer.Ordinal);

        if (!model.ContainsKey("remotes")) {
            return result;
        }

        TomlTableArray? arr = model["remotes"] as TomlTableArray;
        if (arr == null) {
            return result;
        }

        foreach (object item in arr) {
            TomlTable? t = item as TomlTable;
            if (t == null) {
                continue;
            }

            string name = ReadString(t, "name") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)) {
                continue;
            }

            result[name] = new RemoteMetadata {
                Name = name,
                Group = ReadString(t, "group") ?? string.Empty,
                Provider = ReadString(t, "provider") ?? string.Empty,
                IsPublic = ReadBool(t, "isPublic")
            };
        }

        return result;
    }

    private static TomlTableArray GetOrCreateRemoteArray(TomlTable model) {
        if (model.ContainsKey("remotes")) {
            TomlTableArray? existing = model["remotes"] as TomlTableArray;
            if (existing != null) {
                return existing;
            }
        }

        TomlTableArray created = new TomlTableArray();
        model["remotes"] = created;
        return created;
    }

    private static TomlTable GetOrCreateRemoteEntry(TomlTableArray remotes, string name) {
        foreach (object item in remotes) {
            TomlTable? t = item as TomlTable;
            if (t == null) {
                continue;
            }

            string? existingName = ReadString(t, "name");
            if (existingName != null && existingName.Equals(name, StringComparison.Ordinal)) {
                return t;
            }
        }

        TomlTable created = new TomlTable();
        remotes.Add(created);
        return created;
    }

    private static string? ReadString(TomlTable table, string key) {
        if (!table.ContainsKey(key)) {
            return null;
        }
        object? value = table[key];
        return value?.ToString();
    }

    private static bool? ReadBool(TomlTable table, string key) {
        if (!table.ContainsKey(key)) {
            return null;
        }
        object? value = table[key];
        if (value is bool b) {
            return b;
        }
        return null;
    }

    private static string GuessProvider(string? fetchUrl, string? pushUrl) {
        string? url = !string.IsNullOrWhiteSpace(pushUrl) ? pushUrl : fetchUrl;
        if (string.IsNullOrWhiteSpace(url)) {
            return "other";
        }

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

    private static TomlTable ReadProjectTomlModel(string projectTomlPath) {
        if (!File.Exists(projectTomlPath)) {
            return new TomlTable();
        }

        try {
            string content = File.ReadAllText(projectTomlPath);
            TomlTable model = Toml.ToModel(content);
            TomlTable copy = new TomlTable();
            foreach (KeyValuePair<string, object> kvp in model) {
                copy[kvp.Key] = kvp.Value;
            }
            return copy;
        } catch {
            return new TomlTable();
        }
    }
}
