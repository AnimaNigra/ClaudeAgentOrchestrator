using ClaudeOrchestrator.Models;

namespace ClaudeOrchestrator.Services;

public class AgentManager : IAsyncDisposable
{
    private readonly Dictionary<string, Agent> _agents = new();
    private readonly Dictionary<string, PtySession> _sessions = new();
    private readonly List<Func<string, string, object, Task>> _listeners = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly int _maxAgents;

    public AgentManager(int maxAgents = 10)
    {
        _maxAgents = maxAgents;
    }

    public void AddEventListener(Func<string, string, object, Task> listener)
        => _listeners.Add(listener);

    private async Task EmitEventAsync(string agentId, string eventType, object data)
    {
        foreach (var listener in _listeners)
        {
            try { await listener(agentId, eventType, data); }
            catch { }
        }
    }

    public async Task<Agent> SpawnAgentAsync(string name, string? cwd = null)
    {
        await _lock.WaitAsync();
        try
        {
            var agent = new Agent { Name = name, Cwd = cwd };
            var session = new PtySession(agent, EmitEventAsync);

            _agents[agent.Id] = agent;
            _sessions[agent.Id] = session;

            await session.StartAsync();

            await EmitEventAsync(agent.Id, "agent_spawned", new
            {
                id = agent.Id,
                name = agent.Name,
                cwd = agent.Cwd,
                status = agent.Status.ToString().ToLower()
            });

            return agent;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task WriteInputAsync(string agentId, string data)
    {
        if (!_sessions.TryGetValue(agentId, out var session))
            throw new KeyNotFoundException($"Agent {agentId} not found");
        await session.WriteInputAsync(data);
    }

    public async Task ResizePtyAsync(string agentId, int cols, int rows)
    {
        if (_sessions.TryGetValue(agentId, out var session))
            await session.ResizeAsync(cols, rows);
    }

    public async Task KillAgentAsync(string agentId)
    {
        if (_sessions.TryGetValue(agentId, out var session))
        {
            await session.KillAsync();
            await session.DisposeAsync();
            _sessions.Remove(agentId);
        }

        if (_agents.TryGetValue(agentId, out var agent))
        {
            agent.Status = AgentStatus.Done;
            agent.FinishedAt = DateTime.UtcNow;
            await EmitEventAsync(agentId, "agent_killed", new { });
        }
    }

    public IReadOnlyList<Agent> ListAgents() => _agents.Values.ToList();
    public Agent? GetAgent(string agentId) => _agents.GetValueOrDefault(agentId);
    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values)
            await session.DisposeAsync();
        _lock.Dispose();
    }
}
