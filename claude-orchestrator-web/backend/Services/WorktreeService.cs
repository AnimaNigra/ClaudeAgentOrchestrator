using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ClaudeOrchestrator.Services;

public class WorktreeService
{
    public record WorktreeInfo(string Path, string Branch, bool IsMain);

    /// <summary>
    /// Create a new git worktree as a sibling directory to the repo.
    /// Path: &lt;repoDir&gt;-wt-&lt;name&gt;/
    /// Branch: wt/&lt;name&gt;
    /// </summary>
    public async Task<(string worktreePath, string branch)> CreateAsync(string repoPath, string name)
    {
        var topLevel = (await RunGitAsync(repoPath, "rev-parse --show-toplevel")).Trim();
        if (string.IsNullOrEmpty(topLevel))
            throw new InvalidOperationException($"Not a git repository: {repoPath}");

        var safeName = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9_-]", "-");
        var branch = $"wt/{safeName}";
        var worktreePath = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(topLevel, "..",
                System.IO.Path.GetFileName(topLevel) + $"-wt-{safeName}"));

        if (Directory.Exists(worktreePath))
            throw new InvalidOperationException($"Worktree directory already exists: {worktreePath}");

        await RunGitAsync(topLevel, $"worktree add \"{worktreePath}\" -b \"{branch}\"");

        return (worktreePath, branch);
    }

    /// <summary>List all worktrees for the given repo.</summary>
    public async Task<List<WorktreeInfo>> ListAsync(string repoPath)
    {
        var topLevel = (await RunGitAsync(repoPath, "rev-parse --show-toplevel")).Trim();
        if (string.IsNullOrEmpty(topLevel))
            return new List<WorktreeInfo>();

        var output = await RunGitAsync(topLevel, "worktree list --porcelain");
        var worktrees = new List<WorktreeInfo>();
        string? currentPath = null;
        string? currentBranch = null;

        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("worktree "))
            {
                if (currentPath is not null)
                    worktrees.Add(new WorktreeInfo(currentPath, currentBranch ?? "detached",
                        string.Equals(currentPath, topLevel, StringComparison.OrdinalIgnoreCase)));
                currentPath = line[9..];
                currentBranch = null;
            }
            else if (line.StartsWith("branch "))
            {
                var refName = line[7..];
                currentBranch = refName.StartsWith("refs/heads/") ? refName[11..] : refName;
            }
        }
        if (currentPath is not null)
            worktrees.Add(new WorktreeInfo(currentPath, currentBranch ?? "detached",
                string.Equals(currentPath, topLevel, StringComparison.OrdinalIgnoreCase)));

        return worktrees;
    }

    /// <summary>Remove a worktree and optionally delete its branch.</summary>
    public async Task RemoveAsync(string repoPath, string worktreePath, bool deleteBranch = true)
    {
        var topLevel = (await RunGitAsync(repoPath, "rev-parse --show-toplevel")).Trim();

        await RunGitAsync(topLevel, $"worktree remove \"{worktreePath}\" --force");

        if (deleteBranch)
        {
            var dirName = System.IO.Path.GetFileName(worktreePath);
            var match = Regex.Match(dirName, @"-wt-(.+)$");
            if (match.Success)
            {
                var branch = $"wt/{match.Groups[1].Value}";
                try { await RunGitAsync(topLevel, $"branch -D \"{branch}\""); }
                catch { /* branch may not exist or may have been merged */ }
            }
        }
    }

    private static async Task<string> RunGitAsync(string workDir, string args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {args} failed: {error.Trim()}");

        return output;
    }
}
