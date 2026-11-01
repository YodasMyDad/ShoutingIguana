// Builds the five static pages of direction B from the real fixtures.
// Usage (from the repo root): node app/design/directions/clarity/build/build.mjs
import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const out = join(here, '..');

const pages = {
  'home.html': async () => (await import('./pages/home.mjs')).homePage(),
  'live.html': async () => (await import('./pages/live.mjs')).livePage(),
  'issues.html': async () => (await import('./pages/issues.mjs')).issuesPage(),
  'urls.html': async () => (await import('./pages/urls.mjs')).urlsPage(),
  'urls-loading.html': async () => (await import('./pages/urls.mjs')).urlsPage({ loading: true }),
};

const only = process.argv.slice(2);
for (const [file, render] of Object.entries(pages)) {
  if (only.length && !only.includes(file)) continue;
  const html = await render();
  writeFileSync(join(out, file), html);
  console.log(`${file.padEnd(18)} ${(html.length / 1024).toFixed(0)} KB`);
}
