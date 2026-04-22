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
