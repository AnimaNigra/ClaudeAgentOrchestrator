using ClaudeOrchestrator.Services;

namespace ClaudeOrchestrator.Tests;

public class LocalFilePathTests : IDisposable
{
    private static readonly string[] Eml = { ".eml", ".msg" };

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "lfp-" + Guid.NewGuid().ToString("N"));

    public LocalFilePathTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Write(string name, string content = "x")
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public void ExistujiciSoubor_ProjdeAVratiPlnouCestu()
    {
        var p = Write("a.eml");
        var r = LocalFilePath.Validate(p, Eml);
        Assert.True(r.Ok);
        Assert.Equal(PathError.None, r.Error);
        Assert.Equal(p, r.FullPath);
        Assert.Null(r.Message);
    }

    [Fact]
    public void PrazdnyVstup_JeInvalid()
    {
        Assert.Equal(PathError.Invalid, LocalFilePath.Validate("   ", Eml).Error);
        Assert.Equal(PathError.Invalid, LocalFilePath.Validate(null, Eml).Error);
    }

    [Fact]
    public void ObalujiciUvozovky_SeOdstrani()
    {
        // Windows "Kopírovat jako cestu" vrací cestu v uvozovkách.
        var p = Write("b.eml");
        var r = LocalFilePath.Validate($"\"{p}\"", Eml);
        Assert.True(r.Ok);
        Assert.Equal(p, r.FullPath);
    }

    [Fact]
    public void SegmentDvouTecek_JeOdmitnut()
    {
        var p = Path.Combine(_dir, "sub", "..", "c.eml");
        Write("c.eml");
        var r = LocalFilePath.Validate(p, Eml);
        Assert.Equal(PathError.Traversal, r.Error);
        Assert.NotNull(r.Message);
    }

    [Fact]
    public void DvouteckyUvnitrJmenaSouboru_NevadiTraversalu()
    {
        // "a..b.eml" není traversal — kontroluje se celý segment, ne podřetězec.
        var p = Write("a..b.eml");
        Assert.True(LocalFilePath.Validate(p, Eml).Ok);
    }

    [Fact]
    public void NepovolenaPripona_JeOdmitnuta()
    {
        var p = Write("d.txt");
        Assert.Equal(PathError.UnsupportedExtension, LocalFilePath.Validate(p, Eml).Error);
    }

    [Fact]
    public void PriponaSeSrovnavaBezOhleduNaVelikost()
    {
        var p = Write("e.EML");
        Assert.True(LocalFilePath.Validate(p, Eml).Ok);
    }

    [Fact]
    public void NeexistujiciSoubor_JeNotFound()
    {
        var p = Path.Combine(_dir, "neni.eml");
        var r = LocalFilePath.Validate(p, Eml);
        Assert.Equal(PathError.NotFound, r.Error);
    }

    [Fact]
    public void PrilisVelkySoubor_JeTooLarge()
    {
        var p = Write("f.eml", new string('x', 100));
        var r = LocalFilePath.Validate(p, Eml, maxBytes: 10);
        Assert.Equal(PathError.TooLarge, r.Error);
    }

    [Fact]
    public void PoradiKontrol_PriponaPredExistenci()
    {
        // Neexistující soubor se špatnou příponou hlásí příponu: uživateli
        // je užitečnější slyšet "tohle neumím otevřít" než "soubor nenalezen".
        var p = Path.Combine(_dir, "neni.txt");
        Assert.Equal(PathError.UnsupportedExtension, LocalFilePath.Validate(p, Eml).Error);
    }
}
