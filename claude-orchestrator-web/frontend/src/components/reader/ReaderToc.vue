<template>
  <div class="text-xs">
    <h3 class="px-2 py-1 text-gray-500 uppercase tracking-wide">Contents</h3>
    <div v-if="!headings.length" class="px-2 py-2 text-gray-500 italic">No headings</div>
    <a
      v-for="h in headings"
      :key="h.id"
      href="#"
      data-testid="toc-item"
      :data-level="h.level"
      :class="['block py-0.5 truncate hover:text-white',
               indentClass(h.level),
               h.id === activeId ? 'is-active text-blue-400' : 'text-gray-400']"
      @click.prevent="$emit('navigate', h.id)"
    >{{ h.text }}</a>
  </div>
</template>

<script setup>
defineProps({
  headings: { type: Array, required: true },
  activeId: { type: String, default: null },
})
defineEmits(['navigate'])

function indentClass(level) {
  return ['pl-2', 'pl-4', 'pl-6', 'pl-8', 'pl-10', 'pl-12'][Math.min(level - 1, 5)]
}
</script>
