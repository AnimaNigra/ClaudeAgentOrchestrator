<template>
  <div
    ref="scroller"
    class="reader-preview flex-1 overflow-y-auto px-8 py-6"
    @scroll="onScroll"
  >
    <div v-if="!tab" class="h-full flex items-center justify-center text-gray-500 text-sm">
      <div class="text-center">
        <div class="text-4xl mb-2">📄</div>
        <p>No file open</p>
        <p class="text-xs mt-1">Click <b>Open file</b> or drop a Markdown file here.</p>
      </div>
    </div>
    <article
      v-else
      ref="article"
      class="reader-md"
      v-html="html"
    />
  </div>
</template>

<script setup>
import { ref, computed, watch, nextTick, onMounted } from 'vue'
import { createRenderer } from '../../services/markdownRenderer.js'
import { renderAll as renderAllMermaid } from '../../services/mermaidRenderer.js'
import 'highlight.js/styles/github-dark.css'

const props = defineProps({
  tab: { type: Object, default: null },
})
const emit = defineEmits(['headings', 'scroll', 'active-heading'])

const scroller = ref(null)
const article = ref(null)

const rendered = computed(() => {
  if (!props.tab) return { html: '', headings: [] }
  const basePath = props.tab.mode === 'full' && props.tab.path
    ? props.tab.path.replace(/[\\/][^\\/]+$/, '')
    : null
  const r = createRenderer({ mode: props.tab.mode, basePath })
  return r.render(props.tab.content || '')
})
const html = computed(() => rendered.value.html)

watch(rendered, async (v) => {
  emit('headings', v.headings)
  await nextTick()
  if (article.value) await renderAllMermaid(article.value)
  // Restore scroll position
  if (scroller.value && props.tab) scroller.value.scrollTop = props.tab.scrollY || 0
  updateActiveHeading()
}, { immediate: true })

watch(() => props.tab?.id, async () => {
  await nextTick()
  if (scroller.value && props.tab) scroller.value.scrollTop = props.tab.scrollY || 0
})

function onScroll() {
  if (!scroller.value) return
  emit('scroll', scroller.value.scrollTop)
  updateActiveHeading()
}

function updateActiveHeading() {
  if (!article.value || !scroller.value) return
  const headings = rendered.value.headings
  if (!headings.length) { emit('active-heading', null); return }
  const offset = scroller.value.getBoundingClientRect().top + 16
  let activeId = headings[0].id
  for (const h of headings) {
    const el = article.value.querySelector(`#${cssEscape(h.id)}`)
    if (!el) continue
    const top = el.getBoundingClientRect().top
    if (top <= offset) activeId = h.id
    else break
  }
  emit('active-heading', activeId)
}

function cssEscape(s) {
  return window.CSS?.escape ? window.CSS.escape(s) : s.replace(/([^\w-])/g, '\\$1')
}

defineExpose({
  scrollToHeading(id) {
    if (!article.value) return
    const el = article.value.querySelector(`#${cssEscape(id)}`)
    if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }
})

onMounted(() => {
  if (scroller.value && props.tab) scroller.value.scrollTop = props.tab.scrollY || 0
})
</script>

