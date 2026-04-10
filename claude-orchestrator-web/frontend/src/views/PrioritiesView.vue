<template>
  <div class="flex flex-col flex-1 overflow-hidden bg-gray-950">
    <!-- Toolbar -->
    <div class="flex items-center gap-2 px-4 py-2 border-b border-gray-800 flex-shrink-0">
      <input
        v-model="newText"
        @keydown.enter="handleAdd"
        placeholder="Add priority..."
        class="flex-1 bg-gray-800 text-sm text-white placeholder-gray-500 rounded px-3 py-1 outline-none focus:ring-1 focus:ring-blue-500"
      />
      <button
        @click="handleAdd"
        class="text-xs bg-blue-600 hover:bg-blue-500 text-white px-3 py-1 rounded transition-colors"
      >+</button>
      <button
        @click="showVoiceDialog = true"
        class="text-xs bg-gray-700 hover:bg-gray-600 text-gray-300 px-2 py-1 rounded transition-colors"
        title="Voice dictation"
      >🎤</button>
    </div>

    <VoiceDictateDialog
      :show="showVoiceDialog"
      @confirm="onVoiceConfirm"
      @close="showVoiceDialog = false"
    />

    <!-- List -->
    <div class="flex-1 overflow-y-auto px-4 py-2 space-y-1">
      <div
        v-for="(item, index) in store.items"
        :key="item.id"
        class="flex items-center gap-2 bg-gray-900 rounded px-3 py-2 group"
        :class="{ 'opacity-50': item.done }"
        draggable="true"
        @dragstart="onDragStart(item)"
        @dragover.prevent
        @drop="onDrop(item)"
      >
        <!-- Row number -->
        <span class="text-gray-600 text-xs select-none w-5 text-right flex-shrink-0">{{ index + 1 }}.</span>

        <!-- Drag handle -->
        <span class="text-gray-600 cursor-grab select-none text-lg leading-none">⠿</span>

        <!-- Checkbox -->
        <input
          type="checkbox"
          :checked="item.done"
          @change="store.update(item.id, { done: !item.done })"
          class="accent-blue-500 cursor-pointer"
        />

        <!-- Text / inline edit -->
        <span
          v-if="editingId !== item.id"
          class="flex-1 text-sm"
          :class="{ 'line-through text-gray-500': item.done }"
        >{{ item.text }}</span>
        <input
          v-else
          v-model="editText"
          @keydown.enter="saveEdit(item.id)"
          @blur="saveEdit(item.id)"
          class="flex-1 bg-gray-800 text-sm text-white rounded px-2 py-0.5 outline-none focus:ring-1 focus:ring-blue-500"
          :ref="el => { if (el) editInputEl = el }"
        />

        <!-- Actions -->
        <div class="flex gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
          <button
            v-if="editingId !== item.id"
            @click="startEdit(item)"
            class="text-gray-500 hover:text-white text-xs px-1"
            title="Edit"
          >✏</button>
          <button
            @click="store.remove(item.id)"
            class="text-gray-500 hover:text-red-400 text-xs px-1"
            title="Delete"
          >🗑</button>
        </div>
      </div>

      <p v-if="store.items.length === 0" class="text-xs text-gray-600 py-4 text-center">
        No priorities yet. Add your first one.
      </p>
    </div>
  </div>
</template>

<script setup>
import { ref, nextTick, onMounted } from 'vue'
import { usePrioritiesStore } from '../stores/priorities'
import VoiceDictateDialog from '../components/VoiceDictateDialog.vue'

const store = usePrioritiesStore()

const newText = ref('')
const showVoiceDialog = ref(false)

async function onVoiceConfirm(text) {
  if (!text) return
  await store.create(text)
}
const editingId = ref(null)
const editText = ref('')
let editInputEl = null
let dragItem = null

async function handleAdd() {
  const text = newText.value.trim()
  if (!text) return
  await store.create(text)
  newText.value = ''
}

function startEdit(item) {
  editingId.value = item.id
  editText.value = item.text
  nextTick(() => editInputEl?.focus())
}

async function saveEdit(id) {
  const text = editText.value.trim()
  if (text) await store.update(id, { text })
  editingId.value = null
}

function onDragStart(item) {
  dragItem = item
}

async function onDrop(targetItem) {
  if (!dragItem || dragItem.id === targetItem.id) return

  // Insert-based reorder: remove dragged item, re-insert at target position
  const oldIndex = store.items.findIndex(i => i.id === dragItem.id)
  const targetIndexOriginal = store.items.findIndex(i => i.id === targetItem.id)
  if (oldIndex < 0 || targetIndexOriginal < 0) return

  const newItems = [...store.items]
  const [moved] = newItems.splice(oldIndex, 1)
  // Splicing at targetIndexOriginal gives the desired behavior in both directions:
  //  - dragging down: target shifted left by 1 after removal, so targetIndexOriginal inserts AFTER it
  //  - dragging up:   target kept its index, so targetIndexOriginal inserts BEFORE it
  newItems.splice(targetIndexOriginal, 0, moved)

  // Assign fresh sequential order values (0..N)
  const reorderList = newItems.map((item, idx) => ({ id: item.id, order: idx }))

  await store.reorder(reorderList)
  await store.load() // refresh sorted order
  dragItem = null
}

onMounted(() => store.load())
</script>
