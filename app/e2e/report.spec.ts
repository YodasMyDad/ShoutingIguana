// The Report screen (docs/v2-ui-simple.md, "The Report screen") and the score on Advanced's Overview, against the real
// sidecar: the engine's document in its sandboxed frame, the toolbar's figure against the document's own, the two
// cuts, printing the frame alone, saving the report and the spreadsheet through the export job, and the score's
// breakdown. Save goes through the app's one export dialog, whose path field stands in for the save dialog a browser
// doesn't have.
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
    const folder = mkdtempSync(path.join(os.tmpdir(), 'si-e2e-report-'));
    // The app's one export dialog: a browser has no save dialog, so its field takes the path, opened on the default
    // name. The toast says Saved with the file's name, and the file is the document the screen shows.
    const dialog = page.getByRole('dialog');
    const saveAs = async (button: RegExp, choice: RegExp, name: RegExp) => {
      await page.getByRole('button', { name: button }).click();
      await expect(dialog.getByRole('radio', { name: choice })).toBeChecked();
      const field = dialog.getByRole('textbox', { name: 'Save as' });
      const offered = await field.inputValue();
      expect(offered).toMatch(name);
      // The name it offers, in a folder of this test's own (no folder was saved to yet, so it's the name alone).
      const file = path.join(folder, path.win32.basename(offered));
      await field.fill(file);
      await dialog.getByRole('button', { name: 'Export', exact: true }).click();
      await expect(dialog).toBeHidden();
      await expect(
        page
          .locator('.si-export-toast')
          .filter({ hasText: path.basename(file) })
          .getByText('Saved', { exact: true }),
      ).toBeVisible({ timeout: 30_000 });
      return file;
    };
    try {
      const file = await saveAs(/^Save as HTML…/, /The report/, /^.+ \d{4}-\d{2}-\d{2} report\.html$/);
      expect(existsSync(file)).toBe(true);
      // The saved file is the document shown, without the app's scroll bars, which are the frame's alone (G6).
      const shown = (await frame(page).getAttribute('srcdoc')) ?? '';
      expect(shown).toContain('<style id="si-app-scroll-bars">');
      const saved = readFileSync(file, 'utf8');
      expect(saved).not.toContain('si-app-scroll-bars');
      // Nor the app's own parts (the UX audit's S-11): its style, and the link in each problem's heading, where the
      // saved file has the engine's comment.
      expect(shown).toContain('class="si-open"');
      expect(saved).not.toContain('si-open"');
      expect(saved).toBe(
        shown
          .replace(/<style id="si-app-scroll-bars">[\s\S]*?<\/style>/, '')
          .replace(/<style id="si-app-report">[\s\S]*?<\/style>/, '')
          .replace(/<a class="si-open" data-si-open="([a-z0-9-]+)"[^>]*>[^<]*<\/a>/g, '<!--si:open $1-->'),
      );

      const workbook = await saveAs(/^Save spreadsheet…/, /Spreadsheet of everything/, /^.+ report\.xlsx$/);
      expect(path.basename(workbook)).toBe(path.basename(file).replace(/\.html$/, '.xlsx'));
      expect(readFileSync(workbook).subarray(0, 2).toString()).toBe('PK');
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
    // On the garden crawl of 31 October 2026, what the engine scores it now: 87, Good, 100 − 13 (the design's worked
    // example, docs/v2-ui-simple.md, was 85 before two of its checks became notices).
    if (crawl.site === 'gardenfurnituresets.co.uk') {
      expect([analysis.score.value, analysis.score.band, analysis.score.lost]).toEqual([87, 'good', 13]);
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
