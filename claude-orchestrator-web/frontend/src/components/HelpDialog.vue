<template>
  <div v-if="show" class="fixed inset-0 z-50 flex items-center justify-center">
    <div class="absolute inset-0 bg-black/60" @click="$emit('close')"></div>
    <div class="relative bg-gray-800 rounded-xl border border-gray-700 w-[460px] max-w-[95vw] mx-4 p-5 flex flex-col gap-4">
      <div class="flex items-center justify-between">
        <h3 class="text-sm font-semibold text-gray-200">Keyboard shortcuts</h3>
        <button
          @click="$emit('close')"
          class="text-gray-500 hover:text-white text-lg leading-none transition-colors"
          aria-label="Close"
        >&times;</button>
      </div>

      <div v-for="group in groups" :key="group.title" class="flex flex-col gap-1.5">
        <div class="text-[11px] font-semibold uppercase tracking-wide text-gray-500">{{ group.title }}</div>
        <div
          v-for="row in group.rows"
          :key="row.desc"
          class="flex items-center justify-between gap-4 text-xs"
        >
          <span class="text-gray-300">{{ row.desc }}</span>
          <span class="flex items-center gap-1 flex-shrink-0">
            <template v-for="(combo, ci) in row.combos" :key="ci">
              <span v-if="ci > 0" class="text-gray-600">/</span>
              <template v-for="(token, ti) in combo" :key="ti">
                <span v-if="ti > 0" class="text-gray-600 text-[10px]">+</span>
                <kbd class="px-1.5 py-0.5 rounded border border-gray-600 bg-gray-900 text-gray-200 text-[11px] font-mono">{{ token }}</kbd>
              </template>
            </template>
          </span>
        </div>
      </div>

      <p class="text-[11px] text-gray-500 border-t border-gray-700 pt-3">
        In the agent terminal Claude captures the mouse, so plain drag doesn't select &mdash; hold <kbd class="px-1 py-0.5 rounded border border-gray-600 bg-gray-900 text-gray-300 text-[10px] font-mono">Shift</kbd> while dragging to select text.
      </p>
    </div>
  </div>
</template>

<script setup>
import { watch, onBeforeUnmount } from 'vue'

const props = defineProps({
  show: { type: Boolean, default: false },
})

const emit = defineEmits(['close'])

const groups = [
  {
    title: 'Agent terminal',
    rows: [
      { desc: 'Select text', combos: [['Shift', 'drag']] },
      { desc: 'Copy selection', combos: [['Ctrl', 'C']] },
      { desc: 'Paste', combos: [['Ctrl', 'V']] },
      { desc: 'Search in terminal', combos: [['Ctrl', 'F']] },
      { desc: 'Next / previous match', combos: [['Enter'], ['Shift', 'Enter']] },
      { desc: 'Close search', combos: [['Esc']] },
    ],
  },
  {
    title: 'Command bar',
    rows: [
      { desc: 'Send command to agent', combos: [['Enter']] },
      { desc: 'Command history', combos: [['↑'], ['↓']] },
    ],
  },
]

function onKeydown(e) {
  if (e.key === 'Escape') emit('close')
}

watch(() => props.show, (val) => {
  if (val) window.addEventListener('keydown', onKeydown)
  else window.removeEventListener('keydown', onKeydown)
})

onBeforeUnmount(() => window.removeEventListener('keydown', onKeydown))
</script>
