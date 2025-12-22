using LibGit2Sharp;

using System.Diagnostics;

namespace BetterGit;

public partial class RepositoryManager {
    /* :: :: Commands :: START :: */

    // --- COMMAND: PUBLISH ---
    public void Publish() {
        if (!IsValidGitRepo()) {
            throw new Exception("Not a valid BetterGit repository. Run 'init' first.");
        }

        using (Repository repo = new Repository(_repoPath)) {
            RemoteCollection remotes = repo.Network.Remotes;
            if (!remotes.Any()) {
                Console.WriteLine("No remotes configured. Add a remote using 'git remote add <name> <url>' first.");
                return;
            }

            foreach (Remote remote in remotes) {
                Console.WriteLine($"Publishing to {remote.Name}...");

                string branchName = repo.Head.FriendlyName;

                ProcessStartInfo processInfo = new ProcessStartInfo(fileName: "git", arguments: $"push {remote.Name} {branchName}") {
                    WorkingDirectory = _repoPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process? process = Process.Start(processInfo);
                if (process != null) {
                    using (process) {
                        process.WaitForExit();

                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();

                        if (process.ExitCode == 0) {
                            Console.WriteLine($"Successfully published to {remote.Name}.");
                            if (!string.IsNullOrWhiteSpace(output)) {
                                Console.WriteLine(output);
                            }
                        } else {
                            Console.Error.WriteLine($"Failed to publish to {remote.Name}.");
                            Console.Error.WriteLine(error);
                        }
                    }
                }
            }
        }
    }

    /* :: :: Commands :: END :: */
}
