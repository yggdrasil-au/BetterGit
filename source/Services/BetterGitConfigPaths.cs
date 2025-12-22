namespace BetterGit;

/// <summary>
/// Centralized file and directory paths used by BetterGit inside a repository.
/// </summary>
internal static class BetterGitConfigPaths {
    /// <summary>
    /// The BetterGit configuration directory name at the repo root.
    /// </summary>
    internal const string BetterGitDirectoryName = ".betterGit";

    /// <summary>
    /// Public (committed) project configuration file (replaces legacy <c>meta.toml</c>).
    /// </summary>
    internal const string ProjectTomlFileName = "project.toml";

    /// <summary>
    /// Local (ignored) user preferences file.
    /// </summary>
    internal const string LocalTomlFileName = "local.toml";

    /// <summary>
    /// Secrets (ignored) credentials file.
    /// </summary>
    internal const string SecretsTomlFileName = "secrets.toml";

    /// <summary>
    /// Legacy metadata file name kept for migration.
    /// </summary>
    internal const string LegacyMetaTomlFileName = "meta.toml";

    internal static string GetBetterGitDir(string repoPath) {
        return Path.Combine(repoPath, BetterGitDirectoryName);
    }

    internal static string GetProjectTomlPath(string repoPath) {
        return Path.Combine(GetBetterGitDir(repoPath), ProjectTomlFileName);
    }

    internal static string GetLocalTomlPath(string repoPath) {
        return Path.Combine(GetBetterGitDir(repoPath), LocalTomlFileName);
    }

    internal static string GetSecretsTomlPath(string repoPath) {
        return Path.Combine(GetBetterGitDir(repoPath), SecretsTomlFileName);
    }

    internal static string GetLegacyMetaTomlPath(string repoPath) {
        return Path.Combine(GetBetterGitDir(repoPath), LegacyMetaTomlFileName);
    }

    internal static void EnsureBetterGitDirExists(string repoPath) {
        string betterGitDir = GetBetterGitDir(repoPath);
        if (!Directory.Exists(betterGitDir)) {
            Directory.CreateDirectory(betterGitDir);
        }
    }

    /// <summary>
    /// Migrates legacy <c>.betterGit/meta.toml</c> to <c>.betterGit/project.toml</c> when needed.
    /// </summary>
    internal static void MigrateMetaTomlToProjectTomlIfNeeded(string repoPath) {
        EnsureBetterGitDirExists(repoPath);

        string legacyPath = GetLegacyMetaTomlPath(repoPath);
        string projectPath = GetProjectTomlPath(repoPath);

        if (!File.Exists(projectPath) && File.Exists(legacyPath)) {
            try {
                File.Move(sourceFileName: legacyPath, destFileName: projectPath);
            } catch {
                // If migration fails (locked file, permissions, etc.), callers should still work
                // by reading from whichever file exists.
            }
        }
    }
}

