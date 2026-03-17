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
    <main class="relative flex-1 overflow-hidden bg-black">
      <TerminalPanel />
      <VoiceDictateButton
        v-if="store.activeAgentId"
        @open="showVoiceDialog = true"
      />
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
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { useAgentsStore } from '../stores/agents'
import AgentCard from '../components/AgentCard.vue'
import TerminalPanel from '../components/TerminalPanel.vue'
import CommandBar from '../components/CommandBar.vue'
import PermissionDialog from '../components/PermissionDialog.vue'
import VoiceDictateButton from '../components/VoiceDictateButton.vue'
import VoiceDictateDialog from '../components/VoiceDictateDialog.vue'

const store = useAgentsStore()
const cmdBar = ref(null)
const showVoiceDialog = ref(false)

onMounted(() => {
  cmdBar.value?.focus()
})
</script>
