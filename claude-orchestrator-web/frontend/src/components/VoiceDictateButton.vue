<template>
  <button
    @click="$emit('open')"
    title="Voice dictation"
    class="absolute bottom-4 right-4 z-10 w-12 h-12 rounded-full bg-gray-800 border border-gray-600 hover:bg-gray-700 hover:border-blue-500 transition-colors flex items-center justify-center shadow-lg"
  >
    <svg xmlns="http://www.w3.org/2000/svg" class="w-5 h-5 text-gray-300" viewBox="0 0 24 24" fill="currentColor">
      <path d="M12 1a4 4 0 0 1 4 4v6a4 4 0 0 1-8 0V5a4 4 0 0 1 4-4zm-1.5 14.95A7.002 7.002 0 0 1 5 9H3a9.002 9.002 0 0 0 8 8.94V20H8v2h8v-2h-3v-2.05A9.002 9.002 0 0 0 21 9h-2a7 7 0 0 1-5.5 6.95z"/>
    </svg>
  </button>
</template>

<script setup>
defineEmits(['open'])
</script>
