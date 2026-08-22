import { describe, it, expect } from 'vitest'
import { hashText, HASH_ALGORITHMS } from './hash.js'

// Publikované vektory pro "abc", ověřené proti node:crypto.
const ABC = {
  'SHA-1': 'a9993e364706816aba3e25717850c26c9cd0d89d',
  'SHA-256': 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad',
  'SHA-384': 'cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7',
  'SHA-512': 'ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f',
}

describe('hashText', () => {
  it.each(HASH_ALGORITHMS)('%s odpovídá publikovanému vektoru pro "abc"', async alg => {
    expect(await hashText('abc', alg)).toEqual({ ok: true, value: ABC[alg] })
  })

  it('hashuje UTF-8 bajty, ne latin1', async () => {
    const result = await hashText('Příliš žluťoučký kůň', 'SHA-256')
    expect(result).toEqual({
      ok: true,
      value: 'c138196469ecb343ba5d804e06a0d6e39315cba3ca1c59fd1d2fe643622ed89d',
    })
  })

  it('na prázdný vstup vrátí prázdný výstup, ne hash prázdného řetězce', async () => {
    expect(await hashText('', 'SHA-256')).toEqual({ ok: true, value: '' })
  })

  // MD5 je mimo rozsah — SubtleCrypto ho neumí a nikdy nebude.
  it('odmítne neznámý algoritmus, ale nezamítne Promise', async () => {
    const result = await hashText('x', 'MD5')
    expect(result.ok).toBe(false)
    expect(result.error).toContain('MD5')
  })
})
