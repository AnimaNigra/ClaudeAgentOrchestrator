import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

export const useTasksStore = defineStore('tasks', () => {
  const tasks = ref([])
  const loading = ref(false)
  const error = ref(null)

  const todoTasks = computed(() => tasks.value.filter(t => t.status === 'todo'))
  const inProgressTasks = computed(() => tasks.value.filter(t => t.status === 'in-progress'))
  const doneTasks = computed(() => tasks.value.filter(t => t.status === 'done'))

  async function loadTasks() {
    loading.value = true
    error.value = null
    try {
      const res = await fetch('/api/tasks')
      tasks.value = await res.json()
    } catch (e) {
      error.value = 'Failed to load tasks'
    } finally {
      loading.value = false
    }
  }

  async function createTask(title, description, prompt) {
    const res = await fetch('/api/tasks', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ title, description, prompt }),
    })
    if (!res.ok) throw new Error('Failed to create task')
    const task = await res.json()
    tasks.value.push(task)
    return task
  }

  async function updateTask(id, updates) {
    const task = tasks.value.find(t => t.id === id)
    if (!task) return
    const res = await fetch(`/api/tasks/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        title: updates.title ?? task.title,
        description: updates.description !== undefined ? updates.description : task.description,
        prompt: updates.prompt !== undefined ? updates.prompt : task.prompt,
        status: updates.status ?? task.status,
        agentId: updates.agentId !== undefined ? updates.agentId : task.agentId,
        agentName: updates.agentName !== undefined ? updates.agentName : task.agentName,
      }),
    })
    if (!res.ok) throw new Error('Failed to update task')
    const updated = await res.json()
    const idx = tasks.value.findIndex(t => t.id === id)
    if (idx >= 0) tasks.value[idx] = updated
    return updated
  }

  async function deleteTask(id) {
    await fetch(`/api/tasks/${id}`, { method: 'DELETE' })
    tasks.value = tasks.value.filter(t => t.id !== id)
  }

  async function assignTask(taskId, agentId) {
    const res = await fetch(`/api/tasks/${taskId}/assign`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ agentId }),
    })
    if (!res.ok) {
      const err = await res.json()
      throw new Error(err.error ?? 'Failed to assign task')
    }
    const updated = await res.json()
    const idx = tasks.value.findIndex(t => t.id === taskId)
    if (idx >= 0) tasks.value[idx] = updated
    return updated
  }

  async function markDone(taskId) {
    return updateTask(taskId, { status: 'done' })
  }

  return {
    tasks, loading, error,
    todoTasks, inProgressTasks, doneTasks,
    loadTasks, createTask, updateTask, deleteTask, assignTask, markDone,
  }
})
