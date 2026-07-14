using System.Collections.Concurrent;
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
    private bool _statuslineInjectedByUs;

    public Action? OnExited { get; set; }

    // Serializes writes to the proxy's stdin (WriteInputAsync + ResizeAsync)
    private readonly SemaphoreSlim _writeLock = new(1, 1);

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

    private static readonly string StatuslineScript =
        Path.Combine(AppContext.BaseDirectory, "hooks", "statusline.js");

    // Per-file lock to prevent concurrent read/write of settings.local.json
    // when multiple agents share the same CWD.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SettingsFileLocks = new();

    private readonly ConversationHistoryService? _conversationHistory;
    private TerminalLogWriter? _terminalLog;

    public PtySession(Agent agent, Func<string, string, object, Task> emitEvent,
        string orchestratorUrl = "http://localhost:5050",
        ConversationHistoryService? conversationHistory = null)
    {
        _agent = agent;
        _emitEvent = emitEvent;
        _orchestratorUrl = orchestratorUrl;
        _conversationHistory = conversationHistory;
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
        // When using Node.js entry point, resolve full path so node-pty can find it.
        psi.EnvironmentVariables["PTY_CMD"]  = claudeCmd == "node" ? nodePath : claudeCmd;
        psi.EnvironmentVariables["PTY_ARGS"] = JsonSerializer.Serialize(claudeArgs);
        psi.EnvironmentVariables["PTY_CWD"]  = _agent.Cwd ?? Directory.GetCurrentDirectory();
        psi.EnvironmentVariables["PTY_COLS"] = "220";
        psi.EnvironmentVariables["PTY_ROWS"] = "50";

        // Hook env vars — picked up by stop.js / pre-tool.js / notification.js
        psi.EnvironmentVariables["CLAUDE_ORCHESTRATOR_URL"] = _orchestratorUrl;
        psi.EnvironmentVariables["CLAUDE_AGENT_ID"]         = _agent.Id;

        // Claude Code 2.1.187+ grabs the mouse in fullscreen mode (clickable menus),
        // which makes the terminal forward mouse events instead of letting the user
        // drag-select text — breaking copy from the xterm.js terminal. Disable it so
        // selection/copy works; wheel scroll is unaffected. (Flag added in 2.1.195.)
        psi.EnvironmentVariables["CLAUDE_CODE_DISABLE_MOUSE_CLICKS"] = "1";

        // Inject .claude/settings.json with hooks into the agent's working directory
        await InjectHooksAsync(_agent.Cwd ?? Directory.GetCurrentDirectory());

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Exited += OnProcessExited;
        _process.Start();

        _agent.Pid    = _process.Id;
        _agent.Status = AgentStatus.Running;
        _terminalLog = _conversationHistory?.CreateTerminalLogWriter(_agent);

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
                    var bytes = Convert.FromBase64String(base64);
                    _terminalLog?.Write(bytes);
                    var text = Encoding.UTF8.GetString(bytes);
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
        await _writeLock.WaitAsync();
        try
        {
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(data));
            await _process.StandardInput.WriteLineAsync($"INPUT:{b64}");
            await _process.StandardInput.FlushAsync();
        }
        finally { _writeLock.Release(); }
    }

    public async Task ResizeAsync(int cols, int rows)
    {
        if (_process is null || _process.HasExited) return;
        // Suppress state detection for 800ms after resize to ignore the redraw burst
        Volatile.Write(ref _resizeGraceUntilTick, Environment.TickCount64 + 800);
        await _writeLock.WaitAsync();
        try
        {
            await _process.StandardInput.WriteLineAsync($"RESIZE:{cols}x{rows}");
            await _process.StandardInput.FlushAsync();
        }
        finally { _writeLock.Release(); }
    }

    // ── State detection ───────────────────────────────────────────────────

    private void FeedStateDetector(string text)
    {
        // Suppress during post-resize grace period so redraw bursts don't flip Idle→Running
        if (Environment.TickCount64 <= Volatile.Read(ref _resizeGraceUntilTick))
            return;

        bool transitionToRunning = false;
        lock (_recentText)
        {
            // Only transition to Running on first output after being Idle/Done,
            // not on every chunk (which would overwrite Idle status in pty_data events).
            if (_agent.Status == AgentStatus.Idle || _agent.Status == AgentStatus.Done)
            {
                _recentText.Clear();
                _agent.Status = AgentStatus.Running;
                transitionToRunning = true;
            }

            _recentText.Append(text);
            if (_recentText.Length > 8192)
                _recentText.Remove(0, _recentText.Length - 8192);
        }

        // Immediately emit Running so frontend updates without waiting for debounce
        if (transitionToRunning)
        {
            _lastEmittedStatus = AgentStatus.Running;
            _ = _emitEvent(_agent.Id, "agent_status_changed",
                new { agentId = _agent.Id, status = "running" });
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

    private async void OnProcessExited(object? sender, EventArgs e)
    {
        if (_disposed) return;
        _idleTimer?.Dispose();
        _forceIdleTimer?.Dispose();
        _agent.Status = AgentStatus.Done;
        _agent.FinishedAt = DateTime.UtcNow;
        // Emit event BEFORE OnExited removes the agent from the manager,
        // so the frontend receives the agent object with status=Done.
        await _emitEvent(_agent.Id, "agent_exited",
            new { agentId = _agent.Id, exitCode = _process?.ExitCode });
        OnExited?.Invoke();
        await RemoveHooksAsync();
        if (_terminalLog is not null) { await _terminalLog.DisposeAsync(); _terminalLog = null; }
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
        if (_terminalLog is not null) { await _terminalLog.DisposeAsync(); _terminalLog = null; }
    }

    // ── Hooks injection ───────────────────────────────────────────────────

    private async Task InjectHooksAsync(string cwd)
    {
        var claudeDir    = Path.Combine(cwd, ".claude");
        var settingsPath = Path.Combine(claudeDir, "settings.local.json");

        _injectedSettingsPath = settingsPath;
        var fileLock = SettingsFileLocks.GetOrAdd(settingsPath, _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync();
        try
        {
            _settingsCreatedByUs = !File.Exists(settingsPath);

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
            foreach (var (evt, cmd) in BuildOrchestratorHooks(url))
                AppendHook(hooksObj, evt, cmd);

            // Inject statusLine config to receive structured usage data
            if (json["statusLine"] is null)
            {
                var nodePath = FindNodeExe();
                json["statusLine"] = new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = $"\"{nodePath}\" \"{StatuslineScript}\"",
                };
                _statuslineInjectedByUs = true;
            }

            Directory.CreateDirectory(claudeDir);
            await File.WriteAllTextAsync(settingsPath,
                json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { fileLock.Release(); }
    }

    /// <summary>
    /// Orchestrator hook commands injected into the agent's settings.local.json.
    /// Each value is a shell command Claude Code runs for that event.
    /// <paramref name="agentUrl"/> is "{orchestratorUrl}/api/agents/{agentId}".
    /// </summary>
    public static IEnumerable<(string Event, string Command)> BuildOrchestratorHooks(string agentUrl)
    {
        const string post = "curl -s --data-binary @- -H \"Content-Type: application/json\"";
        yield return ("Stop",             $"{post} \"{agentUrl}/hook/stop\"");
        yield return ("Notification",     $"{post} \"{agentUrl}/hook/notification\"");
        yield return ("PreToolUse",       $"{post} \"{agentUrl}/hook/pre-tool\"");
        yield return ("UserPromptSubmit", $"{post} \"{agentUrl}/hook/user-prompt\"");
        yield return ("SessionStart",     $"{post} \"{agentUrl}/hook/session-start\"");
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
        var fileLock = SettingsFileLocks.GetOrAdd(_injectedSettingsPath, _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync();
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

            if (_statuslineInjectedByUs)
                json.Remove("statusLine");

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
        finally { fileLock.Release(); }
    }

    // ── Command resolution ────────────────────────────────────────────────

    /// <summary>
    /// Resolves how to launch Claude Code, independent of install method (npm global
    /// or native <c>install.ps1</c>) and independent of whether the backend process
    /// inherited an up-to-date PATH.
    ///
    /// Order (Windows), first real executable wins:
    ///   1. claude.exe found on PATH            — native install on PATH, custom installs
    ///   2. %USERPROFILE%\.local\bin\claude.exe — native installer default, even if PATH is stale
    ///   3. npm global ...\@anthropic-ai\claude-code\bin\claude.exe
    ///   4. npm global ...\@anthropic-ai\claude-code\cli.js  → node cli.js
    ///   5. bare "claude" (last resort — node-pty may report "File not found")
    ///
    /// node-pty (ConPTY) needs a real .exe: it cannot spawn npm's claude.ps1/.cmd shims,
    /// so the PATH walk deliberately looks only for claude.exe.
    /// </summary>
    private static async Task<(string cmd, string[] args)> ResolveClaudeAsync()
    {
        if (!OperatingSystem.IsWindows())
            return ("claude", []);

        var pathEnv     = Environment.GetEnvironmentVariable("PATH")        ?? "";
        var userProfile = Environment.GetEnvironmentVariable("USERPROFILE") ?? "";

        // Fast, in-process strategies first (PATH walk + known native location).
        if (ResolveClaudeFromPath(pathEnv, userProfile, File.Exists) is { } fromPath)
            return fromPath;

        // Fall back to asking npm for its global package dir (spawns a subprocess).
        string? npmRoot = null;
        try
        {
            npmRoot = (await RunCaptureAsync("cmd", "/c npm root -g")).Trim();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PtySession] 'npm root -g' failed while resolving claude: {ex.Message}");
        }

        if (ResolveClaudeFromNpm(npmRoot, File.Exists) is { } fromNpm)
            return fromNpm;

        Console.Error.WriteLine(
            "[PtySession] Could not locate a real claude executable via PATH, " +
            @"%USERPROFILE%\.local\bin, or npm global. Falling back to bare 'claude' — " +
            "node-pty may report 'File not found'. Install Claude Code so claude.exe is on " +
            "PATH, or run 'npm i -g @anthropic-ai/claude-code'.");

        return ("claude", []);
    }

    /// <summary>
    /// Resolves claude.exe from PATH or the native-installer default location.
    /// Returns null if no real executable is found. Extracted for unit testing.
    /// </summary>
    internal static (string cmd, string[] args)? ResolveClaudeFromPath(
        string pathEnv, string userProfile, Func<string, bool> fileExists)
    {
        // 1. A real claude.exe anywhere on PATH. We look only for the .exe on purpose:
        //    npm's claude.ps1/.cmd shims can't be spawned by node-pty (ConPTY).
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var trimmed = dir.Trim();
            if (trimmed.Length == 0) continue;
            var candidate = Path.Combine(trimmed, "claude.exe");
            if (fileExists(candidate)) return (candidate, []);
        }

        // 2. Native installer default, in case this process never got the refreshed PATH
        //    (services/scheduled tasks don't inherit a just-updated user PATH).
        if (userProfile.Length > 0)
        {
            var nativeExe = Path.Combine(userProfile, ".local", "bin", "claude.exe");
            if (fileExists(nativeExe)) return (nativeExe, []);
        }

        return null;
    }

    /// <summary>
    /// Resolves claude from an npm global package root (native binary or cli.js entry point).
    /// Returns null if npmRoot is empty or the package isn't there. Extracted for unit testing.
    /// </summary>
    internal static (string cmd, string[] args)? ResolveClaudeFromNpm(
        string? npmRoot, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(npmRoot)) return null;

        var packageDir = Path.Combine(npmRoot, "@anthropic-ai", "claude-code");

        // 3. Claude Code 2.x ships a native binary inside the npm package.
        var nativeExe = Path.Combine(packageDir, "bin", "claude.exe");
        if (fileExists(nativeExe)) return (nativeExe, []);

        // 4. Older versions used a Node.js entry point.
        var cliJs = Path.Combine(packageDir, "cli.js");
        if (fileExists(cliJs)) return ("node", [cliJs]);

        return null;
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
