import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useSettingsStore } from './settings.js'

const KEY = 'claude-orchestrator-sound-v1'

describe('settings store — defaults', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('starts at 50 % volume, unmuted, with no muted agents', () => {
    const s = useSettingsStore()
    expect(s.volumePct).toBe(50)
    expect(s.mutedAll).toBe(false)
    expect(s.mutedAgentIds).toEqual([])
  })

  it('default volume reproduces the historical 0.25 gain', () => {
    const s = useSettingsStore()
    expect(s.gainFor('a1')).toBeCloseTo(0.25)
  })

  it('gains nothing extra for a ding without an agent', () => {
    const s = useSettingsStore()
    expect(s.gainFor(null)).toBeCloseTo(0.25)
  })
})

describe('settings store — volume', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('scales gain linearly up to 0.5 at 100 %', () => {
    const s = useSettingsStore()
    s.setVolume(100)
    expect(s.gainFor('a1')).toBeCloseTo(0.5)
  })

  it('is silent at 0 %', () => {
    const s = useSettingsStore()
    s.setVolume(0)
    expect(s.gainFor('a1')).toBe(0)
  })

  it('clamps and rounds out-of-range input', () => {
    const s = useSettingsStore()
    s.setVolume(140)
    expect(s.volumePct).toBe(100)
    s.setVolume(-20)
    expect(s.volumePct).toBe(0)
    s.setVolume(33.6)
    expect(s.volumePct).toBe(34)
  })

  it('ignores a non-numeric volume', () => {
    const s = useSettingsStore()
    s.setVolume('loud')
    expect(s.volumePct).toBe(50)
  })
})

describe('settings store — muting', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('silences every agent while global mute is on', () => {
    const s = useSettingsStore()
    s.toggleMuteAll()
    expect(s.mutedAll).toBe(true)
    expect(s.gainFor('a1')).toBe(0)
    expect(s.gainFor(null)).toBe(0)
  })

  it('silences only the muted agent', () => {
    const s = useSettingsStore()
    s.toggleAgentMute('a1')
    expect(s.isAgentMuted('a1')).toBe(true)
    expect(s.gainFor('a1')).toBe(0)
    expect(s.gainFor('a2')).toBeCloseTo(0.25)
  })

  it('un-mutes an agent on a second toggle', () => {
    const s = useSettingsStore()
    s.toggleAgentMute('a1')
    s.toggleAgentMute('a1')
    expect(s.isAgentMuted('a1')).toBe(false)
    expect(s.gainFor('a1')).toBeCloseTo(0.25)
  })

  it('unmuteAllAgents clears the whole list but leaves global mute alone', () => {
    const s = useSettingsStore()
    s.toggleAgentMute('a1')
    s.toggleAgentMute('a2')
    s.toggleMuteAll()
    s.unmuteAllAgents()
    expect(s.mutedAgentIds).toEqual([])
    expect(s.mutedAll).toBe(true)
  })

  it('forgetAgent drops an exited agent from the muted list', () => {
    const s = useSettingsStore()
    s.toggleAgentMute('a1')
    s.toggleAgentMute('a2')
    s.forgetAgent('a1')
    expect(s.mutedAgentIds).toEqual(['a2'])
  })
})

describe('settings store — persistence', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('writes volume, global mute and muted agents on every change', () => {
    const s = useSettingsStore()
    s.setVolume(80)
    s.toggleMuteAll()
    s.toggleAgentMute('a1')
    const raw = JSON.parse(localStorage.getItem(KEY))
    expect(raw).toEqual({ volumePct: 80, mutedAll: true, mutedAgentIds: ['a1'] })
  })

  it('persists an un-mute so it is not resurrected on reload', () => {
    const s = useSettingsStore()
    s.toggleAgentMute('a1')
    s.forgetAgent('a1')
    expect(JSON.parse(localStorage.getItem(KEY)).mutedAgentIds).toEqual([])
  })

  it('restores saved settings when the store is created', () => {
    localStorage.setItem(KEY, JSON.stringify({
      volumePct: 10, mutedAll: true, mutedAgentIds: ['a1', 'a2'],
    }))
    const s = useSettingsStore()
    expect(s.volumePct).toBe(10)
    expect(s.mutedAll).toBe(true)
    expect(s.isAgentMuted('a2')).toBe(true)
  })

  it('falls back to defaults on corrupt JSON', () => {
    localStorage.setItem(KEY, '<<not json>>')
    const s = useSettingsStore()
    expect(s.volumePct).toBe(50)
    expect(s.mutedAll).toBe(false)
    expect(s.mutedAgentIds).toEqual([])
  })

  it('falls back to defaults for missing or wrongly typed fields', () => {
    localStorage.setItem(KEY, JSON.stringify({ volumePct: 'loud', mutedAgentIds: 'a1' }))
    const s = useSettingsStore()
    expect(s.volumePct).toBe(50)
    expect(s.mutedAll).toBe(false)
    expect(s.mutedAgentIds).toEqual([])
  })

  it('clamps an out-of-range persisted volume', () => {
    localStorage.setItem(KEY, JSON.stringify({ volumePct: 999 }))
    const s = useSettingsStore()
    expect(s.volumePct).toBe(100)
  })
})
