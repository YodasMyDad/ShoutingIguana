// The Report screen (docs/v2-ui-simple.md, "The Report screen") and the score on Advanced's Overview, against the real
// sidecar: the engine's document in its sandboxed frame, the toolbar's figure against the document's own, the two
// cuts, printing the frame alone, saving the report and the spreadsheet through the export job, and the score's
// breakdown. A save has no dialog in a browser: the e2e build takes its folder from local storage
// (`si.e2e.saveFolder`), and the file is looked for there under the default name.
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { colourTweensEnd, countOf, read, seededCrawl, sidecar, type SeededCrawl } from './support';

const SHOTS = path.resolve('test-results', 'r4-report');

interface Analysis {
  pagesAnalyzed: number;
  analyzedAt: string;
  score: { value: number; band: string; partial: boolean; lost: number } | null;
}

const sidebar = (page: Page) => page.getByRole('navigation', { name: 'Main' });
const frame = (page: Page) => page.locator('iframe[title^="The report for"]');
const sheet = (page: Page) => page.frameLocator('iframe[title^="The report for"]');

/** The seeded crawl, analysed by this engine (its score is the report's). */
async function analysedCrawl(): Promise<{ crawl: SeededCrawl; analysis: Analysis }> {
  const crawl = await seededCrawl();
  let analysis = await read<Analysis>(`/api/crawls/${crawl.id}/analysis`).catch(() => null);
  if (!analysis?.score) {
    await sidecar(`/api/crawls/${crawl.id}/analyze`, { method: 'POST' });
    await expect
      .poll(async () => (await read<{ status: string }>(`/api/crawls/${crawl.id}`)).status, { timeout: 60_000 })
      .not.toBe('analyzing');
    analysis = await read<Analysis>(`/api/crawls/${crawl.id}/analysis`);
  }
  return { crawl, analysis };
}

/**
 * axe at WCAG 2.2 AA over the app, as `expectNoViolations` runs it, with the report's frame left out: the document is
 * the engine's (and axe can't open a frame with no scripts to run in it, so it fails on one).
 */
async function expectNoViolations(page: Page) {
  await colourTweensEnd(page);
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .exclude('iframe[title^="The report for"]')
    .analyze();
  expect(
    results.violations.map(
      (violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join('; ')}`,
    ),
  ).toEqual([]);
}

async function saveFolder(page: Page): Promise<string> {
  const folder = mkdtempSync(path.join(os.tmpdir(), 'si-e2e-report-'));
  await page.evaluate((where) => localStorage.setItem('si.e2e.saveFolder', where), folder);
  return folder;
}

test.describe('in Simple', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('Report is the third section: the engine’s document, sandboxed, under a toolbar that agrees with it', async ({
    page,
  }) => {
    const { crawl, analysis } = await analysedCrawl();
    await page.addInitScript(() => localStorage.setItem('si.settings', JSON.stringify({ mode: 'simple' })));
    await page.goto(`/crawls/${crawl.id}/overview`);
    await expect(sidebar(page).getByRole('link', { name: /^Summary/ })).toBeVisible();
    await page.keyboard.press('Control+3');
    await expect(page).toHaveURL(new RegExp(`/crawls/${crawl.id}/report$`));
    await expect(sidebar(page).getByRole('link', { name: /^Report/ })).toHaveAttribute('aria-current', 'page');

    await expect(frame(page)).toHaveAttribute('sandbox', 'allow-same-origin allow-modals');
    await expect(sheet(page).getByRole('heading', { level: 1 })).toBeVisible();
    // The toolbar's pages are the document's own: what the analysis read.
    const line = await page.getByText(/ · checked /).innerText();
    expect(line).toContain(`${analysis.pagesAnalyzed.toLocaleString('en-GB')} pages read`);
    await expect(sheet(page).locator('body')).toContainText(
      `${analysis.pagesAnalyzed.toLocaleString('en-GB')} pages read`,
    );
    // The score in the document is the analysis's.
    if (analysis.score) await expect(sheet(page).locator('body')).toContainText(String(analysis.score.value));

    // The report's one style sheet applies: it isn't the browser's default look.
    const font = await sheet(page)
      .locator('body')
      .evaluate((body) => getComputedStyle(body).fontFamily);
    expect(font).toMatch(/Inter|Segoe UI|system-ui/);
    await expectNoViolations(page);
  });

  test('the summary page is the report’s first part alone, and the cut is in the address', async ({ page }) => {
    const { crawl } = await analysedCrawl();
    await page.addInitScript(() => localStorage.setItem('si.settings', JSON.stringify({ mode: 'simple' })));
    await page.goto(`/crawls/${crawl.id}/report`);
    await expect(sheet(page).locator('body')).toContainText('What to fix');
    const full = (await sheet(page).locator('body').innerText()).length;
    await page.getByRole('radio', { name: 'Summary page' }).click();
    await expect(page).toHaveURL(/\?cut=summary$/);
    await expect.poll(async () => (await sheet(page).locator('body').innerText()).length).toBeLessThan(full);
    await page.reload();
    await expect(page.getByRole('radio', { name: 'Summary page' })).toHaveAttribute('aria-checked', 'true');
  });

  test('Print or save as PDF prints the frame alone, by its button and by Ctrl+P', async ({ page }) => {
    const { crawl } = await analysedCrawl();
    await page.addInitScript(() => localStorage.setItem('si.settings', JSON.stringify({ mode: 'simple' })));
    await page.goto(`/crawls/${crawl.id}/report`);
    await expect(sheet(page).getByRole('heading', { level: 1 })).toBeVisible();
    // The page reaches into the frame (allow-same-origin): the frame's print is what runs, never the app's.
    await page.evaluate(() => {
      const view = document.querySelector<HTMLIFrameElement>('iframe[title^="The report for"]')!.contentWindow!;
      (window as unknown as { printed: string[] }).printed = [];
      view.print = () => (window as unknown as { printed: string[] }).printed.push('frame');
      window.print = () => (window as unknown as { printed: string[] }).printed.push('app');
    });
    await page.getByRole('button', { name: /Print or save as PDF/ }).click();
    await page.keyboard.press('Control+p');
    expect(await page.evaluate(() => (window as unknown as { printed: string[] }).printed)).toEqual(['frame', 'frame']);
  });

  test('Save writes the report under its default name, and Spreadsheet of everything the workbook', async ({
    page,
  }) => {
    const { crawl } = await analysedCrawl();
    await page.addInitScript(() => localStorage.setItem('si.settings', JSON.stringify({ mode: 'simple' })));
    await page.goto(`/crawls/${crawl.id}/report`);
    await expect(sheet(page).getByRole('heading', { level: 1 })).toBeVisible();
    const folder = await saveFolder(page);
    try {
      await page.getByRole('button', { name: /^Save…/ }).click();
      const saved = page.getByText(/^Report saved: /);
      await expect(saved).toBeVisible();
      const name = (await saved.innerText()).replace('Report saved: ', '');
      expect(name).toMatch(/^.+ \d{4}-\d{2}-\d{2} report\.html$/);
      const file = path.join(folder, name);
      expect(existsSync(file)).toBe(true);
      // The file is the document the screen shows.
      const shown = await frame(page).getAttribute('srcdoc');
      expect(readFileSync(file, 'utf8')).toBe(shown);

      await page.getByRole('button', { name: /Spreadsheet of everything/ }).click();
      const workbook = page.getByText(/^Spreadsheet saved: /);
      await expect(workbook).toBeVisible({ timeout: 20_000 });
      const workbookName = (await workbook.innerText()).replace('Spreadsheet saved: ', '');
      expect(workbookName).toBe(name.replace(/\.html$/, '.xlsx'));
      expect(readFileSync(path.join(folder, workbookName)).subarray(0, 2).toString()).toBe('PK');
    } finally {
      rmSync(folder, { recursive: true, force: true });
    }
  });

  test('screens for the record: 1280x720 in both themes, and 200% zoom', async ({ page }) => {
    const { crawl } = await analysedCrawl();
    for (const theme of ['light', 'dark'] as const) {
      await page.addInitScript(
        (chosen) => localStorage.setItem('si.settings', JSON.stringify({ mode: 'simple', theme: chosen })),
        theme,
      );
      await page.setViewportSize({ width: 1280, height: 720 });
      await page.goto(`/crawls/${crawl.id}/report`);
      await expect(sheet(page).getByRole('heading', { level: 1 })).toBeVisible();
      await page.screenshot({ path: path.join(SHOTS, `report-simple-${theme}.png`) });
    }
    await page.setViewportSize({ width: 640, height: 360 });
    await page.goto(`/crawls/${crawl.id}/report`);
    await expect(sheet(page).getByRole('heading', { level: 1 })).toBeVisible();
    // At 200% zoom nothing runs off the side: the page never scrolls sideways.
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({ path: path.join(SHOTS, 'report-simple-200.png') });
  });
});

test('in Advanced, Report is the eighth section, and the Overview’s score opens its breakdown', async ({ page }) => {
  const { crawl, analysis } = await analysedCrawl();
  await page.goto(`/crawls/${crawl.id}/overview`);
  const score = page.getByRole('link', { name: /How it’s worked out/ });
  if (analysis.score) {
    await expect(score).toContainText(String(analysis.score.value));
    // On the garden crawl, the design's own numbers (docs/v2-ui-simple.md, Worked examples): 85, Good, 100 − 15.
    if (crawl.site === 'gardenfurnituresets.co.uk') {
      expect([analysis.score.value, analysis.score.band, analysis.score.lost]).toEqual([85, 'good', 15]);
      await expect(score).toContainText('Score: Good');
    }
    await score.click();
    const dialog = page.getByRole('dialog', { name: 'How the score is worked out' });
    await expect(dialog).toBeVisible();
    await expect(page).toHaveURL(/\?score=1$/);
    await expect(dialog).toContainText(`100 − ${analysis.score.lost}`);
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(score).toBeFocused();
    await page.screenshot({ path: path.join(SHOTS, 'overview-advanced-light.png') });
  }
  await page.keyboard.press('Control+8');
  await expect(page).toHaveURL(new RegExp(`/crawls/${crawl.id}/report$`));
  await expect(sheet(page).getByRole('heading', { level: 1 })).toBeVisible();
  await expect(page.getByText(/ · crawled /)).toBeVisible();
  await expectNoViolations(page);
  expect(
    countOf(
      await page
        .getByText(/pages read/)
        .first()
        .innerText(),
    ),
  ).toBeGreaterThan(0);
});
