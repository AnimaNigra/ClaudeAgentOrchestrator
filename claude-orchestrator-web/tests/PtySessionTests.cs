using System.Text.RegularExpressions;

namespace ClaudeOrchestrator.Tests;

/// <summary>
/// Tests for PtySession's state-detection logic.
/// The regex patterns are tested here as static helpers to keep them verifiable
/// without starting a real PTY process.
/// </summary>
public class PtySessionIdleDetectionTests
{
    // Mirror of the patterns inside PtySession (kept in sync manually)
    private static readonly Regex AnsiStrip = new(
        @"\x1B(?:\[[0-9;?]*[A-Za-z]|\][^\x07]*\x07|.)",
        RegexOptions.Compiled);

    private static readonly Regex IdlePrompt = new(
        @"[│|]\s{0,4}[>›]",
        RegexOptions.Compiled);

    private static string Strip(string s) => AnsiStrip.Replace(s, "");
    private static bool IsIdle(string raw) => IdlePrompt.IsMatch(Strip(raw));

    // ── Claude idle-prompt detection ─────────────────────────────────────

    [Fact]
    public void IdlePrompt_MatchesClaude_BoxDrawingPrompt()
    {
        // Claude's TUI input box: │ >
        var sample = "│ > some text";
        Assert.True(IsIdle(sample));
    }

    [Fact]
    public void IdlePrompt_MatchesGuillemet_Arrow()
    {
        var sample = "│ › ";
        Assert.True(IsIdle(sample));
    }

    [Fact]
    public void IdlePrompt_DoesNotMatchMidOutput()
    {
        // A line in the middle of output that has no input prompt
        var sample = "Here is the answer to your question.";
        Assert.False(IsIdle(sample));
    }

    [Fact]
    public void IdlePrompt_DoesNotMatchSpinnerLine()
    {
        // Typical "thinking" spinner — no prompt box
        var sample = "⠋ Thinking...";
        Assert.False(IsIdle(sample));
    }

    [Fact]
    public void IdlePrompt_MatchesAfterAnsiStrip()
    {
        // ANSI-coloured box-drawing prompt
        var sample = "\x1B[32m│\x1B[0m \x1B[1m>\x1B[0m ";
        Assert.True(IsIdle(sample));
    }

    // ── ANSI stripping ───────────────────────────────────────────────────

    [Fact]
    public void AnsiStrip_RemovesSGR()
    {
        var result = Strip("\x1B[1;32mGreen Bold\x1B[0m");
        Assert.Equal("Green Bold", result);
    }

    [Fact]
    public void AnsiStrip_RemovesCursorMoves()
    {
        var result = Strip("\x1B[2A\x1B[10CHello");
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void AnsiStrip_LeavesPlainTextAlone()
    {
        var result = Strip("plain text");
        Assert.Equal("plain text", result);
    }

    [Fact]
    public void AnsiStrip_RemovesOSCTitle()
    {
        // OSC sequence: ESC ] ... BEL
        var result = Strip("\x1B]0;window title\x07plain");
        Assert.Equal("plain", result);
    }
}
