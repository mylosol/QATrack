/**
 * Vite build for the QATrack board SPA.
 *
 * - `base: './'` emits relative asset URLs so the app also works when IIS
 *   hosts it under a virtual directory (e.g. https://server/qatrack/).
 * - The bundle is written straight into the ASP.NET Core wwwroot, which the
 *   backend serves as static files (spec 1: "served directly as static assets").
 * - The dev server proxies /api to the backend (`dotnet run`, port 5080).
 */
import { defineConfig } from 'vitest/config';

export default defineConfig({
  base: './',
  build: {
    outDir: '../Backend/KanbanBoard.Api/wwwroot',
    emptyOutDir: true,
    sourcemap: false,
    target: 'es2022',
  },
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5080',
    },
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.ts'],
    restoreMocks: true,
  },
});
