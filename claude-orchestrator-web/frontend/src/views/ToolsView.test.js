import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createWebHistory } from 'vue-router'
import ToolsView from './ToolsView.vue'

// Tentýž HS256 token a tajemství, proti kterým se testuje jwt.js.
const HS256_TOKEN =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkphbiBOZW1lYyIsImlhdCI6MTUxNjIzOTAyMn0.keAAZPonJ3eodJzQoZTnC42n6QqyLCANJsfjUuqnUJ4'

function makeRouter() {
  return createRouter({
    history: createWebHistory(),
    routes: [{ path: '/tools/:tool?', component: ToolsView }],
  })
}

async function mountAt(path) {
  const router = makeRouter()
  router.push(path)
  await router.isReady()
  return mount(ToolsView, { global: { plugins: [router] } })
}

describe('ToolsView', () => {
  it('vykreslí odkaz na každý nástroj', async () => {
    const wrapper = await mountAt('/tools/base64')
    const labels = wrapper.findAll('aside a').map(l => l.text())
    expect(labels).toEqual(['Base64', 'URL', 'HTML', 'Basic Auth', 'JWT', 'UUID', 'Hash'])
  })

  it('přepnutí slugu vymění panel', async () => {
    const wrapper = await mountAt('/tools/basic-auth')
    expect(wrapper.find('h2').text()).toBe('Basic Auth')
  })

  it('vybere nástroj podle parametru v route', async () => {
    const wrapper = await mountAt('/tools/base64')
    expect(wrapper.find('h2').text()).toBe('Base64')
  })

  it('u neznámého slugu spadne zpět na první nástroj', async () => {
    const wrapper = await mountAt('/tools/neexistuje')
    expect(wrapper.find('h2').text()).toBe('Base64')
  })

  it('u chybějícího slugu spadne zpět na první nástroj', async () => {
    const wrapper = await mountAt('/tools')
    expect(wrapper.find('h2').text()).toBe('Base64')
  })

  it('URL nástroj se připojí s správným nadpisem', async () => {
    const wrapper = await mountAt('/tools/url')
    expect(wrapper.find('h2').text()).toBe('URL')
  })

  it('URL nástroj kóduje vstup', async () => {
    const wrapper = await mountAt('/tools/url')
    const textareas = wrapper.findAll('textarea')
    await textareas[0].setValue('a b&c')
    await wrapper.vm.$nextTick()
    expect(textareas[1].element.value).toBe('a%20b%26c')
  })

  it('HTML nástroj se připojí s správným nadpisem', async () => {
    const wrapper = await mountAt('/tools/html')
    expect(wrapper.find('h2').text()).toBe('HTML')
  })

  it('HTML nástroj kóduje vstup', async () => {
    const wrapper = await mountAt('/tools/html')
    const textareas = wrapper.findAll('textarea')
    await textareas[0].setValue('<b>&x')
    await wrapper.vm.$nextTick()
    expect(textareas[1].element.value).toBe('&lt;b&gt;&amp;x')
  })

  it('Basic Auth nástroj počítá token a hlavičku', async () => {
    const wrapper = await mountAt('/tools/basic-auth')
    const inputs = wrapper.findAll('input')
    await inputs[0].setValue('user')
    await inputs[1].setValue('pass')
    await wrapper.vm.$nextTick()
    const codes = wrapper.findAll('code')
    expect(codes[0].text()).toBe('dXNlcjpwYXNz')
    expect(codes[1].text()).toBe('Authorization: Basic dXNlcjpwYXNz')
  })

  it('JWT nástroj dekóduje vložený token', async () => {
    const wrapper = await mountAt('/tools/jwt')
    expect(wrapper.find('h2').text()).toBe('JWT')

    await wrapper.find('textarea').setValue(HS256_TOKEN)
    await flushPromises()

    expect(wrapper.text()).toContain('HS256')
    expect(wrapper.text()).toContain('Jan Nemec')
  })

  it('JWT nástroj ověří podpis správným tajemstvím a odmítne špatné', async () => {
    const wrapper = await mountAt('/tools/jwt')
    await wrapper.find('textarea').setValue(HS256_TOKEN)
    await flushPromises()

    // Druhá textarea je pole pro klíč; objeví se až po úspěšném dekódování.
    const keyField = wrapper.findAll('textarea')[1]
    await keyField.setValue('orchestrator-test-secret')
    // crypto.subtle.importKey/verify jsou skutečné asynchronní operace (běží
    // mimo mikrotaskovou frontu), takže jedno flushPromises() je nespolehlivé
    // — vi.waitFor opakuje kontrolu, dokud ověření doopravdy nedoběhne.
    await vi.waitFor(async () => {
      await flushPromises()
      expect(wrapper.text()).toContain('podpis platí')
    })

    await keyField.setValue('spatne')
    await vi.waitFor(async () => {
      await flushPromises()
      expect(wrapper.text()).toContain('podpis neplatí')
    })
  })

  it('JWT nástroj upozorní, když HS token ověřuje proti PEM klíči', async () => {
    const wrapper = await mountAt('/tools/jwt')
    expect(wrapper.text()).toContain('SPKI')

    await wrapper.find('textarea').setValue(HS256_TOKEN)
    await flushPromises()

    const keyField = wrapper.findAll('textarea')[1]
    await keyField.setValue('-----BEGIN PUBLIC KEY-----\nMFw=\n-----END PUBLIC KEY-----')
    await flushPromises()
    expect(wrapper.text()).toContain('vypadá jako PEM')

    await keyField.setValue('obycejne-tajemstvi')
    await flushPromises()
    expect(wrapper.text()).not.toContain('vypadá jako PEM')
  })

  it('UUID nástroj vygeneruje požadovaný počet hodnot', async () => {
    const wrapper = await mountAt('/tools/uuid')
    expect(wrapper.find('h2').text()).toBe('UUID')

    // Generuje se v onMounted, takže jedna hodnota tu je hned.
    expect(wrapper.findAll('li')).toHaveLength(1)

    await wrapper.find('input[type="number"]').setValue(3)
    await wrapper.find('button').trigger('click')
    await wrapper.vm.$nextTick()

    const values = wrapper.findAll('li code').map(c => c.text())
    expect(values).toHaveLength(3)
    expect(new Set(values).size).toBe(3)
    expect(values[0]).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/)
  })

  it('Hash nástroj počítá SHA-256 a přepíná algoritmus', async () => {
    const wrapper = await mountAt('/tools/hash')
    expect(wrapper.find('h2').text()).toBe('Hash')

    await wrapper.find('textarea').setValue('abc')
    await flushPromises()
    // Publikovaný vektor pro SHA-256("abc").
    expect(wrapper.text()).toContain('ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad')

    const sha1Button = wrapper.findAll('button').find(b => b.text() === 'SHA-1')
    await sha1Button.trigger('click')
    await flushPromises()
    // Publikovaný vektor pro SHA-1("abc") — jiná délka i hodnota, takže
    // přepínač je prokazatelně funkční.
    expect(wrapper.text()).toContain('a9993e364706816aba3e25717850c26c9cd0d89d')
  })
})
