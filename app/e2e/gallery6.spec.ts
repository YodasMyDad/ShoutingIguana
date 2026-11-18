// What the version 6 gallery found, in a 1280x720 window (the gallery's: 1920x1080 at 150%), against the real sidecar
// and its synthetic crawl: a sitemap's name shows whole while the table has room for it; Simple Home's Site column
// takes the room its row has, so a long site's name shows whole; and an issue's affected URLs, a found-on list open,
// scroll with the pane, one scroll bar, under a whole focus ring.
import { expect, test, type Locator, type Page } from '@playwright/test';
import type { CrawlListItem } from '../src/api/types';
import { openSection, seededCrawl, showChecks } from './support';

test.use({ viewport: { width: 1280, height: 720 } });

/** A shown name is cut when its text is wider than its box, or it carries the middle truncation's ellipsis. */
async function cut(locator: Locator): Promise<boolean> {
  return locator.evaluate(
    (element) => element.scrollWidth > element.clientWidth + 0.5 || element.textContent.includes('…'),
  );
}

test('every sitemap’s name shows whole while the table has room for it', async ({ page }) => {
  await openSection(page, /^Sitemaps/);
  const tree = page.getByRole('treegrid', { name: 'Sitemaps' });
  const rows = tree.locator('[role="row"][aria-level]');
  await expect(rows.nth(1)).toBeVisible();
  const count = await rows.count();
  expect(count).toBeGreaterThanOrEqual(4);
  for (let index = 0; index < count; index += 1) {
    const name = rows.nth(index).locator('.si-url__shown');
    const full = (await rows.nth(index).locator('.si-url__full').textContent()) ?? '';
    await expect(name, `row ${index + 1}: ${full}`).toHaveText(new URL(full, 'http://x').pathname);
    expect(await cut(name), `row ${index + 1}: ${full}`).toBe(false);
  }
  // The counts' columns are as narrow as their names on two lines: none takes a third.
  const lineHeight = await tree.evaluate((element) => {
    const head = element.querySelector('[role="columnheader"]');
    return head ? Number.parseFloat(getComputedStyle(head).lineHeight) : 0;
  });
  for (const header of await tree.getByRole('columnheader').all()) {
    const lines = await header.evaluate((element) => {
      const range = document.createRange();
      range.selectNodeContents(element);
      return new Set([...range.getClientRects()].map((rect) => Math.round(rect.top))).size;
    });
    expect(lines, `${await header.textContent()} (line height ${lineHeight})`).toBeLessThanOrEqual(2);
  }
});

test('what became of each sitemap shows whole, beside its whole name, once the window has room past the names', async ({
  page,
}) => {
  // At 1280 px the names come first and a failure's reason is cut (its words a hover away); at 1440 both are whole.
  await page.setViewportSize({ width: 1440, height: 720 });
  await openSection(page, /^Sitemaps/);
  const rows = page.getByRole('treegrid', { name: 'Sitemaps' }).locator('[role="row"][aria-level]');
  await expect(rows.nth(1)).toBeVisible();
  const count = await rows.count();
  for (let index = 0; index < count; index += 1) {
    const full = (await rows.nth(index).locator('.si-url__full').textContent()) ?? '';
    expect(await cut(rows.nth(index).locator('.si-url__shown')), `row ${index + 1}: ${full}`).toBe(false);
    const status = rows.nth(index).getByRole('gridcell').nth(2);
    // The synthetic site plants an empty sitemap (sitemap-empty), which the list flags as such since the check audit.
    await expect(status).toHaveText(/^(Read|Couldn’t be read|Empty: lists no URLs$)/);
    expect(await cut(status), `row ${index + 1}: ${await status.textContent()}`).toBe(false);
  }
  await expect(page.getByRole('treegrid', { name: 'Sitemaps' })).toContainText('Couldn’t be read: 404 Not Found');
  await expect(page.getByRole('treegrid', { name: 'Sitemaps' })).toContainText('Empty: lists no URLs');
});

test.describe('Simple Home', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('a long site’s name shows whole while its row has room, beside a row offering Continue', async ({ page }) => {
    const seeded = await seededCrawl();
    const SITE = 'gardenfurnituresets.co.uk';
    // The library as gallery 6 had it: the seeded crawl as the garden site, stopped early, so its row offers Continue.
    await page.route(/\/api\/crawls(\?[^/]*)?$/, async (route) => {
      if (route.request().method() !== 'GET') return route.fallback();
      const response = await route.fetch();
      const library = (await response.json()) as CrawlListItem[];
      return route.fulfill({
        response,
        json: library.map((item) =>
          item.id === seeded.id
            ? // The sidecar keys the site by its start URL (the fix loop's `siteKey`): the app names a row by it.
              { ...item, site: SITE, siteKey: SITE, startUrl: `https://${SITE}/`, status: 'stopped' as const }
            : item,
        ),
      });
    });
    await page.goto('/');
    const sites = page.locator('#main').getByRole('list', { name: /^Your sites/ });
    const name = sites.getByRole('link', { name: SITE, exact: true });
    await expect(name).toBeVisible();
    await expect(sites.getByRole('button', { name: 'Continue' })).toBeVisible();
    expect(await cut(name)).toBe(false);
  });
});

/** The focus colour as the page draws it, in rgb. */
async function focusColour(page: Page): Promise<[number, number, number]> {
  return page.evaluate(() => {
    const probe = document.createElement('span');
    probe.style.color = 'var(--color-focus)';
    document.body.append(probe);
    const [r = 0, g = 0, b = 0] = (getComputedStyle(probe).color.match(/[\d.]+/g) ?? []).map(Number);
    probe.remove();
    return [r, g, b];
  });
}

/** The colours of the window's pixels at these points (CSS px, at a device scale of 1). */
async function pixels(page: Page, points: readonly [number, number][]): Promise<[number, number, number][]> {
  const shot = (await page.screenshot()).toString('base64');
  return page.evaluate(
    async ({ shot, points }) => {
      const image = new Image();
      image.src = `data:image/png;base64,${shot}`;
      await image.decode();
      const canvas = document.createElement('canvas');
      canvas.width = image.width;
      canvas.height = image.height;
      const context = canvas.getContext('2d')!;
      context.drawImage(image, 0, 0);
      return points.map(([x, y]) => {
        const [r = 0, g = 0, b = 0] = context.getImageData(Math.round(x), Math.round(y), 1, 1).data;
        return [r, g, b] as [number, number, number];
      });
    },
    { shot, points },
  );
}

const near = (a: [number, number, number], b: [number, number, number]) =>
  a.every((value, index) => Math.abs(value - b[index]!) <= 48);

// The gallery's crawl had 13 broken URLs, more rows than the pane and `--evidence-max-height` had room for in 720 px;
// the seeded crawl has 4, so the window is as much shorter: the same shape.
test('an issue’s affected URLs, a found-on list open, scroll with the pane, one scroll bar, under a whole focus ring', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1280, height: 460 });
  await openSection(page, /^Issues/);
  await showChecks(page);
  await page.getByRole('option', { name: /Internal URL returns 4xx/ }).click();
  const grid = page.getByRole('treegrid', { name: /Internal URL returns 4xx/ });
  await grid.focus();
  // Opened from the keyboard, as a user does: the group, then the page found on the most pages, its list open.
  const active = grid.locator('[role="row"]:has(> [data-active])');
  const rowIndex = async (row: Locator) => Number(await row.getAttribute('aria-rowindex'));
  await page.keyboard.press('ArrowRight');
  const most = grid.locator('[role="row"][aria-level="2"]').filter({ hasText: /Found on [2-9]\d* pages/ });
  await expect(most.first()).toBeVisible();
  const target = await rowIndex(most.first());
  while ((await rowIndex(active)) < target) await page.keyboard.press('ArrowDown');
  await page.keyboard.press('ArrowRight');
  await expect(active).toHaveAttribute('aria-expanded', 'true');
  await expect(grid.locator('[role="row"][aria-level="3"]').first()).toBeVisible();
  await expect(grid).toBeFocused();

  // One scroll container: the pane. Nothing inside it scrolls up and down on its own.
  const pane = page.locator('[data-check-detail]');
  const scrollers = await pane.evaluate((root) =>
    [root, ...root.querySelectorAll('*')]
      .filter((element) => {
        const overflow = getComputedStyle(element).overflowY;
        return (overflow === 'auto' || overflow === 'scroll') && element.scrollHeight > element.clientHeight + 1;
      })
      .map((element) => (element === root ? 'pane' : (element.getAttribute('role') ?? element.tagName))),
  );
  expect(scrollers).toEqual(['pane']);

  // The keys keep the cursor in view: the pane scrolls to the row it moves to, at the last row and back up to the first.
  const inView = async () => {
    const cell = (await grid.locator('[data-active]').boundingBox())!;
    const shown = (await pane.boundingBox())!;
    return cell.y >= shown.y - 0.5 && cell.y + cell.height <= shown.y + shown.height + 0.5;
  };
  const rowCount = await grid.locator('[role="row"][aria-level]').count();
  const room = await pane.evaluate((element) => element.clientHeight);
  expect(rowCount * 32, 'more rows than the pane has room for').toBeGreaterThan(room);
  for (let guard = 0; guard < 40; guard += 1) {
    const before = await rowIndex(active);
    await page.keyboard.press('ArrowDown');
    if ((await rowIndex(active)) === before) break;
  }
  expect(await inView(), 'the last row').toBe(true);
  for (let step = 0; step < rowCount; step += 1) await page.keyboard.press('ArrowUp');
  expect(await inView(), 'the first row').toBe(true);

  // A whole focus ring: each edge of the grid's frame is the focus colour along its length, the top edge with the pane
  // scrolled to it, the bottom with the frame's end scrolled into view (why it matters and how to fix it follow the
  // list since step 3b, so the pane's end is past it).
  const colour = await focusColour(page);
  const frame = grid.locator('xpath=..');
  const edgePoints = async (edge: 'top' | 'bottom' | 'left' | 'right') => {
    const box = (await frame.boundingBox())!;
    box.x = Math.floor(box.x);
    box.y = Math.floor(box.y);
    const shown = (await pane.boundingBox())!;
    const top = Math.max(box.y, shown.y);
    const bottom = Math.min(box.y + box.height, shown.y + shown.height);
    const at = [0.1, 0.3, 0.5, 0.7, 0.9];
    // The ring is a hairline just inside the frame's border: one or two pixels in from the frame's edge.
    const inward = [1, 2];
    if (edge === 'top' || edge === 'bottom') {
      const y = edge === 'top' ? box.y : box.y + box.height - 1;
      const sign = edge === 'top' ? 1 : -1;
      return at.map((f) => inward.map((d): [number, number] => [box.x + box.width * f, y + sign * d]));
    }
    const x = edge === 'left' ? box.x : box.x + box.width - 1;
    const sign = edge === 'left' ? 1 : -1;
    return at.map((f) => inward.map((d): [number, number] => [x + sign * d, top + (bottom - top) * f]));
  };
  /** For each point along the edge, whether the ring is there (at one of the pixels just inside the border). */
  const ringAt = async (edge: 'top' | 'bottom' | 'left' | 'right') => {
    const points = await edgePoints(edge);
    const found = await pixels(page, points.flat());
    return points.map((_, index) => inward(found, index).some((pixel) => near(pixel, colour)));
  };
  const inward = (found: [number, number, number][], index: number) => found.slice(index * 2, index * 2 + 2);
  await pane.evaluate((element) => (element.scrollTop = 0));
  await grid.evaluate((element) => element.scrollIntoView({ block: 'start' }));
  expect(await ringAt('top'), 'the top edge').toEqual([true, true, true, true, true]);
  expect(await ringAt('left'), 'the left edge').toEqual([true, true, true, true, true]);
  expect(await ringAt('right'), 'the right edge').toEqual([true, true, true, true, true]);
  await frame.evaluate((element) => element.scrollIntoView({ block: 'end' }));
  expect(await ringAt('bottom'), 'the bottom edge').toEqual([true, true, true, true, true]);
});
