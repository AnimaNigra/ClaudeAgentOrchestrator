namespace ClaudeOrchestrator.WPF.Models;

public class AgentRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Cwd { get; set; }
    public string? SessionId { get; set; }
    public string? Notes { get; set; }
    public DateTime? FinishedAt { get; set; }
}
