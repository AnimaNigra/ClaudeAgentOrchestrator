using ClaudeOrchestrator.Models;
using ClaudeOrchestrator.Services;

namespace ClaudeOrchestrator.Tests;

public class SessionIdCaptureTests
{
    [Fact]
    public void TryAdopt_SetsWhenEmpty()
    {
        var a = new Agent { SessionId = "" };
        Assert.True(AgentManager.TryAdoptSessionId(a, "sess-9"));
        Assert.Equal("sess-9", a.SessionId);
    }

    [Fact]
    public void TryAdopt_IgnoresWhenAlreadySet()
    {
        var a = new Agent { SessionId = "existing" };
        Assert.False(AgentManager.TryAdoptSessionId(a, "sess-9"));
        Assert.Equal("existing", a.SessionId);
    }

    [Fact]
    public void TryAdopt_IgnoresEmptyIncoming()
    {
        var a = new Agent { SessionId = "" };
        Assert.False(AgentManager.TryAdoptSessionId(a, null));
        Assert.False(AgentManager.TryAdoptSessionId(a, ""));
    }

    [Fact]
    public void TryAdopt_IgnoresWorktreeAgents()
    {
        var a = new Agent { SessionId = "", WorktreePath = @"C:\proj-wt-x" };
        Assert.False(AgentManager.TryAdoptSessionId(a, "sess-9"));
    }
}
