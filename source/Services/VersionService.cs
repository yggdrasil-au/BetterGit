using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Tomlyn;
using Tomlyn.Model;

namespace BetterGit.Services {
    public enum VersionChangeType {
        Patch,
        Minor,
        Major
    }

    public class VersionService {
        private readonly string _repoPath;

        public VersionService(string repoPath) {
            _repoPath = repoPath;
        }

        public string IncrementVersion(VersionChangeType changeType = VersionChangeType.Patch) {
            // This creates/updates a '.betterGit/meta.toml' file
            string betterGitDir = Path.Combine(_repoPath, ".betterGit");
            if (!Directory.Exists(betterGitDir)) {
                Directory.CreateDirectory(betterGitDir);
            }

            string metaFile = Path.Combine(betterGitDir, "meta.toml");
            long major = 0, minor = 0, patch = 0;

            // 1. Read TOML
            if (File.Exists(metaFile)) {
                try {
                    string content = File.ReadAllText(metaFile);
                    var model = Toml.ToModel(content);
                    
                    // Migration from old single 'version' field
                    if (model.ContainsKey("version") && !model.ContainsKey("patch")) {
                        patch = (long)model["version"];
                    } else {
                        if (model.ContainsKey("major")) major = (long)model["major"];
                        if (model.ContainsKey("minor")) minor = (long)model["minor"];
                        if (model.ContainsKey("patch")) patch = (long)model["patch"];
                    }
                } catch { /* Ignore corrupt, start from 0.0.0 */ }
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
            var toml = new TomlTable {
                ["major"] = major,
                ["minor"] = minor,
                ["patch"] = patch
            };
            File.WriteAllText(metaFile, Toml.FromModel(toml));

            string versionString = $"{major}.{minor}.{patch}";

            // 4. Update package.json if exists
            string packageJsonPath = Path.Combine(_repoPath, "package.json");
            if (File.Exists(packageJsonPath)) {
                try {
                    string content = File.ReadAllText(packageJsonPath);
                    dynamic? json = JsonConvert.DeserializeObject(content);
                    if (json != null) {
                        json.version = versionString;
                        File.WriteAllText(packageJsonPath, JsonConvert.SerializeObject(json, Formatting.Indented));
                    }
                } catch { /* Ignore */ }
            }

            return $"v{versionString}";
        }
    }
}
