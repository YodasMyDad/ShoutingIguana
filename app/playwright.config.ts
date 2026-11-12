import { defineConfig, devices } from '@playwright/test';
import { PREVIEW_PORT } from './scripts/ports.mjs';

// End to end against the real sidecar: e2e/global-setup.ts starts one on a temp data folder holding a crawl, builds
// the app pointed at it and serves the build. Chromium stands in for WebView2 (the same engine, as the spike measured).
const baseURL = `http://localhost:${PREVIEW_PORT}`;

/**
 * Every spec starts in Advanced, which the screens built before Simple were written for: the app's preferences as a
 * plain browser keeps them. A spec of Simple, or of a first start, starts from nothing (`storageState` emptied).
 */
export const ADVANCED_STORAGE = {
  cookies: [],
  origins: [{ origin: baseURL, localStorage: [{ name: 'si.settings', value: JSON.stringify({ mode: 'advanced' }) }] }],
};

export default defineConfig({
  testDir: 'e2e',
  globalSetup: './e2e/global-setup.ts',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  timeout: 30_000,
  use: {
    baseURL,
    storageState: ADVANCED_STORAGE,
    trace: 'retain-on-failure',
    viewport: { width: 1280, height: 800 },
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 800 } } }],
});
