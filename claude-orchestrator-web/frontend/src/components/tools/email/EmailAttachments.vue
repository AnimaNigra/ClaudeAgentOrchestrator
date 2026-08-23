<template>
  <div v-if="attachments.length" class="flex flex-col gap-1">
    <span class="text-xs text-gray-500">Přílohy ({{ attachments.length }})</span>
    <ul class="flex flex-col gap-1">
      <li
        v-for="attachment in attachments"
        :key="attachment.index"
        class="flex items-center gap-2 text-xs"
      >
        <button
          class="px-2 py-0.5 rounded border border-gray-600 text-gray-400 hover:text-white hover:border-gray-400 transition-colors"
          @click="emit('download', attachment.index)"
        >Stáhnout</button>
        <span class="text-gray-200 break-all">{{ attachment.fileName }}</span>
        <span class="text-gray-500 whitespace-nowrap">{{ attachment.contentType }}</span>
        <span class="text-gray-500 whitespace-nowrap">{{ formatSize(attachment.sizeBytes) }}</span>
      </li>
    </ul>
  </div>
</template>

<script setup>
defineProps({
  attachments: { type: Array, required: true },
})

const emit = defineEmits(['download'])

// Stahování obstarává EmailTool — tahle komponenta jen řekne, o kterou
// přílohu jde, aby zůstala bezstavová a testovatelná bez sítě.
function formatSize(bytes) {
  if (bytes < 1000) return `${bytes} B`
  const units = ['kB', 'MB', 'GB']
  let value = bytes / 1000
  let unit = 0
  while (value >= 1000 && unit < units.length - 1) {
    value /= 1000
    unit++
  }
  return `${value.toFixed(1).replace('.', ',')} ${units[unit]}`
}
</script>
