<template>
  <CodecTool
    title="URL"
    hint="Escapuje i !'()* kvůli shodě s Uri.EscapeDataString."
    :encode-fn="encodeUrl"
    :decode-fn="decodeUrl"
  />
</template>

<script setup>
import CodecTool from './CodecTool.vue'
import { encodeUrl, decodeUrl } from '../../services/tools/codecs.js'
</script>
