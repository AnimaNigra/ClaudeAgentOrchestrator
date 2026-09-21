namespace ClaudeOrchestrator.Models;

public record HistoryOptions
{
    public bool ConversationCapture { get; init; } = true;
    public int MaxTurns { get; init; } = 200;
    public bool IncludeTools { get; init; } = true;
    public bool RawTerminalLog { get; init; } = true;
    /// <summary>Cap per terminal.log before it rotates to .1; disk use per agent is ~2× this.</summary>
    public int TerminalLogMaxMB { get; init; } = 50;
    public int ToolResultMaxLines { get; init; } = 100;
}
