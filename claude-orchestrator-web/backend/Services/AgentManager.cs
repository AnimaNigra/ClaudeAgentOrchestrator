using ClaudeOrchestrator.Models;

namespace ClaudeOrchestrator.Services;

public class AgentManager : IAsyncDisposable
{
    private readonly Dictionary<string, Agent> _agents = new();
    private readonly Dictionary<string, PtySession> _sessions = new();
    private readonly List<Func<string, string, object, Task>> _listeners = new();
    private readonly Dictionary<string, TaskCompletionSource<(bool approved, string? reason)>> _pendingPermissions = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly int _maxAgents;
    public readonly string OrchestratorUrl;
    private readonly AgentHistoryService? _historyService;

    public AgentManager(int maxAgents = 10, string orchestratorUrl = "http://localhost:5050",
        AgentHistoryService? historyService = null)
    {
        _maxAgents = maxAgents;
        OrchestratorUrl = orchestratorUrl;
        _historyService = historyService;
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

    public async Task MarkIdleAsync(string agentId)
    {
        if (!_agents.TryGetValue(agentId, out var agent)) return;
        if (agent.Status == AgentStatus.Running)
        {
            agent.Status = AgentStatus.Idle;
            await EmitEventAsync(agentId, "agent_status_changed", new { agentId, status = "idle" });
        }
    }

    public async Task NotifyAsync(string agentId, string message)
    {
        if (!_agents.ContainsKey(agentId)) return;
        await EmitEventAsync(agentId, "agent_notification", new { agentId, message });
    }

    public async Task UpdateUsageAsync(string agentId, int? contextPct, double? estimatedCost,
        int? rateLimitPct, string? rateLimitResetAt, string? modelName)
    {
        if (!_agents.TryGetValue(agentId, out var agent)) return;
        agent.ContextPct = contextPct;
        agent.EstimatedCost = estimatedCost;
        agent.RateLimitPct = rateLimitPct;
        agent.RateLimitResetAt = rateLimitResetAt;
        agent.ModelName = modelName;
        await EmitEventAsync(agentId, "agent_usage_updated", new
        {
            agentId,
            contextPct,
            estimatedCost,
            rateLimitPct,
            rateLimitResetAt,
            modelName,
        });
    }

    public async Task<(bool approved, string? reason)> RequestPermissionAsync(
        string agentId, string toolName, object? toolInput)
    {
        if (!_agents.TryGetValue(agentId, out var agent)) return (true, null);

        var requestId = Guid.NewGuid().ToString("N")[..8];
        var tcs = new TaskCompletionSource<(bool, string?)>();
        _pendingPermissions[requestId] = tcs;

        var prevStatus = agent.Status;
        agent.Status = AgentStatus.Blocked;
        await EmitEventAsync(agentId, "permission_request", new
        {
            requestId, agentId, toolName, toolInput,
            requestedAt = DateTime.UtcNow
        });

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            cts.Token.Register(() => tcs.TrySetResult((true, null))); // timeout → auto-approve
            return await tcs.Task;
        }
        finally
        {
            _pendingPermissions.Remove(requestId);
            if (_agents.TryGetValue(agentId, out var a) && a.Status == AgentStatus.Blocked)
            {
                a.Status = AgentStatus.Running;
                await EmitEventAsync(agentId, "agent_status_changed",
                    new { agentId, status = "running" });
            }
        }
    }

    public void RespondPermission(string requestId, bool approved, string? reason = null)
    {
        if (_pendingPermissions.TryGetValue(requestId, out var tcs))
            tcs.TrySetResult((approved, reason));
    }

    public async Task<Agent> SpawnAgentAsync(string name, string? cwd = null, string? resumeSessionId = null,
        string? worktreePath = null, string? worktreeBranch = null, string? originalCwd = null)
    {
        await _lock.WaitAsync();
        try
        {
            var agent = new Agent
            {
                Name = name, Cwd = cwd, ResumeSessionId = resumeSessionId,
                WorktreePath = worktreePath, WorktreeBranch = worktreeBranch, OriginalCwd = originalCwd,
            };
            var session = new PtySession(agent, EmitEventAsync, OrchestratorUrl);

            // Wire up history persistence + cleanup on natural exit
            session.OnExited = () =>
            {
                _agents.Remove(agent.Id);
                _sessions.Remove(agent.Id);
                if (_historyService is not null && agent.WorktreePath is null)
                    _ = _historyService.SaveAgentAsync(agent);
            };

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
            // Send /exit so Claude Code prints the --resume session ID before we kill it
            try
            {
                await session.WriteInputAsync("/exit\r");
                await Task.Delay(2000); // give Claude time to print resume instructions
            }
            catch { }

            await session.KillAsync();
            await session.DisposeAsync();
            _sessions.Remove(agentId);
        }

        if (_agents.TryGetValue(agentId, out var agent))
        {
            agent.Status = AgentStatus.Done;
            agent.FinishedAt = DateTime.UtcNow;
            if (_historyService is not null && agent.WorktreePath is null)
                await _historyService.SaveAgentAsync(agent);
            _agents.Remove(agentId);
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
