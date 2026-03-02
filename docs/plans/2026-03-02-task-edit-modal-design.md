# Design: Task Edit + Larger Modal

**Date:** 2026-03-02
**Status:** Approved

## Summary

Extend `NewTaskModal.vue` to support both create and edit modes. Add an edit button to `TaskCard.vue`. Wire everything together in `TasksView.vue`. Increase modal size to ~1024×768.

## Components

### `NewTaskModal.vue`
- Add optional prop `task` (Object | null, default null)
- When `task` is provided (edit mode):
  - Title: "Edit Task"
  - Form pre-filled with `task.title`, `task.description`, `task.prompt`
  - Submit emits `'updated'` with `{ id, title, description, prompt }`
- When `task` is null (create mode): unchanged, emits `'created'`
- Size: `w-[1024px] max-w-[95vw]`
- Prompt textarea: `rows="16"`, description: `rows="4"`

### `TaskCard.vue`
- Add edit (pencil ✎) button next to existing delete (✕) button
- Emit `'edit'` event with full `task` object on click

### `TasksView.vue`
- Add `editingTask` ref (null | task object)
- Add `@edit="t => editingTask = t"` to all three `<TaskCard>` groups
- Pass `:task="editingTask"` and `:show="showNewTask || editingTask !== null"` to `<NewTaskModal>`
- Add `@updated="handleUpdateTask"` handler
- `handleUpdateTask({ id, title, description, prompt })` calls `store.updateTask(id, ...)` then clears `editingTask`
- On modal close: clear both `showNewTask` and `editingTask`

## Data Flow

```
TaskCard (edit click) → emit('edit', task)
TasksView: editingTask = task → NewTaskModal opens pre-filled
NewTaskModal (save) → emit('updated', { id, ... })
TasksView: store.updateTask() → PUT /api/tasks/{id} (already implemented)
```

## No backend changes needed
`PUT /api/tasks/{id}` and `store.updateTask()` already exist and work correctly.
