import { defineConfig, devices } from '@playwright/test';
import { ADVANCED_STORAGE, BASE_URL } from './e2e/storage';

// End to end against the real sidecar: e2e/global-setup.ts starts one on a temp data folder holding a crawl, builds
// the app pointed at it and serves the build. Chromium stands in for WebView2 (the same engine, as the spike measured).
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
    baseURL: BASE_URL,
    storageState: ADVANCED_STORAGE,
    trace: 'retain-on-failure',
    viewport: { width: 1280, height: 800 },
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 800 } } }],
});
