<template>
  <ToolPane title="UUID" hint="Generuje UUID verze 4. Jako jediný nástroj nepočítá průběžně — generuje na kliknutí.">
    <div class="flex items-end gap-2">
      <label class="flex flex-col gap-1">
        <span class="text-xs text-gray-500">Počet (1–100)</span>
        <input
          v-model.number="count"
          type="number"
          min="1"
          max="100"
          class="w-24 px-2 py-1 font-mono text-xs bg-gray-950 border border-gray-700 rounded text-gray-200 focus:outline-none focus:border-blue-500"
        />
      </label>
      <button
        class="px-3 py-1 text-xs rounded bg-blue-600 text-white hover:bg-blue-500 transition-colors"
        @click="generate"
      >Generovat</button>
      <CopyButton :value="uuids.join('\n')" label="Kopírovat vše" />
    </div>

    <ul v-if="uuids.length" class="flex flex-col gap-1">
      <li v-for="uuid in uuids" :key="uuid" class="flex items-center gap-2">
        <code class="flex-1 px-2 py-1 font-mono text-xs bg-gray-950 border border-gray-700 rounded text-gray-200">{{ uuid }}</code>
        <CopyButton :value="uuid" />
      </li>
    </ul>
  </ToolPane>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import ToolPane from './ToolPane.vue'
import CopyButton from './CopyButton.vue'
import { generateUuids } from '../../services/tools/uuid.js'

const count = ref(1)
const uuids = ref([])

function generate() {
  uuids.value = generateUuids(count.value).value
}

onMounted(generate)
</script>
