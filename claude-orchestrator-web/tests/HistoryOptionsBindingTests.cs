using ClaudeOrchestrator.Services;
using Microsoft.Extensions.Configuration;

namespace ClaudeOrchestrator.Tests;

public class HistoryOptionsBindingTests
{
    [Fact]
    public void DiConstructor_BindsHistorySection()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataDir"] = Path.Combine(Path.GetTempPath(), "bind-" + Guid.NewGuid().ToString("N")),
                ["History:MaxTurns"] = "55",
                ["History:IncludeTools"] = "false",
                ["History:TerminalLogMaxMB"] = "7",
            }).Build();

        var svc = new ConversationHistoryService(config);
        Assert.Equal(55, svc.Options.MaxTurns);
        Assert.False(svc.Options.IncludeTools);
        Assert.Equal(7, svc.Options.TerminalLogMaxMB);
        Assert.True(svc.Options.ConversationCapture);   // default preserved
    }
}
