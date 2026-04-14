<template>
  <div
    v-if="open"
    class="fixed inset-0 z-40 flex items-center justify-center bg-black/50"
    @click.self="$emit('close')"
  >
    <div class="bg-gray-800 text-gray-100 rounded-lg shadow-xl w-[520px] p-4">
      <h2 class="text-sm font-semibold mb-2">Open Markdown file</h2>
      <label class="block text-xs text-gray-400 mb-1">Absolute path</label>
      <input
        data-testid="open-path-input"
        v-model="path"
        class="w-full px-2 py-1 text-sm bg-gray-900 border border-gray-700 rounded focus:outline-none focus:border-blue-500"
        placeholder="C:\path\to\file.md"
        @keydown.enter="submit"
      />
      <div class="flex gap-2 mt-3 text-xs">
        <button
          class="px-2 py-1 bg-gray-700 hover:bg-gray-600 rounded"
          @click="pasteFromClipboard"
        >Paste from clipboard</button>
        <label class="px-2 py-1 bg-gray-700 hover:bg-gray-600 rounded cursor-pointer" @click="onBrowseClick">
          Browse…
          <input type="file" class="hidden" accept=".md,.markdown,.mdx,.txt" @change="onBrowse" />
        </label>
        <div class="flex-1"></div>
        <button
          data-testid="open-cancel"
          class="px-3 py-1 bg-gray-700 hover:bg-gray-600 rounded"
          @click="$emit('close')"
        >Cancel</button>
        <button
          data-testid="open-submit"
          class="px-3 py-1 bg-blue-600 hover:bg-blue-500 disabled:opacity-40 rounded"
          :disabled="!path.trim()"
          @click="submit"
        >Open</button>
      </div>
      <p class="text-[10px] text-gray-500 mt-2">
        <b>Path</b>: full mode with images + live reload. <b>Browse</b>: quick lite-mode preview (images disabled — browser security hides the full path).
      </p>
    </div>
  </div>
</template>

<script setup>
import { ref, watch } from 'vue'
import { isSupported as fsaSupported, pickFileWithHandle } from '../../services/fsaStore.js'

const props = defineProps({ open: Boolean })
const emit = defineEmits(['submit', 'submit-file', 'close'])
const path = ref('')

watch(() => props.open, (v) => { if (v) path.value = '' })

function submit() {
  const t = path.value.trim()
  if (!t) return
  emit('submit', t)
}

async function pasteFromClipboard() {
  try {
    const text = await navigator.clipboard.readText()
    if (text) path.value = text.trim()
  } catch {}
}

// When FSA API is available, intercept the label click and use showOpenFilePicker
// so we can capture a FileSystemFileHandle for persistent reopen.
async function onBrowseClick(e) {
  if (!fsaSupported()) return  // fall through to native <input type="file">
  e.preventDefault()
  try {
    const res = await pickFileWithHandle()
    if (res) emit('submit-file', res.file, res.handle)
  } catch (err) {
    console.error(err)
  }
}

function onBrowse(e) {
  const f = e.target.files?.[0]
  if (f) emit('submit-file', f, null)  // no FSA handle — lite reopen will need another picker
  e.target.value = ''
}
</script>
