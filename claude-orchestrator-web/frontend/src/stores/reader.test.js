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
