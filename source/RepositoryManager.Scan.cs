using LibGit2Sharp;

using Newtonsoft.Json;

namespace BetterGit;

public partial class RepositoryManager {
    /* :: :: Commands :: START :: */

    // --- COMMAND: SCAN-REPOS ---
    public string ScanRepositories(bool includeNested = true) {
        RepoItem rootItem = GetRepoTree(_repoPath, _repoPath, includeNested);
        return JsonConvert.SerializeObject(rootItem, Formatting.Indented);
    }

    // Returns ONLY non-submodule nested repos (lazy-load for UI).
    public string ScanNestedRepositories() {
        List<RepoItem> nested = GetNestedReposExcludingSubmodules(_repoPath);
        return JsonConvert.SerializeObject(nested, Formatting.Indented);
    }

    /* :: :: Commands :: END :: */
    // //
    /* :: :: Private Helpers :: START :: */

    private RepoItem GetRepoTree(string currentPath, string rootPath, bool includeNested) {
        string relPath = Path.GetRelativePath(rootPath, currentPath);
        if (relPath == ".") {
            relPath = "";
        }

        RepoItem item = new RepoItem {
            Name = Path.GetFileName(currentPath),
            Path = relPath,
            Type = (currentPath == rootPath) ? "root" : "nested"
        };

        HashSet<string> knownPaths = new HashSet<string>();

        // 1. Submodules (Only if it is a repo)
        bool isValidRepo = false;
        try {
            isValidRepo = Repository.IsValid(currentPath);
        } catch { 
            // Ignore permission/ownership errors during validity check
            isValidRepo = false; 
        }

        if (isValidRepo) {
            try {
                using (Repository repo = new Repository(currentPath)) {
                    foreach (Submodule sm in repo.Submodules) {
                        string fullPath = Path.Combine(currentPath, sm.Path);
                        if (Directory.Exists(fullPath)) {
                            RepoItem smItem = GetRepoTree(fullPath, rootPath, includeNested);
                            smItem.Type = "submodule";
                            smItem.Name = sm.Name;
                            item.Children.Add(smItem);
                            
                            knownPaths.Add(Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant());
                        }
                    }
                }
            } catch {}
        }

        // 2. Nested Repos
        if (includeNested) {
            List<string> nestedPaths = new List<string>();
            ScanForNestedRepos(currentPath, nestedPaths, knownPaths, 0);

            foreach (string nestedPath in nestedPaths) {
                RepoItem nestedItem = GetRepoTree(nestedPath, rootPath, includeNested);
                nestedItem.Type = "nested";
                item.Children.Add(nestedItem);
            }
        }

        return item;
    }

    private void ScanForNestedRepos(string dir, List<string> results, HashSet<string> knownPaths, int depth) {
        if (depth > 50) {
            return;
        }

        try {
            string[] directories = Directory.GetDirectories(dir);
            foreach (string d in directories) {
                string name = Path.GetFileName(d);

                // Skip common junk
                if (name.StartsWith(".") && name != ".git") {
                    continue; // Skip .vscode, .vs, etc.
                }
                if (name == "node_modules" || name == "bin" || name == "obj" || name == "packages") {
                    continue;
                }

                // Check if this is a git repo
                string gitPath = Path.Combine(d, ".git");
                if (Directory.Exists(gitPath) || File.Exists(gitPath)) {
                    // It's a repo.

                    // Requirement: nested repos list should only show items that dont exist in the submodules file
                    string fullPath = Path.GetFullPath(d).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
                    if (knownPaths.Contains(fullPath)) {
                        continue;
                    }

                    results.Add(d);
                    // Don't recurse into a repo
                    continue;
                }

                // Recurse
                ScanForNestedRepos(d, results, knownPaths, depth + 1);
            }
        } catch { /* Access denied etc */ }
    }

    private List<RepoItem> GetNestedReposExcludingSubmodules(string rootPath) {
        HashSet<string> submoduleRoots = CollectSubmoduleRootsRecursive(rootPath);
        List<RepoItem> results = new List<RepoItem>();

        void Recurse(string dir, int depth) {
            if (depth > 50) {
                return;
            }

            try {
                foreach (string d in Directory.GetDirectories(dir)) {
                    string name = Path.GetFileName(d);

                    // Skip common junk
                    if (name.StartsWith(".") && name != ".git") {
                        continue;
                    }
                    if (name == "node_modules" || name == "bin" || name == "obj" || name == "packages") {
                        continue;
                    }

                    string full = Path.GetFullPath(d).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    // Don't scan inside submodules
                    if (submoduleRoots.Any(sm => IsSameOrChildPath(full, sm))) {
                        continue;
                    }

                    // Check if this is a git repo
                    string gitPath = Path.Combine(d, ".git");
                    if (Directory.Exists(gitPath) || File.Exists(gitPath)) {
                        // Exclude the root repo itself
                        if (!string.Equals(full, Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) {
                            string rel = Path.GetRelativePath(rootPath, d);
                            if (rel == ".") {
                                rel = "";
                            }
                            results.Add(new RepoItem {
                                Name = Path.GetFileName(d),
                                Path = rel,
                                Type = "nested",
                                Children = new List<RepoItem>()
                            });
                        }
                        // Don't recurse into a repo
                        continue;
                    }

                    Recurse(d, depth + 1);
                }
            } catch {
                // Access denied etc
            }
        }

        Recurse(rootPath, 0);
        return results;
    }

    private static bool IsSameOrChildPath(string candidate, string root) {
        if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        // ensure boundary
        if (candidate.Length == root.Length) {
            return true;
        }
        char c = candidate[root.Length];
        return c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
    }

    private HashSet<string> CollectSubmoduleRootsRecursive(string rootPath) {
        HashSet<string> roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Collect(string repoPath) {
            bool isValid;
            try { isValid = Repository.IsValid(repoPath); } catch { isValid = false; }
            if (!isValid) {
                return;
            }

            try {
                using (Repository repo = new Repository(repoPath)) {
                    foreach (Submodule sm in repo.Submodules) {
                        string fullPath = Path.GetFullPath(Path.Combine(repoPath, sm.Path))
                            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        if (Directory.Exists(fullPath) && roots.Add(fullPath)) {
                            Collect(fullPath);
                        }
                    }
                }
            } catch {
                // ignore
            }
        }

        Collect(rootPath);
        return roots;
    }

    /* :: :: Private Helpers :: END :: */
}
