import { encoder, decoder, base64ToBytes } from './bytes.js'

// base64url -> base64: jiná abeceda a chybějící výplň.
function base64UrlToBytes(part) {
  const partStr = typeof part === 'string' ? part : ''
  const base64 = partStr.replace(/-/g, '+').replace(/_/g, '/')
  return base64ToBytes(base64 + '='.repeat((4 - (base64.length % 4)) % 4))
}

export function decodeJwt(token) {
  const tokenStr = typeof token === 'string' ? token : ''
  const trimmed = tokenStr.trim()
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

export const VERIFY = {
  NOT_ATTEMPTED: 'not-attempted',
  KEY_REQUIRED: 'key-required',
  VERIFIED: 'verified',
  INVALID: 'invalid',
  UNSUPPORTED: 'unsupported',
}

const HMAC_HASHES = { HS256: 'SHA-256', HS384: 'SHA-384', HS512: 'SHA-512' }
const RSA_HASHES = { RS256: 'SHA-256', RS384: 'SHA-384', RS512: 'SHA-512' }
const EC_PARAMS = {
  ES256: { namedCurve: 'P-256', hash: 'SHA-256' },
  ES384: { namedCurve: 'P-384', hash: 'SHA-384' },
  ES512: { namedCurve: 'P-521', hash: 'SHA-512' },
}

// PEM je jen base64 mezi hlavičkou a patičkou. Převod na bajty dělá sdílený
// helper z bytes.js — tenhle soubor už ho používá pro base64UrlToBytes.
function pemToBytes(pem) {
  const body = pem.replace(/-----(BEGIN|END)[^-]*-----/g, '').replace(/\s+/g, '')
  return base64ToBytes(body)
}

export async function verifyJwt(token, key) {
  const decoded = decodeJwt(token)
  if (!decoded.ok || !decoded.value) return VERIFY.NOT_ATTEMPTED

  const trimmedKey = typeof key === 'string' ? key.trim() : ''
  if (!trimmedKey) return VERIFY.KEY_REQUIRED

  const algorithm = decoded.value.algorithm

  // Podpora algoritmu se rozhoduje dřív, než se sáhne na podpis, aby algoritmy
  // mimo tabulky hlásily UNSUPPORTED, ne INVALID.
  if (!Object.hasOwn(HMAC_HASHES, algorithm) &&
      !Object.hasOwn(RSA_HASHES, algorithm) &&
      !Object.hasOwn(EC_PARAMS, algorithm)) {
    return VERIFY.UNSUPPORTED
  }

  const parts = token.trim().split('.')
  const signed = encoder.encode(`${parts[0]}.${parts[1]}`)

  let signature
  try {
    signature = base64UrlToBytes(parts[2])
  } catch {
    return VERIFY.INVALID
  }

  // Cokoliv se uvnitř pokazí — vadný PEM, špatná křivka, nesouhlasná délka —
  // je pro uživatele "podpis neplatí", ne stack trace.
  try {
    if (Object.hasOwn(HMAC_HASHES, algorithm)) {
      const cryptoKey = await crypto.subtle.importKey(
        'raw', encoder.encode(key),
        { name: 'HMAC', hash: HMAC_HASHES[algorithm] }, false, ['verify'])
      return await crypto.subtle.verify('HMAC', cryptoKey, signature, signed)
        ? VERIFY.VERIFIED : VERIFY.INVALID
    }

    if (Object.hasOwn(RSA_HASHES, algorithm)) {
      const cryptoKey = await crypto.subtle.importKey(
        'spki', pemToBytes(key),
        { name: 'RSASSA-PKCS1-v1_5', hash: RSA_HASHES[algorithm] }, false, ['verify'])
      return await crypto.subtle.verify('RSASSA-PKCS1-v1_5', cryptoKey, signature, signed)
        ? VERIFY.VERIFIED : VERIFY.INVALID
    }

    if (Object.hasOwn(EC_PARAMS, algorithm)) {
      const { namedCurve, hash } = EC_PARAMS[algorithm]
      const cryptoKey = await crypto.subtle.importKey(
        'spki', pemToBytes(key),
        { name: 'ECDSA', namedCurve }, false, ['verify'])
      return await crypto.subtle.verify({ name: 'ECDSA', hash }, cryptoKey, signature, signed)
        ? VERIFY.VERIFIED : VERIFY.INVALID
    }
  } catch {
    return VERIFY.INVALID
  }
}
