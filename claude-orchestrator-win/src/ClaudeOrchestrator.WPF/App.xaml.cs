using System.Windows;
using ClaudeOrchestrator.WPF.Services;

namespace ClaudeOrchestrator.WPF;

public partial class App : Application
{
    public HistoryService HistoryService { get; } = new();
    public AgentManager AgentManager { get; private set; } = null!;
    public HookServer HookServer { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        const string orchestratorUrl = "http://localhost:5050";
        AgentManager = new AgentManager(orchestratorUrl, HistoryService);
        HookServer = new HookServer(AgentManager, port: 5050);
        try
        {
            HookServer.Start();
        }
        catch (System.Net.HttpListenerException ex)
        {
            System.Windows.MessageBox.Show(
                $"Cannot start HTTP listener on port 5050.\n{ex.Message}\n\nClose any other running instance and try again.",
                "Startup Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var mainWindow = new MainWindow(AgentManager, HookServer);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        HookServer?.Dispose();
        base.OnExit(e);
    }
}
