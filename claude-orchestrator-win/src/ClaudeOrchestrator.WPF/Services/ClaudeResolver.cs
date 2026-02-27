using System.Diagnostics;
using System.IO;

namespace ClaudeOrchestrator.WPF.Services;

public static class ClaudeResolver
{
    public static async Task<(string app, string[] args)> ResolveAsync()
    {
        if (!OperatingSystem.IsWindows())
            return ("claude", Array.Empty<string>());

        try
        {
            var npmRoot = await RunAsync("cmd", "/c npm root -g");
            npmRoot = npmRoot.Trim();
            var cliJs = Path.Combine(npmRoot, "@anthropic-ai", "claude-code", "cli.js");
            if (File.Exists(cliJs))
            {
                var node = FindNodeExe();
                return (node, new[] { cliJs });
            }
        }
        catch { }

        return ("claude", Array.Empty<string>());
    }

    public static string FindNodeExe()
    {
        if (!OperatingSystem.IsWindows()) return "node";
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var full = Path.Combine(dir.Trim(), "node.exe");
            if (File.Exists(full)) return full;
        }
        return "node";
    }

    private static async Task<string> RunAsync(string fileName, string args)
    {
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,   // prevents deadlock when npm emits warnings to stderr
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        return await p.StandardOutput.ReadToEndAsync();
    }
}
