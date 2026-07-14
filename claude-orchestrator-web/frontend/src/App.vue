<template>
  <div class="flex flex-col h-screen">
    <!-- Header with navigation tabs -->
    <header class="flex items-center justify-between px-4 py-2 bg-gray-900 border-b border-gray-700 flex-shrink-0">
      <div class="flex items-center gap-4">
        <h1 class="text-sm font-bold text-blue-400">⚡ Claude Agent Orchestrator</h1>
        <nav class="flex gap-1">
          <RouterLink
            to="/"
            class="px-3 py-1 text-xs rounded transition-colors"
            :class="$route.path === '/' ? 'bg-blue-600 text-white' : 'text-gray-400 hover:text-white hover:bg-gray-700'"
          >
            Agents
          </RouterLink>
          <RouterLink
            to="/tasks"
            class="px-3 py-1 text-xs rounded transition-colors"
            :class="$route.path === '/tasks' ? 'bg-blue-600 text-white' : 'text-gray-400 hover:text-white hover:bg-gray-700'"
          >
            Tasks
          </RouterLink>
          <RouterLink
            to="/priorities"
            class="px-3 py-1 text-xs rounded transition-colors"
            :class="$route.path === '/priorities' ? 'bg-blue-600 text-white' : 'text-gray-400 hover:text-white hover:bg-gray-700'"
          >
            Priorities
          </RouterLink>
          <RouterLink
            to="/history"
            class="px-3 py-1 text-xs rounded transition-colors"
            :class="$route.path === '/history' ? 'bg-blue-600 text-white' : 'text-gray-400 hover:text-white hover:bg-gray-700'"
          >
            History
          </RouterLink>
          <RouterLink
            to="/worktrees"
            class="px-3 py-1 text-xs rounded transition-colors"
            :class="$route.path === '/worktrees' ? 'bg-blue-600 text-white' : 'text-gray-400 hover:text-white hover:bg-gray-700'"
          >
            Worktrees
          </RouterLink>
          <RouterLink
            to="/reader"
            class="px-3 py-1 text-xs rounded transition-colors"
            :class="$route.path === '/reader' ? 'bg-blue-600 text-white' : 'text-gray-400 hover:text-white hover:bg-gray-700'"
          >
            Reader
          </RouterLink>
        </nav>
      </div>
      <div class="flex items-center gap-4 text-xs text-gray-400">
        <span>{{ store.agentList.length }} agents</span>
        <span>{{ runningCount }} running</span>
        <button
          @click="showHelp = true"
          class="w-5 h-5 flex items-center justify-center rounded-full border border-gray-600 text-gray-400 hover:text-white hover:border-gray-400 transition-colors"
          title="Keyboard shortcuts"
          aria-label="Keyboard shortcuts"
        >?</button>
      </div>
    </header>

    <!-- Route content fills remaining space -->
    <RouterView class="flex-1 overflow-hidden flex flex-col" />

    <HelpDialog :show="showHelp" @close="showHelp = false" />
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { useAgentsStore } from './stores/agents'
import HelpDialog from './components/HelpDialog.vue'

const store = useAgentsStore()

const showHelp = ref(false)

const runningCount = computed(() =>
  store.agentList.filter(a => a.status === 'Running').length
)

onMounted(async () => {
  await store.connect()
  if ('Notification' in window && Notification.permission === 'default')
    Notification.requestPermission()
})
</script>
