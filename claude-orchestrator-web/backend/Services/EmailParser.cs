using AngleSharp.Html.Dom;
using ClaudeOrchestrator.Models;
using Ganss.Xss;
using MimeKit;

namespace ClaudeOrchestrator.Services;

/// <summary>
/// Bajty → <see cref="ParsedEmail"/>. Žádné IO nad rámec předaného streamu,
/// žádné HTTP. Port z EasyTools; oprava přepisu cid: viz spec §6.2.
/// </summary>
public static class EmailParser
{
    public static ParsedEmail ParseEml(Stream stream)
    {
        var msg = MimeMessage.Load(stream);

        var attachments = new List<AttachmentInfo>();
        var idx = 0;
        foreach (var att in EnumerateEmlAttachmentParts(msg))
        {
            attachments.Add(new AttachmentInfo(
                idx,
                att.FileName ?? att.ContentDisposition?.FileName ?? $"attachment-{idx}",
                att.ContentType?.MimeType ?? "application/octet-stream",
                MeasureMimePart(att)));
            idx++;
        }

        var htmlRaw = msg.HtmlBody;
        var (htmlSan, unresolved) = SanitizeHtml(htmlRaw, BuildEmlInlineMap(msg));

        return new ParsedEmail(
            From: msg.From.Mailboxes.Select(FormatMailbox).FirstOrDefault() ?? "",
            To: msg.To.Mailboxes.Select(FormatMailbox).ToList(),
            Cc: msg.Cc.Mailboxes.Select(FormatMailbox).ToList(),
            Bcc: msg.Bcc.Mailboxes.Select(FormatMailbox).ToList(),
            Subject: msg.Subject ?? "",
            Date: msg.Date == DateTimeOffset.MinValue ? null : msg.Date,
            TextBody: msg.TextBody,
            HtmlBodyRaw: htmlRaw,
            HtmlBodySanitized: htmlSan,
            UnresolvedInlineImages: unresolved,
            Attachments: attachments,
            Headers: msg.Headers.Select(h => new HeaderEntry(h.Field, h.Value)).ToList());
    }

    private static string FormatMailbox(MailboxAddress m)
        => string.IsNullOrEmpty(m.Name) ? m.Address : $"{m.Name} <{m.Address}>";

    private static long MeasureMimePart(MimePart part)
    {
        using var ms = new MemoryStream();
        part.Content!.DecodeTo(ms);
        return ms.Length;
    }

    /// <summary>Skutečné přílohy nejdřív, inline části s Content-ID za nimi.
    /// Na tomhle pořadí stojí indexy, kterými se přílohy stahují.</summary>
    private static IEnumerable<MimePart> EnumerateEmlAttachmentParts(MimeMessage msg)
    {
        foreach (var p in msg.Attachments.OfType<MimePart>())
            yield return p;
        foreach (var p in msg.BodyParts.OfType<MimePart>())
            if (!p.IsAttachment && !string.IsNullOrEmpty(p.ContentId))
                yield return p;
    }

    private static Dictionary<string, string> BuildEmlInlineMap(MimeMessage msg)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in msg.BodyParts.OfType<MimePart>())
        {
            if (string.IsNullOrEmpty(part.ContentId)) continue;
            var ctype = part.ContentType?.MimeType ?? "application/octet-stream";
            using var ms = new MemoryStream();
            part.Content!.DecodeTo(ms);
            map[part.ContentId] = $"data:{ctype};base64,{Convert.ToBase64String(ms.ToArray())}";
        }
        return map;
    }

    /// <summary>
    /// Sanitizace + dosazení inline obrázků. Pořadí je záměrné: přepis běží
    /// v PostProcessNode, tedy AŽ ZA validací atributů, a nastavená hodnota
    /// se znovu nekontroluje. Díky tomu smí `data` chybět v AllowedSchemes —
    /// cizí data: URL se zahodí, naše projdou (spec §6.2).
    ///
    /// `cid` v AllowedSchemes být MUSÍ, jinak sanitizer atribut zahodí dřív,
    /// než se handler vůbec zavolá, a žádný inline obrázek se nezobrazí.
    ///
    /// Instance je lokální, ne statická: handler nese stav konkrétního volání
    /// (mapa + počítadlo), takže sdílená instance by byla závod za souběhu.
    /// </summary>
    internal static (string? Html, int Unresolved) SanitizeHtml(
        string? rawHtml, IReadOnlyDictionary<string, string> inlineMap)
    {
        if (string.IsNullOrEmpty(rawHtml)) return (null, 0);

        var unresolved = 0;
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedSchemes.Add("mailto");
        sanitizer.AllowedSchemes.Add("tel");
        sanitizer.AllowedSchemes.Add("cid");

        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is not IHtmlImageElement img) return;
            var src = img.GetAttribute("src");
            if (src is null || !src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase)) return;

            var cid = src[4..].Trim('<', '>');
            if (inlineMap.TryGetValue(cid, out var dataUrl))
                img.SetAttribute("src", dataUrl);
            else
            {
                img.RemoveAttribute("src");
                unresolved++;
            }
        };

        return (sanitizer.Sanitize(rawHtml), unresolved);
    }
}
