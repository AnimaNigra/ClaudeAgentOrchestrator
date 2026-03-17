<template>
  <Teleport to="body">
    <div v-if="show" class="fixed inset-0 z-50 flex items-center justify-center">
      <!-- Backdrop -->
      <div class="absolute inset-0 bg-black/60" @click="cancel" />

      <!-- Dialog -->
      <div class="relative bg-gray-800 rounded-xl border border-gray-700 w-[640px] max-w-[95vw] mx-4 p-6 flex flex-col gap-4">
        <h2 class="text-base font-semibold text-white flex items-center gap-2">
          <span
            class="inline-block w-2 h-2 rounded-full"
            :class="isRecording ? 'bg-red-500 animate-pulse' : 'bg-gray-600'"
          />
          {{ isRecording ? 'Nahrávám…' : 'Hlasové zadávání' }}
        </h2>

        <!-- Error banner -->
        <p v-if="error" class="text-xs text-red-400 bg-red-950/40 border border-red-800 rounded px-3 py-2">
          {{ error }}
        </p>

        <!-- Transcript textarea -->
        <div class="flex flex-col gap-1">
          <label class="text-xs text-gray-400">Přepis</label>
          <textarea
            v-model="transcript"
            rows="6"
            placeholder="Začněte mluvit…"
            class="bg-gray-900 border border-gray-600 rounded px-3 py-2 text-sm text-white placeholder-gray-600 focus:outline-none focus:border-blue-500 resize-none font-mono"
          />
          <!-- Interim text (partial, grayed out) -->
          <p v-if="interimText" class="text-xs text-gray-500 italic px-1">{{ interimText }}</p>
        </div>

        <!-- Image picker -->
        <div class="flex flex-col gap-1">
          <label class="text-xs text-gray-400">Obrázek <span class="text-gray-600">(volitelné)</span></label>
          <div class="flex items-center gap-2">
            <input
              ref="fileInputEl"
              type="file"
              accept="image/*"
              class="hidden"
              @change="onFileChange"
            />
            <button
              @click="fileInputEl.click()"
              class="px-3 py-1.5 text-xs bg-gray-700 hover:bg-gray-600 text-gray-300 rounded transition-colors"
            >
              Vybrat obrázek
            </button>
            <span v-if="imageFile" class="text-xs text-gray-400 truncate max-w-[260px]">{{ imageFile.name }}</span>
            <button v-if="imageFile" @click="imageFile = null" class="text-xs text-gray-600 hover:text-red-400 transition-colors">✕</button>
          </div>
        </div>

        <!-- Actions -->
        <div class="flex items-center justify-between pt-1">
          <!-- Recording toggle -->
          <button
            v-if="isRecording"
            @click="stopRecording"
            class="px-3 py-1.5 text-xs bg-red-900/60 hover:bg-red-900 text-red-300 rounded transition-colors"
          >
            Zastavit
          </button>
          <button
            v-else
            @click="startRecording"
            class="px-3 py-1.5 text-xs bg-gray-700 hover:bg-gray-600 text-gray-300 rounded transition-colors"
          >
            Znovu nahrát
          </button>

          <div class="flex gap-2">
            <button
              @click="cancel"
              class="px-4 py-2 text-sm text-gray-400 hover:text-white transition-colors"
            >Zrušit</button>
            <button
              @click="confirm"
              :disabled="!canConfirm"
              class="px-4 py-2 text-sm bg-blue-600 hover:bg-blue-500 disabled:bg-gray-700 disabled:text-gray-500 text-white rounded transition-colors"
            >Odeslat</button>
          </div>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<script setup>
import { ref, computed, watch } from 'vue'
import { useAgentsStore } from '../stores/agents'

const props = defineProps({
  show: { type: Boolean, default: false },
  agentId: { type: String, required: true },
})
const emit = defineEmits(['close'])

const store = useAgentsStore()

// State
const transcript = ref('')
const interimText = ref('')
const imageFile = ref(null)
const isRecording = ref(false)
const error = ref(null)
const fileInputEl = ref(null)

const canConfirm = computed(() => transcript.value.trim().length > 0 || imageFile.value !== null)

// Reset on open
watch(
  () => props.show,
  (show) => {
    if (show) {
      transcript.value = ''
      interimText.value = ''
      imageFile.value = null
      isRecording.value = false
      error.value = null
    }
  }
)

function onFileChange(e) {
  imageFile.value = e.target.files[0] ?? null
}

function stopRecording() {
  // placeholder — speech logic added in Task 3
  isRecording.value = false
}

function startRecording() {
  // placeholder — speech logic added in Task 3
}

function cancel() {
  stopRecording()
  emit('close')
}

async function confirm() {
  // placeholder — confirm logic added in Task 4
  emit('close')
}
</script>
