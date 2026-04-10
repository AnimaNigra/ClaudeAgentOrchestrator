<template>
  <div
    class="agent-card"
    :class="[statusClass, { active: isActive }]"
    @click="$emit('select', agent.id)"
  >
    <!-- Row 1: Name + model badge + status -->
    <div class="flex items-center justify-between mb-1">
      <div class="flex items-center gap-1.5 min-w-0">
        <span class="font-bold text-sm truncate">{{ agent.name }}</span>
        <span v-if="agent.modelName" class="text-[10px] text-purple-300 bg-purple-400/10 px-1.5 py-0.5 rounded flex-shrink-0">{{ agent.modelName }}</span>
        <span v-if="agent.worktreeBranch" class="text-[10px] text-emerald-400 bg-emerald-400/10 px-1.5 py-0.5 rounded flex-shrink-0" title="Running in worktree">🌿 {{ agent.worktreeBranch }}</span>
      </div>
      <span v-if="agent.status === 'Running'" class="spinner" title="Running"></span>
      <span v-else class="status-dot" :title="agent.status">{{ statusIcon }}</span>
    </div>

    <!-- Row 2: CWD -->
    <div class="text-xs text-gray-400 truncate mb-1 flex items-center gap-1" :title="agent.cwd ?? 'default'">
      <button
        @click.stop="openFolder"
        class="hover:text-blue-400 transition-colors flex-shrink-0"
        title="Open folder in explorer"
      >📁</button>
      <span class="truncate">{{ agent.cwd ?? 'default' }}</span>
    </div>

    <!-- Row 3: Last message -->
    <div class="text-xs truncate text-gray-300 min-h-[1rem]">
      {{ agent.lastMessage || '…' }}
    </div>

    <!-- Row 4: Context bar + rate limit -->
    <div v-if="agent.contextPct != null" class="mt-2 space-y-1">
      <div class="flex items-center gap-2">
        <span class="text-[11px] text-gray-400 w-7 flex-shrink-0">ctx</span>
        <div class="flex-1 bg-gray-700/60 rounded-full h-[6px]">
          <div class="h-[6px] rounded-full transition-all" :class="contextBarColor" :style="{ width: Math.min(agent.contextPct, 100) + '%' }"></div>
        </div>
        <span class="text-[11px] font-medium w-8 text-right" :class="contextColor">{{ agent.contextPct }}%</span>
      </div>
      <div v-if="agent.rateLimitPct != null" class="flex items-center gap-2">
        <span class="text-[11px] text-gray-400 w-7 flex-shrink-0">rate</span>
        <div class="flex-1 bg-gray-700/60 rounded-full h-[6px]">
          <div class="h-[6px] rounded-full transition-all" :class="rateBarColor" :style="{ width: Math.min(agent.rateLimitPct, 100) + '%' }"></div>
        </div>
        <span class="text-[11px] font-medium w-8 text-right" :class="rateColor">{{ agent.rateLimitPct }}%</span>
        <span v-if="agent.rateLimitResetAt" class="text-[10px] text-gray-500 flex-shrink-0">{{ agent.rateLimitResetAt }}</span>
      </div>
    </div>

    <!-- Row 5: Cost + actions -->
    <div class="flex items-center justify-between mt-2">
      <div class="flex items-center gap-2">
        <span v-if="agent.estimatedCost" class="text-[11px] text-gray-400 bg-gray-700/40 px-1.5 py-0.5 rounded">${{ agent.estimatedCost.toFixed(2) }}</span>
      </div>
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

    <!-- Progress bar (task progress, if applicable) -->
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

const contextColor = computed(() => {
  const pct = props.agent.contextPct ?? 0
  if (pct > 80) return 'text-red-400'
  if (pct > 50) return 'text-amber-400'
  return 'text-emerald-400'
})

const contextBarColor = computed(() => {
  const pct = props.agent.contextPct ?? 0
  if (pct > 80) return 'bg-red-500'
  if (pct > 50) return 'bg-amber-500'
  return 'bg-emerald-500'
})

const rateColor = computed(() => {
  const pct = props.agent.rateLimitPct ?? 0
  if (pct > 80) return 'text-red-400'
  if (pct > 50) return 'text-amber-400'
  return 'text-blue-400'
})

const rateBarColor = computed(() => {
  const pct = props.agent.rateLimitPct ?? 0
  if (pct > 80) return 'bg-red-500'
  if (pct > 50) return 'bg-amber-500'
  return 'bg-blue-500'
})
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
