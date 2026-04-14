<template>
  <aside
    class="relative flex flex-col flex-shrink-0 bg-gray-900 border-r border-gray-700 overflow-y-auto"
    :style="{ width: width + 'px' }"
  >
    <div class="py-2">
      <ReaderToc :headings="headings" :active-id="activeHeadingId" @navigate="$emit('navigate', $event)" />
    </div>
    <div class="border-t border-gray-800 py-2">
      <ReaderRecent :items="recent" @open="$emit('open-recent', $event)" @remove="$emit('remove-recent', $event)" />
    </div>
    <!-- Resize handle -->
    <div
      class="absolute top-0 right-0 h-full w-1 cursor-col-resize hover:bg-blue-500/40"
      @mousedown.prevent="startResize"
    />
  </aside>
</template>

<script setup>
import ReaderToc from './ReaderToc.vue'
import ReaderRecent from './ReaderRecent.vue'

const props = defineProps({
  width: { type: Number, required: true },
  headings: { type: Array, required: true },
  activeHeadingId: { type: String, default: null },
  recent: { type: Array, required: true },
})
const emit = defineEmits(['resize', 'navigate', 'open-recent', 'remove-recent'])

function startResize(e) {
  const startX = e.clientX
  const startW = props.width
  function move(ev) { emit('resize', startW + (ev.clientX - startX)) }
  function up() {
    window.removeEventListener('mousemove', move)
    window.removeEventListener('mouseup', up)
  }
  window.addEventListener('mousemove', move)
  window.addEventListener('mouseup', up)
}
</script>
