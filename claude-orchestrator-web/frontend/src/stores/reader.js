import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { v4 as uuid } from 'uuid'

export const useReaderStore = defineStore('reader', () => {
  const tabs = ref([])
  const activeTabId = ref(null)
  const recentFiles = ref([])
  const sidebarWidth = ref(260)

  const activeTab = computed(() =>
    tabs.value.find(t => t.id === activeTabId.value) || null
  )

  function addTab(partial) {
    // Dedupe by path (full-mode only — path !== null, mode !== 'lite')
    if (partial.path && partial.mode !== 'lite') {
      const existing = tabs.value.find(t => t.path === partial.path && t.mode !== 'lite')
      if (existing) {
        existing.content = partial.content
        existing.mtime = partial.mtime ?? existing.mtime
        existing.displayName = partial.displayName ?? existing.displayName
        activeTabId.value = existing.id
        return existing.id
      }
    }
    const tab = {
      id: uuid(),
      path: partial.path ?? null,
      displayName: partial.displayName ?? 'Untitled',
      content: partial.content ?? '',
      mtime: partial.mtime ?? null,
      mode: partial.mode ?? 'full',
      headings: [],
      scrollY: 0,
    }
    tabs.value.push(tab)
    activeTabId.value = tab.id
    return tab.id
  }

  function closeTab(id) {
    const idx = tabs.value.findIndex(t => t.id === id)
    if (idx < 0) return
    const wasActive = activeTabId.value === id
    tabs.value.splice(idx, 1)
    if (wasActive) {
      if (tabs.value.length === 0) activeTabId.value = null
      else activeTabId.value = tabs.value[Math.min(idx, tabs.value.length - 1)].id
    }
  }

  function activateTab(id) {
    if (tabs.value.some(t => t.id === id)) activeTabId.value = id
  }

  return {
    tabs, activeTabId, recentFiles, sidebarWidth,
    activeTab,
    addTab, closeTab, activateTab,
  }
})
