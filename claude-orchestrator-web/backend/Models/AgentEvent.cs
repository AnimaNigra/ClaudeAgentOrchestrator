namespace ClaudeOrchestrator.Models;

public record AgentEvent(string AgentId, string EventType, object Data);

public record SpawnRequest(string Name, string? Cwd = null);

public record KeystrokeRequest(string Data);

public record ResizeRequest(int Cols, int Rows);

public record PermissionRespondRequest(bool Approved, string? Reason = null);
