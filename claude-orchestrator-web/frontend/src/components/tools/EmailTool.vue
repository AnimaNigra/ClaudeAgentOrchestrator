<template>
  <ToolPane
    title="E-mail"
    hint="Otevře .eml nebo .msg — zadej cestu, přetáhni soubor sem, nebo ho vyber."
    @dragover.prevent
    @drop.prevent="onDrop"
  >
    <form class="flex gap-2" @submit.prevent="loadFromPath">
      <input
        v-model="path"
        type="text"
        spellcheck="false"
        placeholder="C:\zpravy\zprava.eml"
        class="flex-1 px-2 py-1 font-mono text-xs bg-gray-950 border border-gray-700 rounded text-gray-200 focus:outline-none focus:border-blue-500"
      />
      <button
        type="submit"
        class="px-3 py-1 text-xs rounded bg-blue-600 text-white hover:bg-blue-500 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
        :disabled="loading"
      >Otevřít</button>
      <input
        type="file"
        accept=".eml,.msg"
        class="text-xs text-gray-400 file:mr-2 file:px-2 file:py-1 file:text-xs file:rounded file:border file:border-gray-600 file:bg-gray-800 file:text-gray-300 hover:file:text-white"
        @change="onPick"
      />
    </form>

    <p v-if="loading" class="text-xs text-gray-500">Načítám…</p>
    <p v-if="error" class="text-xs text-red-400">{{ error }}</p>

    <template v-if="email">
      <EmailHeaders :email="email" />
      <EmailAttachments :attachments="email.attachments" @download="download" />
      <EmailBody :email="email" />
    </template>
  </ToolPane>
</template>

<script setup>
import { ref } from 'vue'
import ToolPane from './ToolPane.vue'
import EmailHeaders from './email/EmailHeaders.vue'
import EmailBody from './email/EmailBody.vue'
import EmailAttachments from './email/EmailAttachments.vue'
import { parseEmail, fetchAttachment, saveBlob } from '../../services/tools/emailApi.js'

const path = ref('')
const email = ref(null)
const error = ref('')
const loading = ref(false)

// Zdroj, ze kterého přišla zobrazená zpráva: { path } nebo { file }. Drží se
// proto, že stahování přílohy potřebuje zdrojová data znovu — server si mezi
// požadavky nic neukládá (spec §4.1).
let source = null

// Počítadlo běhů. Tlačítko "Otevřít" se sice při načítání zamyká, ale přetažení
// souboru ani výběr přes dialog ne, takže se dvě načtení můžou překrýt. Bez
// tohohle by starší odpověď, která doběhne později, přepsala tu novější.
let run = 0

async function load(next) {
  const current = ++run
  loading.value = true
  error.value = ''
  try {
    const parsed = await parseEmail(next)
    if (current !== run) return
    email.value = parsed
    source = next
  } catch (e) {
    if (current !== run) return
    error.value = e.message
    email.value = null
    source = null
  } finally {
    // Jen aktuální běh smí zhasnout indikátor — jinak by zmizel, i když
    // novější požadavek pořád běží.
    if (current === run) loading.value = false
  }
}

function loadFromPath() {
  if (!path.value.trim()) return
  return load({ path: path.value })
}

function onDrop(event) {
  const file = event.dataTransfer?.files?.[0]
  if (file) load({ file })
}

function onPick(event) {
  const file = event.target.files?.[0]
  if (file) load({ file })
}

async function download(index) {
  if (!source) return
  error.value = ''
  // Příloha i zdroj se čtou PŘED awaitem. Kdyby se mezitím načetla jiná zpráva,
  // email.value už ukazuje na ni — příloha by se uložila pod cizím jménem, nebo
  // by find() vrátil undefined a .fileName by spadlo syrovou chybou.
  const attachment = email.value?.attachments.find(a => a.index === index)
  if (!attachment) return
  const downloadSource = source
  try {
    const blob = await fetchAttachment(downloadSource, index)
    saveBlob(blob, attachment.fileName)
  } catch (e) {
    // Zprávu držíme dál — selhalo stahování přílohy, ne načtení zprávy.
    error.value = e.message
  }
}
</script>
