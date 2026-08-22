import { describe, it, expect } from 'vitest'
import { decodeJwt, verifyJwt, VERIFY } from './jwt.js'

// HS256 token vygenerovaný node:crypto, tajemství 'orchestrator-test-secret'.
const HS256_TOKEN =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkphbiBOZW1lYyIsImlhdCI6MTUxNjIzOTAyMn0.keAAZPonJ3eodJzQoZTnC42n6QqyLCANJsfjUuqnUJ4'

describe('decodeJwt', () => {
  it('rozloží token na odsazenou hlavičku, payload, podpis a algoritmus', () => {
    const { ok, value } = decodeJwt(HS256_TOKEN)
    expect(ok).toBe(true)
    expect(value.algorithm).toBe('HS256')
    expect(JSON.parse(value.header)).toEqual({ alg: 'HS256', typ: 'JWT' })
    expect(JSON.parse(value.payload)).toEqual({ sub: '1234567890', name: 'Jan Nemec', iat: 1516239022 })
    expect(value.header).toContain('\n')
    expect(value.signature).toBe(HS256_TOKEN.split('.')[2])
  })

  it('u prázdného vstupu vrátí null, ne chybu', () => {
    expect(decodeJwt('')).toEqual({ ok: true, value: null })
    expect(decodeJwt('   ')).toEqual({ ok: true, value: null })
  })

  it('odmítne jiný počet částí než tři', () => {
    expect(decodeJwt('a.b')).toEqual({
      ok: false,
      error: 'Not a valid JWT (expected 3 dot-separated parts).',
    })
  })

  it('odmítne část, která není validní JSON', () => {
    expect(decodeJwt('bm90anNvbg.bm90anNvbg.x')).toEqual({
      ok: false,
      error: 'Token header or payload is not valid JSON.',
    })
  })

  it('u chybějícího alg vrátí "unknown" místo pádu', () => {
    // {"typ":"JWT"} bez alg
    const token = 'eyJ0eXAiOiJKV1QifQ.eyJhIjoxfQ.x'
    expect(decodeJwt(token).value.algorithm).toBe('unknown')
  })

  it('u non-string vstupu vrátí null místo pádu', () => {
    expect(decodeJwt(123)).toEqual({ ok: true, value: null })
    expect(decodeJwt({})).toEqual({ ok: true, value: null })
    expect(decodeJwt(['x'])).toEqual({ ok: true, value: null })
  })

  it('odmítne nevalidní Base64URL kódování', () => {
    expect(decodeJwt('a!b.eyJhIjoxfQ.x')).toEqual({
      ok: false,
      error: 'Invalid Base64URL encoding in token.',
    })
  })
})

// ES256 token + veřejný klíč vygenerované node:crypto (P-256, ieee-p1363 podpis).
const ES256_TOKEN =
  'eyJhbGciOiJFUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkphbiBOZW1lYyIsImlhdCI6MTUxNjIzOTAyMn0.ZMjCmu39GJ8nYjVGDCMV6Qpgz0P1Y4zq38bdbJK3VsDz1nR_qyCX6WqAn0k36fZHxJq-0Km5w1HNwH50MRrMtA'

const ES256_PEM = `-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE1gJeJ7FhzMGVzuEhJQBodqxi4909
1fanhzE4Tnfvx/u2cwtfolUQPuJpQuCm1byQNVPQC01D+4OUiv9vdZsdcA==
-----END PUBLIC KEY-----`

const NONE_TOKEN =
  'eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkphbiBOZW1lYyIsImlhdCI6MTUxNjIzOTAyMn0.'

// POZOR: nepoškozuj poslední znak podpisu. U 64bajtového ES256 podpisu nese
// poslední base64url znak jen 2 platné bity ze 6 — zbytek se při dekódování
// zahodí, takže záměna 'A' za 'B' vrátí identické bajty a podpis pořád projde.
// Ověřeno měřením. Poškozuj první znak.
function tamperSignature(token) {
  const [header, payload, signature] = token.split('.')
  const first = signature[0] === 'a' ? 'b' : 'a'
  return `${header}.${payload}.${first}${signature.slice(1)}`
}

describe('verifyJwt', () => {
  it('ověří platný HS256 podpis', async () => {
    expect(await verifyJwt(HS256_TOKEN, 'orchestrator-test-secret')).toBe(VERIFY.VERIFIED)
  })

  it('odmítne špatné tajemství', async () => {
    expect(await verifyJwt(HS256_TOKEN, 'spatne')).toBe(VERIFY.INVALID)
  })

  it('odmítne poškozený HS256 podpis', async () => {
    expect(await verifyJwt(tamperSignature(HS256_TOKEN), 'orchestrator-test-secret'))
      .toBe(VERIFY.INVALID)
  })

  it('bez klíče hlásí key-required', async () => {
    expect(await verifyJwt(HS256_TOKEN, '')).toBe(VERIFY.KEY_REQUIRED)
    expect(await verifyJwt(HS256_TOKEN, '   ')).toBe(VERIFY.KEY_REQUIRED)
  })

  it('ověří ES256 podpis z PEM veřejného klíče', async () => {
    expect(await verifyJwt(ES256_TOKEN, ES256_PEM)).toBe(VERIFY.VERIFIED)
  })

  it('odmítne poškozený ES256 podpis', async () => {
    expect(await verifyJwt(tamperSignature(ES256_TOKEN), ES256_PEM)).toBe(VERIFY.INVALID)
  })

  it('nerozbije se na nesmyslném klíči', async () => {
    expect(await verifyJwt(ES256_TOKEN, 'tohle není PEM')).toBe(VERIFY.INVALID)
  })

  it('alg none je nepodporovaný, ne platný', async () => {
    expect(await verifyJwt(NONE_TOKEN, 'cokoliv')).toBe(VERIFY.UNSUPPORTED)
  })

  it('u prázdného tokenu hlásí not-attempted', async () => {
    expect(await verifyJwt('', 'klic')).toBe(VERIFY.NOT_ATTEMPTED)
  })
})
