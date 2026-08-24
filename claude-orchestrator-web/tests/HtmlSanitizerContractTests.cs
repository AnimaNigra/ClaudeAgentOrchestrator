using AngleSharp.Html.Dom;
using Ganss.Xss;

namespace ClaudeOrchestrator.Tests;

/// <summary>
/// Charakterizační testy knihovny HtmlSanitizer. Netestují náš kód — přibíjejí
/// chování, na kterém stojí bezpečnost prohlížeče e-mailů (viz spec §6.2).
/// Když je rozbije upgrade balíčku, je to varování, ne falešný poplach.
/// </summary>
public class HtmlSanitizerContractTests
{
    private static HtmlSanitizer Make(bool allowCid, bool allowData, bool rewrite)
    {
        var s = new HtmlSanitizer();
        s.AllowedSchemes.Add("mailto");
        s.AllowedSchemes.Add("tel");
        if (allowCid) s.AllowedSchemes.Add("cid");
        if (allowData) s.AllowedSchemes.Add("data");
        if (rewrite)
        {
            s.PostProcessNode += (_, e) =>
            {
                if (e.Node is IHtmlImageElement img &&
                    img.GetAttribute("src") is { } src &&
                    src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
                {
                    img.SetAttribute("src", "data:image/png;base64,OURS");
                }
            };
        }
        return s;
    }

    [Fact]
    public void PostProcessNode_SetAttribute_NeniZnovuValidovan()
    {
        // Jádro opravy z §6.2: data: URL projde, i když data NENÍ v AllowedSchemes.
        var html = Make(allowCid: true, allowData: false, rewrite: true)
            .Sanitize("""<img src="cid:logo">""");
        Assert.Contains("data:image/png;base64,OURS", html);
    }

    [Fact]
    public void CidMusiBytPovolen_JinakSePrepisNikdyNespusti()
    {
        // Bez cid v AllowedSchemes zahodí sanitizer atribut dřív, než se
        // PostProcessNode zavolá — handler nemá co přepsat. Tenhle test je
        // důvod, proč je cid v seznamu (Odchylka 2 v plánu).
        var html = Make(allowCid: false, allowData: false, rewrite: true)
            .Sanitize("""<img src="cid:logo">""");
        Assert.DoesNotContain("data:", html);
        Assert.DoesNotContain("cid:", html);
    }

    [Fact]
    public void CiziDataUrl_SeZahodi_KdyzDataNeniPovoleno()
    {
        // Bezpečnostní zisk z §6.2: odesílatelovo data: URL neprojde.
        var html = Make(allowCid: true, allowData: false, rewrite: true)
            .Sanitize("""<img src="data:image/png;base64,CIZI">""");
        Assert.DoesNotContain("CIZI", html);
    }

    [Fact]
    public void PrepisFungujeBezUvozovek()
    {
        // Původní kód EasyTools hledal doslovno "cid:x" a 'cid:x', takže
        // na tenhle tvar nesedl (§6.2).
        var html = Make(allowCid: true, allowData: false, rewrite: true)
            .Sanitize("<img src=cid:logo>");
        Assert.Contains("data:image/png;base64,OURS", html);
    }

    [Fact]
    public void ScriptSeOdstrani()
    {
        var html = Make(allowCid: true, allowData: false, rewrite: true)
            .Sanitize("<p>ok</p><script>alert(1)</script>");
        Assert.DoesNotContain("script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>ok</p>", html);
    }

    [Fact]
    public void MailtoZustava()
    {
        var html = Make(allowCid: true, allowData: false, rewrite: true)
            .Sanitize("""<a href="mailto:x@y.cz">m</a>""");
        Assert.Contains("mailto:x@y.cz", html);
    }
}
