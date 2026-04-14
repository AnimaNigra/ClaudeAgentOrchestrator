let _mermaid = null
let _initialized = false

async function getMermaid() {
  if (_mermaid) return _mermaid
  const mod = await import('mermaid')
  _mermaid = mod.default
  if (!_initialized) {
    _mermaid.initialize({
      startOnLoad: false,
      theme: 'dark',
      securityLevel: 'strict',
    })
    _initialized = true
  }
  return _mermaid
}

export async function renderAll(container) {
  const nodes = Array.from(container.querySelectorAll('.mermaid:not(.mermaid-error)'))
  if (nodes.length === 0) return
  const mermaid = await getMermaid()
  try {
    await mermaid.run({ nodes })
  } catch (err) {
    for (const n of nodes) {
      if (!n.querySelector('svg')) {
        n.classList.add('mermaid-error')
        n.textContent = `Mermaid error: ${err?.message || err}`
      }
    }
  }
}
