import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react-swc'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5064',
        changeOrigin: true,
      },
      '/auth': {
        target: 'http://localhost:5064',
        changeOrigin: true,
      },
      '/billing': {
        target: 'http://localhost:5064',
        // Keeps the browser's host, so Stripe's return addresses point back at the dev server.
        changeOrigin: false,
        // Stripe's return to /billing/success or /billing/cancel is a page of the app.
        bypass: (req) => (req.headers.accept?.includes('text/html') ? '/index.html' : undefined),
      },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
  },
})
