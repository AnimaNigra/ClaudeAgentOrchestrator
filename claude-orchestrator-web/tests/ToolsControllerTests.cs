using System.Text;
using ClaudeOrchestrator.Controllers;
using ClaudeOrchestrator.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ClaudeOrchestrator.Tests;

public class ToolsControllerTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "tct-" + Guid.NewGuid().ToString("N"));
    private readonly ToolsController _ctrl = new();

    public ToolsControllerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private const string Eml = """
        From: Jan Nemec <jan@example.cz>
        To: petr@example.cz
        Subject: Test
        MIME-Version: 1.0
        Content-Type: multipart/mixed; boundary="MIX"

        --MIX
        Content-Type: text/plain; charset=utf-8

        tělo
        --MIX
        Content-Type: text/plain; charset=utf-8
        Content-Disposition: attachment; filename="pozn.txt"

        obsah prilohy
        --MIX--

        """;

    private string WriteEml(string name = "a.eml")
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, Encoding.UTF8.GetBytes(Eml.ReplaceLineEndings("\r\n")));
        return p;
    }

    private static IFormFile Upload(string content, string fileName = "a.eml")
    {
        var bytes = Encoding.UTF8.GetBytes(content.ReplaceLineEndings("\r\n"));
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName);
    }

    [Fact]
    public void ParseByPath_VratiNaparsovanyEmail()
    {
        var res = Assert.IsType<OkObjectResult>(_ctrl.ParseByPath(WriteEml()));
        var email = Assert.IsType<ParsedEmail>(res.Value);
        Assert.Equal("Jan Nemec <jan@example.cz>", email.From);
        Assert.Equal("Test", email.Subject);
        Assert.Single(email.Attachments);
    }

    [Fact]
    public void ParseByPath_PrazdnaCesta_JeBadRequest()
    {
        Assert.IsType<BadRequestObjectResult>(_ctrl.ParseByPath(""));
    }

    [Fact]
    public void ParseByPath_Traversal_JeBadRequest()
    {
        Assert.IsType<BadRequestObjectResult>(
            _ctrl.ParseByPath(Path.Combine(_dir, "sub", "..", "a.eml")));
    }

    [Fact]
    public void ParseByPath_NepodporovanaPripona_JeBadRequest()
    {
        var p = Path.Combine(_dir, "a.txt");
        File.WriteAllText(p, "x");
        Assert.IsType<BadRequestObjectResult>(_ctrl.ParseByPath(p));
    }

    [Fact]
    public void ParseByPath_NeexistujiciSoubor_JeNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(_ctrl.ParseByPath(Path.Combine(_dir, "neni.eml")));
    }

    [Fact]
    public void ParseByPath_PoskozenySoubor_JeBadRequest_NeVyjimka()
    {
        var p = Path.Combine(_dir, "rozbity.eml");
        File.WriteAllText(p, "naprosty nesmysl bez hlavicek");
        var res = Assert.IsType<BadRequestObjectResult>(_ctrl.ParseByPath(p));
        Assert.Contains("Nepodařilo se", res.Value!.ToString());
    }

    [Fact]
    public void ParseUpload_VratiNaparsovanyEmail()
    {
        var res = Assert.IsType<OkObjectResult>(_ctrl.ParseUpload(Upload(Eml)));
        var email = Assert.IsType<ParsedEmail>(res.Value);
        Assert.Equal("Test", email.Subject);
    }

    [Fact]
    public void ParseUpload_ChybejiciSoubor_JeBadRequest()
    {
        Assert.IsType<BadRequestObjectResult>(_ctrl.ParseUpload(null));
    }

    [Fact]
    public void ParseUpload_PrazdnySoubor_JeBadRequest()
    {
        var prazdny = new FormFile(new MemoryStream(), 0, 0, "file", "a.eml");
        Assert.IsType<BadRequestObjectResult>(_ctrl.ParseUpload(prazdny));
    }

    [Fact]
    public void AttachmentByPath_VratiSouborKeStazeni()
    {
        var res = Assert.IsType<FileContentResult>(_ctrl.AttachmentByPath(WriteEml(), 0));
        Assert.Equal("pozn.txt", res.FileDownloadName);
        // Skutečný typ z hlaviček zprávy se ignoruje záměrně — viz komentář
        // u Attachment() v ToolsController.
        Assert.Equal("application/octet-stream", res.ContentType);
        Assert.Equal("obsah prilohy", Encoding.UTF8.GetString(res.FileContents).Trim());
    }

    [Fact]
    public void AttachmentByPath_MimoRozsah_JeNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(_ctrl.AttachmentByPath(WriteEml(), 42));
    }

    [Fact]
    public void AttachmentByPath_NeexistujiciSoubor_JeNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(
            _ctrl.AttachmentByPath(Path.Combine(_dir, "neni.eml"), 0));
    }

    [Fact]
    public void AttachmentUpload_VratiSouborKeStazeni()
    {
        var res = Assert.IsType<FileContentResult>(_ctrl.AttachmentUpload(Upload(Eml), 0));
        Assert.Equal("pozn.txt", res.FileDownloadName);
        Assert.Equal("application/octet-stream", res.ContentType);
        Assert.Equal("obsah prilohy", Encoding.UTF8.GetString(res.FileContents).Trim());
    }

    [Fact]
    public void AttachmentUpload_MimoRozsah_JeNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(_ctrl.AttachmentUpload(Upload(Eml), 42));
    }
}
