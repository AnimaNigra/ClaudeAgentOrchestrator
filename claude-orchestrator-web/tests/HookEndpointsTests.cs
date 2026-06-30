using System.Text.Json;
using ClaudeOrchestrator.Controllers;
using ClaudeOrchestrator.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudeOrchestrator.Tests;

public class HookEndpointsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hep-" + Guid.NewGuid().ToString("N"));
    private readonly ConversationHistoryService _hist;
    private readonly AgentsController _ctrl;

    public HookEndpointsTests()
    {
        _hist = new ConversationHistoryService(_dir, new ClaudeOrchestrator.Models.HistoryOptions());
        _ctrl = new AgentsController(new AgentManager());
    }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public async Task UserPrompt_UnknownAgent_IsOkNoThrow()
    {
        var body = Json("""{"prompt":"hi","session_id":"s","transcript_path":"x"}""");
        var res = await _ctrl.HookUserPrompt("nope", body, _hist);
        Assert.IsType<OkResult>(res);
    }

    [Fact]
    public async Task SessionStart_Clear_UnknownAgent_IsOkNoThrow()
    {
        var body = Json("""{"source":"clear","session_id":"s"}""");
        var res = await _ctrl.HookSessionStart("nope", body, _hist);
        Assert.IsType<OkResult>(res);
    }

    [Fact]
    public async Task Stop_WithPayload_IsOk()
    {
        var body = Json("""{"session_id":"s","transcript_path":"x","stop_hook_active":false}""");
        var res = await _ctrl.HookStop("nope", body, _hist);
        Assert.IsType<OkResult>(res);
    }
}
