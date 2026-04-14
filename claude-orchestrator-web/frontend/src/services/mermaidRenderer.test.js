import { describe, it, expect, vi, beforeEach } from 'vitest'

vi.mock('mermaid', () => ({
  default: {
    initialize: vi.fn(),
    run: vi.fn(async ({ nodes }) => {
      // Simulate mermaid filling in an SVG
      for (const n of nodes) n.innerHTML = '<svg data-mock="mermaid"></svg>'
    }),
  },
}))

import { renderAll } from './mermaidRenderer.js'

describe('mermaidRenderer', () => {
  beforeEach(() => {
    document.body.innerHTML = ''
  })

  it('renders all .mermaid nodes in container', async () => {
    const c = document.createElement('div')
    c.innerHTML = '<div class="mermaid">graph</div><div class="mermaid">other</div>'
    document.body.appendChild(c)
    await renderAll(c)
    const svgs = c.querySelectorAll('svg[data-mock="mermaid"]')
    expect(svgs.length).toBe(2)
  })

  it('is safe to call on a container with no mermaid nodes', async () => {
    const c = document.createElement('div')
    c.innerHTML = '<p>nothing</p>'
    await expect(renderAll(c)).resolves.toBeUndefined()
  })

  it('adds class mermaid-error on parse failure', async () => {
    const mermaid = (await import('mermaid')).default
    mermaid.run.mockImplementationOnce(async () => { throw new Error('parse err') })
    const c = document.createElement('div')
    c.innerHTML = '<div class="mermaid">bad</div>'
    document.body.appendChild(c)
    await renderAll(c)
    const node = c.querySelector('.mermaid')
    expect(node.classList.contains('mermaid-error')).toBe(true)
    expect(node.textContent).toMatch(/parse err/)
  })
})
