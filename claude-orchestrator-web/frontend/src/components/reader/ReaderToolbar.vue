<template>
  <div class="flex items-center gap-2 px-3 py-2 bg-gray-900 border-b border-gray-800">
    <button
      class="px-3 py-1 text-xs bg-blue-600 hover:bg-blue-500 rounded text-white"
      @click="$emit('open')"
    >📂 Open file</button>
    <button
      class="px-3 py-1 text-xs bg-gray-700 hover:bg-gray-600 rounded text-gray-200"
      @click="$emit('print')"
    >🖨 Print</button>
    <div class="flex-1 min-w-0">
      <slot name="tabs" />
    </div>
  </div>
</template>

<script setup>
defineEmits(['open', 'print'])
</script>
