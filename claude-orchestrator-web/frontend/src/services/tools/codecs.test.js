import { describe, it, expect } from 'vitest'
import {
  encodeBase64, decodeBase64,
  encodeUrl, decodeUrl,
  encodeHtml, decodeHtml,
  buildBasicAuth,
} from './codecs.js'

// Vektory vygenerované Node.js: Buffer.from(text, 'utf8').toString('base64')
const CZECH = 'Příliš žluťoučký kůň'
const CZECH_B64 = 'UMWZw61sacWhIMW+bHXFpW91xI1rw70ga8WvxYg='
const EMOJI = '🎉 hotovo'
const EMOJI_B64 = '8J+OiSBob3Rvdm8='

describe('base64', () => {
  // Tohle je ten důvod, proč nestačí holé btoa() — btoa('č') vyhodí InvalidCharacterError.
  it('zakóduje diakritiku přes UTF-8, ne latin1', () => {
    expect(encodeBase64(CZECH)).toEqual({ ok: true, value: CZECH_B64 })
  })

  it('zakóduje znaky mimo BMP (emoji)', () => {
    expect(encodeBase64(EMOJI)).toEqual({ ok: true, value: EMOJI_B64 })
  })

  it('dekóduje zpět na původní text', () => {
    expect(decodeBase64(CZECH_B64)).toEqual({ ok: true, value: CZECH })
    expect(decodeBase64(EMOJI_B64)).toEqual({ ok: true, value: EMOJI })
  })

  it('ignoruje bílé znaky ve vstupu', () => {
    expect(decodeBase64('8J+OiSBo b3Rvdm8=\n')).toEqual({ ok: true, value: EMOJI })
  })

  it('vrátí chybu na nevalidním base64', () => {
    expect(decodeBase64('!!!not base64!!!')).toEqual({ ok: false, error: 'Invalid Base64' })
  })

  it('na prázdný vstup vrátí prázdný výstup, ne chybu', () => {
    expect(encodeBase64('')).toEqual({ ok: true, value: '' })
    expect(decodeBase64('')).toEqual({ ok: true, value: '' })
  })
})

describe('url', () => {
  it('escapuje i !\'()* kvůli parity s Uri.EscapeDataString', () => {
    expect(encodeUrl("a b!'()*~-_.")).toEqual({ ok: true, value: 'a%20b%21%27%28%29%2A~-_.' })
  })

  it('projde tam a zpět', () => {
    expect(decodeUrl(encodeUrl('a b&c=d').value)).toEqual({ ok: true, value: 'a b&c=d' })
  })

  it('vrátí chybu na useknuté escape sekvenci', () => {
    expect(decodeUrl('%E0%A4')).toEqual({ ok: false, error: 'Invalid URL encoding' })
  })
})

describe('html', () => {
  it('escapuje pět znaků stejně jako WebUtility.HtmlEncode', () => {
    expect(encodeHtml(`<a href="x">&'`)).toEqual({
      ok: true,
      value: '&lt;a href=&quot;x&quot;&gt;&amp;&#39;',
    })
  })

  it('nechá diakritiku být (vědomá odchylka od .NET)', () => {
    expect(encodeHtml('kůň')).toEqual({ ok: true, value: 'kůň' })
  })

  it('dekóduje pojmenované i číselné entity', () => {
    expect(decodeHtml('&lt;a&gt;&#39;&amp;')).toEqual({ ok: true, value: `<a>'&` })
  })

  it('dekóduje &nbsp; na nedělitelnou mezeru, ne na obyčejnou', () => {
    // Escape \u00A0 schválně — obyčejná mezera by v testu vypadala stejně a test
    // by tiše procházel, i kdyby dekódování bylo špatně.
    expect(decodeHtml('a&nbsp;b')).toEqual({ ok: true, value: 'a\u00A0b' })
  })
})

describe('basicAuth', () => {
  it('sestaví token i celou hlavičku', () => {
    expect(buildBasicAuth('admin', 'heslo123')).toEqual({
      ok: true,
      value: {
        token: 'YWRtaW46aGVzbG8xMjM=',
        header: 'Authorization: Basic YWRtaW46aGVzbG8xMjM=',
      },
    })
  })

  it('zvládne diakritiku v hesle', () => {
    expect(buildBasicAuth('jan', 'tajné').value.token).toBe('amFuOnRham7DqQ==')
  })

  it('prázdné heslo je platný vstup, ne chyba', () => {
    expect(buildBasicAuth('admin', '').ok).toBe(true)
  })
})
