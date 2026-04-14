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

describe('markdownRenderer (images + mermaid)', () => {
  it('rewrites relative image src to /api/reader/raw in full mode', () => {
    const r = createRenderer({ mode: 'full', basePath: 'C:/docs' })
    const { html } = r.render('![alt](./img.png)')
    // Path-separator-agnostic: we expect basePath + img.png in the query
    expect(html).toMatch(/\/api\/reader\/raw\?path=/)
    expect(decodeURIComponent(html)).toContain('C:/docs')
    expect(decodeURIComponent(html)).toContain('img.png')
  })

  it('leaves absolute URLs untouched in full mode', () => {
    const r = createRenderer({ mode: 'full', basePath: 'C:/docs' })
    const { html } = r.render('![alt](https://example.com/x.png)')
    expect(html).toContain('src="https://example.com/x.png"')
  })

  it('replaces relative image with placeholder in lite mode', () => {
    const r = createRenderer({ mode: 'lite', basePath: null })
    const { html } = r.render('![alt](./img.png)')
    expect(html).toContain('data-placeholder="lite-mode"')
    expect(html).not.toContain('./img.png')
  })

  it('transforms ```mermaid fenced block into <div class="mermaid">', () => {
    const r = createRenderer({ mode: 'full', basePath: '/docs' })
    const { html } = r.render('```mermaid\ngraph TD\nA-->B\n```')
    expect(html).toContain('<div class="mermaid">')
    expect(html).toContain('graph TD')
  })
})
