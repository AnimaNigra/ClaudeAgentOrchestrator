<template>
  <div class="relative flex flex-col flex-1 overflow-hidden">
    <ReaderToolbar @open="dialogOpen = true" @print="printPdf">
      <template #tabs>
        <ReaderTabs
          :tabs="store.tabs"
          :active-tab-id="store.activeTabId"
          @activate="store.activateTab"
          @close="store.closeTab"
          @open="dialogOpen = true"
        />
      </template>
    </ReaderToolbar>

    <div class="flex flex-1 overflow-hidden relative">
      <ReaderSidebar
        :width="store.sidebarWidth"
        :headings="store.activeTab?.headings || []"
        :active-heading-id="activeHeadingId"
        :recent="store.recentFiles"
        @resize="w => { store.setSidebarWidth(w); schedulePersist() }"
        @navigate="onNavigate"
        @open-recent="openRecent"
        @remove-recent="onRemoveRecent"
      />

      <div class="relative flex-1 flex flex-col overflow-hidden">
        <ReaderPreview
          ref="previewEl"
          :tab="store.activeTab"
          @headings="h => store.activeTab && store.updateTabHeadings(store.activeTab.id, h)"
          @scroll="y => store.activeTab && store.setScrollY(store.activeTab.id, y)"
          @active-heading="id => activeHeadingId = id"
        />
        <DropOverlay @file-dropped="onFileDropped" />
      </div>
    </div>

    <OpenFileDialog
      :open="dialogOpen"
      @close="dialogOpen = false"
      @submit="onSubmitPath"
      @submit-file="onSubmitFile"
    />
    <!-- Hidden file input for re-opening lite-mode recent files -->
    <input
      ref="litePicker"
      type="file"
      class="hidden"
      accept=".md,.markdown,.mdx,.txt"
      @change="onLitePickerChange"
    />
  </div>
</template>

<script setup>
import { ref, onMounted, onBeforeUnmount, watch } from 'vue'
import * as signalR from '@microsoft/signalr'
import { useReaderStore } from '../stores/reader.js'
import * as fsa from '../services/fsaStore.js'
import ReaderToolbar from '../components/reader/ReaderToolbar.vue'
import ReaderTabs from '../components/reader/ReaderTabs.vue'
import ReaderSidebar from '../components/reader/ReaderSidebar.vue'
import ReaderPreview from '../components/reader/ReaderPreview.vue'
import DropOverlay from '../components/reader/DropOverlay.vue'
import OpenFileDialog from '../components/reader/OpenFileDialog.vue'

const store = useReaderStore()
const dialogOpen = ref(false)
const activeHeadingId = ref(null)
const previewEl = ref(null)
const litePicker = ref(null)
let connection = null
let persistTimer = null
let litePollTimer = null

onMounted(async () => {
  store.hydrate()
  // Re-fetch content for any persisted full-mode tabs.
  // If a file is gone from disk, drop the tab (and its recent entry) so the
  // user doesn't see a blank, unloadable tab.
  const missing = []
  for (const t of [...store.tabs]) {
    if (t.mode === 'full' && t.path) {
      try { await store.openFromPath(t.path) }
      catch {
        missing.push(t.path)
        store.closeTab(t.id)
        store.removeRecent(`full:${t.path}`)
      }
    }
  }
  if (missing.length) {
    alert(
      `${missing.length === 1 ? 'Previously opened file is' : 'Previously opened files are'} no longer available:\n\n` +
      missing.map(p => '• ' + p).join('\n')
    )
  }
  connection = new signalR.HubConnectionBuilder().withUrl('/hubs/reader').withAutomaticReconnect().build()
  connection.on('FileChanged', (path, mtime) => store.handleFileChanged(path, mtime))
  connection.on('WatchFailed', (path) => console.warn('watch failed', path))
  try { await connection.start() } catch {}

  // Poll lite-mode tabs with FSA handles for on-disk changes (live reload
  // for drag-drop / Browse opens on Chromium browsers)
  litePollTimer = setInterval(pollLiteTabs, 2000)
})

onBeforeUnmount(async () => {
  clearInterval(litePollTimer)
  try { await connection?.stop() } catch {}
})

// Track per-tab content size as an extra change signal — editors that do
// atomic saves (rename) can leave lastModified stale relative to the handle's
// in-memory view, but the re-read File's size is almost always different
// when content changed.
const liteSizeCache = new Map() // tabId → last known byte size

async function pollLiteTabs() {
  if (!fsa.isSupported()) return
  for (const tab of store.tabs) {
    if (tab.mode !== 'lite' || !tab.fsaKey) continue
    try {
      const handle = await fsa.getHandle(tab.fsaKey)
      if (!handle) continue
      // Don't gate on queryPermission — handles from drag-drop / showOpenFilePicker
      // sometimes report 'prompt' even with implicit grant. Just try getFile();
      // a real permission failure throws and lands in the catch.
      const file = await handle.getFile()
      const prevSize = liteSizeCache.get(tab.id)
      const mtime = file.lastModified || 0
      const size = file.size
      const changed = (mtime && mtime !== tab.mtime) || (prevSize !== undefined && prevSize !== size)
      if (changed) {
        const content = await file.text()
        store.updateLiteTabContent(tab.id, content, mtime || Date.now())
        if (import.meta.env.DEV) {
          console.log('[reader-poll] refreshed', tab.displayName, 'mtime', mtime, 'size', size)
        }
      }
      liteSizeCache.set(tab.id, size)
    } catch (err) {
      if (import.meta.env.DEV) {
        console.warn('[reader-poll] skip', tab.displayName, err?.name || err?.message || err)
      }
    }
  }
}

watch(() => [store.tabs.length, store.activeTabId, store.recentFiles.length, store.sidebarWidth],
  () => schedulePersist())

function schedulePersist() {
  clearTimeout(persistTimer)
  persistTimer = setTimeout(() => store.persist(), 200)
}

async function onSubmitPath(path) {
  dialogOpen.value = false
  try { await store.openFromPath(path) }
  catch (e) { alert(`Open failed: ${e.message}`) }
}

async function onFileDropped(file, handle) {
  const name = file.name.toLowerCase()
  if (!/\.(md|markdown|mdx|txt)$/.test(name)) {
    alert('Only .md/.markdown/.mdx/.txt supported')
    return
  }
  let fsaKey = null
  if (handle && fsa.isSupported()) {
    try {
      fsaKey = fsa.generateKey()
      await fsa.saveHandle(fsaKey, handle)
    } catch {
      fsaKey = null
    }
  }
  try { await store.openFromFile(file, fsaKey) }
  catch (e) { alert(`Open failed: ${e.message}`) }
}

async function onSubmitFile(file, handle) {
  dialogOpen.value = false
  let fsaKey = null
  if (handle && fsa.isSupported()) {
    try {
      fsaKey = fsa.generateKey()
      await fsa.saveHandle(fsaKey, handle)
    } catch (err) {
      console.warn('FSA handle save failed:', err)
      fsaKey = null
    }
  }
  try { await store.openFromFile(file, fsaKey) }
  catch (e) { alert(`Open failed: ${e.message}`) }
}

async function openRecent(entry) {
  // Back-compat: string argument (legacy full-mode path)
  if (typeof entry === 'string') entry = { path: entry, mode: 'full' }
  if (entry.mode === 'lite' || !entry.path) {
    // Try FSA handle first for one-click reopen (maybe with a permission prompt)
    if (entry.fsaKey && fsa.isSupported()) {
      try {
        const res = await fsa.reopenFromHandle(entry.fsaKey)
        if (res) {
          await store.openFromFile(res.file, entry.fsaKey)
          return
        }
      } catch (err) {
        console.warn('FSA reopen failed, falling back to file picker:', err)
      }
    }
    // No handle, permission denied, or FSA unsupported — fall back to file picker
    litePicker.value?.click()
    return
  }
  try { await store.openFromPath(entry.path) }
  catch (e) {
    store.removeRecent(`full:${entry.path}`)
    alert(`Cannot open: ${e.message}\n\nRemoved from Recent.`)
  }
}

function onRemoveRecent(entry) {
  if (typeof entry === 'string') { store.removeRecent(entry); return }
  const key = entry.mode === 'lite' ? `lite:${entry.displayName}` : `full:${entry.path}`
  store.removeRecent(key)
  if (entry.fsaKey) fsa.deleteHandle(entry.fsaKey).catch(() => {})
}

async function onLitePickerChange(e) {
  const file = e.target.files?.[0]
  e.target.value = ''
  if (!file) return
  try { await store.openFromFile(file) }
  catch (err) { alert(`Open failed: ${err.message}`) }
}

function onNavigate(id) {
  previewEl.value?.scrollToHeading(id)
}

function printPdf() {
  const article = document.querySelector('.reader-md')
  if (!article) return

  // Clone the rendered article into a detached top-level container so the
  // browser paginates its natural height instead of being clipped by the
  // app's nested overflow: hidden + h-screen containers.
  const root = document.createElement('div')
  root.id = 'reader-print-root'
  root.innerHTML = article.innerHTML
  document.body.appendChild(root)
  document.body.classList.add('printing-reader')

  const cleanup = () => {
    document.body.classList.remove('printing-reader')
    root.remove()
    window.removeEventListener('afterprint', cleanup)
  }
  window.addEventListener('afterprint', cleanup)

  // Defer print one tick so styles apply before the dialog opens
  setTimeout(() => {
    window.print()
    // Firefox/Safari don't fire afterprint reliably in all cases — also
    // schedule a fallback cleanup shortly after the sync print() returns.
    setTimeout(() => {
      if (document.body.classList.contains('printing-reader')) cleanup()
    }, 1000)
  }, 0)
}
</script>
