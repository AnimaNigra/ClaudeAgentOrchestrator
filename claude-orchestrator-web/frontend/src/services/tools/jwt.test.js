import { describe, it, expect } from 'vitest'
import { decodeJwt } from './jwt.js'

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
})
