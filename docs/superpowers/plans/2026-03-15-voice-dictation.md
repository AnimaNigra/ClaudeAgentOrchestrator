# Voice Dictation Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a floating microphone button to the Agents view that opens a dictation dialog, lets the user dictate and optionally attach an image, then sends the result to the active agent.

**Architecture:** Two new Vue components (`VoiceDictateButton`, `VoiceDictateDialog`) integrated into `AgentsView.vue`. Uses the browser's Web Speech API for transcription (Chrome/Edge only). Text is sent via the existing `store.sendKeystroke()`, images via the existing `POST /api/agents/{id}/upload` endpoint.

**Tech Stack:** Vue 3 (Options-free Composition API with `<script setup>`), Pinia store (`useAgentsStore`), Tailwind CSS, Web Speech API (`window.SpeechRecognition`)

---

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `frontend/src/components/VoiceDictateButton.vue` | Create | Floating mic button, emits `open` |
| `frontend/src/components/VoiceDictateDialog.vue` | Create | Modal dialog: speech recognition, transcript editing, image picker, confirm |
| `frontend/src/views/AgentsView.vue` | Modify | Add `relative` to `<main>`, integrate both new components |

---

## Chunk 1: VoiceDictateButton and AgentsView wiring

### Task 1: Create `VoiceDictateButton.vue`

**Files:**
- Create: `claude-orchestrator-web/frontend/src/components/VoiceDictateButton.vue`

- [ ] **Step 1: Create the component**

```vue
<template>
  <button
    @click="$emit('open')"
    title="Voice dictation"
    class="absolute bottom-4 right-4 z-10 w-12 h-12 rounded-full bg-gray-800 border border-gray-600 hover:bg-gray-700 hover:border-blue-500 transition-colors flex items-center justify-center shadow-lg"
  >
    <svg xmlns="http://www.w3.org/2000/svg" class="w-5 h-5 text-gray-300" viewBox="0 0 24 24" fill="currentColor">
      <path d="M12 1a4 4 0 0 1 4 4v6a4 4 0 0 1-8 0V5a4 4 0 0 1 4-4zm-1.5 14.95A7.002 7.002 0 0 1 5 9H3a9.002 9.002 0 0 0 8 8.94V20H8v2h8v-2h-3v-2.05A9.002 9.002 0 0 0 21 9h-2a7 7 0 0 1-5.5 6.95z"/>
    </svg>
  </button>
</template>

<script setup>
defineEmits(['open'])
</script>
```

Save to `claude-orchestrator-web/frontend/src/components/VoiceDictateButton.vue`.

- [ ] **Step 2: Wire into `AgentsView.vue`**

Open `claude-orchestrator-web/frontend/src/views/AgentsView.vue`.

Make three changes:

**a) Add `relative` to `<main>` so the absolute button positions correctly:**
```html
<!-- Before -->
<main class="flex-1 overflow-hidden bg-black">

<!-- After -->
<main class="relative flex-1 overflow-hidden bg-black">
```

**b) Add the button inside `<main>` (after `<TerminalPanel />`), guarded by `activeAgentId`:**
```html
<main class="relative flex-1 overflow-hidden bg-black">
  <TerminalPanel />
  <VoiceDictateButton
    v-if="store.activeAgentId"
    @open="showVoiceDialog = true"
  />
</main>
```

**c) Add the import and the `showVoiceDialog` ref in `<script setup>`:**
```js
import VoiceDictateButton from '../components/VoiceDictateButton.vue'
// add after existing imports

const showVoiceDialog = ref(false)  // add after existing refs
```

- [ ] **Step 3: Verify button renders**

Run the dev server:
```bash
cd claude-orchestrator-web/frontend && npm run dev
```
Open `http://localhost:5173` in Chrome. Create an agent. Confirm the mic button appears bottom-right of the terminal. Confirm it is absent when no agent is selected.

- [ ] **Step 4: Commit**

```bash
git add claude-orchestrator-web/frontend/src/components/VoiceDictateButton.vue \
        claude-orchestrator-web/frontend/src/views/AgentsView.vue
git commit -m "feat: add VoiceDictateButton floating mic button"
```

---

## Chunk 2: VoiceDictateDialog — modal shell

### Task 2: Create `VoiceDictateDialog.vue` shell (no speech yet)

**Files:**
- Create: `claude-orchestrator-web/frontend/src/components/VoiceDictateDialog.vue`
- Modify: `claude-orchestrator-web/frontend/src/views/AgentsView.vue`

- [ ] **Step 1: Create the dialog shell**

Create `claude-orchestrator-web/frontend/src/components/VoiceDictateDialog.vue`:

```vue
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
```

- [ ] **Step 2: Add dialog to `AgentsView.vue`**

Add the import and the dialog component. The final `<script setup>` imports block:
```js
import VoiceDictateDialog from '../components/VoiceDictateDialog.vue'
```

Add the dialog in the template (after `<PermissionDialog />`):
```html
<VoiceDictateDialog
  :show="showVoiceDialog"
  :agent-id="store.activeAgentId"
  @close="showVoiceDialog = false"
/>
```

- [ ] **Step 3: Verify modal opens and closes**

In the browser: click the mic button → dialog appears. Click backdrop or Zrušit → dialog closes. Confirm button is disabled when textarea is empty and no file selected.

- [ ] **Step 4: Commit**

```bash
git add claude-orchestrator-web/frontend/src/components/VoiceDictateDialog.vue \
        claude-orchestrator-web/frontend/src/views/AgentsView.vue
git commit -m "feat: add VoiceDictateDialog modal shell"
```

---

## Chunk 3: Speech recognition + confirm logic

### Task 3: Add Web Speech API to `VoiceDictateDialog.vue`

**Files:**
- Modify: `claude-orchestrator-web/frontend/src/components/VoiceDictateDialog.vue`

- [ ] **Step 1: Add `normalizeTranscript` utility and recognition logic**

In `<script setup>`, replace the placeholder `startRecording` / `stopRecording` functions and add:

```js
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
```

Also update the `show` watcher to call `startRecording()` on open and `abortRecognition()` on close. Add a separate `abortRecognition` helper for Cancel (uses `abort()` instead of `stop()` to prevent `onend` from firing after dialog closes):

```js
function abortRecognition() {
  if (recognition) {
    recognition.abort()
    recognition = null
  }
  isRecording.value = false
}

// Update cancel() to use abort
function cancel() {
  abortRecognition()
  emit('close')
}

// Update the watch to also start recording on open
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
```

Remove the old stub `watch` and the old stub `startRecording`/`stopRecording` from the shell.

- [ ] **Step 2: Verify speech recognition**

In Chrome: open the dialog → browser asks for mic permission → grant it → speak a few words → transcript fills in real time. Speak a time like "deset třicet" (or "ten thirty") → confirm it appears as `10 30`, not `10:30`. Stop button halts recording. Restart button resumes and appends to transcript.

- [ ] **Step 3: Commit**

```bash
git add claude-orchestrator-web/frontend/src/components/VoiceDictateDialog.vue
git commit -m "feat: add Web Speech API and transcript normalization to VoiceDictateDialog"
```

### Task 4: Add confirm logic (send to agent)

**Files:**
- Modify: `claude-orchestrator-web/frontend/src/components/VoiceDictateDialog.vue`

- [ ] **Step 1: Replace placeholder `confirm()` with real logic**

```js
async function confirm() {
  abortRecognition()

  const text = transcript.value.trim()
  const file = imageFile.value

  if (text) {
    store.sendKeystroke(props.agentId, text + '\r')
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
```

- [ ] **Step 2: Verify end-to-end flow**

In Chrome with an active agent:

1. Click mic → dictate a sentence → click Odeslat → confirm the text appears in the terminal and the agent receives it.
2. Open dialog again → dictate → attach a small image → Odeslat → confirm text is sent first, then the image path appears in the terminal.
3. Open dialog → attach an image only (no text) → Odeslat → confirm image path appears in terminal.
4. Open dialog → click Zrušit → confirm nothing is sent to the agent.
5. Open dialog in a browser without Web Speech API (e.g. Firefox) → confirm the error message appears and the dialog remains usable (close still works).

- [ ] **Step 3: Commit**

```bash
git add claude-orchestrator-web/frontend/src/components/VoiceDictateDialog.vue
git commit -m "feat: wire confirm logic - send keystroke and image upload to agent"
```
