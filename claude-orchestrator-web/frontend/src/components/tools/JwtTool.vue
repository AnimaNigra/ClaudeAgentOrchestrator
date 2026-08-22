<template>
  <ToolPane
    title="JWT"
    hint="Klíč i tajemství zůstávají v prohlížeči, nic se neodesílá. Veřejný klíč musí být ve formátu SPKI (-----BEGIN PUBLIC KEY-----)."
  >
    <label class="flex flex-col gap-1">
      <span class="text-xs text-gray-500">Token</span>
      <textarea
        v-model="token"
        rows="4"
        spellcheck="false"
        class="w-full px-2 py-1 font-mono text-xs bg-gray-950 border border-gray-700 rounded text-gray-200 focus:outline-none focus:border-blue-500 resize-y break-all"
      />
    </label>

    <p v-if="!decoded.ok" class="text-xs text-red-400">{{ decoded.error }}</p>

    <template v-if="decoded.ok && decoded.value">
      <div class="flex items-center gap-3 text-xs">
        <span class="text-gray-500">Algoritmus</span>
        <code class="text-gray-200">{{ decoded.value.algorithm }}</code>
        <span :class="verificationClass">{{ verificationLabel }}</span>
      </div>

      <p v-if="pemAsHmacSecret" class="text-xs text-amber-400">
        Klíč vypadá jako PEM, ale token deklaruje algoritmus HMAC — token se ověřuje
        proti textu PEM jako sdílenému tajemství, ne jako proti veřejnému klíči.
      </p>

      <label class="flex flex-col gap-1">
        <span class="text-xs text-gray-500">Tajemství (HS*) nebo veřejný klíč v PEM (RS*, ES*)</span>
        <textarea
          v-model="key"
          rows="3"
          spellcheck="false"
          class="w-full px-2 py-1 font-mono text-xs bg-gray-950 border border-gray-700 rounded text-gray-200 focus:outline-none focus:border-blue-500 resize-y"
        />
      </label>

      <div class="flex flex-col gap-1">
        <div class="flex items-center gap-2">
          <span class="text-xs text-gray-500">Hlavička</span>
          <CopyButton :value="decoded.value.header" />
        </div>
        <pre class="px-2 py-1 font-mono text-xs bg-gray-950 border border-gray-700 rounded text-gray-200 overflow-x-auto">{{ decoded.value.header }}</pre>
      </div>

      <div class="flex flex-col gap-1">
        <div class="flex items-center gap-2">
          <span class="text-xs text-gray-500">Payload</span>
          <CopyButton :value="decoded.value.payload" />
        </div>
        <pre class="px-2 py-1 font-mono text-xs bg-gray-950 border border-gray-700 rounded text-gray-200 overflow-x-auto">{{ decoded.value.payload }}</pre>
      </div>
    </template>
  </ToolPane>
</template>

<script setup>
import { computed, ref, watchEffect } from 'vue'
import ToolPane from './ToolPane.vue'
import CopyButton from './CopyButton.vue'
import { decodeJwt, verifyJwt, VERIFY } from '../../services/tools/jwt.js'

const token = ref('')
const key = ref('')
const verification = ref(VERIFY.NOT_ATTEMPTED)

const decoded = computed(() => decodeJwt(token.value))

// verifyJwt je async, takže sem nepatří computed — výsledek se doplňuje do refu.
// Poslední spuštění vyhrává; starší odpověď se zahodí přes stale guard.
// POZOR: watchEffect sleduje jen refy přečtené PŘED prvním await. Proto se
// token.value i key.value čtou v argumentech volání — kdyby se sáhlo na některý
// až po awaitu, přestal by na něj efekt reagovat.
let run = 0
watchEffect(async () => {
  const current = ++run
  const state = await verifyJwt(token.value, key.value)
  if (current === run) verification.value = state
})

// Klasický downgrade RS256 -> HS256: crypto.subtle HMAC nerozezná PEM od
// náhodného textu, takže se PEM prostě použije jako sdílené tajemství a
// ověření může vyjít "platí". Zde se nic nevynucuje, jen se to upozorní.
const pemAsHmacSecret = computed(() =>
  decoded.value.ok && decoded.value.value?.algorithm?.startsWith('HS') &&
  key.value.includes('-----BEGIN'))

const LABELS = {
  [VERIFY.NOT_ATTEMPTED]: '',
  [VERIFY.KEY_REQUIRED]: 'zadej klíč pro ověření',
  [VERIFY.VERIFIED]: '✓ podpis platí',
  [VERIFY.INVALID]: '✗ podpis neplatí',
  [VERIFY.UNSUPPORTED]: 'algoritmus nepodporován',
}

const CLASSES = {
  [VERIFY.NOT_ATTEMPTED]: '',
  [VERIFY.KEY_REQUIRED]: 'text-gray-500',
  [VERIFY.VERIFIED]: 'text-green-400',
  [VERIFY.INVALID]: 'text-red-400',
  [VERIFY.UNSUPPORTED]: 'text-amber-400',
}

const verificationLabel = computed(() => LABELS[verification.value])
const verificationClass = computed(() => CLASSES[verification.value])
</script>
