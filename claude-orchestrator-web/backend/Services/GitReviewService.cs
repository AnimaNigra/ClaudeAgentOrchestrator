using System.Diagnostics;

namespace ClaudeOrchestrator.Services;

public record GitFileChange(string Path, string Status);
public record GitReviewResult(
    List<GitFileChange> Files,
    string Diff,
    string Branch,
    bool IsGitRepo);

public class GitReviewService
{
    public async Task<GitReviewResult> GetReviewAsync(string? cwd)
    {
        if (string.IsNullOrEmpty(cwd) || !Directory.Exists(cwd))
            return new([], "", "", false);

        var isGit = await RunGitAsync(cwd, "rev-parse --is-inside-work-tree");
        if (isGit.Trim() != "true")
            return new([], "", "", false);

        var branch = (await RunGitAsync(cwd, "branch --show-current")).Trim();
        var status = await RunGitAsync(cwd, "status --porcelain");
        var diff = await RunGitAsync(cwd, "diff");
        var diffCached = await RunGitAsync(cwd, "diff --cached");

        var fullDiff = string.IsNullOrEmpty(diffCached)
            ? diff
            : diff + "\n" + diffCached;

        var files = status
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var s = line[..2].Trim();
                var p = line[3..];
                var label = s switch
                {
                    "M" => "modified",
                    "A" => "added",
                    "D" => "deleted",
                    "??" => "untracked",
                    "R" => "renamed",
                    _ => s,
                };
                return new GitFileChange(p, label);
            })
            .ToList();

        return new(files, fullDiff, branch, true);
    }

    private static async Task<string> RunGitAsync(string cwd, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi)!;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return output;
        }
        catch { return ""; }
    }
}
