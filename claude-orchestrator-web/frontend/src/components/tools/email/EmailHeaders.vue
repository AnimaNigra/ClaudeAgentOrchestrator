<template>
  <div class="flex flex-col gap-1 text-xs">
    <div class="flex gap-2">
      <span class="w-24 shrink-0 text-gray-500">Od</span>
      <span class="text-gray-200 break-all">{{ email.from }}</span>
    </div>
    <div v-if="email.to?.length" class="flex gap-2">
      <span class="w-24 shrink-0 text-gray-500">Komu</span>
      <span class="text-gray-200 break-all">{{ email.to.join(', ') }}</span>
    </div>
    <div v-if="email.cc?.length" class="flex gap-2">
      <span class="w-24 shrink-0 text-gray-500">Kopie</span>
      <span class="text-gray-200 break-all">{{ email.cc.join(', ') }}</span>
    </div>
    <div v-if="email.bcc?.length" class="flex gap-2">
      <span class="w-24 shrink-0 text-gray-500">Skrytá kopie</span>
      <span class="text-gray-200 break-all">{{ email.bcc.join(', ') }}</span>
    </div>
    <div class="flex gap-2">
      <span class="w-24 shrink-0 text-gray-500">Předmět</span>
      <span class="font-semibold text-gray-100 break-all">{{ email.subject }}</span>
    </div>
    <div v-if="email.date" class="flex gap-2">
      <span class="w-24 shrink-0 text-gray-500">Datum</span>
      <span class="text-gray-200">{{ formattedDate }}</span>
    </div>

    <details v-if="email.headers?.length" class="mt-1">
      <summary class="cursor-pointer text-gray-500 hover:text-gray-300 select-none">
        Všechny hlavičky ({{ email.headers.length }})
      </summary>
      <table class="mt-1 w-full border-collapse">
        <tbody>
          <tr v-for="(header, i) in email.headers" :key="i" class="align-top">
            <td class="py-0.5 pr-3 font-mono text-gray-500 whitespace-nowrap">{{ header.name }}</td>
            <td class="py-0.5 font-mono text-gray-300 break-all">{{ header.value }}</td>
          </tr>
        </tbody>
      </table>
    </details>
  </div>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  email: { type: Object, required: true },
})

const formattedDate = computed(() => {
  const value = props.email.date
  if (!value) return ''
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime())
    ? String(value)
    : parsed.toLocaleString('cs-CZ')
})
</script>
