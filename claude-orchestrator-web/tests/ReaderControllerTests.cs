using ClaudeOrchestrator.Controllers;
using ClaudeOrchestrator.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudeOrchestrator.Tests;

public class ReaderControllerTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "rct-" + Guid.NewGuid().ToString("N"));
    private readonly ReaderController _ctrl;
    private readonly FileWatcherService _watcher = new();

    public ReaderControllerTests()
    {
        Directory.CreateDirectory(_dir);
        _ctrl = new ReaderController(_watcher);
    }

    public void Dispose()
    {
        // FileWatcherService je IDisposable a drží FileSystemWatchery.
        _watcher.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string Write(string name, string content)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, System.Text.Encoding.UTF8.GetBytes(content));
        return p;
    }

    [Fact]
    public void GetContent_VratiObsahAMtime()
    {
        var p = Write("a.md", "# Nadpis");
        var res = Assert.IsType<OkObjectResult>(_ctrl.GetContent(p));
        var body = res.Value!;
        var type = body.GetType();
        Assert.Equal(p, type.GetProperty("path")!.GetValue(body));
        Assert.Equal("# Nadpis", type.GetProperty("content")!.GetValue(body));
        Assert.NotNull(type.GetProperty("mtime")!.GetValue(body));
    }

    [Fact]
    public void GetContent_PrazdnaCesta_JeBadRequest()
    {
        Assert.IsType<BadRequestObjectResult>(_ctrl.GetContent(""));
    }

    [Fact]
    public void GetContent_NepodporovanaPripona_JeBadRequest()
    {
        var p = Write("a.exe", "x");
        Assert.IsType<BadRequestObjectResult>(_ctrl.GetContent(p));
    }

    [Fact]
    public void GetContent_NeexistujiciSoubor_JeNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(
            _ctrl.GetContent(Path.Combine(_dir, "neni.md")));
    }

    [Theory]
    [InlineData("a.md")]
    [InlineData("a.markdown")]
    [InlineData("a.mdx")]
    [InlineData("a.txt")]
    public void GetContent_PovolenePripony_Projdou(string name)
    {
        var p = Write(name, "obsah");
        Assert.IsType<OkObjectResult>(_ctrl.GetContent(p));
    }

    [Fact]
    public void GetRaw_VratiSouborSeSpravnymContentType()
    {
        var p = Write("obr.png", "nejsou to fakt PNG bajty, ale to controller neřeší");
        var res = Assert.IsType<FileStreamResult>(_ctrl.GetRaw(p));
        Assert.Equal("image/png", res.ContentType);
        res.FileStream.Dispose();
    }

    [Fact]
    public void GetRaw_NepodporovanaPripona_JeBadRequest()
    {
        var p = Write("a.md", "x");
        Assert.IsType<BadRequestObjectResult>(_ctrl.GetRaw(p));
    }

    [Fact]
    public void GetRaw_NeexistujiciSoubor_JeNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(_ctrl.GetRaw(Path.Combine(_dir, "neni.png")));
    }

    private static string ErrorOf(IActionResult result)
    {
        var value = Assert.IsAssignableFrom<ObjectResult>(result).Value!;
        return (string)value.GetType().GetProperty("error")!.GetValue(value)!;
    }

    [Fact]
    public void GetContent_PrazdnaCesta_VraciAnglickouHlasku()
    {
        // Reader má anglické UI a hlášku zobrazuje přes alert(), takže se do ní
        // nesmí dostat česká zpráva ze sdílené validace.
        Assert.Equal("Invalid path", ErrorOf(_ctrl.GetContent("")));
    }

    [Fact]
    public void GetContent_Traversal_VraciAnglickouHlasku()
    {
        var p = Path.Combine(_dir, "sub", "..", "a.md");
        Assert.Equal("Path must not contain '..' segments", ErrorOf(_ctrl.GetContent(p)));
    }

    [Fact]
    public void GetContent_NepodporovanaPripona_VraciAnglickouHlasku()
    {
        var p = Write("a.exe", "x");
        Assert.Equal("Unsupported extension", ErrorOf(_ctrl.GetContent(p)));
    }
}
