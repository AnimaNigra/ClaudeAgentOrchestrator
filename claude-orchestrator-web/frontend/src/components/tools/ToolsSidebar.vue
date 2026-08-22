<template>
  <aside class="flex flex-col flex-shrink-0 w-44 py-2 bg-gray-900 border-r border-gray-700 overflow-y-auto">
    <RouterLink
      v-for="tool in tools"
      :key="tool.slug"
      :to="`/tools/${tool.slug}`"
      class="px-3 py-1.5 text-xs transition-colors"
      :class="tool.slug === activeSlug
        ? 'bg-blue-600 text-white'
        : 'text-gray-400 hover:text-white hover:bg-gray-700'"
    >{{ tool.label }}</RouterLink>
  </aside>
</template>

<script setup>
defineProps({
  tools: { type: Array, required: true },
  activeSlug: { type: String, required: true },
})
</script>
