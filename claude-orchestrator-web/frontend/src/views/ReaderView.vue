<template>
  <div class="relative flex flex-col flex-1 overflow-hidden">
    <ReaderToolbar @open="dialogOpen = true" @export-pdf="printPdf">
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
})

onBeforeUnmount(async () => {
  try { await connection?.stop() } catch {}
})

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
  document.body.classList.add('printing-reader')
  window.print()
  document.body.classList.remove('printing-reader')
}
</script>
