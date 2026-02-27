using System.Windows;
using System.Windows.Controls;
using ClaudeOrchestrator.WPF.Models;
using ClaudeOrchestrator.WPF.Services;

namespace ClaudeOrchestrator.WPF.Views;

public class HistoryRecordVM(AgentRecord record)
{
    public AgentRecord Record { get; } = record;
    public string Name => Record.Name;
    public string? Cwd => Record.Cwd;
    public string FinishedAtLabel => Record.FinishedAt?.ToLocalTime().ToString("dd.MM.yy HH:mm") ?? "";
    public Visibility ResumeVisible => Record.SessionId != null ? Visibility.Visible : Visibility.Collapsed;
}

public partial class HistoryWindow : Window
{
    private readonly AgentManager _agentManager;
    private readonly HistoryService _history;
    private List<HistoryRecordVM> _vms = [];

    public HistoryWindow(AgentManager agentManager, HistoryService history)
    {
        InitializeComponent();
        _agentManager = agentManager;
        _history = history;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var records = await _history.GetAllAsync();
        _vms = records.Select(r => new HistoryRecordVM(r)).ToList();
        RecordsList.ItemsSource = _vms;
    }

    private async void ResumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not HistoryRecordVM vm) return;
        var r = vm.Record;
        await _agentManager.SpawnAsync(r.Name, r.Cwd, r.SessionId);
        Close();
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not HistoryRecordVM vm) return;
        await _history.DeleteAsync(vm.Record.Id);
        _vms.Remove(vm);
        RecordsList.ItemsSource = null;
        RecordsList.ItemsSource = _vms;
    }
}
