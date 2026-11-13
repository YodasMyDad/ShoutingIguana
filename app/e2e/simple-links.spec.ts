// The links between round 4's parts, end to end against the real sidecar (docs/v2-ui-simple.md; the architecture
// doc's "Simple mode (round 4)"): Home's row and the Summary to the Report, the Summary's counts to What to fix, the
// keys between Simple's three sections and to Advanced's eighth, Advanced's Overview score to the breakdown, and the
// report's Save into the app's one export dialog. A finished check's toast to the Report and the checking screen's
// hand-over to the Summary are walked with a live check in e2e/simple-home.spec.ts.
import { expect, test, type Page } from '@playwright/test';
import type { Analysis } from '../src/api/types';
import { read, seededCrawl, sidecar, site, type SeededCrawl } from './support';

const main = (page: Page) => page.locator('#main');
const sheet = (page: Page) => page.frameLocator('iframe[title^="The report for"]');
const reportOf = (id: string) => new RegExp(`/crawls/${id}/report$`);

/** The seeded crawl, analysed by this engine, so it has a score and a report. */
async function scoredCrawl(): Promise<{ crawl: SeededCrawl; analysis: Analysis }> {
  const crawl = await seededCrawl();
  let analysis = await read<Analysis>(`/api/crawls/${crawl.id}/analysis`).catch(() => null);
  if (!analysis?.score) {
    await sidecar(`/api/crawls/${crawl.id}/analyze`, { method: 'POST' });
    await expect
      .poll(async () => (await read<{ status: string }>(`/api/crawls/${crawl.id}`)).status, { timeout: 60_000 })
      .not.toBe('analyzing');
    analysis = await read<Analysis>(`/api/crawls/${crawl.id}/analysis`);
  }
  expect(analysis.score, 'the seeded crawl is scored by the current engine').toBeTruthy();
  return { crawl, analysis };
}

test.describe('in Simple', () => {
  // A first start: nothing stored, so Simple.
  test.use({ storageState: { cookies: [], origins: [] } });

  test('Home’s row opens the report, and the Summary’s Open the report does too', async ({ page }) => {
    const { crawl } = await scoredCrawl();
    await page.goto('/');
    await main(page)
      .getByRole('button', { name: `More for ${site()}` })
      .first()
      .click();
    await page.getByRole('menuitem', { name: 'Open the report' }).click();
    await expect(page).toHaveURL(reportOf(crawl.id));
    await expect(sheet(page).getByRole('heading', { level: 1 })).toBeVisible({ timeout: 20_000 });

    // The Summary's Open the report, its primary action (the report's card below says it again).
    await page.goto(`/crawls/${crawl.id}/overview`);
    await expect(main(page).getByRole('img', { name: /^Score \d+ out of 100/ })).toBeVisible({ timeout: 20_000 });
    await main(page).getByRole('link', { name: 'Open the report' }).first().click();
    await expect(page).toHaveURL(reportOf(crawl.id));
    await expect(sheet(page).getByRole('heading', { level: 1 })).toBeVisible({ timeout: 20_000 });
  });

  test('the Summary’s counts open What to fix on their part; Ctrl+1 to Ctrl+3 are Simple’s three sections', async ({
    page,
  }) => {
    const { crawl } = await scoredCrawl();
    await page.goto(`/crawls/${crawl.id}/overview`);
    await expect(main(page).getByRole('img', { name: /^Score \d+ out of 100/ })).toBeVisible({ timeout: 20_000 });
    // The first of the counts beside the score that is a link (a count of 0 is none): it lands on its part.
    const first = main(page).locator('a[href*="/issues#"]').first();
    await expect(first).toBeVisible();
    const hash = new URL((await first.getAttribute('href'))!, page.url()).hash;
    await first.click();
    await expect(page).toHaveURL(new RegExp(`/crawls/${crawl.id}/issues${hash}$`));
    await expect(main(page).getByRole('heading', { level: 1, name: 'What to fix' })).toBeVisible();
    await expect(page.locator(hash)).toBeVisible();

    await page.keyboard.press('Control+1');
    await expect(page).toHaveURL(new RegExp(`/crawls/${crawl.id}/overview$`));
    await expect(main(page).getByRole('img', { name: /^Score \d+ out of 100/ })).toBeVisible();
    await page.keyboard.press('Control+2');
    await expect(page).toHaveURL(new RegExp(`/crawls/${crawl.id}/issues$`));
    await expect(main(page).getByRole('heading', { level: 1, name: 'What to fix' })).toBeVisible();
    await page.keyboard.press('Control+3');
    await expect(page).toHaveURL(reportOf(crawl.id));
    await expect(sheet(page).getByRole('heading', { level: 1 })).toBeVisible({ timeout: 20_000 });
  });

  test('the report’s Save and Spreadsheet of everything open the app’s one export dialog on their choice', async ({
    page,
  }) => {
    const { crawl } = await scoredCrawl();
    await page.goto(`/crawls/${crawl.id}/report?cut=summary`);
    await expect(sheet(page).getByRole('heading', { level: 1 })).toBeVisible({ timeout: 20_000 });
    await page.getByRole('button', { name: /^Save…/ }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByRole('radio', { name: /The report/ })).toBeChecked();
    // The cut shown is the cut saved, under the design's default name.
    await expect(dialog.getByRole('textbox', { name: 'Save as' })).toHaveValue(
      / \d{4}-\d{2}-\d{2} report summary\.html$/,
    );
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(page.getByRole('button', { name: /^Save…/ })).toBeFocused();
    await page.getByRole('button', { name: /Spreadsheet of everything/ }).click();
    await expect(dialog.getByRole('radio', { name: /Spreadsheet of everything/ })).toBeChecked();
    await expect(dialog.getByRole('textbox', { name: 'Save as' })).toHaveValue(/ \d{4}-\d{2}-\d{2} report\.xlsx$/);
    await page.keyboard.press('Escape');
  });
});

test('in Advanced, the Overview’s score opens the breakdown the Summary opens, and Ctrl+8 is the Report', async ({
  page,
}) => {
  const { crawl, analysis } = await scoredCrawl();
  await page.goto(`/crawls/${crawl.id}/overview`);
  const score = page.getByRole('link', { name: /How it’s worked out/ });
  await score.click();
  const dialog = page.getByRole('dialog', { name: 'How the score is worked out' });
  await expect(dialog).toBeVisible();
  await expect(page).toHaveURL(/\?score=1$/);
  // Part B's dialog: its sum, its bands and its version, in Advanced as in Simple.
  await expect(dialog).toContainText(`100 − ${analysis.score!.lost}`);
  await expect(dialog).toContainText(`Score version ${analysis.score!.version}`);
  // Its footer's Close (the ✕ is named Close too).
  await dialog.locator('button', { hasText: 'Close' }).click();
  await expect(dialog).toBeHidden();
  await expect(page).not.toHaveURL(/score=1/);
  await expect(score).toBeFocused();
  // A deep link opens it too, and Esc gives focus to the score.
  await page.goto(`/crawls/${crawl.id}/overview?score=1`);
  await expect(dialog).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(score).toBeFocused();

  await page.keyboard.press('Control+8');
  await expect(page).toHaveURL(reportOf(crawl.id));
  await expect(sheet(page).getByRole('heading', { level: 1 })).toBeVisible({ timeout: 20_000 });
});
