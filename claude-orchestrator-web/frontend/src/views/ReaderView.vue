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
        @remove-recent="store.removeRecent"
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
  </div>
</template>

<script setup>
import { ref, onMounted, onBeforeUnmount, watch } from 'vue'
import * as signalR from '@microsoft/signalr'
import { useReaderStore } from '../stores/reader.js'
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
let connection = null
let persistTimer = null

onMounted(async () => {
  store.hydrate()
  // Re-fetch content for any persisted full-mode tabs
  for (const t of store.tabs) {
    if (t.mode === 'full' && t.path) {
      try { await store.openFromPath(t.path) } catch {}
    }
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

async function onFileDropped(file) {
  const name = file.name.toLowerCase()
  if (!/\.(md|markdown|mdx|txt)$/.test(name)) {
    alert('Only .md/.markdown/.mdx/.txt supported')
    return
  }
  try { await store.openFromFile(file) }
  catch (e) { alert(`Open failed: ${e.message}`) }
}

async function onSubmitFile(file) {
  dialogOpen.value = false
  try { await store.openFromFile(file) }
  catch (e) { alert(`Open failed: ${e.message}`) }
}

async function openRecent(path) {
  try { await store.openFromPath(path) }
  catch { store.removeRecent(path) }
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
