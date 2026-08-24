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

    private const string PrazdnaPriloha = """
        From: a@example.cz
        To: b@example.cz
        Subject: Prazdna priloha
        MIME-Version: 1.0
        Content-Type: multipart/mixed; boundary="MIX"

        --MIX
        Content-Type: text/plain; charset=utf-8

        telo
        --MIX
        Content-Type: application/octet-stream
        Content-Disposition: attachment; filename="prazdny.bin"

        --MIX--

        """;

    private const string PrazdnyInline = """
        From: a@example.cz
        To: b@example.cz
        Subject: Prazdny inline
        MIME-Version: 1.0
        Content-Type: multipart/related; boundary="REL"

        --REL
        Content-Type: text/html; charset=utf-8

        <img src="cid:logo">
        --REL
        Content-Type: image/png
        Content-ID: <logo>
        Content-Disposition: inline; filename="logo.png"

        --REL--

        """;

    // Content-ID velkými písmeny proti malému cid: v těle — kód tvrdí, že to
    // porovnává bez ohledu na velikost, ale žádná fixtura to nezkoušela.
    private const string CidJinaVelikostPismen = """
        From: a@example.cz
        To: b@example.cz
        Subject: Velikost pismen
        MIME-Version: 1.0
        Content-Type: multipart/related; boundary="REL"

        --REL
        Content-Type: text/html; charset=utf-8

        <img src="cid:logo123">
        --REL
        Content-Type: image/png
        Content-ID: <LOGO123>
        Content-Transfer-Encoding: base64
        Content-Disposition: inline; filename="logo.png"

        iVBORw0KGgo=
        --REL--

        """;

    [Fact]
    public void PrilohaSPrazdnymTelem_NeshodiParser()
    {
        // MimeKit vrací Content == null u prázdného těla; force-unwrap tu dřív
        // vyhazoval NullReferenceException na naprosto platné zprávě.
        var e = EmailParser.ParseEml(S(PrazdnaPriloha));
        Assert.Single(e.Attachments);
        Assert.Equal("prazdny.bin", e.Attachments[0].FileName);
        Assert.Equal(0, e.Attachments[0].SizeBytes);
    }

    private const string PrazdneJmenoPrilohy = """
        From: a@example.cz
        To: b@example.cz
        Subject: Prazdne jmeno
        MIME-Version: 1.0
        Content-Type: multipart/mixed; boundary="MIX"

        --MIX
        Content-Type: text/plain; charset=utf-8

        telo
        --MIX
        Content-Type: application/octet-stream
        Content-Disposition: attachment; filename=""

        data
        --MIX--

        """;

    [Fact]
    public void PrazdneJmenoPrilohy_SeNahradiVygenerovanym()
    {
        // Content-Disposition: attachment; filename="" je platná hlavička a
        // MimePart.FileName u ní vrátí "", ne null — `??` samotné to
        // nezachytí. Bez opravy by prázdné jméno prošlo až do File(...) a
        // ASP.NET Core by vynechal hlavičku Content-Disposition úplně.
        var e = EmailParser.ParseEml(S(PrazdneJmenoPrilohy));
        Assert.Single(e.Attachments);
        Assert.Equal("attachment-0", e.Attachments[0].FileName);
    }

    [Fact]
    public void InlineCastSPrazdnymTelem_SePocitaJakoNerozresena()
    {
        var e = EmailParser.ParseEml(S(PrazdnyInline));
        Assert.Equal(1, e.UnresolvedInlineImages);
        Assert.DoesNotContain("cid:", e.HtmlBodySanitized);
        Assert.DoesNotContain("data:", e.HtmlBodySanitized);
    }

    [Fact]
    public void CidSeDohledaBezOhleduNaVelikostPismen()
    {
        var e = EmailParser.ParseEml(S(CidJinaVelikostPismen));
        Assert.Equal(0, e.UnresolvedInlineImages);
        Assert.Contains("data:image/png;base64,iVBORw0KGgo=", e.HtmlBodySanitized);
    }

    [Fact]
    public void PrilisVelkyInlineObrazek_SeNedosadiAlePodlimitniAnoDal()
    {
        // Fixtura překračuje EmailParser.MaxInlineImageBytes (10 MB) tím, že
        // base64 v těle dekóduje na přesně 11 MB nulových bajtů. Generuje se
        // programově přes Convert.ToBase64String, aby test nemusel v souboru
        // nést desítky MB textu; MimeKit dekóduje base64 část bez ohledu na
        // to, že leží na jednom obřím řádku bez zalomení. Vedle ní je i
        // normální malý inline obrázek — ten musí projít dál beze změny.
        var big = Convert.ToBase64String(new byte[11 * 1024 * 1024]);
        var eml = $"""
            From: a@example.cz
            To: b@example.cz
            Subject: Velky inline
            MIME-Version: 1.0
            Content-Type: multipart/related; boundary="REL"

            --REL
            Content-Type: text/html; charset=utf-8

            <p><img src="cid:small"><img src="cid:big"></p>
            --REL
            Content-Type: image/png
            Content-ID: <small>
            Content-Transfer-Encoding: base64
            Content-Disposition: inline; filename="small.png"

            iVBORw0KGgo=
            --REL
            Content-Type: image/png
            Content-ID: <big>
            Content-Transfer-Encoding: base64
            Content-Disposition: inline; filename="big.png"

            {big}
            --REL--

            """;
        var e = EmailParser.ParseEml(S(eml));

        Assert.Equal(1, e.UnresolvedInlineImages);
        Assert.Contains("data:image/png;base64,iVBORw0KGgo=", e.HtmlBodySanitized);
        Assert.DoesNotContain("cid:big", e.HtmlBodySanitized);
    }

    [Fact]
    public void Sniff_MagickeBajty_ZnamenajiMsg()
    {
        var msg = new MemoryStream(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0x00, 0x01 });
        Assert.Equal(EmailParser.Format.Msg, EmailParser.SniffFormat(msg));
    }

    [Fact]
    public void Sniff_Text_ZnamenaEml()
    {
        Assert.Equal(EmailParser.Format.Eml, EmailParser.SniffFormat(S(Prosty)));
    }

    [Fact]
    public void Sniff_KratsiNezCtyriBajty_ZnamenaEml()
    {
        var kratky = new MemoryStream(new byte[] { 0xD0 });
        Assert.Equal(EmailParser.Format.Eml, EmailParser.SniffFormat(kratky));
    }

    [Fact]
    public void Sniff_PosunePoziciZpatky()
    {
        // Parser čte ze stejného streamu hned po rozpoznání, takže SniffFormat
        // ho nesmí nechat posunutý — jinak přijde o první čtyři bajty.
        var s = S(Prosty);
        s.Position = 0;
        EmailParser.SniffFormat(s);
        Assert.Equal(0, s.Position);
        Assert.Equal("Příliš žluťoučký", EmailParser.ParseEml(s).Subject);
    }

    [Fact]
    public void Sniff_NeseekovatelnyStream_HodiArgumentException()
    {
        Assert.Throws<ArgumentException>(() => EmailParser.SniffFormat(new NonSeekableStream()));
    }

    [Fact]
    public void Parse_DelegujePodleFormatu()
    {
        Assert.Equal("Příliš žluťoučký", EmailParser.Parse(S(Prosty)).Subject);
    }

    [Fact]
    public void ExtractAttachment_VratiSpravneBajtyJmenoATyp()
    {
        var (bytes, name, ctype) =
            EmailParser.ExtractAttachmentBytes(S(Related), EmailParser.Format.Eml, 0);
        Assert.Equal("pozn.txt", name);
        Assert.Equal("text/plain", ctype);
        Assert.Equal("obsah prilohy", Encoding.UTF8.GetString(bytes).Trim());
    }

    [Fact]
    public void ExtractAttachment_UmiIInlineCast()
    {
        // Index 1 je inline logo.png — musí jít stáhnout jako každá jiná příloha.
        var (bytes, name, ctype) =
            EmailParser.ExtractAttachmentBytes(S(Related), EmailParser.Format.Eml, 1);
        Assert.Equal("logo.png", name);
        Assert.Equal("image/png", ctype);
        Assert.Equal(8, bytes.Length);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(99)]
    public void ExtractAttachment_MimoRozsah_HodiIndexOutOfRange(int index)
    {
        Assert.Throws<IndexOutOfRangeException>(() =>
            EmailParser.ExtractAttachmentBytes(S(Related), EmailParser.Format.Eml, index));
    }

    [Fact]
    public void EmailParser_ZpristupniLegacyKodoveStranky()
    {
        // Dotknutím se typu se spustí jeho statický konstruktor.
        EmailParser.SniffFormat(new MemoryStream(new byte[4]));

        // ISO-8859-2 (Latin-2). Reálné .msg z českého Outlooku ji deklarují
        // a MsgReader na nich bez registrace poskytovatele spadne
        // s NotSupportedException už při načítání příloh — naměřeno.
        Assert.Equal("iso-8859-2", Encoding.GetEncoding(28592).WebName);
    }

    [Fact]
    public void Parse_NechavaStreamOtevreny()
    {
        // EmailParser stream nevlastní. U .eml to platilo vždy; u .msg to
        // MsgReader porušoval, dokud se mu nepředalo leaveStreamOpen. Tenhle
        // test hlídá aspoň tu větev, kterou lze otestovat bez binární fixtury.
        using var s = S(Prosty);
        var e = EmailParser.Parse(s);
        Assert.Equal("Příliš žluťoučký", e.Subject);

        s.Position = 0;
        Assert.Equal(EmailParser.Format.Eml, EmailParser.SniffFormat(s));
    }

    /// <summary>Gitignorovaná lokální fixtura. Když chybí, testy .msg se
    /// přeskočí — v souhrnu dotnet test je pak vidět `Skipped: N`, takže se to
    /// nedá splést s pokrytím, které neexistuje.</summary>
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "fixtures-local", "sample.msg");

    private static Stream OpenFixture()
    {
        Skip.IfNot(File.Exists(FixturePath),
            $"Lokální .msg fixtura chybí ({FixturePath}). Zkopíruj reálný .msg do " +
            "claude-orchestrator-web/tests/fixtures-local/sample.msg — do gitu se nedostane.");
        return File.OpenRead(FixturePath);
    }

    [SkippableFact]
    public void MsgFixtura_JeRozpoznanaJakoMsg()
    {
        using var s = OpenFixture();
        Assert.Equal(EmailParser.Format.Msg, EmailParser.SniffFormat(s));
    }

    [SkippableFact]
    public void MsgFixtura_MaOdesilatelePredmetAPrijemce()
    {
        using var s = OpenFixture();
        var e = EmailParser.Parse(s);
        Assert.NotEmpty(e.From);
        Assert.NotEmpty(e.Subject);
        Assert.NotEmpty(e.To);
        Assert.NotNull(e.Date);
    }

    [SkippableFact]
    public void MsgFixtura_MaTelo()
    {
        using var s = OpenFixture();
        var e = EmailParser.Parse(s);
        Assert.True(!string.IsNullOrWhiteSpace(e.TextBody) ||
                    !string.IsNullOrWhiteSpace(e.HtmlBodySanitized),
                    "Fixtura nemá ani textové, ani HTML tělo — vyber jinou.");
    }

    [SkippableFact]
    public void MsgFixtura_MaHlavicky()
    {
        using var s = OpenFixture();
        Assert.NotEmpty(EmailParser.Parse(s).Headers);
    }

    [SkippableFact]
    public void MsgFixtura_PrilohaJdeStahnoutAOdpovidaVypisu()
    {
        using var s = OpenFixture();
        var e = EmailParser.Parse(s);
        Assert.NotEmpty(e.Attachments);

        var first = e.Attachments[0];
        s.Position = 0;
        var (bytes, name, ctype) =
            EmailParser.ExtractAttachmentBytes(s, EmailParser.Format.Msg, 0);
        // Assert.Equal by při selhání vypsalo skutečné jméno přílohy do výstupu
        // testů. Assert.True vypíše jen zprávu. ContentType a SizeBytes níž
        // zůstávají jako Assert.Equal schválně — "image/png" ani počet bajtů
        // osobní údaj nenesou a informativní hláška je tam užitečnější.
        Assert.True(first.FileName == name,
            "Jméno přílohy z Parse a z ExtractAttachmentBytes se neshoduje.");
        Assert.Equal(first.ContentType, ctype);
        Assert.Equal(first.SizeBytes, bytes.LongLength);
    }

    [SkippableFact]
    public void MsgFixtura_PrilohaMimoRozsah_HodiIndexOutOfRange()
    {
        using var s = OpenFixture();
        Assert.Throws<IndexOutOfRangeException>(() =>
            EmailParser.ExtractAttachmentBytes(s, EmailParser.Format.Msg, 999));
    }

    [SkippableFact]
    public void MsgFixtura_SanitizovaneHtmlNeobsahujeScript()
    {
        using var s = OpenFixture();
        var e = EmailParser.Parse(s);
        Skip.If(e.HtmlBodySanitized is null, "Fixtura nemá HTML tělo.");
        Assert.True(
            !e.HtmlBodySanitized!.Contains("<script", StringComparison.OrdinalIgnoreCase),
            "Sanitizované HTML stále obsahuje <script>.");
    }

    [SkippableFact]
    public void MsgFixtura_InlineObrazekSePrepiseNaDataUrl()
    {
        // Tohle je hlavní důvod, proč se .msg testuje na reálném souboru:
        // oprava z §6.2 se na .msg větvi jinak nikdy nespustí.
        using var s = OpenFixture();
        var e = EmailParser.Parse(s);
        Skip.If(e.HtmlBodySanitized is null, "Fixtura nemá HTML tělo.");
        Assert.True(e.HtmlBodySanitized!.Contains("data:"),
            "Sanitizované HTML neobsahuje žádnou data: URL — inline obrázek se nedosadil.");
        Assert.True(!e.HtmlBodySanitized.Contains("cid:"),
            "V sanitizovaném HTML zůstal nepřepsaný odkaz cid:.");
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
