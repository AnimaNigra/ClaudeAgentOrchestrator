# Task Dashboard + Session Persistence Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a Kanban task board (/tasks route) with Todo/In-Progress/Done columns and task assignment to agents, plus session persistence via Claude Code's `--resume` flag.

**Architecture:** Vue Router added to frontend with two routes (/ = agents, /tasks = kanban). Backend gains TaskService + AgentHistoryService writing to `data/tasks.json` and `data/agents.json`. PtySession detects Claude Code's session ID from PTY output; AgentManager persists history on agent exit/kill.

**Tech Stack:** Vue 3 + Pinia + vue-router@4, ASP.NET Core 9, System.Text.Json (no new packages needed)

---

## Task 1: Backend — New Data Models

**Files:**
- Create: `claude-orchestrator-web/backend/Models/TaskItem.cs`
- Create: `claude-orchestrator-web/backend/Models/AgentRecord.cs`
- Modify: `claude-orchestrator-web/backend/Models/AgentEvent.cs`
- Modify: `claude-orchestrator-web/backend/Models/Agent.cs`

**Step 1: Create TaskItem.cs**

```csharp
namespace ClaudeOrchestrator.Models;

public class TaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Prompt { get; set; }
    public string Status { get; set; } = "todo"; // todo | in-progress | done
    public string? AgentId { get; set; }
    public string? AgentName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

**Step 2: Create AgentRecord.cs**

```csharp
namespace ClaudeOrchestrator.Models;

public class AgentRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Cwd { get; set; }
    public string? SessionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public List<string> TaskIds { get; set; } = new();
}
```

**Step 3: Extend AgentEvent.cs with new DTOs**

Add to the bottom of `AgentEvent.cs`:

```csharp
public record CreateTaskRequest(string Title, string? Description = null, string? Prompt = null);

public record UpdateTaskRequest(
    string Title,
    string? Description = null,
    string? Prompt = null,
    string Status = "todo",
    string? AgentId = null,
    string? AgentName = null);

public record AssignTaskRequest(string AgentId);
```

Also modify the existing `SpawnRequest` record to add `ResumeSessionId`:

```csharp
// BEFORE:
public record SpawnRequest(string Name, string? Cwd = null);

// AFTER:
public record SpawnRequest(string Name, string? Cwd = null, string? ResumeSessionId = null);
```

**Step 4: Add ResumeSessionId to Agent model**

In `Agent.cs`, add one property after `Cwd`:

```csharp
public string? ResumeSessionId { get; set; }
```

This property stores the session to resume (from `SpawnRequest`) — separate from `SessionId` which stores the current session's ID captured from PTY output.

**Step 5: Commit**

```bash
cd claude-orchestrator-web/backend
git add Models/TaskItem.cs Models/AgentRecord.cs Models/AgentEvent.cs Models/Agent.cs
git commit -m "feat: add TaskItem, AgentRecord models and extend DTOs"
```

---

## Task 2: Backend — TaskService

**Files:**
- Create: `claude-orchestrator-web/backend/Services/TaskService.cs`

**Step 1: Create TaskService.cs**

```csharp
using System.Text.Json;
using ClaudeOrchestrator.Models;

namespace ClaudeOrchestrator.Services;

public class TaskService
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TaskService(IConfiguration configuration)
    {
        var dataDir = configuration.GetValue<string>("DataDir")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "tasks.json");
    }

    public async Task<List<TaskItem>> GetAllAsync()
    {
        await _lock.WaitAsync();
        try { return await ReadAsync(); }
        finally { _lock.Release(); }
    }

    public async Task<TaskItem?> GetByIdAsync(string id)
    {
        var tasks = await GetAllAsync();
        return tasks.FirstOrDefault(t => t.Id == id);
    }

    public async Task<TaskItem> CreateAsync(CreateTaskRequest req)
    {
        var task = new TaskItem
        {
            Title = req.Title,
            Description = req.Description,
            Prompt = req.Prompt
        };

        await _lock.WaitAsync();
        try
        {
            var tasks = await ReadAsync();
            tasks.Add(task);
            await WriteAsync(tasks);
            return task;
        }
        finally { _lock.Release(); }
    }

    public async Task<TaskItem?> UpdateAsync(string id, UpdateTaskRequest req)
    {
        await _lock.WaitAsync();
        try
        {
            var tasks = await ReadAsync();
            var idx = tasks.FindIndex(t => t.Id == id);
            if (idx < 0) return null;

            var existing = tasks[idx];
            existing.Title = req.Title;
            existing.Description = req.Description;
            existing.Prompt = req.Prompt;
            existing.Status = req.Status;
            existing.AgentId = req.AgentId;
            existing.AgentName = req.AgentName;
            existing.UpdatedAt = DateTime.UtcNow;

            await WriteAsync(tasks);
            return existing;
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await _lock.WaitAsync();
        try
        {
            var tasks = await ReadAsync();
            var removed = tasks.RemoveAll(t => t.Id == id);
            if (removed > 0) await WriteAsync(tasks);
            return removed > 0;
        }
        finally { _lock.Release(); }
    }

    // Called by AgentManager when an agent is assigned to a task
    public async Task SetInProgressAsync(string taskId, string agentId, string agentName)
    {
        await _lock.WaitAsync();
        try
        {
            var tasks = await ReadAsync();
            var task = tasks.FirstOrDefault(t => t.Id == taskId);
            if (task is null) return;
            task.Status = "in-progress";
            task.AgentId = agentId;
            task.AgentName = agentName;
            task.UpdatedAt = DateTime.UtcNow;
            await WriteAsync(tasks);
        }
        finally { _lock.Release(); }
    }

    // Private helpers — must be called from within _lock
    private async Task<List<TaskItem>> ReadAsync()
    {
        if (!File.Exists(_filePath)) return new List<TaskItem>();
        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<TaskItem>>(json, JsonOptions) ?? new();
    }

    private async Task WriteAsync(List<TaskItem> tasks)
    {
        var json = JsonSerializer.Serialize(tasks, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
    }
}
```

**Step 2: Commit**

```bash
git add Services/TaskService.cs
git commit -m "feat: add TaskService for JSON-backed task CRUD"
```

---

## Task 3: Backend — AgentHistoryService

**Files:**
- Create: `claude-orchestrator-web/backend/Services/AgentHistoryService.cs`

**Step 1: Create AgentHistoryService.cs**

```csharp
using System.Text.Json;
using ClaudeOrchestrator.Models;

namespace ClaudeOrchestrator.Services;

public class AgentHistoryService
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AgentHistoryService(IConfiguration configuration)
    {
        var dataDir = configuration.GetValue<string>("DataDir")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "agents.json");
    }

    public async Task<List<AgentRecord>> GetAllAsync()
    {
        await _lock.WaitAsync();
        try { return await ReadAsync(); }
        finally { _lock.Release(); }
    }

    public async Task SaveAgentAsync(Agent agent, List<string>? taskIds = null)
    {
        var record = new AgentRecord
        {
            Id = agent.Id,
            Name = agent.Name,
            Cwd = agent.Cwd,
            SessionId = string.IsNullOrEmpty(agent.SessionId) ? null : agent.SessionId,
            CreatedAt = agent.CreatedAt,
            FinishedAt = agent.FinishedAt ?? DateTime.UtcNow,
            TaskIds = taskIds ?? new List<string>()
        };

        await _lock.WaitAsync();
        try
        {
            var records = await ReadAsync();
            // Replace if exists, otherwise append
            var idx = records.FindIndex(r => r.Id == record.Id);
            if (idx >= 0) records[idx] = record;
            else records.Add(record);
            await WriteAsync(records);
        }
        finally { _lock.Release(); }
    }

    private async Task<List<AgentRecord>> ReadAsync()
    {
        if (!File.Exists(_filePath)) return new List<AgentRecord>();
        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<AgentRecord>>(json, JsonOptions) ?? new();
    }

    private async Task WriteAsync(List<AgentRecord> records)
    {
        var json = JsonSerializer.Serialize(records, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
    }
}
```

**Step 2: Commit**

```bash
git add Services/AgentHistoryService.cs
git commit -m "feat: add AgentHistoryService for JSON-backed agent history"
```

---

## Task 4: Backend — Session ID Detection in PtySession + Resume Support

**Files:**
- Modify: `claude-orchestrator-web/backend/Services/PtySession.cs`

When Claude Code exits after `/exit`, it prints to the terminal something like:
```
╭─────────────────────────────────────────────────────────╮
│ > To resume this conversation, use: claude --resume ... │
```
The regex captures the hex session ID after `--resume`.

**Step 1: Add session ID regex field to PtySession**

In `PtySession.cs`, after the existing `IdlePrompt` regex declaration (around line 52), add:

```csharp
private static readonly Regex SessionIdRegex = new(
    @"--resume\s+([a-zA-Z0-9_-]+)",
    RegexOptions.Compiled);
```

**Step 2: Add OnExited callback property**

After the existing fields at the top of the class, add:

```csharp
public Action? OnExited { get; set; }
```

**Step 3: Fire OnExited in OnProcessExited**

In `OnProcessExited` method, after setting `_agent.FinishedAt`, add the callback call:

```csharp
private void OnProcessExited(object? sender, EventArgs e)
{
    if (_disposed) return;
    _idleTimer?.Dispose();
    _forceIdleTimer?.Dispose();
    _agent.Status = AgentStatus.Done;
    _agent.FinishedAt = DateTime.UtcNow;
    OnExited?.Invoke();   // ← ADD THIS LINE
    _ = _emitEvent(_agent.Id, "agent_exited",
        new { agentId = _agent.Id, exitCode = _process?.ExitCode });
}
```

**Step 4: Detect session ID in ReadLoopAsync**

In `ReadLoopAsync`, inside the loop after `FeedStateDetector(text)` is called, add session ID detection:

```csharp
// Feed the state detector
try
{
    var text = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    FeedStateDetector(text);

    // Capture Claude Code session ID from exit message (only once)
    if (string.IsNullOrEmpty(_agent.SessionId))
    {
        var match = SessionIdRegex.Match(text);
        if (match.Success)
            _agent.SessionId = match.Groups[1].Value;
    }
}
catch { /* malformed base64 — ignore */ }
```

**Step 5: Support --resume in StartAsync**

In `StartAsync`, after the line that builds `claudeArgs` via `ResolveClaudeAsync()`, add resume support:

```csharp
var (claudeCmd, claudeArgs) = await ResolveClaudeAsync();

// If resuming a previous session, append --resume <id>
if (!string.IsNullOrEmpty(_agent.ResumeSessionId))
    claudeArgs = [..claudeArgs, "--resume", _agent.ResumeSessionId];
```

**Step 6: Verify the build compiles**

```bash
cd claude-orchestrator-web/backend
dotnet build
```
Expected: Build succeeded, 0 errors.

**Step 7: Commit**

```bash
git add Services/PtySession.cs
git commit -m "feat: detect Claude session ID from PTY output and support --resume"
```

---

## Task 5: Backend — AgentManager History Persistence

**Files:**
- Modify: `claude-orchestrator-web/backend/Services/AgentManager.cs`

**Step 1: Add AgentHistoryService field and constructor parameter**

In `AgentManager.cs`, add the field and update the constructor:

```csharp
// ADD field after existing fields:
private readonly AgentHistoryService? _historyService;

// UPDATE constructor:
public AgentManager(int maxAgents = 10, string orchestratorUrl = "http://localhost:5050",
    AgentHistoryService? historyService = null)
{
    _maxAgents = maxAgents;
    OrchestratorUrl = orchestratorUrl;
    _historyService = historyService;
}
```

**Step 2: Update SpawnAgentAsync to accept ResumeSessionId**

Update the method signature and body to pass `ResumeSessionId` through:

```csharp
public async Task<Agent> SpawnAgentAsync(string name, string? cwd = null, string? resumeSessionId = null)
{
    await _lock.WaitAsync();
    try
    {
        var agent = new Agent { Name = name, Cwd = cwd, ResumeSessionId = resumeSessionId };
        var session = new PtySession(agent, EmitEventAsync, OrchestratorUrl);

        // Wire up history persistence on natural exit
        if (_historyService is not null)
            session.OnExited = () => _ = _historyService.SaveAgentAsync(agent);

        _agents[agent.Id] = agent;
        _sessions[agent.Id] = session;

        await session.StartAsync();

        await EmitEventAsync(agent.Id, "agent_spawned", new
        {
            id = agent.Id,
            name = agent.Name,
            cwd = agent.Cwd,
            status = agent.Status.ToString().ToLower()
        });

        return agent;
    }
    finally
    {
        _lock.Release();
    }
}
```

**Step 3: Persist history on KillAgentAsync**

In `KillAgentAsync`, after setting `agent.FinishedAt`, add history save:

```csharp
if (_agents.TryGetValue(agentId, out var agent))
{
    agent.Status = AgentStatus.Done;
    agent.FinishedAt = DateTime.UtcNow;
    if (_historyService is not null)
        await _historyService.SaveAgentAsync(agent);   // ← ADD THIS
    await EmitEventAsync(agentId, "agent_killed", new { });
}
```

**Step 4: Verify build**

```bash
dotnet build
```
Expected: 0 errors.

**Step 5: Commit**

```bash
git add Services/AgentManager.cs
git commit -m "feat: wire AgentHistoryService into AgentManager for history persistence"
```

---

## Task 6: Backend — TasksController

**Files:**
- Create: `claude-orchestrator-web/backend/Controllers/TasksController.cs`

**Step 1: Create TasksController.cs**

```csharp
using ClaudeOrchestrator.Models;
using ClaudeOrchestrator.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudeOrchestrator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TasksController : ControllerBase
{
    private readonly TaskService _tasks;
    private readonly AgentManager _agents;

    public TasksController(TaskService tasks, AgentManager agents)
    {
        _tasks = tasks;
        _agents = agents;
    }

    // GET /api/tasks
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tasks = await _tasks.GetAllAsync();
        return Ok(tasks);
    }

    // POST /api/tasks
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTaskRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { error = "Title is required" });

        var task = await _tasks.CreateAsync(req);
        return Ok(task);
    }

    // PUT /api/tasks/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateTaskRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { error = "Title is required" });

        var task = await _tasks.UpdateAsync(id, req);
        if (task is null) return NotFound(new { error = "Task not found" });
        return Ok(task);
    }

    // DELETE /api/tasks/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var deleted = await _tasks.DeleteAsync(id);
        if (!deleted) return NotFound(new { error = "Task not found" });
        return Ok(new { deleted = true });
    }

    // POST /api/tasks/{id}/assign  — body: { agentId }
    [HttpPost("{id}/assign")]
    public async Task<IActionResult> Assign(string id, [FromBody] AssignTaskRequest req)
    {
        var task = await _tasks.GetByIdAsync(id);
        if (task is null) return NotFound(new { error = "Task not found" });

        var agent = _agents.GetAgent(req.AgentId);
        if (agent is null || agent.Status == AgentStatus.Done || agent.Status == AgentStatus.Error)
            return BadRequest(new { error = "Agent is not available" });

        // Send the prompt to the agent (if there is one)
        if (!string.IsNullOrEmpty(task.Prompt))
        {
            await _agents.WriteInputAsync(req.AgentId, task.Prompt + "\n");
        }

        // Update task to in-progress
        await _tasks.SetInProgressAsync(id, agent.Id, agent.Name);

        var updated = await _tasks.GetByIdAsync(id);
        return Ok(updated);
    }
}
```

**Step 2: Verify build**

```bash
dotnet build
```
Expected: 0 errors.

**Step 3: Commit**

```bash
git add Controllers/TasksController.cs
git commit -m "feat: add TasksController with CRUD and assign endpoint"
```

---

## Task 7: Backend — HistoryController

**Files:**
- Create: `claude-orchestrator-web/backend/Controllers/HistoryController.cs`

**Step 1: Create HistoryController.cs**

```csharp
using ClaudeOrchestrator.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudeOrchestrator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HistoryController : ControllerBase
{
    private readonly AgentHistoryService _history;

    public HistoryController(AgentHistoryService history)
    {
        _history = history;
    }

    // GET /api/history
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var records = await _history.GetAllAsync();
        // Return newest first
        return Ok(records.OrderByDescending(r => r.FinishedAt));
    }
}
```

**Step 2: Commit**

```bash
git add Controllers/HistoryController.cs
git commit -m "feat: add HistoryController for reading agent history"
```

---

## Task 8: Backend — Register Services in Program.cs

**Files:**
- Modify: `claude-orchestrator-web/backend/Program.cs`

**Step 1: Register TaskService and AgentHistoryService**

In `Program.cs`, after `builder.Services.AddHttpClient(...)` line, add:

```csharp
builder.Services.AddSingleton<TaskService>();
builder.Services.AddSingleton<AgentHistoryService>();
```

**Step 2: Inject AgentHistoryService into AgentManager**

Update the `AgentManager` singleton registration to pass `historyService`:

```csharp
builder.Services.AddSingleton<AgentManager>(sp =>
{
    var hub = sp.GetRequiredService<IHubContext<AgentHub>>();
    var historyService = sp.GetRequiredService<AgentHistoryService>();  // ← ADD
    var manager = new AgentManager(
        maxAgents: 10,
        orchestratorUrl: $"http://localhost:{port}",
        historyService: historyService);                                 // ← ADD
    manager.AddEventListener(async (agentId, eventType, data) =>
    {
        var agent = manager.GetAgent(agentId);
        await hub.Clients.All.SendAsync("AgentEvent", new { agentId, eventType, data, agent });
    });
    return manager;
});
```

**Step 3: Update AgentsController.SpawnAgent to pass ResumeSessionId**

Open `Controllers/AgentsController.cs` and find the `SpawnAgent` POST endpoint. Update the `SpawnAgentAsync` call:

```csharp
// BEFORE:
var agent = await _manager.SpawnAgentAsync(req.Name, req.Cwd);

// AFTER:
var agent = await _manager.SpawnAgentAsync(req.Name, req.Cwd, req.ResumeSessionId);
```

**Step 4: Full build + run check**

```bash
dotnet build
dotnet run &
# Wait 5s for startup, then test
curl http://localhost:5050/api/tasks
# Expected: []
curl -X POST http://localhost:5050/api/tasks \
  -H "Content-Type: application/json" \
  -d '{"title":"Test task","prompt":"Write hello world"}'
# Expected: task object with id, title, status: "todo"
curl http://localhost:5050/api/history
# Expected: []
# Kill server with Ctrl+C
```

**Step 5: Commit**

```bash
git add Program.cs Controllers/AgentsController.cs
git commit -m "feat: register TaskService/AgentHistoryService, wire resume into spawn"
```

---

## Task 9: Frontend — Install vue-router + Setup Routing

**Files:**
- Modify: `claude-orchestrator-web/frontend/src/main.js`
- Create: `claude-orchestrator-web/frontend/src/router/index.js`

**Step 1: Install vue-router**

```bash
cd claude-orchestrator-web/frontend
npm install vue-router@4
```
Expected: `vue-router` added to `package.json` dependencies.

**Step 2: Create src/router/index.js**

```js
import { createRouter, createWebHistory } from 'vue-router'
import AgentsView from '../views/AgentsView.vue'
import TasksView from '../views/TasksView.vue'

const routes = [
  { path: '/', component: AgentsView },
  { path: '/tasks', component: TasksView },
]

export default createRouter({
  history: createWebHistory(),
  routes,
})
```

**Step 3: Add router to main.js**

```js
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import router from './router/index.js'
import './assets/main.css'

const app = createApp(App)
app.use(createPinia())
app.use(router)
app.mount('#app')
```

**Step 4: Commit**

```bash
cd claude-orchestrator-web/frontend
git add src/router/index.js src/main.js package.json package-lock.json
git commit -m "feat: add vue-router with / and /tasks routes"
```

---

## Task 10: Frontend — Extract AgentsView + Update App.vue

**Files:**
- Create: `claude-orchestrator-web/frontend/src/views/AgentsView.vue`
- Modify: `claude-orchestrator-web/frontend/src/App.vue`

The current `App.vue` contains the full agents UI. We move that into `AgentsView.vue`, then make `App.vue` a shell with navigation + `<RouterView />`.

**Step 1: Create src/views/AgentsView.vue**

Move the entire body of App.vue (minus the `<header>`) into this file:

```vue
<template>
  <div class="flex flex-1 overflow-hidden">
    <!-- Left: agent cards -->
    <aside class="w-64 flex-shrink-0 bg-gray-950 border-r border-gray-800 overflow-y-auto p-2 flex flex-col gap-2">
      <AgentCard
        v-for="agent in store.agentList"
        :key="agent.id"
        :agent="agent"
        :is-active="store.activeAgentId === agent.id"
        @select="store.activeAgentId = $event"
      />
      <div v-if="!store.agentList.length" class="text-xs text-gray-600 p-2">
        No agents. Type <code class="text-blue-400">create &lt;name&gt;</code> below.
      </div>
    </aside>

    <!-- Right: terminal panel -->
    <main class="flex-1 overflow-hidden bg-black">
      <TerminalPanel />
    </main>
  </div>

  <!-- Command bar -->
  <CommandBar ref="cmdBar" />

  <!-- Permission dialog (rendered via Teleport to body) -->
  <PermissionDialog />
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { useAgentsStore } from '../stores/agents'
import AgentCard from '../components/AgentCard.vue'
import TerminalPanel from '../components/TerminalPanel.vue'
import CommandBar from '../components/CommandBar.vue'
import PermissionDialog from '../components/PermissionDialog.vue'

const store = useAgentsStore()
const cmdBar = ref(null)

onMounted(() => {
  cmdBar.value?.focus()
})
</script>
```

**Step 2: Rewrite App.vue as navigation shell**

```vue
<template>
  <div class="flex flex-col h-screen">
    <!-- Header with navigation tabs -->
    <header class="flex items-center justify-between px-4 py-2 bg-gray-900 border-b border-gray-700 flex-shrink-0">
      <div class="flex items-center gap-4">
        <h1 class="text-sm font-bold text-blue-400">⚡ Claude Agent Orchestrator</h1>
        <nav class="flex gap-1">
          <RouterLink
            to="/"
            class="px-3 py-1 text-xs rounded transition-colors"
            :class="$route.path === '/' ? 'bg-blue-600 text-white' : 'text-gray-400 hover:text-white hover:bg-gray-700'"
          >
            Agents
          </RouterLink>
          <RouterLink
            to="/tasks"
            class="px-3 py-1 text-xs rounded transition-colors"
            :class="$route.path === '/tasks' ? 'bg-blue-600 text-white' : 'text-gray-400 hover:text-white hover:bg-gray-700'"
          >
            Tasks
          </RouterLink>
        </nav>
      </div>
      <div class="flex gap-4 text-xs text-gray-400">
        <span>{{ store.agentList.length }} agents</span>
        <span>{{ runningCount }} running</span>
      </div>
    </header>

    <!-- Route content -->
    <RouterView class="flex-1 overflow-hidden flex flex-col" />
  </div>
</template>

<script setup>
import { computed, onMounted } from 'vue'
import { useAgentsStore } from './stores/agents'

const store = useAgentsStore()

const runningCount = computed(() =>
  store.agentList.filter(a => a.status === 'Running').length
)

onMounted(async () => {
  await store.connect()
  if ('Notification' in window && Notification.permission === 'default')
    Notification.requestPermission()
})
</script>
```

**Step 3: Verify app still works**

Start the backend (`dotnet run` in `backend/`) and visit `http://localhost:5050`. The agents view should still look exactly the same. Click "Tasks" in the nav — should navigate to `/tasks` (blank for now, TasksView not created yet — that's fine).

**Step 4: Commit**

```bash
cd claude-orchestrator-web/frontend
git add src/views/AgentsView.vue src/App.vue
git commit -m "feat: extract AgentsView, convert App.vue to navigation shell"
```

---

## Task 11: Frontend — Tasks Pinia Store

**Files:**
- Create: `claude-orchestrator-web/frontend/src/stores/tasks.js`

**Step 1: Create src/stores/tasks.js**

```js
import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

export const useTasksStore = defineStore('tasks', () => {
  const tasks = ref([])
  const loading = ref(false)
  const error = ref(null)

  const todoTasks = computed(() => tasks.value.filter(t => t.status === 'todo'))
  const inProgressTasks = computed(() => tasks.value.filter(t => t.status === 'in-progress'))
  const doneTasks = computed(() => tasks.value.filter(t => t.status === 'done'))

  async function loadTasks() {
    loading.value = true
    error.value = null
    try {
      const res = await fetch('/api/tasks')
      tasks.value = await res.json()
    } catch (e) {
      error.value = 'Failed to load tasks'
    } finally {
      loading.value = false
    }
  }

  async function createTask(title, description, prompt) {
    const res = await fetch('/api/tasks', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ title, description, prompt }),
    })
    if (!res.ok) throw new Error('Failed to create task')
    const task = await res.json()
    tasks.value.push(task)
    return task
  }

  async function updateTask(id, updates) {
    const task = tasks.value.find(t => t.id === id)
    if (!task) return
    const res = await fetch(`/api/tasks/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        title: updates.title ?? task.title,
        description: updates.description ?? task.description,
        prompt: updates.prompt ?? task.prompt,
        status: updates.status ?? task.status,
        agentId: updates.agentId ?? task.agentId,
        agentName: updates.agentName ?? task.agentName,
      }),
    })
    if (!res.ok) throw new Error('Failed to update task')
    const updated = await res.json()
    const idx = tasks.value.findIndex(t => t.id === id)
    if (idx >= 0) tasks.value[idx] = updated
    return updated
  }

  async function deleteTask(id) {
    await fetch(`/api/tasks/${id}`, { method: 'DELETE' })
    tasks.value = tasks.value.filter(t => t.id !== id)
  }

  async function assignTask(taskId, agentId) {
    const res = await fetch(`/api/tasks/${taskId}/assign`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ agentId }),
    })
    if (!res.ok) {
      const err = await res.json()
      throw new Error(err.error ?? 'Failed to assign task')
    }
    const updated = await res.json()
    const idx = tasks.value.findIndex(t => t.id === taskId)
    if (idx >= 0) tasks.value[idx] = updated
    return updated
  }

  async function markDone(taskId) {
    return updateTask(taskId, { status: 'done' })
  }

  return {
    tasks, loading, error,
    todoTasks, inProgressTasks, doneTasks,
    loadTasks, createTask, updateTask, deleteTask, assignTask, markDone,
  }
})
```

**Step 2: Commit**

```bash
git add src/stores/tasks.js
git commit -m "feat: add tasks Pinia store with CRUD and assign"
```

---

## Task 12: Frontend — TaskCard Component

**Files:**
- Create: `claude-orchestrator-web/frontend/src/components/TaskCard.vue`

**Step 1: Create src/components/TaskCard.vue**

```vue
<template>
  <div class="bg-gray-800 rounded-lg p-3 flex flex-col gap-2 border border-gray-700 hover:border-gray-600 transition-colors">
    <!-- Title + delete button -->
    <div class="flex items-start justify-between gap-2">
      <span class="text-sm font-medium text-white leading-tight">{{ task.title }}</span>
      <button
        @click="$emit('delete', task.id)"
        class="text-gray-600 hover:text-red-400 text-xs flex-shrink-0 transition-colors"
        title="Delete task"
      >✕</button>
    </div>

    <!-- Description -->
    <p v-if="task.description" class="text-xs text-gray-400 leading-relaxed line-clamp-2">
      {{ task.description }}
    </p>

    <!-- Prompt (collapsible) -->
    <div v-if="task.prompt">
      <button
        @click="promptOpen = !promptOpen"
        class="text-xs text-blue-400 hover:text-blue-300 flex items-center gap-1"
      >
        <span>{{ promptOpen ? '▾' : '▸' }}</span>
        <span>Prompt</span>
      </button>
      <pre v-if="promptOpen" class="mt-1 text-xs text-gray-300 bg-gray-900 rounded p-2 whitespace-pre-wrap overflow-auto max-h-24">{{ task.prompt }}</pre>
    </div>

    <!-- Agent badge (when assigned) -->
    <div v-if="task.agentName" class="flex items-center gap-1 text-xs">
      <span class="text-gray-500">🤖</span>
      <span class="text-gray-300">{{ task.agentName }}</span>
    </div>

    <!-- Action buttons -->
    <div class="flex gap-2 mt-1">
      <!-- TODO: assign to agent -->
      <template v-if="task.status === 'todo'">
        <select
          v-if="availableAgents.length"
          @change="onAssign($event.target.value); $event.target.value = ''"
          class="flex-1 text-xs bg-gray-700 text-white rounded px-2 py-1 border border-gray-600 hover:border-gray-500 cursor-pointer"
        >
          <option value="" disabled selected>Assign to agent…</option>
          <option v-for="a in availableAgents" :key="a.id" :value="a.id">
            {{ a.name }} ({{ a.status }})
          </option>
        </select>
        <span v-else class="text-xs text-gray-600 italic">No agents running</span>
      </template>

      <!-- IN PROGRESS: mark done -->
      <template v-if="task.status === 'in-progress'">
        <button
          @click="$emit('mark-done', task.id)"
          class="flex-1 text-xs bg-green-700 hover:bg-green-600 text-white rounded px-2 py-1 transition-colors"
        >
          Mark Done ✓
        </button>
      </template>

      <!-- DONE: resume agent (only if sessionId exists) -->
      <template v-if="task.status === 'done' && agentSessionId">
        <button
          @click="$emit('resume', { agentName: task.agentName, sessionId: agentSessionId, cwd: task.agentCwd })"
          class="flex-1 text-xs bg-blue-700 hover:bg-blue-600 text-white rounded px-2 py-1 transition-colors"
        >
          Resume Agent ↺
        </button>
      </template>
    </div>
  </div>
</template>

<script setup>
import { ref } from 'vue'

const props = defineProps({
  task: { type: Object, required: true },
  availableAgents: { type: Array, default: () => [] },
  // Historical agent record for this task (for resume)
  agentRecord: { type: Object, default: null },
})

const emit = defineEmits(['assign', 'mark-done', 'delete', 'resume'])

const promptOpen = ref(false)

const agentSessionId = computed(() => props.agentRecord?.sessionId ?? null)

function onAssign(agentId) {
  if (agentId) emit('assign', { taskId: props.task.id, agentId })
}
</script>

<script>
import { computed } from 'vue'
</script>
```

Wait — the `computed` import must be in `<script setup>`. Fix the component:

```vue
<template>
  <div class="bg-gray-800 rounded-lg p-3 flex flex-col gap-2 border border-gray-700 hover:border-gray-600 transition-colors">
    <!-- Title + delete button -->
    <div class="flex items-start justify-between gap-2">
      <span class="text-sm font-medium text-white leading-tight">{{ task.title }}</span>
      <button
        @click="$emit('delete', task.id)"
        class="text-gray-600 hover:text-red-400 text-xs flex-shrink-0 transition-colors"
        title="Delete task"
      >✕</button>
    </div>

    <!-- Description -->
    <p v-if="task.description" class="text-xs text-gray-400 leading-relaxed line-clamp-2">
      {{ task.description }}
    </p>

    <!-- Prompt (collapsible) -->
    <div v-if="task.prompt">
      <button
        @click="promptOpen = !promptOpen"
        class="text-xs text-blue-400 hover:text-blue-300 flex items-center gap-1"
      >
        <span>{{ promptOpen ? '▾' : '▸' }}</span>
        <span>Prompt</span>
      </button>
      <pre v-if="promptOpen" class="mt-1 text-xs text-gray-300 bg-gray-900 rounded p-2 whitespace-pre-wrap overflow-auto max-h-24">{{ task.prompt }}</pre>
    </div>

    <!-- Agent badge (when assigned) -->
    <div v-if="task.agentName" class="flex items-center gap-1 text-xs">
      <span class="text-gray-500">🤖</span>
      <span class="text-gray-300">{{ task.agentName }}</span>
    </div>

    <!-- Action buttons -->
    <div class="flex gap-2 mt-1">
      <!-- TODO: assign to agent -->
      <template v-if="task.status === 'todo'">
        <select
          v-if="availableAgents.length"
          @change="onAssign($event.target.value); $event.target.value = ''"
          class="flex-1 text-xs bg-gray-700 text-white rounded px-2 py-1 border border-gray-600 hover:border-gray-500 cursor-pointer"
        >
          <option value="" disabled selected>Assign to agent…</option>
          <option v-for="a in availableAgents" :key="a.id" :value="a.id">
            {{ a.name }} ({{ a.status }})
          </option>
        </select>
        <span v-else class="text-xs text-gray-600 italic">No agents running</span>
      </template>

      <!-- IN PROGRESS: mark done -->
      <template v-if="task.status === 'in-progress'">
        <button
          @click="$emit('mark-done', task.id)"
          class="flex-1 text-xs bg-green-700 hover:bg-green-600 text-white rounded px-2 py-1 transition-colors"
        >
          Mark Done ✓
        </button>
      </template>

      <!-- DONE: resume agent (only if sessionId exists) -->
      <template v-if="task.status === 'done' && agentSessionId">
        <button
          @click="$emit('resume', { agentName: task.agentName, sessionId: agentSessionId })"
          class="flex-1 text-xs bg-blue-700 hover:bg-blue-600 text-white rounded px-2 py-1 transition-colors"
        >
          Resume Agent ↺
        </button>
      </template>
    </div>
  </div>
</template>

<script setup>
import { ref, computed } from 'vue'

const props = defineProps({
  task: { type: Object, required: true },
  availableAgents: { type: Array, default: () => [] },
  agentRecord: { type: Object, default: null },
})

defineEmits(['assign', 'mark-done', 'delete', 'resume'])

const promptOpen = ref(false)
const agentSessionId = computed(() => props.agentRecord?.sessionId ?? null)

function onAssign(agentId) {
  if (agentId) emit('assign', { taskId: props.task.id, agentId })
}
</script>
```

Note: fix `emit` reference — use `defineEmits` return value:

**Final correct version:**

```vue
<template>
  <div class="bg-gray-800 rounded-lg p-3 flex flex-col gap-2 border border-gray-700 hover:border-gray-600 transition-colors">
    <div class="flex items-start justify-between gap-2">
      <span class="text-sm font-medium text-white leading-tight">{{ task.title }}</span>
      <button @click="emit('delete', task.id)"
        class="text-gray-600 hover:text-red-400 text-xs flex-shrink-0 transition-colors">✕</button>
    </div>

    <p v-if="task.description" class="text-xs text-gray-400 leading-relaxed line-clamp-2">{{ task.description }}</p>

    <div v-if="task.prompt">
      <button @click="promptOpen = !promptOpen" class="text-xs text-blue-400 hover:text-blue-300 flex items-center gap-1">
        <span>{{ promptOpen ? '▾' : '▸' }}</span><span>Prompt</span>
      </button>
      <pre v-if="promptOpen" class="mt-1 text-xs text-gray-300 bg-gray-900 rounded p-2 whitespace-pre-wrap overflow-auto max-h-24">{{ task.prompt }}</pre>
    </div>

    <div v-if="task.agentName" class="flex items-center gap-1 text-xs">
      <span class="text-gray-500">🤖</span>
      <span class="text-gray-300">{{ task.agentName }}</span>
    </div>

    <div class="flex gap-2 mt-1">
      <template v-if="task.status === 'todo'">
        <select v-if="availableAgents.length"
          @change="onAssign($event.target.value); $event.target.value = ''"
          class="flex-1 text-xs bg-gray-700 text-white rounded px-2 py-1 border border-gray-600 hover:border-gray-500 cursor-pointer">
          <option value="" disabled selected>Assign to agent…</option>
          <option v-for="a in availableAgents" :key="a.id" :value="a.id">{{ a.name }} ({{ a.status }})</option>
        </select>
        <span v-else class="text-xs text-gray-600 italic">No agents running</span>
      </template>

      <template v-if="task.status === 'in-progress'">
        <button @click="emit('mark-done', task.id)"
          class="flex-1 text-xs bg-green-700 hover:bg-green-600 text-white rounded px-2 py-1 transition-colors">
          Mark Done ✓
        </button>
      </template>

      <template v-if="task.status === 'done' && agentSessionId">
        <button @click="emit('resume', { agentName: task.agentName, sessionId: agentSessionId })"
          class="flex-1 text-xs bg-blue-700 hover:bg-blue-600 text-white rounded px-2 py-1 transition-colors">
          Resume Agent ↺
        </button>
      </template>
    </div>
  </div>
</template>

<script setup>
import { ref, computed } from 'vue'

const props = defineProps({
  task: { type: Object, required: true },
  availableAgents: { type: Array, default: () => [] },
  agentRecord: { type: Object, default: null },
})

const emit = defineEmits(['assign', 'mark-done', 'delete', 'resume'])

const promptOpen = ref(false)
const agentSessionId = computed(() => props.agentRecord?.sessionId ?? null)

function onAssign(agentId) {
  if (agentId) emit('assign', { taskId: props.task.id, agentId })
}
</script>
```

**Step 2: Commit**

```bash
git add src/components/TaskCard.vue
git commit -m "feat: add TaskCard component with assign/done/resume actions"
```

---

## Task 13: Frontend — NewTaskModal Component

**Files:**
- Create: `claude-orchestrator-web/frontend/src/components/NewTaskModal.vue`

**Step 1: Create src/components/NewTaskModal.vue**

```vue
<template>
  <Teleport to="body">
    <div v-if="show" class="fixed inset-0 z-50 flex items-center justify-center">
      <!-- Backdrop -->
      <div class="absolute inset-0 bg-black/60" @click="$emit('close')" />

      <!-- Modal -->
      <div class="relative bg-gray-800 rounded-xl border border-gray-700 w-full max-w-md mx-4 p-6 flex flex-col gap-4">
        <h2 class="text-base font-semibold text-white">New Task</h2>

        <div class="flex flex-col gap-3">
          <!-- Title -->
          <div class="flex flex-col gap-1">
            <label class="text-xs text-gray-400">Title *</label>
            <input
              v-model="form.title"
              @keydown.enter="submit"
              placeholder="Task title"
              class="bg-gray-900 border border-gray-600 rounded px-3 py-2 text-sm text-white placeholder-gray-600 focus:outline-none focus:border-blue-500"
              autofocus
            />
          </div>

          <!-- Description -->
          <div class="flex flex-col gap-1">
            <label class="text-xs text-gray-400">Description</label>
            <textarea
              v-model="form.description"
              rows="2"
              placeholder="Optional description"
              class="bg-gray-900 border border-gray-600 rounded px-3 py-2 text-sm text-white placeholder-gray-600 focus:outline-none focus:border-blue-500 resize-none"
            />
          </div>

          <!-- Prompt -->
          <div class="flex flex-col gap-1">
            <label class="text-xs text-gray-400">Prompt <span class="text-gray-600">(sent to agent on assign)</span></label>
            <textarea
              v-model="form.prompt"
              rows="4"
              placeholder="What should the agent do?"
              class="bg-gray-900 border border-gray-600 rounded px-3 py-2 text-sm text-white placeholder-gray-600 focus:outline-none focus:border-blue-500 resize-none font-mono"
            />
          </div>
        </div>

        <p v-if="error" class="text-xs text-red-400">{{ error }}</p>

        <div class="flex justify-end gap-2 pt-1">
          <button
            @click="$emit('close')"
            class="px-4 py-2 text-sm text-gray-400 hover:text-white transition-colors"
          >Cancel</button>
          <button
            @click="submit"
            :disabled="!form.title.trim()"
            class="px-4 py-2 text-sm bg-blue-600 hover:bg-blue-500 disabled:bg-gray-700 disabled:text-gray-500 text-white rounded transition-colors"
          >Create Task</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<script setup>
import { ref, watch } from 'vue'

const props = defineProps({
  show: { type: Boolean, default: false },
})

const emit = defineEmits(['close', 'created'])

const form = ref({ title: '', description: '', prompt: '' })
const error = ref(null)

// Reset form when modal opens
watch(() => props.show, (val) => {
  if (val) {
    form.value = { title: '', description: '', prompt: '' }
    error.value = null
  }
})

function submit() {
  if (!form.value.title.trim()) {
    error.value = 'Title is required'
    return
  }
  emit('created', {
    title: form.value.title.trim(),
    description: form.value.description.trim() || null,
    prompt: form.value.prompt.trim() || null,
  })
}
</script>
```

**Step 2: Commit**

```bash
git add src/components/NewTaskModal.vue
git commit -m "feat: add NewTaskModal component"
```

---

## Task 14: Frontend — TasksView (Kanban Board)

**Files:**
- Create: `claude-orchestrator-web/frontend/src/views/TasksView.vue`

**Step 1: Create src/views/TasksView.vue**

```vue
<template>
  <div class="flex flex-col flex-1 overflow-hidden bg-gray-950">
    <!-- Toolbar -->
    <div class="flex items-center justify-between px-4 py-2 border-b border-gray-800 flex-shrink-0">
      <span class="text-xs text-gray-500">{{ store.tasks.length }} tasks</span>
      <button
        @click="showNewTask = true"
        class="text-xs bg-blue-600 hover:bg-blue-500 text-white px-3 py-1 rounded transition-colors"
      >
        + New Task
      </button>
    </div>

    <!-- Toast error -->
    <div v-if="toastError" class="mx-4 mt-2 flex-shrink-0 bg-red-900/60 border border-red-700 text-red-300 text-xs rounded px-3 py-2">
      {{ toastError }}
    </div>

    <!-- Kanban columns -->
    <div class="flex flex-1 overflow-hidden gap-0">
      <!-- TODO -->
      <KanbanColumn
        title="Todo"
        :count="store.todoTasks.length"
        color="text-gray-400"
      >
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
        />
        <button
          @click="showNewTask = true"
          class="text-xs text-gray-600 hover:text-gray-400 mt-1 py-1 w-full text-left transition-colors"
        >
          + Add task
        </button>
      </KanbanColumn>

      <!-- IN PROGRESS -->
      <KanbanColumn
        title="In Progress"
        :count="store.inProgressTasks.length"
        color="text-yellow-400"
      >
        <TaskCard
          v-for="task in store.inProgressTasks"
          :key="task.id"
          :task="task"
          :available-agents="availableAgents"
          :agent-record="findAgentRecord(task.agentId)"
          @assign="handleAssign"
          @mark-done="handleMarkDone"
          @delete="handleDelete"
          @resume="handleResume"
        />
      </KanbanColumn>

      <!-- DONE -->
      <KanbanColumn
        title="Done"
        :count="store.doneTasks.length"
        color="text-green-400"
      >
        <TaskCard
          v-for="task in store.doneTasks"
          :key="task.id"
          :task="task"
          :available-agents="availableAgents"
          :agent-record="findAgentRecord(task.agentId)"
          @assign="handleAssign"
          @mark-done="handleMarkDone"
          @delete="handleDelete"
          @resume="handleResume"
        />
      </KanbanColumn>
    </div>

    <!-- New Task Modal -->
    <NewTaskModal
      :show="showNewTask"
      @close="showNewTask = false"
      @created="handleCreateTask"
    />
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useTasksStore } from '../stores/tasks'
import { useAgentsStore } from '../stores/agents'
import TaskCard from '../components/TaskCard.vue'
import NewTaskModal from '../components/NewTaskModal.vue'

const store = useTasksStore()
const agentsStore = useAgentsStore()

const showNewTask = ref(false)
const toastError = ref(null)
const agentHistory = ref([])

// Only Idle and Running agents can receive tasks
const availableAgents = computed(() =>
  agentsStore.agentList.filter(a => a.status === 'Idle' || a.status === 'Running')
)

function findAgentRecord(agentId) {
  if (!agentId) return null
  return agentHistory.value.find(r => r.id === agentId) ?? null
}

function showError(msg) {
  toastError.value = msg
  setTimeout(() => { toastError.value = null }, 4000)
}

async function loadHistory() {
  try {
    const res = await fetch('/api/history')
    agentHistory.value = await res.json()
  } catch { /* ignore */ }
}

async function handleCreateTask({ title, description, prompt }) {
  try {
    await store.createTask(title, description, prompt)
    showNewTask.value = false
  } catch {
    showError('Failed to create task')
  }
}

async function handleAssign({ taskId, agentId }) {
  try {
    await store.assignTask(taskId, agentId)
  } catch (e) {
    showError(e.message ?? 'Failed to assign task')
  }
}

async function handleMarkDone(taskId) {
  try {
    await store.markDone(taskId)
  } catch {
    showError('Failed to update task')
  }
}

async function handleDelete(taskId) {
  try {
    await store.deleteTask(taskId)
  } catch {
    showError('Failed to delete task')
  }
}

async function handleResume({ agentName, sessionId }) {
  try {
    await agentsStore.spawnAgent(agentName + '-resumed', null, sessionId)
    // Navigate back to agents view
    window.location.href = '/'
  } catch (e) {
    showError(e.message ?? 'Failed to resume agent')
  }
}

onMounted(async () => {
  await store.loadTasks()
  await loadHistory()
})
</script>
```

**Step 2: Create inline KanbanColumn component**

The `KanbanColumn` used above is a simple wrapper. Add it as a local component directly in `TasksView.vue` using a `defineComponent` in the same file, OR create a separate file. Create separate file for cleanliness:

**File: `src/components/KanbanColumn.vue`**

```vue
<template>
  <div class="flex-1 flex flex-col overflow-hidden border-r border-gray-800 last:border-r-0">
    <!-- Column header -->
    <div class="flex items-center gap-2 px-3 py-2 border-b border-gray-800 flex-shrink-0">
      <span class="text-xs font-semibold uppercase tracking-wide" :class="color">{{ title }}</span>
      <span class="text-xs text-gray-600 bg-gray-800 rounded-full px-2">{{ count }}</span>
    </div>
    <!-- Scrollable card list -->
    <div class="flex-1 overflow-y-auto p-2 flex flex-col gap-2">
      <slot />
    </div>
  </div>
</template>

<script setup>
defineProps({
  title: String,
  count: Number,
  color: { type: String, default: 'text-gray-400' },
})
</script>
```

Also update the `TasksView.vue` import to include `KanbanColumn`:

```js
import KanbanColumn from '../components/KanbanColumn.vue'
```

**Step 3: Update agents store spawnAgent to support resumeSessionId**

In `stores/agents.js`, update `spawnAgent`:

```js
async function spawnAgent(name, cwd, resumeSessionId = null) {
  const res = await fetch('/api/agents', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, cwd: cwd || null, resumeSessionId: resumeSessionId || null }),
  })
  if (!res.ok) {
    const err = await res.json()
    throw new Error(err.error ?? 'Failed to spawn')
  }
  return res.json()
}
```

**Step 4: Full end-to-end test**

Start the backend and open the app:

```bash
cd claude-orchestrator-web/backend
dotnet run
```

Open `http://localhost:5050`:
1. Click **Tasks** tab → see kanban board with 3 empty columns
2. Click **+ New Task** → fill in title + prompt → **Create Task** → task appears in Todo
3. Go to **Agents** tab → create an agent
4. Go back to **Tasks** → use the "Assign to agent" dropdown on the Todo task → task moves to In Progress
5. Click **Mark Done ✓** → task moves to Done column

**Step 5: Commit**

```bash
cd claude-orchestrator-web/frontend
git add src/views/TasksView.vue src/components/KanbanColumn.vue src/stores/agents.js
git commit -m "feat: add TasksView kanban board with KanbanColumn, wire resume support"
```

---

## Task 15: Final Integration Verification

**Step 1: Build production bundle**

```bash
cd claude-orchestrator-web/backend
dotnet publish -c Release -r win-x64 --self-contained
```
Expected: Build succeeded, no errors.

**Step 2: Verify session ID capture**

1. Start backend, go to Agents tab, create agent
2. In the terminal, type `/exit` and press Enter
3. Claude Code will print resume instructions containing `--resume <id>`
4. Wait for agent to show Done status
5. Go to `http://localhost:5050/api/history` in browser
6. Verify the agent record appears with a non-null `sessionId`

**Step 3: Verify resume flow**

1. Create a task and assign it to a completed agent (the one with sessionId from step above)
2. Mark the task as Done manually: `PUT /api/tasks/<id>` with `status: "done"`
   (Or let the agent finish and the task will be in Done column with the agent's history linked)
3. In the Done column, "Resume Agent ↺" button should appear
4. Click it → new terminal session opens connected to the previous conversation

**Step 4: Final commit**

```bash
git add .
git commit -m "feat: complete task dashboard + session persistence implementation"
```

---

## Summary of Files Changed/Created

### Backend (new)
- `Models/TaskItem.cs`
- `Models/AgentRecord.cs`
- `Services/TaskService.cs`
- `Services/AgentHistoryService.cs`
- `Controllers/TasksController.cs`
- `Controllers/HistoryController.cs`

### Backend (modified)
- `Models/AgentEvent.cs` — add CreateTaskRequest, UpdateTaskRequest, AssignTaskRequest; extend SpawnRequest
- `Models/Agent.cs` — add ResumeSessionId property
- `Services/PtySession.cs` — session ID regex detection, OnExited callback, --resume support
- `Services/AgentManager.cs` — inject AgentHistoryService, history persistence on kill/exit, resume passthrough
- `Controllers/AgentsController.cs` — pass ResumeSessionId in spawn call
- `Program.cs` — register TaskService, AgentHistoryService, inject into AgentManager

### Frontend (new)
- `src/router/index.js`
- `src/views/AgentsView.vue`
- `src/views/TasksView.vue`
- `src/stores/tasks.js`
- `src/components/TaskCard.vue`
- `src/components/NewTaskModal.vue`
- `src/components/KanbanColumn.vue`

### Frontend (modified)
- `src/main.js` — add vue-router
- `src/App.vue` — convert to navigation shell with RouterView
- `src/stores/agents.js` — add resumeSessionId to spawnAgent
