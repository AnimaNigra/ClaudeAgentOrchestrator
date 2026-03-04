<template>
  <div class="flex flex-col flex-1 overflow-hidden bg-gray-950">
    <!-- Toolbar -->
    <div class="flex items-center gap-2 px-4 py-2 border-b border-gray-800 flex-shrink-0">
      <input
        v-model="newText"
        @keydown.enter="handleAdd"
        placeholder="Přidat prioritu..."
        class="flex-1 bg-gray-800 text-sm text-white placeholder-gray-500 rounded px-3 py-1 outline-none focus:ring-1 focus:ring-blue-500"
      />
      <button
        @click="handleAdd"
        class="text-xs bg-blue-600 hover:bg-blue-500 text-white px-3 py-1 rounded transition-colors"
      >+</button>
    </div>

    <!-- List -->
    <div class="flex-1 overflow-y-auto px-4 py-2 space-y-1">
      <div
        v-for="item in store.items"
        :key="item.id"
        class="flex items-center gap-2 bg-gray-900 rounded px-3 py-2 group"
        :class="{ 'opacity-50': item.done }"
        draggable="true"
        @dragstart="onDragStart(item)"
        @dragover.prevent
        @drop="onDrop(item)"
      >
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
        Žádné priority. Přidej první.
      </p>
    </div>
  </div>
</template>

<script setup>
import { ref, nextTick, onMounted } from 'vue'
import { usePrioritiesStore } from '../stores/priorities'

const store = usePrioritiesStore()

const newText = ref('')
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

  // Swap orders
  const reorderList = store.items.map(i => ({ id: i.id, order: i.order }))
  const dragEntry  = reorderList.find(r => r.id === dragItem.id)
  const dropEntry  = reorderList.find(r => r.id === targetItem.id)
  if (!dragEntry || !dropEntry) return

  const tempOrder = dragEntry.order
  dragEntry.order = dropEntry.order
  dropEntry.order = tempOrder

  await store.reorder(reorderList)
  await store.load() // refresh sorted order
  dragItem = null
}

onMounted(() => store.load())
</script>
