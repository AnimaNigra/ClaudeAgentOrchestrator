namespace ClaudeOrchestrator.Models;

public class TaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Prompt { get; set; }
    public string Status { get; set; } = "todo"; // todo | in-progress | done
    public string? AgentId { get; set; }
    public string? AgentName { get; set; }
    public List<string> Attachments { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
