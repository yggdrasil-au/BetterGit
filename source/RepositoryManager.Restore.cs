using LibGit2Sharp;

namespace BetterGit;

public partial class RepositoryManager {
    /* :: :: Commands :: START :: */

    // --- COMMAND: RESTORE / REDO ---
    public void Restore(string targetSha) {
        if (!IsValidGitRepo()) {
            throw new Exception("Not a valid BetterGit repository. Run 'init' first.");
        }

        EnsureSafeState();

        using (Repository? repo = new Repository(_repoPath)) {
            Commit? currentCommit = repo.Head.Tip;
            Commit? targetCommit = repo.Lookup<Commit>(targetSha);

            if (targetCommit == null) {
                throw new Exception("Target state not found.");
            }

            bool isTargetAncestor = repo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = currentCommit }).Any(c => c.Sha == targetCommit.Sha);

            if (currentCommit.Sha != targetCommit.Sha) {
                string parkBranchName = $"archive/swapped_{DateTime.Now:yyyyMMdd_HHmmss}_{currentCommit.Sha.Substring(0, 7)}";
                repo.CreateBranch(parkBranchName, currentCommit);
                Console.WriteLine($"Current changes parked in {parkBranchName}");
            }

            repo.Reset(ResetMode.Hard, targetCommit);
            Console.WriteLine($"Restored to {targetSha.Substring(0, 7)}");
        }
    }

    /* :: :: Commands :: END :: */
}
