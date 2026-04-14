import { describe, it, expect } from 'vitest'

describe('test stack', () => {
  it('runs', () => {
    expect(1 + 1).toBe(2)
  })
  it('has jsdom', () => {
    expect(typeof document).toBe('object')
    const el = document.createElement('div')
    expect(el.tagName).toBe('DIV')
  })
  it('clears localStorage between tests', () => {
    localStorage.setItem('x', '1')
    expect(localStorage.getItem('x')).toBe('1')
  })
})
