// Round 5, part A: Simple's truth (the closing audit's gaps 1, 2, 11, 12 and G3), against the real sidecar and the
// engine's synthetic site (never a real site). An address typed without its scheme reaches the site where it answers;
// a stopped check has its results so far, with its toast's two actions under its text; a problem opens at its top and
// Back finds the list where it was; Where it is counts what it lists. Screenshots go to test-results/r5-simple/.
import path from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import type { CrawlListItem } from '../src/api/types';
import { libraryIds, removeCrawlsNotIn } from './startedCrawls';
import { colourTweensEnd, env, expectNoViolations, read, seededCrawl, sidecar } from './support';

const SHOTS = path.resolve(import.meta.dirname, '..', 'test-results', 'r5-simple');

// A first start: nothing stored, so Simple.
test.use({ storageState: { cookies: [], origins: [] } });

let seeded: string[] = [];
test.beforeAll(async () => {
  seeded = await libraryIds();
});
test.afterEach(async () => {
  await removeCrawlsNotIn(seeded);
  await sidecar('/api/crawl-options/defaults', { method: 'PUT', body: JSON.stringify({}) });
});

const main = (page: Page) => page.locator('#main');

async function shoot(page: Page, name: string) {
  await colourTweensEnd(page);
  await page.screenshot({ path: path.join(SHOTS, `${name}.png`) });
}

const crawlIdOf = (page: Page) => /\/crawls\/([0-9a-f]{16})\//.exec(page.url())?.[1] ?? '';

test('an address typed without its scheme is checked where the site answers: http, at once', async ({ page }) => {
  test.setTimeout(120_000);
  await sidecar('/api/crawl-options/defaults', {
    method: 'PUT',
    body: JSON.stringify({ concurrency: 16, maxRequestsPerSecond: 0, maxUrls: 60 }),
  });
  // The test site serves http only; typed as the audit typed it, "127.0.0.1:port", no scheme.
  const bare = env('SI_E2E_TESTSITE')
    .replace(/^https?:\/\//, '')
    .replace(/\/$/, '');
  await page.goto('/');
  const address = main(page).getByRole('textbox', { name: 'Web address' });
  await address.fill(bare);
  const started = Date.now();
  await address.press('Enter');
  await expect(page).toHaveURL(/\/crawls\/[0-9a-f]{16}\/overview$/, { timeout: 15_000 });
  const item = await read<CrawlListItem>(`/api/crawls/${crawlIdOf(page)}`);
  expect(item.startUrl).toBe(`http://${bare}/`);
  // It reads the site, and is done in seconds, not the two minutes https's robots.txt cool-down took.
  await expect(main(page).getByRole('img', { name: /^Score \d+ out of 100/ })).toBeVisible({ timeout: 60_000 });
  await expect(main(page).getByText('No pages could be read')).toBeHidden();
  test.info().annotations.push({ type: 'Enter to the Summary (ms)', description: String(Date.now() - started) });
  expect(Date.now() - started).toBeLessThan(60_000);
});

test('a stopped check has its results so far, and its toast’s two actions sit under its text', async ({ page }) => {
  test.setTimeout(180_000);
  await sidecar('/api/crawl-options/defaults', {
    method: 'PUT',
    body: JSON.stringify({ concurrency: 2, maxRequestsPerSecond: 20 }),
  });
  await page.goto('/');
  const address = main(page).getByRole('textbox', { name: 'Web address' });
  await address.fill(env('SI_E2E_TESTSITE'));
  await address.press('Enter');
  await expect(page).toHaveURL(/\/crawls\/[0-9a-f]{16}\/overview$/);
  const id = crawlIdOf(page);
  await expect
    .poll(async () => (await read<CrawlListItem>(`/api/crawls/${id}`)).pageCount, { timeout: 60_000 })
    .toBeGreaterThan(30);
  await main(page).getByRole('button', { name: /^Stop/ }).click();
  // The sidecar works out what it read: the Summary shows it, so far, with the stopped banner and Continue.
  await expect(main(page).getByRole('img', { name: /^Score \d+ out of 100 so far/ })).toBeVisible({
    timeout: 60_000,
  });
  await expect(main(page).getByText(/This check was stopped early/)).toBeVisible();
  await expect(main(page).getByRole('button', { name: 'Continue checking' }).first()).toBeVisible();
  await expect(main(page).getByText(/^Stopped on .+ so far\.$/)).toBeVisible();
  await expect(main(page).getByRole('heading', { name: 'No score yet' })).toBeHidden();
  const item = await read<CrawlListItem>(`/api/crawls/${id}`);
  expect(item.status).toBe('stopped');
  expect(item.analyzedAt).not.toBeNull();
  expect(item.score?.partial).toBe(true);

  // Its toast says the results so far, its two actions under its text, which keeps a readable measure.
  const toast = page.locator('.si-toast').filter({ hasText: 'was stopped' });
  await expect(toast).toContainText(/Score \d+ so far/, { timeout: 20_000 });
  await toast.hover();
  const text = await toast.locator('[data-content]').boundingBox();
  const buttons = await toast
    .locator('[data-button]')
    .evaluateAll((all) => all.map((button) => button.getBoundingClientRect().top));
  expect(buttons).toHaveLength(2);
  expect(text?.width ?? 0).toBeGreaterThanOrEqual(200);
  for (const top of buttons) expect(top).toBeGreaterThanOrEqual((text?.y ?? 0) + (text?.height ?? 0));
  await expectNoViolations(page);
  await shoot(page, 'summary-stopped-1280-light');
  await page.emulateMedia({ colorScheme: 'dark' });
  await expectNoViolations(page);
  await shoot(page, 'summary-stopped-1280-dark');
  await page.setViewportSize({ width: 640, height: 360 });
  await shoot(page, 'summary-stopped-200pct-dark');
});

test('a problem opens at its top from a scrolled What to fix, and Back finds the list where it was', async ({
  page,
}) => {
  const crawl = await seededCrawl();
  await page.setViewportSize({ width: 1280, height: 720 });
  await page.goto(`/crawls/${crawl.id}/issues`);
  const list = main(page).getByRole('heading', { level: 1, name: 'What to fix' });
  await expect(list).toBeVisible();
  const rows = main(page).locator('a[href*="/issues/"]');
  await expect(rows.first()).toBeVisible();
  const last = rows.last();
  await last.scrollIntoViewIfNeeded();
  const place = await main(page).evaluate((element) => element.scrollTop);
  expect(place, 'the list is long enough to scroll').toBeGreaterThan(0);
  await last.click();
  await expect(page).toHaveURL(/\/issues\/[^/]+$/);
  await expect(
    main(page)
      .getByRole('link', { name: /What to fix/ })
      .first(),
  ).toBeInViewport();
  expect(await main(page).evaluate((element) => element.scrollTop)).toBe(0);
  // Where it is counts what it lists, and says where its shortened addresses are.
  await expect(
    main(page).getByText(
      /(The one (cause|page|address) is shown|All [\d,]+ \w+ are shown|Showing [\d,]+ of [\d,]+ \w+)\./,
    ),
  ).toBeVisible();
  await expect(main(page).getByText(/It is shown\.|Addresses are on /)).toHaveCount(0);
  await shoot(page, 'problem-opened-from-scrolled-list-1280-light');

  await page.goBack();
  await expect(list).toBeVisible();
  await expect.poll(() => main(page).evaluate((element) => element.scrollTop)).toBe(place);
});
