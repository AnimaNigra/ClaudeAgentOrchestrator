<template>
  <div class="flex flex-1 overflow-hidden">
    <!-- Left: agent cards -->
    <aside class="flex-shrink-0 bg-gray-950 border-r border-gray-800 overflow-y-auto p-2 flex flex-col gap-2"
           :style="{ width: sidebarWidth + 'px' }">
      <AgentCard
        v-for="agent in store.agentList"
        :key="agent.id"
        :agent="agent"
        :is-active="store.activeAgentId === agent.id"
        @select="store.activeAgentId = $event"
        @review="toggleReview($event)"
        @create-worktree="handleCreateWorktree($event)"
      />
      <div v-if="!store.agentList.length" class="text-xs text-gray-600 p-2">
        No agents. Type <code class="text-blue-400">create &lt;name&gt;</code> below.
      </div>
    </aside>

    <!-- Sidebar resize handle -->
    <div
      class="w-1 flex-shrink-0 cursor-col-resize hover:bg-blue-500/50 active:bg-blue-500/70 transition-colors"
      @mousedown="startSidebarResize"
    />

    <!-- Right: terminal or review panel -->
    <main class="relative flex-1 overflow-hidden bg-black">
      <ReviewPanel
        v-if="reviewAgentId"
        :agent-id="reviewAgentId"
        @close="reviewAgentId = null"
      />
      <template v-else>
        <TerminalPanel />
        <VoiceDictateButton
          v-if="store.activeAgentId"
          @open="showVoiceDialog = true"
        />
      </template>
    </main>
  </div>

  <!-- Command bar -->
  <CommandBar ref="cmdBar" />

  <!-- Permission dialog (rendered via Teleport to body) -->
  <PermissionDialog />
  <VoiceDictateDialog
    v-if="store.activeAgentId"
    :show="showVoiceDialog"
    :agent-id="store.activeAgentId"
    @close="showVoiceDialog = false"
  />

  <!-- Worktree name prompt -->
  <PromptDialog
    :show="!!worktreeAgent"
    title="Create Worktree"
    :message="`New worktree for ${worktreeAgent?.name}`"
    type="prompt"
    placeholder="Worktree name"
    :default-value="worktreeAgent ? worktreeAgent.name + '-wt' : ''"
    confirm-text="Create"
    :error-msg="worktreeError"
    @close="worktreeAgent = null; worktreeError = ''"
    @confirm="doCreateWorktree"
  />
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { useAgentsStore } from '../stores/agents'
import AgentCard from '../components/AgentCard.vue'
import TerminalPanel from '../components/TerminalPanel.vue'
import ReviewPanel from '../components/ReviewPanel.vue'
import CommandBar from '../components/CommandBar.vue'
import PermissionDialog from '../components/PermissionDialog.vue'
import VoiceDictateButton from '../components/VoiceDictateButton.vue'
import VoiceDictateDialog from '../components/VoiceDictateDialog.vue'
import PromptDialog from '../components/PromptDialog.vue'

const store = useAgentsStore()
const cmdBar = ref(null)
const showVoiceDialog = ref(false)
const reviewAgentId = ref(null)
const sidebarWidth = ref(256)

function toggleReview(agentId) {
  reviewAgentId.value = reviewAgentId.value === agentId ? null : agentId
  store.activeAgentId = agentId
}

const worktreeAgent = ref(null)
const worktreeError = ref('')

function handleCreateWorktree(agent) {
  if (!agent.cwd) return
  worktreeAgent.value = agent
  worktreeError.value = ''
}

async function doCreateWorktree(name) {
  try {
    const newAgent = await store.createWorktree(name, worktreeAgent.value.cwd)
    worktreeAgent.value = null
    worktreeError.value = ''
    store.activeAgentId = newAgent.id
  } catch (e) {
    worktreeError.value = e.message
  }
}

function startSidebarResize(e) {
  const startX = e.clientX
  const startWidth = sidebarWidth.value
  const onMove = (ev) => {
    sidebarWidth.value = Math.max(150, Math.min(600, startWidth + ev.clientX - startX))
  }
  const onUp = () => {
    document.removeEventListener('mousemove', onMove)
    document.removeEventListener('mouseup', onUp)
    document.body.style.cursor = ''
    document.body.style.userSelect = ''
  }
  document.body.style.cursor = 'col-resize'
  document.body.style.userSelect = 'none'
  document.addEventListener('mousemove', onMove)
  document.addEventListener('mouseup', onUp)
}

onMounted(() => {
  cmdBar.value?.focus()
})
</script>
