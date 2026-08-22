<template>
  <ToolPane title="Hash" hint="MD5 chybí záměrně — WebCrypto ho neumí.">
    <div class="flex gap-1">
      <button
        v-for="algorithm in HASH_ALGORITHMS"
        :key="algorithm"
        class="px-3 py-1 text-xs rounded transition-colors"
        :class="selected === algorithm
          ? 'bg-blue-600 text-white'
          : 'bg-gray-800 text-gray-400 hover:text-white hover:bg-gray-700'"
        @click="selected = algorithm"
      >{{ algorithm }}</button>
    </div>

    <p v-if="selected === 'SHA-1'" class="text-xs text-amber-400">
      SHA-1 je prolomená — nepoužívej ji pro bezpečnostní účely.
    </p>

    <textarea
      v-model="input"
      rows="6"
      spellcheck="false"
      placeholder="Vstup"
      class="w-full px-2 py-1 font-mono text-xs bg-gray-950 border border-gray-700 rounded text-gray-200 focus:outline-none focus:border-blue-500 resize-y"
    />

    <div class="flex items-center gap-2">
      <span class="text-xs text-gray-500">Hash</span>
      <CopyButton :value="result.ok ? result.value : ''" />
    </div>

    <code
      class="px-2 py-1 font-mono text-xs bg-gray-950 border rounded break-all"
      :class="result.ok ? 'border-gray-700 text-gray-200' : 'border-red-700 text-red-400'"
    >{{ result.ok ? result.value : result.error }}</code>
  </ToolPane>
</template>

<script setup>
import { ref, watchEffect } from 'vue'
import ToolPane from './ToolPane.vue'
import CopyButton from './CopyButton.vue'
import { hashText, HASH_ALGORITHMS } from '../../services/tools/hash.js'

const input = ref('')
const selected = ref('SHA-256')
const result = ref({ ok: true, value: '' })

// hashText je async — stejný stale guard jako u ověření JWT. Stejně jako tam
// platí, že oba refy musí být přečtené před awaitem, jinak je watchEffect
// přestane sledovat.
let run = 0
watchEffect(async () => {
  const current = ++run
  const next = await hashText(input.value, selected.value)
  if (current === run) result.value = next
})
</script>
