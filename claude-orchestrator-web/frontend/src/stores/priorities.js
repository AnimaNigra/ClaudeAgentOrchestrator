import { defineStore } from 'pinia'
import { ref } from 'vue'

export const usePrioritiesStore = defineStore('priorities', () => {
  const items = ref([])

  async function load() {
    const res = await fetch('/api/priorities')
    if (!res.ok) throw new Error('Failed to load priorities')
    items.value = await res.json()
  }

  async function create(text) {
    const res = await fetch('/api/priorities', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ text }),
    })
    if (!res.ok) throw new Error('Failed to create priority')
    const item = await res.json()
    items.value.push(item)
    return item
  }

  async function update(id, patch) {
    const res = await fetch(`/api/priorities/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(patch),
    })
    if (!res.ok) throw new Error('Failed to update priority')
    const updated = await res.json()
    const idx = items.value.findIndex(i => i.id === id)
    if (idx >= 0) items.value[idx] = updated
    return updated
  }

  async function remove(id) {
    const res = await fetch(`/api/priorities/${id}`, { method: 'DELETE' })
    if (!res.ok) throw new Error('Failed to delete priority')
    items.value = items.value.filter(i => i.id !== id)
  }

  async function reorder(reorderList) {
    const res = await fetch('/api/priorities/reorder', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(reorderList),
    })
    if (!res.ok) throw new Error('Failed to reorder priorities')
  }

  return { items, load, create, update, remove, reorder }
})
