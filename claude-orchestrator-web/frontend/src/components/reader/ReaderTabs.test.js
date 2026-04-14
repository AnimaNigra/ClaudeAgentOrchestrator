import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ReaderTabs from './ReaderTabs.vue'

const tabs = [
  { id: '1', displayName: 'a.md', mode: 'full' },
  { id: '2', displayName: 'b.md', mode: 'lite' },
]

describe('ReaderTabs', () => {
  it('renders one element per tab', () => {
    const w = mount(ReaderTabs, { props: { tabs, activeTabId: '1' } })
    expect(w.findAll('[data-testid="reader-tab"]').length).toBe(2)
  })

  it('marks the active tab', () => {
    const w = mount(ReaderTabs, { props: { tabs, activeTabId: '2' } })
    const [t1, t2] = w.findAll('[data-testid="reader-tab"]')
    expect(t1.classes()).not.toContain('is-active')
    expect(t2.classes()).toContain('is-active')
  })

  it('emits activate on tab click', async () => {
    const w = mount(ReaderTabs, { props: { tabs, activeTabId: '1' } })
    await w.findAll('[data-testid="reader-tab"]')[1].trigger('click')
    expect(w.emitted('activate')?.[0]?.[0]).toBe('2')
  })

  it('emits close on × click and does not bubble to activate', async () => {
    const w = mount(ReaderTabs, { props: { tabs, activeTabId: '1' } })
    await w.findAll('[data-testid="reader-tab-close"]')[1].trigger('click')
    expect(w.emitted('close')?.[0]?.[0]).toBe('2')
    expect(w.emitted('activate')).toBeFalsy()
  })

  it('emits open on + button', async () => {
    const w = mount(ReaderTabs, { props: { tabs, activeTabId: '1' } })
    await w.get('[data-testid="reader-tab-new"]').trigger('click')
    expect(w.emitted('open')).toBeTruthy()
  })
})
