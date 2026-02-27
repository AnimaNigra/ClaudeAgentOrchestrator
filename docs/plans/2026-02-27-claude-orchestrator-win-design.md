# Claude Orchestrator Win — Design Doc

**Datum:** 2026-02-27
**Cíl:** WPF desktopová aplikace pro multi-agent orchestraci Claude CLI — bez JS, bez ASP.NET, čistý .NET 9 + WPF.

---

## Motivation

Stávající web apka (`claude-orchestrator-web`) používá node-pty subprocess + xterm.js v prohlížeči. Tato vrstva způsobuje problémy: double cursor, broken scrollbar, clipboard quirky. WPF verze eliminuje JS úplně — terminál renderuje přímo v .NET pomocí WPF DrawingVisual.

---

## Tech Stack

| Vrstva | Technologie |
|--------|-------------|
| UI | WPF (.NET 9, Windows only) |
| PTY | `Pty.Net` (Microsoft.VisualStudio.Terminal.Pty) — ConPTY wrapper |
| VT100 parser | `VtNetCore` — parsuje escape sekvence, udržuje grid buněk |
| Terminal renderer | `DrawingVisual` / custom `FrameworkElement` v WPF |
| Hooks HTTP server | `System.Net.HttpListener` (built-in .NET) |
| Persistence | SQLite via `Microsoft.Data.Sqlite` |
| Notifikace | `Windows.UI.Notifications` (Toast) + `SystemSounds` |

---

## Struktura projektu

```
claude-orchestrator-win/
  ClaudeOrchestrator.sln
  src/
    ClaudeOrchestrator.WPF/
      App.xaml / App.xaml.cs
      MainWindow.xaml            ← shell: sidebar (left) + content (right)
      Views/
        AgentSidebarView.xaml    ← seznam agentů, tlačítko New Agent
        TerminalView.xaml        ← DrawingVisual terminal renderer
        HistoryView.xaml         ← seznam ukončených sessions
        PermissionDialog.xaml    ← modal: Approve / Deny / Always Allow
      ViewModels/
        MainViewModel.cs         ← aktivní agent, navigace
        AgentViewModel.cs        ← obaluje PtySession, status (Running/Idle/Done)
        TerminalViewModel.cs     ← grid buněk z VtNetCore, Invalidate()
        HistoryViewModel.cs
      Services/
        AgentManager.cs          ← správa kolekce agentů
        PtySessionService.cs     ← Pty.Net start/stop/resize/write + VtNetCore feed
        HookServer.cs            ← HttpListener na :5050, routuje /hook/stop|notification|pre-tool
        HistoryService.cs        ← SQLite CRUD pro AgentRecord
        ClaudeResolver.cs        ← najde claude CLI (npm root -g / PATH)
        HooksInjector.cs         ← zapíše / smaže .claude/settings.json
      Models/
        Agent.cs                 ← id, name, cwd, status, sessionId, pid
        AgentRecord.cs           ← history záznam (SQLite)
        PermissionRequest.cs     ← requestId, toolName, toolInput
```

---

## Data Flow

### Spuštění agenta
```
MainViewModel.SpawnAgent(name, cwd)
  → AgentManager.SpawnAsync()
    → ClaudeResolver → najde node + cli.js
    → HooksInjector.Inject(cwd)  ← zapíše .claude/settings.json
    → PtySessionService.StartAsync()
      → Pty.Net: IPtyConnection = PtyProvider.SpawnAsync(opts)
      → Task.Run: ReadLoopAsync()
        → pty.ReaderStream → VtNetCore.Feed(bytes)
        → VtNetCore.Output → TerminalViewModel.Invalidate()
                           → WPF Dispatcher → redraw DrawingVisual
```

### Klávesový vstup
```
TerminalView.KeyDown / TextInput
  → PtySessionService.WriteAsync(bytes)
    → pty.WriterStream.WriteAsync(bytes)
```

### Resize
```
TerminalView.SizeChanged
  → vypočítej cols/rows (šířka / charWidth, výška / charHeight)
  → PtySessionService.ResizeAsync(cols, rows)
    → pty.ResizeAsync(cols, rows)
```

### Claude hooks
```
Claude agent spustí curl POST http://localhost:5050/api/agents/{id}/hook/stop
  → HookServer.HttpListener
    → AgentManager.OnHookStop(id)
      → AgentViewModel.Status = Idle
      → Toast notifikace + zvuk
      → (response 200 ihned)

Claude agent spustí curl POST .../hook/pre-tool  (tělo: JSON s toolName, toolInput)
  → HookServer
    → AgentManager.OnPermissionRequest(id, req)
      → Dispatcher: PermissionDialog.ShowDialog()
        → user klikne Approve / Deny
      → HookServer response 200 JSON {approved: true/false}  ← curl čeká (blokující)
```

### Session history
```
PtySessionService.OnExited
  → HooksInjector.Remove(cwd)
  → HistoryService.SaveRecord(agent)  ← SQLite INSERT
  → AgentManager.Remove(agent)  (po 2s)

HistoryView: fetch SQLite → seznam AgentRecord
Resume: AgentManager.SpawnAsync(name, cwd, resumeSessionId)
```

---

## Terminal Renderer (TerminalView)

WPF custom control dědí z `FrameworkElement`, renderuje přes `DrawingVisual`:

```csharp
// Struktura buňky z VtNetCore
struct Cell { char Char; Color Fg; Color BgColor; bool Bold; }

// Render loop (volaný z WPF Dispatcher po každém VtNetCore.Output)
void Render() {
    using var dc = _visual.RenderOpen();
    for (int row = 0; row < Rows; row++)
    for (int col = 0; col < Cols; col++) {
        var cell = _terminal.ViewPort[row, col];
        // Background rectangle
        dc.DrawRectangle(cell.BgBrush, null, CellRect(col, row));
        // Glyph
        dc.DrawText(new FormattedText(cell.Char, ...), CellPoint(col, row));
    }
    // Cursor
    var cur = _terminal.ViewPort.CursorPosition;
    dc.DrawRectangle(CursorBrush, null, CellRect(cur.Column, cur.Row));
}
```

**Scroll:** VtNetCore udržuje scrollback buffer. `ScrollViewer` (nebo vlastní ScrollBar) posouvá `ViewportOffset` v VtNetCore.

---

## Permissions (blokující curl)

`HookServer` drží `TaskCompletionSource<bool>` pro každý čekající permission request. Curl čeká dokud `TCS.Task` nekompletuje. WPF dispatcher otevře `PermissionDialog`; kliknutí nastaví `TCS.SetResult(approved)`.

```csharp
var tcs = new TaskCompletionSource<bool>();
_pending[requestId] = tcs;
Dispatcher.InvokeAsync(() => ShowPermissionDialog(req, tcs));
var approved = await tcs.Task;  // ← curl čeká tady
await response.WriteAsync(JsonSerializer.Serialize(new { approved }));
```

---

## Persistence

SQLite soubor: `%APPDATA%\ClaudeOrchestratorWin\history.db`

Tabulka `agent_records`:
```sql
CREATE TABLE agent_records (
  id TEXT PRIMARY KEY,
  name TEXT,
  cwd TEXT,
  session_id TEXT,
  notes TEXT,
  finished_at TEXT
);
```

---

## Co se NEMIGRUJE z web apky

- Vue / Pinia / SignalR / Vite — nic z toho
- node-pty proxy (`pty-proxy/index.js`) — nahrazeno Pty.Net
- ASP.NET Core Controllers — nahrazeno HttpListener
- xterm.js — nahrazeno VtNetCore + WPF DrawingVisual

---

## Out of scope (YAGNI)

- Mac/Linux podpora (WPF = Windows only)
- Remote přístup z prohlížeče
- Tmavý/světlý theme switch (default tmavý)
- Plugin systém
