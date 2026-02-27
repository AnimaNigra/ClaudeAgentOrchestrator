using System.Windows;
using ClaudeOrchestrator.WPF.Services;

namespace ClaudeOrchestrator.WPF;

public partial class App : Application
{
    public HistoryService HistoryService { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
    }
}
