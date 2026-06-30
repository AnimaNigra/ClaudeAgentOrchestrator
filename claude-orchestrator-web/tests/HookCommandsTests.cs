using ClaudeOrchestrator.Services;

namespace ClaudeOrchestrator.Tests;

public class HookCommandsTests
{
    private static Dictionary<string, string> Hooks()
        => PtySession.BuildOrchestratorHooks("http://localhost:6001/api/agents/abc")
                     .ToDictionary(h => h.Event, h => h.Command);

    [Fact]
    public void Includes_AllRequiredEvents()
    {
        var h = Hooks();
        Assert.Contains("Stop", h.Keys);
        Assert.Contains("Notification", h.Keys);
        Assert.Contains("PreToolUse", h.Keys);
        Assert.Contains("UserPromptSubmit", h.Keys);
        Assert.Contains("SessionStart", h.Keys);
    }

    [Fact]
    public void UserPrompt_And_SessionStart_PostToCorrectEndpoints()
    {
        var h = Hooks();
        Assert.Contains("/hook/user-prompt", h["UserPromptSubmit"]);
        Assert.Contains("/hook/session-start", h["SessionStart"]);
        Assert.Contains("--data-binary @-", h["UserPromptSubmit"]);
        Assert.Contains("--data-binary @-", h["SessionStart"]);
    }

    [Fact]
    public void Stop_ForwardsPayload()
    {
        var h = Hooks();
        Assert.Contains("/hook/stop", h["Stop"]);
        Assert.Contains("--data-binary @-", h["Stop"]);   // now sends transcript_path
    }
}
