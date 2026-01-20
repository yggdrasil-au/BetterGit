using LibGit2Sharp;

using Newtonsoft.Json;

using System.Text.RegularExpressions;

namespace BetterGit;

public partial class RepositoryManager {
    /* :: :: Public API :: START :: */

    // --- HELPER: JSON OUTPUT FOR VS CODE ---
    public string GetTreeDataJson() {
        if (!IsValidGitRepo()) {
            return JsonConvert.SerializeObject(new {
                isInitialized = false,
                changes = new List<string>(),
                timeline = new List<object>(),
                archives = new List<object>(),
                warnings = new List<string>()
            });
        }

        List<string> warnings = GetGitmodulesWarnings();
        EmitWarningsToStderr(warnings);

        using (Repository repo = new Repository(_repoPath)) {
            var changes = GetChangesSafe(repo, _repoPath, includeUntracked: true)
                .Select(s => new { path = s.path, status = s.status })
                .ToList();

            bool hasUpstream = false;
            int aheadBy = 0;
            int behindBy = 0;
            bool isPublishPending = false;
            string? upstream = null;

            try {
                Branch? tracked = repo.Head.TrackedBranch;
                hasUpstream = repo.Head.IsTracking && tracked != null;
                if (hasUpstream && tracked != null) {
                    upstream = tracked.FriendlyName;
                    aheadBy = repo.Head.TrackingDetails.AheadBy ?? 0;
                    behindBy = repo.Head.TrackingDetails.BehindBy ?? 0;
                    isPublishPending = aheadBy > 0;
                }
            } catch {
                // ignore tracking failures
            }

            var timeline = repo.Commits
                                .Take(20)
                                .Select(c => new {
                                    id = c.Sha,
                                    version = ExtractVersion(c.MessageShort),
                                    message = ExtractMessage(c.MessageShort)
                                })
                                .ToList();

            var archives = repo.Branches
                                .Where(b => b.FriendlyName.StartsWith("archive/"))
                                .Select(b => new {
                                    name = b.FriendlyName.Replace("archive/", ""),
                                    sha = b.Tip.Sha,
                                    version = ExtractVersion(b.Tip.MessageShort),
                                    message = ExtractMessage(b.Tip.MessageShort)
                                })
                                .OrderByDescending(x => x.name)
                                .ToList();

            List<RemoteInfo> remotes = new List<RemoteInfo>();
            try {
                remotes = _remoteService.ListMergedRemotes(repo);
            } catch {
                // If remotes fail to load (corrupt config, libgit2 issue), keep the UI usable.
                remotes = new List<RemoteInfo>();
            }

            var data = new {
                isInitialized = true,
                changes,
                timeline,
                archives,
                warnings,
                remotes = remotes.Select(r => new {
                    name = r.Name,
                    fetchUrl = r.FetchUrl,
                    pushUrl = r.PushUrl,
                    group = r.Group,
                    provider = r.Provider,
                    isPublic = r.IsPublic,
                    hasGitRemote = r.HasGitRemote,
                    hasMetadata = r.HasMetadata,
                    isMisconfigured = r.IsMisconfigured
                }).ToList(),
                publish = new {
                    hasUpstream,
                    upstream,
                    aheadBy,
                    behindBy,
                    isPublishPending
                }
            };

            return JsonConvert.SerializeObject(data, Formatting.Indented);
        }
    }

    /* :: :: Public API :: END :: */
    // //
    /* :: :: Private Helpers :: START :: */

    private static readonly Regex GitmodulesSectionRegex = new(
        "^\\s*\\[\\s*submodule\\s+\"(?<name>[^\"]+)\"\\s*\\]\\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex GitmodulesKeyValueRegex = new(
        "^\\s*(?<key>[A-Za-z0-9\\.\\-_]+)\\s*=\\s*(?<value>.*)\\s*$",
        RegexOptions.Compiled);

    private static string TrimGitmodulesValue(string value) {
        string v = value.Trim();
        if (v.Length >= 2 && ((v.StartsWith('"') && v.EndsWith('"')) || (v.StartsWith('\'') && v.EndsWith('\'')))) {
            v = v.Substring(1, v.Length - 2);
        }
        return v.Trim();
    }

    private static string NormalizeGitmodulesPath(string raw) {
        string p = raw.Trim();
        p = p.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        p = p.TrimEnd(Path.DirectorySeparatorChar);
        return p;
    }

    private List<string> GetGitmodulesWarnings() {
        List<string> warnings = new List<string>();
        string gitmodulesPath = Path.Combine(_repoPath, ".gitmodules");
        if (!File.Exists(gitmodulesPath)) {
            return warnings;
        }

        string[] lines;
        try {
            lines = File.ReadAllLines(gitmodulesPath);
        } catch (Exception ex) {
            warnings.Add($".gitmodules: failed to read file: {ex.Message}");
            return warnings;
        }

        string? currentName = null;
        Dictionary<string, string> current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool parsedAnySection = false;

        void FinalizeCurrent() {
            if (currentName == null) {
                return;
            }
            parsedAnySection = true;

            string? rawPath;
            string? url;
            current.TryGetValue("path", out rawPath);
            current.TryGetValue("url", out url);

            if (string.IsNullOrWhiteSpace(rawPath)) {
                warnings.Add($".gitmodules: submodule \"{currentName}\" is missing required key: path");
                return;
            }

            string relPath = NormalizeGitmodulesPath(rawPath);
            if (string.IsNullOrWhiteSpace(relPath)) {
                warnings.Add($".gitmodules: submodule \"{currentName}\" has an empty/invalid path value");
                return;
            }

            string repoRootFull;
            string subAbsFull;
            try {
                repoRootFull = Path.GetFullPath(_repoPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
                subAbsFull = Path.GetFullPath(Path.Combine(_repoPath, relPath));
            } catch (Exception ex) {
                warnings.Add($".gitmodules: submodule \"{currentName}\" path \"{rawPath}\" could not be resolved: {ex.Message}");
                return;
            }

            if (!subAbsFull.StartsWith(repoRootFull, StringComparison.OrdinalIgnoreCase)) {
                warnings.Add($".gitmodules: submodule \"{currentName}\" path \"{rawPath}\" resolves outside repo root (ignored)");
                return;
            }

            if (string.IsNullOrWhiteSpace(url)) {
                warnings.Add($".gitmodules: submodule \"{currentName}\" path \"{rawPath}\" is missing key: url");
            }

            if (!Directory.Exists(subAbsFull)) {
                warnings.Add($".gitmodules: submodule \"{currentName}\" path \"{rawPath}\" not found on disk (not initialized or incorrect)");
                return;
            }

            string dotGit = Path.Combine(subAbsFull, ".git");
            if (!File.Exists(dotGit) && !Directory.Exists(dotGit)) {
                warnings.Add($".gitmodules: submodule \"{currentName}\" path \"{rawPath}\" exists but is not a git repo (missing .git)");
            }
        }

        for (int i = 0; i < lines.Length; i++) {
            string line = lines[i];
            string trimmed = line.Trim();

            if (trimmed.Length == 0) {
                continue;
            }
            if (trimmed.StartsWith("#") || trimmed.StartsWith(";")) {
                continue;
            }

            Match sec = GitmodulesSectionRegex.Match(line);
            if (sec.Success) {
                FinalizeCurrent();
                currentName = sec.Groups["name"].Value.Trim();
                current.Clear();
                continue;
            }

            Match kv = GitmodulesKeyValueRegex.Match(line);
            if (kv.Success) {
                if (currentName == null) {
                    warnings.Add($".gitmodules: key/value outside any [submodule] section at line {i + 1}: {trimmed}");
                    continue;
                }

                string key = kv.Groups["key"].Value.Trim();
                string value = TrimGitmodulesValue(kv.Groups["value"].Value);
                current[key] = value;
                continue;
            }

            warnings.Add($".gitmodules: syntax error at line {i + 1}: {trimmed}");
        }

        FinalizeCurrent();

        if (!parsedAnySection) {
            warnings.Add(".gitmodules: file present but no [submodule \"...\"] sections could be parsed");
        }

        return warnings;
    }

    private static void EmitWarningsToStderr(IEnumerable<string> warnings) {
        foreach (string w in warnings) {
            Console.Error.WriteLine($"[WARN] {w}");
        }
    }

    /* :: :: Private Helpers :: END :: */
}
