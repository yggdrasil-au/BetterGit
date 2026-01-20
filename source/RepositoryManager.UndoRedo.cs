using LibGit2Sharp;

namespace BetterGit;

public partial class RepositoryManager {
    /* :: :: Commands :: START :: */

    // --- COMMAND: UNDO ---
    public void Undo() {
        if (!IsValidGitRepo()) {
            throw new Exception("Not a valid BetterGit repository. Run 'init' first.");
        }

        EnsureSafeState();

        using (Repository? repo = new Repository(_repoPath)) {
            Commit? currentCommit = repo.Head.Tip;

            if (!currentCommit.Parents.Any()) {
                throw new Exception("Cannot Undo: This is the first commit.");
            }

            Commit? parentCommit = currentCommit.Parents.First();

            string parkBranchName = $"archive/undo_{DateTime.Now:yyyyMMdd_HHmmss}_{currentCommit.Sha.Substring(0, 7)}";
            repo.CreateBranch(parkBranchName, currentCommit);

            repo.Reset(ResetMode.Hard, parentCommit);

            Console.WriteLine($"Undone. Previous state archived at: {parkBranchName}");
        }
    }

    // --- COMMAND: REDO ---
    public void Redo() {
        if (!IsValidGitRepo()) {
            throw new Exception("Not a valid BetterGit repository. Run 'init' first.");
        }

        EnsureSafeState();

        using (Repository? repo = new Repository(_repoPath)) {
            Commit? currentCommit = repo.Head.Tip;

            // Find branches that are "undo" archives
            List<(Branch Branch, Commit Commit)> candidates = repo.Branches
                .Where(b => b.FriendlyName.StartsWith("archive/undo_"))
                .Select(b => (Branch: b, Commit: b.Tip))
                // Exclude the current state so we can step through the redo stack
                .Where(x => x.Commit.Sha != currentCommit.Sha)
                .OrderByDescending(x => x.Branch.FriendlyName) // Newer timestamps first
                .ToList();

            if (!candidates.Any()) {
                throw new Exception("Nothing to Redo. No undone states found from this point.");
            }

            // Pick the most recent one
            (Branch Branch, Commit Commit) target = candidates.First();

            // Restore to that commit
            // We call Restore directly. It will handle parking the current state (which is the parent)
            // into a swapped branch, ensuring we don't lose the "undo" point either.
            Restore(target.Commit.Sha);
        }
    }

    /* :: :: Commands :: END :: */
}
