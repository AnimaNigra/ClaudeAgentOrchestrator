using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClaudeOrchestrator.Models;

namespace ClaudeOrchestrator.Services;

/// <summary>
/// Manages one claude agent session running inside a real PTY (via node-pty proxy).
///
/// The proxy script (pty-proxy/index.js) spawns claude in a ConPTY on Windows
/// (xterm-256color on Linux/macOS) and bridges I/O over stdin/stdout using a
/// simple line-based protocol:
///
///   C# → proxy stdin:  INPUT:<base64>\n   RESIZE:<cols>x<rows>\n
///   proxy stdout → C#: DATA:<base64>\n
///
/// PTY output is forwarded to SignalR clients as-is (base64-encoded UTF-8 bytes).
/// State detection watches for claude's idle input-box prompt to flip Running↔Idle.
/// </summary>
public class PtySession : IAsyncDisposable
{
    private readonly Agent _agent;
    private readonly Func<string, string, object, Task> _emitEvent;
    private Process? _process;
    private bool _disposed;

    // State detection
    private readonly StringBuilder _recentText = new(8192);
    private System.Timers.Timer? _idleTimer;
    private AgentStatus _lastEmittedStatus = AgentStatus.Running;

    // Strips most common ANSI/VT escape sequences
    // Order matters: try CSI and OSC before the catch-all single-char alternative
    private static readonly Regex AnsiStrip = new(
        @"\x1B(?:\[[0-9;?]*[A-Za-z]|\][^\x07]*\x07|.)",
        RegexOptions.Compiled);

    // Matches any state where Claude is waiting for user input:
    //   │ >  — main chat input box
    //   ❯    — selection cursor in Claude's numbered option menus (permission prompts)
    //   [y/n] / [Y/n] / (y/n) — inline yes/no prompts
    private static readonly Regex IdlePrompt = new(
        @"[│|]\s{0,4}[>›]|❯|\[[Yy]/[Nn]\]|\([Yy]/[Nn]\)",
        RegexOptions.Compiled);

    private static readonly string ProxyScript =
        Path.Combine(AppContext.BaseDirectory, "pty-proxy", "index.js");

    public PtySession(Agent agent, Func<string, string, object, Task> emitEvent)
    {
        _agent = agent;
        _emitEvent = emitEvent;
    }

    // ── Startup ───────────────────────────────────────────────────────────

    public async Task StartAsync()
    {
        if (!File.Exists(ProxyScript))
            throw new FileNotFoundException(
                $"pty-proxy not found at {ProxyScript}. Run 'npm install' in backend/pty-proxy/.");

        var (claudeCmd, claudeArgs) = await ResolveClaudeAsync();
        var nodePath = FindNodeExe();

        var psi = new ProcessStartInfo
        {
            FileName = nodePath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _agent.Cwd ?? Directory.GetCurrentDirectory(),
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add(ProxyScript);

        // Pass target command via env so the proxy can spawn it in the PTY.
        // On Windows claudeCmd is "node" — use the full path so node-pty can find it.
        psi.EnvironmentVariables["PTY_CMD"]  = claudeCmd == "node" ? nodePath : claudeCmd;
        psi.EnvironmentVariables["PTY_ARGS"] = JsonSerializer.Serialize(claudeArgs);
        psi.EnvironmentVariables["PTY_CWD"]  = _agent.Cwd ?? Directory.GetCurrentDirectory();
        psi.EnvironmentVariables["PTY_COLS"] = "220";
        psi.EnvironmentVariables["PTY_ROWS"] = "50";

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Exited += OnProcessExited;
        _process.Start();

        _agent.Pid    = _process.Id;
        _agent.Status = AgentStatus.Running;

        _ = Task.Run(ReadLoopAsync);
        _ = Task.Run(ReadStderrLoopAsync);
    }

    // ── Read loops ────────────────────────────────────────────────────────

    private async Task ReadLoopAsync()
    {
        if (_process?.StandardOutput is null) return;
        try
        {
            while (!_process.HasExited)
            {
                var line = await _process.StandardOutput.ReadLineAsync();
                if (line is null) break;
                if (!line.StartsWith("DATA:")) continue;

                var base64 = line[5..];
                await _emitEvent(_agent.Id, "pty_data",
                    new { chunk = base64, agentId = _agent.Id });

                // Feed the state detector
                try
                {
                    var text = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                    FeedStateDetector(text);
                }
                catch { /* malformed base64 — ignore */ }
            }
        }
        catch (Exception ex)
        {
            if (!_disposed)
                await _emitEvent(_agent.Id, "agent_error", new { error = ex.Message });
        }
    }

    private async Task ReadStderrLoopAsync()
    {
        if (_process?.StandardError is null) return;
        try
        {
            while (!_process.HasExited)
            {
                var line = await _process.StandardError.ReadLineAsync();
                if (line is null) break;
                if (!_disposed && !string.IsNullOrWhiteSpace(line))
                    await _emitEvent(_agent.Id, "agent_stderr",
                        new { text = line, agentId = _agent.Id });
            }
        }
        catch { }
    }

    // ── Input ─────────────────────────────────────────────────────────────

    public async Task WriteInputAsync(string data)
    {
        if (_process is null || _process.HasExited) return;
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(data));
        await _process.StandardInput.WriteLineAsync($"INPUT:{b64}");
        await _process.StandardInput.FlushAsync();
    }

    public async Task ResizeAsync(int cols, int rows)
    {
        if (_process is null || _process.HasExited) return;
        await _process.StandardInput.WriteLineAsync($"RESIZE:{cols}x{rows}");
        await _process.StandardInput.FlushAsync();
    }

    // ── State detection ───────────────────────────────────────────────────

    private void FeedStateDetector(string text)
    {
        // Mark as running on any output
        if (_agent.Status != AgentStatus.Running)
            _agent.Status = AgentStatus.Running;

        lock (_recentText)
        {
            _recentText.Append(text);
            if (_recentText.Length > 8192)
                _recentText.Remove(0, _recentText.Length - 8192);
        }

        // Debounce: wait for output to stop, then check for idle prompt
        if (_idleTimer is null)
        {
            _idleTimer = new System.Timers.Timer(800) { AutoReset = false };
            _idleTimer.Elapsed += (_, _) => _ = CheckIdleAsync();
        }
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    private async Task CheckIdleAsync()
    {
        if (_disposed) return;

        string snapshot;
        lock (_recentText)
            snapshot = _recentText.ToString();

        var plain = AnsiStrip.Replace(snapshot, "");
        var newStatus = IdlePrompt.IsMatch(plain) ? AgentStatus.Idle : AgentStatus.Running;

        if (_lastEmittedStatus == newStatus) return;
        _lastEmittedStatus = newStatus;
        _agent.Status = newStatus;

        await _emitEvent(_agent.Id, "agent_status_changed",
            new { agentId = _agent.Id, status = newStatus.ToString().ToLower() });
    }

    // ── Process exit ──────────────────────────────────────────────────────

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_disposed) return;
        _idleTimer?.Dispose();
        _agent.Status = AgentStatus.Done;
        _agent.FinishedAt = DateTime.UtcNow;
        _ = _emitEvent(_agent.Id, "agent_exited",
            new { agentId = _agent.Id, exitCode = _process?.ExitCode });
    }

    public Task KillAsync()
    {
        try { _process?.Kill(entireProcessTree: true); }
        catch { }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _idleTimer?.Dispose();
        await KillAsync();
        _process?.Dispose();
    }

    // ── Command resolution ────────────────────────────────────────────────

    private static async Task<(string cmd, string[] args)> ResolveClaudeAsync()
    {
        if (!OperatingSystem.IsWindows())
            return ("claude", []);

        try
        {
            var npmRoot = await RunCaptureAsync("cmd", "/c npm root -g");
            npmRoot = npmRoot.Trim();
            var cliJs = Path.Combine(npmRoot, "@anthropic-ai", "claude-code", "cli.js");
            if (File.Exists(cliJs))
                return ("node", [cliJs]);
        }
        catch { }

        return ("claude", []);
    }

    private static string FindNodeExe()
    {
        if (!OperatingSystem.IsWindows()) return "node";
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var full = Path.Combine(dir.Trim(), "node.exe");
            if (File.Exists(full)) return full;
        }
        return "node";
    }

    private static async Task<string> RunCaptureAsync(string fileName, string args)
    {
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        return await p.StandardOutput.ReadToEndAsync();
    }
}
