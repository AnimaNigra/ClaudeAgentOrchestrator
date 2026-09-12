import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
// Captures the hub callbacks so tests can drive the real event handler.
const hubHandlers = {}
vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: class {
    withUrl() { return this }
    withAutomaticReconnect() { return this }
    build() {
      return {
        on: (name, fn) => { hubHandlers[name] = fn },
        onreconnected() {},
        onclose() {},
        start: async () => {},
      }
    }
  },
}))

import { useAgentsStore } from './agents.js'
import { useSettingsStore } from './settings.js'

// Minimal stand-in for the Web Audio API, which jsdom does not implement.
// Records the gain the ding is scheduled at.
function installFakeAudio() {
  const gain = {
    connect: vi.fn(),
    gain: {
      setValueAtTime: vi.fn(),
      exponentialRampToValueAtTime: vi.fn(),
    },
  }
  const oscillators = []
  window.AudioContext = vi.fn(function () {
    this.state = 'running'
    this.currentTime = 0
    this.destination = {}
    this.resume = vi.fn()
    this.createGain = () => gain
    this.createOscillator = () => {
      const osc = { connect: vi.fn(), frequency: {}, type: '', start: vi.fn(), stop: vi.fn() }
      oscillators.push(osc)
      return osc
    }
  })
  return { gain, oscillators }
}

describe('agents store — ding volume', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('plays the ding at the volume from settings', async () => {
    const { gain, oscillators } = installFakeAudio()
    useSettingsStore().setVolume(100)
    await useAgentsStore().playDing('a1')
    expect(oscillators.length).toBe(2)
    expect(gain.gain.setValueAtTime).toHaveBeenCalledWith(0.5, expect.any(Number))
  })

  it('stays silent for an agent the user muted', async () => {
    const { gain, oscillators } = installFakeAudio()
    useSettingsStore().toggleAgentMute('a1')
    await useAgentsStore().playDing('a1')
    expect(oscillators.length).toBe(0)
    expect(gain.gain.setValueAtTime).not.toHaveBeenCalled()
  })

  it('still dings for the other agents', async () => {
    const { oscillators } = installFakeAudio()
    useSettingsStore().toggleAgentMute('a1')
    await useAgentsStore().playDing('a2')
    expect(oscillators.length).toBe(2)
  })

  it('stays silent for everything while global mute is on', async () => {
    const { oscillators } = installFakeAudio()
    useSettingsStore().toggleMuteAll()
    await useAgentsStore().playDing('a1')
    expect(oscillators.length).toBe(0)
  })
})

describe('agents store — removing an exited agent', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('drops the agent and stops remembering its mute', () => {
    const s = useAgentsStore()
    const settings = useSettingsStore()
    s.agents = { a1: { id: 'a1', name: 'one' }, a2: { id: 'a2', name: 'two' } }
    s.activeAgentId = 'a1'
    settings.toggleAgentMute('a1')

    s.removeAgent('a1')

    expect(s.agents.a1).toBeUndefined()
    expect(settings.isAgentMuted('a1')).toBe(false)
    expect(s.activeAgentId).toBe('a2')
  })

  it('leaves the active agent alone when a different one exits', () => {
    const s = useAgentsStore()
    s.agents = { a1: { id: 'a1' }, a2: { id: 'a2' } }
    s.activeAgentId = 'a1'

    s.removeAgent('a2')

    expect(s.activeAgentId).toBe('a1')
  })
})

describe('agents store — permission queue dings', () => {
  let audio

  async function connectedStore() {
    const s = useAgentsStore()
    await s.connect()
    s.agents = { a1: { id: 'a1', name: 'one' }, a2: { id: 'a2', name: 'two' } }
    return s
  }

  const request = (agentId, requestId) =>
    hubHandlers.AgentEvent({
      agentId,
      eventType: 'permission_request',
      data: { requestId, agentId, toolName: 'Bash', toolInput: {} },
      agent: null,
    })

  const settled = () => new Promise(resolve => setTimeout(resolve, 0))

  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    audio = installFakeAudio()
    global.Notification = { permission: 'denied' }
  })

  it('a muted agent sitting in the queue does not swallow another agent ding', async () => {
    await connectedStore()
    useSettingsStore().toggleAgentMute('a1')

    request('a1', 'r1')
    await settled()
    expect(audio.oscillators.length).toBe(0)

    request('a2', 'r2')
    await settled()
    expect(audio.oscillators.length).toBe(2)
  })

  it('dings only once while an audible request is already queued', async () => {
    await connectedStore()

    request('a1', 'r1')
    await settled()
    request('a2', 'r2')
    await settled()

    expect(audio.oscillators.length).toBe(2)
  })

  it('dings again for a new batch once the queue has been drained', async () => {
    const s = await connectedStore()

    request('a1', 'r1')
    await settled()
    s.pendingPermissions.shift()

    request('a2', 'r2')
    await settled()
    expect(audio.oscillators.length).toBe(4)
  })

  it('still dings an idle agent when a different agent is muted', async () => {
    await connectedStore()
    useSettingsStore().toggleAgentMute('a1')

    hubHandlers.AgentEvent({ agentId: 'a2', eventType: 'agent_status_changed', data: { status: 'idle' }, agent: null })
    await settled()
    expect(audio.oscillators.length).toBe(2)
  })
})
