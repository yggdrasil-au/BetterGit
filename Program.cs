using BetterGit.Services;
using BetterGit.source;

namespace BetterGit;

class Program {
    static void Main(String[] args) {
        if (args.Length == 0 || args[0] == "-h" || args[0] == "--help") {
            PrintHelp();
            return;
        }

        // VS Code sets the "Current Working Directory" to the user's project folder.
        // We use this to find the repository.
        String repoPath = Directory.GetCurrentDirectory();
        var manager = new RepositoryManager(repoPath);

        try {
            switch (args[0].ToLower()) {
                case "init":
                    // usage: BetterGit.exe init "C:/Projects/MyNewGame" [--node]
                    // If no path provided, use current directory
                    String targetPath = Directory.GetCurrentDirectory();
                    Boolean isNode = false;

                    // Simple arg parsing
                    foreach (var arg in args.Skip(1)) {
                        if (arg.Equals("--node", StringComparison.OrdinalIgnoreCase)) {
                            isNode = true;
                        } else if (!arg.StartsWith("-")) {
                            targetPath = arg;
                        }
                    }

                    RepositoryManager.InitProject(targetPath, isNode);
                    break;

                case "save":
                    // usage: BetterGit.exe save "My commit message" [--major|--minor|--patch]
                    if (args.Length < 2) {
                        throw new Exception("Message required.");
                    }

                    VersionChangeType changeType = VersionChangeType.Patch;
                    if (args.Contains("--major")) changeType = VersionChangeType.Major;
                    else if (args.Contains("--minor")) changeType = VersionChangeType.Minor;

                    manager.Save(args[1], changeType);
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
                    // usage: BetterGit.exe publish
                    manager.Publish();
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
        Console.WriteLine("  -h, --help             Show this help message");
    }
}
