<template>
  <div class="flex flex-col h-screen">
    <!-- Header -->
    <header class="flex items-center justify-between px-4 py-2 bg-gray-900 border-b border-gray-700 flex-shrink-0">
      <h1 class="text-sm font-bold text-blue-400">⚡ Claude Agent Orchestrator</h1>
      <div class="flex gap-4 text-xs text-gray-400">
        <span>{{ store.agentList.length }} agents</span>
        <span>{{ runningCount }} running</span>
      </div>
    </header>

    <!-- Main area -->
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
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { useAgentsStore } from './stores/agents'
import AgentCard from './components/AgentCard.vue'
import TerminalPanel from './components/TerminalPanel.vue'
import CommandBar from './components/CommandBar.vue'
import PermissionDialog from './components/PermissionDialog.vue'

const store = useAgentsStore()
const cmdBar = ref(null)

const runningCount = computed(() =>
  store.agentList.filter(a => a.status === 'Running').length
)

onMounted(async () => {
  await store.connect()
  cmdBar.value?.focus()
  if ('Notification' in window && Notification.permission === 'default')
    Notification.requestPermission()
})
</script>
