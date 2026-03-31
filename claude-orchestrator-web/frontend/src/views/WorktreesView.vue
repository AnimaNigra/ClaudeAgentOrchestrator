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
        <div class="text-xs font-semibold text-gray-400 uppercase tracking-wide">
          {{ group.mainPath }}
          <span class="text-gray-600 normal-case ml-2">{{ group.branch }}</span>
        </div>

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

  <!-- Spawn agent prompt -->
  <PromptDialog
    :show="!!spawnWt"
    title="Spawn Agent in Worktree"
    :message="spawnWt ? `Branch: ${spawnWt.branch}` : ''"
    type="prompt"
    placeholder="Agent name"
    :default-value="spawnWt ? spawnWt.branch.replace('wt/', '') : ''"
    confirm-text="Spawn"
    :error-msg="spawnError"
    @close="spawnWt = null; spawnError = ''"
    @confirm="doSpawn"
  />

  <!-- Remove worktree confirm -->
  <PromptDialog
    :show="!!removeWt"
    title="Remove Worktree"
    :message="removeWt ? `Delete worktree and branch &quot;${removeWt.branch}&quot;?\n${removeWt.path}` : ''"
    type="confirm"
    confirm-text="Delete"
    :danger="true"
    :error-msg="removeError"
    @close="removeWt = null; removeError = ''"
    @confirm="doRemove"
  />
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAgentsStore } from '../stores/agents'
import PromptDialog from '../components/PromptDialog.vue'

const router = useRouter()
const store = useAgentsStore()

const cwdInput = ref('')
const allWorktrees = ref([])
const loading = ref(false)
const error = ref('')

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

const spawnWt = ref(null)
const spawnError = ref('')
const removeWt = ref(null)
const removeError = ref('')

function spawnInWorktree(wt) {
  spawnWt.value = wt
  spawnError.value = ''
}

async function doSpawn(name) {
  try {
    const agent = await store.spawnAgent(name, spawnWt.value.path)
    spawnWt.value = null
    spawnError.value = ''
    store.activeAgentId = agent.id
    router.push('/')
  } catch (e) {
    spawnError.value = e.message
  }
}

function removeWorktree(wt) {
  removeWt.value = wt
  removeError.value = ''
}

async function doRemove() {
  const mainCwd = allWorktrees.value.find(w => w.isMain)?.path
  if (!mainCwd) return
  try {
    await fetch(`/api/worktree?cwd=${encodeURIComponent(mainCwd)}&worktreePath=${encodeURIComponent(removeWt.value.path)}&deleteBranch=true`, {
      method: 'DELETE',
    })
    removeWt.value = null
    removeError.value = ''
    await loadWorktrees()
  } catch (e) {
    removeError.value = e.message
  }
}

async function openFolder(path) {
  try {
    await fetch('/api/worktree/open-folder', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ path }),
    })
  } catch { /* ignore */ }
}

// Task 7: Auto-load worktrees for all unique agent CWDs
onMounted(async () => {
  const cwds = new Set()
  for (const agent of store.agentList) {
    const repoCwd = agent.originalCwd || agent.cwd
    if (repoCwd) cwds.add(repoCwd)
  }
  if (cwds.size === 1) {
    cwdInput.value = [...cwds][0]
    await loadWorktrees()
  }
})
</script>
