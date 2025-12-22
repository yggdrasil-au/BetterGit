namespace BetterGit;

/// <summary>
/// A merged view of a Git remote (from <c>.git/config</c>) and BetterGit remote metadata (from <c>.betterGit/project.toml</c>).
/// </summary>
public sealed class RemoteInfo {
    /// <summary>
    /// The remote name (e.g. <c>origin</c>).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The fetch URL from Git config, if present.
    /// </summary>
    public string? FetchUrl { get; init; }

    /// <summary>
    /// The push URL from Git config, if present.
    /// </summary>
    public string? PushUrl { get; init; }

    /// <summary>
    /// The BetterGit group label (defaults to <c>Ungrouped</c>).
    /// </summary>
    public string Group { get; init; } = "Ungrouped";

    /// <summary>
    /// The remote provider (e.g. <c>github</c>, <c>gitlab</c>, <c>bitbucket</c>, <c>other</c>).
    /// </summary>
    public string Provider { get; init; } = "other";

    /// <summary>
    /// Whether BetterGit considers this remote public.
    /// </summary>
    public bool IsPublic { get; init; }

    /// <summary>
    /// True when the remote exists in Git config.
    /// </summary>
    public bool HasGitRemote { get; init; }

    /// <summary>
    /// True when BetterGit metadata exists for the remote in <c>project.toml</c>.
    /// </summary>
    public bool HasMetadata { get; init; }

    /// <summary>
    /// True when BetterGit metadata exists but the remote is missing from Git config.
    /// </summary>
    public bool IsMisconfigured { get; init; }
}

