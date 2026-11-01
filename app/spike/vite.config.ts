import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  clearScreen: false,
  build: {
    target: 'chrome120',
    sourcemap: false,
    reportCompressedSize: true,
    chunkSizeWarningLimit: 2_000,
  },
  server: {
    strictPort: true,
  },
});
