using ClaudeOrchestrator.Models;
using ClaudeOrchestrator.Services;

namespace ClaudeOrchestrator.Tests;

public class ConversationHistoryServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chs-" + Guid.NewGuid().ToString("N"));
    private ConversationHistoryService New(HistoryOptions? o = null)
        => new(_dir, o ?? new HistoryOptions());
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static Agent A(string name = "Bob", string? cwd = @"C:\proj")
        => new() { Name = name, Cwd = cwd };

    [Fact]
    public void ResolveAgentDir_CreatesDirAndIsStable()
    {
        var svc = New();
        var a = A();
        var d1 = svc.ResolveAgentDir(a);
        var d2 = svc.ResolveAgentDir(a);
        Assert.Equal(d1, d2);
        Assert.True(Directory.Exists(d1));
        Assert.EndsWith("Bob", d1);
    }

    [Fact]
    public void ResolveAgentDir_SuffixesOnNameCollisionWithDifferentCwd()
    {
        var svc = New();
        var d1 = svc.ResolveAgentDir(new Agent { Id = "1", Name = "Bob", Cwd = @"C:\a" });
        var d2 = svc.ResolveAgentDir(new Agent { Id = "2", Name = "Bob", Cwd = @"C:\b" });
        Assert.NotEqual(d1, d2);
    }

    [Fact]
    public async Task AppendUserPromptAsync_WritesHeaderThenUserBlock()
    {
        var svc = New();
        var a = A();
        await svc.AppendUserPromptAsync(a, "oprav ten bug");

        var md = await File.ReadAllTextAsync(Path.Combine(svc.ResolveAgentDir(a), "history.md"));
        Assert.Contains("# Konverzace — Bob", md);
        Assert.Contains("### 🧑 Uživatel", md);
        Assert.Contains("oprav ten bug", md);
    }

    [Fact]
    public async Task AppendUserPromptAsync_DoesNotRewriteHeaderTwice()
    {
        var svc = New();
        var a = A();
        await svc.AppendUserPromptAsync(a, "first");
        await svc.AppendUserPromptAsync(a, "second");

        var md = await File.ReadAllTextAsync(Path.Combine(svc.ResolveAgentDir(a), "history.md"));
        Assert.Single(Split(md, "# Konverzace — Bob"));   // header appears exactly once
        Assert.Contains("first", md);
        Assert.Contains("second", md);
    }

    [Fact]
    public async Task Disabled_Capture_NoOp()
    {
        var svc = New(new HistoryOptions { ConversationCapture = false });
        var a = A();
        await svc.AppendUserPromptAsync(a, "x");
        Assert.False(File.Exists(Path.Combine(svc.ResolveAgentDir(a), "history.md")));
    }

    private static string[] Split(string s, string sep)
        => s.Split(new[] { sep }, StringSplitOptions.None).Skip(1).ToArray();
}
