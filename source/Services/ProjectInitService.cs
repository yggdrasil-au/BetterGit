using LibGit2Sharp;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Tomlyn;
using Tomlyn.Model;

namespace BetterGit;

public class ProjectInitService {
    /* :: :: Public API :: START :: */

    public static void InitProject(string path, bool isNode = false) {
        // 1. Create Directory if it doesn't exist
        if (!Directory.Exists(path)) {
            Directory.CreateDirectory(path);
        }

        // 2. Initialize Git (LibGit2Sharp)
        // This is safe: if a repo already exists, it just re-initializes it without deleting data.
        Repository.Init(path);

        using (Repository repo = new Repository(path)) {
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
        bool isNodeProject = isNode;

        if (File.Exists(packageJsonPath)) {
            // Read existing version to sync project.toml, but DO NOT modify package.json
            try {
                string content = File.ReadAllText(packageJsonPath);
                JObject? json = JsonConvert.DeserializeObject<JObject>(content);
                JToken? versionToken = json?["version"];
                if (versionToken != null) {
                    string v = versionToken.ToString();
                    string[] parts = v.Split('.');
                    if (parts.Length >= 1) long.TryParse(parts[0], out major);
                    if (parts.Length >= 2) long.TryParse(parts[1], out minor);
                    if (parts.Length >= 3) long.TryParse(parts[2], out patch);
                }
            } catch { /* Ignore corrupt package.json */ }
            isNodeProject = true;
        } else if (isNode) {
            // Create new package.json
            JObject pkg = new JObject {
                ["name"] = new DirectoryInfo(path).Name.ToLower().Replace(" ", "-"),
                ["version"] = "0.0.0",
                ["description"] = "Initialized by BetterGit"
            };
            File.WriteAllText(packageJsonPath, JsonConvert.SerializeObject(value: pkg, formatting: Formatting.Indented));
            isNodeProject = true;
        }

        // 4. Create the BetterGit config folder + TOML files.
        BetterGitConfigPaths.MigrateMetaTomlToProjectTomlIfNeeded(path);
        BetterGitConfigPaths.EnsureBetterGitDirExists(path);

        string projectFile = BetterGitConfigPaths.GetProjectTomlPath(path);
        if (!File.Exists(projectFile)) {
            TomlTable toml = new TomlTable {
                ["major"] = major,
                ["minor"] = minor,
                ["patch"] = patch,
                ["isAlpha"] = false,
                ["isBeta"] = false,
                ["isNodeProject"] = isNodeProject
            };
            File.WriteAllText(projectFile, Toml.FromModel(toml));
        }

        // Local-only settings and credentials should never be committed.
        string localFile = BetterGitConfigPaths.GetLocalTomlPath(path);
        if (!File.Exists(localFile)) {
            string localTemplate =
                "# BetterGit local configuration (ignored by git)\n" +
                "# User preferences live here (e.g., default publish group).\n";
            File.WriteAllText(localFile, localTemplate);
        }

        string secretsFile = BetterGitConfigPaths.GetSecretsTomlPath(path);
        if (!File.Exists(secretsFile)) {
            string secretsTemplate =
                "# BetterGit secrets (ignored by git)\n" +
                "# Store credentials here (e.g., provider tokens) - never commit this file.\n";
            File.WriteAllText(secretsFile, secretsTemplate);
        }

        // 5. Ensure .gitignore contains BetterGit ignore rules (recommended)
        string ignoreFile = Path.Combine(path, ".gitignore");
        if (!File.Exists(ignoreFile)) {
            // Ignore the .vs folder, bin/obj, and the archive branches metadata if you ever store it in files
            // Also ignore node_modules if node
            string ignores = "bin/\nobj/\n.vscode/\n";
            if (isNodeProject) {
                ignores += "node_modules/\n";
            }

            ignores += ".betterGit/local.toml\n";
            ignores += ".betterGit/secrets.toml\n";
            File.WriteAllText(ignoreFile, ignores);
        } else {
            EnsureGitignoreRules(ignoreFile, new[] { ".betterGit/local.toml", ".betterGit/secrets.toml" });
        }

        Console.WriteLine($"BetterGit initialized in: {path}");
        Console.WriteLine("Ready for first Save.");
    }

    /* :: :: Public API :: END :: */

    private static void EnsureGitignoreRules(string gitignorePath, IReadOnlyList<string> rules) {
        string content;
        try {
            content = File.ReadAllText(gitignorePath);
        } catch {
            return;
        }

        string normalized = content.Replace("\r\n", "\n");
        HashSet<string> existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in normalized.Split('\n')) {
            string trimmed = line.Trim();
            if (trimmed.Length > 0) {
                existing.Add(trimmed);
            }
        }

        List<string> missing = new List<string>();
        foreach (string rule in rules) {
            if (!existing.Contains(rule)) {
                missing.Add(rule);
            }
        }
        if (missing.Count == 0) {
            return;
        }

        using (StreamWriter writer = new StreamWriter(gitignorePath, append: true)) {
            if (!normalized.EndsWith("\n")) {
                writer.WriteLine();
            }
            writer.WriteLine();
            writer.WriteLine("# BetterGit (local/private files)");
            foreach (string rule in missing) {
                writer.WriteLine(rule);
            }
        }
    }
}
