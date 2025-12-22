using System.Text.RegularExpressions;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Tomlyn;
using Tomlyn.Model;

namespace BetterGit;

public class VersionService : IVersionService {
    private readonly string _repoPath;

    /* :: :: Constructors :: START :: */

    public VersionService(string repoPath) {
        _repoPath = repoPath;
    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Methods :: START :: */

    public (long Major, long Minor, long Patch, bool IsAlpha, bool IsBeta) GetCurrentVersion() {
        (long Major, long Minor, long Patch, bool IsAlpha, bool IsBeta, bool IsNodeProject) state = ReadCurrentVersionState();
        return (state.Major, state.Minor, state.Patch, state.IsAlpha, state.IsBeta);
    }

    private (long Major, long Minor, long Patch, bool IsAlpha, bool IsBeta, bool IsNodeProject) ReadCurrentVersionState() {
        BetterGitConfigPaths.MigrateMetaTomlToProjectTomlIfNeeded(_repoPath);

        string projectFile = BetterGitConfigPaths.GetProjectTomlPath(_repoPath);
        string legacyMetaFile = BetterGitConfigPaths.GetLegacyMetaTomlPath(_repoPath);
        long major = 0, minor = 0, patch = 0;
        bool isAlpha = false;
        bool isBeta = false;
        bool isNodeProject = false;
        bool projectExists = File.Exists(projectFile);
        bool legacyExists = File.Exists(legacyMetaFile);

        // 1. Read TOML
        string? fileToRead = projectExists ? projectFile : (legacyExists ? legacyMetaFile : null);
        if (fileToRead != null) {
            try {
                string content = File.ReadAllText(fileToRead);
                TomlTable model = Toml.ToModel(content);
                
                if (model.ContainsKey("version") && !model.ContainsKey("patch")) {
                    patch = (long)model["version"];
                } else {
                    if (model.ContainsKey("major")) major = (long)model["major"];
                    if (model.ContainsKey("minor")) minor = (long)model["minor"];
                    if (model.ContainsKey("patch")) patch = (long)model["patch"];
                    if (model.ContainsKey("isAlpha")) isAlpha = (bool)model["isAlpha"];
                    if (model.ContainsKey("isBeta")) isBeta = (bool)model["isBeta"];
                    if (model.ContainsKey("isNodeProject")) isNodeProject = (bool)model["isNodeProject"];
                }
            } catch { /* Ignore corrupt, start from 0.0.0 */ }
        }

        // 1b. Sync with package.json if needed
        string packageJsonPath = Path.Combine(_repoPath, "package.json");
        if (isNodeProject || ((!projectExists && !legacyExists) && File.Exists(packageJsonPath))) {
             if (File.Exists(packageJsonPath)) {
                try {
                    string content = File.ReadAllText(packageJsonPath);
                    JObject? json = JsonConvert.DeserializeObject<JObject>(content);
                    JToken? versionToken = json?["version"];
                    if (versionToken != null) {
                        string v = versionToken.ToString();
                        long pMajor = 0, pMinor = 0, pPatch = 0;
                        bool pAlpha = false, pBeta = false;

                        // Handle suffixes
                        if (v.EndsWith("-A")) {
                            pAlpha = true;
                            v = v.Substring(0, v.Length - 2);
                        } else if (v.EndsWith("-B")) {
                            pBeta = true;
                            v = v.Substring(0, v.Length - 2);
                        }
                        
                        string[] parts = v.Split('.');
                        if (parts.Length >= 1) long.TryParse(parts[0], out pMajor);
                        if (parts.Length >= 2) long.TryParse(parts[1], out pMinor);
                        if (parts.Length >= 3) long.TryParse(parts[2], out pPatch);

                        // Compare and take highest
                        bool pkgIsHigher = false;
                        if (pMajor > major) pkgIsHigher = true;
                        else if (pMajor == major) {
                            if (pMinor > minor) pkgIsHigher = true;
                            else if (pMinor == minor) {
                                if (pPatch > patch) pkgIsHigher = true;
                            }
                        }

                        if (pkgIsHigher) {
                            major = pMajor;
                            minor = pMinor;
                            patch = pPatch;
                            isAlpha = pAlpha;
                            isBeta = pBeta;
                        }
                    }
                } catch { /* Ignore */ }
            }
        }
        return (major, minor, patch, isAlpha, isBeta, isNodeProject);
    }

    public string IncrementVersion(VersionChangeType changeType = VersionChangeType.Patch, string? manualVersion = null) {
        // This creates/updates a '.betterGit/project.toml' file (public/committed)
        BetterGitConfigPaths.MigrateMetaTomlToProjectTomlIfNeeded(_repoPath);
        BetterGitConfigPaths.EnsureBetterGitDirExists(_repoPath);

        (long Major, long Minor, long Patch, bool IsAlpha, bool IsBeta, bool IsNodeProject) state = ReadCurrentVersionState();
        long major = state.Major;
        long minor = state.Minor;
        long patch = state.Patch;
        bool isAlpha = state.IsAlpha;
        bool isBeta = state.IsBeta;
        bool isNodeProject = state.IsNodeProject;

        string projectFile = BetterGitConfigPaths.GetProjectTomlPath(_repoPath);
        TomlTable toml = ReadProjectTomlModel(projectFile);

        // 2. Increment Logic
        if (changeType == VersionChangeType.Manual && !string.IsNullOrWhiteSpace(manualVersion)) {
             string v = manualVersion;
             // Reset flags
             isAlpha = false;
             isBeta = false;

             if (v.EndsWith("-A")) {
                isAlpha = true;
                v = v.Substring(0, v.Length - 2);
             } else if (v.EndsWith("-B")) {
                isBeta = true;
                v = v.Substring(0, v.Length - 2);
             }

             string[] parts = v.Split('.');
             if (parts.Length >= 1) long.TryParse(parts[0], out major); else major = 0;
             if (parts.Length >= 2) long.TryParse(parts[1], out minor); else minor = 0;
             if (parts.Length >= 3) long.TryParse(parts[2], out patch); else patch = 0;

        } else if (changeType == VersionChangeType.Major) {
            major++;
            minor = 0;
            patch = 0;
        } else if (changeType == VersionChangeType.Minor) {
            minor++;
            patch = 0;
        } else if (changeType == VersionChangeType.Patch) {
            patch++;
        }
        // If None, do nothing

        // 3. Write TOML
        toml["major"] = major;
        toml["minor"] = minor;
        toml["patch"] = patch;
        toml["isAlpha"] = isAlpha;
        toml["isBeta"] = isBeta;
        toml["isNodeProject"] = isNodeProject;
        File.WriteAllText(projectFile, Toml.FromModel(toml));

        string versionString = $"{major}.{minor}.{patch}";
        if (isAlpha) versionString += "-A";
        else if (isBeta) versionString += "-B";

        // 4. Update package.json if exists (Preserving Formatting)
        string pkgPath = Path.Combine(_repoPath, "package.json");
        if (File.Exists(pkgPath)) {
            try {
                string content = File.ReadAllText(pkgPath);
                // Regex to find "version": "..." and replace it, preserving whitespace
                string pattern = "(\"version\"\\s*:\\s*\")(.*?)(\")";
                Regex regex = new Regex(pattern);
                
                // Only replace the first occurrence
                string newContent = regex.Replace(content, $"${{1}}{versionString}$3", 1);
                
                File.WriteAllText(pkgPath, newContent);
            } catch { /* Ignore */ }
        }

        return $"v{versionString}";
    }

    public void SetChannel(string channel) {
        BetterGitConfigPaths.MigrateMetaTomlToProjectTomlIfNeeded(_repoPath);
        BetterGitConfigPaths.EnsureBetterGitDirExists(_repoPath);

        string projectFile = BetterGitConfigPaths.GetProjectTomlPath(_repoPath);
        TomlTable toml = ReadProjectTomlModel(projectFile);

        // Update flags
        toml["isAlpha"] = false;
        toml["isBeta"] = false;

        if (channel.ToLower() == "alpha") {
            toml["isAlpha"] = true;
        } else if (channel.ToLower() == "beta") {
            toml["isBeta"] = true;
        }

        File.WriteAllText(projectFile, Toml.FromModel(toml));
        Console.WriteLine($"Channel set to: {channel}");
    }

    /* :: :: Methods :: END :: */

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
