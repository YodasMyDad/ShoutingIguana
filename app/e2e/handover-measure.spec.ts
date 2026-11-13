// Measures hand over and search against their budgets (docs/v2-ui-brief.md, Performance budgets): Ctrl+E to the
// export dialog, and a palette keystroke to its results (100 ms each), and the URL grid exported as Excel end to end,
// the time the toast shows against the engine's own. Only when asked, on a big crawl:
//   SI_E2E_MEASURE=1 SI_E2E_CRAWL=<the 100k crawl> npx playwright test e2e/handover-measure.spec.ts
// Each time is from the input to the frame after the condition holds, measured in the page.
import { expect, test, type Page } from '@playwright/test';
import { mkdtempSync, rmSync, statSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { read, seededCrawl } from './support';

test.skip(!process.env.SI_E2E_MEASURE, 'Measures only when SI_E2E_MEASURE is set');

let crawl = '';
test.beforeAll(async () => {
  crawl = (await seededCrawl()).id;
});

const say = (line: string) => process.stdout.write(`${line}\n`);

/** Runs `act` in the page and waits, frame by frame, for `done`: the time from the act to the frame after. */
async function timed(page: Page, act: string, done: string): Promise<number> {
  return page.evaluate<number>(`(async () => {
    const frame = () => new Promise((resolve) => requestAnimationFrame(resolve));
    const start = performance.now();
    ${act};
    for (let frames = 0; frames < 100000 && !(${done}); frames++) await frame();
    await frame();
    return performance.now() - start;
  })()`);
}

const KEY = (key: string, ctrl = false) =>
  `document.activeElement.dispatchEvent(new KeyboardEvent('keydown', { key: ${JSON.stringify(key)}, code: ${JSON.stringify(
    ctrl ? `Key${key.toUpperCase()}` : '',
  )}, ctrlKey: ${ctrl}, bubbles: true, cancelable: true }))`;

test('export and search on this crawl, against their budgets', async ({ page }) => {
  test.setTimeout(20 * 60_000);
  await page.setViewportSize({ width: 1280, height: 720 });
  await page.goto(`/crawls/${crawl}/urls`);
  const grid = page.getByRole('grid', { name: 'URLs' });
  await expect(grid.getByRole('row').nth(1)).toBeVisible({ timeout: 60_000 });
  await grid.focus();

  // Ctrl+E to the dialog, five times.
  const toDialog: number[] = [];
  for (let run = 0; run < 5; run++) {
    toDialog.push(
      Math.round(
        await timed(
          page,
          KEY('e', true),
          `!!document.querySelector('[role="dialog"] input[aria-label], [role="dialog"] [role="radiogroup"]')`,
        ),
      ),
    );
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog')).toBeHidden();
    await grid.focus();
  }
  say(`Ctrl+E to the export dialog: ${toDialog.join(', ')} ms`);

  // A palette keystroke to its results: the palette open (its warm-up done), then a letter at a time.
  await page.keyboard.press('Control+k');
  const field = page.getByRole('combobox');
  await expect(field).toBeFocused();
  await page.waitForTimeout(500);
  const toResults: number[] = [];
  let typed = '';
  for (const letter of 'product') {
    typed += letter;
    const ms = await timed(
      page,
      `(() => { const input = document.querySelector('[role="combobox"]'); const set = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set; set.call(input, ${JSON.stringify(typed)}); input.dispatchEvent(new Event('input', { bubbles: true })); })()`,
      `[...document.querySelectorAll('[role="option"] mark')].some((mark) => mark.textContent.toLowerCase() === ${JSON.stringify(typed)})`,
    );
    toResults.push(Math.round(ms));
  }
  say(`A palette keystroke to its marked results ("${typed}", a letter at a time): ${toResults.join(', ')} ms`);
  await page.keyboard.press('Escape');

  // The whole URL grid as Excel, end to end: from Export to the toast's Saved, against the engine's own time.
  const folder = mkdtempSync(path.join(os.tmpdir(), 'si-measure-export-'));
  try {
    const file = path.join(folder, 'urls.xlsx');
    await grid.focus();
    await page.keyboard.press('Control+e');
    const box = page.getByRole('dialog', { name: 'Export URLs' });
    await box.getByRole('radio', { name: 'Excel' }).check();
    await box.getByRole('textbox', { name: 'Save as' }).fill(file);
    const total = await read<{ total: number }>(`/api/crawls/${crawl}/urls`, { columns: ['url'], limit: 1 });
    const ms = await timed(
      page,
      `[...document.querySelectorAll('[role="dialog"] button')].find((b) => b.textContent === 'Export').click()`,
      `[...document.querySelectorAll('.si-export-toast')].some((t) => t.textContent.includes('urls.xlsx') && t.textContent.includes('Saved'))`,
    );
    const toast = page.locator('.si-export-toast').filter({ hasText: 'urls.xlsx' });
    const shown = await toast.innerText();
    say(
      `The URL grid (${total.total.toLocaleString('en-GB')} URLs) as Excel, Export to Saved in the page: ${Math.round(ms)} ms; ` +
        `the toast says “${shown.replace(/\s+/g, ' ').trim()}”; the file is ${(statSync(file).size / 1_048_576).toFixed(1)} MB`,
    );
  } finally {
    rmSync(folder, { recursive: true, force: true });
  }
});
