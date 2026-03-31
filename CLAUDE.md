# Claude Agent Orchestrator — context for Claude Code

## What is this project

A web application for managing multiple Claude Code agents at once. The backend is ASP.NET Core 9, the frontend is Vue 3 + Pinia + xterm.js. Each agent runs in its own PTY (via a node-pty proxy) and communicates with the frontend over SignalR.

## Structure

```
claude-orchestrator-web/
  backend/          — ASP.NET Core 9 (.NET 9)
    Controllers/    — REST API (Agents, Worktree, Priorities, History, Tasks)
    Services/       — AgentManager, PtySession, WorktreeService, PriorityService, ...
    Models/         — Agent, AgentRecord, PriorityItem, ...
    pty-proxy/      — Node.js proxy (node-pty -> ConPTY/pty)
  frontend/         — Vue 3 + Vite + Pinia + TailwindCSS
    src/
      components/   — AgentCard, TerminalPanel, CommandBar, PromptDialog, VoiceDictate*, PermissionDialog
      views/        — AgentsView, WorktreesView, HistoryView, PrioritiesView, TasksView
      stores/       — agentStore, priorityStore
```

## Key technologies

- **Backend:** ASP.NET Core 9, SignalR, node-pty (via Node.js proxy)
- **Frontend:** Vue 3 (Composition API), Pinia, xterm.js, TailwindCSS, Web Speech API
- **Persistence:** JSON files in `data/` directory

## Dev startup

```bash
cd claude-orchestrator-web/backend
dotnet run
```

The backend auto-launches Vite on port 5180 and the pty-proxy.

> If Vite fails: run `npm install` in `frontend/`.

## Production build

```powershell
cd claude-orchestrator-web/backend
dotnet clean -c Release; dotnet publish -c Release
```

`dotnet clean` is required — Vite generates new hashes on every build.

## Important patterns

- Frontend-backend communication uses **SignalR** (`/hubs/agents`) for real-time events and **REST API** for actions
- PTY output is transmitted as base64: `DATA:<base64>` -> `atob()` -> `terminal.write()`
- Hooks are injected into `.claude/settings.local.json` in each agent's CWD and cleaned up on exit (with per-file locking for concurrent access)
- `PtySession` detects agent state primarily via hooks, with regex-based PTY output detection as fallback
- Worktrees are created as sibling directories: `<project>-wt-<name>/`
- High-frequency `pty_data` events do NOT include the agent object to prevent overwriting idle status
- Worktree agents are excluded from session history (temporary directories)
