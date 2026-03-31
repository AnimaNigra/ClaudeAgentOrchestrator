<template>
  <div v-if="show" class="fixed inset-0 z-50 flex items-center justify-center">
    <div class="absolute inset-0 bg-black/60" @click="$emit('close')"></div>
    <div class="relative bg-gray-800 rounded-xl border border-gray-700 w-[420px] max-w-[95vw] mx-4 p-5 flex flex-col gap-4">
      <h3 class="text-sm font-semibold text-gray-200">{{ title }}</h3>

      <p v-if="message" class="text-xs text-gray-400 whitespace-pre-line">{{ message }}</p>

      <input
        v-if="type === 'prompt'"
        ref="inputEl"
        v-model="inputValue"
        :placeholder="placeholder"
        class="w-full text-sm text-white bg-gray-900 border border-gray-600 rounded px-3 py-2 focus:outline-none focus:border-blue-500 placeholder-gray-600"
        @keydown.enter="handleConfirm"
      />

      <div v-if="errorMsg" class="text-xs text-red-400">{{ errorMsg }}</div>

      <div class="flex items-center justify-end gap-2">
        <button
          @click="$emit('close')"
          class="px-4 py-1.5 text-sm text-gray-400 hover:text-white rounded transition-colors"
        >Cancel</button>
        <button
          @click="handleConfirm"
          class="px-4 py-1.5 text-sm rounded transition-colors"
          :class="danger
            ? 'bg-red-700 hover:bg-red-600 text-white'
            : 'bg-blue-600 hover:bg-blue-500 text-white'"
        >{{ confirmText }}</button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, watch, nextTick } from 'vue'

const props = defineProps({
  show: { type: Boolean, default: false },
  title: { type: String, default: '' },
  message: { type: String, default: '' },
  type: { type: String, default: 'confirm' }, // 'confirm' | 'prompt' | 'alert'
  placeholder: { type: String, default: '' },
  defaultValue: { type: String, default: '' },
  confirmText: { type: String, default: 'OK' },
  danger: { type: Boolean, default: false },
  errorMsg: { type: String, default: '' },
})

const emit = defineEmits(['close', 'confirm'])

const inputEl = ref(null)
const inputValue = ref('')

watch(() => props.show, async (val) => {
  if (val) {
    inputValue.value = props.defaultValue
    await nextTick()
    inputEl.value?.focus()
    inputEl.value?.select()
  }
})

function handleConfirm() {
  if (props.type === 'prompt') {
    if (!inputValue.value.trim()) return
    emit('confirm', inputValue.value.trim())
  } else {
    emit('confirm')
  }
}
</script>
