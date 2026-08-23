using System.Text;
using ClaudeOrchestrator.Services;

namespace ClaudeOrchestrator.Tests;

public class EmailParserTests
{
    /// <summary>MIME vyžaduje CRLF; zdrojový soubor má LF. Bez tohohle
    /// převodu testy prochází nahodile podle nastavení gitu.</summary>
    private static Stream S(string eml) =>
        new MemoryStream(Encoding.UTF8.GetBytes(eml.ReplaceLineEndings("\r\n")));

    private const string Prosty = """
        From: Jan Nemec <jan@example.cz>
        To: Petr Novak <petr@example.cz>
        Cc: Eva <eva@example.cz>
        Subject: =?utf-8?B?UMWZw61sacWhIMW+bHXFpW91xI1rw70=?=
        Date: Mon, 23 Aug 2026 10:00:00 +0200
        MIME-Version: 1.0
        Content-Type: text/plain; charset=utf-8

        Ahoj, tohle je tělo.

        """;

    private const string Alternative = """
        From: a@example.cz
        To: b@example.cz
        Subject: Obojí
        MIME-Version: 1.0
        Content-Type: multipart/alternative; boundary="ALT"

        --ALT
        Content-Type: text/plain; charset=utf-8

        textová verze
        --ALT
        Content-Type: text/html; charset=utf-8

        <p>HTML verze</p>
        --ALT--

        """;

    /// <summary>Inline obrázek s cid, druhý cid bez odpovídající části,
    /// cizí data: URL, script a mailto — celá bezpečnostní matice v jedné
    /// zprávě. Plus jedna skutečná příloha.</summary>
    private const string Related = """
        From: a@example.cz
        To: b@example.cz
        Subject: Inline
        MIME-Version: 1.0
        Content-Type: multipart/related; boundary="REL"

        --REL
        Content-Type: text/html; charset=utf-8

        <p>Ahoj</p><img src=cid:logo123><img src="cid:chybi"><img src="data:image/png;base64,CIZI"><script>alert(1)</script><a href="mailto:x@y.cz">m</a>
        --REL
        Content-Type: image/png
        Content-ID: <logo123>
        Content-Transfer-Encoding: base64
        Content-Disposition: inline; filename="logo.png"

        iVBORw0KGgo=
        --REL
        Content-Type: text/plain; charset=utf-8
        Content-Disposition: attachment; filename="pozn.txt"

        obsah prilohy
        --REL--

        """;

    [Fact]
    public void ProstyEmail_MaHlavickyATelo()
    {
        var e = EmailParser.ParseEml(S(Prosty));
        Assert.Equal("Jan Nemec <jan@example.cz>", e.From);
        Assert.Equal(new[] { "Petr Novak <petr@example.cz>" }, e.To);
        Assert.Equal(new[] { "Eva <eva@example.cz>" }, e.Cc);
        Assert.Empty(e.Bcc);
        Assert.Equal("Ahoj, tohle je tělo.", e.TextBody!.Trim());
        Assert.Null(e.HtmlBodySanitized);
        Assert.Empty(e.Attachments);
        Assert.Equal(0, e.UnresolvedInlineImages);
    }

    [Fact]
    public void Diakritika_VKodovanemPredmetu_SeDekoduje()
    {
        // RFC 2047 base64 hlavička — tohle je nejčastější zdroj rozsypaného čaje.
        Assert.Equal("Příliš žluťoučký", EmailParser.ParseEml(S(Prosty)).Subject);
    }

    [Fact]
    public void Datum_SeParsujeVcetneZony()
    {
        var e = EmailParser.ParseEml(S(Prosty));
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.FromHours(2)), e.Date);
    }

    [Fact]
    public void VsechnyHlavicky_JsouVSeznamu()
    {
        var e = EmailParser.ParseEml(S(Prosty));
        Assert.Contains(e.Headers, h => h.Name == "Subject");
        Assert.Contains(e.Headers, h => h.Name == "Date");
        // Šest, ne sedm: MimeKit stěhuje Content-Type na tělovou část, takže
        // v hlavičkách zprávy zůstane From, To, Cc, Subject, Date, MIME-Version.
        Assert.Equal(6, e.Headers.Count);
    }

    [Fact]
    public void MultipartAlternative_MaObeTela()
    {
        var e = EmailParser.ParseEml(S(Alternative));
        Assert.Equal("textová verze", e.TextBody!.Trim());
        Assert.Contains("<p>HTML verze</p>", e.HtmlBodySanitized);
    }

    [Fact]
    public void InlineObrazek_SePrepiseNaDataUrl()
    {
        var e = EmailParser.ParseEml(S(Related));
        Assert.Contains("<img src=\"data:image/png;base64,iVBORw0KGgo=\">", e.HtmlBodySanitized);
    }

    [Fact]
    public void CidBezUvozovek_SeTakePrepise()
    {
        // Regrese proti původnímu kódu EasyTools, který hledal doslovné
        // "cid:x" a 'cid:x' a na <img src=cid:x> nesedl (spec §6.2).
        // Ve fixtuře Related je logo123 zapsané právě bez uvozovek.
        var e = EmailParser.ParseEml(S(Related));
        Assert.DoesNotContain("cid:logo123", e.HtmlBodySanitized);
        Assert.Contains("data:image/png;base64,", e.HtmlBodySanitized);
    }

    [Fact]
    public void CiziDataUrl_SeZahodi_AleNaseZustane()
    {
        // Bezpečnostní zisk z §6.2: odesílatelovo data: URL neprojde,
        // naše dosazené ve stejné zprávě ano.
        var e = EmailParser.ParseEml(S(Related));
        Assert.DoesNotContain("CIZI", e.HtmlBodySanitized);
        Assert.Contains("iVBORw0KGgo=", e.HtmlBodySanitized);
    }

    [Fact]
    public void NerozresenyCid_SeSpocitaAAtributZmizi()
    {
        var e = EmailParser.ParseEml(S(Related));
        Assert.Equal(1, e.UnresolvedInlineImages);
        Assert.DoesNotContain("cid:chybi", e.HtmlBodySanitized);
    }

    [Fact]
    public void Script_JePoSanitizaciPryc()
    {
        var e = EmailParser.ParseEml(S(Related));
        Assert.DoesNotContain("alert(1)", e.HtmlBodySanitized);
        Assert.DoesNotContain("<script", e.HtmlBodySanitized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mailto_PoSanitizaciZustava()
    {
        Assert.Contains("mailto:x@y.cz", EmailParser.ParseEml(S(Related)).HtmlBodySanitized);
    }

    [Fact]
    public void SyroveHtml_ZustavaNesanitizovane()
    {
        // Tab "source" musí ukázat originál — jinak nemá smysl.
        var e = EmailParser.ParseEml(S(Related));
        Assert.Contains("<script>alert(1)</script>", e.HtmlBodyRaw);
        Assert.Contains("cid:logo123", e.HtmlBodyRaw);
    }

    [Fact]
    public void Prilohy_MajiJmenoTypVelikostAPoradi()
    {
        // Skutečné přílohy jdou první, inline části s Content-ID za nimi.
        var e = EmailParser.ParseEml(S(Related));
        Assert.Equal(2, e.Attachments.Count);

        Assert.Equal(0, e.Attachments[0].Index);
        Assert.Equal("pozn.txt", e.Attachments[0].FileName);
        Assert.Equal("text/plain", e.Attachments[0].ContentType);
        Assert.Equal(13, e.Attachments[0].SizeBytes);

        Assert.Equal(1, e.Attachments[1].Index);
        Assert.Equal("logo.png", e.Attachments[1].FileName);
        Assert.Equal("image/png", e.Attachments[1].ContentType);
        Assert.Equal(8, e.Attachments[1].SizeBytes);
    }

    [Fact]
    public void PoskozenyVstup_HodiVyjimku_NeSpadneJinak()
    {
        var garbage = new MemoryStream(Encoding.UTF8.GetBytes("naprosty nesmysl bez hlavicek"));
        Assert.Throws<FormatException>(() => EmailParser.ParseEml(garbage));
    }

    [Fact]
    public void PrazdneHtml_VratiNullANulaNerozresenych()
    {
        var (html, unresolved) = EmailParser.SanitizeHtml(null, new Dictionary<string, string>());
        Assert.Null(html);
        Assert.Equal(0, unresolved);
    }
}
