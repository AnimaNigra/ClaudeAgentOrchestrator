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
    string Status = "todo",
    string? AgentId = null,
    string? AgentName = null);

public record AssignTaskRequest(string AgentId);
