namespace ClaudeOrchestrator.Models;

public enum AgentStatus
{
    Idle,
    Running,
    Done,
    Error,
    Blocked
}

public class Agent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public AgentStatus Status { get; set; } = AgentStatus.Idle;
    public string LastMessage { get; set; } = "";
    public int ProgressPct { get; set; } = -1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public string SessionId { get; set; } = "";
    public int? Pid { get; set; }
    public string? Cwd { get; set; }
    public string? ResumeSessionId { get; set; }
    public string? WorktreePath { get; set; }
    public string? WorktreeBranch { get; set; }
    public string? OriginalCwd { get; set; }

    public string ElapsedStr
    {
        get
        {
            var elapsed = (FinishedAt ?? DateTime.UtcNow) - CreatedAt;
            var total = (int)elapsed.TotalSeconds;
            var h = total / 3600;
            var m = (total % 3600) / 60;
            var s = total % 60;
            if (h > 0) return $"{h}h {m}m {s}s";
            if (m > 0) return $"{m}m {s}s";
            return $"{s}s";
        }
    }
}
