/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// The tests run with the machine's clock in Auckland. Rotas are shown in the business's time zone, never the
// browser's, so a helper that leaned on the machine's zone would fail here.
process.env.TZ = 'Pacific/Auckland';

export default defineConfig({
  plugins: [react()],
  server: {
    // In development the API runs on its own port; the browser still sees one origin, so the cookie just works.
    proxy: { '/api': 'http://localhost:5137' },
  },
  build: {
    // The API serves the built app from its wwwroot, on the same origin as /api.
    outDir: '../src/Rotadesk.Api/wwwroot',
    emptyOutDir: true,
  },
  test: {
    include: ['src/**/*.test.{ts,tsx}'],
  },
});
