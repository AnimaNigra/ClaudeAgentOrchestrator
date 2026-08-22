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
import UrlTool from '../components/tools/UrlTool.vue'
import HtmlTool from '../components/tools/HtmlTool.vue'
import BasicAuthTool from '../components/tools/BasicAuthTool.vue'
import JwtTool from '../components/tools/JwtTool.vue'
import UuidTool from '../components/tools/UuidTool.vue'
import HashTool from '../components/tools/HashTool.vue'

// Pořadí určuje pořadí v sidebaru. První položka je zároveň fallback
// pro neznámý nebo chybějící slug v route.
const TOOLS = [
  { slug: 'base64', label: 'Base64', component: markRaw(Base64Tool) },
  { slug: 'url', label: 'URL', component: markRaw(UrlTool) },
  { slug: 'html', label: 'HTML', component: markRaw(HtmlTool) },
  { slug: 'basic-auth', label: 'Basic Auth', component: markRaw(BasicAuthTool) },
  { slug: 'jwt', label: 'JWT', component: markRaw(JwtTool) },
  { slug: 'uuid', label: 'UUID', component: markRaw(UuidTool) },
  { slug: 'hash', label: 'Hash', component: markRaw(HashTool) },
]

const route = useRoute()
const activeTool = computed(() =>
  TOOLS.find(tool => tool.slug === route.params.tool) ?? TOOLS[0])
</script>
