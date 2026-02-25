using Microsoft.AspNetCore.SignalR;
using ClaudeOrchestrator.Services;

namespace ClaudeOrchestrator.Hubs;

public class AgentHub : Hub
{
    private readonly AgentManager _manager;

    public AgentHub(AgentManager manager)
    {
        _manager = manager;
    }

    public override async Task OnConnectedAsync()
    {
        // Send current agents state on connect
        var agents = _manager.ListAgents();
        await Clients.Caller.SendAsync("InitialState", agents);
        await base.OnConnectedAsync();
    }
}
