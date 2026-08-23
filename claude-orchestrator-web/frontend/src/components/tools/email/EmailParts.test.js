import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import EmailHeaders from './EmailHeaders.vue'
import EmailBody from './EmailBody.vue'
import EmailAttachments from './EmailAttachments.vue'

const EMAIL = {
  from: 'Jan Nemec <jan@example.cz>',
  to: ['Petr <petr@example.cz>', 'Eva <eva@example.cz>'],
  cc: ['Karel <karel@example.cz>'],
  bcc: [],
  subject: 'Příliš žluťoučký',
  date: '2026-08-23T10:00:00+02:00',
  textBody: 'textová verze',
  htmlBodyRaw: '<p>ahoj</p><script>alert(1)<\/script>',
  htmlBodySanitized: '<p>ahoj</p>',
  unresolvedInlineImages: 0,
  attachments: [
    { index: 5, fileName: 'pozn.txt', contentType: 'text/plain', sizeBytes: 13 },
    { index: 2, fileName: 'logo.png', contentType: 'image/png', sizeBytes: 2048 },
  ],
  headers: [
    { name: 'Subject', value: '=?utf-8?B?…?=' },
    { name: 'X-Mailer', value: 'Outlook' },
  ],
}

describe('EmailHeaders', () => {
  it('vypíše odesílatele, příjemce, předmět a datum', () => {
    const w = mount(EmailHeaders, { props: { email: EMAIL } })
    expect(w.text()).toContain('Jan Nemec <jan@example.cz>')
    expect(w.text()).toContain('Petr <petr@example.cz>')
    expect(w.text()).toContain('Eva <eva@example.cz>')
    expect(w.text()).toContain('Karel <karel@example.cz>')
    expect(w.text()).toContain('Příliš žluťoučký')
  })

  it('vynechá prázdné řádky Cc a Bcc', () => {
    const w = mount(EmailHeaders, { props: { email: { ...EMAIL, cc: [], bcc: [] } } })
    expect(w.text()).not.toContain('Kopie')
    expect(w.text()).not.toContain('Skrytá kopie')
  })

  it('zobrazí všechny hlavičky v rozbalovací tabulce', () => {
    const w = mount(EmailHeaders, { props: { email: EMAIL } })
    expect(w.find('details').exists()).toBe(true)
    expect(w.findAll('details tbody tr')).toHaveLength(2)
    expect(w.text()).toContain('X-Mailer')
    expect(w.text()).toContain('Outlook')
  })

  it('naformátuje datum podle českého locale', () => {
    const w = mount(EmailHeaders, { props: { email: EMAIL } })
    // Fixtura má 2026-08-23T10:00:00+02:00. Neporovnává se celý řetězec —
    // toLocaleString se mezi verzemi ICU liší v mezerách a tečkách — ale rok,
    // měsíc i den tam být musí, a nesmí to být syrové ISO.
    const text = w.text()
    expect(text).toContain('2026')
    expect(text).toMatch(/8\.\s*|srpna/)
    expect(text).not.toContain('2026-08-23T10:00:00')
  })

  it('nečitelné datum vypíše tak, jak přišlo', () => {
    const w = mount(EmailHeaders, { props: { email: { ...EMAIL, date: 'tohle není datum' } } })
    expect(w.text()).toContain('tohle není datum')
  })
})

describe('EmailBody', () => {
  it('vykreslí HTML v izolovaném iframu s prázdným sandboxem', () => {
    const w = mount(EmailBody, { props: { email: EMAIL } })
    const frame = w.find('iframe')
    expect(frame.exists()).toBe(true)
    // Prázdný sandbox je nejpřísnější varianta (spec §5.2) — kdyby sem někdo
    // dopsal allow-scripts, spadne tenhle test.
    expect(frame.attributes('sandbox')).toBe('')
    expect(frame.attributes('srcdoc')).toBe('<p>ahoj</p>')
  })

  it('do iframu nikdy nepustí syrové HTML', () => {
    const w = mount(EmailBody, { props: { email: EMAIL } })
    expect(w.find('iframe').attributes('srcdoc')).not.toContain('script')
  })

  it('tab text ukáže textovou verzi', async () => {
    const w = mount(EmailBody, { props: { email: EMAIL } })
    await w.findAll('button').find(b => b.text() === 'text').trigger('click')
    expect(w.find('iframe').exists()).toBe(false)
    expect(w.find('pre').text()).toBe('textová verze')
  })

  it('tab source ukáže syrové HTML jako text, ne jako dokument', async () => {
    const w = mount(EmailBody, { props: { email: EMAIL } })
    await w.findAll('button').find(b => b.text() === 'source').trigger('click')
    expect(w.find('iframe').exists()).toBe(false)
    expect(w.find('pre').text()).toContain('<script>alert(1)</script>')
  })

  it('bez HTML těla nabídne rovnou text', () => {
    const w = mount(EmailBody, {
      props: { email: { ...EMAIL, htmlBodySanitized: null, htmlBodyRaw: null } },
    })
    expect(w.find('iframe').exists()).toBe(false)
    expect(w.find('pre').text()).toBe('textová verze')
    expect(w.findAll('button').map(b => b.text())).toEqual(['text'])
  })

  it('upozorní na nerozřešené inline obrázky', () => {
    const w = mount(EmailBody, { props: { email: { ...EMAIL, unresolvedInlineImages: 2 } } })
    expect(w.text()).toContain('2')
    expect(w.text()).toContain('nepodařilo dohledat')
  })

  it('bez nerozřešených obrázků neupozorňuje', () => {
    const w = mount(EmailBody, { props: { email: EMAIL } })
    expect(w.text()).not.toContain('nepodařilo dohledat')
  })
})

describe('EmailAttachments', () => {
  it('vypíše přílohy se jménem, typem a čitelnou velikostí', () => {
    const w = mount(EmailAttachments, { props: { attachments: EMAIL.attachments } })
    expect(w.text()).toContain('pozn.txt')
    expect(w.text()).toContain('text/plain')
    expect(w.text()).toContain('13 B')
    expect(w.text()).toContain('logo.png')
    expect(w.text()).toContain('2,0 kB')
  })

  it('emituje download s indexem přílohy, ne s pořadím v seznamu', async () => {
    const w = mount(EmailAttachments, { props: { attachments: EMAIL.attachments } })
    // Druhý řádek má index 2. Kdyby komponenta posílala pozici v poli,
    // přišla by 1 a tenhle test spadne — o to jde.
    await w.findAll('button')[1].trigger('click')
    expect(w.emitted('download')).toEqual([[2]])
  })

  it('u hranice mezi jednotkami zvolí správnou jednotku', () => {
    const w = mount(EmailAttachments, {
      props: {
        attachments: [
          { index: 0, fileName: 'a.bin', contentType: 'application/octet-stream', sizeBytes: 999_960 },
          { index: 1, fileName: 'b.bin', contentType: 'application/octet-stream', sizeBytes: 999_000 },
        ],
      },
    })
    expect(w.text()).toContain('1,0 MB')
    expect(w.text()).not.toContain('1000,0 kB')
    expect(w.text()).toContain('999,0 kB')
  })

  it('bez příloh se nevykreslí vůbec', () => {
    const w = mount(EmailAttachments, { props: { attachments: [] } })
    expect(w.find('button').exists()).toBe(false)
    expect(w.text()).toBe('')
  })
})
