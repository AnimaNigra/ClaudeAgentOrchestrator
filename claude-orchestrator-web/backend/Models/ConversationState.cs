namespace ClaudeOrchestrator.Models;

public class ConversationState
{
    public string AgentId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Cwd { get; set; }
    public string? SessionId { get; set; }
    public string? TranscriptPath { get; set; }
    public string? LastMessageUuid { get; set; }
    public int TurnCount { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
