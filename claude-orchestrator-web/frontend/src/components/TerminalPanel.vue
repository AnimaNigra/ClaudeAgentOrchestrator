<template>
  <div ref="containerRef" class="relative w-full h-full bg-black overflow-hidden">
    <!-- One div per agent; v-show preserves the xterm DOM while hiding it -->
    <div
      v-for="agent in agentList"
      :key="agent.id"
      v-show="agent.id === activeAgentId"
      :ref="el => mountTerminal(agent.id, el)"
      class="absolute inset-0"
    />
    <div v-if="!activeAgentId" class="flex items-center justify-center h-full text-gray-600 text-sm select-none">
      No agent selected — type <code class="mx-1 text-blue-400">create &lt;name&gt;</code> to create one
    </div>
  </div>
</template>

<script setup>
import { ref, watch, onMounted, onUnmounted, nextTick } from 'vue'
import { storeToRefs } from 'pinia'
import { Terminal } from '@xterm/xterm'
import { FitAddon } from '@xterm/addon-fit'
import '@xterm/xterm/css/xterm.css'
import { useAgentsStore } from '../stores/agents'

const store = useAgentsStore()
const { agentList, activeAgentId } = storeToRefs(store)

const containerRef = ref(null)

// Map of agentId → { terminal, fitAddon }
const terminals = {}

function mountTerminal(agentId, el) {
  if (!el || terminals[agentId]) return

  const fitAddon = new FitAddon()
  const terminal = new Terminal({
    theme: {
      background: '#0d1117',
      foreground: '#c9d1d9',
      cursor: '#58a6ff',
      cursorAccent: '#0d1117',
      black: '#484f58', red: '#ff7b72', green: '#3fb950', yellow: '#d29922',
      blue: '#58a6ff', magenta: '#bc8cff', cyan: '#39c5cf', white: '#b1bac4',
      brightBlack: '#6e7681', brightRed: '#ffa198', brightGreen: '#56d364',
      brightYellow: '#e3b341', brightBlue: '#79c0ff', brightMagenta: '#d2a8ff',
      brightCyan: '#56d4dd', brightWhite: '#f0f6fc',
    },
    fontFamily: '"Cascadia Code", "Cascadia Mono", Consolas, "Courier New", monospace',
    fontSize: 13,
    lineHeight: 1.2,
    scrollback: 10000,
    cursorBlink: true,
    allowProposedApi: true,
  })

  terminal.loadAddon(fitAddon)
  terminal.open(el)

  // Ctrl+C with selection → copy to clipboard (not SIGINT)
  // Ctrl+V / Ctrl+Shift+V → paste from clipboard
  terminal.attachCustomKeyEventHandler(e => {
    if (e.type !== 'keydown') return true
    if (e.ctrlKey && e.code === 'KeyC' && terminal.hasSelection()) {
      navigator.clipboard.writeText(terminal.getSelection()).catch(() => {})
      return false
    }
    if ((e.ctrlKey && e.code === 'KeyV') || (e.ctrlKey && e.shiftKey && e.code === 'KeyV')) {
      navigator.clipboard.readText().then(text => {
        if (text) store.sendKeystroke(agentId, text)
      }).catch(() => {})
      return false
    }
    return true
  })

  // Send typed input to backend → PTY
  terminal.onData(data => {
    store.sendKeystroke(agentId, data)
  })

  terminals[agentId] = { terminal, fitAddon }

  // Register as the PTY data receiver for this agent in the store
  store.registerPtyHandler(agentId, chunk => {
    try {
      const bytes = Uint8Array.from(atob(chunk), c => c.charCodeAt(0))
      terminal.write(bytes)
    } catch { /* ignore malformed chunks */ }
  })

  // If this is the active agent, fit immediately (element is visible)
  if (agentId === activeAgentId.value) {
    nextTick(() => {
      fitAddon.fit()
      notifyResize(agentId, fitAddon)
      terminal.focus()
    })
  }
}

// Resize active terminal when switching agents
watch(activeAgentId, newId => {
  if (!newId) return
  nextTick(() => {
    const t = terminals[newId]
    if (t) {
      t.fitAddon.fit()
      notifyResize(newId, t.fitAddon)
      t.terminal.focus()
    }
  })
})

function notifyResize(agentId, fitAddon) {
  const dims = fitAddon.proposeDimensions()
  if (dims?.cols && dims?.rows)
    store.resizePty(agentId, dims.cols, dims.rows)
}

// Resize terminal on container size change
let resizeObs = null
onMounted(() => {
  if (!containerRef.value) return
  resizeObs = new ResizeObserver(() => {
    const id = activeAgentId.value
    if (!id || !terminals[id]) return
    terminals[id].fitAddon.fit()
    notifyResize(id, terminals[id].fitAddon)
  })
  resizeObs.observe(containerRef.value)
})

onUnmounted(() => {
  resizeObs?.disconnect()
  Object.values(terminals).forEach(({ terminal }) => terminal.dispose())
})
</script>
