import MarkdownIt from 'markdown-it'
import anchor from 'markdown-it-anchor'
import hljs from 'highlight.js'

function slugify(s) {
  return s.toLowerCase()
    .replace(/[^\w\s-]/g, '')
    .trim()
    .replace(/\s+/g, '-')
}

export function createRenderer({ mode, basePath }) {
  const md = new MarkdownIt({
    html: false,
    linkify: true,
    breaks: false,
    highlight(str, lang) {
      if (lang === 'mermaid') {
        return `<div class="mermaid">${escapeHtml(str)}</div>`
      }
      if (lang && hljs.getLanguage(lang)) {
        try {
          return `<pre class="hljs"><code>${
            hljs.highlight(str, { language: lang, ignoreIllegals: true }).value
          }</code></pre>`
        } catch {}
      }
      return `<pre class="hljs"><code>${escapeHtml(str)}</code></pre>`
    },
  })

  md.use(anchor, { slugify })

  // Headings accumulator lives per-render, populated by a custom rule.
  let headings = []
  const origHeadingOpen = md.renderer.rules.heading_open
  md.renderer.rules.heading_open = function (tokens, idx, options, env, self) {
    const token = tokens[idx]
    const inline = tokens[idx + 1]
    const text = inline.children
      .filter(t => t.type === 'text' || t.type === 'code_inline')
      .map(t => t.content)
      .join('')
    const id = token.attrGet('id') || slugify(text)
    if (!token.attrGet('id')) token.attrSet('id', id)
    headings.push({
      level: Number(token.tag.slice(1)),
      text,
      id,
    })
    return origHeadingOpen
      ? origHeadingOpen(tokens, idx, options, env, self)
      : self.renderToken(tokens, idx, options)
  }

  // Image src rewriting
  const defaultImageRender = md.renderer.rules.image
    || function (tokens, idx, options, env, self) { return self.renderToken(tokens, idx, options) }

  md.renderer.rules.image = function (tokens, idx, options, env, self) {
    const token = tokens[idx]
    const srcIndex = token.attrIndex('src')
    const src = srcIndex >= 0 ? token.attrs[srcIndex][1] : ''
    if (isAbsoluteUrl(src)) {
      return defaultImageRender(tokens, idx, options, env, self)
    }
    if (mode === 'lite') {
      token.attrSet('src', '')
      token.attrSet('data-placeholder', 'lite-mode')
      token.attrSet('title', 'Image not loaded — lite mode (drag-drop). Open via path dialog for images.')
    } else {
      const resolved = resolveRelative(basePath, src)
      token.attrs[srcIndex][1] = `/api/reader/raw?path=${encodeURIComponent(resolved)}`
    }
    return defaultImageRender(tokens, idx, options, env, self)
  }

  return {
    render(src) {
      headings = []
      const html = md.render(src)
      return { html, headings: [...headings] }
    },
  }
}

function isAbsoluteUrl(s) {
  return /^(https?:|data:|blob:|\/)/.test(s)
}

function resolveRelative(base, rel) {
  if (!base) return rel
  // Normalise separators to '/'; allow backslashes in input (Windows paths).
  const b = base.replace(/\\/g, '/').replace(/\/+$/, '')
  const r = rel.replace(/\\/g, '/').replace(/^\.\//, '')
  // Walk '..' segments
  const parts = (b + '/' + r).split('/')
  const out = []
  for (const p of parts) {
    if (p === '..') out.pop()
    else if (p !== '.' && p !== '') out.push(p)
  }
  // Preserve leading separator for non-Windows absolute paths
  const leading = b.startsWith('/') ? '/' : ''
  return leading + out.join('/')
}

function escapeHtml(s) {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}
