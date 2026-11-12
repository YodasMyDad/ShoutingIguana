import { defineConfig, mergeConfig } from 'vitest/config';
import { filledTable } from './scripts/filled-table.mjs';
import viteConfig from './vite.config';

// Unit and component tests: jsdom, the app's own source only (never the spike or the design work). Two projects: the
// app as it is, and the app with every row of the routes' table set to a stub screen (`filled`), so a Simple screen
// round 4 sets can't break the frame (scripts/filled-table.mjs).
const unit = {
  environment: 'jsdom',
  include: ['src/**/*.test.{ts,tsx}'],
  setupFiles: ['src/test/setup.ts'],
  restoreMocks: true,
  unstubGlobals: true,
  css: false,
} as const;

export default defineConfig((env) =>
  mergeConfig(viteConfig(env), {
    test: {
      projects: [
        { extends: true, test: { ...unit, name: 'app' } },
        { extends: true, plugins: [filledTable()], test: { ...unit, name: 'filled' } },
      ],
    },
  }),
);
