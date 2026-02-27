using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClaudeOrchestrator.WPF.Models;
using ClaudeOrchestrator.WPF.Services;
using ClaudeOrchestrator.WPF.Views;

namespace ClaudeOrchestrator.WPF;

public partial class MainWindow : Window
{
    private readonly AgentManager _agentManager;
    private readonly HookServer _hookServer;
    private Agent? _activeAgent;
    private readonly Dictionary<string, TerminalView> _terminalViews = new();

    public MainWindow(AgentManager agentManager, HookServer hookServer)
    {
        InitializeComponent();
        _agentManager = agentManager;
        _hookServer = hookServer;

        AgentsList.ItemsSource = _agentManager.Agents;

        _agentManager.AgentStatusChanged += OnAgentStatusChanged;
        _agentManager.AgentExited += OnAgentExited;
        _agentManager.AgentIdled += agentId => Dispatcher.Invoke(() => OnAgentIdle(agentId));

        hookServer.AgentStopped += agentId => Dispatcher.Invoke(() => OnAgentIdle(agentId));
        hookServer.PermissionRequested += OnPermissionRequested;
    }

    private void OnAgentStatusChanged(Agent agent, AgentStatus status)
    {
        Dispatcher.Invoke(() => {
            // Force ItemsControl to re-evaluate bindings for status label
            var idx = _agentManager.Agents.IndexOf(agent);
            if (idx >= 0)
            {
                _agentManager.Agents.RemoveAt(idx);
                _agentManager.Agents.Insert(idx, agent);
            }
        });
    }

    private void OnAgentExited(Agent agent)
    {
        Dispatcher.Invoke(() => {
            _terminalViews.Remove(agent.Id);
            if (_activeAgent?.Id == agent.Id)
            {
                _activeAgent = _agentManager.Agents.FirstOrDefault();
                ShowTerminal(_activeAgent);
            }
        });
    }

    private void OnAgentIdle(string agentId)
    {
        System.Media.SystemSounds.Asterisk.Play();
    }

    private async Task<bool> OnPermissionRequested(PermissionRequest req)
    {
        return await Dispatcher.InvokeAsync(() => {
            var dialog = new PermissionDialog(req) { Owner = this };
            return dialog.ShowDialog() == true;
        });
    }

    private void ShowTerminal(Agent? agent)
    {
        if (agent is null) { TerminalHost.Content = null; return; }

        if (!_terminalViews.TryGetValue(agent.Id, out var tv))
        {
            tv = new TerminalView();
            var session = _agentManager.GetSession(agent.Id);
            if (session != null) tv.Attach(session);
            _terminalViews[agent.Id] = tv;
        }

        TerminalHost.Content = tv;
        tv.Focus();
    }

    private void AgentCard_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Agent agent)
        {
            _activeAgent = agent;
            ShowTerminal(agent);
        }
    }

    private async void SpawnButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NewAgentInput.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;
        NewAgentInput.Clear();

        var agent = await _agentManager.SpawnAsync(name, null);
        _activeAgent = agent;
        ShowTerminal(agent);
    }

    private void NewAgentInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) SpawnButton_Click(sender, e);
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        var hw = new HistoryWindow(_agentManager, app.HistoryService) { Owner = this };
        hw.Show();
    }
}
