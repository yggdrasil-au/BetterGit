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

    public string IncrementVersion(VersionChangeType changeType = VersionChangeType.Patch) {
        // This creates/updates a '.betterGit/meta.toml' file
        string betterGitDir = Path.Combine(_repoPath, ".betterGit");
        if (!Directory.Exists(betterGitDir)) {
            Directory.CreateDirectory(betterGitDir);
        }

        string metaFile = Path.Combine(betterGitDir, "meta.toml");
        long major = 0, minor = 0, patch = 0;
        bool isAlpha = false;
        bool isBeta = false;
        bool metaExists = File.Exists(metaFile);

        // 1. Read TOML
        if (metaExists) {
            try {
                string content = File.ReadAllText(metaFile);
                TomlTable model = Toml.ToModel(content);
                
                // Migration from old single 'version' field
                if (model.ContainsKey("version") && !model.ContainsKey("patch")) {
                    patch = (long)model["version"];
                } else {
                    if (model.ContainsKey("major")) major = (long)model["major"];
                    if (model.ContainsKey("minor")) minor = (long)model["minor"];
                    if (model.ContainsKey("patch")) patch = (long)model["patch"];
                    if (model.ContainsKey("isAlpha")) isAlpha = (bool)model["isAlpha"];
                    if (model.ContainsKey("isBeta")) isBeta = (bool)model["isBeta"];
                }
            } catch { /* Ignore corrupt, start from 0.0.0 */ }
        } else {
            // If meta doesn't exist, try to seed from package.json
            string packageJsonPath = Path.Combine(_repoPath, "package.json");
            if (File.Exists(packageJsonPath)) {
                try {
                    string content = File.ReadAllText(packageJsonPath);
                    JObject? json = JsonConvert.DeserializeObject<JObject>(content);
                    JToken? versionToken = json?["version"];
                    if (versionToken != null) {
                        string v = versionToken.ToString();
                        // Handle suffixes
                        if (v.EndsWith("-A")) {
                            isAlpha = true;
                            v = v.Substring(0, v.Length - 2);
                        } else if (v.EndsWith("-B")) {
                            isBeta = true;
                            v = v.Substring(0, v.Length - 2);
                        }
                        
                        string[] parts = v.Split('.');
                        if (parts.Length >= 1) long.TryParse(parts[0], out major);
                        if (parts.Length >= 2) long.TryParse(parts[1], out minor);
                        if (parts.Length >= 3) long.TryParse(parts[2], out patch);
                    }
                } catch { /* Ignore */ }
            }
        }

        // 2. Increment Logic
        if (changeType == VersionChangeType.Major) {
            major++;
            minor = 0;
            patch = 0;
        } else if (changeType == VersionChangeType.Minor) {
            minor++;
            patch = 0;
        } else {
            // Patch (Default)
            patch++;
        }

        // 3. Write TOML
        TomlTable toml = new TomlTable {
            ["major"] = major,
            ["minor"] = minor,
            ["patch"] = patch,
            ["isAlpha"] = isAlpha,
            ["isBeta"] = isBeta
        };
        File.WriteAllText(metaFile, Toml.FromModel(toml));

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
        string betterGitDir = Path.Combine(_repoPath, ".betterGit");
        if (!Directory.Exists(betterGitDir)) {
            Directory.CreateDirectory(betterGitDir);
        }

        string metaFile = Path.Combine(betterGitDir, "meta.toml");
        TomlTable toml = new TomlTable();

        // Read existing to preserve version numbers
        if (File.Exists(metaFile)) {
            try {
                string content = File.ReadAllText(metaFile);
                TomlTable model = Toml.ToModel(content);
                foreach (KeyValuePair<string, object> kvp in model) {
                    toml[kvp.Key] = kvp.Value;
                }
            } catch { }
        }

        // Update flags
        toml["isAlpha"] = false;
        toml["isBeta"] = false;

        if (channel.ToLower() == "alpha") {
            toml["isAlpha"] = true;
        } else if (channel.ToLower() == "beta") {
            toml["isBeta"] = true;
        }

        File.WriteAllText(metaFile, Toml.FromModel(toml));
        Console.WriteLine($"Channel set to: {channel}");
    }

    /* :: :: Methods :: END :: */
}

