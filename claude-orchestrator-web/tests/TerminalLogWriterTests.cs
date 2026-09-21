using System.Text;
using ClaudeOrchestrator.Services;

namespace ClaudeOrchestrator.Tests;

public class TerminalLogWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tlw-" + Guid.NewGuid().ToString("N"));
    public TerminalLogWriterTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public async Task Write_ThenDispose_PersistsBytes()
    {
        var path = Path.Combine(_dir, "terminal.log");
        var w = new TerminalLogWriter(path);
        w.Write(Encoding.UTF8.GetBytes("hello "));
        w.Write(Encoding.UTF8.GetBytes("world"));
        await w.DisposeAsync();

        Assert.Equal("hello world", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task WriteMarker_AppendsText()
    {
        var path = Path.Combine(_dir, "terminal.log");
        var w = new TerminalLogWriter(path);
        w.Write(Encoding.UTF8.GetBytes("a"));
        w.WriteMarker("--- /clear ---");
        await w.DisposeAsync();

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("--- /clear ---", content);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var w = new TerminalLogWriter(Path.Combine(_dir, "terminal.log"));
        w.Write(Encoding.UTF8.GetBytes("x"));
        await w.DisposeAsync();
        await w.DisposeAsync();   // must not throw
    }

    // ── Size-based rotation ─────────────────────────────────────────────

    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);
    private string Current => Path.Combine(_dir, "terminal.log");
    private string Backup  => Path.Combine(_dir, "terminal.log.1");

    [Fact]
    public async Task Write_ThatWouldExceedMax_RotatesCurrentIntoBackupFirst()
    {
        var w = new TerminalLogWriter(Current, maxBytes: 10);
        w.Write(B("123456"));   // 6  – fits
        w.Write(B("abcdef"));   // 12 – rotate, then write into fresh file
        w.Write(B("new"));      // 9  – fits
        await w.DisposeAsync();

        Assert.Equal("123456",    await File.ReadAllTextAsync(Backup));
        Assert.Equal("abcdefnew", await File.ReadAllTextAsync(Current));
    }

    [Fact]
    public async Task SecondRotation_ReplacesPreviousBackup()
    {
        var w = new TerminalLogWriter(Current, maxBytes: 4);
        w.Write(B("aaaa"));
        w.Write(B("bbbb"));     // rotate: .1 = aaaa
        w.Write(B("cc"));       // rotate: .1 = bbbb
        await w.DisposeAsync();

        Assert.Equal("bbbb", await File.ReadAllTextAsync(Backup));
        Assert.Equal("cc",   await File.ReadAllTextAsync(Current));
        Assert.False(File.Exists(Backup + ".1"));   // never more than one backup
    }

    [Fact]
    public async Task PreExistingOversizedFile_RotatesOnFirstWrite()
    {
        await File.WriteAllTextAsync(Current, new string('o', 20));
        var w = new TerminalLogWriter(Current, maxBytes: 10);
        w.Write(B("x"));
        await w.DisposeAsync();

        Assert.Equal(20,  new FileInfo(Backup).Length);
        Assert.Equal("x", await File.ReadAllTextAsync(Current));
    }

    [Fact]
    public async Task SingleWriteLargerThanMax_DoesNotRotateEmptyFile()
    {
        var w = new TerminalLogWriter(Current, maxBytes: 4);
        w.Write(B("abcdefgh"));
        await w.DisposeAsync();

        Assert.False(File.Exists(Backup));
        Assert.Equal("abcdefgh", await File.ReadAllTextAsync(Current));
    }

    [Fact]
    public async Task WhenBackupRenameIsBlocked_StillCapsCurrentFile()
    {
        var w = new TerminalLogWriter(Current, maxBytes: 4);
        w.Write(B("aaaa"));

        // A reader without FileShare.Delete blocks File.Move on Windows.
        using (new FileStream(Current, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            w.Write(B("bbbb"));
        }
        await w.DisposeAsync();

        Assert.Equal("bbbb", await File.ReadAllTextAsync(Current));
    }
}
