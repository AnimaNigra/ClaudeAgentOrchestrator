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

    private async Task<string> WriteTranscript(params string[] lines)
    {
        Directory.CreateDirectory(_dir);
        var p = Path.Combine(_dir, "t-" + Guid.NewGuid().ToString("N") + ".jsonl");
        await File.WriteAllLinesAsync(p, lines);
        return p;
    }

    private const string AText =
        """{"type":"assistant","uuid":"a1","message":{"role":"assistant","content":[{"type":"text","text":"hotovo"}]}}""";
    private const string AText2 =
        """{"type":"assistant","uuid":"a2","message":{"role":"assistant","content":[{"type":"text","text":"a ještě tohle"}]}}""";

    [Fact]
    public async Task AppendAssistantTurn_WritesClaudeBlock_AndAdvancesMarker()
    {
        var svc = New();
        var a = A();
        var tp = await WriteTranscript(AText);
        await svc.AppendAssistantTurnAsync(a, tp);

        var md = await File.ReadAllTextAsync(Path.Combine(svc.ResolveAgentDir(a), "history.md"));
        Assert.Contains("### 🤖 Claude", md);
        Assert.Contains("hotovo", md);
    }

    [Fact]
    public async Task AppendAssistantTurn_NoDuplicate_OnSecondCall()
    {
        var svc = New();
        var a = A();
        var tp = await WriteTranscript(AText);
        await svc.AppendAssistantTurnAsync(a, tp);
        await svc.AppendAssistantTurnAsync(a, tp);   // same transcript, nothing new

        var md = await File.ReadAllTextAsync(Path.Combine(svc.ResolveAgentDir(a), "history.md"));
        Assert.Single(md.Split("hotovo").Skip(1).ToArray());

        // append a new line to the transcript → only the new turn is added
        await File.AppendAllLinesAsync(tp, new[] { AText2 });
        await svc.AppendAssistantTurnAsync(a, tp);
        md = await File.ReadAllTextAsync(Path.Combine(svc.ResolveAgentDir(a), "history.md"));
        Assert.Contains("a ještě tohle", md);
        Assert.Single(md.Split("hotovo").Skip(1).ToArray());   // still only once
    }

    [Fact]
    public async Task AppendAssistantTurn_TrimsToMaxTurns()
    {
        var svc = New(new HistoryOptions { MaxTurns = 2 });
        var a = A();
        await svc.AppendUserPromptAsync(a, "q1");
        await svc.AppendAssistantTurnAsync(a, await WriteTranscript(AText));
        await svc.AppendUserPromptAsync(a, "q2");

        var md = await File.ReadAllTextAsync(Path.Combine(svc.ResolveAgentDir(a), "history.md"));
        Assert.DoesNotContain("q1", md);   // oldest turn trimmed
        Assert.Contains("q2", md);
        Assert.StartsWith("# Konverzace", md);  // header preserved
    }

    [Fact]
    public void CreateTerminalLogWriter_NullWhenDisabled()
    {
        var svc = New(new HistoryOptions { RawTerminalLog = false });
        Assert.Null(svc.CreateTerminalLogWriter(A()));
    }

    [Fact]
    public async Task CreateTerminalLogWriter_WritesIntoAgentDir()
    {
        var svc = New();
        var a = A();
        var w = svc.CreateTerminalLogWriter(a);
        Assert.NotNull(w);
        w!.Write(System.Text.Encoding.UTF8.GetBytes("raw"));
        await w.DisposeAsync();
        Assert.True(File.Exists(Path.Combine(svc.ResolveAgentDir(a), "terminal.log")));
    }
}
