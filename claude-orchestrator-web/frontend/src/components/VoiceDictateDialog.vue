<template>
  <Teleport to="body">
    <div v-if="show" class="fixed inset-0 z-50 flex items-center justify-center">
      <!-- Backdrop -->
      <div class="absolute inset-0 bg-black/60" @click="cancel" />

      <!-- Dialog -->
      <div class="relative bg-gray-800 rounded-xl border border-gray-700 w-[640px] max-w-[95vw] mx-4 p-6 flex flex-col gap-4"
           @paste="onPaste">
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

        <!-- Image picker (agent mode only) -->
        <div v-if="agentId" class="flex flex-col gap-1">
          <label class="text-xs text-gray-400">Obrázky <span class="text-gray-600">(volitelné)</span></label>
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
            >
              Vybrat obrázek
            </button>
            <span class="text-xs text-gray-600">nebo Ctrl+V</span>
          </div>
          <!-- File list -->
          <div v-if="imageFiles.length" class="flex flex-wrap gap-1 mt-1">
            <span
              v-for="(f, i) in imageFiles" :key="i"
              class="inline-flex items-center gap-1 bg-gray-700/50 text-xs text-gray-400 px-2 py-0.5 rounded"
            >
              {{ f.name }}
              <button @click="removeImage(i)" class="text-gray-600 hover:text-red-400 transition-colors">✕</button>
            </span>
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
  agentId: { type: String, default: null },
})
const emit = defineEmits(['close', 'confirm'])

const store = useAgentsStore()

// State
const transcript = ref('')
const interimText = ref('')
const imageFiles = ref([])
const isRecording = ref(false)
const error = ref(null)
const fileInputEl = ref(null)

const canConfirm = computed(() => transcript.value.trim().length > 0 || imageFiles.value.length > 0)

function onFileChange(e) {
  for (const f of e.target.files) {
    imageFiles.value.push(f)
  }
}

function onPaste(e) {
  if (!props.agentId) return
  const items = e.clipboardData?.items
  if (!items) return
  for (const item of items) {
    if (item.type.startsWith('image/')) {
      e.preventDefault()
      const blob = item.getAsFile()
      if (blob) {
        const ext = item.type.split('/')[1] || 'png'
        const file = new File([blob], `paste_${Date.now()}.${ext}`, { type: item.type })
        imageFiles.value.push(file)
      }
      return
    }
  }
}

function removeImage(index) {
  imageFiles.value.splice(index, 1)
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
  r.lang = 'cs-CZ'
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
    interimText.value = ''
    // Web Speech API sometimes stops on its own (silence timeout).
    // Auto-restart unless the user explicitly stopped recording.
    if (isRecording.value && props.show) {
      try { r.start() } catch { isRecording.value = false }
    } else {
      isRecording.value = false
    }
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
  isRecording.value = false
  if (recognition) {
    recognition.stop()
    recognition = null
  }
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
// immediate: true ensures auto-start fires even if the dialog is mounted with show already true.
watch(
  () => props.show,
  (show) => {
    if (show) {
      transcript.value = ''
      interimText.value = ''
      imageFiles.value = []
      isRecording.value = false
      error.value = null
      // Reset the native file input so a previously selected file doesn't appear stale on re-open
      if (fileInputEl.value) fileInputEl.value.value = ''
      // Start recording automatically
      startRecording()
    } else {
      abortRecognition()
    }
  },
  { immediate: true }
)

async function confirm() {
  abortRecognition()
  error.value = null

  const text = transcript.value.trim()
  const files = [...imageFiles.value]

  // Text-only mode (no agent) — just emit transcript
  if (!props.agentId) {
    emit('confirm', text)
    emit('close')
    return
  }

  // Agent mode — upload images first, then send text
  for (const file of files) {
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
        return
      }
    } catch (e) {
      error.value = `Chyba nahrávání: ${e.message}`
      return
    }
  }

  if (text) {
    try {
      // Small delay so Claude processes the image attachment first
      if (files.length) await new Promise(r => setTimeout(r, 500))
      await store.sendKeystroke(props.agentId, text + '\r')
      transcript.value = ''
    } catch (e) {
      error.value = `Chyba odeslání: ${e.message}`
      return
    }
  }

  emit('close')
}
</script>
