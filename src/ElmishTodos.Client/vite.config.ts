import { defineConfig } from 'vite'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  clearScreen: false,
  server: {
      watch: {
          ignored: [
              "**/*.fs" // Don't watch F# files
          ]
      }
  },
  plugins: [
    tailwindcss(),
  ],
})
