// Jediné místo ve frontendu, které zná URL endpointů prohlížeče e-mailů.
//
// `source` je buď { path: '<absolutní cesta>' } nebo { file: File }. Cesta jde
// přes GET, nahraný soubor přes POST — server je jinak nerozlišuje.

async function parseError(res) {
  try {
    const body = await res.json()
    if (body?.error) return new Error(body.error)
  } catch {}
  return new Error(`HTTP ${res.status}`)
}

function upload(file, index) {
  const form = new FormData()
  form.append('file', file)
  if (index !== undefined) form.append('index', String(index))
  return { method: 'POST', body: form }
}

export async function parseEmail(source) {
  const res = source.file
    ? await fetch('/api/tools/email/parse', upload(source.file))
    : await fetch(`/api/tools/email/parse?path=${encodeURIComponent(source.path)}`)
  if (!res.ok) throw await parseError(res)
  return res.json()
}

// Server si nahraný soubor nedrží (spec §4.1), takže se při stahování přílohy
// posílá znovu. Na localhostu je to bezvýznamné.
export async function fetchAttachment(source, index) {
  const res = source.file
    ? await fetch('/api/tools/email/attachment', upload(source.file, index))
    : await fetch(
        `/api/tools/email/attachment?path=${encodeURIComponent(source.path)}&index=${index}`
      )
  if (!res.ok) throw await parseError(res)
  return res.blob()
}

export function saveBlob(blob, fileName) {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  // Odvolání se odkládá — některé prohlížeče stahování zruší, když se URL
  // uvolní ve stejném tiku, ve kterém se na odkaz kliklo.
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
