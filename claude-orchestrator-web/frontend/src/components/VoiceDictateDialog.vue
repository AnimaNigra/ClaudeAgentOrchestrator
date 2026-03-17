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

function onFileChange(e) {
  imageFile.value = e.target.files[0] ?? null
}

// Undo browser ITN: converts "10:30" → "10 30" to prevent spoken numbers
// being silently reformatted as times.
function normalizeTranscript(text) {
  return text.replace(/\b(\d{1,2}):(\d{2})\b/g, '$1 $2')
}

let recognition = null

function buildRecognition() {
  const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition
  if (!SpeechRecognition) {
    error.value = 'Váš prohlížeč nepodporuje hlasové zadávání. Použijte Chrome nebo Edge.'
    return null
  }
  const r = new SpeechRecognition()
  r.continuous = true
  r.interimResults = true

  r.onresult = (event) => {
    let interim = ''
    for (let i = event.resultIndex; i < event.results.length; i++) {
      const result = event.results[i]
      if (result.isFinal) {
        transcript.value += normalizeTranscript(result[0].transcript)
      } else {
        interim += result[0].transcript
      }
    }
    interimText.value = interim
  }

  r.onerror = (event) => {
    error.value = `Chyba rozpoznávání: ${event.error}`
    isRecording.value = false
  }

  r.onend = () => {
    isRecording.value = false
    interimText.value = ''
  }

  return r
}

function startRecording() {
  error.value = null
  recognition = buildRecognition()
  if (!recognition) return
  recognition.start()
  isRecording.value = true
}

function stopRecording() {
  if (recognition) {
    recognition.stop()
    recognition = null
  }
  isRecording.value = false
}

function abortRecognition() {
  if (recognition) {
    recognition.abort()
    recognition = null
  }
  isRecording.value = false
}

function cancel() {
  abortRecognition()
  emit('close')
}

// Start/stop recording in sync with dialog visibility.
watch(
  () => props.show,
  (show) => {
    if (show) {
      transcript.value = ''
      interimText.value = ''
      imageFile.value = null
      isRecording.value = false
      error.value = null
      // Reset the native file input so a previously selected file doesn't appear stale on re-open
      if (fileInputEl.value) fileInputEl.value.value = ''
      // Start recording automatically
      startRecording()
    } else {
      abortRecognition()
    }
  }
)

async function confirm() {
  abortRecognition()
  error.value = null

  const text = transcript.value.trim()
  const file = imageFile.value

  if (text) {
    try {
      await store.sendKeystroke(props.agentId, text + '\r')
      transcript.value = ''  // prevent double-send if image upload fails and user retries
    } catch (e) {
      error.value = `Chyba odeslání: ${e.message}`
      return
    }
  }

  if (file) {
    const formData = new FormData()
    formData.append('file', file)
    try {
      const res = await fetch(`/api/agents/${props.agentId}/upload`, {
        method: 'POST',
        body: formData,
      })
      if (!res.ok) {
        const body = await res.json().catch(() => ({}))
        error.value = body.error ?? `Chyba nahrávání: HTTP ${res.status}`
        return  // keep dialog open so user sees the error
      }
    } catch (e) {
      error.value = `Chyba nahrávání: ${e.message}`
      return
    }
  }

  emit('close')
}
</script>
