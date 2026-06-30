using System.Text;
using System.Text.Json;
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

    public static RenderedTurns RenderNewTurns(
        IReadOnlyList<string> jsonlLines, string? afterUuid, Models.HistoryOptions options)
    {
        var sb = new StringBuilder();
        var seenMarker = afterUuid is null;
        string? lastUuid = afterUuid;

        foreach (var line in jsonlLines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonElement root;
            try { root = JsonDocument.Parse(line).RootElement; }
            catch { continue; }                                   // malformed → ignore

            var uuid = root.TryGetProperty("uuid", out var u) ? u.GetString() : null;

            if (!seenMarker)
            {
                if (uuid == afterUuid) seenMarker = true;          // skip up to AND including marker
                continue;
            }

            if (uuid is not null) lastUuid = uuid;                 // advance high-water mark

            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (!root.TryGetProperty("message", out var msg)) continue;
            if (!msg.TryGetProperty("content", out var content)) continue;

            if (type == "assistant")
                RenderAssistantContent(sb, content, options);
            else if (type == "user")
                RenderUserToolResults(sb, content, options);      // human prompt text is skipped here
        }

        return new RenderedTurns(sb.ToString(), lastUuid);
    }

    private static void RenderAssistantContent(StringBuilder sb, JsonElement content, Models.HistoryOptions o)
    {
        if (content.ValueKind != JsonValueKind.Array) return;
        foreach (var block in content.EnumerateArray())
        {
            var bt = block.TryGetProperty("type", out var x) ? x.GetString() : null;
            if (bt == "text" && block.TryGetProperty("text", out var txt))
            {
                var s = txt.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    sb.Append("\n### 🤖 Claude\n\n").Append(s!.TrimEnd()).Append('\n');
            }
            else if (bt == "tool_use" && o.IncludeTools)
            {
                var name = block.TryGetProperty("name", out var n) ? n.GetString() : "tool";
                sb.Append("\n> 🔧 **").Append(name).Append("**\n");
                if (block.TryGetProperty("input", out var input))
                    sb.Append("\n```json\n").Append(input.ToString()).Append("\n```\n");
            }
        }
    }

    private static void RenderUserToolResults(StringBuilder sb, JsonElement content, Models.HistoryOptions o)
    {
        if (!o.IncludeTools || content.ValueKind != JsonValueKind.Array) return;
        foreach (var block in content.EnumerateArray())
        {
            var bt = block.TryGetProperty("type", out var x) ? x.GetString() : null;
            if (bt != "tool_result") continue;
            var text = ExtractToolResultText(block);
            sb.Append("\n```\n").Append(Truncate(text, o.ToolResultMaxLines)).Append("\n```\n");
        }
    }

    private static string ExtractToolResultText(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var c)) return "";
        if (c.ValueKind == JsonValueKind.String) return c.GetString() ?? "";
        if (c.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var part in c.EnumerateArray())
                if (part.TryGetProperty("text", out var pt)) sb.Append(pt.GetString());
            return sb.ToString();
        }
        return c.ToString();
    }

    private static string Truncate(string text, int maxLines)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length <= maxLines) return text.TrimEnd();
        return string.Join("\n", lines.Take(maxLines)) + "\n… (zkráceno)";
    }
}

public record RenderedTurns(string Markdown, string? LastUuid);
