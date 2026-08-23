import { describe, it, expect, vi, beforeEach } from 'vitest'
import * as api from './emailApi.js'

describe('emailApi', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  describe('parseEmail', () => {
    it('u cesty volá GET se zakódovaným parametrem', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({ subject: 'Ahoj' }),
      })
      const result = await api.parseEmail({ path: 'C:\\a b\\x.eml' })
      expect(result).toEqual({ subject: 'Ahoj' })
      expect(fetch).toHaveBeenCalledWith(
        '/api/tools/email/parse?path=C%3A%5Ca%20b%5Cx.eml'
      )
    })

    it('u souboru volá POST s multipart tělem', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({ subject: 'Z uploadu' }),
      })
      const file = new File(['obsah'], 'x.eml', { type: 'message/rfc822' })
      const result = await api.parseEmail({ file })

      expect(result).toEqual({ subject: 'Z uploadu' })
      const [url, init] = fetch.mock.calls[0]
      expect(url).toBe('/api/tools/email/parse')
      expect(init.method).toBe('POST')
      expect(init.body).toBeInstanceOf(FormData)
      expect(init.body.get('file')).toBe(file)
    })

    it('vyhodí chybu se zprávou ze serveru', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: false,
        status: 400,
        json: async () => ({ error: 'Soubor nenalezen: x.eml' }),
      })
      await expect(api.parseEmail({ path: 'x.eml' })).rejects.toThrow(/Soubor nenalezen/)
    })

    it('vyhodí obecnou chybu, když odpověď není JSON', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: false,
        status: 500,
        json: async () => { throw new Error('not json') },
      })
      await expect(api.parseEmail({ path: 'x.eml' })).rejects.toThrow(/500/)
    })
  })

  describe('fetchAttachment', () => {
    it('u cesty volá GET s cestou i indexem', async () => {
      const blob = new Blob(['data'])
      globalThis.fetch = vi.fn().mockResolvedValue({ ok: true, blob: async () => blob })

      const result = await api.fetchAttachment({ path: 'C:\\x.eml' }, 2)
      expect(result).toBe(blob)
      expect(fetch).toHaveBeenCalledWith(
        '/api/tools/email/attachment?path=C%3A%5Cx.eml&index=2'
      )
    })

    it('u souboru pošle soubor znovu spolu s indexem', async () => {
      // Bezstavovost ze spec §4.1 — server si nahraný soubor nedrží.
      const blob = new Blob(['data'])
      globalThis.fetch = vi.fn().mockResolvedValue({ ok: true, blob: async () => blob })
      const file = new File(['obsah'], 'x.eml')

      await api.fetchAttachment({ file }, 3)
      const [url, init] = fetch.mock.calls[0]
      expect(url).toBe('/api/tools/email/attachment')
      expect(init.method).toBe('POST')
      expect(init.body.get('file')).toBe(file)
      expect(init.body.get('index')).toBe('3')
    })

    it('vyhodí chybu se zprávou ze serveru', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: false,
        status: 404,
        json: async () => ({ error: 'Příloha s indexem 9 neexistuje.' }),
      })
      await expect(api.fetchAttachment({ path: 'x.eml' }, 9)).rejects.toThrow(/indexem 9/)
    })
  })

  describe('saveBlob', () => {
    it('vytvoří odkaz se jménem souboru a klikne na něj', () => {
      const click = vi.fn()
      const create = vi.spyOn(document, 'createElement')
      globalThis.URL.createObjectURL = vi.fn(() => 'blob:fake')
      globalThis.URL.revokeObjectURL = vi.fn()

      const anchor = document.createElement('a')
      anchor.click = click
      create.mockReturnValueOnce(anchor)

      api.saveBlob(new Blob(['x']), 'pozn.txt')

      expect(anchor.download).toBe('pozn.txt')
      expect(anchor.href).toContain('blob:fake')
      expect(click).toHaveBeenCalled()
      expect(document.body.contains(anchor)).toBe(false)
    })
  })
})
