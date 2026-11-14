// Advanced's Home as designed (docs/v2-ui-ia.md, Home; the closing audit's gaps 4 and 31), against the real sidecar: a
// crawl of the engine's test site running as a card above the library, paused and stopped from it; the library
// grouped by site with a crawl's actions in its menu (Remove and Undo, Delete file… asking first); every state of it
// axe-clean in both themes and at 200% zoom with nothing scrolling sideways. Overview's totals and Issues' "% of total"
// are drawn on the seeded crawl. The screenshots (1280x720 in both themes, and 200%) go to test-results/r5-home/.
import { expect, test, type Page } from '@playwright/test';
import path from 'node:path';
import { countOf, env, expectNoViolations, read, seededCrawl, siteRow } from './support';
import { libraryIds, removeCrawlsNotIn } from './startedCrawls';

const SHOTS = path.join(import.meta.dirname, '..', 'test-results', 'r5-home');

async function shoot(page: Page, name: string) {
  for (const theme of ['light', 'dark'] as const) {
    await page.emulateMedia({ colorScheme: theme });
    await expectNoViolations(page);
    await page.screenshot({ path: path.join(SHOTS, `${name}-${theme}.png`) });
  }
  await page.emulateMedia({ colorScheme: 'light' });
}

/** Nothing on the screen scrolls sideways: the page, the main area, or a card past the window's edge. */
async function expectNoSidewaysScroll(page: Page) {
  const overflow = await page.evaluate(() => {
    const main = document.querySelector('main');
    return {
      page: document.scrollingElement!.scrollWidth - document.scrollingElement!.clientWidth,
      main: main ? main.scrollWidth - main.clientWidth : -1,
    };
  });
  expect(overflow).toEqual({ page: 0, main: 0 });
}

let seeded: string[] = [];
test.beforeAll(async () => {
  seeded = await libraryIds();
});
test.afterEach(async () => {
  await removeCrawlsNotIn(seeded);
});

test('a running crawl is a card above the library, with its live figures and its controls', async ({ page }) => {
  test.setTimeout(120_000);
  await page.setViewportSize({ width: 1280, height: 720 });
  const started = await read<{ id: string }>('/api/crawls', {
    options: { startUrl: env('SI_E2E_TESTSITE'), concurrency: 2, maxRequestsPerSecond: 20 },
  });
  await page.goto('/');
  // Named by its heading, which says what the crawls do as the sidebar's does: "1 crawling", "1 paused".
  const running = page.getByRole('region', { name: /^\d+ (crawling|paused|stopping|analysing)/ });
  const card = running.getByRole('article');
  await expect(card).toHaveCount(1);
  const crawled = () => card.locator('.si-stat__value').first().innerText();
  await expect.poll(async () => countOf(await crawled()), { timeout: 30_000 }).toBeGreaterThan(20);
  await shoot(page, 'home-running');
  // At 200% zoom the card's figures wrap into rows, and nothing scrolls sideways.
  await page.setViewportSize({ width: 640, height: 360 });
  await card.scrollIntoViewIfNeeded();
  await expectNoSidewaysScroll(page);
  await shoot(page, 'home-running-200');
  await page.setViewportSize({ width: 1280, height: 720 });

  // Pause from the card: it says so, the sidebar too, and Resume takes Pause's place.
  await card.getByRole('button', { name: /^Pause/ }).click();
  await expect(card.getByText('Paused', { exact: true })).toBeVisible({ timeout: 15_000 });
  await expect(
    page.getByRole('region', { name: 'Running crawls' }).getByRole('heading', { name: '1 paused' }),
  ).toBeVisible();
  await expect(running.getByRole('heading', { name: '1 paused' })).toBeVisible();
  await expect(card.getByRole('button', { name: /^Resume/ })).toBeVisible();
  await shoot(page, 'home-paused');

  // Stop: the card goes, and the crawl joins the library, stopped, with Continue beside its status.
  await card.getByRole('button', { name: /^Stop/ }).click();
  await expect(running).toBeHidden({ timeout: 30_000 });
  const row = page
    .getByRole('region', { name: 'Library' })
    .getByRole('row')
    .filter({ has: page.locator(`a[href$="/crawls/${started.id}"]`) });
  await expect(row.getByRole('button', { name: /^Continue,/ })).toBeVisible({ timeout: 30_000 });
  await expect(page.locator('main')).not.toBeFocused();
  await shoot(page, 'home-stopped');
});

for (const size of [
  { width: 640, height: 360 },
  { width: 1280, height: 720 },
]) {
  test(`a running crawl's card keeps its height as its figures arrive, at ${size.width}x${size.height} (R1-10)`, async ({
    page,
  }) => {
    test.setTimeout(60_000);
    await page.setViewportSize(size);
    await page.goto('/');
    await expect(page.getByRole('region', { name: 'Library' })).toBeVisible();
    // Every height the card has from the frame it first shows, read on every frame.
    await page.evaluate(() => {
      const heights: number[] = [];
      (window as unknown as { cardHeights: number[] }).cardHeights = heights;
      const frame = () => {
        const card = document.querySelector('main article');
        if (card) heights.push(Math.round(card.getBoundingClientRect().height));
        requestAnimationFrame(frame);
      };
      requestAnimationFrame(frame);
    });
    await read<{ id: string }>('/api/crawls', {
      options: { startUrl: env('SI_E2E_TESTSITE'), concurrency: 2, maxRequestsPerSecond: 20 },
    });
    const crawled = page.locator('main article .si-stat__value').first();
    await expect.poll(async () => countOf(await crawled.innerText()), { timeout: 30_000 }).toBeGreaterThan(5);
    await page.waitForTimeout(1_000);
    const heights = await page.evaluate(() => (window as unknown as { cardHeights: number[] }).cardHeights);
    expect(heights.length).toBeGreaterThan(10);
    expect([...new Set(heights)]).toEqual([heights[0]]);
  });
}

test('a crawl’s actions are in its menu: Remove puts it back with Undo, Delete file asks first', async ({ page }) => {
  await page.goto('/');
  const library = page.getByRole('region', { name: 'Library' });
  await expect(library.getByRole('link', { name: siteRow(), exact: true })).toBeVisible();

  await library.getByRole('button', { name: `Actions for ${siteRow()}` }).click();
  const menu = page.getByRole('menu');
  await expect(menu.getByRole('menuitem')).toHaveText([
    'Open',
    'Crawl again…',
    'Analyse again',
    'Remove from library',
    'Delete file…',
  ]);
  await shoot(page, 'home-menu');
  await menu.getByRole('menuitem', { name: 'Remove from library' }).click();
  await expect(library.getByRole('link', { name: siteRow(), exact: true })).toBeHidden();
  // Focus never falls to the page when the row goes.
  await expect.poll(() => page.evaluate(() => document.activeElement !== document.body)).toBe(true);
  await page.locator('[data-sonner-toast]').getByRole('button', { name: 'Undo' }).click();
  await expect(library.getByRole('link', { name: siteRow(), exact: true })).toBeVisible({ timeout: 10_000 });
  // The toast gives focus back as it goes: wait for it, as a hand on the mouse would.
  await expect(page.locator('[data-sonner-toast]')).toHaveCount(0, { timeout: 15_000 });

  // Delete file… names the crawl and its file, and Cancel leaves it be.
  await library.getByRole('button', { name: `Actions for ${siteRow()}` }).click();
  await page.getByRole('menuitem', { name: 'Delete file…' }).click();
  const dialog = page.getByRole('dialog', { name: `Delete ${siteRow()}’s crawl file?` });
  await expect(dialog).toContainText('is deleted from the disk');
  await shoot(page, 'home-delete');
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  await expect(dialog).toBeHidden();
  expect((await read<{ id: string }[]>('/api/crawls')).length).toBe(seeded.length);
});

test('the library at 1280x720 and at 200% zoom, older crawls folded under their site', async ({ page }) => {
  test.setTimeout(120_000);
  // A second crawl of the seeded crawl's site: the site's row shows the newest, the seeded one folded under it.
  const crawl = await seededCrawl();
  const again = await read<{ id: string }>('/api/crawls', {
    options: { startUrl: env('SI_E2E_TESTSITE'), concurrency: 4, maxRequestsPerSecond: 0, maxUrls: 50 },
  });
  for (;;) {
    const item = await read<{ status: string }>(`/api/crawls/${again.id}`);
    if (!['running', 'paused', 'stopping', 'analyzing'].includes(item.status)) break;
    await page.waitForTimeout(250);
  }
  await page.setViewportSize({ width: 1280, height: 720 });
  await page.goto('/');
  const library = page.getByRole('region', { name: 'Library' });
  await expect(library.locator(`a[href$="/crawls/${again.id}"]`)).toBeVisible();
  const groups = await library.getByRole('row').count();
  // Grouped with the seeded crawl when the library takes them for one site (the same host, and since round 5's part
  // A the same port): then the seeded one is folded under it.
  const fold = library.getByRole('button', { name: /older crawl/ });
  if ((await fold.count()) > 0) {
    await fold.click();
    await expect(library.getByRole('row')).toHaveCount(groups + 1);
    await expect(library.locator(`a[href$="/crawls/${crawl.id}"]`)).toBeVisible();
  }
  await expectNoSidewaysScroll(page);
  await shoot(page, 'home-library');

  await page.setViewportSize({ width: 640, height: 360 });
  await library.scrollIntoViewIfNeeded();
  await expectNoSidewaysScroll(page);
  await shoot(page, 'home-library-200');
});

test('Overview opens with its totals, and Issues says each check’s share beside its count', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 720 });
  const crawl = await seededCrawl();
  await page.goto(`/crawls/${crawl.id}/overview`);
  const totals = page.getByRole('region', { name: 'Totals' });
  await expect(totals.getByRole('link')).toHaveCount(5);
  await expect(totals.locator('.si-stat__value').first()).toHaveText(/\d/);
  await shoot(page, 'overview-totals');
  await page.setViewportSize({ width: 640, height: 360 });
  await expectNoSidewaysScroll(page);
  await shoot(page, 'overview-totals-200');

  await page.setViewportSize({ width: 1280, height: 720 });
  await page.goto(`/crawls/${crawl.id}/issues`);
  const list = page.getByRole('listbox');
  await expect(list.getByRole('option').first()).toBeVisible();
  // A share beside a count, said whole in the row's name.
  await expect(
    list.getByRole('option', { name: /, [<>\d.]+% of the [\d,]+ (pages analysed|URLs crawled)$/ }).first(),
  ).toBeVisible();
  await shoot(page, 'issues-share');
});
