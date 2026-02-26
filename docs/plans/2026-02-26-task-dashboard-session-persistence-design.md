# Design: Task Dashboard + Session Persistence

**Date:** 2026-02-26
**Project:** Claude Orchestrator Web
**Approach:** Kanban Dashboard (Approach B)
**Storage:** JSON files

---

## Overview

Two new features for the Claude Orchestrator web application:

1. **Task Dashboard** — Kanban-style board (Todo / In Progress / Done) where users can write tasks with prompts and assign them to running agents
2. **Session Persistence** — Capture Claude Code session IDs from PTY output so agents can be resumed after restart

---

## Architecture

### Frontend

- Add `vue-router` — two routes:
  - `/` → existing Agents view (terminal + sidebar, unchanged)
  - `/tasks` → new Task Board view
- Navigation tabs in the header: `Agents | Tasks`
- New Pinia store: `stores/tasks.js`
- New Pinia store: `stores/history.js` (agent history)

### Backend

- **New:** `TasksController` — REST CRUD for tasks
- **New:** `HistoryController` — read agent history
- **New:** `TaskService` — reads/writes `data/tasks.json`
- **New:** `AgentHistoryService` — reads/writes `data/agents.json`
- **Modified:** `AgentManager` — on agent finish, persist metadata + session ID to `data/agents.json`
- **Modified:** `PtySession` — regex scan of PTY output to capture session ID

### Data Files

```
data/                          (auto-created if missing)
  tasks.json                   Array of task objects
  agents.json                  Array of historical agent records
```

---

## Data Models

### Task (`data/tasks.json`)

```json
{
  "id": "uuid",
  "title": "string (required)",
  "description": "string (optional)",
  "prompt": "string (text sent to agent on assign)",
  "status": "todo | in-progress | done",
  "agentId": "uuid | null",
  "agentName": "string | null",
  "createdAt": "ISO8601",
  "updatedAt": "ISO8601"
}
```

### Agent History (`data/agents.json`)

```json
{
  "id": "uuid",
  "name": "string",
  "cwd": "string | null",
  "sessionId": "string | null",
  "createdAt": "ISO8601",
  "finishedAt": "ISO8601 | null",
  "taskIds": ["uuid"]
}
```

---

## Kanban Task Board UI

```
┌─────────────────────────────────────────────────────────────┐
│  [Agents]  [Tasks]                          + New Task       │
├──────────────┬──────────────┬──────────────────────────────┤
│   TODO       │  IN PROGRESS │  DONE                         │
│              │              │                               │
│ ┌──────────┐ │ ┌──────────┐ │ ┌──────────┐                 │
│ │ Task A   │ │ │ Task B   │ │ │ Task C   │                 │
│ │ desc...  │ │ │ 🤖 Bot1  │ │ │ ✓ Bot2   │ [Resume Bot2]  │
│ │[Assign▼] │ │ │[Mark Done]│ │ │          │                 │
│ └──────────┘ │ └──────────┘ │ └──────────┘                 │
│              │              │                               │
│ + Add task   │              │                               │
└──────────────┴──────────────┴─────────────────────────────┘
```

### Task Card Contents

- **Title** (bold)
- **Description** (optional, truncated to 2 lines)
- **Prompt** (collapsible section — shows what will be sent to agent)
- **Agent badge** (name + status icon, shown when assigned)
- **Action buttons:**
  - TODO: `Assign to Agent` dropdown (lists active agents) → sends prompt + moves to In Progress
  - IN PROGRESS: `Mark Done` button
  - DONE: `Resume Agent` button (only shown if `sessionId` is not null)

### New Task Modal

Fields:
- Title (required)
- Description (optional, textarea)
- Prompt (optional, textarea — what gets sent to the agent)

---

## Session Persistence

### Capture Flow

`PtySession.cs` scans PTY output with regex:
```
Regex: --resume\s+([a-f0-9]+)
```

When matched:
- Store `sessionId` on the `Agent` object
- `AgentManager` persists updated record to `data/agents.json` on agent finish

### Resume Flow

1. User clicks `Resume Agent` on a Done task card
2. Frontend: `POST /api/agents` with `{ name, cwd, resumeSessionId: "abc123" }`
3. Backend: `SpawnRequest` gains optional `ResumeSessionId` field
4. `PtySession` passes `--resume <id>` to Claude CLI arguments
5. New terminal session continues the previous conversation

### Fallback

- No session ID captured (agent was killed) → `Resume Agent` button hidden
- Session ID expired (Claude deleted old session) → agent starts normally, shows warning toast

---

## Backend API Changes

### New Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/tasks` | GET | List all tasks |
| `/api/tasks` | POST | Create task |
| `/api/tasks/{id}` | PUT | Update task (title, desc, prompt, status, agentId) |
| `/api/tasks/{id}` | DELETE | Delete task |
| `/api/tasks/{id}/assign` | POST | Assign task to agent (sends prompt, moves to in-progress) |
| `/api/history` | GET | List historical agents |

### Modified Endpoints

| Endpoint | Change |
|----------|--------|
| `POST /api/agents` | Add optional `ResumeSessionId` field to `SpawnRequest` |

---

## Error Handling

| Scenario | Behavior |
|----------|----------|
| `tasks.json` missing | Auto-create empty array on startup |
| `agents.json` missing | Auto-create empty array on startup |
| Concurrent JSON writes | Lock object serializes writes in `TaskService` / `AgentHistoryService` |
| Assign to dead agent | Error toast, task stays in Todo |
| Session ID regex no match | `sessionId = null`, Resume button hidden |
| Expired session ID on resume | Agent starts normally, warning toast shown |
| `data/` directory missing | Auto-created by services on first write |

---

## Components to Create

### Frontend

| File | Purpose |
|------|---------|
| `src/views/AgentsView.vue` | Extract current App.vue content into a view |
| `src/views/TasksView.vue` | Kanban board view |
| `src/components/TaskCard.vue` | Individual task card |
| `src/components/NewTaskModal.vue` | Modal for creating tasks |
| `src/stores/tasks.js` | Pinia store for task state + API calls |
| `src/router/index.js` | Vue Router config |

### Backend

| File | Purpose |
|------|---------|
| `Controllers/TasksController.cs` | REST CRUD for tasks |
| `Controllers/HistoryController.cs` | Read agent history |
| `Services/TaskService.cs` | JSON read/write for tasks |
| `Services/AgentHistoryService.cs` | JSON read/write for agent history |
| `Models/TaskItem.cs` | Task data model |
| `Models/AgentRecord.cs` | Agent history data model |

---

## Out of Scope

- Drag & drop between kanban columns (click-to-move instead)
- Task dependencies or subtasks
- Task priorities or due dates
- Multiple users / shared dashboards
- Real-time task sync via SignalR (REST polling sufficient)
