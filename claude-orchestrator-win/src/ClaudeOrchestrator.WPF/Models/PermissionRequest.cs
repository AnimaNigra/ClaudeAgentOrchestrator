namespace ClaudeOrchestrator.WPF.Models;

public class PermissionRequest
{
    public string RequestId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public object? ToolInput { get; set; }
}
