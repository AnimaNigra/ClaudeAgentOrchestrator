import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import SettingsView from './SettingsView.vue'
import { useSettingsStore } from '../stores/settings.js'
import { useAgentsStore } from '../stores/agents.js'

function byText(wrapper, text) {
  return wrapper.findAll('button').find(b => b.text().includes(text))
}

describe('SettingsView — zvuk', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('ukazuje aktuální hlasitost', () => {
    useSettingsStore().setVolume(70)
    const wrapper = mount(SettingsView)
    expect(wrapper.find('input[type="range"]').element.value).toBe('70')
    expect(wrapper.text()).toContain('70 %')
  })

  it('posunutí slideru uloží hlasitost', async () => {
    const settings = useSettingsStore()
    const wrapper = mount(SettingsView)
    const slider = wrapper.find('input[type="range"]')
    slider.element.value = '25'
    await slider.trigger('input')
    expect(settings.volumePct).toBe(25)
  })

  it('přepínač ztlumí všechny zvuky', async () => {
    const settings = useSettingsStore()
    const wrapper = mount(SettingsView)
    await wrapper.find('input[type="checkbox"]').trigger('change')
    expect(settings.mutedAll).toBe(true)
  })

  it('ukázka přehraje ding aktuální hlasitostí', async () => {
    const agents = useAgentsStore()
    const spy = vi.spyOn(agents, 'playDing').mockResolvedValue()
    const wrapper = mount(SettingsView)
    await byText(wrapper, 'Play test sound').trigger('click')
    expect(spy).toHaveBeenCalledWith(null)
  })

  it('vypíše ztlumené agenty a umí je odtlumit najednou', async () => {
    const settings = useSettingsStore()
    const agents = useAgentsStore()
    agents.agents = { a1: { id: 'a1', name: 'alpha' }, a2: { id: 'a2', name: 'beta' } }
    settings.toggleAgentMute('a1')
    settings.toggleAgentMute('a2')
    const wrapper = mount(SettingsView)

    expect(wrapper.text()).toContain('alpha')
    expect(wrapper.text()).toContain('beta')
    await byText(wrapper, 'Unmute all').trigger('click')
    expect(settings.mutedAgentIds).toEqual([])
  })

  it('bez ztlumených agentů nenabízí hromadné odtlumení', () => {
    const wrapper = mount(SettingsView)
    expect(byText(wrapper, 'Unmute all')).toBeUndefined()
  })
})
