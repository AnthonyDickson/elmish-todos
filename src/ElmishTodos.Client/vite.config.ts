import { defineConfig } from 'vite'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  clearScreen: false,
  server: {
      watch: {
          ignored: [
              "**/*.fsproj", // Don't watch project files
              "dist/",
          ]
      }
  },
  plugins: [
    tailwindcss(),
  ],
})
