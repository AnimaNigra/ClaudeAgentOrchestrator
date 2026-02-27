using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using ClaudeOrchestrator.WPF.Models;

namespace ClaudeOrchestrator.WPF.Services;

public class AgentManager
{
    private readonly string _orchestratorUrl;
    private readonly HistoryService _history;
    private readonly Dictionary<string, (PtySessionService Session, HooksInjector Injector)> _sessions = new();

    public ObservableCollection<Agent> Agents { get; } = new();

    public event Action<Agent, AgentStatus>? AgentStatusChanged;
    public event Action<Agent>? AgentExited;
    public event Action<string>? AgentIdled;  // agentId

    public AgentManager(string orchestratorUrl, HistoryService history)
    {
        _orchestratorUrl = orchestratorUrl;
        _history = history;
    }

    public async Task<Agent> SpawnAsync(string name, string? cwd, string? resumeSessionId = null)
    {
        var agent = new Agent
        {
            Name = name,
            Cwd = cwd,
            ResumeSessionId = resumeSessionId,
        };

        var injector = new HooksInjector(agent.Id, _orchestratorUrl);
        await injector.InjectAsync(cwd ?? Directory.GetCurrentDirectory());

        var session = new PtySessionService(agent, _orchestratorUrl);
        _sessions[agent.Id] = (session, injector);

        session.StatusChanged += status =>
        {
            agent.Status = status;
            Application.Current?.Dispatcher.Invoke(() => RefreshAgentList());
            AgentStatusChanged?.Invoke(agent, status);
            if (status == AgentStatus.Idle)
                AgentIdled?.Invoke(agent.Id);
        };

        session.Exited += async () =>
        {
            await _history.SaveAsync(new AgentRecord
            {
                Id = agent.Id,
                Name = agent.Name,
                Cwd = agent.Cwd,
                SessionId = agent.SessionId,
                FinishedAt = agent.FinishedAt,
            });
            await injector.RemoveAsync();

            await Task.Delay(2000);
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Agents.Remove(agent);
                _sessions.Remove(agent.Id);
            });

            AgentExited?.Invoke(agent);
        };

        await session.StartAsync();

        Application.Current?.Dispatcher.Invoke(() => Agents.Add(agent));
        return agent;
    }

    public PtySessionService? GetSession(string agentId)
        => _sessions.TryGetValue(agentId, out var s) ? s.Session : null;

    public async Task KillAsync(string agentId)
    {
        if (_sessions.TryGetValue(agentId, out var s))
            await s.Session.DisposeAsync();
    }

    private void RefreshAgentList()
    {
        // Trigger CollectionChanged for bindings to update StatusLabel.
        // ObservableCollection doesn't notify on property changes, so we force refresh
        // by replacing items in place. Bindings will update if Agent implements
        // INotifyPropertyChanged. For now the UI refreshes on status change events.
        // No-op body — the StatusChanged callback already triggers any needed UI refresh.
    }
}
