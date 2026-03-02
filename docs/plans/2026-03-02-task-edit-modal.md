# Task Edit Modal Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Allow editing existing tasks via an enlarged modal reused from the create flow.

**Architecture:** Extend `NewTaskModal.vue` with an optional `task` prop that switches it into edit mode. Add an edit button to `TaskCard.vue`. Wire state management in `TasksView.vue`. No backend changes needed — `PUT /api/tasks/{id}` and `store.updateTask()` already exist.

**Tech Stack:** Vue 3 Composition API, Pinia store, Tailwind CSS

---

### Task 1: Extend `NewTaskModal.vue` to support edit mode

**Files:**
- Modify: `claude-orchestrator-web/frontend/src/components/NewTaskModal.vue`

**Step 1: Add `task` prop and computed mode**

Replace the `defineProps` and `defineEmits` section, and update `watch` to pre-fill form in edit mode:

```vue
const props = defineProps({
  show: { type: Boolean, default: false },
  task: { type: Object, default: null },
})

const emit = defineEmits(['close', 'created', 'updated'])

const isEdit = computed(() => props.task !== null)

watch(() => props.show, (val) => {
  if (val) {
    form.value = {
      title: props.task?.title ?? '',
      description: props.task?.description ?? '',
      prompt: props.task?.prompt ?? '',
    }
    error.value = null
  }
})
```

Add `import { ref, watch, computed } from 'vue'` (add `computed` to existing import).

**Step 2: Update `submit()` to branch on mode**

```js
function submit() {
  if (!form.value.title.trim()) {
    error.value = 'Title is required'
    return
  }
  const data = {
    title: form.value.title.trim(),
    description: form.value.description.trim() || null,
    prompt: form.value.prompt.trim() || null,
  }
  if (isEdit.value) {
    emit('updated', { id: props.task.id, ...data })
  } else {
    emit('created', data)
  }
}
```

**Step 3: Update template — title, button label, modal size, textarea rows**

Change modal container:
```
w-full max-w-md mx-4
```
→
```
w-[1024px] max-w-[95vw] mx-4
```

Change `<h2>` text:
```vue
<h2 class="text-base font-semibold text-white">{{ isEdit ? 'Edit Task' : 'New Task' }}</h2>
```

Change description textarea: `rows="2"` → `rows="4"`

Change prompt textarea: `rows="4"` → `rows="16"`

Change save button label:
```vue
{{ isEdit ? 'Save Changes' : 'Create Task' }}
```

**Step 4: Verify in browser** — Open app, click "+ New Task", confirm modal is ~1024px wide with taller textareas.

**Step 5: Commit**
```bash
git add claude-orchestrator-web/frontend/src/components/NewTaskModal.vue
git commit -m "feat: extend NewTaskModal to support edit mode and increase size"
```

---

### Task 2: Add edit button to `TaskCard.vue`

**Files:**
- Modify: `claude-orchestrator-web/frontend/src/components/TaskCard.vue`

**Step 1: Add 'edit' to emits**

```js
const emit = defineEmits(['assign', 'mark-done', 'delete', 'resume', 'edit'])
```

**Step 2: Add edit button in the title row**

Current title row:
```vue
<div class="flex items-start justify-between gap-2">
  <span class="text-sm font-medium text-white leading-tight">{{ task.title }}</span>
  <button
    @click="emit('delete', task.id)"
    class="text-gray-600 hover:text-red-400 text-xs flex-shrink-0 transition-colors"
    title="Delete task"
  >✕</button>
</div>
```

Replace with:
```vue
<div class="flex items-start justify-between gap-2">
  <span class="text-sm font-medium text-white leading-tight">{{ task.title }}</span>
  <div class="flex gap-1 flex-shrink-0">
    <button
      @click="emit('edit', task)"
      class="text-gray-600 hover:text-blue-400 text-xs transition-colors"
      title="Edit task"
    >✎</button>
    <button
      @click="emit('delete', task.id)"
      class="text-gray-600 hover:text-red-400 text-xs transition-colors"
      title="Delete task"
    >✕</button>
  </div>
</div>
```

**Step 3: Verify in browser** — Task cards should show a small pencil icon next to the X.

**Step 4: Commit**
```bash
git add claude-orchestrator-web/frontend/src/components/TaskCard.vue
git commit -m "feat: add edit button to TaskCard"
```

---

### Task 3: Wire edit flow in `TasksView.vue`

**Files:**
- Modify: `claude-orchestrator-web/frontend/src/views/TasksView.vue`

**Step 1: Add `editingTask` ref**

In the `<script setup>` section, after `const showNewTask = ref(false)`:
```js
const editingTask = ref(null)
```

**Step 2: Add `handleUpdateTask` function**

After `handleCreateTask`:
```js
async function handleUpdateTask({ id, title, description, prompt }) {
  try {
    await store.updateTask(id, { title, description, prompt })
    editingTask.value = null
  } catch {
    showError('Failed to update task')
  }
}
```

**Step 3: Add `@edit` handler to all three TaskCard groups**

All three `<TaskCard>` blocks (in Todo, In Progress, Done columns) need:
```vue
@edit="t => editingTask = t"
```

Example (Todo column):
```vue
<TaskCard
  v-for="task in store.todoTasks"
  :key="task.id"
  :task="task"
  :available-agents="availableAgents"
  :agent-record="findAgentRecord(task.agentId)"
  @assign="handleAssign"
  @mark-done="handleMarkDone"
  @delete="handleDelete"
  @resume="handleResume"
  @edit="t => editingTask = t"
/>
```

Apply the same `@edit` line to the In Progress and Done columns too.

**Step 4: Update `<NewTaskModal>` binding**

Current:
```vue
<NewTaskModal
  :show="showNewTask"
  @close="showNewTask = false"
  @created="handleCreateTask"
/>
```

Replace with:
```vue
<NewTaskModal
  :show="showNewTask || editingTask !== null"
  :task="editingTask"
  @close="showNewTask = false; editingTask = null"
  @created="handleCreateTask"
  @updated="handleUpdateTask"
/>
```

**Step 5: Verify full flow in browser**
1. Click ✎ on a task → modal opens pre-filled with existing values
2. Edit title/description/prompt → click "Save Changes" → card updates
3. Click "+ New Task" → modal opens empty → "Create Task" still works
4. Press Escape / click backdrop → modal closes without saving

**Step 6: Commit**
```bash
git add claude-orchestrator-web/frontend/src/views/TasksView.vue
git commit -m "feat: wire task edit modal in TasksView"
```

---

### Task 4: Build frontend and verify production assets

**Step 1: Build frontend**
```bash
cd claude-orchestrator-web/frontend
npm run build
```
Expected: build succeeds with no errors.

**Step 2: Commit updated wwwroot assets**
```bash
cd ../..
git add claude-orchestrator-web/backend/wwwroot/
git commit -m "chore: rebuild frontend assets"
```
