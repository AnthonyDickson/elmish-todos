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
      },
      proxy: {
        '/api': {
          target: 'http://localhost:5000',
          changeOrigin: true,
          configure: (proxy, _options) => {
            proxy.on('error', (err, _req, _res) => {
              console.log('proxy error', err);
            });
          },
        },
        '/login': {
          target: 'http://localhost:5000',
          changeOrigin: true,
        },
        '/logout': {
          target: 'http://localhost:5000',
          changeOrigin: true,
        },
        '/signin-oidc': {
          target: 'http://localhost:5000',
          changeOrigin: true,
        },
        '/static': {
          target: 'http://localhost:5000',
          changeOrigin: true,
        },
      }
  },
  plugins: [
    tailwindcss(),
  ],
})
