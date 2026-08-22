<template>
  <div class="flex flex-1 overflow-hidden">
    <ToolsSidebar :tools="TOOLS" :active-slug="activeTool.slug" />
    <component :is="activeTool.component" :key="activeTool.slug" />
  </div>
</template>

<script setup>
import { computed, markRaw } from 'vue'
import { useRoute } from 'vue-router'
import ToolsSidebar from '../components/tools/ToolsSidebar.vue'
import Base64Tool from '../components/tools/Base64Tool.vue'

// Pořadí určuje pořadí v sidebaru. První položka je zároveň fallback
// pro neznámý nebo chybějící slug v route.
const TOOLS = [
  { slug: 'base64', label: 'Base64', component: markRaw(Base64Tool) },
]

const route = useRoute()
const activeTool = computed(() =>
  TOOLS.find(tool => tool.slug === route.params.tool) ?? TOOLS[0])
</script>
