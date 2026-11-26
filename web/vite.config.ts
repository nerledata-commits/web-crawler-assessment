import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// In development the API runs on http://localhost:5080; proxying avoids CORS.
// In docker-compose, nginx does the same proxying (see web/nginx.conf).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5080',
    },
  },
})
