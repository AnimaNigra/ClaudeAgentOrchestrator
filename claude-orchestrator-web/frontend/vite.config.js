import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  plugins: [
    vue(),
    VitePWA({
      registerType: 'autoUpdate',
      devOptions: { enabled: true },
      workbox: {
        // Don't cache API/SignalR — only static assets
        navigateFallback: 'index.html',
        runtimeCaching: [
          {
            urlPattern: /^https?:\/\/localhost.*\/(api|hubs)\//,
            handler: 'NetworkOnly',
          },
        ],
      },
      manifest: {
        name: 'Claude Agent Orchestrator',
        short_name: 'Orchestrator',
        description: 'Manage multiple Claude Code agents',
        theme_color: '#0a0f1a',
        background_color: '#0a0f1a',
        display: 'standalone',
        start_url: '/',
        icons: [
          {
            src: '/icon-192.png',
            sizes: '192x192',
            type: 'image/png',
            purpose: 'any',
          },
          {
            src: '/icon-512.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'any maskable',
          },
        ],
      },
    }),
  ],
  server: {
    port: 5180,
    strictPort: true,
    proxy: {
      '/api': 'http://localhost:6001',
      '/hubs': {
        target: 'http://localhost:6001',
        ws: true,
      },
    },
  },
  build: {
    outDir: '../backend/wwwroot',
    emptyOutDir: true,
  },
})
