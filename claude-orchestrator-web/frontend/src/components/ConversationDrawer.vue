<template>
  <div class="flex flex-col h-full bg-gray-950 border-l border-gray-800 min-w-0">
    <!-- Header -->
    <header class="flex items-center justify-between gap-2 px-3 py-2 border-b border-gray-800 flex-shrink-0">
      <div class="flex items-center gap-2 min-w-0">
        <span class="text-sm">📄</span>
        <span class="text-sm font-medium text-gray-200 whitespace-nowrap">Přepis konverzace</span>
        <span v-if="agentName" class="text-xs text-gray-500 truncate">· {{ agentName }}</span>
      </div>
      <div class="flex items-center gap-1 flex-shrink-0">
        <button
          @click="reload"
          :disabled="loading"
          title="Obnovit"
          aria-label="Obnovit přepis"
          class="w-7 h-7 flex items-center justify-center rounded text-gray-400 hover:text-gray-100 hover:bg-gray-800 transition-colors disabled:opacity-40"
        >
          <svg xmlns="http://www.w3.org/2000/svg" class="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M23 4v6h-6M1 20v-6h6"/><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"/></svg>
        </button>
        <button
          @click="$emit('close')"
          title="Zavřít"
          aria-label="Zavřít přepis"
          class="w-7 h-7 flex items-center justify-center rounded text-gray-400 hover:text-gray-100 hover:bg-gray-800 transition-colors"
        >
          <svg xmlns="http://www.w3.org/2000/svg" class="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6 6 18M6 6l12 12"/></svg>
        </button>
      </div>
    </header>

    <!-- Body: reuse the Reader's markdown preview (native scroll → touchpad-friendly) -->
    <div class="flex-1 min-h-0 flex flex-col">
      <ReaderPreview v-if="tab" :tab="tab" @scroll="onPreviewScroll" />

      <div v-else class="flex-1 flex items-center justify-center p-6 text-center text-sm text-gray-500">
        <div v-if="loading">Načítám přepis…</div>
        <div v-else-if="notYet">
          <div class="text-3xl mb-2">💬</div>
          <p>Zatím žádný přepis.</p>
          <p class="text-xs mt-1 text-gray-600">Jakmile agent něco řekne, objeví se tady.</p>
          <button @click="reload" class="mt-3 text-xs px-2 py-1 rounded bg-gray-800 hover:bg-gray-700 text-gray-300">Zkusit znovu</button>
        </div>
        <div v-else-if="error">
          <div class="text-3xl mb-2">⚠️</div>
          <p>Přepis se nepodařilo načíst.</p>
          <p class="text-xs mt-1 text-gray-600 break-all">{{ error }}</p>
          <button @click="reload" class="mt-3 text-xs px-2 py-1 rounded bg-gray-800 hover:bg-gray-700 text-gray-300">Zkusit znovu</button>
        </div>
        <div v-else>
          <p>Žádný aktivní agent.</p>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
// Read-back panel for an agent's conversation. It renders the durable `history.md`
// (the transcript the backend already captures + resets on /clear) through the existing
// Reader stack: /api/reader/content to load, /hubs/reader FileChanged to live-update.
// It never touches the xterm terminal or its wheel handling — it's a plain scrollable
// panel next to it, so scrolling works fine on a touchpad without the mouse-mode jank.
import { ref, watch, onMounted, onBeforeUnmount } from 'vue'
import * as signalR from '@microsoft/signalr'
import * as readerApi from '../services/readerApi.js'
import ReaderPreview from './reader/ReaderPreview.vue'

const props = defineProps({
  agentId: { type: String, default: null },
  agentName: { type: String, default: '' },
})
defineEmits(['close'])

// tab is shaped like a Reader store tab so ReaderPreview can render it directly.
const tab = ref(null)
const loading = ref(false)
const notYet = ref(false)
const error = ref('')

let absPath = null        // normalized path (from getContent) — the key for watch + FileChanged
let connection = null
let loadSeq = 0           // guards against out-of-order loads when the active agent changes fast
let refetchTimer = null
const BOTTOM = 1e9        // scrollY sentinel: land on the newest turn on (re)load (clamps to max)

async function stopWatch() {
  if (absPath) {
    const p = absPath
    absPath = null
    try { await readerApi.unwatch(p) } catch {}
  }
}

async function load() {
  const seq = ++loadSeq
  await stopWatch()
  tab.value = null
  error.value = ''
  notYet.value = false
  if (!props.agentId) { loading.value = false; return }
  loading.value = true
  try {
    const res = await fetch(`/api/agents/${props.agentId}/conversation-path`)
    if (seq !== loadSeq) return
    if (!res.ok) throw new Error(`HTTP ${res.status}`)
    const { path, exists } = await res.json()
    if (!exists) { notYet.value = true; return }
    const { path: abs, content } = await readerApi.getContent(path)
    if (seq !== loadSeq) return
    absPath = abs
    tab.value = { path: abs, content, mode: 'full', scrollY: BOTTOM }
    try { await readerApi.watch(abs) } catch {}
  } catch (e) {
    if (seq === loadSeq) error.value = e?.message || String(e)
  } finally {
    if (seq === loadSeq) loading.value = false
  }
}

function reload() { load() }

// Preserve the user's scroll position across live re-renders (ReaderPreview restores
// scrollTop to tab.scrollY on every content change).
function onPreviewScroll(y) {
  if (tab.value) tab.value.scrollY = y
}

function scheduleRefetch() {
  if (refetchTimer) clearTimeout(refetchTimer)
  refetchTimer = setTimeout(async () => {
    refetchTimer = null
    if (!absPath || !tab.value) return
    try {
      const { content } = await readerApi.getContent(absPath)
      if (tab.value) tab.value.content = content
    } catch { /* keep existing content on refetch failure */ }
  }, 250)
}

function onFileChanged(path) {
  if (path === absPath) scheduleRefetch()
}

watch(() => props.agentId, () => { load() })

onMounted(async () => {
  connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/reader')
    .withAutomaticReconnect()
    .build()
  connection.on('FileChanged', (path) => onFileChanged(path))
  try { await connection.start() } catch { /* live updates degrade to manual refresh */ }
  await load()
})

onBeforeUnmount(async () => {
  if (refetchTimer) clearTimeout(refetchTimer)
  loadSeq++            // invalidate any in-flight load
  await stopWatch()
  try { await connection?.stop() } catch {}
})
</script>
