<template>
  <Teleport to="body">
    <div v-if="show" class="fixed inset-0 z-50 flex items-center justify-center">
      <!-- Backdrop -->
      <div class="absolute inset-0 bg-black/60" @click="$emit('close')" />

      <!-- Modal -->
      <div class="relative bg-gray-800 rounded-xl border border-gray-700 w-[1024px] max-w-[95vw] mx-4 p-6 flex flex-col gap-4">
        <h2 class="text-base font-semibold text-white">{{ isEdit ? 'Edit Task' : 'New Task' }}</h2>

        <div class="flex flex-col gap-3">
          <!-- Title -->
          <div class="flex flex-col gap-1">
            <label class="text-xs text-gray-400">Title *</label>
            <input
              v-model="form.title"
              @keydown.enter="submit"
              placeholder="Task title"
              class="bg-gray-900 border border-gray-600 rounded px-3 py-2 text-sm text-white placeholder-gray-600 focus:outline-none focus:border-blue-500"
              autofocus
            />
          </div>

          <!-- Description -->
          <div class="flex flex-col gap-1">
            <label class="text-xs text-gray-400">Description</label>
            <textarea
              v-model="form.description"
              rows="4"
              placeholder="Optional description"
              class="bg-gray-900 border border-gray-600 rounded px-3 py-2 text-sm text-white placeholder-gray-600 focus:outline-none focus:border-blue-500 resize-none"
            />
          </div>

          <!-- Prompt -->
          <div class="flex flex-col gap-1">
            <label class="text-xs text-gray-400">Prompt <span class="text-gray-600">(sent to agent on assign)</span></label>
            <textarea
              v-model="form.prompt"
              rows="16"
              placeholder="What should the agent do?"
              class="bg-gray-900 border border-gray-600 rounded px-3 py-2 text-sm text-white placeholder-gray-600 focus:outline-none focus:border-blue-500 resize-none font-mono"
            />
          </div>
        </div>

        <p v-if="error" class="text-xs text-red-400">{{ error }}</p>

        <div class="flex justify-end gap-2 pt-1">
          <button
            @click="$emit('close')"
            class="px-4 py-2 text-sm text-gray-400 hover:text-white transition-colors"
          >Cancel</button>
          <button
            @click="submit"
            :disabled="!form.title.trim()"
            class="px-4 py-2 text-sm bg-blue-600 hover:bg-blue-500 disabled:bg-gray-700 disabled:text-gray-500 text-white rounded transition-colors"
          >{{ isEdit ? 'Save Changes' : 'Create Task' }}</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<script setup>
import { ref, watch, computed } from 'vue'

const props = defineProps({
  show: { type: Boolean, default: false },
  task: { type: Object, default: null },
})

const emit = defineEmits(['close', 'created', 'updated'])

const isEdit = computed(() => props.task !== null)

const form = ref({ title: '', description: '', prompt: '' })
const error = ref(null)

watch(
  [() => props.show, () => props.task],
  ([show]) => {
    if (show) {
      form.value = {
        title: props.task?.title ?? '',
        description: props.task?.description ?? '',
        prompt: props.task?.prompt ?? '',
      }
      error.value = null
    }
  }
)

function submit() {
  if (!form.value.title.trim()) {
    error.value = 'Title is required'
    return
  }
  const data = {
    title: form.value.title.trim(),
    description: form.value.description.trim() || null,
    prompt: form.value.prompt.trim() || null,
  }
  if (isEdit.value) {
    emit('updated', { id: props.task.id, ...data })
  } else {
    emit('created', data)
  }
}
</script>
