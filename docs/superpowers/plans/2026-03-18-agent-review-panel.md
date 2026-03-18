# Agent Review Panel — Git Diff Viewer

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show which files each agent changed via `git diff` in a review panel within the Agents view.

**Architecture:** New REST endpoint runs `git status`/`git diff` in the agent's CWD. A new `ReviewPanel.vue` component renders the file list and diffs. The panel replaces the terminal area when toggled via a button on the AgentCard.

**Tech Stack:** ASP.NET Core (Process to run git), Vue 3 Composition API, TailwindCSS

---

### Task 1: Backend — Git Review Endpoint

**Files:**
- Create: `backend/Services/GitReviewService.cs`
- Modify: `backend/Controllers/AgentsController.cs`
- Modify: `backend/Program.cs` (register service)

- [ ] **Step 1: Create GitReviewService**

Create `backend/Services/GitReviewService.cs`:

```csharp
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace ClaudeOrchestrator.Services;

public record GitFileChange(string Path, string Status);
public record GitReviewResult(
    List<GitFileChange> Files,
    string Diff,
    string Branch,
    bool IsGitRepo);

public class GitReviewService
{
    public async Task<GitReviewResult> GetReviewAsync(string cwd)
    {
        if (string.IsNullOrEmpty(cwd) || !Directory.Exists(cwd))
            return new([], "", "", false);

        var isGit = await RunGitAsync(cwd, "rev-parse --is-inside-work-tree");
        if (isGit.Trim() != "true")
            return new([], "", "", false);

        var branch = (await RunGitAsync(cwd, "branch --show-current")).Trim();
        var status = await RunGitAsync(cwd, "status --porcelain");
        var diff = await RunGitAsync(cwd, "diff");
        var diffCached = await RunGitAsync(cwd, "diff --cached");

        var fullDiff = string.IsNullOrEmpty(diffCached)
            ? diff
            : diff + "\n" + diffCached;

        var files = status
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var s = line[..2].Trim();
                var p = line[3..];
                var label = s switch
                {
                    "M" => "modified",
                    "A" => "added",
                    "D" => "deleted",
                    "??" => "untracked",
                    "R" => "renamed",
                    _ => s,
                };
                return new GitFileChange(p, label);
            })
            .ToList();

        return new(files, fullDiff, branch, true);
    }

    private static async Task<string> RunGitAsync(string cwd, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi)!;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return output;
        }
        catch { return ""; }
    }
}
```

- [ ] **Step 2: Register service in Program.cs**

Add after `builder.Services.AddSingleton<PriorityService>();`:

```csharp
builder.Services.AddSingleton<GitReviewService>();
```

- [ ] **Step 3: Add endpoint to AgentsController**

Add new endpoint after the existing `Get(id)` method:

```csharp
[HttpGet("{id}/review")]
public async Task<IActionResult> Review(string id, [FromServices] GitReviewService git)
{
    var agent = _manager.GetAgent(id);
    if (agent is null) return NotFound();
    var result = await git.GetReviewAsync(agent.Cwd);
    return Ok(result);
}
```

- [ ] **Step 4: Verify backend builds**

Run: `cd claude-orchestrator-web/backend && dotnet build`
Expected: Build succeeded.

---

### Task 2: Frontend — ReviewPanel Component

**Files:**
- Create: `frontend/src/components/ReviewPanel.vue`

- [ ] **Step 1: Create ReviewPanel.vue**

The panel shows a file list on the left and a unified diff on the right. It fetches data from `GET /api/agents/{id}/review`.

```vue
<template>
  <div class="flex flex-col h-full bg-gray-950 text-gray-200 overflow-hidden">
    <!-- Header -->
    <div class="flex items-center justify-between px-4 py-2 border-b border-gray-800 bg-gray-900">
      <div class="flex items-center gap-3">
        <span class="text-sm font-semibold">Review</span>
        <span v-if="data?.branch" class="text-xs bg-blue-900/50 text-blue-300 px-2 py-0.5 rounded">
          {{ data.branch }}
        </span>
        <span v-if="data?.files.length" class="text-xs text-gray-500">
          {{ data.files.length }} file{{ data.files.length === 1 ? '' : 's' }} changed
        </span>
      </div>
      <div class="flex items-center gap-2">
        <button @click="load" class="text-xs text-gray-400 hover:text-white px-2 py-1 rounded hover:bg-gray-800"
                :disabled="loading">
          {{ loading ? 'Loading…' : '↻ Refresh' }}
        </button>
        <button @click="$emit('close')" class="text-xs text-gray-400 hover:text-white px-2 py-1 rounded hover:bg-gray-800">
          ✕ Close
        </button>
      </div>
    </div>

    <!-- Not a git repo -->
    <div v-if="data && !data.isGitRepo" class="flex items-center justify-center h-full text-gray-600 text-sm">
      Not a git repository
    </div>

    <!-- No changes -->
    <div v-else-if="data && !data.files.length" class="flex items-center justify-center h-full text-gray-600 text-sm">
      No changes detected
    </div>

    <!-- Content -->
    <div v-else-if="data" class="flex flex-1 overflow-hidden">
      <!-- File list -->
      <div class="w-64 flex-shrink-0 border-r border-gray-800 overflow-y-auto">
        <button
          v-for="f in data.files" :key="f.path"
          class="w-full text-left px-3 py-1.5 text-xs hover:bg-gray-800 flex items-center gap-2 border-b border-gray-800/50"
          :class="{ 'bg-gray-800': selectedFile === f.path }"
          @click="selectedFile = f.path"
        >
          <span :class="statusColor(f.status)" class="font-mono text-[10px] uppercase w-10">{{ f.status }}</span>
          <span class="truncate" :title="f.path">{{ f.path }}</span>
        </button>
      </div>

      <!-- Diff view -->
      <div class="flex-1 overflow-auto">
        <pre class="p-4 text-xs font-mono leading-5 whitespace-pre-wrap"><template
          v-for="(line, i) in diffLines" :key="i"
        ><span :class="lineClass(line)">{{ line }}
</span></template></pre>
      </div>
    </div>

    <!-- Loading -->
    <div v-else class="flex items-center justify-center h-full text-gray-600 text-sm">
      Loading…
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, computed, watch } from 'vue'

const props = defineProps({ agentId: String })
defineEmits(['close'])

const data = ref(null)
const loading = ref(false)
const selectedFile = ref(null)

async function load() {
  loading.value = true
  try {
    const res = await fetch(`/api/agents/${props.agentId}/review`)
    if (res.ok) data.value = await res.json()
  } finally { loading.value = false }
}

const diffLines = computed(() => {
  if (!data.value?.diff) return []
  const lines = data.value.diff.split('\n')
  if (!selectedFile.value) return lines
  // Filter diff to selected file
  const chunks = []
  let inFile = false
  for (const line of lines) {
    if (line.startsWith('diff --git')) {
      inFile = line.includes(selectedFile.value)
    }
    if (inFile) chunks.push(line)
  }
  return chunks.length ? chunks : lines
})

function lineClass(line) {
  if (line.startsWith('+') && !line.startsWith('+++')) return 'text-green-400 bg-green-900/20'
  if (line.startsWith('-') && !line.startsWith('---')) return 'text-red-400 bg-red-900/20'
  if (line.startsWith('@@')) return 'text-blue-400'
  if (line.startsWith('diff --git')) return 'text-yellow-400 font-bold'
  return 'text-gray-400'
}

function statusColor(status) {
  return {
    modified: 'text-yellow-400',
    added: 'text-green-400',
    deleted: 'text-red-400',
    untracked: 'text-gray-500',
    renamed: 'text-blue-400',
  }[status] ?? 'text-gray-400'
}

watch(() => props.agentId, () => { data.value = null; selectedFile.value = null; load() })
onMounted(load)
</script>
```

---

### Task 3: Integration — Toggle Review in AgentsView

**Files:**
- Modify: `frontend/src/views/AgentsView.vue`
- Modify: `frontend/src/components/AgentCard.vue`

- [ ] **Step 1: Add review toggle to AgentCard**

Add a "Review" button to AgentCard that emits a `review` event.

- [ ] **Step 2: Update AgentsView**

Add `showReview` ref. When true, show `ReviewPanel` instead of `TerminalPanel` in the main area. Wire the toggle from AgentCard.

- [ ] **Step 3: Verify end-to-end**

Start server, open browser, create agent, make changes, click Review button. Verify file list and diffs display correctly.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: add git review panel to see file changes per agent"
```
