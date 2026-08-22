<template>
  <ToolPane :title="title" :hint="hint">
    <div class="flex gap-1">
      <button
        v-for="option in MODES"
        :key="option.value"
        class="px-3 py-1 text-xs rounded transition-colors"
        :class="mode === option.value
          ? 'bg-blue-600 text-white'
          : 'bg-gray-800 text-gray-400 hover:text-white hover:bg-gray-700'"
        @click="mode = option.value"
      >{{ option.label }}</button>
    </div>

    <textarea
      v-model="input"
      rows="6"
      spellcheck="false"
      placeholder="Vstup"
      class="w-full px-2 py-1 font-mono text-xs bg-gray-950 border border-gray-700 rounded text-gray-200 focus:outline-none focus:border-blue-500 resize-y"
    />

    <div class="flex items-center gap-2">
      <span class="text-xs text-gray-500">Výstup</span>
      <CopyButton :value="result.ok ? result.value : ''" />
    </div>

    <textarea
      :value="result.ok ? result.value : result.error"
      rows="6"
      readonly
      spellcheck="false"
      class="w-full px-2 py-1 font-mono text-xs bg-gray-950 border rounded resize-y focus:outline-none"
      :class="result.ok ? 'border-gray-700 text-gray-200' : 'border-red-700 text-red-400'"
    />
  </ToolPane>
</template>

<script setup>
import { computed, ref } from 'vue'
import ToolPane from './ToolPane.vue'
import CopyButton from './CopyButton.vue'

const props = defineProps({
  title: { type: String, required: true },
  hint: { type: String, default: '' },
  encodeFn: { type: Function, required: true },
  decodeFn: { type: Function, required: true },
})

const MODES = [
  { value: 'encode', label: 'Zakódovat' },
  { value: 'decode', label: 'Dekódovat' },
]

const mode = ref('encode')
const input = ref('')

const result = computed(() =>
  mode.value === 'encode' ? props.encodeFn(input.value) : props.decodeFn(input.value))
</script>
