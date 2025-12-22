
namespace BetterGit;

class Program {
    /* :: :: Entry Point :: START :: */

    static void Main(string[] args) {
        if (args.Length == 0 || args[0] == "-h" || args[0] == "--help") {
            PrintHelp();
            return;
        }

        // VS Code sets the "Current Working Directory" to the user's project folder.
        // We use this to find the repository.
        string repoPath = Directory.GetCurrentDirectory();

        // Check for --path argument
        for (int i = 0; i < args.Length; i++) {
            if (args[i] == "--path" && i + 1 < args.Length) {
                repoPath = args[i + 1];
                break;
            }
        }

        RepositoryManager manager = new RepositoryManager(repoPath);

        try {
            switch (args[0].ToLower()) {
                case "scan-repos":
                    {
                        // By default we include nested repos for backwards compatibility.
                        // VS Code UI may pass --no-nested to avoid scanning until user expands "Other Modules".
                        bool includeNested = !args.Any(a => a.Equals("--no-nested", StringComparison.OrdinalIgnoreCase));
                        Console.WriteLine(manager.ScanRepositories(includeNested));
                        break;
                    }

                case "scan-nested-repos":
                    // usage: BetterGit.exe scan-nested-repos --path <root>
                    Console.WriteLine(manager.ScanNestedRepositories());
                    break;

                case "init":
                    // If no path provided, use current directory
                    string targetPath = Directory.GetCurrentDirectory();
                    bool isNode = false;

                    // Simple arg parsing
                    foreach (string arg in args.Skip(1)) {
                        if (arg.Equals("--node", StringComparison.OrdinalIgnoreCase)) {
                            isNode = true;
                        } else if (!arg.StartsWith("-")) {
                            targetPath = arg;
                        }
                    }

                    RepositoryManager.InitProject(targetPath, isNode);
                    break;

                case "save":
                    // usage: BetterGit.exe save "My commit message" [--major|--minor|--patch|--no-increment|--set-version <v>]
                    if (args.Length < 2) {
                        throw new Exception("Message required.");
                    }

                    VersionChangeType changeType = VersionChangeType.Patch;
                    string? manualVersion = null;

                    if (args.Contains("--major")) changeType = VersionChangeType.Major;
                    else if (args.Contains("--minor")) changeType = VersionChangeType.Minor;
                    else if (args.Contains("--no-increment")) changeType = VersionChangeType.None;

                    // Check for --set-version
                    for (int i = 0; i < args.Length; i++) {
                        if (args[i] == "--set-version" && i + 1 < args.Length) {
                            changeType = VersionChangeType.Manual;
                            manualVersion = args[i + 1];
                        }
                    }

                    manager.Save(args[1], changeType, manualVersion);
                    break;

                case "undo":
                    // usage: BetterGit.exe undo
                    manager.Undo();
                    break;

                case "redo":
                    // usage: BetterGit.exe redo
                    manager.Redo();
                    break;

                case "restore":
                    // usage: BetterGit.exe restore [commit-sha]
                    if (args.Length < 2) {
                        throw new Exception("Commit SHA required.");
                    }

                    manager.Restore(args[1]);
                    break;

                case "get-tree-data":
                    // usage: BetterGit.exe get-tree-data
                    // Outputs JSON for VS Code to read
                    Console.WriteLine(manager.GetTreeDataJson());
                    break;

                case "cat-file":
                    // usage: BetterGit.exe cat-file [commit-sha|HEAD] [relative-path]
                    if (args.Length < 3) {
                        throw new Exception("SHA and Path required.");
                    }

                    manager.CatFile(args[1], args[2]);
                    break;

                case "publish":
                    // usage: BetterGit.exe publish [--group <name>] [--public|--private]
                    string? publishGroup = null;
                    bool? publishPublic = null;

                    for (int i = 1; i < args.Length; i++) {
                        if (args[i].Equals("--group", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                            publishGroup = args[i + 1];
                            i++;
                            continue;
                        }
                        if (args[i].Equals("--public", StringComparison.OrdinalIgnoreCase)) {
                            publishPublic = true;
                            continue;
                        }
                        if (args[i].Equals("--private", StringComparison.OrdinalIgnoreCase)) {
                            publishPublic = false;
                            continue;
                        }
                    }

                    manager.Publish(publishGroup, publishPublic);
                    break;

                case "remote":
                    // usage:
                    //   BetterGit.exe remote list [--json]
                    //   BetterGit.exe remote set-meta <name> [--group <g>] [--provider <p>] [--public|--private]
                    if (args.Length < 2) {
                        throw new Exception("Remote subcommand required (list, set-meta).");
                    }

                    string sub = args[1].ToLowerInvariant();
                    if (sub == "list") {
                        bool asJson = args.Any(a => a.Equals("--json", StringComparison.OrdinalIgnoreCase));
                        if (asJson) {
                            Console.WriteLine(manager.GetRemotesJson());
                        } else {
                            List<RemoteInfo> remotes = manager.ListRemotes();
                            foreach (RemoteInfo r in remotes) {
                                string url = !string.IsNullOrWhiteSpace(r.PushUrl) ? r.PushUrl! : (r.FetchUrl ?? "(no url)");
                                string pub = r.IsPublic ? "public" : "private";
                                string status = r.IsMisconfigured ? "MISCONFIGURED" : (r.HasMetadata ? "managed" : "unmanaged");
                                Console.WriteLine($"{r.Name} [{status}] ({r.Provider}) ({r.Group}) ({pub}) -> {url}");
                            }
                        }
                        break;
                    }

                    if (sub == "set-meta") {
                        if (args.Length < 3) {
                            throw new Exception("Remote name required.");
                        }

                        string remoteName = args[2];
                        string? group = null;
                        string? provider = null;
                        bool? isPublic = null;

                        for (int i = 3; i < args.Length; i++) {
                            if (args[i].Equals("--group", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                                group = args[i + 1];
                                i++;
                                continue;
                            }
                            if (args[i].Equals("--provider", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                                provider = args[i + 1];
                                i++;
                                continue;
                            }
                            if (args[i].Equals("--public", StringComparison.OrdinalIgnoreCase)) {
                                isPublic = true;
                                continue;
                            }
                            if (args[i].Equals("--private", StringComparison.OrdinalIgnoreCase)) {
                                isPublic = false;
                                continue;
                            }
                        }

                        manager.SetRemoteMetadata(remoteName, group, provider, isPublic);
                        Console.WriteLine($"Updated metadata for remote: {remoteName}");
                        break;
                    }

                    throw new Exception($"Unknown remote subcommand: {args[1]}");

                case "merge":
                    // usage: BetterGit.exe merge [sha]
                    if (args.Length < 2) {
                        throw new Exception("Source SHA required.");
                    }
                    manager.Merge(args[1]);
                    break;

                case "set-channel":
                    // usage: BetterGit.exe set-channel [alpha|beta|stable]
                    if (args.Length < 2) {
                        throw new Exception("Channel required (alpha, beta, stable).");
                    }
                    manager.SetChannel(args[1]);
                    break;

                case "get-version-info":
                    // usage: BetterGit.exe get-version-info
                    Console.WriteLine(manager.GetVersionInfo());
                    break;

                default:
                    Console.WriteLine($"Unknown command: {args[0]}");
                    Console.WriteLine("Use --help to see available commands.");
                    break;
            }
        } catch (Exception ex) {
            // We print errors to Standard Error so VS Code knows something failed
            Console.Error.WriteLine(ex.Message);
        }
    }

    /* :: :: Entry Point :: END :: */
    // //
    /* :: :: Helpers :: START :: */

    static void PrintHelp() {
        Console.WriteLine("BetterGit - A simplified Git wrapper");
        Console.WriteLine("Usage: BetterGit <command> [arguments]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  init [path] [--node]   Initialize a new project");
        Console.WriteLine("  save <message>         Save changes (commit)");
        Console.WriteLine("  undo                   Undo the last save");
        Console.WriteLine("  redo                   Redo the last undo");
        Console.WriteLine("  restore <sha>          Restore a specific state");
        Console.WriteLine("  get-tree-data          Output JSON history tree");
        Console.WriteLine("  cat-file <sha> <path>  Print file content");
        Console.WriteLine("  publish [--group <g>] [--public|--private]  Push current branch");
        Console.WriteLine("  remote list [--json]   List Git remotes + metadata");
        Console.WriteLine("  remote set-meta <name> [--group <g>] [--provider <p>] [--public|--private]  Update metadata");
        Console.WriteLine("  -h, --help             Show this help message");
    }

    /* :: :: Helpers :: END :: */
}
