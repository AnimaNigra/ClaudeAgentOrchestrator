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
      {{ unresolvedNotice }}
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
      :srcdoc="framedHtml"
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

// Jednotné číslo je nejčastější případ ("1 vložený obrázek"), takže si
// zaslouží vlastní shodu — ne jen ohnutý plurál.
const unresolvedNotice = computed(() => {
  const n = props.email.unresolvedInlineImages
  return n === 1
    ? 'U 1 vloženého obrázku se nepodařilo dohledat odpovídající část zprávy, takže se nezobrazí.'
    : `U ${n} vložených obrázků se nepodařilo dohledat odpovídající část zprávy, takže se nezobrazí.`
})

// Po načtení jiné zprávy nemusí dosavadní tab existovat — spadni na první.
watch(tabs, list => {
  if (!list.includes(active.value)) active.value = list[0] ?? 'text'
})

// sandbox="" řeší spouštění (skripty, formuláře, navigaci), ale NEŘEŠÍ
// stahování podřízených zdrojů — obrázek z ciziny by se načetl a odesílateli
// prozradil IP adresu, prohlížeč i čas otevření. Tohle je klasický sledovací
// pixel a běžní poštovní klienti ho blokují. Aplikace žádné CSP nemá, takže
// se dosazuje přímo do dokumentu v srcdoc.
//   default-src 'none'  — nic zvenčí
//   img-src data:       — jen obrázky, které jsme sami dosadili z příloh
//   style-src/font-src  — inline styly a fonty z dat, aby zpráva nevypadala rozbitě
const framedHtml = computed(() => {
  if (!props.email.htmlBodySanitized) return ''
  const csp = "default-src 'none'; img-src data:; style-src 'unsafe-inline'; font-src data:"
  return `<meta http-equiv="Content-Security-Policy" content="${csp}">` +
    props.email.htmlBodySanitized
})
</script>
