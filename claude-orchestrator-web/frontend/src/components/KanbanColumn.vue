<template>
  <div class="flex-1 flex flex-col overflow-hidden border-r border-gray-800 last:border-r-0">
    <!-- Column header -->
    <div class="flex items-center gap-2 px-3 py-2 border-b border-gray-800 flex-shrink-0">
      <span class="text-xs font-semibold uppercase tracking-wide" :class="color">{{ title }}</span>
      <span class="text-xs text-gray-600 bg-gray-800 rounded-full px-2">{{ count }}</span>
    </div>
    <!-- Scrollable card list -->
    <div class="flex-1 overflow-y-auto p-2 flex flex-col gap-2">
      <slot />
    </div>
  </div>
</template>

<script setup>
defineProps({
  title: String,
  count: Number,
  color: { type: String, default: 'text-gray-400' },
})
</script>
