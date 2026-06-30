using ClaudeOrchestrator.Services;

namespace ClaudeOrchestrator.Tests;

public class HistoryFormatTests
{
    [Theory]
    [InlineData("My Agent", "My Agent")]
    [InlineData("feat/x:y*z?", "feat-x-y-z-")]
    [InlineData("  trailing dots..", "trailing dots")]
    [InlineData("", "agent")]
    public void SanitizeDirName_StripsInvalidChars(string input, string expected)
    {
        Assert.Equal(expected, HistoryFormat.SanitizeDirName(input));
    }

    [Fact]
    public void RenderUserPrompt_WrapsInUserHeader()
    {
        var md = HistoryFormat.RenderUserPrompt("  oprav bug\n");
        Assert.Contains("### 🧑 Uživatel", md);
        Assert.Contains("oprav bug", md);
        Assert.DoesNotContain("oprav bug\n\n\n", md); // trimmed tail
    }

    [Fact]
    public void Header_ContainsMetadata()
    {
        var md = HistoryFormat.Header("Bob", @"C:\proj", "sess-1",
            new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc));
        Assert.Contains("Bob", md);
        Assert.Contains(@"C:\proj", md);
        Assert.Contains("sess-1", md);
        Assert.StartsWith("# ", md);
    }

    [Fact]
    public void TrimToLastTurns_KeepsHeaderAndLastNTurns()
    {
        var md = "# Konverzace\n\nmeta\n"
            + "### 🧑 Uživatel\n\nq1\n"
            + "### 🤖 Claude\n\na1\n"
            + "### 🧑 Uživatel\n\nq2\n"
            + "### 🤖 Claude\n\na2\n";
        var trimmed = HistoryFormat.TrimToLastTurns(md, maxTurns: 2);
        Assert.StartsWith("# Konverzace", trimmed);
        Assert.DoesNotContain("q1", trimmed);
        Assert.Contains("q2", trimmed);
        Assert.Contains("a2", trimmed);
        // kept turns must stay on their own lines (separator not swallowed by trim)
        Assert.Contains("\n### 🤖 Claude", trimmed);
    }

    [Fact]
    public void TrimToLastTurns_NoOpWhenUnderLimit()
    {
        var md = "# H\n\n### 🧑 Uživatel\n\nq1\n### 🤖 Claude\n\na1\n";
        Assert.Equal(md, HistoryFormat.TrimToLastTurns(md, maxTurns: 10));
    }
}
