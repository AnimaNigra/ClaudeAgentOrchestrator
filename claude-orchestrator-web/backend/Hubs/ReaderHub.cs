using Microsoft.AspNetCore.SignalR;

namespace ClaudeOrchestrator.Hubs;

public class ReaderHub : Hub
{
    public override Task OnConnectedAsync() => base.OnConnectedAsync();
}
