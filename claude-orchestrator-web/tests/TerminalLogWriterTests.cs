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
}
