<template>
  <div class="flex flex-col h-full overflow-hidden bg-gray-950">
    <div class="flex items-center justify-between px-4 py-3 border-b border-gray-800 flex-shrink-0">
      <h2 class="text-sm font-semibold text-gray-200">Session History</h2>
      <span class="text-xs text-gray-500">{{ records.length }} sessions</span>
    </div>

    <div v-if="loading" class="flex items-center justify-center h-full text-gray-500 text-sm">
      Načítám...
    </div>

    <div v-else-if="records.length === 0" class="flex items-center justify-center h-full text-gray-600 text-sm select-none">
      Žádná historie — session se zde zobrazí po ukončení agenta.
    </div>

    <div v-else class="flex-1 overflow-y-auto px-4 py-3 space-y-2">
      <div
        v-for="r in records"
        :key="r.id"
        class="bg-gray-900 border border-gray-800 rounded-lg p-3 space-y-2"
      >
        <!-- Header -->
        <div class="flex items-start justify-between gap-3">
          <div class="min-w-0 flex-1">
            <div class="text-sm font-medium text-gray-200 truncate">{{ r.name }}</div>
            <div v-if="r.cwd" class="text-xs text-gray-500 truncate mt-0.5">{{ r.cwd }}</div>
          </div>
          <div class="flex items-center gap-2 flex-shrink-0 mt-0.5">
            <span v-if="r.sessionId" class="text-xs text-green-400 bg-green-400/10 px-2 py-0.5 rounded">resumable</span>
            <span class="text-xs text-gray-600">{{ formatDate(r.finishedAt) }}</span>
          </div>
        </div>

        <!-- Notes textarea -->
        <textarea
          :value="r.notes ?? ''"
          @blur="e => saveNotes(r.id, e.target.value)"
          @keydown.ctrl.enter="e => e.target.blur()"
          placeholder="Poznámky k session... (Ctrl+Enter pro uložení)"
          rows="2"
          class="w-full text-xs text-gray-300 bg-gray-800 border border-gray-700 rounded px-2 py-1.5 resize-none focus:outline-none focus:border-blue-500 placeholder-gray-600"
        />

        <!-- Actions -->
        <div class="flex items-center gap-2">
          <button
            v-if="r.sessionId"
            @click="resume(r)"
            class="text-xs px-3 py-1 bg-blue-600 hover:bg-blue-500 text-white rounded transition-colors"
          >
            Resume ↺
          </button>
          <span v-else class="text-xs text-gray-600 italic">no session id</span>
          <button
            @click="deleteRecord(r.id)"
            class="text-xs px-2 py-1 text-gray-600 hover:text-red-400 hover:bg-gray-800 rounded transition-colors ml-auto"
          >
            Smazat
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAgentsStore } from '../stores/agents'

const router = useRouter()
const store = useAgentsStore()

const records = ref([])
const loading = ref(true)

async function load() {
  loading.value = true
  try {
    const res = await fetch('/api/history')
    records.value = await res.json()
  } finally {
    loading.value = false
  }
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

async function deleteRecord(id) {
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
