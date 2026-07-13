import { defineConfig } from 'vite'
import tailwindcss from '@tailwindcss/vite'

const backendPaths = ['/api', '/login', '/logout', '/signin-oidc']

const proxy = Object.fromEntries(
  backendPaths.map(path => [path, {
    target: 'http://localhost:5000',
    changeOrigin: true,
    configure: (proxy: any, _options: any) => {
      proxy.on('error', (err: any, _req: any, _res: any) => {
        console.log('proxy error', err)
      })
    },
  }])
)

export default defineConfig({
  clearScreen: false,
  server: {
    watch: {
      ignored: [
        "**/*.fsproj",
        "dist/",
      ]
    },
    proxy,
  },
  plugins: [
    tailwindcss(),
  ],
})
