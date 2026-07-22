import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'

// Mirrors frontend-admin/vitest.config.ts (same alias shape as vite.config.ts).
// jsdom is required: api-client.ts / useCartStore.ts / useLocale.ts all touch
// sessionStorage/localStorage at module load time.
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      '@shared': fileURLToPath(new URL('../shared', import.meta.url)),
    },
  },
  test: {
    environment: 'jsdom',
    globals: false,
  },
})
