namespace ClaudeOrchestrator.Models;

public class PriorityItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public int Order { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public record CreatePriorityRequest(string Text);
public record UpdatePriorityRequest(string? Text = null, bool? Done = null);
public record ReorderPriorityItem(string Id, int Order);
