<template>
  <button
    class="px-2 py-0.5 text-xs rounded border transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
    :class="copied
      ? 'border-green-500 text-green-400'
      : 'border-gray-600 text-gray-400 hover:text-white hover:border-gray-400'"
    :disabled="!value"
    @click="copy"
  >{{ copied ? 'Zkopírováno' : label }}</button>
</template>

<script setup>
import { ref, onBeforeUnmount } from 'vue'

const props = defineProps({
  value: { type: String, default: '' },
  label: { type: String, default: 'Kopírovat' },
})

const copied = ref(false)
let timer = null

async function copy() {
  if (!props.value) return
  await navigator.clipboard.writeText(props.value)
  copied.value = true
  clearTimeout(timer)
  timer = setTimeout(() => { copied.value = false }, 1500)
}

onBeforeUnmount(() => clearTimeout(timer))
</script>
