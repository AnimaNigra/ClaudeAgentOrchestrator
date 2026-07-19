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
        draggable="true"
        :class="{ 'opacity-40': dragAgentId === agent.id }"
        @dragstart="onDragStart(agent)"
        @dragend="dragAgentId = null"
        @dragover.prevent
        @drop="onDrop(agent)"
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
      <div v-else class="flex h-full w-full overflow-hidden">
        <!-- Terminal (shrinks when the conversation drawer is open; its own
             ResizeObserver refits xterm automatically) -->
        <div class="relative flex-1 min-w-0 overflow-hidden">
          <TerminalPanel />
          <VoiceDictateButton
            v-if="store.activeAgentId"
            @open="showVoiceDialog = true"
          />
          <!-- Read-back conversation toggle (top-left — clear of search/mic overlays) -->
          <button
            v-if="store.activeAgentId"
            @click="toggleConversation"
            :title="showConversation ? 'Skrýt přepis konverzace' : 'Zobrazit přepis konverzace (scrollovatelný, i na touchpadu)'"
            aria-label="Přepnout přepis konverzace"
            class="absolute top-2 left-2 z-20 w-9 h-9 flex items-center justify-center rounded-md border shadow-lg transition-colors"
            :class="showConversation
              ? 'bg-blue-600 border-blue-400 text-white hover:bg-blue-700'
              : 'bg-gray-800/85 border-gray-600 text-gray-300 hover:bg-gray-700 hover:border-blue-500'"
          >
            <svg xmlns="http://www.w3.org/2000/svg" class="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6"/><path d="M8 13h8M8 17h6"/></svg>
          </button>
        </div>

        <!-- Resize handle between terminal and drawer -->
        <div
          v-if="showConversation && store.activeAgentId"
          class="w-1 flex-shrink-0 cursor-col-resize hover:bg-blue-500/50 active:bg-blue-500/70 transition-colors"
          @mousedown="startConvResize"
        />

        <!-- Conversation read-back drawer -->
        <div
          v-if="showConversation && store.activeAgentId"
          class="flex-shrink-0 h-full overflow-hidden"
          :style="{ width: convWidth + 'px' }"
        >
          <ConversationDrawer
            :agent-id="store.activeAgentId"
            :agent-name="activeAgentName"
            @close="closeConversation"
          />
        </div>
      </div>
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
import { ref, computed, onMounted } from 'vue'
import { useAgentsStore } from '../stores/agents'
import AgentCard from '../components/AgentCard.vue'
import TerminalPanel from '../components/TerminalPanel.vue'
import ReviewPanel from '../components/ReviewPanel.vue'
import CommandBar from '../components/CommandBar.vue'
import PermissionDialog from '../components/PermissionDialog.vue'
import VoiceDictateButton from '../components/VoiceDictateButton.vue'
import VoiceDictateDialog from '../components/VoiceDictateDialog.vue'
import PromptDialog from '../components/PromptDialog.vue'
import ConversationDrawer from '../components/ConversationDrawer.vue'

const store = useAgentsStore()
const cmdBar = ref(null)
const showVoiceDialog = ref(false)
const reviewAgentId = ref(null)
const sidebarWidth = ref(Number(localStorage.getItem('sidebarWidth')) || 256)

// Conversation read-back drawer (renders the active agent's history.md; see ConversationDrawer.vue)
const showConversation = ref(localStorage.getItem('showConversation') === '1')
const convWidth = ref(Number(localStorage.getItem('conversationWidth')) || 460)
const activeAgentName = computed(
  () => store.agentList.find(a => a.id === store.activeAgentId)?.name || ''
)

function toggleConversation() {
  showConversation.value = !showConversation.value
  localStorage.setItem('showConversation', showConversation.value ? '1' : '0')
}

function closeConversation() {
  showConversation.value = false
  localStorage.setItem('showConversation', '0')
}

function startConvResize(e) {
  const startX = e.clientX
  const startWidth = convWidth.value
  const onMove = (ev) => {
    // Drawer sits on the right, so dragging left widens it.
    convWidth.value = Math.max(280, Math.min(900, startWidth - (ev.clientX - startX)))
  }
  const onUp = () => {
    document.removeEventListener('mousemove', onMove)
    document.removeEventListener('mouseup', onUp)
    document.body.style.cursor = ''
    document.body.style.userSelect = ''
    localStorage.setItem('conversationWidth', String(convWidth.value))
  }
  document.body.style.cursor = 'col-resize'
  document.body.style.userSelect = 'none'
  document.addEventListener('mousemove', onMove)
  document.addEventListener('mouseup', onUp)
}

// Drag-and-drop reordering of the agent sidebar (session-only; mirrors PrioritiesView)
const dragAgentId = ref(null)
let dragAgent = null

function onDragStart(agent) {
  dragAgent = agent
  dragAgentId.value = agent.id
}

function onDrop(targetAgent) {
  dragAgentId.value = null
  if (!dragAgent || dragAgent.id === targetAgent.id) return
  const ids = store.agentList.map(a => a.id)
  const oldIndex = ids.indexOf(dragAgent.id)
  const targetIndex = ids.indexOf(targetAgent.id)
  if (oldIndex < 0 || targetIndex < 0) return
  // Insert-based reorder: remove dragged id, re-insert at the target position.
  const [moved] = ids.splice(oldIndex, 1)
  ids.splice(targetIndex, 0, moved)
  store.reorderAgents(ids)
  dragAgent = null
}

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
    localStorage.setItem('sidebarWidth', sidebarWidth.value)
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
