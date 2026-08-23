<template>
  <div class="flex flex-col gap-2">
    <div v-if="tabs.length > 1" class="flex gap-1">
      <button
        v-for="tab in tabs"
        :key="tab"
        class="px-3 py-1 text-xs rounded transition-colors"
        :class="active === tab
          ? 'bg-blue-600 text-white'
          : 'bg-gray-800 text-gray-400 hover:text-white hover:bg-gray-700'"
        @click="active = tab"
      >{{ tab }}</button>
    </div>
    <div v-else-if="tabs.length === 1" class="flex gap-1">
      <button class="px-3 py-1 text-xs rounded bg-blue-600 text-white">{{ tabs[0] }}</button>
    </div>

    <p v-if="email.unresolvedInlineImages > 0" class="text-xs text-amber-400">
      U {{ email.unresolvedInlineImages }} vložených obrázků se nepodařilo dohledat
      odpovídající část zprávy, takže se nezobrazí.
    </p>

    <!--
      Prázdný sandbox je nejpřísnější varianta (spec §5.2): blokuje skripty,
      formuláře i navigaci nadřazeného okna a dává rámu unikátní origin.
      Do srcdoc jde VÝHRADNĚ sanitizované HTML — syrové se ukazuje jako text
      v tabu "source". Výška je pevná, protože se sandboxem nejde obsah změřit.
    -->
    <iframe
      v-if="active === 'html'"
      sandbox=""
      :srcdoc="email.htmlBodySanitized"
      class="w-full h-[60vh] bg-white border border-gray-700 rounded"
    />
    <pre
      v-else
      class="px-2 py-1 max-h-[60vh] overflow-auto font-mono text-xs bg-gray-950 border border-gray-700 rounded text-gray-200 whitespace-pre-wrap break-all"
    >{{ active === 'text' ? email.textBody : email.htmlBodyRaw }}</pre>
  </div>
</template>

<script setup>
import { computed, ref, watch } from 'vue'

const props = defineProps({
  email: { type: Object, required: true },
})

// Nabízí se jen to, co zpráva doopravdy má. Prázdný tab je horší než žádný.
const tabs = computed(() => {
  const available = []
  if (props.email.htmlBodySanitized) available.push('html')
  if (props.email.textBody) available.push('text')
  if (props.email.htmlBodyRaw) available.push('source')
  return available
})

const active = ref(tabs.value[0] ?? 'text')

// Po načtení jiné zprávy nemusí dosavadní tab existovat — spadni na první.
watch(tabs, list => {
  if (!list.includes(active.value)) active.value = list[0] ?? 'text'
})
</script>
