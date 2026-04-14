using System.Collections.Concurrent;

namespace ClaudeOrchestrator.Services;

public class FileWatcherService : IDisposable
{
    public event Action<string, long>? FileChanged;
    public event Action<string>? WatchFailed;

    private readonly ConcurrentDictionary<string, WatchEntry> _entries = new();
    private readonly object _lock = new();

    private class WatchEntry
    {
        public FileSystemWatcher Watcher = null!;
        public int RefCount;
        public int ConsecutiveFailures;
        public System.Timers.Timer? Debounce;
    }

    public void Watch(string absolutePath)
    {
        var normalized = Path.GetFullPath(absolutePath);
        lock (_lock)
        {
            if (_entries.TryGetValue(normalized, out var existing))
            {
                existing.RefCount++;
                return;
            }

            var dir = Path.GetDirectoryName(normalized)!;
            var name = Path.GetFileName(normalized);
            var w = new FileSystemWatcher(dir, name)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            var entry = new WatchEntry { Watcher = w, RefCount = 1 };

            w.Changed += (_, __) => OnChanged(normalized, entry);
            w.Error += (_, e) => OnError(normalized, entry);

            _entries[normalized] = entry;
        }
    }

    public void Unwatch(string absolutePath)
    {
        var normalized = Path.GetFullPath(absolutePath);
        lock (_lock)
        {
            if (!_entries.TryGetValue(normalized, out var entry)) return;
            entry.RefCount--;
            if (entry.RefCount <= 0)
            {
                entry.Watcher.EnableRaisingEvents = false;
                entry.Watcher.Dispose();
                entry.Debounce?.Dispose();
                _entries.TryRemove(normalized, out _);
            }
        }
    }

    private void OnChanged(string path, WatchEntry entry)
    {
        // Debounce 100ms — editors often write twice in rapid succession.
        entry.Debounce?.Stop();
        entry.Debounce?.Dispose();
        var t = new System.Timers.Timer(100) { AutoReset = false };
        entry.Debounce = t;
        t.Elapsed += (_, __) =>
        {
            try
            {
                if (!File.Exists(path)) return;
                var mtime = new DateTimeOffset(File.GetLastWriteTimeUtc(path))
                    .ToUnixTimeMilliseconds();
                entry.ConsecutiveFailures = 0;
                FileChanged?.Invoke(path, mtime);
            }
            catch
            {
                entry.ConsecutiveFailures++;
                if (entry.ConsecutiveFailures >= 3)
                    WatchFailed?.Invoke(path);
            }
        };
        t.Start();
    }

    private void OnError(string path, WatchEntry entry)
    {
        entry.ConsecutiveFailures++;
        if (entry.ConsecutiveFailures >= 3)
            WatchFailed?.Invoke(path);
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values)
        {
            entry.Watcher.Dispose();
            entry.Debounce?.Dispose();
        }
        _entries.Clear();
    }
}
