<template>
  <CodecTool
    title="HTML"
    :hint="HINT"
    :encode-fn="encodeHtml"
    :decode-fn="decodeHtml"
  />
</template>

<script setup>
import CodecTool from './CodecTool.vue'
import { encodeHtml, decodeHtml } from '../../services/tools/codecs.js'

// Hint jde přes vazbu, ne přes literál v atributu — syrové < a > uvnitř
// hodnoty atributu Vue kompilátor plete.
const HINT = `Escapuje & < > " ' — diakritiku nechává být.`
</script>
