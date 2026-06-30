using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeOrchestrator.Services;

/// <summary>
/// Pure (filesystem-free) formatting helpers for agent conversation history.
/// Kept separate from ConversationHistoryService so the rendering rules are unit-testable.
/// </summary>
public static partial class HistoryFormat
{
    private static readonly Regex InvalidDirChars = new(@"[^\w \-.]+", RegexOptions.Compiled);

    public static string SanitizeDirName(string name)
    {
        var cleaned = InvalidDirChars.Replace(name ?? "", "-");
        cleaned = cleaned.Trim().Trim('.').Trim();
        return string.IsNullOrEmpty(cleaned) ? "agent" : cleaned;
    }

    public static string RenderUserPrompt(string prompt)
        => $"\n### 🧑 Uživatel\n\n{(prompt ?? "").TrimEnd()}\n";

    public static string Header(string name, string? cwd, string? sessionId, DateTime startedUtc)
        => new StringBuilder()
            .Append("# Konverzace — ").Append(name).Append('\n').Append('\n')
            .Append("- **CWD:** ").Append(cwd ?? "(none)").Append('\n')
            .Append("- **Session:** ").Append(string.IsNullOrEmpty(sessionId) ? "(unknown)" : sessionId).Append('\n')
            .Append("- **Začátek:** ").Append(startedUtc.ToString("u")).Append('\n')
            .ToString();

    /// <summary>
    /// Keep the leading non-turn header plus the last <paramref name="maxTurns"/> turn
    /// sections (a turn = a block starting with "### "). Tool blocks stay attached to the
    /// "### 🤖 Claude" section that owns them.
    /// </summary>
    public static string TrimToLastTurns(string markdown, int maxTurns)
    {
        if (maxTurns <= 0) return markdown;
        var marker = "### ";
        var idx = markdown.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return markdown;

        var head = markdown[..idx];
        var body = markdown[idx..];

        // Split on each "### " boundary (keep the marker on each chunk).
        var parts = body.Split(new[] { "\n### " }, StringSplitOptions.None);
        // parts[0] starts with "### " already (the first turn); the rest lost their
        // "\n### " separator to the split, so re-add it (newline included) when rebuilding
        // so kept turns stay on their own lines after a join.
        var turns = new List<string> { parts[0] };
        for (var i = 1; i < parts.Length; i++) turns.Add("\n### " + parts[i]);

        if (turns.Count <= maxTurns) return markdown;

        var kept = turns.Skip(turns.Count - maxTurns);
        return head + string.Join("", kept);
    }
}
