namespace ClaudeOrchestrator.Models;

public record HistoryOptions
{
    public bool ConversationCapture { get; init; } = true;
    public int MaxTurns { get; init; } = 200;
    public bool IncludeTools { get; init; } = true;
    public bool RawTerminalLog { get; init; } = true;
    public int ToolResultMaxLines { get; init; } = 100;
}
