using System;
using System.IO;
using LibGit2Sharp;
using Newtonsoft.Json;
using Tomlyn;
using Tomlyn.Model;

namespace BetterGit.Services {
    public class ProjectInitService {
        public static void InitProject(string path, bool isNode = false) {
            // 1. Create Directory if it doesn't exist
            if (!Directory.Exists(path)) {
                Directory.CreateDirectory(path);
            }

            // 2. Initialize Git (LibGit2Sharp)
            // This is safe: if a repo already exists, it just re-initializes it without deleting data.
            Repository.Init(path);

            using (var repo = new Repository(path)) {
                // Force "main" as the default branch if the repo is empty (fresh init)
                if (!repo.Commits.Any()) {
                    // set HEAD to point to refs/heads/main
                    repo.Refs.UpdateTarget(repo.Refs["HEAD"], "refs/heads/main");
                }
            }

            // 3. Handle Node.js package.json & Determine Initial Version
            // If --node flag is passed OR package.json already exists
            string packageJsonPath = Path.Combine(path, "package.json");
            long major = 0, minor = 0, patch = 0;

            if (File.Exists(packageJsonPath)) {
                // Read existing version to sync meta.toml, but DO NOT modify package.json
                try {
                    string content = File.ReadAllText(packageJsonPath);
                    dynamic? json = JsonConvert.DeserializeObject(content);
                    if (json != null && json.version != null) {
                        string v = json.version.ToString();
                        var parts = v.Split('.');
                        if (parts.Length >= 1) long.TryParse(parts[0], out major);
                        if (parts.Length >= 2) long.TryParse(parts[1], out minor);
                        if (parts.Length >= 3) long.TryParse(parts[2], out patch);
                    }
                } catch { /* Ignore corrupt package.json */ }
            } else if (isNode) {
                // Create new package.json
                var pkg = new {
                    name = new DirectoryInfo(path).Name.ToLower().Replace(" ", "-"),
                    version = "0.0.0",
                    description = "Initialized by BetterGit"
                };
                File.WriteAllText(packageJsonPath, JsonConvert.SerializeObject(pkg, Formatting.Indented));
            }

            // 4. Create the "BetterGit" Metadata (.betterGit/meta.toml)
            string betterGitDir = Path.Combine(path, ".betterGit");
            if (!Directory.Exists(betterGitDir)) {
                Directory.CreateDirectory(betterGitDir);
            }

            string metaFile = Path.Combine(betterGitDir, "meta.toml");

            if (!File.Exists(metaFile)) {
                var toml = new TomlTable {
                    ["major"] = major,
                    ["minor"] = minor,
                    ["patch"] = patch,
                    ["isAlpha"] = false,
                    ["isBeta"] = false
                };
                File.WriteAllText(metaFile, Toml.FromModel(toml));
            }

            // 5. Create a default .gitignore (Optional but recommended)
            string ignoreFile = Path.Combine(path, ".gitignore");
            if (!File.Exists(ignoreFile)) {
                // Ignore the .vs folder, bin/obj, and the archive branches metadata if you ever store it in files
                // Also ignore node_modules if node
                string ignores = "bin/\nobj/\n.vscode/\n";
                if (isNode) {
                    ignores += "node_modules/\n";
                }

                File.WriteAllText(ignoreFile, ignores);
            }

            Console.WriteLine($"BetterGit initialized in: {path}");
            Console.WriteLine("Ready for first Save.");
        }
    }
}
