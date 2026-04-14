import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ReaderRecent from './ReaderRecent.vue'

const items = [
  { path: '/a.md', displayName: 'a.md', openedAt: 2 },
  { path: '/b.md', displayName: 'b.md', openedAt: 1 },
]

describe('ReaderRecent', () => {
  it('renders one item per entry', () => {
    const w = mount(ReaderRecent, { props: { items } })
    expect(w.findAll('[data-testid="recent-item"]').length).toBe(2)
  })

  it('emits open with path on click', async () => {
    const w = mount(ReaderRecent, { props: { items } })
    await w.findAll('[data-testid="recent-item"]')[1].trigger('click')
    expect(w.emitted('open')?.[0]?.[0]).toBe('/b.md')
  })

  it('emits remove with path on × click', async () => {
    const w = mount(ReaderRecent, { props: { items } })
    await w.findAll('[data-testid="recent-remove"]')[0].trigger('click')
    expect(w.emitted('remove')?.[0]?.[0]).toBe('/a.md')
  })

  it('collapses and expands when header clicked', async () => {
    const w = mount(ReaderRecent, { props: { items } })
    expect(w.findAll('[data-testid="recent-item"]').length).toBe(2)
    await w.get('[data-testid="recent-toggle"]').trigger('click')
    expect(w.findAll('[data-testid="recent-item"]').length).toBe(0)
  })
})
