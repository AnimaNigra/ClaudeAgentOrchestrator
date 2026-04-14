<template>
  <div class="text-xs">
    <button
      data-testid="recent-toggle"
      class="w-full flex items-center justify-between px-2 py-1 text-gray-500 uppercase tracking-wide hover:text-white"
      @click="expanded = !expanded"
    >
      <span>Recent</span>
      <span>{{ expanded ? '▾' : '▸' }}</span>
    </button>
    <div v-if="expanded">
      <div
        v-for="r in items"
        :key="r.path"
        data-testid="recent-item"
        class="group flex items-center justify-between px-2 py-0.5 hover:bg-gray-800 text-gray-400 hover:text-white cursor-pointer"
        @click="$emit('open', r.path)"
      >
        <span class="truncate" :title="r.path">{{ r.displayName }}</span>
        <button
          data-testid="recent-remove"
          class="opacity-0 group-hover:opacity-100 text-gray-500 hover:text-white"
          @click.stop="$emit('remove', r.path)"
        >×</button>
      </div>
      <div v-if="!items.length" class="px-2 py-2 italic text-gray-500">No recent files</div>
    </div>
  </div>
</template>

<script setup>
import { ref } from 'vue'
defineProps({ items: { type: Array, required: true } })
defineEmits(['open', 'remove'])
const expanded = ref(true)
</script>
