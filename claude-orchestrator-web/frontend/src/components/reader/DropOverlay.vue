<template>
  <div
    data-testid="drop-overlay"
    :class="['absolute inset-0 z-30 flex items-center justify-center transition-opacity',
             active ? 'opacity-100' : 'invisible opacity-0 pointer-events-none']"
    @dragenter.prevent="onEnter"
    @dragover.prevent
    @dragleave.prevent="onLeave"
    @drop.prevent="onDrop"
  >
    <div class="pointer-events-none px-6 py-4 rounded-lg border-2 border-dashed border-blue-400 bg-gray-900/80 text-blue-200 text-sm">
      Drop Markdown file to preview (lite mode — images disabled)
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, onBeforeUnmount } from 'vue'

const emit = defineEmits(['file-dropped'])
const active = ref(false)

function onEnter(e) {
  if (Array.from(e.dataTransfer?.types || []).includes('Files')) active.value = true
}
function onLeave() { active.value = false }
function onDrop(e) {
  active.value = false
  const file = e.dataTransfer?.files?.[0]
  if (file) emit('file-dropped', file)
}

// Hook window-level drag so the overlay can catch drops anywhere on the view
function onWindowDragEnter(e) {
  if (Array.from(e.dataTransfer?.types || []).includes('Files')) active.value = true
}
function onWindowDragEnd() { active.value = false }

onMounted(() => {
  window.addEventListener('dragenter', onWindowDragEnter)
  window.addEventListener('dragend', onWindowDragEnd)
})
onBeforeUnmount(() => {
  window.removeEventListener('dragenter', onWindowDragEnter)
  window.removeEventListener('dragend', onWindowDragEnd)
})
</script>
