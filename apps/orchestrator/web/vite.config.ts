import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import path from 'path'

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src')
    }
  },
  base: './',
  server: {
    proxy: {
      '^/api': {
        target: 'http://localhost:5124',
        changeOrigin: true,
      },
      '^/hubs': {
        target: 'http://localhost:5124',
        changeOrigin: true,
        ws: true,
      },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
    exclude: ['.worktrees/**', 'node_modules/**'],
  },
  build: {
    outDir: path.resolve(__dirname, '../backend/wwwroot'),
    emptyOutDir: true
  }
})
