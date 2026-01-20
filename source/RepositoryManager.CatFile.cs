using LibGit2Sharp;

namespace BetterGit;

public partial class RepositoryManager {
    /* :: :: Commands :: START :: */

    // --- COMMAND: CAT-FILE ---
    public void CatFile(string sha, string relativePath) {
        if (!IsValidGitRepo()) {
            return;
        }

        using (Repository? repo = new Repository(_repoPath)) {
            Commit? commit;
            if (sha.ToUpper() == "HEAD") {
                commit = repo.Head.Tip;
            } else {
                commit = repo.Lookup<Commit>(sha);
            }

            if (commit == null) {
                throw new Exception("Commit not found.");
            }

            TreeEntry? treeEntry = commit[relativePath];
            if (treeEntry == null) {
                return;
            }

            Blob? blob = treeEntry.Target as Blob;
            if (blob == null) {
                return;
            }

            using (Stream content = blob.GetContentStream())
            using (StreamReader reader = new StreamReader(content)) {
                Console.Write(reader.ReadToEnd());
            }
        }
    }

    /* :: :: Commands :: END :: */
}
