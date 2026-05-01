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

    <!-- Speak selection button: always visible while an agent is active.
         Dimmed when there's no selection so the user knows where to click. -->
    <button
      v-if="activeAgentId"
      @click="toggleSpeak"
      :disabled="!hasSelection && !isSpeaking"
      :title="isSpeaking ? 'Stop reading' : (hasSelection ? 'Read selection aloud' : 'Select text in the terminal first (drag with mouse, hold Alt if it doesn\'t work), then click here')"
      :aria-label="isSpeaking ? 'Stop reading' : 'Read selection aloud'"
      class="absolute bottom-4 right-20 z-10 w-12 h-12 rounded-full border transition-colors flex items-center justify-center shadow-lg"
      :class="isSpeaking
        ? 'bg-blue-600 border-blue-400 hover:bg-blue-700'
        : (hasSelection
          ? 'bg-gray-800 border-gray-600 hover:bg-gray-700 hover:border-blue-500 cursor-pointer'
          : 'bg-gray-900 border-gray-700 opacity-40 cursor-not-allowed')"
    >
      <svg v-if="!isSpeaking" xmlns="http://www.w3.org/2000/svg" class="w-5 h-5 text-gray-300" viewBox="0 0 24 24" fill="currentColor">
        <path d="M3 9v6h4l5 5V4L7 9H3zm13.5 3A4.5 4.5 0 0 0 14 7.97v8.05c1.48-.73 2.5-2.25 2.5-4.02zM14 3.23v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z"/>
      </svg>
      <svg v-else xmlns="http://www.w3.org/2000/svg" class="w-5 h-5 text-white animate-pulse" viewBox="0 0 24 24" fill="currentColor">
        <path d="M6 6h12v12H6z"/>
      </svg>
    </button>
  </div>
</template>

<script setup>
import { ref, watch, onMounted, onUnmounted } from 'vue'
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

// Track last PTY dimensions sent per agent — only send resize when they actually change
// This prevents unnecessary PTY redraws (which falsely trigger the "Running" state) on agent switch
const lastSentDims = {}

// Speak-selection state
const hasSelection = ref(false)
const isSpeaking = ref(false)

function refreshSelectionState() {
  const id = activeAgentId.value
  const t = id ? terminals[id] : null
  hasSelection.value = !!t?.terminal.hasSelection()
}

function toggleSpeak() {
  if (isSpeaking.value) {
    window.speechSynthesis.cancel()
    isSpeaking.value = false
    return
  }
  const id = activeAgentId.value
  const t = id ? terminals[id] : null
  if (!t) return
  const text = t.terminal.getSelection().trim()
  if (!text) return

  const utter = new SpeechSynthesisUtterance(text)
  // Pick Czech voice when text contains Czech diacritics; otherwise default to English.
  utter.lang = /[áčďéěíňóřšťúůýžÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ]/.test(text) ? 'cs-CZ' : 'en-US'
  utter.onend = () => { isSpeaking.value = false }
  utter.onerror = () => { isSpeaking.value = false }
  // Cancel anything pending so the new utterance starts cleanly
  if (window.speechSynthesis.speaking || window.speechSynthesis.pending) {
    window.speechSynthesis.cancel()
  }
  isSpeaking.value = true
  window.speechSynthesis.speak(utter)
}

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

  // Ctrl+C with selection → copy. Ctrl+V → suppress xterm's key handling;
  // the actual paste is handled by the 'paste' event listener below.
  terminal.attachCustomKeyEventHandler(e => {
    if (e.type !== 'keydown') return true

    if (e.ctrlKey && e.code === 'KeyC' && terminal.hasSelection()) {
      navigator.clipboard.writeText(terminal.getSelection()).catch(() => {})
      return false
    }

    // Returning false prevents xterm from treating Ctrl+V as a raw key sequence.
    // The browser still fires a 'paste' event which our listener below handles.
    if (e.ctrlKey && e.code === 'KeyV') return false

    return true
  })

  // Inspect the clipboard synchronously. If it contains an image, we upload
  // it ourselves and stop xterm from also processing the event. For text-only
  // paste we deliberately fall through so xterm's native paste handler runs —
  // it wraps multi-line input in bracketed-paste sequences (\x1b[200~ ... \x1b[201~)
  // which prevents Claude Code from interpreting each embedded newline as Enter.
  terminal.textarea?.addEventListener('paste', e => {
    const items = e.clipboardData?.items
    if (!items) return

    let imageItem = null
    for (const item of items) {
      if (item.type.startsWith('image/')) { imageItem = item; break }
    }
    if (!imageItem) return  // text-only — let xterm handle it (bracketed paste)

    e.preventDefault()
    e.stopImmediatePropagation()
    const blob = imageItem.getAsFile()
    if (!blob) return
    const ext = imageItem.type.split('/')[1] || 'png'
    const formData = new FormData()
    formData.append('file', blob, `paste_${Date.now()}.${ext}`)
    fetch(`/api/agents/${agentId}/upload`, { method: 'POST', body: formData }).catch(() => {})
  }, true /* capture phase */)

  // Send typed input to backend → PTY
  terminal.onData(data => {
    store.sendKeystroke(agentId, data)
  })

  // Track selection state so the speak button can show/hide
  terminal.onSelectionChange(() => {
    if (agentId === activeAgentId.value) {
      hasSelection.value = terminal.hasSelection()
    }
  })

  terminals[agentId] = { terminal, fitAddon }

  // For the visible (active) agent, fit synchronously BEFORE replay so the
  // rolling buffer reflows into the correct dimensions. Hidden agents stay
  // at default cols until they become active and get refit.
  if (agentId === activeAgentId.value) {
    try { fitAddon.fit() } catch {}
  }

  // Register as the PTY data receiver for this agent in the store.
  // This also replays the historical buffer into the terminal, so scrollback
  // survives view switches that unmount AgentsView.
  store.registerPtyHandler(agentId, chunk => {
    try {
      const bytes = Uint8Array.from(atob(chunk), c => c.charCodeAt(0))
      terminal.write(bytes)
    } catch { /* ignore malformed chunks */ }
  })

  // Final fit + scroll-to-bottom on the next frame for the active agent
  if (agentId === activeAgentId.value) {
    refitAndScroll(agentId, { focus: true })
  }
}

// Fit the terminal after layout has actually painted (rAF), scroll to the
// latest output, and re-fit once more on the next frame. The double-fit
// compensates for cases where the first measurement caught a stale layout
// (e.g. v-show flip, window resize landing mid-frame). Without this, xterm's
// viewport can end up smaller than available space — the scrollbar then
// disappears and recent output appears "cut off".
function refitAndScroll(agentId, { focus = false } = {}) {
  requestAnimationFrame(() => {
    const t = terminals[agentId]
    if (!t) return
    try { t.fitAddon.fit() } catch {}
    notifyResize(agentId, t.fitAddon)
    try { t.terminal.scrollToBottom() } catch {}
    if (focus) try { t.terminal.focus() } catch {}
    // Second fit on the next frame — catches late layout settles
    requestAnimationFrame(() => {
      const t2 = terminals[agentId]
      if (!t2) return
      try { t2.fitAddon.fit() } catch {}
      notifyResize(agentId, t2.fitAddon)
      try { t2.terminal.scrollToBottom() } catch {}
    })
  })
}

// Resize active terminal when switching agents
watch(activeAgentId, newId => {
  refreshSelectionState()
  if (!newId) return
  refitAndScroll(newId, { focus: true })
})

function notifyResize(agentId, fitAddon) {
  const dims = fitAddon.proposeDimensions()
  if (!dims?.cols || !dims?.rows) return
  const last = lastSentDims[agentId]
  if (last?.cols === dims.cols && last?.rows === dims.rows) return
  lastSentDims[agentId] = { cols: dims.cols, rows: dims.rows }
  store.resizePty(agentId, dims.cols, dims.rows)
}

// Resize terminal on container size change
let resizeObs = null
let resizeDebounce = null
onMounted(() => {
  if (!containerRef.value) return
  resizeObs = new ResizeObserver(() => {
    const id = activeAgentId.value
    if (!id || !terminals[id]) return
    // Debounce so a flurry of resize events (drag, window resize) settles
    // before we compute final dimensions.
    clearTimeout(resizeDebounce)
    resizeDebounce = setTimeout(() => refitAndScroll(id), 50)
  })
  resizeObs.observe(containerRef.value)
})

onUnmounted(() => {
  resizeObs?.disconnect()
  clearTimeout(resizeDebounce)
  try { window.speechSynthesis.cancel() } catch {}
  // Drop our handler refs so the store stops calling into disposed terminals
  // while we're unmounted; the rolling buffer keeps history for the next mount.
  Object.keys(terminals).forEach(id => store.unregisterPtyHandler(id))
  Object.values(terminals).forEach(({ terminal }) => terminal.dispose())
})
</script>
