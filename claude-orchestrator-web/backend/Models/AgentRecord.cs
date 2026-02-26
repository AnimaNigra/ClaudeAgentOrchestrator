namespace ClaudeOrchestrator.Models;

public class AgentRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Cwd { get; set; }
    public string? SessionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public List<string> TaskIds { get; set; } = new();
}
