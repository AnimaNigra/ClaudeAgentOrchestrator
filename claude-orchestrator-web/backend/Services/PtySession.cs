using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private readonly string _orchestratorUrl;
    private Process? _process;
    private bool _disposed;
    private string? _injectedSettingsPath;
    private bool _settingsCreatedByUs;

    public Action? OnExited { get; set; }

    // State detection
    private readonly StringBuilder _recentText = new(8192);
    private System.Timers.Timer? _idleTimer;
    private System.Timers.Timer? _forceIdleTimer;
    private AgentStatus _lastEmittedStatus = AgentStatus.Running;
    // After a PTY resize we suppress state-detection for 800ms so the terminal
    // redraw doesn't falsely flip an idle agent to Running.
    private long _resizeGraceUntilTick = 0;

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

    private static readonly Regex SessionIdRegex = new(
        @"--resume\s+([a-zA-Z0-9_-]+)",
        RegexOptions.Compiled);

    private static readonly string ProxyScript =
        Path.Combine(AppContext.BaseDirectory, "pty-proxy", "index.js");

    public PtySession(Agent agent, Func<string, string, object, Task> emitEvent,
        string orchestratorUrl = "http://localhost:5050")
    {
        _agent = agent;
        _emitEvent = emitEvent;
        _orchestratorUrl = orchestratorUrl;
    }

    // ── Startup ───────────────────────────────────────────────────────────

    public async Task StartAsync()
    {
        if (!File.Exists(ProxyScript))
            throw new FileNotFoundException(
                $"pty-proxy not found at {ProxyScript}. Run 'npm install' in backend/pty-proxy/.");

        var (claudeCmd, claudeArgs) = await ResolveClaudeAsync();

        // If resuming a previous session, append --resume <id>
        if (!string.IsNullOrEmpty(_agent.ResumeSessionId))
            claudeArgs = [..claudeArgs, "--resume", _agent.ResumeSessionId];

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

        // Hook env vars — picked up by stop.js / pre-tool.js / notification.js
        psi.EnvironmentVariables["CLAUDE_ORCHESTRATOR_URL"] = _orchestratorUrl;
        psi.EnvironmentVariables["CLAUDE_AGENT_ID"]         = _agent.Id;

        // Inject .claude/settings.json with hooks into the agent's working directory
        await InjectHooksAsync(_agent.Cwd ?? Directory.GetCurrentDirectory());

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

                    // Capture Claude Code session ID from exit message (only once)
                    if (string.IsNullOrEmpty(_agent.SessionId))
                    {
                        var match = SessionIdRegex.Match(text);
                        if (match.Success)
                            _agent.SessionId = match.Groups[1].Value;
                    }
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
        // Suppress state detection for 800ms after resize to ignore the redraw burst
        Volatile.Write(ref _resizeGraceUntilTick, Environment.TickCount64 + 800);
        await _process.StandardInput.WriteLineAsync($"RESIZE:{cols}x{rows}");
        await _process.StandardInput.FlushAsync();
    }

    // ── State detection ───────────────────────────────────────────────────

    private void FeedStateDetector(string text)
    {
        // Suppress during post-resize grace period so redraw bursts don't flip Idle→Running
        if (Environment.TickCount64 <= Volatile.Read(ref _resizeGraceUntilTick))
            return;

        lock (_recentText)
        {
            // Clear buffer when transitioning Idle→Running so old prompts don't match
            if (_agent.Status != AgentStatus.Running)
                _recentText.Clear();

            _agent.Status = AgentStatus.Running;

            _recentText.Append(text);
            if (_recentText.Length > 8192)
                _recentText.Remove(0, _recentText.Length - 8192);
        }

        // Fast path: 800ms debounce + regex check
        if (_idleTimer is null)
        {
            _idleTimer = new System.Timers.Timer(800) { AutoReset = false };
            _idleTimer.Elapsed += (_, _) => _ = CheckIdleAsync(force: false);
        }
        _idleTimer.Stop();
        _idleTimer.Start();

        // Fallback: after 3s of silence always mark idle (handles unrecognised prompts)
        if (_forceIdleTimer is null)
        {
            _forceIdleTimer = new System.Timers.Timer(3000) { AutoReset = false };
            _forceIdleTimer.Elapsed += (_, _) => _ = CheckIdleAsync(force: true);
        }
        _forceIdleTimer.Stop();
        _forceIdleTimer.Start();
    }

    private async Task CheckIdleAsync(bool force = false)
    {
        if (_disposed) return;

        AgentStatus newStatus;
        if (force)
        {
            newStatus = AgentStatus.Idle;
        }
        else
        {
            string snapshot;
            lock (_recentText)
                snapshot = _recentText.ToString();

            var plain = AnsiStrip.Replace(snapshot, "");
            newStatus = IdlePrompt.IsMatch(plain) ? AgentStatus.Idle : AgentStatus.Running;
        }

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
        _forceIdleTimer?.Dispose();
        _agent.Status = AgentStatus.Done;
        _agent.FinishedAt = DateTime.UtcNow;
        OnExited?.Invoke();
        _ = RemoveHooksAsync();
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
        _forceIdleTimer?.Dispose();
        await KillAsync();
        _process?.Dispose();
        await RemoveHooksAsync();
    }

    // ── Hooks injection ───────────────────────────────────────────────────

    private async Task InjectHooksAsync(string cwd)
    {
        var claudeDir    = Path.Combine(cwd, ".claude");
        var settingsPath = Path.Combine(claudeDir, "settings.json");

        _injectedSettingsPath = settingsPath;
        _settingsCreatedByUs  = !File.Exists(settingsPath);

        // Parse existing JSON (or start empty) and merge our hooks in
        JsonObject json;
        if (!_settingsCreatedByUs)
        {
            try
            {
                var existing = await File.ReadAllTextAsync(settingsPath);
                json = JsonNode.Parse(existing) as JsonObject ?? new JsonObject();
            }
            catch { json = new JsonObject(); _settingsCreatedByUs = true; }
        }
        else
        {
            json = new JsonObject();
        }

        if (json["hooks"] is not JsonObject hooksObj)
        {
            hooksObj = new JsonObject();
            json["hooks"] = hooksObj;
        }

        var url = $"{_orchestratorUrl}/api/agents/{_agent.Id}";
        AppendHook(hooksObj, "Stop",         $"curl -s -X POST \"{url}/hook/stop\"");
        AppendHook(hooksObj, "Notification", $"curl -s --data-binary @- -H \"Content-Type: application/json\" \"{url}/hook/notification\"");
        AppendHook(hooksObj, "PreToolUse",   $"curl -s --data-binary @- -H \"Content-Type: application/json\" \"{url}/hook/pre-tool\"");

        Directory.CreateDirectory(claudeDir);
        await File.WriteAllTextAsync(settingsPath,
            json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void AppendHook(JsonObject hooksObj, string eventType, string command)
    {
        var entry = new JsonObject
        {
            ["hooks"] = new JsonArray
            {
                new JsonObject { ["type"] = "command", ["command"] = command }
            }
        };

        if (hooksObj[eventType] is JsonArray arr)
            arr.Add(entry);
        else
            hooksObj[eventType] = new JsonArray { entry };
    }

    private async Task RemoveHooksAsync()
    {
        if (_injectedSettingsPath is null) return;
        try
        {
            if (!File.Exists(_injectedSettingsPath)) return;

            var currentContent = await File.ReadAllTextAsync(_injectedSettingsPath);
            var json = JsonNode.Parse(currentContent) as JsonObject ?? new JsonObject();

            // Remove only hooks that belong to this agent (identified by agent ID in the command URL)
            var agentUrlFragment = $"/api/agents/{_agent.Id}/";
            if (json["hooks"] is JsonObject hooksObj)
            {
                foreach (var key in hooksObj.Select(kv => kv.Key).ToList())
                {
                    if (hooksObj[key] is not JsonArray arr) continue;

                    var toKeep = arr
                        .OfType<JsonObject>()
                        .Where(entry =>
                        {
                            var hooks = entry["hooks"] as JsonArray;
                            return hooks == null || !hooks.OfType<JsonObject>().Any(h =>
                                h["command"]?.GetValue<string>()?.Contains(agentUrlFragment) == true);
                        })
                        .Select(e => JsonNode.Parse(e.ToJsonString()))
                        .ToArray();

                    if (toKeep.Length == 0)
                        hooksObj.Remove(key);
                    else
                    {
                        var newArr = new JsonArray();
                        foreach (var item in toKeep) newArr.Add(item);
                        hooksObj[key] = newArr;
                    }
                }

                if (!hooksObj.Any())
                    json.Remove("hooks");
            }

            // Delete file if now empty and we created it; otherwise write back
            if (!json.Any() && _settingsCreatedByUs)
            {
                File.Delete(_injectedSettingsPath);
                var dir = Path.GetDirectoryName(_injectedSettingsPath)!;
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            else
            {
                await File.WriteAllTextAsync(_injectedSettingsPath,
                    json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch { }
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
