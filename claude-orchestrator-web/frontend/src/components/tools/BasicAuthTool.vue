<template>
  <ToolPane title="Basic Auth" hint="Token se počítá průběžně, nic se neodesílá na server.">
    <label class="flex flex-col gap-1">
      <span class="text-xs text-gray-500">Uživatel</span>
      <input
        v-model="username"
        type="text"
        spellcheck="false"
        class="w-full max-w-md px-2 py-1 font-mono text-xs bg-gray-950 border border-gray-700 rounded text-gray-200 focus:outline-none focus:border-blue-500"
      />
    </label>

    <label class="flex flex-col gap-1">
      <span class="text-xs text-gray-500">Heslo</span>
      <div class="flex items-center gap-2 max-w-md">
        <input
          v-model="password"
          :type="revealed ? 'text' : 'password'"
          spellcheck="false"
          autocomplete="off"
          class="flex-1 px-2 py-1 font-mono text-xs bg-gray-950 border border-gray-700 rounded text-gray-200 focus:outline-none focus:border-blue-500"
        />
        <button
          class="px-2 py-0.5 text-xs rounded border border-gray-600 text-gray-400 hover:text-white hover:border-gray-400 transition-colors"
          @click="revealed = !revealed"
        >{{ revealed ? 'Skrýt' : 'Zobrazit' }}</button>
      </div>
    </label>

    <div class="flex flex-col gap-1">
      <div class="flex items-center gap-2">
        <span class="text-xs text-gray-500">Token</span>
        <CopyButton :value="result.value.token" />
      </div>
      <code class="px-2 py-1 font-mono text-xs bg-gray-950 border border-gray-700 rounded text-gray-200 break-all">{{ result.value.token }}</code>
    </div>

    <div class="flex flex-col gap-1">
      <div class="flex items-center gap-2">
        <span class="text-xs text-gray-500">Hlavička</span>
        <CopyButton :value="result.value.header" />
      </div>
      <code class="px-2 py-1 font-mono text-xs bg-gray-950 border border-gray-700 rounded text-gray-200 break-all">{{ result.value.header }}</code>
    </div>
  </ToolPane>
</template>

<script setup>
import { computed, ref } from 'vue'
import ToolPane from './ToolPane.vue'
import CopyButton from './CopyButton.vue'
import { buildBasicAuth } from '../../services/tools/codecs.js'

const username = ref('')
const password = ref('')
const revealed = ref(false)

const result = computed(() => buildBasicAuth(username.value, password.value))
</script>
