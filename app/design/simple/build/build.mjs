// Builds the static Simple mockups from ../data/garden.json, so every number is the garden crawl's.
// Usage (from the repo root): node app/design/simple/build/build.mjs [page.html ...]
import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import * as simple from './pages/simple.mjs';
import * as screens from './pages/screens.mjs';
import { reportDocument } from './pages/report.mjs';

const out = join(dirname(fileURLToPath(import.meta.url)), '..');

export const pages = {
  'home.html': simple.homePage,
  'home-first.html': simple.homeFirstPage,
  'new-simple.html': screens.newSimplePage,
  'checking.html': simple.checkingPage,
  'summary.html': simple.summaryPage,
  'score.html': simple.scorePage,
  'summary-perfect.html': simple.summaryPerfectPage,
  'summary-stopped.html': simple.summaryStoppedPage,
  'fix.html': simple.fixPage,
  'problem.html': simple.problemPage,
  'problem-template.html': simple.problemTemplatePage,
  'report-screen.html': () => screens.reportScreenPage('simple'),
  'report.html': reportDocument,
  'settings-simple.html': () => screens.settingsPage('simple'),
  'settings-advanced.html': () => screens.settingsPage('advanced'),
  'new-advanced.html': screens.newAdvancedPage,
  'advanced-overview.html': screens.advancedOverviewPage,
  'advanced-live.html': screens.advancedLivePage,
};

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const only = process.argv.slice(2);
  for (const [file, render] of Object.entries(pages)) {
    if (only.length && !only.includes(file)) continue;
    const html = render();
    writeFileSync(join(out, file), html);
    console.log(`${file.padEnd(24)} ${(html.length / 1024).toFixed(0)} KB`);
  }
}
