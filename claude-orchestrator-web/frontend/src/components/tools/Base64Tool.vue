<template>
  <CodecTool
    title="Base64"
    hint="Kóduje přes UTF-8, takže diakritika i emoji projdou."
    :encode-fn="encodeBase64"
    :decode-fn="decodeBase64"
  />
</template>

<script setup>
import CodecTool from './CodecTool.vue'
import { encodeBase64, decodeBase64 } from '../../services/tools/codecs.js'
</script>
