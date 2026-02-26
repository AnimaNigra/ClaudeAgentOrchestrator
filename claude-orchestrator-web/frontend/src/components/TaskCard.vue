<template>
  <div class="bg-gray-800 rounded-lg p-3 flex flex-col gap-2 border border-gray-700 hover:border-gray-600 transition-colors">
    <!-- Title + delete button -->
    <div class="flex items-start justify-between gap-2">
      <span class="text-sm font-medium text-white leading-tight">{{ task.title }}</span>
      <button
        @click="emit('delete', task.id)"
        class="text-gray-600 hover:text-red-400 text-xs flex-shrink-0 transition-colors"
        title="Delete task"
      >✕</button>
    </div>

    <!-- Description -->
    <p v-if="task.description" class="text-xs text-gray-400 leading-relaxed line-clamp-2">
      {{ task.description }}
    </p>

    <!-- Prompt (collapsible) -->
    <div v-if="task.prompt">
      <button
        @click="promptOpen = !promptOpen"
        class="text-xs text-blue-400 hover:text-blue-300 flex items-center gap-1"
      >
        <span>{{ promptOpen ? '▾' : '▸' }}</span>
        <span>Prompt</span>
      </button>
      <pre v-if="promptOpen" class="mt-1 text-xs text-gray-300 bg-gray-900 rounded p-2 whitespace-pre-wrap overflow-auto max-h-24">{{ task.prompt }}</pre>
    </div>

    <!-- Agent badge (when assigned) -->
    <div v-if="task.agentName" class="flex items-center gap-1 text-xs">
      <span class="text-gray-500">🤖</span>
      <span class="text-gray-300">{{ task.agentName }}</span>
    </div>

    <!-- Action buttons -->
    <div class="flex gap-2 mt-1">
      <!-- TODO: assign to agent -->
      <template v-if="task.status === 'todo'">
        <select
          v-if="availableAgents.length"
          @change="onAssign($event.target.value); $event.target.value = ''"
          class="flex-1 text-xs bg-gray-700 text-white rounded px-2 py-1 border border-gray-600 hover:border-gray-500 cursor-pointer"
        >
          <option value="" disabled selected>Assign to agent…</option>
          <option v-for="a in availableAgents" :key="a.id" :value="a.id">
            {{ a.name }} ({{ a.status }})
          </option>
        </select>
        <span v-else class="text-xs text-gray-600 italic">No agents running</span>
      </template>

      <!-- IN PROGRESS: mark done -->
      <template v-if="task.status === 'in-progress'">
        <button
          @click="emit('mark-done', task.id)"
          class="flex-1 text-xs bg-green-700 hover:bg-green-600 text-white rounded px-2 py-1 transition-colors"
        >
          Mark Done ✓
        </button>
      </template>

      <!-- DONE: resume agent (only if sessionId exists in agentRecord) -->
      <template v-if="task.status === 'done' && agentSessionId">
        <button
          @click="emit('resume', { agentName: task.agentName, sessionId: agentSessionId })"
          class="flex-1 text-xs bg-blue-700 hover:bg-blue-600 text-white rounded px-2 py-1 transition-colors"
        >
          Resume Agent ↺
        </button>
      </template>
    </div>
  </div>
</template>

<script setup>
import { ref, computed } from 'vue'

const props = defineProps({
  task: { type: Object, required: true },
  availableAgents: { type: Array, default: () => [] },
  agentRecord: { type: Object, default: null },
})

const emit = defineEmits(['assign', 'mark-done', 'delete', 'resume'])

const promptOpen = ref(false)
const agentSessionId = computed(() => props.agentRecord?.sessionId ?? null)

function onAssign(agentId) {
  if (agentId) emit('assign', { taskId: props.task.id, agentId })
}
</script>
