<template>
  <Teleport to="body">
    <div v-if="show" class="fixed inset-0 z-50 flex items-center justify-center">
      <!-- Backdrop -->
      <div class="absolute inset-0 bg-black/60" @click="$emit('close')" />

      <!-- Modal -->
      <div class="relative bg-gray-800 rounded-xl border border-gray-700 w-[1024px] max-w-[95vw] mx-4 p-6 flex flex-col gap-4"
           @paste="onPaste">
        <h2 class="text-base font-semibold text-white">{{ isEdit ? 'Edit Task' : 'New Task' }}</h2>

        <div class="flex flex-col gap-3">
          <!-- Title -->
          <div class="flex flex-col gap-1">
            <div class="flex items-center justify-between">
              <label class="text-xs text-gray-400">Title *</label>
              <button @click="dictateTarget = 'title'; showVoice = true"
                class="text-xs text-gray-500 hover:text-gray-300 transition-colors" title="Dictate">🎤</button>
            </div>
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
            <div class="flex items-center justify-between">
              <label class="text-xs text-gray-400">Description</label>
              <button @click="dictateTarget = 'description'; showVoice = true"
                class="text-xs text-gray-500 hover:text-gray-300 transition-colors" title="Dictate">🎤</button>
            </div>
            <textarea
              v-model="form.description"
              rows="4"
              placeholder="Optional description"
              class="bg-gray-900 border border-gray-600 rounded px-3 py-2 text-sm text-white placeholder-gray-600 focus:outline-none focus:border-blue-500 resize-none"
            />
          </div>

          <!-- Prompt -->
          <div class="flex flex-col gap-1">
            <div class="flex items-center justify-between">
              <label class="text-xs text-gray-400">Prompt <span class="text-gray-600">(sent to agent on assign)</span></label>
              <button @click="dictateTarget = 'prompt'; showVoice = true"
                class="text-xs text-gray-500 hover:text-gray-300 transition-colors" title="Dictate">🎤</button>
            </div>
            <textarea
              v-model="form.prompt"
              rows="16"
              placeholder="What should the agent do?"
              class="bg-gray-900 border border-gray-600 rounded px-3 py-2 text-sm text-white placeholder-gray-600 focus:outline-none focus:border-blue-500 resize-none font-mono"
            />
          </div>

          <!-- Attachments -->
          <div class="flex flex-col gap-1">
            <div class="flex items-center justify-between">
              <label class="text-xs text-gray-400">Attachments</label>
              <span class="text-xs text-gray-600">Ctrl+V to paste</span>
            </div>
            <div class="flex items-center gap-2">
              <input
                ref="fileInputEl"
                type="file"
                accept="image/*"
                multiple
                class="hidden"
                @change="onFileChange"
              />
              <button
                @click="fileInputEl.click()"
                class="px-3 py-1.5 text-xs bg-gray-700 hover:bg-gray-600 text-gray-300 rounded transition-colors"
              >+ Add image</button>
            </div>
            <!-- Existing attachments (edit mode) -->
            <div v-if="existingAttachments.length" class="flex flex-wrap gap-1 mt-1">
              <span
                v-for="(path, i) in existingAttachments" :key="'e'+i"
                class="inline-flex items-center gap-1 bg-gray-700/50 text-xs text-gray-400 px-2 py-0.5 rounded"
              >
                {{ path.split('/').pop().split('\\').pop() }}
                <button @click="removeExistingAttachment(i)" class="text-gray-600 hover:text-red-400 transition-colors">✕</button>
              </span>
            </div>
            <!-- New files to upload -->
            <div v-if="pendingFiles.length" class="flex flex-wrap gap-1 mt-1">
              <span
                v-for="(f, i) in pendingFiles" :key="'p'+i"
                class="inline-flex items-center gap-1 bg-blue-900/30 text-xs text-blue-300 px-2 py-0.5 rounded"
              >
                {{ f.name }}
                <button @click="pendingFiles.splice(i, 1)" class="text-blue-600 hover:text-red-400 transition-colors">✕</button>
              </span>
            </div>
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

    <VoiceDictateDialog
      :show="showVoice"
      @confirm="onDictated"
      @close="showVoice = false"
    />
  </Teleport>
</template>

<script setup>
import { ref, watch, computed } from 'vue'
import VoiceDictateDialog from './VoiceDictateDialog.vue'

const props = defineProps({
  show: { type: Boolean, default: false },
  task: { type: Object, default: null },
  prefill: { type: String, default: null },
})

const emit = defineEmits(['close', 'created', 'updated'])

const isEdit = computed(() => props.task !== null)

const form = ref({ title: '', description: '', prompt: '' })
const error = ref(null)
const showVoice = ref(false)
const dictateTarget = ref('title')
const fileInputEl = ref(null)
const pendingFiles = ref([])
const existingAttachments = ref([])
const removedAttachments = ref([])

function onDictated(text) {
  if (!text) return
  const current = form.value[dictateTarget.value]
  form.value[dictateTarget.value] = current ? current + ' ' + text : text
}

function onFileChange(e) {
  for (const f of e.target.files) pendingFiles.value.push(f)
  if (fileInputEl.value) fileInputEl.value.value = ''
}

function onPaste(e) {
  const items = e.clipboardData?.items
  if (!items) return
  for (const item of items) {
    if (item.type.startsWith('image/')) {
      e.preventDefault()
      const blob = item.getAsFile()
      if (blob) {
        const ext = item.type.split('/')[1] || 'png'
        pendingFiles.value.push(new File([blob], `paste_${Date.now()}.${ext}`, { type: item.type }))
      }
      return
    }
  }
}

function removeExistingAttachment(index) {
  const [path] = existingAttachments.value.splice(index, 1)
  removedAttachments.value.push(path)
}

watch(
  [() => props.show, () => props.task],
  ([show]) => {
    if (show) {
      form.value = {
        title: props.task?.title ?? props.prefill ?? '',
        description: props.task?.description ?? '',
        prompt: props.task?.prompt ?? '',
      }
      existingAttachments.value = [...(props.task?.attachments ?? [])]
      removedAttachments.value = []
      pendingFiles.value = []
      error.value = null
    }
  }
)

async function uploadFiles(taskId) {
  for (const file of pendingFiles.value) {
    const formData = new FormData()
    formData.append('file', file)
    const res = await fetch(`/api/tasks/${taskId}/upload`, {
      method: 'POST',
      body: formData,
    })
    if (!res.ok) {
      const body = await res.json().catch(() => ({}))
      throw new Error(body.error ?? `Upload failed: HTTP ${res.status}`)
    }
  }
}

async function deleteRemovedAttachments(taskId) {
  for (const path of removedAttachments.value) {
    await fetch(`/api/tasks/${taskId}/attachment?path=${encodeURIComponent(path)}`, {
      method: 'DELETE',
    })
  }
}

async function submit() {
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
    try {
      await deleteRemovedAttachments(props.task.id)
      await uploadFiles(props.task.id)
    } catch (e) {
      error.value = e.message
      return
    }
    emit('updated', { id: props.task.id, ...data })
  } else {
    // Create task via API, upload files, then notify parent
    try {
      const res = await fetch('/api/tasks', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data),
      })
      if (!res.ok) throw new Error('Failed to create task')
      const task = await res.json()
      await uploadFiles(task.id)
      emit('created', task)
    } catch (e) {
      error.value = e.message ?? 'Failed to create task'
      return
    }
  }
}
</script>
