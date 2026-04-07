# BetterGit CLI

The core engine for BetterGit, built with .NET and `LibGit2Sharp`. This console application handles all the heavy lifting for repository management, versioning, and safe history traversal.

the core concept is to simplify version control by eliminating standard Git complexities i dont need
instead using it as a non-destructive save/load/backup system.
and adding some new features like publishing backups to maultiple remote repositories simultaneously.
all whithout preventing the user from using git directly if they want to.


## Prerequisites
* .NET 10.0 SDK

## Building
```pwsh
dotnet build
```

## Usage
*   **Initialize:**
    ```pwsh
    BetterGit.exe init "A:/Path/To/Project" [--node]
    ```
*   **Save Changes:**
    ```pwsh
    BetterGit.exe save "Commit message" [--major|--minor]
    ```
*   **Undo Last Save:**
    ```pwsh
    BetterGit.exe undo
    ```
    *Moves the current state to an archive branch and resets HEAD to the parent commit.*
*   **Redo:**
    ```pwsh
    BetterGit.exe redo
    ```
    *Restores the state from the most recent archive branch.*
*   **Get Tree Data (JSON):**
    ```pwsh
    BetterGit.exe get-tree-data
    ```
*   **Add Remote Mirror:**
    ```pwsh
    BetterGit.exe remote add <name> <url> [--group <g>] [--provider <p>] [--branch <b>] [--public|--private]
    ```
    *Creates a Git remote and stores the matching BetterGit metadata in `.betterGit/project.toml`.*
*   **Change Remote Metadata:**
    ```pwsh
    BetterGit.exe remote set-meta <name> [--group <g>] [--provider <p>] [--branch <b>] [--public|--private]
    ```

## Notice

This project was generated entirely with AI.
