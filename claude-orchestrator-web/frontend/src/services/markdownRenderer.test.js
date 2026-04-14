import { describe, it, expect } from 'vitest'
import { createRenderer } from './markdownRenderer.js'

describe('markdownRenderer (base)', () => {
  const r = createRenderer({ mode: 'full', basePath: '/docs' })

  it('renders paragraphs and inline formatting', () => {
    const { html } = r.render('hello **world**')
    expect(html).toContain('<p>')
    expect(html).toContain('<strong>world</strong>')
  })

  it('extracts headings with level, text, id', () => {
    const md = '# A\n\n## B one\n\n### C'
    const { headings } = r.render(md)
    expect(headings).toEqual([
      { level: 1, text: 'A', id: expect.any(String) },
      { level: 2, text: 'B one', id: expect.any(String) },
      { level: 3, text: 'C', id: expect.any(String) },
    ])
    // anchors injected into HTML
    const { html } = r.render(md)
    expect(html).toMatch(/<h1 id="[^"]+"[^>]*>A<\/h1>/)
  })

  it('highlights code blocks with known language', () => {
    const { html } = r.render('```js\nconst x = 1\n```')
    expect(html).toContain('hljs')
    expect(html).toContain('const')
  })

  it('escapes HTML in unknown language code blocks', () => {
    const { html } = r.render('```\n<script>alert(1)</script>\n```')
    expect(html).not.toContain('<script>alert(1)</script>')
    expect(html).toContain('&lt;script&gt;')
  })
})
