import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

const STORAGE_KEY = 'claude-orchestrator-sound-v1'
const DEFAULT_VOLUME_PCT = 50
// Gain at 100 %. The historical hard-coded ding gain was 0.25, so the
// 50 % default sounds exactly like it did before this setting existed.
const MAX_GAIN = 0.5

function clampPct(value) {
  return Math.min(100, Math.max(0, Math.round(value)))
}

export const useSettingsStore = defineStore('settings', () => {
  const volumePct = ref(DEFAULT_VOLUME_PCT)
  const mutedAll = ref(false)
  const _mutedAgents = ref(new Set())

  const mutedAgentIds = computed(() => [..._mutedAgents.value])

  function persist() {
    const payload = {
      volumePct: volumePct.value,
      mutedAll: mutedAll.value,
      mutedAgentIds: mutedAgentIds.value,
    }
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(payload)) } catch { /* private mode */ }
  }

  function hydrate() {
    let raw
    try { raw = JSON.parse(localStorage.getItem(STORAGE_KEY) || 'null') } catch { raw = null }
    if (!raw || typeof raw !== 'object') return
    if (typeof raw.volumePct === 'number' && Number.isFinite(raw.volumePct))
      volumePct.value = clampPct(raw.volumePct)
    if (typeof raw.mutedAll === 'boolean') mutedAll.value = raw.mutedAll
    if (Array.isArray(raw.mutedAgentIds))
      _mutedAgents.value = new Set(raw.mutedAgentIds.filter(id => typeof id === 'string'))
  }

  hydrate()

  function setVolume(pct) {
    const n = Number(pct)
    if (!Number.isFinite(n)) return
    volumePct.value = clampPct(n)
    persist()
  }

  function toggleMuteAll() {
    mutedAll.value = !mutedAll.value
    persist()
  }

  function isAgentMuted(agentId) {
    return _mutedAgents.value.has(agentId)
  }

  function toggleAgentMute(agentId) {
    if (!agentId) return
    if (_mutedAgents.value.has(agentId)) _mutedAgents.value.delete(agentId)
    else _mutedAgents.value.add(agentId)
    persist()
  }

  function unmuteAllAgents() {
    _mutedAgents.value.clear()
    persist()
  }

  // Called when an agent exits so the persisted list can't grow forever.
  function forgetAgent(agentId) {
    if (_mutedAgents.value.delete(agentId)) persist()
  }

  // Gain for one notification sound. 0 means "don't play at all".
  function gainFor(agentId) {
    if (mutedAll.value) return 0
    if (agentId && isAgentMuted(agentId)) return 0
    return (volumePct.value / 100) * MAX_GAIN
  }

  return {
    volumePct, mutedAll, mutedAgentIds,
    setVolume, toggleMuteAll, toggleAgentMute, isAgentMuted,
    unmuteAllAgents, forgetAgent, gainFor,
  }
})
