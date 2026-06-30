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
            }).Build();

        var svc = new ConversationHistoryService(config);
        Assert.Equal(55, svc.Options.MaxTurns);
        Assert.False(svc.Options.IncludeTools);
        Assert.True(svc.Options.ConversationCapture);   // default preserved
    }
}
