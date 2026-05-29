<template>
  <div class="relative flex-1 flex flex-col min-h-0">
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

    <!-- Speak selection button: always visible while a file is open.
         Dimmed when there's no selection so the user knows where to click. -->
    <button
      v-if="tab"
      @click="toggleSpeak"
      :disabled="!hasSelection && !isSpeaking"
      :title="isSpeaking ? 'Stop reading' : (hasSelection ? 'Read selection aloud' : 'Select text in the document first, then click here')"
      :aria-label="isSpeaking ? 'Stop reading' : 'Read selection aloud'"
      class="absolute bottom-4 right-4 z-10 w-12 h-12 rounded-full border transition-colors flex items-center justify-center shadow-lg"
      :class="isSpeaking
        ? 'bg-blue-600 border-blue-400 hover:bg-blue-700'
        : (hasSelection
          ? 'bg-gray-800 border-gray-600 hover:bg-gray-700 hover:border-blue-500 cursor-pointer'
          : 'bg-gray-900 border-gray-700 opacity-40 cursor-not-allowed')"
    >
      <svg v-if="!isSpeaking" xmlns="http://www.w3.org/2000/svg" class="w-5 h-5 text-gray-300" viewBox="0 0 24 24" fill="currentColor">
        <path d="M3 9v6h4l5 5V4L7 9H3zm13.5 3A4.5 4.5 0 0 0 14 7.97v8.05c1.48-.73 2.5-2.25 2.5-4.02zM14 3.23v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z"/>
      </svg>
      <svg v-else xmlns="http://www.w3.org/2000/svg" class="w-5 h-5 text-white animate-pulse" viewBox="0 0 24 24" fill="currentColor">
        <path d="M6 6h12v12H6z"/>
      </svg>
    </button>
  </div>
</template>

<script setup>
import { ref, computed, watch, nextTick, onMounted, onUnmounted } from 'vue'
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

// Speak-selection state — mirrors TerminalPanel pattern.
const hasSelection = ref(false)
const isSpeaking = ref(false)

function getSelectedTextInArticle() {
  if (!article.value) return ''
  const sel = window.getSelection()
  if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return ''
  // Only honor selection that is inside the article element.
  const range = sel.getRangeAt(0)
  if (!article.value.contains(range.commonAncestorContainer)) return ''
  return sel.toString().trim()
}

function refreshSelectionState() {
  hasSelection.value = !!getSelectedTextInArticle()
}

function toggleSpeak() {
  if (isSpeaking.value) {
    window.speechSynthesis.cancel()
    isSpeaking.value = false
    return
  }
  const text = getSelectedTextInArticle()
  if (!text) return

  const utter = new SpeechSynthesisUtterance(text)
  utter.lang = /[áčďéěíňóřšťúůýžÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ]/.test(text) ? 'cs-CZ' : 'en-US'
  utter.onend = () => { isSpeaking.value = false }
  utter.onerror = () => { isSpeaking.value = false }
  if (window.speechSynthesis.speaking || window.speechSynthesis.pending) {
    window.speechSynthesis.cancel()
  }
  isSpeaking.value = true
  window.speechSynthesis.speak(utter)
}

onMounted(() => {
  if (scroller.value && props.tab) scroller.value.scrollTop = props.tab.scrollY || 0
  document.addEventListener('selectionchange', refreshSelectionState)
})

onUnmounted(() => {
  document.removeEventListener('selectionchange', refreshSelectionState)
  if (isSpeaking.value) window.speechSynthesis.cancel()
})
</script>

