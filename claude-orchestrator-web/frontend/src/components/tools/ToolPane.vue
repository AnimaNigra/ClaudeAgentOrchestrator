<template>
  <section class="flex flex-col flex-1 gap-3 p-4 overflow-y-auto">
    <div>
      <h2 class="text-sm font-semibold text-blue-400">{{ title }}</h2>
      <p v-if="hint" class="mt-1 text-xs text-gray-500">{{ hint }}</p>
    </div>
    <slot />
  </section>
</template>

<script setup>
defineProps({
  title: { type: String, required: true },
  hint: { type: String, default: '' },
})
</script>
