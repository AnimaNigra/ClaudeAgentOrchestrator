<template>
  <div
    class="agent-card"
    :class="[statusClass, { active: isActive }]"
    @click="$emit('select', agent.id)"
  >
    <div class="flex items-center justify-between mb-1">
      <div class="flex items-center gap-1.5 min-w-0">
        <span class="font-bold text-sm truncate">{{ agent.name }}</span>
        <span v-if="agent.worktreeBranch" class="text-[10px] text-emerald-400 bg-emerald-400/10 px-1.5 py-0.5 rounded flex-shrink-0" title="Running in worktree">🌿 {{ agent.worktreeBranch }}</span>
      </div>
      <span v-if="agent.status === 'Running'" class="spinner" title="Running"></span>
      <span v-else class="status-dot" :title="agent.status">{{ statusIcon }}</span>
    </div>

    <div class="text-xs text-gray-400 truncate mb-1 flex items-center gap-1" :title="agent.cwd ?? 'default'">
      <button
        @click.stop="openFolder"
        class="hover:text-blue-400 transition-colors flex-shrink-0"
        title="Open folder in explorer"
      >📁</button>
      <span class="truncate">{{ agent.cwd ?? 'default' }}</span>
    </div>

    <div class="text-xs truncate text-gray-300 min-h-[1rem]">
      {{ agent.lastMessage || '…' }}
    </div>

    <div class="flex items-center justify-between mt-2">
      <span class="text-xs text-gray-500">{{ agent.elapsedStr }}</span>
      <div class="flex items-center gap-2">
        <button
          v-if="!agent.worktreeBranch"
          class="text-[10px] text-gray-500 hover:text-emerald-400 px-1.5 py-0.5 rounded hover:bg-gray-700/50 transition-colors"
          @click.stop="$emit('create-worktree', agent)"
          title="Create a worktree clone of this agent's repo"
        >🌿 worktree</button>
        <button
          class="text-[10px] text-gray-500 hover:text-blue-400 px-1.5 py-0.5 rounded hover:bg-gray-700/50 transition-colors"
          @click.stop="$emit('review', agent.id)"
          title="Review git changes"
        >review</button>
        <span class="text-xs" :class="statusTextClass">{{ agent.status }}</span>
      </div>
    </div>

    <div v-if="agent.progressPct >= 0" class="mt-1">
      <div class="w-full bg-gray-700 rounded h-1">
        <div class="h-1 rounded bg-blue-500 transition-all" :style="{ width: agent.progressPct + '%' }"></div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({ agent: Object, isActive: Boolean })
defineEmits(['select', 'review', 'create-worktree'])

const STATUS_ICONS = {
  Running: '🟢', Idle: '🔵', Done: '✅', Error: '🔴', Blocked: '🟡'
}
const STATUS_TEXT = {
  Running: 'text-green-400', Idle: 'text-blue-400',
  Done: 'text-white', Error: 'text-red-400', Blocked: 'text-yellow-400'
}
const STATUS_BORDER = {
  Running: 'border-green-600', Idle: 'border-blue-700',
  Done: 'border-gray-600', Error: 'border-red-600', Blocked: 'border-yellow-600'
}

async function openFolder() {
  await fetch(`/api/agents/${props.agent.id}/open-folder`, { method: 'POST' })
}

const statusIcon = computed(() => STATUS_ICONS[props.agent.status] ?? '⚪')
const statusTextClass = computed(() => STATUS_TEXT[props.agent.status] ?? 'text-gray-400')
const statusClass = computed(() => ({
  [STATUS_BORDER[props.agent.status] ?? 'border-gray-600']: true,
  'border-2': props.isActive,
  'border': !props.isActive,
}))
</script>

<style scoped>
.agent-card {
  background: #1a1f2e;
  border-radius: 8px;
  padding: 10px 12px;
  cursor: pointer;
  transition: background 0.15s;
  min-width: 200px;
}
.agent-card:hover { background: #1e2438; }
.agent-card.active { background: #1e2a3a; }
.spinner {
  display: inline-block;
  width: 14px;
  height: 14px;
  border: 2px solid #4ade80;
  border-top-color: transparent;
  border-radius: 50%;
  animation: spin 0.7s linear infinite;
}
@keyframes spin { to { transform: rotate(360deg); } }
</style>
