namespace ClaudeOrchestrator.WPF.Models;

public enum AgentStatus { Running, Idle, Done }

public class Agent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string? Cwd { get; set; }
    public string? ResumeSessionId { get; set; }
    public AgentStatus Status { get; set; } = AgentStatus.Running;
    public int? Pid { get; set; }
    public string? SessionId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

    public string StatusLabel => Status switch
    {
        AgentStatus.Running => "● Running",
        AgentStatus.Idle    => "⏳ Waiting",
        AgentStatus.Done    => "✓ Done",
        _ => ""
    };
}
