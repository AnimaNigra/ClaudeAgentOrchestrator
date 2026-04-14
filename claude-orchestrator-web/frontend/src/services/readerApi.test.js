import { describe, it, expect, vi, beforeEach } from 'vitest'
import * as api from './readerApi.js'

describe('readerApi', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  describe('getContent', () => {
    it('returns parsed payload on success', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({ path: '/x.md', content: '# hi', mtime: 1 }),
      })
      const result = await api.getContent('/x.md')
      expect(result).toEqual({ path: '/x.md', content: '# hi', mtime: 1 })
      expect(fetch).toHaveBeenCalledWith(
        '/api/reader/content?path=%2Fx.md'
      )
    })

    it('throws with error message on 4xx/5xx', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: false,
        status: 404,
        json: async () => ({ error: 'File not found', path: '/x.md' }),
      })
      await expect(api.getContent('/x.md')).rejects.toThrow(/File not found/)
    })

    it('throws generic error if response not JSON', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: false,
        status: 500,
        json: async () => { throw new Error('not json') },
      })
      await expect(api.getContent('/x.md')).rejects.toThrow(/500/)
    })
  })

  describe('rawUrl', () => {
    it('builds URL with encoded path', () => {
      expect(api.rawUrl('/a b/c.png')).toBe(
        '/api/reader/raw?path=%2Fa%20b%2Fc.png'
      )
    })
  })

  describe('watch / unwatch', () => {
    it('POSTs watch body', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => ({}) })
      await api.watch('/x.md')
      expect(fetch).toHaveBeenCalledWith('/api/reader/watch', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ path: '/x.md' }),
      })
    })

    it('POSTs unwatch body', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => ({}) })
      await api.unwatch('/x.md')
      expect(fetch).toHaveBeenCalledWith('/api/reader/unwatch', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ path: '/x.md' }),
      })
    })
  })
})
