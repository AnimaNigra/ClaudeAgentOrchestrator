import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import EmailTool from './EmailTool.vue'
import * as emailApi from '../../services/tools/emailApi.js'

vi.mock('../../services/tools/emailApi.js', () => ({
  parseEmail: vi.fn(),
  fetchAttachment: vi.fn(),
  saveBlob: vi.fn(),
}))

const EMAIL = {
  from: 'Jan Nemec <jan@example.cz>',
  to: ['petr@example.cz'],
  cc: [],
  bcc: [],
  subject: 'Příliš žluťoučký',
  date: '2026-08-23T10:00:00+02:00',
  textBody: 'tělo zprávy',
  htmlBodyRaw: '<p>ahoj</p>',
  htmlBodySanitized: '<p>ahoj</p>',
  unresolvedInlineImages: 0,
  attachments: [{ index: 0, fileName: 'pozn.txt', contentType: 'text/plain', sizeBytes: 13 }],
  headers: [{ name: 'Subject', value: 'x' }],
}

function pathInput(w) {
  return w.find('input[type="text"]')
}

describe('EmailTool', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('načte zprávu podle cesty a vypíše hlavičky', async () => {
    emailApi.parseEmail.mockResolvedValue(EMAIL)
    const w = mount(EmailTool)
    expect(w.find('h2').text()).toBe('E-mail')

    await pathInput(w).setValue('C:\\zpravy\\a.eml')
    await w.find('form').trigger('submit')
    await flushPromises()

    expect(emailApi.parseEmail).toHaveBeenCalledWith({ path: 'C:\\zpravy\\a.eml' })
    expect(w.text()).toContain('Jan Nemec <jan@example.cz>')
    expect(w.text()).toContain('Příliš žluťoučký')
    expect(w.text()).toContain('pozn.txt')
  })

  it('prázdnou cestu neodesílá', async () => {
    const w = mount(EmailTool)
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(emailApi.parseEmail).not.toHaveBeenCalled()
  })

  it('zobrazí chybu ze serveru a nevykreslí zprávu', async () => {
    emailApi.parseEmail.mockRejectedValue(new Error('Soubor nenalezen: C:\\x.eml'))
    const w = mount(EmailTool)

    await pathInput(w).setValue('C:\\x.eml')
    await w.find('form').trigger('submit')
    await flushPromises()

    expect(w.text()).toContain('Soubor nenalezen')
    expect(w.find('iframe').exists()).toBe(false)
  })

  it('po chybě a novém úspěšném načtení chyba zmizí', async () => {
    emailApi.parseEmail.mockRejectedValueOnce(new Error('Rozbité'))
    const w = mount(EmailTool)
    await pathInput(w).setValue('C:\\x.eml')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(w.text()).toContain('Rozbité')

    emailApi.parseEmail.mockResolvedValueOnce(EMAIL)
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(w.text()).not.toContain('Rozbité')
    expect(w.text()).toContain('Příliš žluťoučký')
  })

  it('načte přetažený soubor', async () => {
    emailApi.parseEmail.mockResolvedValue(EMAIL)
    const w = mount(EmailTool)
    const file = new File(['obsah'], 'zprava.eml')

    await w.find('section').trigger('drop', { dataTransfer: { files: [file] } })
    await flushPromises()

    expect(emailApi.parseEmail).toHaveBeenCalledWith({ file })
    expect(w.text()).toContain('Příliš žluťoučký')
  })

  it('drop bez souboru nic nevolá', async () => {
    const w = mount(EmailTool)
    await w.find('section').trigger('drop', { dataTransfer: { files: [] } })
    await flushPromises()
    expect(emailApi.parseEmail).not.toHaveBeenCalled()
  })

  it('stáhne přílohu ze stejného zdroje, ze kterého zpráva přišla', async () => {
    // Bezstavovost (spec §4.1): u nahraného souboru se soubor pošle znovu.
    emailApi.parseEmail.mockResolvedValue(EMAIL)
    const blob = new Blob(['data'])
    emailApi.fetchAttachment.mockResolvedValue(blob)

    const w = mount(EmailTool)
    const file = new File(['obsah'], 'zprava.eml')
    await w.find('section').trigger('drop', { dataTransfer: { files: [file] } })
    await flushPromises()

    await w.findAll('button').find(b => b.text() === 'Stáhnout').trigger('click')
    await flushPromises()

    expect(emailApi.fetchAttachment).toHaveBeenCalledWith({ file }, 0)
    expect(emailApi.saveBlob).toHaveBeenCalledWith(blob, 'pozn.txt')
  })

  it('chybu při stahování přílohy ukáže a zprávu nezahodí', async () => {
    emailApi.parseEmail.mockResolvedValue(EMAIL)
    emailApi.fetchAttachment.mockRejectedValue(new Error('Příloha s indexem 0 neexistuje.'))

    const w = mount(EmailTool)
    await pathInput(w).setValue('C:\\a.eml')
    await w.find('form').trigger('submit')
    await flushPromises()

    await w.findAll('button').find(b => b.text() === 'Stáhnout').trigger('click')
    await flushPromises()

    expect(w.text()).toContain('indexem 0')
    expect(w.text()).toContain('Příliš žluťoučký')
    expect(emailApi.saveBlob).not.toHaveBeenCalled()
  })

  it('výběr souboru přes dialog načte zprávu', async () => {
    emailApi.parseEmail.mockResolvedValue(EMAIL)
    const w = mount(EmailTool)
    const file = new File(['obsah'], 'vybrana.eml')
    const input = w.find('input[type="file"]')

    Object.defineProperty(input.element, 'files', { value: [file] })
    await input.trigger('change')
    await flushPromises()

    expect(emailApi.parseEmail).toHaveBeenCalledWith({ file })
  })
})
