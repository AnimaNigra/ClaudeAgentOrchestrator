# Claude Agent Orchestrator

Webová aplikace pro správu více Claude Code agentů najednou. Každý agent běží ve vlastním pseudo-terminálu (PTY) a je zobrazen přes xterm.js v prohlížeči — plná podpora barev, TUI, slash příkazů (`/clear`, `/compact`, `/init`) a potvrzovacích promptů.

## Požadavky

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) (LTS)
- [Claude Code CLI](https://docs.anthropic.com/claude-code): `npm install -g @anthropic-ai/claude-code`

## Vývoj (dev mode)

```bash
cd claude-orchestrator-web/backend
dotnet run
```

Automaticky spustí Vite dev server (hot reload) a backend na `http://localhost:5050`.

## Publikování (produkce)

```powershell
cd claude-orchestrator-web/backend
dotnet clean -c Release; dotnet publish -c Release
```

> `dotnet clean` je nutný, protože Vite generuje při každém buildu nový hash pro JS/CSS soubory. Bez cleanování způsobí stará cache v `obj/Release` chybu při komprimaci statických assetů.

Výstup je v `bin/Release/net9.0/publish/`. Spuštění:

```bash
./bin/Release/net9.0/publish/ClaudeOrchestrator.exe
```

Pro self-contained (bez nutnosti mít nainstalovaný .NET):

```bash
dotnet clean; dotnet publish -c Release
```

## Konfigurace portu

Výchozí port je `5050`. Změnit ho lze v `backend/appsettings.json`:

```json
{
  "Port": 8080
}
```

Nebo přes proměnnou prostředí:

```bash
Port=8080 ./ClaudeOrchestrator.exe
```

## Použití

Po spuštění otevři `http://localhost:5050` (nebo jiný port dle konfigurace).

### Příkazy v command baru (spodní lišta)

| Příkaz | Popis |
|--------|-------|
| `create <název> [<cesta>]` | Vytvoří nového agenta, volitelně v zadané složce |
| `kill <název>` | Ukončí agenta |
| `select <název>` | Přepne zobrazení na daného agenta |
| `list` | Vypíše všechny agenty a jejich stav |
| `help` | Zobrazí nápovědu |

**Příklady:**
```
create docx C:\Projects\MyApp
create api
kill docx
select api
```

### Interakce s agentem

Po vytvoření agenta klikni na jeho kartu v levém panelu (nebo použij `select`) a piš přímo do terminálu. Claude Code potvrzovací prompty (`❯ 1. Yes / 2. No`) potvrzuješ stisknutím čísla.

### Notifikace

Při první návštěvě prohlížeč požádá o povolení notifikací. Kdykoli agent přejde do stavu Idle (Claude dokončil odpověď), přehraje se zvukový signál a ukáže se systémová notifikace — funguje i při minimalizovaném okně.

### Potvrzování nástrojů (Permission dialog)

Aplikace využívá Claude Code `PreToolUse` hook. Pokaždé, když Claude chce použít nástroj (Bash, Write, Edit, atd.), backend pozastaví volání a zobrazí dialog v prohlížeči s názvem nástroje a jeho parametry. Uživatel může akci **Schválit** nebo **Zamítnout** — Claude se dozví výsledek přes stdout hooku (`{"decision":"approve"}` / `{"decision":"block"}`).

Pokud uživatel neodpoví do 2 minut, akce se automaticky schválí.

## Architektura

```
Browser
  └─ Vue 3 + Pinia + xterm.js
      ├─ CommandBar      — správa agentů (create/kill/select)
      ├─ AgentCard       — stav agenta (Running/Idle/Blocked/Done)
      ├─ TerminalPanel   — jeden xterm.js terminál per agent
      └─ PermissionDialog — modální dialog pro PreToolUse schválení

ASP.NET Core (:5050)
  ├─ AgentsController — REST API (/api/agents)
  │     ├─ POST /{id}/hook/stop         — Claude dokončil odpověď
  │     ├─ POST /{id}/hook/notification — Claude poslal notifikaci
  │     ├─ POST /{id}/hook/pre-tool     — žádost o schválení nástroje (blokující)
  │     └─ POST /{id}/permission/{rid}  — odpověď uživatele z frontendu
  ├─ AgentHub         — SignalR real-time events (/hubs/agents)
  ├─ AgentManager     — správa životního cyklu agentů
  └─ PtySession       — jeden PTY per agent přes node-pty proxy

pty-proxy/index.js (Node.js)
  └─ node-pty → ConPTY (Windows) / pty (Linux/macOS)
      └─ claude (CLI) běží v plném pseudo-terminálu
```

### Tok dat

```
PTY výstup → base64 → DATA: linka → C# ReadLine → SignalR pty_data → atob() → Uint8Array → terminal.write()
Keystroke  → terminal.onData → POST /keystroke → C# → INPUT: linka → PTY stdin
Resize     → ResizeObserver → POST /resize → C# → RESIZE: linka → pty.resize()
```

### Detekce stavu (hooks)

Stav agenta je detekován spolehlivě přes **Claude Code hooks**, které se automaticky injektují do `.claude/settings.json` v pracovním adresáři každého agenta:

| Hook | Kdy se spustí | Akce |
|------|--------------|------|
| `Stop` | Claude dokončil odpověď | `curl POST /hook/stop` → agent → Idle |
| `Notification` | Claude posílá notifikaci | `curl POST /hook/notification` → zvuk + systémová notif |
| `PreToolUse` | Před každým voláním nástroje | `curl POST /hook/pre-tool` → čeká na odpověď uživatele |

Při vytvoření agenta `PtySession` zálohuje stávající `settings.json`, přidá hooks a při ukončení agenta soubor obnoví do původního stavu (nebo smaže, pokud neexistoval).

Jako záloha pro případy mimo hooks sleduje `PtySession` i PTY výstup a po 3s nečinnosti nastaví stav na Idle.
