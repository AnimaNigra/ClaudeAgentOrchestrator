# Claude Agent Orchestrator — kontext pro Claude Code

## Co je tento projekt

Webová aplikace pro správu více Claude Code agentů najednou. Backend je ASP.NET Core 9, frontend Vue 3 + Pinia + xterm.js. Každý agent běží ve vlastním PTY (přes node-pty proxy) a komunikuje se s frontendem přes SignalR.

## Struktura

```
claude-orchestrator-web/
  backend/          — ASP.NET Core 9 (.NET 9)
    Controllers/    — REST API (Agents, Priorities)
    Services/       — AgentManager, PtySession, PriorityService, ...
    Models/         — Agent, PriorityItem, ...
    pty-proxy/      — Node.js proxy (node-pty → ConPTY/pty)
  frontend/         — Vue 3 + Vite + Pinia + TailwindCSS
    src/
      components/   — AgentCard, TerminalPanel, CommandBar, VoiceDictate*, PermissionDialog
      views/        — AgentsView, PrioritiesView
      stores/       — agentStore, priorityStore
```

## Klíčové technologie

- **Backend:** ASP.NET Core 9, SignalR, node-pty (přes Node.js proxy)
- **Frontend:** Vue 3 (Composition API), Pinia, xterm.js, TailwindCSS, Web Speech API
- **Persistence:** Prioritní seznam v `data/priorities.json`

## Dev spuštění

```bash
cd claude-orchestrator-web/backend
dotnet run
```

Backend automaticky spustí Vite na portu 5173 a proxy na `http://localhost:5050`.

> Pokud selže s "Vite dev server not available": spusť `npm install` ve `frontend/`.

## Produkční build

```powershell
cd claude-orchestrator-web/backend
dotnet clean -c Release; dotnet publish -c Release
```

`dotnet clean` je nutný — Vite generuje nové hashe při každém buildu.

## Funkce

- Více Claude agentů najednou, každý v samostatném PTY
- Plný xterm.js terminál (barvy, TUI, slash příkazy)
- PreToolUse hook — schvalování nástrojů přes dialog v prohlížeči
- Hlasové diktování (`VoiceDictateButton` + `VoiceDictateDialog`) — Web Speech API, normalizace čísel, příloha obrázku
- Prioritní seznam (záložka Priorities) — drag & drop, inline editace, JSON persistence

## Důležité vzory

- Komunikace frontend ↔ backend probíhá přes **SignalR** (`/hubs/agents`) pro real-time eventy a **REST API** pro akce
- PTY výstup je přenášen jako base64: `DATA:<base64>` → `atob()` → `terminal.write()`
- Hooky se injektují do `.claude/settings.local.json` v CWD každého agenta a po ukončení se čistí
- `PtySession` detekuje stav agenta primárně přes hooks, záložně přes regex na PTY výstup
