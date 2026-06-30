using ClaudeOrchestrator.Models;
using ClaudeOrchestrator.Services;

namespace ClaudeOrchestrator.Tests;

public class TranscriptRenderTests
{
    private static readonly HistoryOptions Full = new();         // IncludeTools = true
    private static readonly HistoryOptions NoTools = new() { IncludeTools = false };

    // Realistic Claude Code transcript jsonl lines (one JSON object per line).
    private static readonly string UserLine =
        """{"type":"user","uuid":"u1","parentUuid":null,"sessionId":"s","message":{"role":"user","content":"oprav bug"}}""";
    private static readonly string AssistantText =
        """{"type":"assistant","uuid":"a1","parentUuid":"u1","message":{"role":"assistant","content":[{"type":"text","text":"Jasně, mrknu na to."}]}}""";
    private static readonly string AssistantTool =
        """{"type":"assistant","uuid":"a2","parentUuid":"a1","message":{"role":"assistant","content":[{"type":"tool_use","id":"t1","name":"Edit","input":{"file_path":"x.cs"}}]}}""";
    private static readonly string ToolResult =
        """{"type":"user","uuid":"r1","parentUuid":"a2","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t1","content":"File edited"}]}}""";

    [Fact]
    public void RenderNewTurns_SkipsUserPrompt_RendersAssistantText()
    {
        var r = HistoryFormat.RenderNewTurns(new[] { UserLine, AssistantText }, null, Full);
        Assert.DoesNotContain("oprav bug", r.Markdown);          // user prompt skipped
        Assert.Contains("### 🤖 Claude", r.Markdown);
        Assert.Contains("Jasně, mrknu na to.", r.Markdown);
        Assert.Equal("a1", r.LastUuid);                          // marker reached the end
    }

    [Fact]
    public void RenderNewTurns_RespectsAfterUuid()
    {
        var r = HistoryFormat.RenderNewTurns(new[] { UserLine, AssistantText }, "u1", Full);
        Assert.Contains("Jasně", r.Markdown);
        Assert.Equal("a1", r.LastUuid);

        var none = HistoryFormat.RenderNewTurns(new[] { UserLine, AssistantText }, "a1", Full);
        Assert.Equal("", none.Markdown);
        Assert.Equal("a1", none.LastUuid);
    }

    [Fact]
    public void RenderNewTurns_RendersToolUseAndResult_WhenIncludeTools()
    {
        var r = HistoryFormat.RenderNewTurns(new[] { AssistantTool, ToolResult }, null, Full);
        Assert.Contains("🔧 **Edit**", r.Markdown);
        Assert.Contains("x.cs", r.Markdown);
        Assert.Contains("File edited", r.Markdown);
        Assert.Equal("r1", r.LastUuid);
    }

    [Fact]
    public void RenderNewTurns_OmitsTools_WhenDisabled()
    {
        var r = HistoryFormat.RenderNewTurns(new[] { AssistantTool, ToolResult }, null, NoTools);
        Assert.DoesNotContain("🔧", r.Markdown);
        Assert.DoesNotContain("File edited", r.Markdown);
        Assert.Equal("r1", r.LastUuid);                          // still advances marker
    }

    [Fact]
    public void RenderNewTurns_TruncatesLongToolResult()
    {
        var big = string.Join("\\n", Enumerable.Range(0, 50).Select(i => $"line{i}"));
        var line =
            $$$"""{"type":"user","uuid":"r2","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t","content":"{{{big}}}"}]}}""";
        var opts = new HistoryOptions { ToolResultMaxLines = 5 };
        var r = HistoryFormat.RenderNewTurns(new[] { line }, null, opts);
        Assert.Contains("line0", r.Markdown);
        Assert.DoesNotContain("line49", r.Markdown);
        Assert.Contains("(zkráceno)", r.Markdown);
    }

    [Fact]
    public void RenderNewTurns_IgnoresMalformedLines()
    {
        var r = HistoryFormat.RenderNewTurns(new[] { "{not json", "", AssistantText }, null, Full);
        Assert.Contains("Jasně", r.Markdown);
        Assert.Equal("a1", r.LastUuid);
    }

    [Fact]
    public void RenderNewTurns_AfterUuidNotFound_RendersNothing_KeepsMarker()
    {
        var r = HistoryFormat.RenderNewTurns(new[] { AssistantText }, "nonexistent", Full);
        Assert.Equal("", r.Markdown);
        Assert.Equal("nonexistent", r.LastUuid);
    }
}
