# Git Worktree Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable multiple Claude agents to work on the same git project simultaneously by isolating each in its own git worktree with a dedicated branch.

**Architecture:** A `WorktreeService` manages git worktree lifecycle (create/list/remove) via `git` CLI calls. The `Agent` model gains worktree fields (`WorktreePath`, `WorktreeBranch`, `OriginalCwd`). The frontend adds a worktree button to AgentCard and a Worktrees management tab showing all worktrees grouped by parent project. Worktrees are created as sibling directories: `<project>-wt-<name>/`.

**Tech Stack:** ASP.NET Core 9, Vue 3, git CLI, SignalR

---

## File Structure

**Create:**
- `claude-orchestrator-web/backend/Services/WorktreeService.cs` — git worktree add/list/remove operations
- `claude-orchestrator-web/backend/Controllers/WorktreeController.cs` — REST API for worktree operations
- `claude-orchestrator-web/frontend/src/views/WorktreesView.vue` — worktree manager tab

**Modify:**
- `claude-orchestrator-web/backend/Models/Agent.cs` — add worktree fields
- `claude-orchestrator-web/backend/Models/AgentRecord.cs` — persist worktree fields in history
- `claude-orchestrator-web/backend/Models/AgentEvent.cs` — add `CreateWorktreeRequest`
- `claude-orchestrator-web/backend/Services/AgentManager.cs` — pass worktree info through spawn
- `claude-orchestrator-web/frontend/src/components/AgentCard.vue` — worktree button + indicator
- `claude-orchestrator-web/frontend/src/stores/agents.js` — `createWorktree` action
- `claude-orchestrator-web/frontend/src/router/index.js` — add `/worktrees` route
- `claude-orchestrator-web/frontend/src/App.vue` — add Worktrees nav tab

---

### Task 1: Add worktree fields to Agent and AgentRecord models

**Files:**
- Modify: `claude-orchestrator-web/backend/Models/Agent.cs:13-25`
- Modify: `claude-orchestrator-web/backend/Models/AgentRecord.cs:4-13`

- [ ] **Step 1: Add worktree properties to Agent model**

In `Agent.cs`, add three nullable properties after line 24 (`ResumeSessionId`):

```csharp
public string? WorktreePath { get; set; }
public string? WorktreeBranch { get; set; }
public string? OriginalCwd { get; set; }
```

- [ ] **Step 2: Add worktree properties to AgentRecord model**

In `AgentRecord.cs`, add matching properties after line 11 (`Notes`):

```csharp
public string? WorktreePath { get; set; }
public string? WorktreeBranch { get; set; }
public string? OriginalCwd { get; set; }
```

- [ ] **Step 3: Build to verify**

Run: `cd claude-orchestrator-web/backend && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add claude-orchestrator-web/backend/Models/Agent.cs claude-orchestrator-web/backend/Models/AgentRecord.cs
git commit -m "feat: add worktree fields to Agent and AgentRecord models"
```

---

### Task 2: Create WorktreeService

**Files:**
- Create: `claude-orchestrator-web/backend/Services/WorktreeService.cs`

This service wraps git CLI calls. All methods receive a `repoPath` (the main repo CWD) and operate on it.

- [ ] **Step 1: Create WorktreeService**

Create `claude-orchestrator-web/backend/Services/WorktreeService.cs`:

```csharp
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ClaudeOrchestrator.Services;

public class WorktreeService
{
    public record WorktreeInfo(string Path, string Branch, bool IsMain);

    /// <summary>
    /// Create a new git worktree as a sibling directory to the repo.
    /// Path: <repoDir>-wt-<name>/
    /// Branch: wt/<name>
    /// </summary>
    public async Task<(string worktreePath, string branch)> CreateAsync(string repoPath, string name)
    {
        // Resolve to git toplevel so we always work from the repo root
        var topLevel = (await RunGitAsync(repoPath, "rev-parse --show-toplevel")).Trim();
        if (string.IsNullOrEmpty(topLevel))
            throw new InvalidOperationException($"Not a git repository: {repoPath}");

        var safeName = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9_-]", "-");
        var branch = $"wt/{safeName}";
        var worktreePath = Path.GetFullPath(Path.Combine(topLevel, "..", Path.GetFileName(topLevel) + $"-wt-{safeName}"));

        // Check if worktree path already exists
        if (Directory.Exists(worktreePath))
            throw new InvalidOperationException($"Worktree directory already exists: {worktreePath}");

        // Create worktree with new branch from current HEAD
        await RunGitAsync(topLevel, $"worktree add \"{worktreePath}\" -b \"{branch}\"");

        return (worktreePath, branch);
    }

    /// <summary>List all worktrees for the given repo.</summary>
    public async Task<List<WorktreeInfo>> ListAsync(string repoPath)
    {
        var topLevel = (await RunGitAsync(repoPath, "rev-parse --show-toplevel")).Trim();
        if (string.IsNullOrEmpty(topLevel))
            return new List<WorktreeInfo>();

        var output = await RunGitAsync(topLevel, "worktree list --porcelain");
        var worktrees = new List<WorktreeInfo>();
        string? currentPath = null;
        string? currentBranch = null;

        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("worktree "))
            {
                // Save previous entry
                if (currentPath is not null)
                    worktrees.Add(new WorktreeInfo(currentPath, currentBranch ?? "detached", currentPath == topLevel));
                currentPath = line[9..];
                currentBranch = null;
            }
            else if (line.StartsWith("branch "))
            {
                // "branch refs/heads/wt/fix-auth" → "wt/fix-auth"
                var refName = line[7..];
                currentBranch = refName.StartsWith("refs/heads/") ? refName[11..] : refName;
            }
        }
        // Final entry
        if (currentPath is not null)
            worktrees.Add(new WorktreeInfo(currentPath, currentBranch ?? "detached", currentPath == topLevel));

        return worktrees;
    }

    /// <summary>Remove a worktree and optionally delete its branch.</summary>
    public async Task RemoveAsync(string repoPath, string worktreePath, bool deleteBranch = true)
    {
        var topLevel = (await RunGitAsync(repoPath, "rev-parse --show-toplevel")).Trim();

        // Force-remove the worktree (handles dirty state)
        await RunGitAsync(topLevel, $"worktree remove \"{worktreePath}\" --force");

        // Optionally delete the branch
        if (deleteBranch)
        {
            // Find which branch was used
            var branchName = Path.GetFileName(worktreePath);
            // Extract from directory name: <project>-wt-<name> → wt/<name>
            var match = Regex.Match(branchName, @"-wt-(.+)$");
            if (match.Success)
            {
                var branch = $"wt/{match.Groups[1].Value}";
                try { await RunGitAsync(topLevel, $"branch -D \"{branch}\""); }
                catch { /* branch may not exist or may have been merged */ }
            }
        }
    }

    private static async Task<string> RunGitAsync(string workDir, string args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {args} failed: {error.Trim()}");

        return output;
    }
}
```

- [ ] **Step 2: Register WorktreeService in DI**

In `claude-orchestrator-web/backend/Program.cs`, add after the existing `AddSingleton<GitReviewService>()` line:

```csharp
builder.Services.AddSingleton<WorktreeService>();
```

- [ ] **Step 3: Build to verify**

Run: `cd claude-orchestrator-web/backend && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add claude-orchestrator-web/backend/Services/WorktreeService.cs claude-orchestrator-web/backend/Program.cs
git commit -m "feat: add WorktreeService for git worktree lifecycle management"
```

---

### Task 3: Create WorktreeController with REST API

**Files:**
- Create: `claude-orchestrator-web/backend/Controllers/WorktreeController.cs`
- Modify: `claude-orchestrator-web/backend/Models/AgentEvent.cs` — add request record

- [ ] **Step 1: Add CreateWorktreeRequest to AgentEvent.cs**

Add at the end of `AgentEvent.cs`:

```csharp
public record CreateWorktreeRequest(string Name, string Cwd);
```

- [ ] **Step 2: Create WorktreeController**

Create `claude-orchestrator-web/backend/Controllers/WorktreeController.cs`:

```csharp
using ClaudeOrchestrator.Models;
using ClaudeOrchestrator.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudeOrchestrator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WorktreeController : ControllerBase
{
    private readonly WorktreeService _worktree;
    private readonly AgentManager _manager;

    public WorktreeController(WorktreeService worktree, AgentManager manager)
    {
        _worktree = worktree;
        _manager = manager;
    }

    /// <summary>List all worktrees for a given repository path.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string cwd)
    {
        if (string.IsNullOrEmpty(cwd))
            return BadRequest(new { error = "cwd is required" });
        try
        {
            var worktrees = await _worktree.ListAsync(cwd);
            return Ok(worktrees);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Create a worktree and spawn a new agent in it.
    /// POST /api/worktree { name: "fix-auth", cwd: "C:\...\Source" }
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWorktreeRequest req)
    {
        try
        {
            var (worktreePath, branch) = await _worktree.CreateAsync(req.Cwd, req.Name);
            var agent = await _manager.SpawnAgentAsync(req.Name, worktreePath);
            agent.WorktreePath = worktreePath;
            agent.WorktreeBranch = branch;
            agent.OriginalCwd = req.Cwd;
            return Ok(agent);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Remove a worktree (and optionally its branch).
    /// DELETE /api/worktree?cwd=...&worktreePath=...
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> Remove(
        [FromQuery] string cwd,
        [FromQuery] string worktreePath,
        [FromQuery] bool deleteBranch = true)
    {
        try
        {
            await _worktree.RemoveAsync(cwd, worktreePath, deleteBranch);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
```

- [ ] **Step 3: Build to verify**

Run: `cd claude-orchestrator-web/backend && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add claude-orchestrator-web/backend/Controllers/WorktreeController.cs claude-orchestrator-web/backend/Models/AgentEvent.cs
git commit -m "feat: add WorktreeController REST API for create/list/remove"
```

---

### Task 4: Persist worktree fields through AgentManager and history

**Files:**
- Modify: `claude-orchestrator-web/backend/Services/AgentManager.cs:93-129`
- Modify: `claude-orchestrator-web/backend/Services/AgentHistoryService.cs` (SaveAgentAsync)

- [ ] **Step 1: Copy worktree fields in AgentHistoryService.SaveAgentAsync**

In `AgentHistoryService.cs`, inside the `SaveAgentAsync` method where the `AgentRecord` is constructed (around line 34-43), add the worktree fields:

```csharp
var record = new AgentRecord
{
    Id = agent.Id,
    Name = agent.Name,
    Cwd = agent.Cwd,
    SessionId = string.IsNullOrEmpty(agent.SessionId) ? null : agent.SessionId,
    CreatedAt = agent.CreatedAt,
    FinishedAt = agent.FinishedAt,
    TaskIds = taskIds ?? new List<string>(),
    WorktreePath = agent.WorktreePath,
    WorktreeBranch = agent.WorktreeBranch,
    OriginalCwd = agent.OriginalCwd,
};
```

- [ ] **Step 2: Build to verify**

Run: `cd claude-orchestrator-web/backend && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add claude-orchestrator-web/backend/Services/AgentHistoryService.cs
git commit -m "feat: persist worktree fields in agent history records"
```

---

### Task 5: Add worktree button and indicator to AgentCard

**Files:**
- Modify: `claude-orchestrator-web/frontend/src/components/AgentCard.vue`
- Modify: `claude-orchestrator-web/frontend/src/stores/agents.js`

- [ ] **Step 1: Add createWorktree action to agents store**

In `claude-orchestrator-web/frontend/src/stores/agents.js`, add a new function before the `return` block (around line 190):

```javascript
async function createWorktree(name, cwd) {
  const res = await fetch('/api/worktree', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, cwd }),
  })
  if (!res.ok) {
    const err = await res.json()
    throw new Error(err.error ?? 'Failed to create worktree')
  }
  return res.json()
}
```

Then add `createWorktree` to the return object:

```javascript
return {
  agents, activeAgentId, connected, pendingPermissions, alwaysAllowedTools,
  agentList,
  connect, spawnAgent, createWorktree, sendKeystroke, resizePty, killAgent,
  registerPtyHandler, addAlwaysAllowed,
}
```

- [ ] **Step 2: Add worktree button and badge to AgentCard**

In `claude-orchestrator-web/frontend/src/components/AgentCard.vue`, replace the entire template and script with:

Template — add a worktree badge after the name and a worktree button in the actions row. The full updated template:

```vue
<template>
  <div
    class="agent-card"
    :class="[statusClass, { active: isActive }]"
    @click="$emit('select', agent.id)"
  >
    <div class="flex items-center justify-between mb-1">
      <div class="flex items-center gap-1.5 min-w-0">
        <span class="font-bold text-sm truncate">{{ agent.name }}</span>
        <span v-if="agent.worktreeBranch" class="text-[10px] text-emerald-400 bg-emerald-400/10 px-1.5 py-0.5 rounded flex-shrink-0" title="Running in worktree">🌿 {{ agent.worktreeBranch }}</span>
      </div>
      <span v-if="agent.status === 'Running'" class="spinner" title="Running"></span>
      <span v-else class="status-dot" :title="agent.status">{{ statusIcon }}</span>
    </div>

    <div class="text-xs text-gray-400 truncate mb-1 flex items-center gap-1" :title="agent.cwd ?? 'default'">
      <button
        @click.stop="openFolder"
        class="hover:text-blue-400 transition-colors flex-shrink-0"
        title="Open folder in explorer"
      >📁</button>
      <span class="truncate">{{ agent.cwd ?? 'default' }}</span>
    </div>

    <div class="text-xs truncate text-gray-300 min-h-[1rem]">
      {{ agent.lastMessage || '…' }}
    </div>

    <div class="flex items-center justify-between mt-2">
      <span class="text-xs text-gray-500">{{ agent.elapsedStr }}</span>
      <div class="flex items-center gap-2">
        <button
          v-if="!agent.worktreeBranch"
          class="text-[10px] text-gray-500 hover:text-emerald-400 px-1.5 py-0.5 rounded hover:bg-gray-700/50 transition-colors"
          @click.stop="$emit('create-worktree', agent)"
          title="Create a worktree clone of this agent's repo"
        >🌿 worktree</button>
        <button
          class="text-[10px] text-gray-500 hover:text-blue-400 px-1.5 py-0.5 rounded hover:bg-gray-700/50 transition-colors"
          @click.stop="$emit('review', agent.id)"
          title="Review git changes"
        >review</button>
        <span class="text-xs" :class="statusTextClass">{{ agent.status }}</span>
      </div>
    </div>

    <div v-if="agent.progressPct >= 0" class="mt-1">
      <div class="w-full bg-gray-700 rounded h-1">
        <div class="h-1 rounded bg-blue-500 transition-all" :style="{ width: agent.progressPct + '%' }"></div>
      </div>
    </div>
  </div>
</template>
```

Script — add the `create-worktree` emit:

```vue
<script setup>
import { computed } from 'vue'

const props = defineProps({ agent: Object, isActive: Boolean })
defineEmits(['select', 'review', 'create-worktree'])

const STATUS_ICONS = {
  Running: '🟢', Idle: '🔵', Done: '✅', Error: '🔴', Blocked: '🟡'
}
const STATUS_TEXT = {
  Running: 'text-green-400', Idle: 'text-blue-400',
  Done: 'text-white', Error: 'text-red-400', Blocked: 'text-yellow-400'
}
const STATUS_BORDER = {
  Running: 'border-green-600', Idle: 'border-blue-700',
  Done: 'border-gray-600', Error: 'border-red-600', Blocked: 'border-yellow-600'
}

async function openFolder() {
  await fetch(`/api/agents/${props.agent.id}/open-folder`, { method: 'POST' })
}

const statusIcon = computed(() => STATUS_ICONS[props.agent.status] ?? '⚪')
const statusTextClass = computed(() => STATUS_TEXT[props.agent.status] ?? 'text-gray-400')
const statusClass = computed(() => ({
  [STATUS_BORDER[props.agent.status] ?? 'border-gray-600']: true,
  'border-2': props.isActive,
  'border': !props.isActive,
}))
</script>
```

Style stays the same (no changes to `<style scoped>` block).

- [ ] **Step 3: Handle the create-worktree event in AgentsView**

In `claude-orchestrator-web/frontend/src/views/AgentsView.vue`, add the event handler to `AgentCard`:

Replace the `<AgentCard>` usage (around line 6-13):

```vue
<AgentCard
  v-for="agent in store.agentList"
  :key="agent.id"
  :agent="agent"
  :is-active="store.activeAgentId === agent.id"
  @select="store.activeAgentId = $event"
  @review="toggleReview($event)"
  @create-worktree="handleCreateWorktree($event)"
/>
```

Add the handler function in `<script setup>` after the existing functions:

```javascript
async function handleCreateWorktree(agent) {
  if (!agent.cwd) return
  const name = prompt(`Worktree name for ${agent.name}:`, `${agent.name}-wt`)
  if (!name) return
  try {
    const newAgent = await store.createWorktree(name, agent.cwd)
    store.activeAgentId = newAgent.id
  } catch (e) {
    alert(e.message)
  }
}
```

- [ ] **Step 4: Build frontend to verify**

Run: `cd claude-orchestrator-web/frontend && npx vite build`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add claude-orchestrator-web/frontend/src/components/AgentCard.vue claude-orchestrator-web/frontend/src/stores/agents.js claude-orchestrator-web/frontend/src/views/AgentsView.vue
git commit -m "feat: add worktree button to AgentCard and createWorktree store action"
```

---

### Task 6: Create WorktreesView management tab

**Files:**
- Create: `claude-orchestrator-web/frontend/src/views/WorktreesView.vue`
- Modify: `claude-orchestrator-web/frontend/src/router/index.js`
- Modify: `claude-orchestrator-web/frontend/src/App.vue`

- [ ] **Step 1: Create WorktreesView component**

Create `claude-orchestrator-web/frontend/src/views/WorktreesView.vue`:

```vue
<template>
  <div class="flex flex-col h-full overflow-hidden bg-gray-950">
    <div class="flex items-center justify-between px-4 py-3 border-b border-gray-800 flex-shrink-0">
      <h2 class="text-sm font-semibold text-gray-200">Git Worktrees</h2>
      <div class="flex items-center gap-3">
        <input
          v-model="cwdInput"
          placeholder="Repository path to scan..."
          class="text-xs bg-gray-800 border border-gray-700 rounded px-2 py-1 text-gray-300 w-64 focus:outline-none focus:border-blue-500"
          @keydown.enter="loadWorktrees"
        />
        <button
          @click="loadWorktrees"
          class="text-xs px-3 py-1 bg-blue-600 hover:bg-blue-500 text-white rounded transition-colors"
        >Scan</button>
      </div>
    </div>

    <div v-if="loading" class="flex items-center justify-center h-full text-gray-500 text-sm">
      Loading...
    </div>

    <div v-else-if="!groups.length && !error" class="flex items-center justify-center h-full text-gray-600 text-sm select-none">
      Enter a repository path above and click Scan to see worktrees.
    </div>

    <div v-else-if="error" class="flex items-center justify-center h-full text-red-400 text-sm">
      {{ error }}
    </div>

    <div v-else class="flex-1 overflow-y-auto px-4 py-3 space-y-4">
      <div v-for="group in groups" :key="group.mainPath" class="space-y-2">
        <!-- Main repo header -->
        <div class="text-xs font-semibold text-gray-400 uppercase tracking-wide">
          {{ group.mainPath }}
          <span class="text-gray-600 normal-case ml-2">{{ group.branch }}</span>
        </div>

        <!-- Worktree entries -->
        <div
          v-for="wt in group.worktrees"
          :key="wt.path"
          class="bg-gray-900 border border-gray-800 rounded-lg p-3 flex items-center justify-between gap-3"
        >
          <div class="min-w-0 flex-1">
            <div class="flex items-center gap-2">
              <span class="text-emerald-400 text-sm">🌿</span>
              <span class="text-sm font-medium text-gray-200">{{ wt.branch }}</span>
              <span v-if="runningAgentForPath(wt.path)" class="text-[10px] text-green-400 bg-green-400/10 px-1.5 py-0.5 rounded">
                agent: {{ runningAgentForPath(wt.path).name }}
              </span>
            </div>
            <div class="text-xs text-gray-500 truncate mt-0.5">{{ wt.path }}</div>
          </div>
          <div class="flex items-center gap-2 flex-shrink-0">
            <button
              v-if="!runningAgentForPath(wt.path)"
              @click="spawnInWorktree(wt)"
              class="text-xs px-2.5 py-1 bg-blue-600 hover:bg-blue-500 text-white rounded transition-colors"
              title="Spawn a new agent in this worktree"
            >Spawn Agent</button>
            <button
              @click="openFolder(wt.path)"
              class="text-xs px-2 py-1 text-gray-500 hover:text-blue-400 hover:bg-gray-800 rounded transition-colors"
              title="Open in explorer"
            >📁</button>
            <button
              v-if="!runningAgentForPath(wt.path)"
              @click="removeWorktree(wt)"
              class="text-xs px-2 py-1 text-gray-500 hover:text-red-400 hover:bg-gray-800 rounded transition-colors"
              title="Remove worktree and delete branch"
            >Delete</button>
          </div>
        </div>

        <div v-if="!group.worktrees.length" class="text-xs text-gray-600 pl-2">
          No worktrees. Use the 🌿 button on an agent card to create one.
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useAgentsStore } from '../stores/agents'

const router = useRouter()
const store = useAgentsStore()

const cwdInput = ref('')
const allWorktrees = ref([])
const loading = ref(false)
const error = ref('')

// Group worktrees: main repo + its child worktrees
const groups = computed(() => {
  const main = allWorktrees.value.find(w => w.isMain)
  if (!main) return []

  return [{
    mainPath: main.path,
    branch: main.branch,
    worktrees: allWorktrees.value.filter(w => !w.isMain),
  }]
})

function runningAgentForPath(path) {
  return store.agentList.find(a => a.cwd === path)
}

async function loadWorktrees() {
  if (!cwdInput.value.trim()) return
  loading.value = true
  error.value = ''
  try {
    const res = await fetch(`/api/worktree?cwd=${encodeURIComponent(cwdInput.value.trim())}`)
    if (!res.ok) {
      const err = await res.json()
      throw new Error(err.error ?? 'Failed to list worktrees')
    }
    allWorktrees.value = await res.json()
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

async function spawnInWorktree(wt) {
  const name = prompt('Agent name:', wt.branch.replace('wt/', ''))
  if (!name) return
  try {
    const agent = await store.spawnAgent(name, wt.path)
    store.activeAgentId = agent.id
    router.push('/')
  } catch (e) {
    alert(e.message)
  }
}

async function removeWorktree(wt) {
  if (!confirm(`Remove worktree and delete branch "${wt.branch}"?\n${wt.path}`)) return
  const mainCwd = allWorktrees.value.find(w => w.isMain)?.path
  if (!mainCwd) return
  try {
    await fetch(`/api/worktree?cwd=${encodeURIComponent(mainCwd)}&worktreePath=${encodeURIComponent(wt.path)}&deleteBranch=true`, {
      method: 'DELETE',
    })
    await loadWorktrees()
  } catch (e) {
    alert(e.message)
  }
}

async function openFolder(path) {
  // Use the same pattern as agent open-folder but direct
  try {
    await fetch('/api/worktree/open-folder', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ path }),
    })
  } catch { /* ignore */ }
}
</script>
```

- [ ] **Step 2: Add open-folder endpoint to WorktreeController**

Add to the end of `WorktreeController.cs` (before the closing brace):

```csharp
/// <summary>Open a worktree directory in the file explorer.</summary>
[HttpPost("open-folder")]
public IActionResult OpenFolder([FromBody] OpenFolderRequest req)
{
    if (!Directory.Exists(req.Path))
        return BadRequest(new { error = "Directory does not exist" });
    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
        FileName = req.Path,
        UseShellExecute = true,
    });
    return Ok();
}
```

Add the request record to `AgentEvent.cs`:

```csharp
public record OpenFolderRequest(string Path);
```

- [ ] **Step 3: Add Worktrees route**

In `claude-orchestrator-web/frontend/src/router/index.js`, add the import and route:

```javascript
import WorktreesView from '../views/WorktreesView.vue'
```

Add to the routes array:

```javascript
{ path: '/worktrees', component: WorktreesView },
```

- [ ] **Step 4: Add Worktrees nav tab to App.vue**

In `claude-orchestrator-web/frontend/src/App.vue`, add a new `RouterLink` after the History tab (around line 35):

```vue
<RouterLink
  to="/worktrees"
  class="px-3 py-1 text-xs rounded transition-colors"
  :class="$route.path === '/worktrees' ? 'bg-blue-600 text-white' : 'text-gray-400 hover:text-white hover:bg-gray-700'"
>
  Worktrees
</RouterLink>
```

- [ ] **Step 5: Build both frontend and backend**

Run: `cd claude-orchestrator-web/backend && dotnet build`
Run: `cd claude-orchestrator-web/frontend && npx vite build`
Expected: Both succeed.

- [ ] **Step 6: Commit**

```bash
git add claude-orchestrator-web/frontend/src/views/WorktreesView.vue claude-orchestrator-web/frontend/src/router/index.js claude-orchestrator-web/frontend/src/App.vue claude-orchestrator-web/backend/Controllers/WorktreeController.cs claude-orchestrator-web/backend/Models/AgentEvent.cs
git commit -m "feat: add Worktrees management tab with list/spawn/delete"
```

---

### Task 7: Auto-populate WorktreesView with CWDs from running agents

**Files:**
- Modify: `claude-orchestrator-web/frontend/src/views/WorktreesView.vue`

Currently the user must type a repo path manually. This step adds auto-detection from running agents so the view loads pre-populated.

- [ ] **Step 1: Auto-detect CWDs from running agents**

In `WorktreesView.vue`, add an `onMounted` hook that collects unique CWDs from running agents and loads worktrees for each. Replace the `<script setup>` section — add after the existing imports:

```javascript
import { ref, computed, onMounted } from 'vue'
```

Add at the end of the script (before `</script>`):

```javascript
// Auto-load worktrees for all unique agent CWDs
onMounted(async () => {
  const cwds = new Set()
  for (const agent of store.agentList) {
    // Use originalCwd if it's a worktree agent, otherwise use cwd
    const repoCwd = agent.originalCwd || agent.cwd
    if (repoCwd) cwds.add(repoCwd)
  }
  if (cwds.size === 1) {
    cwdInput.value = [...cwds][0]
    await loadWorktrees()
  }
})
```

- [ ] **Step 2: Build to verify**

Run: `cd claude-orchestrator-web/frontend && npx vite build`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add claude-orchestrator-web/frontend/src/views/WorktreesView.vue
git commit -m "feat: auto-populate WorktreesView from running agent CWDs"
```

---

## Self-Review

**1. Spec coverage:**
- Button on AgentCard to create worktree: Task 5
- WorktreeService backend: Task 2
- Sibling directory paths: Task 2 (CreateAsync generates `<project>-wt-<name>`)
- Worktree manager UI: Task 6
- Delete button: Task 6 (RemoveWorktree)
- Merge left to agent: Not automated (by design per requirements)
- Agent model fields: Task 1
- Persist worktree info in history: Task 4
- Grouping by parent project: Task 6 (groups computed property)

**2. Placeholder scan:** No TBDs, TODOs, or "implement later" found. All code is complete.

**3. Type consistency:**
- `WorktreePath`, `WorktreeBranch`, `OriginalCwd` consistent across Agent.cs, AgentRecord.cs, AgentHistoryService.cs
- `CreateWorktreeRequest(Name, Cwd)` consistent between controller and frontend
- `WorktreeInfo(Path, Branch, IsMain)` matches frontend property access (`wt.path`, `wt.branch`, `wt.isMain`)
- `createWorktree` store action matches `handleCreateWorktree` in AgentsView
- Frontend uses camelCase property names which matches the ASP.NET default JSON serialization (`JsonNamingPolicy.CamelCase` for agents is handled by the default System.Text.Json settings)
