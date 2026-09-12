import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import AgentCard from './AgentCard.vue'
import { useSettingsStore } from '../stores/settings.js'

function mountCard(agent = { id: 'a1', name: 'one', status: 'Running' }) {
  return mount(AgentCard, { props: { agent, isActive: false } })
}

describe('AgentCard — ztlumení agenta', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('kliknutí na zvonek ztlumí právě tohoto agenta', async () => {
    const settings = useSettingsStore()
    const wrapper = mountCard()

    await wrapper.find('button[aria-label="Mute notification sound"]').trigger('click')

    expect(settings.isAgentMuted('a1')).toBe(true)
    expect(settings.isAgentMuted('a2')).toBe(false)
  })

  it('u ztlumeného agenta nabízí odtlumení', async () => {
    const settings = useSettingsStore()
    settings.toggleAgentMute('a1')
    const wrapper = mountCard()

    const btn = wrapper.find('button[aria-label="Unmute notification sound"]')
    expect(btn.text()).toBe('🔕')
    await btn.trigger('click')
    expect(settings.isAgentMuted('a1')).toBe(false)
  })

  it('ztlumení nevybere agenta jako aktivního', async () => {
    const wrapper = mountCard()
    await wrapper.find('button[aria-label="Mute notification sound"]').trigger('click')
    expect(wrapper.emitted('select')).toBeUndefined()
  })
})
