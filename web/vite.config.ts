import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

export default defineConfig({
  plugins: [react()],
  base: './',
  build: {
    outDir: path.resolve(__dirname, '../src/EJInstaller.Orchestrator/wwwroot'),
    emptyOutDir: true
  },
  server: {
    proxy: {
      '/api': 'http://localhost:5124'
    }
  }
})
