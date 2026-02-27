using System.Text.Json;
using System.Windows;
using ClaudeOrchestrator.WPF.Models;

namespace ClaudeOrchestrator.WPF.Views;

public partial class PermissionDialog : Window
{
    public PermissionDialog(PermissionRequest req)
    {
        InitializeComponent();
        ToolNameRun.Text = req.ToolName;
        if (req.ToolInput != null)
        {
            InputBox.Text = JsonSerializer.Serialize(req.ToolInput,
                new JsonSerializerOptions { WriteIndented = true });
        }
        else
            InputBox.Visibility = Visibility.Collapsed;
    }

    private void ApproveButton_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void DenyButton_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
