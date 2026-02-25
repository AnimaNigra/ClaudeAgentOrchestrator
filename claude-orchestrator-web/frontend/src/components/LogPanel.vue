<template>
  <div class="flex flex-col h-full overflow-hidden">
    <!-- Tab bar: All / per-agent -->
    <div class="flex gap-1 px-2 pt-2 flex-shrink-0 overflow-x-auto">
      <button
        class="tab-btn"
        :class="{ active: store.activeAgentId === null }"
        @click="store.activeAgentId = null"
      >All</button>
      <button
        v-for="agent in store.agentList"
        :key="agent.id"
        class="tab-btn"
        :class="{ active: store.activeAgentId === agent.id }"
        @click="store.activeAgentId = agent.id"
      >{{ agent.name }}</button>
    </div>

    <!-- Log entries -->
    <div ref="logEl" class="flex-1 overflow-y-auto px-3 py-2 text-xs font-mono space-y-0.5">
      <div
        v-for="(entry, i) in store.activeLogs"
        :key="i"
        class="log-line"
        :class="levelClass(entry.level)"
      >
        <span class="text-gray-600 mr-2">{{ entry.time }}</span>
        <span class="mr-2" :style="{ color: agentColor(entry.agentId) }">{{ entry.agentName }}</span>
        <span class="whitespace-pre-wrap break-words">{{ entry.text }}</span>
      </div>
    </div>

    <!-- Message input for active agent -->
    <div v-if="store.activeAgentId" class="flex-shrink-0 border-t border-gray-700 p-2 flex gap-2">
      <input
        v-model="messageInput"
        class="flex-1 bg-gray-800 text-white text-xs px-3 py-1.5 rounded border border-gray-600 focus:border-blue-500 outline-none"
        placeholder="Send message to agent…"
        @keydown.enter="sendMessage"
        @keydown.up.prevent="historyUp"
        @keydown.down.prevent="historyDown"
      />
      <button
        class="px-3 py-1.5 bg-blue-600 hover:bg-blue-500 text-xs rounded"
        @click="sendMessage"
      >Send</button>
    </div>
  </div>
</template>

<script setup>
import { ref, watch, nextTick } from 'vue'
import { useAgentsStore } from '../stores/agents'

const store = useAgentsStore()
const logEl = ref(null)
const messageInput = ref('')
const msgHistory = ref([])
const histIdx = ref(-1)

const COLORS = ['#67e8f9','#c084fc','#fbbf24','#4ade80','#60a5fa','#f87171']
const colorMap = {}
let colorIdx = 0

function agentColor(agentId) {
  if (!colorMap[agentId]) colorMap[agentId] = COLORS[colorIdx++ % COLORS.length]
  return colorMap[agentId]
}

function levelClass(level) {
  return {
    'text-red-400': level === 'error',
    'text-green-400': level === 'done',
    'text-yellow-400': level === 'warn',
    'text-gray-300': level === 'output',
    'text-gray-500': level === 'info',
  }
}

async function sendMessage() {
  const msg = messageInput.value.trim()
  if (!msg || !store.activeAgentId) return
  msgHistory.value.push(msg)
  histIdx.value = -1
  messageInput.value = ''
  try {
    await store.sendMessage(store.activeAgentId, msg)
  } catch (e) {
    store.addSystemLog(e.message, 'error')
  }
}

function historyUp() {
  if (!msgHistory.value.length) return
  if (histIdx.value === -1) histIdx.value = msgHistory.value.length - 1
  else if (histIdx.value > 0) histIdx.value--
  messageInput.value = msgHistory.value[histIdx.value]
}

function historyDown() {
  if (histIdx.value === -1) return
  histIdx.value++
  if (histIdx.value >= msgHistory.value.length) { histIdx.value = -1; messageInput.value = '' }
  else messageInput.value = msgHistory.value[histIdx.value]
}

// Auto-scroll
watch(() => store.activeLogs.length, async () => {
  await nextTick()
  if (logEl.value) logEl.value.scrollTop = logEl.value.scrollHeight
})
</script>

<style scoped>
.tab-btn {
  padding: 2px 10px;
  border-radius: 4px 4px 0 0;
  font-size: 11px;
  background: #1a1f2e;
  color: #94a3b8;
  border: 1px solid #2d3748;
  white-space: nowrap;
  cursor: pointer;
}
.tab-btn.active {
  background: #1e2a3a;
  color: #e2e8f0;
  border-bottom-color: #1e2a3a;
}
.log-line { line-height: 1.5; }
</style>
