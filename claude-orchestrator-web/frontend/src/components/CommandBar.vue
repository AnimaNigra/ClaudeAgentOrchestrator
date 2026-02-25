<template>
  <div class="flex items-center gap-2 px-3 py-2 bg-gray-900 border-t border-gray-700">
    <span class="text-blue-400 text-sm font-bold flex-shrink-0">❯</span>
    <input
      ref="inputEl"
      v-model="input"
      class="flex-1 bg-transparent text-white text-sm outline-none"
      :placeholder="placeholder"
      @keydown.enter="submit"
      @keydown.up.prevent="historyUp"
      @keydown.down.prevent="historyDown"
    />
    <span v-if="statusMsg" class="text-xs" :class="statusClass">{{ statusMsg }}</span>
    <span
      class="text-xs"
      :class="store.connected ? 'text-green-500' : 'text-red-500'"
    >{{ store.connected ? '● connected' : '● disconnected' }}</span>
  </div>
</template>

<script setup>
import { ref, computed, onUnmounted } from 'vue'
import { useAgentsStore } from '../stores/agents'

const store = useAgentsStore()
const input = ref('')
const history = ref([])
const histIdx = ref(-1)
const inputEl = ref(null)
const statusMsg = ref('')
const statusIsError = ref(false)
const statusClass = computed(() => statusIsError.value ? 'text-red-400' : 'text-gray-400')

let statusTimer = null
function showStatus(msg, isError = false) {
  statusMsg.value = msg
  statusIsError.value = isError
  clearTimeout(statusTimer)
  statusTimer = setTimeout(() => { statusMsg.value = '' }, 3000)
}

const placeholder = computed(() => {
  if (store.activeAgentId) {
    const name = store.agents[store.activeAgentId]?.name
    return `create <name> [<cwd>]  |  kill <name>  |  select <name>  —  active: ${name}`
  }
  return 'create <name> [<cwd>]  |  kill <name>  |  select <name>'
})

async function submit() {
  const raw = input.value.trim()
  if (!raw) return
  if (!history.value.length || history.value.at(-1) !== raw) {
    history.value.push(raw)
    if (history.value.length > 50) history.value.shift()
  }
  histIdx.value = -1
  input.value = ''
  await handleCommand(raw)
}

async function handleCommand(raw) {
  const parts = raw.split(/\s+/)
  const cmd = parts[0].toLowerCase()

  if (cmd === 'create') {
    const name = parts[1]
    const cwd = parts.slice(2).join(' ') || null
    if (!name) { showStatus('Usage: create <name> [<cwd>]', true); return }
    try {
      await store.spawnAgent(name, cwd)
      showStatus(`Created: ${name}`)
    } catch (e) {
      showStatus(e.message, true)
    }

  } else if (cmd === 'kill') {
    const target = parts[1]
    if (!target) { showStatus('Usage: kill <name|id>', true); return }
    const agent = store.agentList.find(a => a.name === target || a.id === target)
    if (!agent) { showStatus(`Not found: ${target}`, true); return }
    await store.killAgent(agent.id)
    showStatus(`Killed: ${agent.name}`)

  } else if (cmd === 'select') {
    const target = parts[1]
    const agent = store.agentList.find(a => a.name === target || a.id === target)
    if (!agent) { showStatus(`Not found: ${target}`, true); return }
    store.activeAgentId = agent.id

  } else if (cmd === 'list') {
    const info = store.agentList.map(a => `${a.name}(${a.status})`).join('  ') || 'No agents'
    showStatus(info)

  } else if (cmd === 'help') {
    showStatus('create <name> [<cwd>]  |  kill <name>  |  select <name>  |  list')

  } else {
    showStatus(`Unknown: ${cmd}`, true)
  }
}

function historyUp() {
  if (!history.value.length) return
  if (histIdx.value === -1) histIdx.value = history.value.length - 1
  else if (histIdx.value > 0) histIdx.value--
  input.value = history.value[histIdx.value]
}

function historyDown() {
  if (histIdx.value === -1) return
  histIdx.value++
  if (histIdx.value >= history.value.length) { histIdx.value = -1; input.value = '' }
  else input.value = history.value[histIdx.value]
}

onUnmounted(() => clearTimeout(statusTimer))
defineExpose({ focus: () => inputEl.value?.focus() })
</script>
