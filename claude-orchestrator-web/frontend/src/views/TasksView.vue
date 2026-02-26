<template>
  <div class="flex flex-col flex-1 overflow-hidden bg-gray-950">
    <!-- Toolbar -->
    <div class="flex items-center justify-between px-4 py-2 border-b border-gray-800 flex-shrink-0">
      <span class="text-xs text-gray-500">{{ store.tasks.length }} tasks</span>
      <button
        @click="showNewTask = true"
        class="text-xs bg-blue-600 hover:bg-blue-500 text-white px-3 py-1 rounded transition-colors"
      >
        + New Task
      </button>
    </div>

    <!-- Error toast -->
    <div v-if="toastError" class="mx-4 mt-2 flex-shrink-0 bg-red-900/60 border border-red-700 text-red-300 text-xs rounded px-3 py-2">
      {{ toastError }}
    </div>

    <!-- Kanban columns -->
    <div class="flex flex-1 overflow-hidden">
      <!-- TODO -->
      <KanbanColumn title="Todo" :count="store.todoTasks.length" color="text-gray-400">
        <TaskCard
          v-for="task in store.todoTasks"
          :key="task.id"
          :task="task"
          :available-agents="availableAgents"
          :agent-record="findAgentRecord(task.agentId)"
          @assign="handleAssign"
          @mark-done="handleMarkDone"
          @delete="handleDelete"
          @resume="handleResume"
        />
        <button
          @click="showNewTask = true"
          class="text-xs text-gray-600 hover:text-gray-400 mt-1 py-1 w-full text-left transition-colors"
        >
          + Add task
        </button>
      </KanbanColumn>

      <!-- IN PROGRESS -->
      <KanbanColumn title="In Progress" :count="store.inProgressTasks.length" color="text-yellow-400">
        <TaskCard
          v-for="task in store.inProgressTasks"
          :key="task.id"
          :task="task"
          :available-agents="availableAgents"
          :agent-record="findAgentRecord(task.agentId)"
          @assign="handleAssign"
          @mark-done="handleMarkDone"
          @delete="handleDelete"
          @resume="handleResume"
        />
      </KanbanColumn>

      <!-- DONE -->
      <KanbanColumn title="Done" :count="store.doneTasks.length" color="text-green-400">
        <TaskCard
          v-for="task in store.doneTasks"
          :key="task.id"
          :task="task"
          :available-agents="availableAgents"
          :agent-record="findAgentRecord(task.agentId)"
          @assign="handleAssign"
          @mark-done="handleMarkDone"
          @delete="handleDelete"
          @resume="handleResume"
        />
      </KanbanColumn>
    </div>

    <!-- New Task Modal -->
    <NewTaskModal
      :show="showNewTask"
      @close="showNewTask = false"
      @created="handleCreateTask"
    />
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useTasksStore } from '../stores/tasks'
import { useAgentsStore } from '../stores/agents'
import TaskCard from '../components/TaskCard.vue'
import NewTaskModal from '../components/NewTaskModal.vue'
import KanbanColumn from '../components/KanbanColumn.vue'

const router = useRouter()
const store = useTasksStore()
const agentsStore = useAgentsStore()

const showNewTask = ref(false)
const toastError = ref(null)
const agentHistory = ref([])

// Only Idle and Running agents can receive tasks
const availableAgents = computed(() =>
  agentsStore.agentList.filter(a => a.status === 'Idle' || a.status === 'Running')
)

function findAgentRecord(agentId) {
  if (!agentId) return null
  return agentHistory.value.find(r => r.id === agentId) ?? null
}

function showError(msg) {
  toastError.value = msg
  setTimeout(() => { toastError.value = null }, 4000)
}

async function loadHistory() {
  try {
    const res = await fetch('/api/history')
    agentHistory.value = await res.json()
  } catch { /* ignore */ }
}

async function handleCreateTask({ title, description, prompt }) {
  try {
    await store.createTask(title, description, prompt)
    showNewTask.value = false
  } catch {
    showError('Failed to create task')
  }
}

async function handleAssign({ taskId, agentId }) {
  try {
    await store.assignTask(taskId, agentId)
  } catch (e) {
    showError(e.message ?? 'Failed to assign task')
  }
}

async function handleMarkDone(taskId) {
  try {
    await store.markDone(taskId)
  } catch {
    showError('Failed to update task')
  }
}

async function handleDelete(taskId) {
  try {
    await store.deleteTask(taskId)
  } catch {
    showError('Failed to delete task')
  }
}

async function handleResume({ agentName, sessionId }) {
  try {
    await agentsStore.spawnAgent((agentName ?? 'agent') + '-resumed', null, sessionId)
    router.push('/')
  } catch (e) {
    showError(e.message ?? 'Failed to resume agent')
  }
}

onMounted(async () => {
  await store.loadTasks()
  await loadHistory()
})
</script>
