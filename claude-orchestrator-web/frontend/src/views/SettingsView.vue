<template>
  <div class="bg-gray-950 text-gray-200">
    <div class="flex-1 overflow-y-auto p-6">
      <div class="max-w-xl space-y-6">
        <h2 class="text-sm font-bold text-blue-400">Settings</h2>

        <section class="bg-gray-900 border border-gray-800 rounded-lg p-4 space-y-4">
          <h3 class="text-xs font-semibold uppercase tracking-wide text-gray-400">Notification sound</h3>
          <p class="text-xs text-gray-500">
            Played when an agent goes idle, asks for permission, or sends a notification.
          </p>

          <label class="block space-y-1">
            <div class="flex items-center justify-between text-xs">
              <span :class="settings.mutedAll ? 'text-gray-600' : 'text-gray-300'">Volume</span>
              <span class="text-gray-400 tabular-nums">{{ settings.volumePct }} %</span>
            </div>
            <input
              type="range"
              min="0"
              max="100"
              step="5"
              class="w-full accent-blue-500 disabled:opacity-40"
              :value="settings.volumePct"
              :disabled="settings.mutedAll"
              @input="settings.setVolume($event.target.value)"
            />
          </label>

          <div class="flex items-center justify-between gap-4">
            <label class="flex items-center gap-2 text-xs text-gray-300 cursor-pointer">
              <input
                type="checkbox"
                class="accent-blue-500"
                :checked="settings.mutedAll"
                @change="settings.toggleMuteAll()"
              />
              Mute all sounds
            </label>
            <button
              class="text-xs px-2 py-1 rounded bg-gray-800 text-gray-300 hover:bg-gray-700 hover:text-white transition-colors"
              @click="agents.playDing(null)"
            >🔊 Play test sound</button>
          </div>
        </section>

        <section class="bg-gray-900 border border-gray-800 rounded-lg p-4 space-y-3">
          <h3 class="text-xs font-semibold uppercase tracking-wide text-gray-400">Muted agents</h3>
          <p v-if="!mutedAgents.length" class="text-xs text-gray-500">
            No agent is muted. Use the 🔔 button on an agent card to silence just that one.
          </p>
          <template v-else>
            <ul class="space-y-1">
              <li
                v-for="a in mutedAgents"
                :key="a.id"
                class="flex items-center justify-between text-xs bg-gray-800/50 rounded px-2 py-1"
              >
                <span class="truncate">
                  🔕 {{ a.name }}
                  <span v-if="!a.running" class="text-gray-500">(not running)</span>
                </span>
                <button
                  class="text-gray-400 hover:text-white px-1.5 py-0.5 rounded hover:bg-gray-700 transition-colors"
                  @click="settings.toggleAgentMute(a.id)"
                >unmute</button>
              </li>
            </ul>
            <button
              class="text-xs px-2 py-1 rounded bg-gray-800 text-gray-300 hover:bg-gray-700 hover:text-white transition-colors"
              @click="settings.unmuteAllAgents()"
            >Unmute all</button>
          </template>
        </section>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { useSettingsStore } from '../stores/settings'
import { useAgentsStore } from '../stores/agents'

const settings = useSettingsStore()
const agents = useAgentsStore()

// Muted ids can outlive the agent itself (backend restart while the browser
// kept its localStorage), so fall back to the raw id for the label.
const mutedAgents = computed(() =>
  settings.mutedAgentIds.map(id => {
    const agent = agents.agents[id]
    return { id, name: agent?.name ?? id, running: !!agent }
  })
)
</script>
