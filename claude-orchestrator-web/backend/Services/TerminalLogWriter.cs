using System.Text;

namespace ClaudeOrchestrator.Services;

/// <summary>
/// Appends the raw PTY byte stream to a per-agent terminal.log. Keeps the file handle
/// open for the writer's lifetime and fsyncs on a 1s timer so a power loss costs at most
/// ~1s of raw output. Used by PtySession as a forensic "everything as shown" safety net.
///
/// The file is capped at <c>maxBytes</c>: a write that would push it past the cap first
/// rotates the current file to <c>terminal.log.1</c> (replacing the previous backup), so
/// an agent never holds more than ~2× the cap on disk while at least one cap's worth of
/// history stays readable.
/// </summary>
public sealed class TerminalLogWriter : IAsyncDisposable
{
    public const long DefaultMaxBytes = 50L * 1024 * 1024;

    private readonly string _path;
    private readonly long _maxBytes;
    private FileStream _fs;
    private long _length;
    private readonly System.Timers.Timer _flushTimer;
    private readonly object _gate = new();
    private bool _disposed;

    public TerminalLogWriter(string path, long maxBytes = DefaultMaxBytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _path = path;
        _maxBytes = maxBytes;
        _fs = Open(FileMode.Append);
        _length = _fs.Length;
        _flushTimer = new System.Timers.Timer(1000) { AutoReset = true };
        _flushTimer.Elapsed += (_, _) => SafeFlush();
        _flushTimer.Start();
    }

    private FileStream Open(FileMode mode)
        => new(_path, mode, FileAccess.Write, FileShare.Read);

    public void Write(byte[] bytes)
    {
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                if (_length > 0 && _length + bytes.Length > _maxBytes) Rotate();
                _fs.Write(bytes, 0, bytes.Length);
                _length += bytes.Length;
            }
            catch { /* forensic tee only — never let it break the PTY read loop */ }
        }
    }

    public void WriteMarker(string text)
        => Write(Encoding.UTF8.GetBytes($"\n{text}\n"));

    /// <summary>
    /// Current → .1 (dropping the older backup), then a fresh current file. If the rename
    /// is blocked (another process holds the file open without FileShare.Delete) the
    /// current file is truncated in place instead, so the cap holds either way.
    /// </summary>
    private void Rotate()
    {
        try { _fs.Flush(flushToDisk: true); } catch { }
        _fs.Dispose();
        try { File.Move(_path, _path + ".1", overwrite: true); }
        catch { /* fall through: FileMode.Create truncates the still-present file */ }
        _fs = Open(FileMode.Create);
        _length = 0;
    }

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
