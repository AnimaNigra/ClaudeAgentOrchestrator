<template>
  <div class="flex flex-col h-full overflow-hidden bg-gray-950">
    <div class="flex items-center justify-between px-4 py-3 border-b border-gray-800 flex-shrink-0">
      <h2 class="text-sm font-semibold text-gray-200">Session History</h2>
      <span class="text-xs text-gray-500">{{ records.length }} sessions in {{ groups.length }} groups</span>
    </div>

    <div v-if="loading" class="flex items-center justify-center h-full text-gray-500 text-sm">
      Loading...
    </div>

    <div v-else-if="groups.length === 0" class="flex items-center justify-center h-full text-gray-600 text-sm select-none">
      No history yet — sessions will appear here after an agent finishes.
    </div>

    <div v-else class="flex-1 overflow-y-auto px-4 py-3 space-y-2">
      <div
        v-for="group in groups"
        :key="group.key"
        class="bg-gray-900 border border-gray-800 rounded-lg overflow-hidden"
      >
        <!-- Group header -->
        <button
          @click="toggle(group.key)"
          class="w-full flex items-center gap-3 px-3 py-2.5 hover:bg-gray-800/50 transition-colors text-left"
        >
          <span class="text-gray-500 text-xs w-4 flex-shrink-0 transition-transform" :class="{ 'rotate-90': expanded[group.key] }">&#9654;</span>
          <div class="min-w-0 flex-1">
            <span class="text-sm font-medium text-gray-200">{{ group.name }}</span>
            <span v-if="group.cwd" class="text-xs text-gray-500 ml-2 truncate">{{ group.cwd }}</span>
          </div>
          <div class="flex items-center gap-3 flex-shrink-0">
            <span class="text-xs text-gray-500">&times;{{ group.sessions.length }}</span>
            <span v-if="group.resumableCount > 0" class="text-xs text-green-400 bg-green-400/10 px-1.5 py-0.5 rounded">{{ group.resumableCount }} resumable</span>
            <span class="text-xs text-gray-600">{{ formatDate(group.lastFinished) }}</span>
          </div>
        </button>

        <!-- Expanded sessions -->
        <div v-if="expanded[group.key]" class="border-t border-gray-800">
          <div
            v-for="r in group.sessions"
            :key="r.id"
            class="px-3 py-2.5 border-b border-gray-800/50 last:border-b-0 space-y-2"
          >
            <div class="flex items-center justify-between gap-3">
              <div class="flex items-center gap-2 min-w-0">
                <span v-if="r.sessionId" class="text-xs text-green-400 bg-green-400/10 px-1.5 py-0.5 rounded flex-shrink-0">resumable</span>
                <span v-else class="text-xs text-gray-600 italic flex-shrink-0">no session id</span>
                <span class="text-xs text-gray-500">{{ formatDate(r.finishedAt) }}</span>
              </div>
              <div class="flex items-center gap-2 flex-shrink-0">
                <button
                  v-if="r.sessionId"
                  @click="resume(r)"
                  class="text-xs px-2.5 py-1 bg-blue-600 hover:bg-blue-500 text-white rounded transition-colors"
                >
                  Resume
                </button>
                <button
                  @click="deleteRecord(r.id, group.key)"
                  class="text-xs px-2 py-1 text-gray-600 hover:text-red-400 hover:bg-gray-800 rounded transition-colors"
                >
                  Delete
                </button>
              </div>
            </div>

            <!-- Notes -->
            <textarea
              :value="r.notes ?? ''"
              @blur="e => saveNotes(r.id, e.target.value)"
              @keydown.ctrl.enter="e => e.target.blur()"
              placeholder="Notes... (Ctrl+Enter to save)"
              rows="1"
              class="w-full text-xs text-gray-300 bg-gray-800 border border-gray-700 rounded px-2 py-1 resize-none focus:outline-none focus:border-blue-500 placeholder-gray-600"
            />
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAgentsStore } from '../stores/agents'

const router = useRouter()
const store = useAgentsStore()

const records = ref([])
const loading = ref(true)
const expanded = ref({})

const groups = computed(() => {
  const map = new Map()
  for (const r of records.value) {
    const key = `${r.name}|||${r.cwd ?? ''}`
    if (!map.has(key)) {
      map.set(key, { key, name: r.name, cwd: r.cwd, sessions: [] })
    }
    map.get(key).sessions.push(r)
  }
  const result = []
  for (const group of map.values()) {
    group.sessions.sort((a, b) => new Date(b.finishedAt ?? 0) - new Date(a.finishedAt ?? 0))
    group.lastFinished = group.sessions[0]?.finishedAt
    group.resumableCount = group.sessions.filter(s => s.sessionId).length
    result.push(group)
  }
  result.sort((a, b) => new Date(b.lastFinished ?? 0) - new Date(a.lastFinished ?? 0))
  return result
})

async function load() {
  loading.value = true
  try {
    const res = await fetch('/api/history')
    records.value = await res.json()
  } finally {
    loading.value = false
  }
}

function toggle(key) {
  expanded.value[key] = !expanded.value[key]
}

async function saveNotes(id, notes) {
  await fetch(`/api/history/${id}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ notes }),
  })
  const r = records.value.find(r => r.id === id)
  if (r) r.notes = notes
}

async function resume(record) {
  const agent = await store.spawnAgent(record.name, record.cwd, record.sessionId)
  store.activeAgentId = agent.id
  router.push('/')
}

async function startFresh(group, event) {
  event.stopPropagation()
  const agent = await store.spawnAgent(group.name, group.cwd, null)
  store.activeAgentId = agent.id
  router.push('/')
}

async function deleteRecord(id, groupKey) {
  await fetch(`/api/history/${id}`, { method: 'DELETE' })
  records.value = records.value.filter(r => r.id !== id)
}

function formatDate(dt) {
  if (!dt) return ''
  return new Date(dt).toLocaleString('cs-CZ', {
    day: '2-digit', month: '2-digit', year: '2-digit',
    hour: '2-digit', minute: '2-digit',
  })
}

onMounted(load)
</script>
