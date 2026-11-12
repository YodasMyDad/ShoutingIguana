// The end-to-end tests of the frame (the mode, the top bar, New crawl) with every row of the routes' table set to a
// stub screen (scripts/filled-table.mjs): `npm run e2e:filled`, with any of Playwright's options after `--`.
import { spawnSync } from 'node:child_process';

const specs = ['e2e/mode.spec.ts', 'e2e/topbar.spec.ts', 'e2e/new-crawl.spec.ts'];
const result = spawnSync('npx', ['playwright', 'test', ...specs, ...process.argv.slice(2)], {
  stdio: 'inherit',
  shell: process.platform === 'win32',
  env: { ...process.env, SI_FILLED_TABLE: '1' },
});
process.exit(result.status ?? 1);
