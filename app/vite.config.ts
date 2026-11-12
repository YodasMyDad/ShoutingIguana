import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { FILLED_TABLE, filledTable } from './scripts/filled-table.mjs';
import { DEV_SERVER_PORT, PREVIEW_PORT } from './scripts/ports.mjs';

// The dev server's port is fixed: the Tauri shell's devUrl and the dev sidecar's allowed origin both name it.
export default defineConfig(({ mode }) => ({
  // `SI_FILLED_TABLE=1`: every row of the routes' table a stub screen, for `npm run e2e:filled`.
  plugins: [react(), tailwindcss(), ...(FILLED_TABLE ? [filledTable()] : [])],
  clearScreen: false,
  server: {
    host: 'localhost',
    port: DEV_SERVER_PORT,
    strictPort: true,
    // The spike and the design work have their own tooling: never watch or serve them as app code.
    watch: { ignored: ['**/spike/**', '**/src-tauri/**'] },
  },
  preview: { host: 'localhost', port: PREVIEW_PORT, strictPort: true },
  build: {
    // WebView2 is evergreen Chromium; macOS and Linux webviews are WebKit.
    target: ['chrome120', 'safari17'],
    // Source maps for development, tests and the mock build; a release ships none.
    sourcemap: mode !== 'production',
    reportCompressedSize: false,
  },
}));
