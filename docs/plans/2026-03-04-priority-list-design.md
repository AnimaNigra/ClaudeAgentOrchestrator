# Priority List Feature — Design

**Date:** 2026-03-04

## Overview

Add a new "Priorities" tab to the Claude Agent Orchestrator web UI. Users can maintain a personal priority list with drag-and-drop reordering, inline editing, done/undone toggle, and delete.

## Backend

### Model — `PriorityItem`

```csharp
public class PriorityItem
{
    public string Id { get; set; }       // GUID
    public string Text { get; set; }
    public bool Done { get; set; }
    public int Order { get; set; }       // sort key, lower = higher priority
    public DateTime CreatedAt { get; set; }
}
```

### `PriorityService`

- Stores data in `data/priorities.json`
- Same pattern as `TaskService`: SemaphoreSlim for file I/O, load-on-first-use
- Methods: `GetAllAsync`, `CreateAsync`, `UpdateAsync`, `ReorderAsync`, `DeleteAsync`

### `PrioritiesController` — `/api/priorities`

| Method | Route | Body | Description |
|--------|-------|------|-------------|
| GET | `/api/priorities` | — | Returns items sorted by Order |
| POST | `/api/priorities` | `{ text }` | Creates new item (Order = max+1) |
| PUT | `/api/priorities/{id}` | `{ text?, done? }` | Updates text or done flag |
| PUT | `/api/priorities/reorder` | `[{ id, order }]` | Bulk update Order after drag-and-drop |
| DELETE | `/api/priorities/{id}` | — | Deletes item |

## Frontend

### New files

- `src/views/PrioritiesView.vue` — main view
- `src/stores/priorities.js` — Pinia store (fetch wrapper)

### Navigation

Add `RouterLink` to `/priorities` in `App.vue` nav bar, between Tasks and History.

### UI Layout

```
┌────────────────────────────────────────┐
│ ☰  [ Přidat prioritu...          ] [+] │
├────────────────────────────────────────┤
│ ⠿  ☐  Dodělat PTY dokumentaci   ✏ 🗑  │
│ ⠿  ☐  Opravit hook race condition ✏ 🗑 │
│ ⠿  ☑  Tooltip na agent card      ✏ 🗑  │  ← přeškrtnuté + ztlumené
└────────────────────────────────────────┘
```

### Interactions

- **Add:** text input at top + Enter or [+] button → POST
- **Drag handle (⠿):** HTML5 native drag-and-drop → PUT /reorder on drop
- **Checkbox:** toggle done → PUT /{id}
- **Edit (✏):** inline text input replaces label, Enter/blur saves → PUT /{id}
- **Delete (🗑):** immediate DELETE, no confirmation dialog
- Done items: `line-through` + `opacity-50`, stay in current position

### Data flow

```
PrioritiesView
    └── priorities store (Pinia)
            └── fetch('/api/priorities')
```

No SignalR needed — single-user, no real-time sync required.
