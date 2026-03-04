# Priority List Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a "Priorities" tab with a drag-and-drop sortable checklist persisted to `data/priorities.json` on the backend.

**Architecture:** New `PriorityService` + `PrioritiesController` on the backend (same pattern as `TaskService`), new `priorities.js` Pinia store + `PrioritiesView.vue` on the frontend. HTML5 native drag-and-drop — no new dependencies.

**Tech Stack:** ASP.NET Core 9, C# records, Vue 3 Composition API, Pinia, Tailwind CSS

---

### Task 1: Backend model + requests

**Files:**
- Modify: `claude-orchestrator-web/backend/Models/AgentEvent.cs`

Add these records at the bottom of the file (after existing records):

```csharp
public class PriorityItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public int Order { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public record CreatePriorityRequest(string Text);
public record UpdatePriorityRequest(string? Text = null, bool? Done = null);
public record ReorderPriorityItem(string Id, int Order);
```

**Step 1: Add records to AgentEvent.cs**

Open `backend/Models/AgentEvent.cs` and append the code above after the last existing record.

**Step 2: Build to verify**

```bash
cd claude-orchestrator-web/backend
dotnet build
```
Expected: Build succeeded, 0 errors.

**Step 3: Commit**

```bash
git add claude-orchestrator-web/backend/Models/AgentEvent.cs
git commit -m "feat: add PriorityItem model and request records"
```

---

### Task 2: PriorityService

**Files:**
- Create: `claude-orchestrator-web/backend/Services/PriorityService.cs`

```csharp
using System.Text.Json;
using ClaudeOrchestrator.Models;

namespace ClaudeOrchestrator.Services;

public class PriorityService
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PriorityService(IConfiguration configuration)
    {
        var dataDir = configuration.GetValue<string>("DataDir")
            ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "priorities.json");
    }

    public async Task<List<PriorityItem>> GetAllAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var items = await ReadAsync();
            return items.OrderBy(i => i.Order).ToList();
        }
        finally { _lock.Release(); }
    }

    public async Task<PriorityItem> CreateAsync(CreatePriorityRequest req)
    {
        await _lock.WaitAsync();
        try
        {
            var items = await ReadAsync();
            var item = new PriorityItem
            {
                Text = req.Text,
                Order = items.Count > 0 ? items.Max(i => i.Order) + 1 : 0
            };
            items.Add(item);
            await WriteAsync(items);
            return item;
        }
        finally { _lock.Release(); }
    }

    public async Task<PriorityItem?> UpdateAsync(string id, UpdatePriorityRequest req)
    {
        await _lock.WaitAsync();
        try
        {
            var items = await ReadAsync();
            var item = items.FirstOrDefault(i => i.Id == id);
            if (item is null) return null;
            if (req.Text is not null) item.Text = req.Text;
            if (req.Done is not null) item.Done = req.Done.Value;
            await WriteAsync(items);
            return item;
        }
        finally { _lock.Release(); }
    }

    public async Task ReorderAsync(List<ReorderPriorityItem> reorderList)
    {
        await _lock.WaitAsync();
        try
        {
            var items = await ReadAsync();
            foreach (var r in reorderList)
            {
                var item = items.FirstOrDefault(i => i.Id == r.Id);
                if (item is not null) item.Order = r.Order;
            }
            await WriteAsync(items);
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await _lock.WaitAsync();
        try
        {
            var items = await ReadAsync();
            var removed = items.RemoveAll(i => i.Id == id);
            if (removed > 0) await WriteAsync(items);
            return removed > 0;
        }
        finally { _lock.Release(); }
    }

    private async Task<List<PriorityItem>> ReadAsync()
    {
        if (!File.Exists(_filePath)) return [];
        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<PriorityItem>>(json, JsonOptions) ?? [];
    }

    private async Task WriteAsync(List<PriorityItem> items)
    {
        var json = JsonSerializer.Serialize(items, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
    }
}
```

**Step 1: Create the file** with the content above.

**Step 2: Build**

```bash
cd claude-orchestrator-web/backend
dotnet build
```
Expected: Build succeeded, 0 errors.

**Step 3: Commit**

```bash
git add claude-orchestrator-web/backend/Services/PriorityService.cs
git commit -m "feat: add PriorityService with JSON file persistence"
```

---

### Task 3: PrioritiesController

**Files:**
- Create: `claude-orchestrator-web/backend/Controllers/PrioritiesController.cs`

```csharp
using ClaudeOrchestrator.Models;
using ClaudeOrchestrator.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudeOrchestrator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PrioritiesController : ControllerBase
{
    private readonly PriorityService _priorities;

    public PrioritiesController(PriorityService priorities)
    {
        _priorities = priorities;
    }

    // GET /api/priorities
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await _priorities.GetAllAsync();
        return Ok(items);
    }

    // POST /api/priorities
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePriorityRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Text))
            return BadRequest(new { error = "Text is required" });

        var item = await _priorities.CreateAsync(req);
        return Ok(item);
    }

    // PUT /api/priorities/reorder  — must be before {id} route to avoid ambiguity
    [HttpPut("reorder")]
    public async Task<IActionResult> Reorder([FromBody] List<ReorderPriorityItem> reorderList)
    {
        await _priorities.ReorderAsync(reorderList);
        return Ok(new { ok = true });
    }

    // PUT /api/priorities/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdatePriorityRequest req)
    {
        var item = await _priorities.UpdateAsync(id, req);
        if (item is null) return NotFound(new { error = "Priority not found" });
        return Ok(item);
    }

    // DELETE /api/priorities/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var deleted = await _priorities.DeleteAsync(id);
        if (!deleted) return NotFound(new { error = "Priority not found" });
        return Ok(new { deleted = true });
    }
}
```

**Step 1: Create the file** with the content above.

**Step 2: Register PriorityService in Program.cs**

In `claude-orchestrator-web/backend/Program.cs`, add after the line `builder.Services.AddSingleton<AgentHistoryService>();`:

```csharp
builder.Services.AddSingleton<PriorityService>();
```

**Step 3: Build**

```bash
cd claude-orchestrator-web/backend
dotnet build
```
Expected: Build succeeded, 0 errors.

**Step 4: Smoke test the API**

Start the backend:
```bash
dotnet run
```

In another terminal:
```bash
# Create
curl -s -X POST http://localhost:5050/api/priorities \
  -H "Content-Type: application/json" \
  -d '{"text":"Test priority"}' | python -m json.tool

# Get all
curl -s http://localhost:5050/api/priorities | python -m json.tool
```
Expected: item returned with id, text, done=false, order=0.

**Step 5: Commit**

```bash
git add claude-orchestrator-web/backend/Controllers/PrioritiesController.cs \
        claude-orchestrator-web/backend/Program.cs
git commit -m "feat: add PrioritiesController and register PriorityService"
```

---

### Task 4: Backend tests

**Files:**
- Create: `claude-orchestrator-web/tests/PriorityServiceTests.cs`

```csharp
using ClaudeOrchestrator.Models;
using ClaudeOrchestrator.Services;
using Microsoft.Extensions.Configuration;

namespace ClaudeOrchestrator.Tests;

public class PriorityServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PriorityService _service;

    public PriorityServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DataDir"] = _tempDir })
            .Build();

        _service = new PriorityService(config);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task CreateAsync_ReturnsItemWithCorrectText()
    {
        var item = await _service.CreateAsync(new CreatePriorityRequest("Hello"));
        Assert.Equal("Hello", item.Text);
        Assert.False(item.Done);
        Assert.Equal(0, item.Order);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsSortedByOrder()
    {
        await _service.CreateAsync(new CreatePriorityRequest("First"));
        await _service.CreateAsync(new CreatePriorityRequest("Second"));

        var items = await _service.GetAllAsync();
        Assert.Equal(2, items.Count);
        Assert.True(items[0].Order <= items[1].Order);
    }

    [Fact]
    public async Task UpdateAsync_TogglesDone()
    {
        var item = await _service.CreateAsync(new CreatePriorityRequest("Task"));
        var updated = await _service.UpdateAsync(item.Id, new UpdatePriorityRequest(Done: true));
        Assert.NotNull(updated);
        Assert.True(updated!.Done);
    }

    [Fact]
    public async Task UpdateAsync_ChangesText()
    {
        var item = await _service.CreateAsync(new CreatePriorityRequest("Old"));
        var updated = await _service.UpdateAsync(item.Id, new UpdatePriorityRequest(Text: "New"));
        Assert.Equal("New", updated!.Text);
    }

    [Fact]
    public async Task DeleteAsync_RemovesItem()
    {
        var item = await _service.CreateAsync(new CreatePriorityRequest("Delete me"));
        var deleted = await _service.DeleteAsync(item.Id);
        Assert.True(deleted);
        var all = await _service.GetAllAsync();
        Assert.Empty(all);
    }

    [Fact]
    public async Task ReorderAsync_UpdatesOrder()
    {
        var a = await _service.CreateAsync(new CreatePriorityRequest("A"));
        var b = await _service.CreateAsync(new CreatePriorityRequest("B"));

        await _service.ReorderAsync(
        [
            new ReorderPriorityItem(a.Id, 10),
            new ReorderPriorityItem(b.Id, 5),
        ]);

        var items = await _service.GetAllAsync();
        Assert.Equal("B", items[0].Text); // B has order 5, comes first
        Assert.Equal("A", items[1].Text);
    }
}
```

**Step 1: Create the test file** with the content above.

**Step 2: Run tests**

```bash
cd claude-orchestrator-web/tests
dotnet test --verbosity normal
```
Expected: All tests pass (including existing PtySessionTests).

**Step 3: Commit**

```bash
git add claude-orchestrator-web/tests/PriorityServiceTests.cs
git commit -m "test: add PriorityService unit tests"
```

---

### Task 5: Frontend Pinia store

**Files:**
- Create: `claude-orchestrator-web/frontend/src/stores/priorities.js`

```js
import { defineStore } from 'pinia'
import { ref } from 'vue'

export const usePrioritiesStore = defineStore('priorities', () => {
  const items = ref([])

  async function load() {
    const res = await fetch('/api/priorities')
    items.value = await res.json()
  }

  async function create(text) {
    const res = await fetch('/api/priorities', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ text }),
    })
    if (!res.ok) throw new Error('Failed to create priority')
    const item = await res.json()
    items.value.push(item)
    return item
  }

  async function update(id, patch) {
    const res = await fetch(`/api/priorities/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(patch),
    })
    if (!res.ok) throw new Error('Failed to update priority')
    const updated = await res.json()
    const idx = items.value.findIndex(i => i.id === id)
    if (idx >= 0) items.value[idx] = updated
    return updated
  }

  async function remove(id) {
    await fetch(`/api/priorities/${id}`, { method: 'DELETE' })
    items.value = items.value.filter(i => i.id !== id)
  }

  async function reorder(reorderList) {
    await fetch('/api/priorities/reorder', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(reorderList),
    })
  }

  return { items, load, create, update, remove, reorder }
})
```

**Step 1: Create the file** with the content above.

**Step 2: Commit**

```bash
git add claude-orchestrator-web/frontend/src/stores/priorities.js
git commit -m "feat: add priorities Pinia store"
```

---

### Task 6: PrioritiesView component

**Files:**
- Create: `claude-orchestrator-web/frontend/src/views/PrioritiesView.vue`

```vue
<template>
  <div class="flex flex-col flex-1 overflow-hidden bg-gray-950">
    <!-- Toolbar -->
    <div class="flex items-center gap-2 px-4 py-2 border-b border-gray-800 flex-shrink-0">
      <input
        v-model="newText"
        @keydown.enter="handleAdd"
        placeholder="Přidat prioritu..."
        class="flex-1 bg-gray-800 text-sm text-white placeholder-gray-500 rounded px-3 py-1 outline-none focus:ring-1 focus:ring-blue-500"
      />
      <button
        @click="handleAdd"
        class="text-xs bg-blue-600 hover:bg-blue-500 text-white px-3 py-1 rounded transition-colors"
      >+</button>
    </div>

    <!-- List -->
    <div class="flex-1 overflow-y-auto px-4 py-2 space-y-1">
      <div
        v-for="item in store.items"
        :key="item.id"
        class="flex items-center gap-2 bg-gray-900 rounded px-3 py-2 group"
        :class="{ 'opacity-50': item.done }"
        draggable="true"
        @dragstart="onDragStart(item)"
        @dragover.prevent
        @drop="onDrop(item)"
      >
        <!-- Drag handle -->
        <span class="text-gray-600 cursor-grab select-none text-lg leading-none">⠿</span>

        <!-- Checkbox -->
        <input
          type="checkbox"
          :checked="item.done"
          @change="store.update(item.id, { done: !item.done })"
          class="accent-blue-500 cursor-pointer"
        />

        <!-- Text / inline edit -->
        <span
          v-if="editingId !== item.id"
          class="flex-1 text-sm"
          :class="{ 'line-through text-gray-500': item.done }"
        >{{ item.text }}</span>
        <input
          v-else
          v-model="editText"
          @keydown.enter="saveEdit(item.id)"
          @blur="saveEdit(item.id)"
          class="flex-1 bg-gray-800 text-sm text-white rounded px-2 py-0.5 outline-none focus:ring-1 focus:ring-blue-500"
          ref="editInputRef"
        />

        <!-- Actions -->
        <div class="flex gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
          <button
            v-if="editingId !== item.id"
            @click="startEdit(item)"
            class="text-gray-500 hover:text-white text-xs px-1"
            title="Edit"
          >✏</button>
          <button
            @click="store.remove(item.id)"
            class="text-gray-500 hover:text-red-400 text-xs px-1"
            title="Delete"
          >🗑</button>
        </div>
      </div>

      <p v-if="store.items.length === 0" class="text-xs text-gray-600 py-4 text-center">
        Žádné priority. Přidej první.
      </p>
    </div>
  </div>
</template>

<script setup>
import { ref, nextTick, onMounted } from 'vue'
import { usePrioritiesStore } from '../stores/priorities'

const store = usePrioritiesStore()

const newText = ref('')
const editingId = ref(null)
const editText = ref('')
const editInputRef = ref(null)
let dragItem = null

async function handleAdd() {
  const text = newText.value.trim()
  if (!text) return
  await store.create(text)
  newText.value = ''
}

function startEdit(item) {
  editingId.value = item.id
  editText.value = item.text
  nextTick(() => editInputRef.value?.focus())
}

async function saveEdit(id) {
  const text = editText.value.trim()
  if (text) await store.update(id, { text })
  editingId.value = null
}

function onDragStart(item) {
  dragItem = item
}

async function onDrop(targetItem) {
  if (!dragItem || dragItem.id === targetItem.id) return

  // Swap orders
  const reorderList = store.items.map(i => ({ id: i.id, order: i.order }))
  const dragEntry  = reorderList.find(r => r.id === dragItem.id)
  const dropEntry  = reorderList.find(r => r.id === targetItem.id)
  if (!dragEntry || !dropEntry) return

  const tempOrder = dragEntry.order
  dragEntry.order = dropEntry.order
  dropEntry.order = tempOrder

  await store.reorder(reorderList)
  await store.load() // refresh sorted order
  dragItem = null
}

onMounted(() => store.load())
</script>
```

**Step 1: Create the file** with the content above.

**Step 2: Commit**

```bash
git add claude-orchestrator-web/frontend/src/views/PrioritiesView.vue
git commit -m "feat: add PrioritiesView with drag-and-drop and inline edit"
```

---

### Task 7: Wire up router + nav

**Files:**
- Modify: `claude-orchestrator-web/frontend/src/router/index.js`
- Modify: `claude-orchestrator-web/frontend/src/App.vue`

**Step 1: Add route to router/index.js**

Add the import after existing imports:
```js
import PrioritiesView from '../views/PrioritiesView.vue'
```

Add the route inside the `routes` array:
```js
{ path: '/priorities', component: PrioritiesView },
```

**Step 2: Add nav link in App.vue**

Add a new `RouterLink` in the `<nav>` block, between Tasks and History:
```html
<RouterLink
  to="/priorities"
  class="px-3 py-1 text-xs rounded transition-colors"
  :class="$route.path === '/priorities' ? 'bg-blue-600 text-white' : 'text-gray-400 hover:text-white hover:bg-gray-700'"
>
  Priorities
</RouterLink>
```

**Step 3: Manual test**

1. Start backend: `cd claude-orchestrator-web/backend && dotnet run`
2. Open browser → http://localhost:5050
3. Click "Priorities" tab
4. Add a few items
5. Check/uncheck one
6. Edit one (click ✏, change text, press Enter)
7. Delete one
8. Drag to reorder

**Step 4: Commit**

```bash
git add claude-orchestrator-web/frontend/src/router/index.js \
        claude-orchestrator-web/frontend/src/App.vue
git commit -m "feat: add Priorities tab to navigation"
```
