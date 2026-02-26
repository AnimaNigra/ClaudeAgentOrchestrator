import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import * as signalR from '@microsoft/signalr'

export const useAgentsStore = defineStore('agents', () => {
  const agents = ref({})        // id → agent object
  const activeAgentId = ref(null)
  const connected = ref(false)
  const pendingPermissions = ref([])  // queue of { requestId, agentId, toolName, toolInput }
  const alwaysAllowedTools = ref(new Set())

  let connection = null

  // Non-reactive PTY data callbacks (agentId → fn(base64chunk))
  const _ptyHandlers = {}
  // Buffer chunks that arrive before the terminal registers its handler
  const _ptyQueues = {}

  function registerPtyHandler(agentId, fn) {
    _ptyHandlers[agentId] = fn
    // Replay any chunks that arrived before the terminal was ready
    const queued = _ptyQueues[agentId]
    if (queued?.length) {
      queued.forEach(chunk => fn(chunk))
      delete _ptyQueues[agentId]
    }
  }

  // ── Notifications ────────────────────────────────────
  // AudioContext must be created/resumed after a user gesture.
  // We create it lazily on first keydown/click, then reuse it.
  let _audioCtx = null

  async function _ensureAudio() {
    if (!window.AudioContext && !window.webkitAudioContext) return null
    if (!_audioCtx) _audioCtx = new (window.AudioContext || window.webkitAudioContext)()
    if (_audioCtx.state === 'suspended') await _audioCtx.resume()
    return _audioCtx
  }

  // Warm up AudioContext on first user interaction so it's ready when needed
  if (typeof window !== 'undefined') {
    const warm = () => { _ensureAudio(); window.removeEventListener('keydown', warm); window.removeEventListener('click', warm) }
    window.addEventListener('keydown', warm, { once: true })
    window.addEventListener('click', warm, { once: true })
  }

  async function playDing() {
    try {
      const ctx = await _ensureAudio()
      if (!ctx) return
      const gain = ctx.createGain()
      gain.connect(ctx.destination)
      // Two-tone ding: 880 Hz then 1100 Hz
      ;[880, 1100].forEach((freq, i) => {
        const osc = ctx.createOscillator()
        osc.connect(gain)
        osc.frequency.value = freq
        osc.type = 'sine'
        const t = ctx.currentTime + i * 0.15
        gain.gain.setValueAtTime(0.25, t)
        gain.gain.exponentialRampToValueAtTime(0.001, t + 0.3)
        osc.start(t)
        osc.stop(t + 0.3)
      })
    } catch { /* ignore */ }
  }

  async function notifyIdle(agent) {
    if (!agent) return
    await playDing()
    if (Notification.permission === 'granted') {
      new Notification(`⏳ ${agent.name} čeká na vstup`, {
        body: 'Agent potřebuje tvoji odpověď.',
        silent: true, // zvuk hrajeme sami
      })
    }
  }

  // Tracks which agents have already received an idle notification this session.
  // Cleared when the agent transitions back to running.
  const _notifiedIdle = new Set()

  // ── SignalR ──────────────────────────────────────────
  async function connect() {
    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/agents')
      .withAutomaticReconnect()
      .build()

    connection.on('InitialState', (agentList) => {
      agents.value = {}
      for (const a of agentList) agents.value[a.id] = a
    })

    connection.on('AgentEvent', ({ agentId, eventType, data, agent }) => {
      if (agent) agents.value[agentId] = agent

      if (eventType === 'agent_spawned') {
        if (!activeAgentId.value) activeAgentId.value = agentId
      } else if (eventType === 'pty_data') {
        if (_ptyHandlers[agentId]) {
          _ptyHandlers[agentId](data.chunk)
        } else {
          // Terminal not mounted yet — buffer until registerPtyHandler is called
          if (!_ptyQueues[agentId]) _ptyQueues[agentId] = []
          _ptyQueues[agentId].push(data.chunk)
          if (_ptyQueues[agentId].length > 2000) _ptyQueues[agentId].shift()
        }
      } else if (eventType === 'agent_status_changed') {
        if (data?.status === 'idle' && !_notifiedIdle.has(agentId)) {
          _notifiedIdle.add(agentId)
          notifyIdle(agents.value[agentId])
        } else if (data?.status === 'running') {
          _notifiedIdle.delete(agentId)
        }
      } else if (eventType === 'permission_request') {
        if (alwaysAllowedTools.value.has(data.toolName)) {
          fetch(`/api/agents/${agentId}/permission/${data.requestId}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ approved: true }),
          })
        } else {
          const wasEmpty = pendingPermissions.value.length === 0
          pendingPermissions.value.push(data)
          if (wasEmpty) notifyIdle(agents.value[agentId]) // notify only on first in queue
        }
      } else if (eventType === 'agent_notification') {
        notifyIdle(agents.value[agentId])
      } else if (eventType === 'agent_killed' || eventType === 'agent_exited') {
        // Remove from list after a brief pause so user sees the Done state
        setTimeout(() => {
          delete agents.value[agentId]
          if (activeAgentId.value === agentId)
            activeAgentId.value = Object.keys(agents.value)[0] ?? null
        }, 2000)
      }
    })

    connection.onreconnected(() => { connected.value = true })
    connection.onclose(() => { connected.value = false })

    await connection.start()
    connected.value = true
  }

  // ── API calls ────────────────────────────────────────
  async function spawnAgent(name, cwd, resumeSessionId = null) {
    const res = await fetch('/api/agents', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name, cwd: cwd || null, resumeSessionId: resumeSessionId || null }),
    })
    if (!res.ok) {
      const err = await res.json()
      throw new Error(err.error ?? 'Failed to spawn')
    }
    return res.json()
  }

  async function sendKeystroke(agentId, data) {
    await fetch(`/api/agents/${agentId}/keystroke`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ data }),
    })
  }

  async function resizePty(agentId, cols, rows) {
    await fetch(`/api/agents/${agentId}/resize`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ cols, rows }),
    })
  }

  async function killAgent(agentId) {
    await fetch(`/api/agents/${agentId}`, { method: 'DELETE' })
  }

  function addAlwaysAllowed(toolName) {
    alwaysAllowedTools.value.add(toolName)
  }

  const agentList = computed(() => Object.values(agents.value))

  return {
    agents, activeAgentId, connected, pendingPermissions, alwaysAllowedTools,
    agentList,
    connect, spawnAgent, sendKeystroke, resizePty, killAgent,
    registerPtyHandler, addAlwaysAllowed,
  }
})
