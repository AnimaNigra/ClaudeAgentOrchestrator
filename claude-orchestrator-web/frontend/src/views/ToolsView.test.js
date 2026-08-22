import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createRouter, createWebHistory } from 'vue-router'
import ToolsView from './ToolsView.vue'

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
    expect(labels).toEqual(['Base64', 'URL', 'HTML', 'Basic Auth'])
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
})
