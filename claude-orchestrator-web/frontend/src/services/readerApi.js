async function parseError(res) {
  try {
    const body = await res.json()
    if (body?.error) return new Error(body.error)
  } catch {}
  return new Error(`HTTP ${res.status}`)
}

export async function getContent(path) {
  const res = await fetch(`/api/reader/content?path=${encodeURIComponent(path)}`)
  if (!res.ok) throw await parseError(res)
  return res.json()
}

export function rawUrl(path) {
  return `/api/reader/raw?path=${encodeURIComponent(path)}`
}

export async function watch(path) {
  const res = await fetch('/api/reader/watch', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ path }),
  })
  if (!res.ok) throw await parseError(res)
}

export async function unwatch(path) {
  const res = await fetch('/api/reader/unwatch', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ path }),
  })
  if (!res.ok) throw await parseError(res)
}
