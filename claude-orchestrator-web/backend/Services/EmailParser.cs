using AngleSharp.Html.Dom;
using ClaudeOrchestrator.Models;
using Ganss.Xss;
using MimeKit;
using System.Text;
using MsgStorage = MsgReader.Outlook.Storage;

namespace ClaudeOrchestrator.Services;

/// <summary>
/// Bajty → <see cref="ParsedEmail"/>. Žádné IO nad rámec předaného streamu,
/// žádné HTTP. Port z EasyTools; oprava přepisu cid: viz spec §6.2.
/// </summary>
public static class EmailParser
{
    public enum Format { Eml, Msg }

    /// <summary>Strop pro jeden vložený obrázek a pro jejich součet. Vložené
    /// obrázky se kódují do base64 přímo do těla (spec §6.2), takže velký
    /// obrázek nafoukne odpověď o třetinu navíc — přesně ten důvod, proč spec
    /// §4.1 odmítla posílat takhle přílohy. Co se nevejde, se nedosadí a
    /// započítá se jako nerozřešené, o čemž UI uživatele zpraví.</summary>
    private const long MaxInlineImageBytes = 10L * 1024 * 1024;
    private const long MaxInlineTotalBytes = 25L * 1024 * 1024;

    static EmailParser()
    {
        // .NET Core nevozí legacy kódové stránky. Reálné .msg z českého Outlooku
        // deklarují ISO-8859-2 (28592) a MsgReader na nich bez tohohle spadne
        // s NotSupportedException už při načítání příloh — naměřeno na skutečné
        // zprávě, viz Odchylka 1. Registrace patří sem, ne do Program.cs, aby
        // parser fungoval i v testech.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Čte první čtyři bajty (D0 CF 11 E0 = OLE compound file, tedy .msg)
    /// a vrátí pozici streamu tam, kde byla — volající z něj čte hned potom.</summary>
    public static Format SniffFormat(Stream stream)
    {
        if (!stream.CanSeek) throw new ArgumentException("Stream must be seekable.", nameof(stream));
        Span<byte> head = stackalloc byte[4];
        var origin = stream.Position;
        var read = stream.Read(head);
        stream.Position = origin;
        return read == 4 && head[0] == 0xD0 && head[1] == 0xCF && head[2] == 0x11 && head[3] == 0xE0
            ? Format.Msg
            : Format.Eml;
    }

    /// <summary>Rozpozná formát a deleguje. Předaný stream zůstává otevřený
    /// a použitelný i po návratu — u obou formátů.</summary>
    public static ParsedEmail Parse(Stream stream) =>
        SniffFormat(stream) == Format.Msg ? ParseMsg(stream) : ParseEml(stream);

    public static ParsedEmail ParseEml(Stream stream)
    {
        var msg = MimeMessage.Load(stream);

        var attachments = new List<AttachmentInfo>();
        var idx = 0;
        foreach (var att in EnumerateEmlAttachmentParts(msg))
        {
            attachments.Add(new AttachmentInfo(
                idx,
                FileNameOrFallback(att.FileName, att.ContentDisposition?.FileName, idx),
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

    /// <summary>Jako `??`, ale prázdný řetězec bere jako chybějící hodnotu.
    /// `Content-Disposition: attachment; filename=""` je platná hlavička, kterou
    /// `??` samotné nezachytí — prázdné jméno by pak dotáhlo `File(...)` k tomu,
    /// že vynechá hlavičku Content-Disposition úplně a prohlížeč by dostal cizí
    /// bajty pod cizím Content-Type na naší doméně. Větev .msg tohle už řešila
    /// přes IsNullOrEmpty, tady se s ní sjednocuje.</summary>
    private static string FileNameOrFallback(string? fileName, string? dispositionFileName, int index)
        => !string.IsNullOrEmpty(fileName) ? fileName
         : !string.IsNullOrEmpty(dispositionFileName) ? dispositionFileName
         : $"attachment-{index}";

    private static long MeasureMimePart(MimePart part)
    {
        // Content je null u části s prázdným tělem i u streamu useknutého
        // uprostřed části — MimeMessage.Load ani u jednoho nevyhodí chybu,
        // takže se to sem dostane jako platná zpráva.
        if (part.Content is null) return 0;
        using var ms = new MemoryStream();
        part.Content.DecodeTo(ms);
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
        long total = 0;
        foreach (var part in msg.BodyParts.OfType<MimePart>())
        {
            if (string.IsNullOrEmpty(part.ContentId)) continue;
            // Bez těla není co dosadit — část se do mapy nedostane a její cid:
            // se tím pádem započítá jako nerozřešené, což je správné chování.
            if (part.Content is null) continue;
            using var ms = new MemoryStream();
            part.Content.DecodeTo(ms);
            var size = ms.Length;
            // Nad stropem pro jeden obrázek nebo pro jejich součet se
            // nedosadí — mapa o té části neví, takže dopadne stejně jako
            // chybějící tělo výše: cid: zůstane a započítá se jako nerozřešený.
            if (size > MaxInlineImageBytes || total + size > MaxInlineTotalBytes) continue;
            total += size;
            var ctype = part.ContentType?.MimeType ?? "application/octet-stream";
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

    public static ParsedEmail ParseMsg(Stream stream)
    {
        // MsgReader si stream ve výchozím nastavení přivlastní a při Dispose ho
        // zavře. Volající ho ale vlastní sám (a u .eml větve zůstává otevřený),
        // takže se to musí vypnout — jinak po Parse spadne každé další čtení
        // ze stejného streamu na ObjectDisposedException.
        using var msg = new MsgStorage.Message(stream, FileAccess.Read, leaveStreamOpen: true);

        var headers = new List<HeaderEntry>();
        if (msg.Headers?.RawHeaders is { } rawHeaders)
            foreach (string key in rawHeaders)
                headers.Add(new HeaderEntry(key, rawHeaders[key] ?? ""));

        var inline = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var attachments = new List<AttachmentInfo>();
        var idx = 0;
        long inlineTotal = 0;
        foreach (var a in msg.Attachments.OfType<MsgStorage.Attachment>())
        {
            var name = !string.IsNullOrEmpty(a.FileName) ? a.FileName : $"attachment-{idx}";
            var ctype = !string.IsNullOrEmpty(a.MimeType) ? a.MimeType : "application/octet-stream";
            if (!string.IsNullOrEmpty(a.ContentId) && a.Data is not null)
            {
                // Stejné stropy jako u .eml větve (BuildEmlInlineMap) — nad
                // limit se nedosadí a cid: zůstane nerozřešený.
                var size = a.Data.LongLength;
                if (size <= MaxInlineImageBytes && inlineTotal + size <= MaxInlineTotalBytes)
                {
                    inline[a.ContentId] = $"data:{ctype};base64,{Convert.ToBase64String(a.Data)}";
                    inlineTotal += size;
                }
            }
            attachments.Add(new AttachmentInfo(idx, name, ctype, a.Data?.LongLength ?? 0L));
            idx++;
        }

        var htmlRaw = msg.BodyHtml;
        var (htmlSan, unresolved) = SanitizeHtml(htmlRaw, inline);

        return new ParsedEmail(
            From: FormatMsgAddress(msg.Sender?.DisplayName, msg.Sender?.Email),
            To: msg.Recipients.Where(r => r.Type == MsgReader.Outlook.RecipientType.To)
                              .Select(r => FormatMsgAddress(r.DisplayName, r.Email)).ToList(),
            Cc: msg.Recipients.Where(r => r.Type == MsgReader.Outlook.RecipientType.Cc)
                              .Select(r => FormatMsgAddress(r.DisplayName, r.Email)).ToList(),
            Bcc: msg.Recipients.Where(r => r.Type == MsgReader.Outlook.RecipientType.Bcc)
                               .Select(r => FormatMsgAddress(r.DisplayName, r.Email)).ToList(),
            Subject: msg.Subject ?? "",
            Date: msg.SentOn,
            TextBody: msg.BodyText,
            HtmlBodyRaw: htmlRaw,
            HtmlBodySanitized: htmlSan,
            UnresolvedInlineImages: unresolved,
            Attachments: attachments,
            Headers: headers);
    }

    private static string FormatMsgAddress(string? name, string? email)
    {
        if (string.IsNullOrEmpty(email)) return name ?? "";
        if (string.IsNullOrEmpty(name) || name == email) return email;
        return $"{name} <{email}>";
    }

    /// <summary>Znovu naparsuje zdroj a vytáhne N-tou přílohu. Bezstavové
    /// záměrně — server si mezi požadavky nic nedrží (spec §4.1).
    /// Předaný stream zůstává otevřený a použitelný i po návratu.</summary>
    public static (byte[] Bytes, string FileName, string ContentType) ExtractAttachmentBytes(
        Stream stream, Format format, int index) => format switch
    {
        Format.Eml => ExtractEmlAttachment(stream, index),
        Format.Msg => ExtractMsgAttachment(stream, index),
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    private static (byte[] Bytes, string FileName, string ContentType) ExtractEmlAttachment(
        Stream stream, int index)
    {
        var msg = MimeMessage.Load(stream);
        var parts = EnumerateEmlAttachmentParts(msg).ToList();
        if (index < 0 || index >= parts.Count)
            throw new IndexOutOfRangeException($"Attachment index {index} out of range (0..{parts.Count - 1}).");
        var part = parts[index];
        using var ms = new MemoryStream();
        // Stejný důvod jako v MeasureMimePart: prázdné tělo znamená Content == null,
        // ne chybu. Stáhne se prázdný soubor místo pádu s NullReferenceException.
        part.Content?.DecodeTo(ms);
        return (ms.ToArray(),
                FileNameOrFallback(part.FileName, part.ContentDisposition?.FileName, index),
                part.ContentType?.MimeType ?? "application/octet-stream");
    }

    private static (byte[] Bytes, string FileName, string ContentType) ExtractMsgAttachment(
        Stream stream, int index)
    {
        // Stejný důvod jako v ParseMsg: stream patří volajícímu.
        using var msg = new MsgStorage.Message(stream, FileAccess.Read, leaveStreamOpen: true);
        var all = msg.Attachments.OfType<MsgStorage.Attachment>().ToList();
        if (index < 0 || index >= all.Count)
            throw new IndexOutOfRangeException($"Attachment index {index} out of range (0..{all.Count - 1}).");
        var a = all[index];
        return (a.Data ?? Array.Empty<byte>(),
                !string.IsNullOrEmpty(a.FileName) ? a.FileName : $"attachment-{index}",
                !string.IsNullOrEmpty(a.MimeType) ? a.MimeType : "application/octet-stream");
    }
}
