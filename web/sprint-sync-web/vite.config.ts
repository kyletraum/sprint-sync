import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Aspire injects the API address; fall back to the local launch profile.
    proxy: {
      '/api': {
        target:
          process.env.services__api__https__0 ??
          process.env.services__api__http__0 ??
          'http://localhost:5080',
        changeOrigin: true,
        secure: false,
      },
    },
  },
  test: {
    pool: 'threads',
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
  },
});
