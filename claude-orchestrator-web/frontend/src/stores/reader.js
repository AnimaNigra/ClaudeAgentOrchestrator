import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { v4 as uuid } from 'uuid'
import * as readerApi from '../services/readerApi.js'

const LARGE_THRESHOLD = 5 * 1024 * 1024

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
    const tab = tabs.value[idx]
    const wasActive = activeTabId.value === id
    if (tab.mode === 'full' && tab.path) {
      readerApi.unwatch(tab.path)?.catch(() => {})
    }
    tabs.value.splice(idx, 1)
    if (wasActive) {
      if (tabs.value.length === 0) activeTabId.value = null
      else activeTabId.value = tabs.value[Math.min(idx, tabs.value.length - 1)].id
    }
  }

  function activateTab(id) {
    if (tabs.value.some(t => t.id === id)) activeTabId.value = id
  }

  function addRecent(path, displayName) {
    const existingIdx = recentFiles.value.findIndex(r => r.path === path)
    if (existingIdx >= 0) recentFiles.value.splice(existingIdx, 1)
    recentFiles.value.unshift({ path, displayName, openedAt: Date.now() })
    if (recentFiles.value.length > 20) recentFiles.value.length = 20
  }

  function removeRecent(path) {
    recentFiles.value = recentFiles.value.filter(r => r.path !== path)
  }

  function setSidebarWidth(px) {
    sidebarWidth.value = Math.min(600, Math.max(160, Math.round(px)))
  }

  function setScrollY(id, y) {
    const tab = tabs.value.find(t => t.id === id)
    if (tab) tab.scrollY = y
  }

  function updateTabHeadings(id, headings) {
    const tab = tabs.value.find(t => t.id === id)
    if (tab) tab.headings = headings
  }

  const STORAGE_KEY = 'claude-orchestrator-reader-state-v1'

  function persist() {
    const payload = {
      tabs: tabs.value
        .filter(t => t.mode === 'full' && t.path)
        .map(({ content, headings, scrollY, ...rest }) => rest),
      activeTabId: activeTabId.value,
      recentFiles: recentFiles.value,
      sidebarWidth: sidebarWidth.value,
    }
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(payload)) } catch {}
  }

  function hydrate() {
    let raw
    try { raw = JSON.parse(localStorage.getItem(STORAGE_KEY) || 'null') } catch { raw = null }
    if (!raw || typeof raw !== 'object') return
    if (Array.isArray(raw.tabs)) {
      tabs.value = raw.tabs.map(t => ({
        id: t.id,
        path: t.path ?? null,
        displayName: t.displayName ?? 'Untitled',
        content: '',
        mtime: t.mtime ?? null,
        mode: 'full',
        headings: [],
        scrollY: 0,
      }))
    }
    if (typeof raw.activeTabId === 'string') activeTabId.value = raw.activeTabId
    if (Array.isArray(raw.recentFiles)) recentFiles.value = raw.recentFiles
    if (typeof raw.sidebarWidth === 'number') sidebarWidth.value = raw.sidebarWidth
  }

  async function openFromPath(path) {
    const { path: abs, content, mtime } = await readerApi.getContent(path)
    if (content.length > LARGE_THRESHOLD) {
      const ok = globalThis.confirm(
        `File is large (${(content.length / 1024 / 1024).toFixed(1)} MB). Render anyway?`
      )
      if (!ok) return null
    }
    const displayName = abs.split(/[\\/]/).pop()
    const id = addTab({ path: abs, content, mtime, mode: 'full', displayName })
    try { await readerApi.watch(abs) } catch {}
    addRecent(abs, displayName)
    return id
  }

  function readFile(file) {
    return new Promise((resolve, reject) => {
      const fr = new FileReader()
      fr.onload = () => resolve(String(fr.result ?? ''))
      fr.onerror = () => reject(fr.error || new Error('Read failed'))
      fr.readAsText(file)
    })
  }

  async function openFromFile(file) {
    const content = await readFile(file)
    if (content.length > LARGE_THRESHOLD) {
      const ok = globalThis.confirm(
        `File is large (${(content.length / 1024 / 1024).toFixed(1)} MB). Render anyway?`
      )
      if (!ok) return null
    }
    return addTab({
      path: null, content, mtime: null, mode: 'lite', displayName: file.name,
    })
  }

  async function handleFileChanged(path, mtime) {
    const tab = tabs.value.find(t => t.path === path)
    if (!tab) return
    try {
      const { content } = await readerApi.getContent(path)
      tab.content = content
      tab.mtime = mtime
    } catch {
      // Leave existing content in place on refetch failure
    }
  }

  return {
    tabs, activeTabId, recentFiles, sidebarWidth,
    activeTab,
    addTab, closeTab, activateTab,
    addRecent, removeRecent,
    setSidebarWidth, setScrollY, updateTabHeadings,
    persist, hydrate,
    openFromPath, openFromFile, handleFileChanged,
  }
})
