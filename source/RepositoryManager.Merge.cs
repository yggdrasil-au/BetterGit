using LibGit2Sharp;

namespace BetterGit;

public partial class RepositoryManager {
    /* :: :: Commands :: START :: */

    // --- COMMAND: MERGE ---
    public void Merge(string sourceSha) {
        if (!IsValidGitRepo()) {
            throw new Exception("Not a valid BetterGit repository. Run 'init' first.");
        }

        EnsureSafeState();

        using (Repository repo = new Repository(_repoPath)) {
            Commit? sourceCommit = repo.Lookup<Commit>(sourceSha);
            if (sourceCommit == null) {
                throw new Exception($"Commit {sourceSha} not found.");
            }

            Console.WriteLine($"Merging {sourceSha.Substring(0, 7)} into HEAD...");

            Signature author = repo.Config.BuildSignature(DateTime.Now);
            if (author == null) {
                author = new Signature(name: "BetterGit User", email: "user@bettergit.local", when: DateTime.Now);
            }

            // We use NoFastForward to ensure we can stop and review (and because we want to preserve history structure usually)
            // CommitOnSuccess = false allows the user to review changes before saving.
            MergeResult result = repo.Merge(sourceCommit, author, new MergeOptions {
                CommitOnSuccess = false,
                FastForwardStrategy = FastForwardStrategy.NoFastForward
            });

            // Fix: Revert changes to .betterGit folder to avoid version conflicts
            // We want to keep the current version, not the one from the merge source.
            List<string> betterGitFiles;
            try {
                StatusOptions opts = new StatusOptions {
                    IncludeUntracked = false,
                    RecurseUntrackedDirs = false
                };
                betterGitFiles = repo.RetrieveStatus(opts)
                    .Where(s => s.FilePath.Replace("\\", "/").StartsWith(".betterGit/"))
                    .Select(s => s.FilePath)
                    .ToList();
            } catch {
                // If status fails (e.g., long path in untracked files), just skip this safety revert.
                betterGitFiles = new List<string>();
            }

            if (betterGitFiles.Any()) {
                // Checkout from HEAD (current state), effectively ignoring the merge for these files
                repo.CheckoutPaths(repo.Head.Tip.Sha, betterGitFiles, new CheckoutOptions {
                    CheckoutModifiers = CheckoutModifiers.Force
                });
            }

            if (repo.Index.Conflicts.Any()) {
                Console.WriteLine("Merge resulted in conflicts. Please resolve them in the editor and then Save.");
            } else if (result.Status == MergeStatus.UpToDate) {
                Console.WriteLine("Already up to date.");
            } else {
                Console.WriteLine("Merge staged. Review changes in the list and Save when ready.");
            }
        }
    }

    /* :: :: Commands :: END :: */
}
