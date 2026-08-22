// Čisté funkce pro textové kodeky. Nic tu nevyhazuje výjimku — chyby se vracejí
// jako { ok: false, error }, aby je komponenta mohla vykreslit bez try/catch.

import { encoder, decoder, bytesToBase64, base64ToBytes } from './bytes.js'

export function encodeBase64(text) {
  if (!text) return { ok: true, value: '' }
  return { ok: true, value: bytesToBase64(encoder.encode(text)) }
}

export function decodeBase64(text) {
  if (!text) return { ok: true, value: '' }
  try {
    return { ok: true, value: decoder.decode(base64ToBytes(text.replace(/\s+/g, ''))) }
  } catch {
    return { ok: false, error: 'Invalid Base64' }
  }
}

export function encodeUrl(text) {
  if (!text) return { ok: true, value: '' }
  // encodeURIComponent nechává !'()* být, Uri.EscapeDataString je escapuje (RFC 3986).
  const value = encodeURIComponent(text)
    .replace(/[!'()*]/g, c => '%' + c.charCodeAt(0).toString(16).toUpperCase())
  return { ok: true, value }
}

export function decodeUrl(text) {
  if (!text) return { ok: true, value: '' }
  try {
    return { ok: true, value: decodeURIComponent(text) }
  } catch {
    return { ok: false, error: 'Invalid URL encoding' }
  }
}

const HTML_ESCAPES = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }

export function encodeHtml(text) {
  if (!text) return { ok: true, value: '' }
  return { ok: true, value: text.replace(/[&<>"']/g, c => HTML_ESCAPES[c]) }
}

export function decodeHtml(text) {
  if (!text) return { ok: true, value: '' }
  // DOMParser místo innerHTML: nikdy se nedotkneme parseru, který by mohl něco spustit.
  const doc = new DOMParser().parseFromString(`<!doctype html><body>${text}`, 'text/html')
  return { ok: true, value: doc.body.textContent ?? '' }
}

export function buildBasicAuth(username, password) {
  const encoded = encodeBase64(`${username ?? ''}:${password ?? ''}`)
  if (!encoded.ok) return encoded
  return {
    ok: true,
    value: { token: encoded.value, header: `Authorization: Basic ${encoded.value}` },
  }
}
