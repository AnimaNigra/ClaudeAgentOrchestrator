namespace ClaudeOrchestrator.Models;

public record AgentEvent(string AgentId, string EventType, object Data);

public record SpawnRequest(string Name, string? Cwd = null, string? ResumeSessionId = null);

public record KeystrokeRequest(string Data);

public record ResizeRequest(int Cols, int Rows);

public record PermissionRespondRequest(bool Approved, string? Reason = null);

public record CreateTaskRequest(string Title, string? Description = null, string? Prompt = null);

public record UpdateTaskRequest(
    string Title,
    string? Description = null,
    string? Prompt = null,
    string? Status = null,
    string? AgentId = null,
    string? AgentName = null);

public record AssignTaskRequest(string AgentId);

public record UpdateHistoryRequest(string? Notes = null);

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
