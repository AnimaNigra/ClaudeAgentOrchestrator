import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useReaderStore } from './reader.js'
import { vi } from 'vitest'
vi.mock('../services/readerApi.js', () => ({
  getContent: vi.fn(),
  watch: vi.fn(),
  unwatch: vi.fn(),
  rawUrl: (p) => `/api/reader/raw?path=${encodeURIComponent(p)}`,
}))

import * as readerApi from '../services/readerApi.js'

function mkTab(partial = {}) {
  return {
    path: partial.path ?? '/a.md',
    displayName: partial.displayName ?? 'a.md',
    content: partial.content ?? '# a',
    mtime: partial.mtime ?? 1,
    mode: partial.mode ?? 'full',
  }
}

describe('reader store — tab lifecycle', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('adds a new tab and activates it', () => {
    const s = useReaderStore()
    const id = s.addTab(mkTab())
    expect(s.tabs.length).toBe(1)
    expect(s.activeTabId).toBe(id)
    expect(s.tabs[0].id).toBe(id)
    expect(s.tabs[0].headings).toEqual([])
    expect(s.tabs[0].scrollY).toBe(0)
  })

  it('dedupes by path — reopening same path activates existing tab', () => {
    const s = useReaderStore()
    const id1 = s.addTab(mkTab({ path: '/a.md' }))
    s.addTab(mkTab({ path: '/b.md' }))
    const id3 = s.addTab(mkTab({ path: '/a.md', content: 'new' }))
    expect(s.tabs.length).toBe(2)
    expect(id3).toBe(id1)
    expect(s.activeTabId).toBe(id1)
    // content is refreshed for the deduped tab
    expect(s.tabs.find(t => t.id === id1).content).toBe('new')
  })

  it('does NOT dedupe lite tabs (path=null)', () => {
    const s = useReaderStore()
    s.addTab(mkTab({ path: null, mode: 'lite' }))
    s.addTab(mkTab({ path: null, mode: 'lite' }))
    expect(s.tabs.length).toBe(2)
  })

  it('closeTab removes and activates sibling', () => {
    const s = useReaderStore()
    const a = s.addTab(mkTab({ path: '/a.md' }))
    const b = s.addTab(mkTab({ path: '/b.md' }))
    s.activateTab(a)
    s.closeTab(a)
    expect(s.tabs.length).toBe(1)
    expect(s.activeTabId).toBe(b)
  })

  it('closing the last tab leaves activeTabId null', () => {
    const s = useReaderStore()
    const a = s.addTab(mkTab())
    s.closeTab(a)
    expect(s.tabs.length).toBe(0)
    expect(s.activeTabId).toBeNull()
  })

  it('closing an inactive tab does not change active', () => {
    const s = useReaderStore()
    const a = s.addTab(mkTab({ path: '/a.md' }))
    const b = s.addTab(mkTab({ path: '/b.md' }))
    s.activateTab(a)
    s.closeTab(b)
    expect(s.activeTabId).toBe(a)
  })

  it('activeTab getter returns the active tab object', () => {
    const s = useReaderStore()
    const a = s.addTab(mkTab({ path: '/a.md' }))
    expect(s.activeTab.id).toBe(a)
  })
})

describe('reader store — recent files', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('adds to recent with openedAt and dedupes by path', () => {
    const s = useReaderStore()
    s.addRecent('/a.md', 'a.md')
    s.addRecent('/b.md', 'b.md')
    s.addRecent('/a.md', 'a.md')
    expect(s.recentFiles.length).toBe(2)
    // most recent first
    expect(s.recentFiles[0].path).toBe('/a.md')
    expect(s.recentFiles[1].path).toBe('/b.md')
  })

  it('caps recent at 20 entries, dropping oldest', () => {
    const s = useReaderStore()
    for (let i = 0; i < 25; i++) s.addRecent(`/f${i}.md`, `f${i}.md`)
    expect(s.recentFiles.length).toBe(20)
    expect(s.recentFiles[0].path).toBe('/f24.md')
    expect(s.recentFiles[19].path).toBe('/f5.md')
  })

  it('removeRecent deletes by path', () => {
    const s = useReaderStore()
    s.addRecent('/a.md', 'a.md')
    s.addRecent('/b.md', 'b.md')
    s.removeRecent('/a.md')
    expect(s.recentFiles.map(r => r.path)).toEqual(['/b.md'])
  })
})

describe('reader store — misc setters', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('setSidebarWidth clamps to [160, 600]', () => {
    const s = useReaderStore()
    s.setSidebarWidth(100)
    expect(s.sidebarWidth).toBe(160)
    s.setSidebarWidth(999)
    expect(s.sidebarWidth).toBe(600)
    s.setSidebarWidth(300)
    expect(s.sidebarWidth).toBe(300)
  })

  it('setScrollY updates scroll on the tab', () => {
    const s = useReaderStore()
    const id = s.addTab({ path: '/a.md', displayName: 'a', content: '', mode: 'full' })
    s.setScrollY(id, 420)
    expect(s.tabs[0].scrollY).toBe(420)
  })

  it('updateTabHeadings sets headings on the tab', () => {
    const s = useReaderStore()
    const id = s.addTab({ path: '/a.md', displayName: 'a', content: '', mode: 'full' })
    s.updateTabHeadings(id, [{ level: 1, text: 'T', id: 't' }])
    expect(s.tabs[0].headings).toEqual([{ level: 1, text: 'T', id: 't' }])
  })
})

describe('reader store — persistence', () => {
  const KEY = 'claude-orchestrator-reader-state-v1'

  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('persist writes full tabs (without content), recent, width', () => {
    const s = useReaderStore()
    s.addTab({ path: '/a.md', displayName: 'a.md', content: 'XX', mode: 'full', mtime: 7 })
    s.addTab({ path: null, displayName: 'b.md', content: 'YY', mode: 'lite' }) // lite NOT persisted
    s.addRecent('/a.md', 'a.md')
    s.setSidebarWidth(333)
    s.persist()
    const raw = JSON.parse(localStorage.getItem(KEY))
    expect(raw.tabs.length).toBe(1)
    expect(raw.tabs[0].path).toBe('/a.md')
    expect(raw.tabs[0].content).toBeUndefined()
    expect(raw.recentFiles.length).toBe(1)
    expect(raw.sidebarWidth).toBe(333)
  })

  it('hydrate restores recent + width and tab stubs (content empty pending re-fetch)', () => {
    localStorage.setItem(KEY, JSON.stringify({
      tabs: [{ id: 'x', path: '/a.md', displayName: 'a.md', mtime: 1, mode: 'full' }],
      activeTabId: 'x',
      recentFiles: [{ path: '/a.md', displayName: 'a.md', openedAt: 1 }],
      sidebarWidth: 400,
    }))
    const s = useReaderStore()
    s.hydrate()
    expect(s.tabs.length).toBe(1)
    expect(s.tabs[0].content).toBe('')
    expect(s.tabs[0].headings).toEqual([])
    expect(s.activeTabId).toBe('x')
    expect(s.sidebarWidth).toBe(400)
    expect(s.recentFiles.length).toBe(1)
  })

  it('hydrate tolerates corrupt JSON', () => {
    localStorage.setItem(KEY, '<<not json>>')
    const s = useReaderStore()
    expect(() => s.hydrate()).not.toThrow()
    expect(s.tabs.length).toBe(0)
  })

  it('hydrate tolerates missing fields', () => {
    localStorage.setItem(KEY, JSON.stringify({ tabs: [] }))
    const s = useReaderStore()
    s.hydrate()
    expect(s.sidebarWidth).toBe(260) // default
    expect(s.recentFiles).toEqual([])
  })
})

describe('reader store — async actions', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('openFromPath fetches content, adds tab, calls watch, adds recent', async () => {
    readerApi.getContent.mockResolvedValue({ path: '/a.md', content: '# hi', mtime: 42 })
    readerApi.watch.mockResolvedValue()
    const s = useReaderStore()
    const id = await s.openFromPath('/a.md')
    expect(readerApi.getContent).toHaveBeenCalledWith('/a.md')
    expect(readerApi.watch).toHaveBeenCalledWith('/a.md')
    expect(s.tabs[0].content).toBe('# hi')
    expect(s.tabs[0].mode).toBe('full')
    expect(s.activeTabId).toBe(id)
    expect(s.recentFiles[0].path).toBe('/a.md')
  })

  it('openFromPath propagates fetch errors and does not add a tab', async () => {
    readerApi.getContent.mockRejectedValue(new Error('File not found'))
    const s = useReaderStore()
    await expect(s.openFromPath('/nope.md')).rejects.toThrow(/not found/)
    expect(s.tabs.length).toBe(0)
  })

  it('openFromFile reads File via FileReader and adds a lite tab', async () => {
    const file = new File(['# from disk'], 'note.md', { type: 'text/markdown' })
    const s = useReaderStore()
    const id = await s.openFromFile(file)
    expect(s.tabs[0].mode).toBe('lite')
    expect(s.tabs[0].path).toBeNull()
    expect(s.tabs[0].displayName).toBe('note.md')
    expect(s.tabs[0].content).toBe('# from disk')
    expect(s.activeTabId).toBe(id)
  })

  it('handleFileChanged refetches content for matching tab and preserves scrollY', async () => {
    readerApi.getContent.mockResolvedValueOnce({ path: '/a.md', content: 'v1', mtime: 1 })
    readerApi.watch.mockResolvedValue()
    const s = useReaderStore()
    const id = await s.openFromPath('/a.md')
    s.setScrollY(id, 123)
    readerApi.getContent.mockResolvedValueOnce({ path: '/a.md', content: 'v2', mtime: 2 })
    await s.handleFileChanged('/a.md', 2)
    const t = s.tabs.find(t => t.id === id)
    expect(t.content).toBe('v2')
    expect(t.mtime).toBe(2)
    expect(t.scrollY).toBe(123)
  })

  it('handleFileChanged is a no-op if no tab matches the path', async () => {
    const s = useReaderStore()
    await s.handleFileChanged('/unknown.md', 9)
    expect(readerApi.getContent).not.toHaveBeenCalled()
  })

  it('closeTab on full-mode tab calls unwatch', async () => {
    readerApi.getContent.mockResolvedValue({ path: '/a.md', content: 'x', mtime: 1 })
    readerApi.watch.mockResolvedValue()
    readerApi.unwatch.mockResolvedValue()
    const s = useReaderStore()
    const id = await s.openFromPath('/a.md')
    s.closeTab(id)
    expect(readerApi.unwatch).toHaveBeenCalledWith('/a.md')
  })
})

describe('reader store — large file / extension guards', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('asks for confirm when content > 5 MB; aborts on decline', async () => {
    const bigContent = 'x'.repeat(6 * 1024 * 1024)
    readerApi.getContent.mockResolvedValue({ path: '/a.md', content: bigContent, mtime: 1 })
    const s = useReaderStore()
    const origConfirm = globalThis.confirm
    globalThis.confirm = vi.fn(() => false)
    const id = await s.openFromPath('/a.md')
    expect(id).toBeNull()
    expect(s.tabs.length).toBe(0)
    globalThis.confirm = origConfirm
  })

  it('continues when user accepts the large-file prompt', async () => {
    const bigContent = 'x'.repeat(6 * 1024 * 1024)
    readerApi.getContent.mockResolvedValue({ path: '/a.md', content: bigContent, mtime: 1 })
    readerApi.watch.mockResolvedValue()
    const s = useReaderStore()
    globalThis.confirm = vi.fn(() => true)
    const id = await s.openFromPath('/a.md')
    expect(id).toBeTruthy()
    expect(s.tabs.length).toBe(1)
  })
})
