import { encoder } from './bytes.js'

// SubtleCrypto umí jen tyhle čtyři. MD5 v seznamu chybí záměrně — WebCrypto ho
// neimplementuje a doplnit by ho šlo jen vlastní implementací v JS.
export const HASH_ALGORITHMS = ['SHA-1', 'SHA-256', 'SHA-384', 'SHA-512']

function toHex(bytes) {
  return Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('')
}

export async function hashText(text, algorithm) {
  if (!HASH_ALGORITHMS.includes(algorithm)) {
    return { ok: false, error: `Nepodporovaný algoritmus: ${algorithm}` }
  }
  if (!text) return { ok: true, value: '' }
  try {
    const digest = await crypto.subtle.digest(algorithm, encoder.encode(text))
    return { ok: true, value: toHex(new Uint8Array(digest)) }
  } catch {
    return { ok: false, error: 'Výpočet hashe selhal.' }
  }
}
