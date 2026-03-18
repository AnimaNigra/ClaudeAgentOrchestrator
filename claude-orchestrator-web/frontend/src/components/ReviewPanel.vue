<template>
  <div class="flex flex-col h-full bg-gray-950 text-gray-200 overflow-hidden">
    <!-- Header -->
    <div class="flex items-center justify-between px-4 py-2 border-b border-gray-800 bg-gray-900">
      <div class="flex items-center gap-3">
        <span class="text-sm font-semibold">Review</span>
        <span v-if="data?.branch" class="text-xs bg-blue-900/50 text-blue-300 px-2 py-0.5 rounded">
          {{ data.branch }}
        </span>
        <span v-if="data?.files.length" class="text-xs text-gray-500">
          {{ data.files.length }} file{{ data.files.length === 1 ? '' : 's' }} changed
        </span>
      </div>
      <div class="flex items-center gap-2">
        <button @click="load" class="text-xs text-gray-400 hover:text-white px-2 py-1 rounded hover:bg-gray-800"
                :disabled="loading">
          {{ loading ? 'Loading…' : '↻ Refresh' }}
        </button>
        <button @click="$emit('close')" class="text-xs text-gray-400 hover:text-white px-2 py-1 rounded hover:bg-gray-800">
          ✕ Close
        </button>
      </div>
    </div>

    <!-- Not a git repo -->
    <div v-if="data && !data.isGitRepo" class="flex items-center justify-center h-full text-gray-600 text-sm">
      Not a git repository
    </div>

    <!-- No changes -->
    <div v-else-if="data && !data.files.length" class="flex items-center justify-center h-full text-gray-600 text-sm">
      No changes detected
    </div>

    <!-- Content -->
    <div v-else-if="data" class="flex flex-1 overflow-hidden">
      <!-- File list -->
      <div class="flex-shrink-0 border-r border-gray-800 overflow-y-auto" :style="{ width: fileListWidth + 'px' }">
        <button
          v-for="f in data.files" :key="f.path"
          class="w-full text-left px-3 py-1.5 text-xs hover:bg-gray-800 flex items-center gap-2 border-b border-gray-800/50"
          :class="{ 'bg-gray-800': selectedFile === f.path }"
          @click="selectedFile = selectedFile === f.path ? null : f.path"
        >
          <span :class="statusColor(f.status)" class="font-mono text-[10px] uppercase w-14 flex-shrink-0">{{ f.status }}</span>
          <span class="truncate" :title="f.path">{{ f.path }}</span>
        </button>
      </div>

      <!-- Resize handle -->
      <div
        class="w-1 flex-shrink-0 cursor-col-resize hover:bg-blue-500/50 active:bg-blue-500/70 transition-colors"
        @mousedown="startResize"
      />

      <!-- Diff view -->
      <div class="flex-1 overflow-auto min-w-0">
        <pre class="p-4 text-xs font-mono leading-5 whitespace-pre-wrap"><template
          v-for="(line, i) in diffLines" :key="i"
><span :class="lineClass(line)">{{ line }}
</span></template></pre>
      </div>
    </div>

    <!-- Loading -->
    <div v-else class="flex items-center justify-center h-full text-gray-600 text-sm">
      Loading…
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, computed, watch } from 'vue'

const props = defineProps({ agentId: String })
defineEmits(['close'])

const data = ref(null)
const loading = ref(false)
const selectedFile = ref(null)
const fileListWidth = ref(256)

function startResize(e) {
  const startX = e.clientX
  const startWidth = fileListWidth.value
  const onMove = (ev) => {
    fileListWidth.value = Math.max(120, Math.min(800, startWidth + ev.clientX - startX))
  }
  const onUp = () => {
    document.removeEventListener('mousemove', onMove)
    document.removeEventListener('mouseup', onUp)
    document.body.style.cursor = ''
    document.body.style.userSelect = ''
  }
  document.body.style.cursor = 'col-resize'
  document.body.style.userSelect = 'none'
  document.addEventListener('mousemove', onMove)
  document.addEventListener('mouseup', onUp)
}

async function load() {
  loading.value = true
  try {
    const res = await fetch(`/api/agents/${props.agentId}/review`)
    if (res.ok) data.value = await res.json()
  } finally { loading.value = false }
}

const diffLines = computed(() => {
  if (!data.value?.diff) return []
  const lines = data.value.diff.split('\n')
  if (!selectedFile.value) return lines
  // Filter diff to selected file
  const chunks = []
  let inFile = false
  for (const line of lines) {
    if (line.startsWith('diff --git')) {
      inFile = line.includes(selectedFile.value)
    }
    if (inFile) chunks.push(line)
  }
  return chunks.length ? chunks : lines
})

function lineClass(line) {
  if (line.startsWith('+') && !line.startsWith('+++')) return 'text-green-400 bg-green-900/20'
  if (line.startsWith('-') && !line.startsWith('---')) return 'text-red-400 bg-red-900/20'
  if (line.startsWith('@@')) return 'text-blue-400'
  if (line.startsWith('diff --git')) return 'text-yellow-400 font-bold'
  return 'text-gray-400'
}

function statusColor(status) {
  return {
    modified: 'text-yellow-400',
    added: 'text-green-400',
    deleted: 'text-red-400',
    untracked: 'text-gray-500',
    renamed: 'text-blue-400',
  }[status] ?? 'text-gray-400'
}

watch(() => props.agentId, () => { data.value = null; selectedFile.value = null; load() })
onMounted(load)
</script>
