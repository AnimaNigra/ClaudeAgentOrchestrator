using System.Text;

namespace ClaudeOrchestrator.Services;

/// <summary>
/// Appends the raw PTY byte stream to a per-agent terminal.log. Keeps the file handle
/// open for the writer's lifetime and fsyncs on a 1s timer so a power loss costs at most
/// ~1s of raw output. Used by PtySession as a forensic "everything as shown" safety net.
/// </summary>
public sealed class TerminalLogWriter : IAsyncDisposable
{
    private readonly FileStream _fs;
    private readonly System.Timers.Timer _flushTimer;
    private readonly object _gate = new();
    private bool _disposed;

    public TerminalLogWriter(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        _flushTimer = new System.Timers.Timer(1000) { AutoReset = true };
        _flushTimer.Elapsed += (_, _) => SafeFlush();
        _flushTimer.Start();
    }

    public void Write(byte[] bytes)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _fs.Write(bytes, 0, bytes.Length);
        }
    }

    public void WriteMarker(string text)
        => Write(Encoding.UTF8.GetBytes($"\n{text}\n"));

    private void SafeFlush()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try { _fs.Flush(flushToDisk: true); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _flushTimer.Stop();
        _flushTimer.Dispose();
        try { _fs.Flush(flushToDisk: true); } catch { }
        await _fs.DisposeAsync();
    }
}
