import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ReaderToc from './ReaderToc.vue'

const headings = [
  { level: 1, text: 'A', id: 'a' },
  { level: 2, text: 'B', id: 'b' },
  { level: 3, text: 'C', id: 'c' },
]

describe('ReaderToc', () => {
  it('renders one link per heading with indent by level', () => {
    const w = mount(ReaderToc, { props: { headings, activeId: null } })
    const items = w.findAll('[data-testid="toc-item"]')
    expect(items.length).toBe(3)
    expect(items[0].attributes('data-level')).toBe('1')
    expect(items[2].attributes('data-level')).toBe('3')
  })

  it('emits navigate with heading id on click', async () => {
    const w = mount(ReaderToc, { props: { headings, activeId: null } })
    await w.findAll('[data-testid="toc-item"]')[1].trigger('click')
    expect(w.emitted('navigate')?.[0]?.[0]).toBe('b')
  })

  it('highlights the active heading', () => {
    const w = mount(ReaderToc, { props: { headings, activeId: 'b' } })
    const items = w.findAll('[data-testid="toc-item"]')
    expect(items[0].classes()).not.toContain('is-active')
    expect(items[1].classes()).toContain('is-active')
  })

  it('shows empty state if no headings', () => {
    const w = mount(ReaderToc, { props: { headings: [], activeId: null } })
    expect(w.text()).toMatch(/no headings/i)
  })
})
