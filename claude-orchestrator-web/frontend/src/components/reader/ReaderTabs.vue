<template>
  <div class="flex items-center gap-1 overflow-x-auto">
    <div
      v-for="t in tabs"
      :key="t.id"
      data-testid="reader-tab"
      :class="['flex items-center gap-1 px-2 py-1 text-xs rounded cursor-pointer select-none',
               t.id === activeTabId
                 ? 'bg-gray-700 text-white is-active'
                 : 'bg-gray-800 text-gray-400 hover:bg-gray-700']"
      @click="$emit('activate', t.id)"
    >
      <span v-if="t.mode === 'lite'" title="Lite mode (no images/reload)">·</span>
      <span class="truncate max-w-[12rem]">{{ t.displayName }}</span>
      <button
        data-testid="reader-tab-close"
        class="text-gray-500 hover:text-white"
        @click.stop="$emit('close', t.id)"
      >×</button>
    </div>
    <button
      data-testid="reader-tab-new"
      class="px-2 py-1 text-xs text-gray-400 hover:text-white"
      @click="$emit('open')"
    >+</button>
  </div>
</template>

<script setup>
defineProps({
  tabs: { type: Array, required: true },
  activeTabId: { type: String, default: null },
})
defineEmits(['activate', 'close', 'open'])
</script>
