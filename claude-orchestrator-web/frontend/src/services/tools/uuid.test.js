import { describe, it, expect } from 'vitest'
import { generateUuids } from './uuid.js'

const V4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/

describe('generateUuids', () => {
  it('vrátí jeden UUID ve tvaru v4', () => {
    const { ok, value } = generateUuids(1)
    expect(ok).toBe(true)
    expect(value).toHaveLength(1)
    expect(value[0]).toMatch(V4)
  })

  it('vrátí požadovaný počet různých hodnot', () => {
    const { value } = generateUuids(10)
    expect(value).toHaveLength(10)
    expect(new Set(value).size).toBe(10)
  })

  it('ořízne počet do rozsahu 1..100 místo hlášení chyby', () => {
    expect(generateUuids(0).value).toHaveLength(1)
    expect(generateUuids(-5).value).toHaveLength(1)
    expect(generateUuids(1000).value).toHaveLength(100)
    expect(generateUuids('nesmysl').value).toHaveLength(1)
  })
})
