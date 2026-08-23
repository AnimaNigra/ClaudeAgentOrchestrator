namespace ClaudeOrchestrator.Models;

public sealed record ParsedEmail(
    string From,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string Subject,
    DateTimeOffset? Date,
    string? TextBody,
    string? HtmlBodyRaw,
    string? HtmlBodySanitized,
    /// <summary>Počet <img src="cid:…">, ke kterým se nenašla odpovídající
    /// část zprávy. UI o tom uživatele zpraví, ať netápe, kde je obrázek.</summary>
    int UnresolvedInlineImages,
    IReadOnlyList<AttachmentInfo> Attachments,
    IReadOnlyList<HeaderEntry> Headers);

public sealed record AttachmentInfo(
    int Index,
    string FileName,
    string ContentType,
    long SizeBytes);

public sealed record HeaderEntry(string Name, string Value);
