using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Pty.Net;
using VtNetCore.VirtualTerminal;
using VtNetCore.XTermParser;
using ClaudeOrchestrator.WPF.Models;

namespace ClaudeOrchestrator.WPF.Services;

// Pty.Net 0.1.16-pre API notes:
//   PtyProvider.Spawn(command, width, height, workingDirectory, BackendOptions) -> IPtyConnection
//   IPtyConnection: events PtyData(sender, string data), PtyDisconnected(sender)
//                   methods Write(string), WriteAsync(string), Resize(int width, int height)
//   No Pid, no streams, no async spawn, no env var support in this version.
//
// VtNetCore 1.0.30 API notes:
//   DataConsumer.Push(byte[] data) — full array only
//   VirtualTerminalController.SendData is EventHandler<SendDataEventArgs> (field, not event keyword)
//   SendDataEventArgs.Data is byte[]
//   VirtualTerminalController.ResizeView(int columns, int rows) — exists

public class PtySessionService : IAsyncDisposable
{
    private readonly Agent _agent;
    private readonly string _orchestratorUrl;
    private IPtyConnection? _pty;
    private VirtualTerminalController? _vtController;
    private DataConsumer? _vtConsumer;
    private bool _disposed;

    // State detection
    private readonly StringBuilder _recentText = new(8192);
    private System.Timers.Timer? _idleTimer;
    private System.Timers.Timer? _forceIdleTimer;
    private AgentStatus _lastEmittedStatus = AgentStatus.Running;
    private long _resizeGraceUntilTick = 0;

    private static readonly Regex AnsiStrip = new(
        @"\x1B(?:\[[0-9;?]*[A-Za-z]|\][^\x07]*\x07|.)",
        RegexOptions.Compiled);
    private static readonly Regex IdlePrompt = new(
        @"[│|]\s{0,4}[>›]|❯|\[[Yy]/[Nn]\]|\([Yy]/[Nn]\)",
        RegexOptions.Compiled);
    private static readonly Regex SessionIdRegex = new(
        @"--resume\s+([a-zA-Z0-9_-]+)",
        RegexOptions.Compiled);

    public event Action<AgentStatus>? StatusChanged;
    public event Action? Exited;
    public event Action? ViewportInvalidated;

    public VirtualTerminalController? Terminal => _vtController;
    public Agent Agent => _agent;

    public PtySessionService(Agent agent, string orchestratorUrl = "http://localhost:5050")
    {
        _agent = agent;
        _orchestratorUrl = orchestratorUrl;
    }

    public Task StartAsync(int cols = 220, int rows = 50)
    {
        // Resolve command synchronously (the resolver is async but fast)
        return StartInternalAsync(cols, rows);
    }

    private async Task StartInternalAsync(int cols, int rows)
    {
        var (app, args) = await ClaudeResolver.ResolveAsync();

        if (!string.IsNullOrEmpty(_agent.ResumeSessionId))
            args = [.. args, "--resume", _agent.ResumeSessionId];

        // Pty.Net 0.1.16-pre does not support environment variable injection via Spawn.
        // We set env vars on the current process environment before spawning.
        // These will be inherited by the child process.
        Environment.SetEnvironmentVariable("CLAUDE_ORCHESTRATOR_URL", _orchestratorUrl);
        Environment.SetEnvironmentVariable("CLAUDE_AGENT_ID", _agent.Id);

        var cwd = _agent.Cwd ?? Directory.GetCurrentDirectory();

        // Build command string: if there are args, join them with the app
        // Pty.Net.Spawn takes a single command string; on Windows with node + script we
        // need to compose "node path\to\cli.js [extra args]"
        string command;
        if (args.Length > 0)
            command = app + " " + string.Join(" ", args.Select(QuoteArg));
        else
            command = app;

        _vtController = new VirtualTerminalController();
        _vtController.ResizeView(cols, rows);
        _vtConsumer = new DataConsumer(_vtController);

        // Wire VtNetCore -> PTY (terminal requesting to send bytes back to host, e.g. cursor report)
        _vtController.SendData += (sender, e) =>
        {
            if (_pty != null && e.Data != null)
            {
                var text = Encoding.UTF8.GetString(e.Data);
                _pty.Write(text);
            }
        };

        // Pty.Net 0.1.16-pre: synchronous spawn
        _pty = PtyProvider.Spawn(command, cols, rows, cwd, BackendOptions.Default);
        _agent.Status = AgentStatus.Running;

        // Hook PTY output -> VtNetCore
        _pty.PtyData += OnPtyData;
        _pty.PtyDisconnected += OnPtyDisconnected;
    }

    private void OnPtyData(object? sender, string data)
    {
        if (_disposed) return;

        // Convert string data to bytes for VtNetCore
        var bytes = Encoding.UTF8.GetBytes(data);
        try { _vtConsumer!.Push(bytes); } catch { /* ignore parse errors */ }
        ViewportInvalidated?.Invoke();

        FeedStateDetector(data);

        if (string.IsNullOrEmpty(_agent.SessionId))
        {
            var m = SessionIdRegex.Match(data);
            if (m.Success) _agent.SessionId = m.Groups[1].Value;
        }
    }

    private void OnPtyDisconnected(object? sender)
    {
        if (_disposed) return;
        _agent.Status = AgentStatus.Done;
        _agent.FinishedAt = DateTime.UtcNow;
        Exited?.Invoke();
    }

    public async Task WriteAsync(string text)
    {
        if (_pty is null) return;
        await _pty.WriteAsync(text);
    }

    public Task WriteAsync(byte[] bytes)
    {
        if (_pty is null) return Task.CompletedTask;
        var text = Encoding.UTF8.GetString(bytes);
        return _pty.WriteAsync(text);
    }

    public Task ResizeAsync(int cols, int rows)
    {
        if (_pty is null) return Task.CompletedTask;
        Volatile.Write(ref _resizeGraceUntilTick, Environment.TickCount64 + 800);
        _vtController?.ResizeView(cols, rows);
        _pty.Resize(cols, rows);  // synchronous in this version
        return Task.CompletedTask;
    }

    private void FeedStateDetector(string text)
    {
        if (Environment.TickCount64 <= Volatile.Read(ref _resizeGraceUntilTick))
            return;

        lock (_recentText)
        {
            if (_agent.Status != AgentStatus.Running)
                _recentText.Clear();
            _agent.Status = AgentStatus.Running;
            _recentText.Append(text);
            if (_recentText.Length > 8192)
                _recentText.Remove(0, _recentText.Length - 8192);
        }

        if (_idleTimer is null)
        {
            _idleTimer = new System.Timers.Timer(800) { AutoReset = false };
            _idleTimer.Elapsed += (_, _) => CheckIdle(force: false);
        }
        _idleTimer.Stop(); _idleTimer.Start();

        if (_forceIdleTimer is null)
        {
            _forceIdleTimer = new System.Timers.Timer(3000) { AutoReset = false };
            _forceIdleTimer.Elapsed += (_, _) => CheckIdle(force: true);
        }
        _forceIdleTimer.Stop(); _forceIdleTimer.Start();
    }

    private void CheckIdle(bool force)
    {
        if (_disposed) return;
        AgentStatus newStatus;
        if (force)
            newStatus = AgentStatus.Idle;
        else
        {
            string snapshot;
            lock (_recentText) snapshot = _recentText.ToString();
            var plain = AnsiStrip.Replace(snapshot, "");
            newStatus = IdlePrompt.IsMatch(plain) ? AgentStatus.Idle : AgentStatus.Running;
        }
        if (_lastEmittedStatus == newStatus) return;
        _lastEmittedStatus = newStatus;
        _agent.Status = newStatus;
        StatusChanged?.Invoke(newStatus);
    }

    public void Kill() => _pty?.Dispose();

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _idleTimer?.Dispose();
        _forceIdleTimer?.Dispose();
        if (_pty != null)
        {
            _pty.PtyData -= OnPtyData;
            _pty.PtyDisconnected -= OnPtyDisconnected;
            _pty.Dispose();
        }
        await Task.CompletedTask;
    }

    private static string QuoteArg(string arg)
    {
        // Wrap in quotes if the arg contains spaces and isn't already quoted
        if (arg.Contains(' ') && !arg.StartsWith('"'))
            return $"\"{arg}\"";
        return arg;
    }
}
