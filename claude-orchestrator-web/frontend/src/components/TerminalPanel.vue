<template>
  <div ref="containerRef" class="relative w-full h-full bg-black overflow-hidden">
    <!-- One div per agent; v-show preserves the xterm DOM while hiding it.
         The right edge is inset by the scroll rail's width so the rail never
         covers the last column of Claude's box-drawn UI. -->
    <div
      v-for="agent in agentList"
      :key="agent.id"
      v-show="agent.id === activeAgentId"
      :ref="el => mountTerminal(agent.id, el)"
      class="absolute inset-y-0 left-0 right-3.5"
    />
    <div v-if="!activeAgentId" class="flex items-center justify-center h-full text-gray-600 text-sm select-none">
      No agent selected — type <code class="mx-1 text-blue-400">create &lt;name&gt;</code> to create one
    </div>

    <!-- Search overlay (Ctrl+F): Enter = next, Shift+Enter = previous, Esc = close -->
    <div
      v-if="activeAgentId && searchOpen"
      class="absolute top-3 right-6 z-20 flex items-center gap-1 bg-gray-900/95 border border-gray-700 rounded-md shadow-lg px-2 py-1"
    >
      <svg xmlns="http://www.w3.org/2000/svg" class="w-4 h-4 text-gray-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <circle cx="11" cy="11" r="7" /><path d="m21 21-4.3-4.3" />
      </svg>
      <input
        ref="searchInputRef"
        v-model="searchTerm"
        type="text"
        placeholder="Find in terminal…"
        spellcheck="false"
        class="bg-transparent text-sm text-gray-200 placeholder-gray-600 outline-none w-44"
        @keydown.enter.prevent="runSearch(!$event.shiftKey)"
        @keydown.esc.prevent="closeSearch"
      />
      <button
        @click="runSearch(false)"
        title="Previous match (Shift+Enter)"
        class="p-1 rounded text-gray-400 hover:text-gray-100 hover:bg-gray-700"
      >
        <svg xmlns="http://www.w3.org/2000/svg" class="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="m18 15-6-6-6 6" /></svg>
      </button>
      <button
        @click="runSearch(true)"
        title="Next match (Enter)"
        class="p-1 rounded text-gray-400 hover:text-gray-100 hover:bg-gray-700"
      >
        <svg xmlns="http://www.w3.org/2000/svg" class="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="m6 9 6 6 6-6" /></svg>
      </button>
      <button
        @click="closeSearch"
        title="Close (Esc)"
        class="p-1 rounded text-gray-400 hover:text-gray-100 hover:bg-gray-700"
      >
        <svg xmlns="http://www.w3.org/2000/svg" class="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 6 6 18M6 6l12 12" /></svg>
      </button>
    </div>


    <!-- Scroll rail.
         Claude Code turns on mouse reporting, so xterm hands the wheel to the PTY
         instead of scrolling its own viewport — the wheel scrolls Claude's view
         (cleanly redrawn) while the native scrollbar walks xterm's raw scrollback
         (a byte log of a redraw-in-place TUI, full of torn half-frames). Two
         different scroll spaces. This rail drives the wheel one, so dragging it
         behaves exactly like the wheel. Shift+drag reaches the raw scrollback. -->
    <div
      v-if="activeAgentId"
      class="absolute inset-y-0 right-0 w-3.5 z-10 flex flex-col select-none touch-none
             border-l border-gray-800 bg-gray-950/70 transition-opacity"
      :class="railActive ? 'opacity-100' : 'opacity-60 hover:opacity-100'"
    >
      <button
        class="rail-btn"
        title="Scroll up (hold to repeat)"
        aria-label="Scroll up"
        @pointerdown="startRepeat($event, -1)"
        @pointerup="stopRepeat"
        @pointercancel="stopRepeat"
      >
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3"><path d="m6 15 6-6 6 6" /></svg>
      </button>

      <div
        class="rail-grip"
        title="Drag to scroll — hold Shift to scroll xterm's raw scrollback instead"
        @pointerdown="onRailDown"
        @pointermove="onRailMove"
        @pointerup="onRailUp"
        @pointercancel="onRailUp"
        @wheel.prevent="onRailWheel"
      />

      <button
        class="rail-btn"
        title="Scroll down (hold to repeat)"
        aria-label="Scroll down"
        @pointerdown="startRepeat($event, 1)"
        @pointerup="stopRepeat"
        @pointercancel="stopRepeat"
      >
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3"><path d="m6 9 6 6 6-6" /></svg>
      </button>

      <button
        class="rail-btn"
        title="Jump to latest output"
        aria-label="Jump to latest output"
        @click="jumpToBottom"
      >
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3"><path d="M12 4v13M6 12l6 6 6-6" /></svg>
      </button>
    </div>

    <!-- Speak selection button: always visible while an agent is active.
         Dimmed when there's no selection so the user knows where to click. -->
    <button
      v-if="activeAgentId"
      @click="toggleSpeak"
      :disabled="!hasSelection && !isSpeaking"
      :title="isSpeaking ? 'Stop reading' : (hasSelection ? 'Read selection aloud' : 'Hold Shift and drag to select text in the terminal (Claude grabs the mouse otherwise), then click to read it aloud')"
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
import { ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue'
import { storeToRefs } from 'pinia'
import { Terminal } from '@xterm/xterm'
import { FitAddon } from '@xterm/addon-fit'
import { WebglAddon } from '@xterm/addon-webgl'
import { WebLinksAddon } from '@xterm/addon-web-links'
import { SearchAddon } from '@xterm/addon-search'
import { Unicode11Addon } from '@xterm/addon-unicode11'
import '@xterm/xterm/css/xterm.css'
import { useAgentsStore } from '../stores/agents'

const store = useAgentsStore()
const { agentList, activeAgentId } = storeToRefs(store)

const containerRef = ref(null)

// Map of agentId → { terminal, fitAddon, searchAddon }
const terminals = {}

// Search overlay state (Ctrl+F). The SearchAddon is per-terminal; the overlay
// always drives the currently active agent's addon.
const searchOpen = ref(false)
const searchTerm = ref('')
const searchInputRef = ref(null)

// Match-highlight colors tuned to the terminal theme.
const searchDecorations = {
  matchBackground: '#3b5070',
  matchBorder: '#1f6feb',
  matchOverviewRuler: '#58a6ff',
  activeMatchBackground: '#58a6ff',
  activeMatchBorder: '#79c0ff',
  activeMatchColorOverviewRuler: '#79c0ff',
}

function runSearch(forward = true) {
  const id = activeAgentId.value
  const addon = id ? terminals[id]?.searchAddon : null
  if (!addon) return
  const term = searchTerm.value
  if (!term) { addon.clearDecorations?.(); return }
  const opts = { decorations: searchDecorations }
  if (forward) addon.findNext(term, opts)
  else addon.findPrevious(term, opts)
}

function openSearch() {
  searchOpen.value = true
  nextTick(() => searchInputRef.value?.focus())
  if (searchTerm.value) runSearch(true)
}

function closeSearch() {
  searchOpen.value = false
  const id = activeAgentId.value
  if (id) {
    terminals[id]?.searchAddon?.clearDecorations?.()
    terminals[id]?.terminal?.focus()
  }
}

// Re-run search live as the term changes.
watch(searchTerm, () => runSearch(true))

// ── Scroll rail ──────────────────────────────────────────────────────────────
// Claude Code enables mouse reporting, so xterm forwards the wheel to the PTY
// (CoreBrowserTerminal.bindMouse) instead of scrolling its own viewport. The
// rail reproduces that by dispatching synthetic wheel events at the terminal:
// xterm has no isTrusted check anywhere, so they take the exact same path and
// get encoded in whatever protocol the app negotiated (SGR, vt200, …).

const railActive = ref(false)

// Drag distance that equals one wheel notch.
const PX_PER_NOTCH = 14
// Notch ceiling per frame. Claude repaints its whole frame per notch, so an
// unthrottled flick would flood the PTY.
const MAX_NOTCHES_PER_FRAME = 10
// Lines per notch when scrolling xterm's own scrollback rather than the app's
// view — that buffer holds up to `scrollback` lines, so it needs a bigger step.
const RAW_LINES_PER_NOTCH = 5

// One wheel event == one wheel report to the app regardless of deltaY magnitude
// (bindMouse only reads its sign), so N notches need N events. deltaMode LINE
// keeps xterm's trackpad heuristics and partial-scroll accumulator out of it.
function emitWheelNotch(term, dir) {
  const el = term.element
  if (!el) return
  const r = el.getBoundingClientRect()
  el.dispatchEvent(new WheelEvent('wheel', {
    deltaY: dir,
    deltaMode: 1, // WheelEvent.DOM_DELTA_LINE
    clientX: r.left + r.width / 2,
    clientY: r.top + r.height / 2,
    bubbles: true,
    cancelable: true,
  }))
}

// Scroll the active terminal by `lines` (negative = up).
// raw=true forces xterm's own scrollback instead of the running app's view.
function railScroll(lines, raw = false, cap = MAX_NOTCHES_PER_FRAME) {
  const id = activeAgentId.value
  const t = id ? terminals[id] : null
  if (!t || !lines) return
  const term = t.terminal

  // The app owns the wheel once it turns on mouse reporting; in the alt buffer
  // xterm additionally converts wheel into cursor keys. Both need real events.
  // Everything else (plain shell at a prompt) has no listener to receive a
  // synthetic wheel — untrusted events don't trigger the viewport's native
  // scroll either — so drive xterm's scrollback directly.
  const appOwnsWheel = term.modes.mouseTrackingMode !== 'none'
    || term.buffer.active.type === 'alternate'

  if (raw || !appOwnsWheel) {
    // One line per notch would make a full-rail drag cover ~50 lines of a
    // 10000-line scrollback, so give the raw space a coarser step.
    try { term.scrollLines(lines * RAW_LINES_PER_NOTCH) } catch {}
    return
  }

  const dir = lines < 0 ? -1 : 1
  const n = Math.min(Math.abs(lines), cap)
  for (let i = 0; i < n; i++) emitWheelNotch(term, dir)
}

// Drag state. Pixels are accumulated and converted to notches once per frame.
let dragPx = 0
let dragLastY = 0
let dragRaf = 0
let dragRaw = false

function onRailDown(e) {
  if (e.button !== 0) return
  e.preventDefault()
  e.currentTarget.setPointerCapture(e.pointerId)
  railActive.value = true
  dragLastY = e.clientY
  dragPx = 0
  dragRaw = e.shiftKey
}

function onRailMove(e) {
  if (!railActive.value) return
  dragPx += e.clientY - dragLastY
  dragLastY = e.clientY
  if (!dragRaf) dragRaf = requestAnimationFrame(flushDrag)
}

function flushDrag() {
  dragRaf = 0
  const notches = Math.trunc(dragPx / PX_PER_NOTCH)
  if (!notches) return
  dragPx -= notches * PX_PER_NOTCH
  railScroll(notches, dragRaw)
}

function onRailUp(e) {
  if (!railActive.value) return
  railActive.value = false
  try { e.currentTarget.releasePointerCapture(e.pointerId) } catch {}
  if (dragRaf) { cancelAnimationFrame(dragRaf); dragRaf = 0 }
  dragPx = 0
}

// The rail is a sibling overlay, not a child of terminal.element, so a real
// wheel over it would otherwise go nowhere. Forward it as a single notch.
function onRailWheel(e) {
  if (!e.deltaY) return
  railScroll(e.deltaY < 0 ? -1 : 1, e.shiftKey)
}

let repeatDelay = 0
let repeatTimer = 0

function startRepeat(e, dir) {
  if (e.button !== 0) return
  e.preventDefault()
  e.currentTarget.setPointerCapture(e.pointerId)
  railActive.value = true
  railScroll(dir)
  repeatDelay = setTimeout(() => {
    repeatTimer = setInterval(() => railScroll(dir), 50)
  }, 300)
}

function stopRepeat(e) {
  clearTimeout(repeatDelay); repeatDelay = 0
  clearInterval(repeatTimer); repeatTimer = 0
  railActive.value = false
  try { e?.currentTarget?.releasePointerCapture?.(e.pointerId) } catch {}
}

// xterm's scrollback has a real bottom to jump to. The app's own view has no
// "go to end" in the wheel protocol, so send a burst of notches and let it
// clamp. Claude Code advertises Ctrl+End for this, but sending `ESC [1;5F`
// doesn't move it — and injecting keys into its input parser is riskier than
// mouse reports anyway, which it can only read as scrolling.
function jumpToBottom() {
  const id = activeAgentId.value
  const t = id ? terminals[id] : null
  if (!t) return
  try { t.terminal.scrollToBottom() } catch {}
  railScroll(40, false, 40)
}

// Track last PTY dimensions sent per agent — only send resize when they actually change
// This prevents unnecessary PTY redraws (which falsely trigger the "Running" state) on agent switch
const lastSentDims = {}

// Speak-selection state.
// We cache the last non-empty selection rather than reading the live xterm
// selection at click time: an active agent writes to the terminal frequently
// (high-frequency pty_data), and every terminal.write() CLEARS the current
// xterm selection. Reading it on click would then return '' right after the
// user selected something. Caching the latest non-empty selection keeps the
// speak button usable on a terminal that is still producing output.
const selectedText = ref('')
const hasSelection = computed(() => !!selectedText.value.trim())
const isSpeaking = ref(false)

// On agent switch, reset the cached selection to the newly active terminal's
// own selection (normally empty) so a stale selection from another agent
// doesn't carry over.
function refreshSelectionState() {
  const id = activeAgentId.value
  const t = id ? terminals[id] : null
  selectedText.value = t?.terminal.getSelection() || ''
}

function toggleSpeak() {
  if (isSpeaking.value) {
    window.speechSynthesis.cancel()
    isSpeaking.value = false
    return
  }
  const text = selectedText.value.trim()
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
  // Chrome can leave the engine stuck in a "paused" state after a prior
  // cancel() (incl. one from the Reader tab — speechSynthesis is a global
  // singleton), making speak() silently do nothing. resume() clears it and is
  // a harmless no-op when not paused.
  window.speechSynthesis.resume()
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

  // Correct width for wide chars / emoji that Claude's output uses. Requires
  // allowProposedApi (set above); activeVersion must be set after the addon loads.
  terminal.loadAddon(new Unicode11Addon())
  terminal.unicode.activeVersion = '11'

  // Make URLs in the output clickable (Claude frequently prints links).
  terminal.loadAddon(new WebLinksAddon())

  // Ctrl+F search across the scrollback buffer.
  const searchAddon = new SearchAddon()
  terminal.loadAddon(searchAddon)

  terminal.open(el)

  // GPU-accelerated rendering. Must load after open(). If WebGL is unavailable
  // or its context is lost at runtime, dispose the addon so xterm falls back to
  // the DOM renderer instead of rendering a blank terminal.
  try {
    const webgl = new WebglAddon()
    webgl.onContextLoss(() => { try { webgl.dispose() } catch {} })
    terminal.loadAddon(webgl)
  } catch { /* WebGL unsupported — DOM renderer stays active */ }

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

    // Ctrl+F opens our search overlay instead of the browser's find dialog.
    if (e.ctrlKey && e.code === 'KeyF') { openSearch(); return false }

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

  // Cache the selection so the speak button works even after terminal output
  // clears the live xterm selection. Only capture non-empty selections; an
  // empty change (e.g. output wrote and wiped the highlight) keeps the last one.
  terminal.onSelectionChange(() => {
    if (agentId !== activeAgentId.value) return
    const sel = terminal.getSelection()
    if (sel.trim()) selectedText.value = sel
  })

  terminals[agentId] = { terminal, fitAddon, searchAddon }

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
  // The SearchAddon is per-terminal; close the overlay so its matches don't
  // appear to "belong" to the newly active agent.
  if (searchOpen.value) searchOpen.value = false
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
  clearTimeout(repeatDelay)
  clearInterval(repeatTimer)
  if (dragRaf) cancelAnimationFrame(dragRaf)
  try { window.speechSynthesis.cancel() } catch {}
  // Drop our handler refs so the store stops calling into disposed terminals
  // while we're unmounted; the rolling buffer keeps history for the next mount.
  Object.keys(terminals).forEach(id => store.unregisterPtyHandler(id))
  Object.values(terminals).forEach(({ terminal }) => terminal.dispose())
})
</script>

<style scoped>
.rail-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  height: 16px;
  color: #6b7280;
  transition: color .12s, background-color .12s;
}
.rail-btn:hover {
  color: #58a6ff;
  background: #1f2937;
}
.rail-btn svg {
  width: 10px;
  height: 10px;
}

/* Dotted grip, so the rail reads as a drag surface rather than a track with a
   thumb — there is no thumb to draw: the app never reports how tall its own
   content is or where in it we are, so this control is relative, not absolute. */
.rail-grip {
  flex: 1;
  cursor: ns-resize;
  background-image: repeating-linear-gradient(
    to bottom,
    #4b5563 0, #4b5563 2px,
    transparent 2px, transparent 5px
  );
  background-size: 2px 100%;
  background-position: center;
  background-repeat: no-repeat;
  opacity: .5;
  transition: opacity .12s;
}
.rail-grip:hover {
  opacity: 1;
}
</style>
