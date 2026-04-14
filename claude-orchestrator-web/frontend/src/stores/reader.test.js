import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useReaderStore } from './reader.js'

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
