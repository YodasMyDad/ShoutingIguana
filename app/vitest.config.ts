import { defineConfig, mergeConfig } from 'vitest/config';
import viteConfig from './vite.config';

// Unit and component tests: jsdom, the app's own source only (never the spike or the design work).
export default defineConfig((env) =>
  mergeConfig(viteConfig(env), {
    test: {
      environment: 'jsdom',
      include: ['src/**/*.test.{ts,tsx}'],
      setupFiles: ['src/test/setup.ts'],
      restoreMocks: true,
      unstubGlobals: true,
      css: false,
    },
  }),
);
