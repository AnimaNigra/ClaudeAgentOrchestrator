<template>
  <Teleport to="body">
    <div
      v-if="store.pendingPermissions.length"
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/70"
    >
      <div class="bg-gray-900 border border-yellow-600 rounded-lg shadow-2xl w-full max-w-lg mx-4 p-5">
        <div class="flex items-center gap-2 mb-3">
          <span class="text-yellow-400 text-lg">⚠</span>
          <h2 class="text-white font-semibold">Permission Request</h2>
          <span v-if="store.pendingPermissions.length > 1" class="text-xs text-yellow-500 font-medium">
            1 of {{ store.pendingPermissions.length }}
          </span>
          <span class="ml-auto text-xs text-gray-500">{{ agentName }}</span>
        </div>

        <div class="mb-4">
          <div class="text-sm text-gray-300 mb-1">
            Agent wants to use <code class="text-yellow-300 bg-gray-800 px-1 rounded">{{ current.toolName }}</code>
          </div>
          <pre
            v-if="inputJson"
            class="text-xs text-gray-400 bg-gray-950 rounded p-3 overflow-auto max-h-48 whitespace-pre-wrap break-all"
          >{{ inputJson }}</pre>
        </div>

        <div class="flex flex-col gap-2">
          <div class="flex gap-3 justify-end">
            <button
              class="px-4 py-1.5 rounded text-sm bg-red-700 hover:bg-red-600 text-white"
              @click="respond(false)"
            >Deny</button>
            <button
              class="px-4 py-1.5 rounded text-sm bg-green-700 hover:bg-green-600 text-white"
              @click="respond(true)"
            >Approve</button>
          </div>
          <div class="flex gap-3 justify-end border-t border-gray-700 pt-2">
            <button
              class="px-4 py-1.5 rounded text-sm bg-blue-800 hover:bg-blue-700 text-white"
              @click="alwaysAllow"
              :title="`Always auto-approve ${current.toolName}`"
            >Always Allow {{ current.toolName }}</button>
            <button
              v-if="store.pendingPermissions.length > 1"
              class="px-4 py-1.5 rounded text-sm bg-green-900 hover:bg-green-800 text-white"
              @click="approveAll"
            >Approve All ({{ store.pendingPermissions.length }})</button>
          </div>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<script setup>
import { computed } from 'vue'
import { useAgentsStore } from '../stores/agents'

const store = useAgentsStore()

const current = computed(() => store.pendingPermissions[0] ?? {})

const agentName = computed(() => {
  const a = store.agents[current.value?.agentId]
  return a?.name ?? current.value?.agentId ?? ''
})

const inputJson = computed(() => {
  const ti = current.value?.toolInput
  if (!ti) return null
  try { return JSON.stringify(ti, null, 2) } catch { return String(ti) }
})

async function respond(approved) {
  const req = store.pendingPermissions[0]
  if (!req) return
  await fetch(`/api/agents/${req.agentId}/permission/${req.requestId}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ approved }),
  })
  store.pendingPermissions.shift()
}

async function approveAll() {
  const all = [...store.pendingPermissions]
  store.pendingPermissions.splice(0)
  await Promise.all(all.map(req =>
    fetch(`/api/agents/${req.agentId}/permission/${req.requestId}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ approved: true }),
    })
  ))
}

async function alwaysAllow() {
  store.addAlwaysAllowed(current.value.toolName)
  await respond(true)
}
</script>
