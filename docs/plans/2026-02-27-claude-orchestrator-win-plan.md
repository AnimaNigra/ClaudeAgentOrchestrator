# Claude Orchestrator Win — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** WPF .NET 9 desktopová aplikace pro multi-agent orchestraci Claude CLI — bez JS, bez ASP.NET, bez node-pty.

**Architecture:** Pty.Net (ConPTY wrapper) spouští Claude přímo z .NET procesu. VtNetCore parsuje VT100/xterm-256color výstup do gridu buněk. WPF DrawingVisual renderuje grid bez jakéhokoliv JS. HttpListener naslouchá Claude hooks (stop/notification/pre-tool) na localhost:5050.

**Tech Stack:** WPF .NET 9-windows, `Pty.Net` (NuGet), `VtNetCore` (NuGet), `Microsoft.Data.Sqlite` (NuGet), `System.Net.HttpListener` (built-in).

**Design doc:** `docs/plans/2026-02-27-claude-orchestrator-win-design.md`

---

## Důležité poznámky před začátkem

### Pty.Net API
```csharp
// NuGet: Pty.Net
using Pty.Net;
var options = new PtyOptions {
    Name = "xterm-256color",
    Cols = 220, Rows = 50,
    Cwd = cwd,
    App = nodePath,
    CommandLine = new[] { cliJsPath },   // nebo jen claudePath, []
    EnvironmentVariables = new Dictionary<string, string> { ... }
};
IPtyConnection pty = await PtyProvider.SpawnAsync(options, CancellationToken.None);
// pty.ReaderStream  — Stream pro čtení PTY output
// pty.WriterStream  — Stream pro zápis input
// await pty.ResizeAsync(cols, rows, CancellationToken.None);
// pty.Dispose()     — zabije proces
```

### VtNetCore API
```csharp
// NuGet: VtNetCore
using VtNetCore.VirtualTerminal;
using VtNetCore.XTermParser;
var controller = new VirtualTerminalController();
var consumer = new DataConsumer(controller);
controller.SendData += (sender, e) => { /* odpovědi (např. cursor position report) → zpět do PTY WriterStream */ };
// Feed bytes:
consumer.Push(bytes, 0, length);
// Čtení viewportu:
var vp = controller.ViewPort;
// vp.ColumnCount, vp.RowCount
// vp[col, row] → TerminalCharacter (c.Character, c.Attributes.ForegroundColor, c.Attributes.BackgroundColor, c.Attributes.Bright)
// vp.CursorPosition → Position (Row, Column)
// Scrollback: controller.Buffer.Lines → všechny řádky
```

### WPF projekt — csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

### Claude spuštění (Windows)
Stejná logika jako v `claude-orchestrator-web/backend/Services/PtySession.cs`:
1. `npm root -g` → najdi `@anthropic-ai/claude-code/cli.js`
2. Pokud existuje: `App = node.exe`, `CommandLine = [cliJsPath]`
3. Jinak: `App = "claude"`, `CommandLine = []`

---

## Task 1: Project scaffold

**Files:**
- Create: `claude-orchestrator-win/ClaudeOrchestrator.sln`
- Create: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/ClaudeOrchestrator.WPF.csproj`
- Create: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/App.xaml`
- Create: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/App.xaml.cs`
- Create: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/MainWindow.xaml`
- Create: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/MainWindow.xaml.cs`

**Step 1: Vytvoř adresář a WPF projekt**

```bash
cd claude-orchestrator-win
dotnet new sln -n ClaudeOrchestrator
dotnet new wpf -n ClaudeOrchestrator.WPF -f net9.0-windows -o src/ClaudeOrchestrator.WPF
dotnet sln add src/ClaudeOrchestrator.WPF/ClaudeOrchestrator.WPF.csproj
```

**Step 2: Přidej NuGet balíčky**

```bash
cd src/ClaudeOrchestrator.WPF
dotnet add package Pty.Net
dotnet add package VtNetCore
dotnet add package Microsoft.Data.Sqlite
```

**Step 3: Ověř build**

```bash
dotnet build
```
Očekávaný výstup: `Build succeeded.`

**Step 4: Uprav App.xaml.cs — základní startup**

```csharp
// App.xaml.cs
using System.Windows;

namespace ClaudeOrchestrator.WPF;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var mainWindow = new MainWindow();
        mainWindow.Show();
    }
}
```

**Step 5: Ověř spuštění**

```bash
dotnet run
```
Očekávaný výstup: prázdné WPF okno se otevře.

**Step 6: Commit**

```bash
git add claude-orchestrator-win/
git commit -m "feat: scaffold WPF project claude-orchestrator-win"
```

---

## Task 2: Models

**Files:**
- Create: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/Models/Agent.cs`
- Create: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/Models/AgentRecord.cs`
- Create: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/Models/PermissionRequest.cs`

**Step 1: Vytvoř Agent.cs**

```csharp
// Models/Agent.cs
namespace ClaudeOrchestrator.WPF.Models;

public enum AgentStatus { Running, Idle, Done }

public class Agent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string? Cwd { get; set; }
    public string? ResumeSessionId { get; set; }
    public AgentStatus Status { get; set; } = AgentStatus.Running;
    public int? Pid { get; set; }
    public string? SessionId { get; set; }      // zachyceno z PTY výstupu
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
}
```

**Step 2: Vytvoř AgentRecord.cs**

```csharp
// Models/AgentRecord.cs
namespace ClaudeOrchestrator.WPF.Models;

public class AgentRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Cwd { get; set; }
    public string? SessionId { get; set; }
    public string? Notes { get; set; }
    public DateTime? FinishedAt { get; set; }
}
```

**Step 3: Vytvoř PermissionRequest.cs**

```csharp
// Models/PermissionRequest.cs
namespace ClaudeOrchestrator.WPF.Models;

public class PermissionRequest
{
    public string RequestId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public object? ToolInput { get; set; }
}
```

**Step 4: Build**

```bash
dotnet build
```

**Step 5: Commit**

```bash
git add claude-orchestrator-win/
git commit -m "feat: add Agent/AgentRecord/PermissionRequest models"
```

---

## Task 3: ClaudeResolver

**Files:**
- Create: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/Services/ClaudeResolver.cs`

**Step 1: Vytvoř ClaudeResolver.cs**

```csharp
// Services/ClaudeResolver.cs
using System.Diagnostics;

namespace ClaudeOrchestrator.WPF.Services;

public static class ClaudeResolver
{
    // Vrací (nodePath, cliJsPath?) nebo (null, null) → použij "claude" přímo
    public static async Task<(string app, string[] args)> ResolveAsync()
    {
        if (!OperatingSystem.IsWindows())
            return ("claude", Array.Empty<string>());

        try
        {
            var npmRoot = await RunAsync("cmd", "/c npm root -g");
            npmRoot = npmRoot.Trim();
            var cliJs = Path.Combine(npmRoot, "@anthropic-ai", "claude-code", "cli.js");
            if (File.Exists(cliJs))
            {
                var node = FindNodeExe();
                return (node, new[] { cliJs });
            }
        }
        catch { }

        return ("claude", Array.Empty<string>());
    }

    public static string FindNodeExe()
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

    private static async Task<string> RunAsync(string fileName, string args)
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
```

**Step 2: Ručně ověř**

```bash
dotnet build
```

Bez testů — logika je identická s ověřenou web verzí v `PtySession.cs:ResolveClaudeAsync()`.

**Step 3: Commit**

```bash
git add claude-orchestrator-win/
git commit -m "feat: add ClaudeResolver service"
```

---

## Task 4: HooksInjector

**Files:**
- Create: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/Services/HooksInjector.cs`

Logika je identická s `PtySession.cs:InjectHooksAsync()` a `RemoveHooksAsync()` — zkopíruj a adaptuj.

**Step 1: Vytvoř HooksInjector.cs**

```csharp
// Services/HooksInjector.cs
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeOrchestrator.WPF.Services;

public class HooksInjector(string agentId, string orchestratorUrl)
{
    private string? _settingsPath;
    private bool _createdByUs;

    public async Task InjectAsync(string cwd)
    {
        var claudeDir = Path.Combine(cwd, ".claude");
        _settingsPath = Path.Combine(claudeDir, "settings.json");
        _createdByUs = !File.Exists(_settingsPath);

        JsonObject json;
        if (!_createdByUs)
        {
            try
            {
                var text = await File.ReadAllTextAsync(_settingsPath);
                json = JsonNode.Parse(text) as JsonObject ?? new JsonObject();
            }
            catch { json = new JsonObject(); _createdByUs = true; }
        }
        else json = new JsonObject();

        if (json["hooks"] is not JsonObject hooksObj)
        {
            hooksObj = new JsonObject();
            json["hooks"] = hooksObj;
        }

        var url = $"{orchestratorUrl}/api/agents/{agentId}";
        AppendHook(hooksObj, "Stop",         $"curl -s -X POST \"{url}/hook/stop\"");
        AppendHook(hooksObj, "Notification", $"curl -s --data-binary @- -H \"Content-Type: application/json\" \"{url}/hook/notification\"");
        AppendHook(hooksObj, "PreToolUse",   $"curl -s --data-binary @- -H \"Content-Type: application/json\" \"{url}/hook/pre-tool\"");

        Directory.CreateDirectory(claudeDir);
        await File.WriteAllTextAsync(_settingsPath,
            json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public async Task RemoveAsync()
    {
        if (_settingsPath is null || !File.Exists(_settingsPath)) return;
        try
        {
            var text = await File.ReadAllTextAsync(_settingsPath);
            var json = JsonNode.Parse(text) as JsonObject ?? new JsonObject();
            var fragment = $"/api/agents/{agentId}/";

            if (json["hooks"] is JsonObject hooksObj)
            {
                foreach (var key in hooksObj.Select(kv => kv.Key).ToList())
                {
                    if (hooksObj[key] is not JsonArray arr) continue;
                    var keep = arr.OfType<JsonObject>()
                        .Where(e => {
                            var hooks = e["hooks"] as JsonArray;
                            return hooks == null || !hooks.OfType<JsonObject>().Any(h =>
                                h["command"]?.GetValue<string>()?.Contains(fragment) == true);
                        })
                        .Select(e => JsonNode.Parse(e.ToJsonString()))
                        .ToArray();
                    if (keep.Length == 0) hooksObj.Remove(key);
                    else { var a = new JsonArray(); foreach (var i in keep) a.Add(i); hooksObj[key] = a; }
                }
                if (!hooksObj.Any()) json.Remove("hooks");
            }

            if (!json.Any() && _createdByUs)
            {
                File.Delete(_settingsPath);
                var dir = Path.GetDirectoryName(_settingsPath)!;
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            else
                await File.WriteAllTextAsync(_settingsPath,
                    json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
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
        if (hooksObj[eventType] is JsonArray arr) arr.Add(entry);
        else hooksObj[eventType] = new JsonArray { entry };
    }
}
```

**Step 2: Build**

```bash
dotnet build
```

**Step 3: Commit**

```bash
git add claude-orchestrator-win/
git commit -m "feat: add HooksInjector service"
```

---

## Task 5: HistoryService (SQLite)

**Files:**
- Create: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/Services/HistoryService.cs`

**Step 1: Vytvoř HistoryService.cs**

```csharp
// Services/HistoryService.cs
using Microsoft.Data.Sqlite;
using ClaudeOrchestrator.WPF.Models;

namespace ClaudeOrchestrator.WPF.Services;

public class HistoryService
{
    private readonly string _dbPath;

    public HistoryService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClaudeOrchestratorWin");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "history.db");
        InitDb();
    }

    private void InitDb()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS agent_records (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                cwd TEXT,
                session_id TEXT,
                notes TEXT,
                finished_at TEXT
            )
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task SaveAsync(AgentRecord record)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO agent_records (id, name, cwd, session_id, notes, finished_at)
            VALUES ($id, $name, $cwd, $sessionId, $notes, $finishedAt)
            """;
        cmd.Parameters.AddWithValue("$id", record.Id);
        cmd.Parameters.AddWithValue("$name", record.Name);
        cmd.Parameters.AddWithValue("$cwd", (object?)record.Cwd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sessionId", (object?)record.SessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)record.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$finishedAt", record.FinishedAt?.ToString("O") ?? (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<AgentRecord>> GetAllAsync()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, cwd, session_id, notes, finished_at FROM agent_records ORDER BY finished_at DESC";
        var result = new List<AgentRecord>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new AgentRecord
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Cwd = reader.IsDBNull(2) ? null : reader.GetString(2),
                SessionId = reader.IsDBNull(3) ? null : reader.GetString(3),
                Notes = reader.IsDBNull(4) ? null : reader.GetString(4),
                FinishedAt = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5)),
            });
        }
        return result;
    }

    public async Task UpdateNotesAsync(string id, string notes)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE agent_records SET notes=$notes WHERE id=$id";
        cmd.Parameters.AddWithValue("$notes", notes);
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM agent_records WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }
}
```

**Step 2: Build**

```bash
dotnet build
```

**Step 3: Commit**

```bash
git add claude-orchestrator-win/
git commit -m "feat: add HistoryService with SQLite"
```

---

## Task 6: PtySessionService

Toto je srdce aplikace. Pty.Net spouští Claude v ConPTY, VtNetCore parsuje output.

**Files:**
- Create: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/Services/PtySessionService.cs`

**Step 1: Vytvoř PtySessionService.cs**

```csharp
// Services/PtySessionService.cs
using System.Text;
using System.Text.RegularExpressions;
using Pty.Net;
using VtNetCore.VirtualTerminal;
using VtNetCore.XTermParser;
using ClaudeOrchestrator.WPF.Models;

namespace ClaudeOrchestrator.WPF.Services;

public class PtySessionService : IAsyncDisposable
{
    private readonly Agent _agent;
    private readonly string _orchestratorUrl;
    private IPtyConnection? _pty;
    private VirtualTerminalController? _vtController;
    private DataConsumer? _vtConsumer;
    private bool _disposed;

    // State detection (stejná logika jako web verze)
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

    // Events
    public event Action<AgentStatus>? StatusChanged;
    public event Action? Exited;
    public event Action? ViewportInvalidated;    // TerminalView se přihlásí a zavolá Render()

    // Pro čtení viewportu z TerminalView
    public VirtualTerminalController? Terminal => _vtController;

    public PtySessionService(Agent agent, string orchestratorUrl = "http://localhost:5050")
    {
        _agent = agent;
        _orchestratorUrl = orchestratorUrl;
    }

    public async Task StartAsync(int cols = 220, int rows = 50)
    {
        var (app, args) = await ClaudeResolver.ResolveAsync();

        // Přidej --resume pokud resumujeme session
        if (!string.IsNullOrEmpty(_agent.ResumeSessionId))
            args = [..args, "--resume", _agent.ResumeSessionId];

        // Env vars pro hooks
        var env = new Dictionary<string, string>(
            System.Environment.GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .Where(e => e.Key is string && e.Value is string)
                .ToDictionary(e => (string)e.Key, e => (string)e.Value!))
        {
            ["CLAUDE_ORCHESTRATOR_URL"] = _orchestratorUrl,
            ["CLAUDE_AGENT_ID"] = _agent.Id,
        };

        var options = new PtyOptions
        {
            Name = "xterm-256color",
            Cols = cols,
            Rows = rows,
            Cwd = _agent.Cwd ?? Directory.GetCurrentDirectory(),
            App = app,
            CommandLine = args,
            EnvironmentVariables = env,
        };

        _vtController = new VirtualTerminalController();
        _vtConsumer = new DataConsumer(_vtController);

        // Odpovědi z terminálu (cursor position report atd.) jdou zpátky do PTY
        _vtController.SendData += async (_, e) =>
        {
            if (_pty != null && e.Data != null)
                await _pty.WriterStream.WriteAsync(e.Data);
        };

        _pty = await PtyProvider.SpawnAsync(options, CancellationToken.None);
        _agent.Pid = _pty.Pid;
        _agent.Status = AgentStatus.Running;

        _ = Task.Run(ReadLoopAsync);
    }

    private async Task ReadLoopAsync()
    {
        if (_pty is null) return;
        var buffer = new byte[4096];
        try
        {
            while (!_disposed)
            {
                var read = await _pty.ReaderStream.ReadAsync(buffer);
                if (read == 0) break;

                var chunk = buffer[..read];

                // Feed VtNetCore
                _vtConsumer!.Push(chunk, 0, read);
                ViewportInvalidated?.Invoke();   // Trigger WPF render

                // State detection
                var text = Encoding.UTF8.GetString(chunk);
                FeedStateDetector(text);

                // Capture session ID
                if (string.IsNullOrEmpty(_agent.SessionId))
                {
                    var m = SessionIdRegex.Match(text);
                    if (m.Success) _agent.SessionId = m.Groups[1].Value;
                }
            }
        }
        catch { }
        finally
        {
            if (!_disposed)
            {
                _agent.Status = AgentStatus.Done;
                _agent.FinishedAt = DateTime.UtcNow;
                Exited?.Invoke();
            }
        }
    }

    public async Task WriteAsync(string text)
    {
        if (_pty is null) return;
        var bytes = Encoding.UTF8.GetBytes(text);
        await _pty.WriterStream.WriteAsync(bytes);
    }

    public async Task WriteAsync(byte[] bytes)
    {
        if (_pty is null) return;
        await _pty.WriterStream.WriteAsync(bytes);
    }

    public async Task ResizeAsync(int cols, int rows)
    {
        if (_pty is null) return;
        Volatile.Write(ref _resizeGraceUntilTick, Environment.TickCount64 + 800);
        _vtController?.ResizeView(cols, rows);
        await _pty.ResizeAsync(cols, rows, CancellationToken.None);
    }

    // State detection — stejná logika jako PtySession.cs ve web verzi
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
        _pty?.Dispose();
        await Task.CompletedTask;
    }
}
```

**Step 2: Build**

```bash
dotnet build
```

Pokud VtNetCore API nesedí (různé verze mají různý namespace) — zkontroluj NuGet package browser nebo `dotnet list package` pro přesnou verzi a uprav namespace.

**Step 3: Commit**

```bash
git add claude-orchestrator-win/
git commit -m "feat: add PtySessionService with Pty.Net + VtNetCore"
```

---

## Task 7: AgentManager

**Files:**
- Create: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/Services/AgentManager.cs`

**Step 1: Vytvoř AgentManager.cs**

```csharp
// Services/AgentManager.cs
using System.Collections.ObjectModel;
using ClaudeOrchestrator.WPF.Models;

namespace ClaudeOrchestrator.WPF.Services;

public class AgentManager
{
    private readonly string _orchestratorUrl;
    private readonly HistoryService _history;
    private readonly Dictionary<string, PtySessionService> _sessions = new();

    // ObservableCollection pro WPF binding
    public ObservableCollection<Agent> Agents { get; } = new();

    public event Action<Agent, AgentStatus>? AgentStatusChanged;
    public event Action<Agent>? AgentExited;

    public AgentManager(string orchestratorUrl, HistoryService history)
    {
        _orchestratorUrl = orchestratorUrl;
        _history = history;
    }

    public async Task<Agent> SpawnAsync(string name, string? cwd, string? resumeSessionId = null)
    {
        var agent = new Agent
        {
            Name = name,
            Cwd = cwd,
            ResumeSessionId = resumeSessionId,
        };

        var session = new PtySessionService(agent, _orchestratorUrl);
        _sessions[agent.Id] = session;

        // Inject hooks před spuštěním
        var injector = new HooksInjector(agent.Id, _orchestratorUrl);
        await injector.InjectAsync(cwd ?? Directory.GetCurrentDirectory());

        session.StatusChanged += status =>
        {
            agent.Status = status;
            AgentStatusChanged?.Invoke(agent, status);
        };

        session.Exited += async () =>
        {
            // Ulož do history
            await _history.SaveAsync(new AgentRecord
            {
                Id = agent.Id,
                Name = agent.Name,
                Cwd = agent.Cwd,
                SessionId = agent.SessionId,
                FinishedAt = agent.FinishedAt,
            });
            await injector.RemoveAsync();

            // Odstraň z Agents po 2s (aby user viděl Done stav)
            await Task.Delay(2000);
            App.Current.Dispatcher.Invoke(() =>
            {
                Agents.Remove(agent);
                _sessions.Remove(agent.Id);
            });

            AgentExited?.Invoke(agent);
        };

        await session.StartAsync();

        App.Current.Dispatcher.Invoke(() => Agents.Add(agent));
        return agent;
    }

    public PtySessionService? GetSession(string agentId)
        => _sessions.TryGetValue(agentId, out var s) ? s : null;

    public async Task KillAsync(string agentId)
    {
        if (_sessions.TryGetValue(agentId, out var s))
            await s.DisposeAsync();
    }
}
```

**Step 2: Build**

```bash
dotnet build
```

**Step 3: Commit**

```bash
git add claude-orchestrator-win/
git commit -m "feat: add AgentManager service"
```

---

## Task 8: HookServer

HTTP server který přijímá Claude hooks. Permission requesty jsou blokující (curl čeká na odpověď).

**Files:**
- Create: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/Services/HookServer.cs`

**Step 1: Vytvoř HookServer.cs**

```csharp
// Services/HookServer.cs
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeOrchestrator.WPF.Models;

namespace ClaudeOrchestrator.WPF.Services;

public class HookServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly AgentManager _agentManager;
    private bool _disposed;

    // Events — volaná z UI threadu přes Dispatcher
    public event Action<string>? AgentStopped;           // agentId
    public event Action<string, string>? Notification;  // agentId, message
    public event Func<PermissionRequest, Task<bool>>? PermissionRequested;

    public HookServer(AgentManager agentManager, int port = 5050)
    {
        _agentManager = agentManager;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public void Start()
    {
        _listener.Start();
        _ = Task.Run(ListenAsync);
    }

    private async Task ListenAsync()
    {
        while (!_disposed)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "";
        // Očekávaný path: /api/agents/{agentId}/hook/{hookType}
        var parts = path.Trim('/').Split('/');
        // parts: ["api", "agents", "{agentId}", "hook", "{hookType}"]

        if (parts.Length < 5 || parts[0] != "api" || parts[1] != "agents" || parts[3] != "hook")
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }

        var agentId = parts[2];
        var hookType = parts[4];

        string body = "";
        if (ctx.Request.HasEntityBody)
            using (var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                body = await sr.ReadToEndAsync();

        switch (hookType)
        {
            case "stop":
                AgentStopped?.Invoke(agentId);
                await WriteResponse(ctx, 200, "{\"ok\":true}");
                break;

            case "notification":
                Notification?.Invoke(agentId, body);
                await WriteResponse(ctx, 200, "{\"ok\":true}");
                break;

            case "pre-tool":
                await HandlePermissionAsync(ctx, agentId, body);
                break;

            default:
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                break;
        }
    }

    private async Task HandlePermissionAsync(HttpListenerContext ctx, string agentId, string body)
    {
        try
        {
            var json = JsonNode.Parse(body) as JsonObject;
            var req = new PermissionRequest
            {
                RequestId = Guid.NewGuid().ToString("N")[..8],
                AgentId = agentId,
                ToolName = json?["tool_name"]?.GetValue<string>() ?? "",
                ToolInput = json?["tool_input"],
            };

            bool approved = true;
            if (PermissionRequested != null)
                approved = await PermissionRequested(req);

            var response = JsonSerializer.Serialize(new { approved });
            await WriteResponse(ctx, 200, response);
        }
        catch
        {
            ctx.Response.StatusCode = 500;
            ctx.Response.Close();
        }
    }

    private static async Task WriteResponse(HttpListenerContext ctx, int statusCode, string json)
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        _disposed = true;
        _listener.Stop();
        _listener.Close();
    }
}
```

**Step 2: Build**

```bash
dotnet build
```

**Step 3: Commit**

```bash
git add claude-orchestrator-win/
git commit -m "feat: add HookServer with HttpListener"
```

---

## Task 9: TerminalView (WPF DrawingVisual renderer)

Toto je nejsložitější část. Custom WPF control který renderuje VtNetCore grid do DrawingVisual.

**Files:**
- Create: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/Views/TerminalView.cs` (code-behind, žádný XAML — je jednodušší pro custom renderování)

**Step 1: Vytvoř TerminalView.cs**

```csharp
// Views/TerminalView.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VtNetCore.VirtualTerminal;
using ClaudeOrchestrator.WPF.Services;

namespace ClaudeOrchestrator.WPF.Views;

/// <summary>
/// Custom WPF control pro renderování VtNetCore terminálu.
/// Dědí z FrameworkElement, renderuje přes DrawingVisual.
/// Scroll je řešen jako posun ViewportOffset v VtNetCore.
/// </summary>
public class TerminalView : FrameworkElement
{
    private readonly DrawingVisual _visual = new();
    private readonly VisualCollection _visuals;
    private PtySessionService? _session;

    // Monospace font pro rendering
    private static readonly Typeface MonoTypeface = new(
        new FontFamily("Cascadia Mono, Consolas, Courier New"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private const double FontSize = 13.0;

    // Rozměry buňky (měří se z fontu)
    private double _charWidth = 8.0;
    private double _charHeight = 16.0;

    public TerminalView()
    {
        _visuals = new VisualCollection(this);
        _visuals.Add(_visual);
        Focusable = true;
        Background = Brushes.Black;
        ClipToBounds = true;

        // Změř přesné rozměry buňky z fontu
        MeasureCharSize();

        SizeChanged += OnSizeChanged;
        MouseDown += (_, _) => Focus();
    }

    // ── Napojení na PtySessionService ──────────────────────

    public void Attach(PtySessionService session)
    {
        if (_session != null)
            _session.ViewportInvalidated -= OnViewportInvalidated;

        _session = session;
        _session.ViewportInvalidated += OnViewportInvalidated;
        Render();
    }

    public void Detach()
    {
        if (_session != null)
            _session.ViewportInvalidated -= OnViewportInvalidated;
        _session = null;
        ClearVisual();
    }

    private void OnViewportInvalidated()
        => Dispatcher.InvokeAsync(Render);

    // ── Rendering ──────────────────────────────────────────

    private void Render()
    {
        var terminal = _session?.Terminal;
        if (terminal is null) { ClearVisual(); return; }

        using var dc = _visual.RenderOpen();
        dc.DrawRectangle(Brushes.Black, null, new Rect(RenderSize));

        var vp = terminal.ViewPort;
        for (int row = 0; row < vp.RowCount; row++)
        for (int col = 0; col < vp.ColumnCount; col++)
        {
            try
            {
                var cell = vp[col, row];
                var ch = cell.Character.ToString();
                if (string.IsNullOrEmpty(ch) || ch == "\0") ch = " ";

                var bg = VtColorToWpf(cell.Attributes.BackgroundColor, isBackground: true);
                var fg = VtColorToWpf(cell.Attributes.ForegroundColor, isBackground: false);

                var rect = new Rect(col * _charWidth, row * _charHeight, _charWidth, _charHeight);

                // Background
                if (bg != Brushes.Black)
                    dc.DrawRectangle(bg, null, rect);

                // Glyph
                if (ch != " ")
                {
                    var weight = cell.Attributes.Bright ? FontWeights.Bold : FontWeights.Normal;
                    var ft = new FormattedText(ch,
                        System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(MonoTypeface.FontFamily, MonoTypeface.Style, weight, MonoTypeface.Stretch),
                        FontSize, fg,
                        VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    dc.DrawText(ft, rect.Location);
                }
            }
            catch { /* přeskoč vadné buňky */ }
        }

        // Cursor
        try
        {
            var cur = vp.CursorPosition;
            var cursorRect = new Rect(cur.Column * _charWidth, cur.Row * _charHeight, _charWidth, _charHeight);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(180, 200, 200, 200)), null, cursorRect);
        }
        catch { }
    }

    private void ClearVisual()
    {
        using var dc = _visual.RenderOpen();
        dc.DrawRectangle(Brushes.Black, null, new Rect(RenderSize));
    }

    // ── Klávesový vstup ────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_session is null) return;
        var bytes = KeyToBytes(e);
        if (bytes != null)
        {
            _ = _session.WriteAsync(bytes);
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        if (_session is null || string.IsNullOrEmpty(e.Text)) return;
        _ = _session.WriteAsync(e.Text);
        e.Handled = true;
    }

    // ── Paste ───────────────────────────────────────────────

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_session is null) return;
        if (e.Key == Key.V && (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0)
        {
            e.Handled = true;
            var text = Clipboard.GetText();
            if (!string.IsNullOrEmpty(text))
                _ = _session.WriteAsync(text);
        }
    }

    // ── Resize ──────────────────────────────────────────────

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_session is null) return;
        var cols = Math.Max(1, (int)(e.NewSize.Width / _charWidth));
        var rows = Math.Max(1, (int)(e.NewSize.Height / _charHeight));
        _ = _session.ResizeAsync(cols, rows);
    }

    // ── Scroll ──────────────────────────────────────────────

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        // VtNetCore scroll — posun scrollback bufferu
        // Implementace závisí na VtNetCore verzi — viz dokumentaci
        // Základní: zatím noop, scrollback se dá doplnit později
        base.OnMouseWheel(e);
    }

    // ── Helpers ─────────────────────────────────────────────

    private void MeasureCharSize()
    {
        var ft = new FormattedText("W",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            MonoTypeface, FontSize, Brushes.White,
            96.0);  // 96 DPI jako základ
        _charWidth = ft.Width;
        _charHeight = ft.Height;
    }

    private static Brush VtColorToWpf(object? vtColor, bool isBackground)
    {
        // VtNetCore vrací různé typy barev — upravit dle skutečného API
        // Základní implementace s ANSI 16 barvami
        if (vtColor is null)
            return isBackground ? Brushes.Black : Brushes.LightGray;

        // Pokus o konverzi — VtNetCore může vrátit Color nebo index
        try
        {
            if (vtColor is System.Drawing.Color dc)
            {
                var wpfColor = Color.FromRgb(dc.R, dc.G, dc.B);
                return new SolidColorBrush(wpfColor);
            }
        }
        catch { }

        return isBackground ? Brushes.Black : Brushes.LightGray;
    }

    private static byte[]? KeyToBytes(KeyEventArgs e)
    {
        // Speciální klávesy → ANSI escape sekvence
        return e.Key switch
        {
            Key.Return     => "\r"u8.ToArray(),
            Key.Back       => "\x7f"u8.ToArray(),
            Key.Tab        => "\t"u8.ToArray(),
            Key.Escape     => "\x1b"u8.ToArray(),
            Key.Up         => "\x1b[A"u8.ToArray(),
            Key.Down       => "\x1b[B"u8.ToArray(),
            Key.Right      => "\x1b[C"u8.ToArray(),
            Key.Left       => "\x1b[D"u8.ToArray(),
            Key.Home       => "\x1b[H"u8.ToArray(),
            Key.End        => "\x1b[F"u8.ToArray(),
            Key.Delete     => "\x1b[3~"u8.ToArray(),
            Key.PageUp     => "\x1b[5~"u8.ToArray(),
            Key.PageDown   => "\x1b[6~"u8.ToArray(),
            Key.F1         => "\x1bOP"u8.ToArray(),
            Key.F2         => "\x1bOQ"u8.ToArray(),
            Key.F3         => "\x1bOR"u8.ToArray(),
            Key.F4         => "\x1bOS"u8.ToArray(),
            // Ctrl+klávesy
            Key.C when (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0
                => "\x03"u8.ToArray(),
            Key.D when (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0
                => "\x04"u8.ToArray(),
            Key.L when (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0
                => "\x0c"u8.ToArray(),
            Key.Z when (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0
                => "\x1a"u8.ToArray(),
            _ => null  // TextInput event zpracuje tisknutelné znaky
        };
    }

    // ── FrameworkElement overhead ──────────────────────────

    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];
}
```

**Step 2: Build**

```bash
dotnet build
```

Pokud VtNetCore API pro čtení buněk neodpovídá (různé verze mají různou strukturu) — zkontroluj IntelliSense nebo NuGet README a uprav `VtColorToWpf` a přístup k `cell.Attributes`.

**Step 3: Commit**

```bash
git add claude-orchestrator-win/
git commit -m "feat: add TerminalView WPF DrawingVisual renderer"
```

---

## Task 10: MainWindow shell + AgentSidebar

**Files:**
- Modify: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/MainWindow.xaml`
- Modify: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/MainWindow.xaml.cs`

**Step 1: Uprav MainWindow.xaml**

```xml
<!-- MainWindow.xaml -->
<Window x:Class="ClaudeOrchestrator.WPF.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:ClaudeOrchestrator.WPF"
        Title="Claude Agent Orchestrator"
        Width="1400" Height="900"
        Background="#0a0a0a">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="240"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <!-- Sidebar -->
        <Grid Grid.Column="0" Background="#09090b">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <!-- Header -->
            <Border Grid.Row="0" BorderBrush="#27272a" BorderThickness="0,0,0,1" Padding="12,10">
                <StackPanel>
                    <TextBlock Text="⚡ Claude Orchestrator" Foreground="#60a5fa"
                               FontWeight="Bold" FontSize="13"/>
                </StackPanel>
            </Border>

            <!-- Agent list -->
            <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
                <ItemsControl x:Name="AgentsList" Margin="8,8,8,0">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border x:Name="AgentCard"
                                    Margin="0,0,0,6" Padding="10,8"
                                    CornerRadius="6" Cursor="Hand"
                                    BorderThickness="1"
                                    MouseDown="AgentCard_MouseDown">
                                <Grid>
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="Auto"/>
                                        <RowDefinition Height="Auto"/>
                                    </Grid.RowDefinitions>
                                    <TextBlock Grid.Row="0" Text="{Binding Name}"
                                               Foreground="#e4e4e7" FontSize="12" FontWeight="Medium"
                                               TextTrimming="CharacterEllipsis"/>
                                    <TextBlock Grid.Row="1" Text="{Binding StatusLabel}"
                                               Foreground="#71717a" FontSize="10" Margin="0,2,0,0"/>
                                </Grid>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>

            <!-- New agent input + History button -->
            <StackPanel Grid.Row="2" Margin="8" Spacing="6">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBox x:Name="NewAgentInput" Grid.Column="0"
                             Background="#18181b" Foreground="#e4e4e7"
                             BorderBrush="#3f3f46" BorderThickness="1"
                             Padding="8,4" FontSize="12"
                             PlaceholderText="Agent name..."
                             KeyDown="NewAgentInput_KeyDown"/>
                    <Button Grid.Column="1" Content="+" Margin="4,0,0,0"
                            Padding="8,4" Click="SpawnButton_Click"
                            Background="#2563eb" Foreground="White"
                            BorderThickness="0" FontSize="14"/>
                </Grid>
                <Button Content="📋 History" Click="HistoryButton_Click"
                        Background="Transparent" Foreground="#71717a"
                        BorderThickness="0" FontSize="11" HorizontalAlignment="Left"/>
            </StackPanel>
        </Grid>

        <!-- Terminal area -->
        <Grid Grid.Column="1">
            <Border BorderBrush="#27272a" BorderThickness="1,0,0,0">
                <!-- TerminalView je přidán code-behind -->
                <ContentPresenter x:Name="TerminalHost"/>
            </Border>
        </Grid>
    </Grid>
</Window>
```

**Step 2: Uprav MainWindow.xaml.cs**

```csharp
// MainWindow.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClaudeOrchestrator.WPF.Models;
using ClaudeOrchestrator.WPF.Services;
using ClaudeOrchestrator.WPF.Views;

namespace ClaudeOrchestrator.WPF;

public partial class MainWindow : Window
{
    private readonly AgentManager _agentManager;
    private readonly HookServer _hookServer;
    private Agent? _activeAgent;
    private TerminalView? _terminalView;

    // Dictionary: agentId → TerminalView (každý agent má svůj)
    private readonly Dictionary<string, TerminalView> _terminalViews = new();

    public MainWindow(AgentManager agentManager, HookServer hookServer)
    {
        InitializeComponent();
        _agentManager = agentManager;
        _hookServer = hookServer;

        // Bind agent list
        AgentsList.ItemsSource = _agentManager.Agents;

        // Hook events
        _agentManager.AgentStatusChanged += OnAgentStatusChanged;
        _agentManager.AgentExited += OnAgentExited;

        hookServer.AgentStopped += agentId => Dispatcher.Invoke(() => OnAgentIdle(agentId));
        hookServer.PermissionRequested += OnPermissionRequested;
    }

    private void OnAgentStatusChanged(Agent agent, AgentStatus status)
    {
        Dispatcher.Invoke(() => {
            // Refresh ItemsControl (pro binding StatusLabel)
            AgentsList.Items.Refresh();
        });
    }

    private void OnAgentExited(Agent agent)
    {
        Dispatcher.Invoke(() => {
            _terminalViews.Remove(agent.Id);
            if (_activeAgent?.Id == agent.Id)
            {
                _activeAgent = _agentManager.Agents.FirstOrDefault();
                ShowTerminal(_activeAgent);
            }
        });
    }

    private void OnAgentIdle(string agentId)
    {
        // Zvuk notifikace
        System.Media.SystemSounds.Asterisk.Play();
        // TODO: Windows Toast notifikace (Task 13)
    }

    private async Task<bool> OnPermissionRequested(PermissionRequest req)
    {
        return await Dispatcher.InvokeAsync(() => {
            var dialog = new PermissionDialog(req) { Owner = this };
            return dialog.ShowDialog() == true;
        });
    }

    private void ShowTerminal(Agent? agent)
    {
        if (agent is null) { TerminalHost.Content = null; return; }

        if (!_terminalViews.TryGetValue(agent.Id, out var tv))
        {
            tv = new TerminalView();
            var session = _agentManager.GetSession(agent.Id);
            if (session != null) tv.Attach(session);
            _terminalViews[agent.Id] = tv;
        }

        TerminalHost.Content = tv;
        tv.Focus();
    }

    private void AgentCard_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Agent agent)
        {
            _activeAgent = agent;
            ShowTerminal(agent);
        }
    }

    private async void SpawnButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NewAgentInput.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;
        NewAgentInput.Clear();

        var agent = await _agentManager.SpawnAsync(name, null);
        _activeAgent = agent;
        ShowTerminal(agent);
    }

    private void NewAgentInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) SpawnButton_Click(sender, e);
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var historyWindow = new HistoryWindow(_agentManager,
            ((App)App.Current).HistoryService) { Owner = this };
        historyWindow.Show();
    }
}
```

**Step 3: Přidej `StatusLabel` property do Agent modelu**

```csharp
// Models/Agent.cs — přidej property
public string StatusLabel => Status switch
{
    AgentStatus.Running => "● Running",
    AgentStatus.Idle    => "⏳ Waiting",
    AgentStatus.Done    => "✓ Done",
    _ => ""
};
```

**Step 4: Build**

```bash
dotnet build
```

**Step 5: Commit**

```bash
git add claude-orchestrator-win/
git commit -m "feat: add MainWindow shell with sidebar and terminal hosting"
```

---

## Task 11: PermissionDialog

**Files:**
- Create: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/Views/PermissionDialog.xaml`
- Create: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/Views/PermissionDialog.xaml.cs`

**Step 1: Vytvoř PermissionDialog.xaml**

```xml
<Window x:Class="ClaudeOrchestrator.WPF.Views.PermissionDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Permission Request"
        Width="520" Height="Auto" SizeToContent="Height"
        ResizeMode="NoResize" WindowStartupLocation="CenterOwner"
        Background="#111113">
    <StackPanel Margin="20">
        <StackPanel Orientation="Horizontal" Margin="0,0,0,12">
            <TextBlock Text="⚠ " Foreground="#facc15" FontSize="18"/>
            <TextBlock Text="Permission Request" Foreground="White"
                       FontSize="14" FontWeight="SemiBold" VerticalAlignment="Center"/>
        </StackPanel>

        <TextBlock Foreground="#a1a1aa" FontSize="12" Margin="0,0,0,4">
            <Run Text="Agent wants to use "/>
            <Run x:Name="ToolNameRun" Foreground="#fde047" FontFamily="Cascadia Mono, Consolas"/>
        </TextBlock>

        <TextBox x:Name="InputBox"
                 Background="#09090b" Foreground="#a1a1aa"
                 BorderBrush="#27272a" BorderThickness="1"
                 FontFamily="Cascadia Mono, Consolas" FontSize="11"
                 IsReadOnly="True" TextWrapping="Wrap"
                 MaxHeight="200" VerticalScrollBarVisibility="Auto"
                 Margin="0,0,0,16" Padding="8"/>

        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
            <Button x:Name="DenyButton" Content="Deny"
                    Background="#7f1d1d" Foreground="White"
                    Padding="14,6" BorderThickness="0"
                    Click="DenyButton_Click"/>
            <Button x:Name="ApproveButton" Content="Approve"
                    Background="#14532d" Foreground="White"
                    Padding="14,6" BorderThickness="0"
                    Click="ApproveButton_Click"/>
        </StackPanel>
    </StackPanel>
</Window>
```

**Step 2: Vytvoř PermissionDialog.xaml.cs**

```csharp
// Views/PermissionDialog.xaml.cs
using System.Text.Json;
using System.Windows;
using ClaudeOrchestrator.WPF.Models;

namespace ClaudeOrchestrator.WPF.Views;

public partial class PermissionDialog : Window
{
    public PermissionDialog(PermissionRequest req)
    {
        InitializeComponent();
        ToolNameRun.Text = req.ToolName;
        if (req.ToolInput != null)
            InputBox.Text = JsonSerializer.Serialize(req.ToolInput,
                new JsonSerializerOptions { WriteIndented = true });
        else
            InputBox.Visibility = Visibility.Collapsed;
    }

    private void ApproveButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void DenyButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
```

**Step 3: Build**

```bash
dotnet build
```

**Step 4: Commit**

```bash
git add claude-orchestrator-win/
git commit -m "feat: add PermissionDialog"
```

---

## Task 12: HistoryWindow

**Files:**
- Create: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/Views/HistoryWindow.xaml`
- Create: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/Views/HistoryWindow.xaml.cs`

**Step 1: Vytvoř HistoryWindow.xaml**

```xml
<Window x:Class="ClaudeOrchestrator.WPF.Views.HistoryWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Session History" Width="700" Height="500"
        Background="#09090b">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <Border Grid.Row="0" BorderBrush="#27272a" BorderThickness="0,0,0,1" Padding="16,10">
            <TextBlock Text="Session History" Foreground="#e4e4e7"
                       FontSize="13" FontWeight="SemiBold"/>
        </Border>

        <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto" Padding="12">
            <ItemsControl x:Name="RecordsList">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Margin="0,0,0,8" Padding="12" CornerRadius="6"
                                Background="#18181b" BorderBrush="#27272a" BorderThickness="1">
                            <Grid>
                                <Grid.RowDefinitions>
                                    <RowDefinition Height="Auto"/>
                                    <RowDefinition Height="Auto"/>
                                    <RowDefinition Height="Auto"/>
                                </Grid.RowDefinitions>
                                <Grid Grid.Row="0">
                                    <TextBlock Text="{Binding Name}" Foreground="#e4e4e7"
                                               FontSize="12" FontWeight="Medium"/>
                                    <TextBlock Text="{Binding FinishedAtLabel}"
                                               Foreground="#52525b" FontSize="11"
                                               HorizontalAlignment="Right"/>
                                </Grid>
                                <TextBlock Grid.Row="1" Text="{Binding Cwd}"
                                           Foreground="#52525b" FontSize="10" Margin="0,2,0,6"
                                           TextTrimming="CharacterEllipsis"/>
                                <StackPanel Grid.Row="2" Orientation="Horizontal" Spacing="8">
                                    <Button Content="Resume ↺" Tag="{Binding}"
                                            Background="#1d4ed8" Foreground="White"
                                            Padding="10,4" BorderThickness="0" FontSize="11"
                                            Click="ResumeButton_Click"
                                            Visibility="{Binding ResumeVisible}"/>
                                    <Button Content="Delete" Tag="{Binding}"
                                            Background="Transparent" Foreground="#52525b"
                                            Padding="6,4" BorderThickness="0" FontSize="11"
                                            Click="DeleteButton_Click"/>
                                </StackPanel>
                            </Grid>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </Grid>
</Window>
```

**Step 2: Vytvoř HistoryWindow.xaml.cs**

```csharp
// Views/HistoryWindow.xaml.cs
using System.Windows;
using System.Windows.Controls;
using ClaudeOrchestrator.WPF.Models;
using ClaudeOrchestrator.WPF.Services;

namespace ClaudeOrchestrator.WPF.Views;

public class HistoryRecordVM
{
    public AgentRecord Record { get; init; } = null!;
    public string Name => Record.Name;
    public string? Cwd => Record.Cwd;
    public string FinishedAtLabel => Record.FinishedAt?.ToLocalTime().ToString("dd.MM.yy HH:mm") ?? "";
    public Visibility ResumeVisible => Record.SessionId != null ? Visibility.Visible : Visibility.Collapsed;
}

public partial class HistoryWindow : Window
{
    private readonly AgentManager _agentManager;
    private readonly HistoryService _history;
    private List<HistoryRecordVM> _vms = new();

    public HistoryWindow(AgentManager agentManager, HistoryService history)
    {
        InitializeComponent();
        _agentManager = agentManager;
        _history = history;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var records = await _history.GetAllAsync();
        _vms = records.Select(r => new HistoryRecordVM { Record = r }).ToList();
        RecordsList.ItemsSource = _vms;
    }

    private async void ResumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not HistoryRecordVM vm) return;
        var r = vm.Record;
        await _agentManager.SpawnAsync(r.Name, r.Cwd, r.SessionId);
        Close();
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not HistoryRecordVM vm) return;
        await _history.DeleteAsync(vm.Record.Id);
        _vms.Remove(vm);
        RecordsList.ItemsSource = null;
        RecordsList.ItemsSource = _vms;
    }
}
```

**Step 3: Build + Commit**

```bash
dotnet build
git add claude-orchestrator-win/
git commit -m "feat: add HistoryWindow"
```

---

## Task 13: Notifikace (Windows Toast)

**Files:**
- Modify: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/ClaudeOrchestrator.WPF.csproj`
- Modify: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/MainWindow.xaml.cs`

**Step 1: Přidej NuGet balíček pro Toast**

```bash
dotnet add package Microsoft.Toolkit.Uwp.Notifications
```

**Step 2: Uprav OnAgentIdle v MainWindow.xaml.cs**

```csharp
using Microsoft.Toolkit.Uwp.Notifications;

private void OnAgentIdle(string agentId)
{
    var agent = _agentManager.Agents.FirstOrDefault(a => a.Id == agentId);

    // Zvuk
    System.Media.SystemSounds.Asterisk.Play();

    // Windows Toast
    try
    {
        new ToastContentBuilder()
            .AddText($"⏳ {agent?.Name ?? agentId} čeká na vstup")
            .AddText("Agent potřebuje tvoji odpověď.")
            .Show();
    }
    catch { /* Toast nemusí být dostupný */ }
}
```

**Step 3: Build**

```bash
dotnet build
```

**Step 4: Commit**

```bash
git add claude-orchestrator-win/
git commit -m "feat: add Windows Toast + sound notifications"
```

---

## Task 14: App.xaml.cs — wiring a startup

Posledním krokem je propojit všechny služby v App.xaml.cs a předat je MainWindow.

**Files:**
- Modify: `claude-orchestrator-win/src/ClaudeOrchestrator.WPF/App.xaml.cs`

**Step 1: Uprav App.xaml.cs**

```csharp
// App.xaml.cs
using System.Windows;
using ClaudeOrchestrator.WPF.Services;

namespace ClaudeOrchestrator.WPF;

public partial class App : Application
{
    public HistoryService HistoryService { get; } = new();
    public AgentManager AgentManager { get; private set; } = null!;
    public HookServer HookServer { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        const string orchestratorUrl = "http://localhost:5050";
        AgentManager = new AgentManager(orchestratorUrl, HistoryService);
        HookServer = new HookServer(AgentManager, port: 5050);
        HookServer.Start();

        var mainWindow = new MainWindow(AgentManager, HookServer);
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        HookServer.Dispose();
        base.OnExit(e);
    }
}
```

**Step 2: Build**

```bash
dotnet build
```

**Step 3: Manuálně spusť a ověř základní flow**

```bash
dotnet run
```

Ověř:
- Okno se otevře
- Zadej název agenta, klikni "+" → agent se spawne
- V terminálu se zobrazí Claude výstup
- Klávesy fungují, Claude odpovídá
- Po idle → notifikace

**Step 4: Final commit**

```bash
git add claude-orchestrator-win/
git commit -m "feat: wire up App.xaml.cs startup, complete claude-orchestrator-win"
```

---

## Poznámky pro implementaci

### VtNetCore — zjisti přesné API
Po `dotnet add package VtNetCore` spusť:
```bash
dotnet build
```
Pokud jsou chyby — IntelliSense ukáže skutečné property names. Klíčové jsou:
- Jak číst buňku: `vp[col, row]` vs `vp.GetCharacter(col, row)`
- Typ barvy: `System.Drawing.Color` vs custom type
- Jak resetovat scroll: `controller.ScrollToBottom()` nebo podobné

### Pty.Net — CommandLine vs Arguments
V různých verzích Pty.Net se parametr jmenuje jinak. Zkontroluj přes IntelliSense.

### Potenciální problém: DPI awareness
WPF na HiDPI displej — TerminalView musí používat správné DPI pro `FormattedText`. Viz `VisualTreeHelper.GetDpi(this).PixelsPerDip`.
