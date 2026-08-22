const encoder = new TextEncoder()
const decoder = new TextDecoder()

function base64ToBytes(base64) {
  const binary = atob(base64)
  const bytes = new Uint8Array(binary.length)
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i)
  return bytes
}

// base64url -> base64: jiná abeceda a chybějící výplň.
export function base64UrlToBytes(part) {
  const base64 = part.replace(/-/g, '+').replace(/_/g, '/')
  return base64ToBytes(base64 + '='.repeat((4 - (base64.length % 4)) % 4))
}

export function decodeJwt(token) {
  const trimmed = (token ?? '').trim()
  if (!trimmed) return { ok: true, value: null }

  const parts = trimmed.split('.')
  if (parts.length !== 3) {
    return { ok: false, error: 'Not a valid JWT (expected 3 dot-separated parts).' }
  }

  let headerJson, payloadJson
  try {
    headerJson = decoder.decode(base64UrlToBytes(parts[0]))
    payloadJson = decoder.decode(base64UrlToBytes(parts[1]))
  } catch {
    return { ok: false, error: 'Invalid Base64URL encoding in token.' }
  }

  let header, payload
  try {
    header = JSON.parse(headerJson)
    payload = JSON.parse(payloadJson)
  } catch {
    return { ok: false, error: 'Token header or payload is not valid JSON.' }
  }

  return {
    ok: true,
    value: {
      header: JSON.stringify(header, null, 2),
      payload: JSON.stringify(payload, null, 2),
      signature: parts[2],
      algorithm: typeof header?.alg === 'string' ? header.alg : 'unknown',
    },
  }
}
