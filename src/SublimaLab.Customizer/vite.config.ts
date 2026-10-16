/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { fileURLToPath } from 'node:url';

// The studio is built as an ES module library into the storefront's wwwroot, where the
// Blazor page loads it on demand. The 3D viewer (three.js) is a separate, lazily imported chunk,
// so neither the storefront nor the 2D editor pays for WebGL until the preview is shown.
export default defineConfig(({ mode }) => ({
  plugins: [react()],
  define: {
    'process.env.NODE_ENV': JSON.stringify(mode === 'test' || mode === 'development' ? mode : 'production'),
  },
  build: {
    outDir: fileURLToPath(new URL('../SublimaLab.Blazor/wwwroot/customizer', import.meta.url)),
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: false,
    lib: {
      entry: fileURLToPath(new URL('./src/main.tsx', import.meta.url)),
      formats: ['es'],
      fileName: () => 'customizer.js',
      cssFileName: 'customizer',
    },
    rollupOptions: {
      output: {
        chunkFileNames: 'chunks/[name]-[hash].js',
        // The host page links the stylesheet by a stable name; other assets are fingerprinted.
        assetFileNames: (asset) => (asset.names.some((n) => n.endsWith('.css')) ? 'customizer.css' : 'assets/[name]-[hash][extname]'),
      },
    },
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.ts', 'src/**/*.test.tsx'],
  },
}));
