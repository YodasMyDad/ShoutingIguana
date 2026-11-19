// The URL grid against the real sidecar: presets and the chooser, a sort of two keys from the headers, a facet that
// lands on its count, Ctrl+F, the row menu and where its own item leads, a saved view that outlives a reload, and axe
// in both themes. Screenshots of its views at 1280x720 in both themes and at 200% zoom go to test-results/urls-grid.
import { expect, test, type Page } from '@playwright/test';
import { expectNoViolations, read, seededCrawl } from './support';

interface Filter {
  column: string;
  op: string;
  value?: string | number | boolean;
  values?: (string | number)[];
}
interface Page_ {
  rows: Record<string, string | number | boolean | null>[];
  matching: number;
  total: number;
}

let crawl = '';
test.beforeAll(async () => {
  crawl = (await seededCrawl()).id;
});

const urls = (body: {
  filters?: Filter[];
  sort?: { column: string; desc: boolean }[];
  columns?: string[];
  limit?: number;
}) => read<Page_>(`/api/crawls/${crawl}/urls`, { filters: [], sort: [], limit: 20, ...body });
const main = (page: Page) => page.getByRole('main');
const grid = (page: Page) => page.getByRole('grid', { name: 'URLs' });
const number = (text: string | null) => Number((text ?? '').replace(/[^\d]/g, ''));
const shown = async (page: Page) => number(await main(page).locator('.si-result-count__n').innerText());
const expectShown = (page: Page, expected: number) =>
  expect.poll(() => shown(page), { timeout: 30_000 }).toBe(expected);
/** The full URLs of the rows on screen, in order: each cell's URL is its accessible name. */
const rowUrls = (page: Page) =>
  grid(page)
    .locator('[role="row"]:has([role="gridcell"]) .si-url__full')
    .evaluateAll((cells) => cells.map((cell) => cell.textContent));

async function openUrls(page: Page, search = '') {
  await page.goto(`/crawls/${crawl}/urls${search}`);
  await expect(grid(page).getByRole('row').nth(1)).toBeVisible();
}

test('shows the Overview preset, and each preset’s columns at a press, in the route', async ({ page }) => {
  await openUrls(page);
  const headers = () => grid(page).getByRole('columnheader').allInnerTexts();
  await expect(
    main(page).getByRole('group', { name: 'Column presets' }).getByRole('button', { name: 'Overview', exact: true }),
  ).toHaveAttribute('aria-pressed', 'true');
  expect((await headers()).length).toBeGreaterThan(9);
  for (const name of ['Indexability', 'Content', 'Performance', 'Links']) {
    await main(page).getByRole('group', { name: 'Column presets' }).getByRole('button', { name, exact: true }).click();
    await expect(
      main(page).getByRole('group', { name: 'Column presets' }).getByRole('button', { name, exact: true }),
    ).toHaveAttribute('aria-pressed', 'true');
    expect(new URL(page.url()).searchParams.get('cols')).not.toBeNull();
    await expect(grid(page).getByRole('row').nth(1)).toBeVisible();
  }
  await expect(grid(page).getByRole('columnheader', { name: /^Followed inlinks/ })).toBeVisible();
  // The page never scrolls sideways: the grid does.
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('sorts by two keys from the headers, in the order the sidecar gives them', async ({ page }) => {
  await openUrls(page);
  await grid(page)
    .getByRole('button', { name: /^Depth/ })
    .click();
  await expect(grid(page).getByRole('columnheader', { name: /^Depth/ })).toHaveAttribute('aria-sort', 'ascending');
  await grid(page)
    .getByRole('button', { name: /^Inlinks/ })
    .click({ modifiers: ['Shift'] });
  await grid(page)
    .getByRole('button', { name: /^Inlinks/ })
    .click({ modifiers: ['Shift'] });
  await expect(grid(page).getByRole('columnheader', { name: /^Inlinks/ })).toContainText('sorted second, descending');
  const expected = await urls({
    columns: ['url'],
    sort: [
      { column: 'depth', desc: false },
      { column: 'inlinks', desc: true },
    ],
    limit: 8,
  });
  await expect.poll(async () => (await rowUrls(page)).slice(0, 8)).toEqual(expected.rows.map((row) => row.url));
});

test('narrows to a facet’s value, landing on its count, which the sidecar counts too', async ({ page }) => {
  await openUrls(page);
  await main(page).getByRole('button', { name: 'Filter by status class', exact: true }).click();
  const item = page.getByRole('menu').getByRole('menuitem').first();
  await expect(item.locator('.si-menu-item__detail')).toBeVisible({ timeout: 30_000 });
  const said = number(await item.locator('.si-menu-item__detail [aria-hidden="true"]').innerText());
  await item.click();
  await expectShown(page, said);
  const filters = JSON.parse(new URL(page.url()).searchParams.get('filter') ?? '[]') as Filter[];
  expect((await urls({ filters, limit: 1 })).matching).toBe(said);
});

test('Ctrl+F puts the keyboard in the search, from the grid', async ({ page }) => {
  await openUrls(page);
  await grid(page).focus();
  await page.keyboard.press('Control+f');
  await expect(main(page).getByRole('searchbox', { name: 'Search URLs and titles' })).toBeFocused();
});

test('opens a row’s menu with Shift+F10, gives the keyboard back to the row, and shows its inlinks in Links', async ({
  page,
}) => {
  await openUrls(page);
  const first = (await urls({ columns: ['url'], limit: 1 })).rows[0]!;
  await grid(page).focus();
  await page.keyboard.press('Shift+F10');
  const menu = page.getByRole('menu');
  await expect(menu).toBeVisible();
  await expect(menu.getByRole('menuitem', { name: 'Copy the URL' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(menu).toBeHidden();
  await expect(grid(page)).toBeFocused();
  await grid(page).getByRole('row').nth(1).click({ button: 'right' });
  await page.getByRole('menu').getByRole('menuitem', { name: 'Show its inlinks in Links' }).click();
  await expect(page).toHaveURL(/\/links\?/);
  const links = await read<Page_>(`/api/crawls/${crawl}/links`, {
    filters: [
      { column: 'to.id', op: 'eq', value: first.id },
      { column: 'selfLink', op: 'is', value: false },
    ],
    sort: [],
    limit: 1,
  });
  await expectShown(page, links.matching);
});

test('opens a row’s menu with Shift+F10 at its row, when the grid had scrolled the row out of view', async ({
  page,
}) => {
  await openUrls(page);
  await grid(page).focus();
  await page.keyboard.press('ArrowDown');
  // The rows scroll on under the pointer; the cursor stays on its row, now out of view.
  await grid(page).hover();
  await page.mouse.wheel(0, 4_000);
  await page.waitForTimeout(200);
  await page.keyboard.press('Shift+F10');
  const menu = page.getByRole('menu');
  await expect(menu).toBeVisible();
  const box = await grid(page).boundingBox();
  const cell = await page.evaluate(() => {
    const element = document.querySelector('[role="grid"][aria-label="URLs"]');
    const id = element?.getAttribute('aria-activedescendant') ?? '';
    return document.getElementById(id)?.getBoundingClientRect().toJSON() as { bottom: number } | undefined;
  });
  const at = await menu.boundingBox();
  // The keys brought the row back into view, and the menu opened at it: under its cell, inside the grid.
  expect(box && at && cell).toBeTruthy();
  expect(at!.y).toBeGreaterThanOrEqual(box!.y);
  expect(at!.y).toBeLessThanOrEqual(box!.y + box!.height);
  expect(Math.abs(at!.y - cell!.bottom)).toBeLessThanOrEqual(8);
  await page.keyboard.press('Escape');
});

test('saves a view under a name, which outlives a reload and opens from the menu', async ({ page }) => {
  await openUrls(page, `?sort=${encodeURIComponent(JSON.stringify([{ column: 'depth', desc: true }]))}`);
  await main(page).getByRole('button', { name: 'Saved views' }).click();
  await page.getByRole('menuitem', { name: 'Save this view…' }).click();
  await page.getByRole('dialog').getByRole('textbox', { name: 'Name' }).fill('Deepest first');
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click();
  await expect(main(page).getByRole('button', { name: 'Deepest first' })).toBeVisible();
  await openUrls(page);
  await page.reload();
  await expect(grid(page).getByRole('row').nth(1)).toBeVisible();
  await main(page).getByRole('button', { name: 'Saved views' }).click();
  await page.getByRole('menuitemradio', { name: 'Deepest first' }).click();
  await expect(grid(page).getByRole('columnheader', { name: /^Depth/ })).toHaveAttribute('aria-sort', 'descending');
  // Deleted again, so the other specs start with none.
  await main(page).getByRole('button', { name: 'Deepest first' }).click();
  await page.getByRole('menuitem', { name: 'Delete “Deepest first”' }).click();
  await expect(main(page).getByRole('button', { name: 'Saved views' })).toBeVisible();
});

test('has no axe violations in either theme, with a menu open', async ({ page }) => {
  await openUrls(page);
  await expectNoViolations(page);
  await page.emulateMedia({ colorScheme: 'dark', forcedColors: 'none' });
  await expectNoViolations(page);
  await grid(page).focus();
  await page.keyboard.press('Shift+F10');
  await expect(page.getByRole('menu')).toBeVisible();
  await expectNoViolations(page);
});

const SHOTS = 'test-results/urls-grid';
const VIEWS: [name: string, search: string][] = [
  ['overview', ''],
  [
    'indexability',
    '?cols=' +
      encodeURIComponent(
        JSON.stringify([
          'url',
          'statusCode',
          'indexability',
          'canonicalUrl',
          'noindex',
          'nofollow',
          'inSitemap',
          'metaRobots',
          'xRobotsTag',
          'redirectTarget',
        ]),
      ),
  ],
  [
    'content',
    '?cols=' +
      encodeURIComponent(
        JSON.stringify([
          'url',
          'title',
          'titleLength',
          'titleWidth',
          'h1',
          'h1Count',
          'wordCount',
          'description',
          'descriptionLength',
          'descriptionWidth',
        ]),
      ),
  ],
  [
    'sorted-filtered',
    `?filter=${encodeURIComponent(
      JSON.stringify([
        { column: 'statusClass', op: 'in', values: ['2xx'] },
        { column: 'depth', op: 'between', min: 1, max: 3 },
      ]),
    )}&sort=${encodeURIComponent(
      JSON.stringify([
        { column: 'depth', desc: false },
        { column: 'inlinks', desc: true },
      ]),
    )}`,
  ],
  [
    'no-match',
    `?filter=${encodeURIComponent(JSON.stringify([{ column: 'title', op: 'contains', value: 'no page has this title' }]))}`,
  ],
];

test('screenshots: each view at 1280x720 in both themes and at 200% zoom', async ({ page }) => {
  test.setTimeout(3 * 60_000);
  for (const [name, search] of VIEWS) {
    for (const [size, scheme] of [
      [{ width: 1280, height: 720 }, 'light'],
      [{ width: 1280, height: 720 }, 'dark'],
      [{ width: 640, height: 360 }, 'light'],
    ] as const) {
      await page.setViewportSize(size);
      await page.emulateMedia({ colorScheme: scheme, forcedColors: 'none' });
      await page.goto(`/crawls/${crawl}/urls${search}`);
      await expect(main(page).locator('.si-result-count__n')).toBeVisible();
      await page.waitForFunction(() => document.getAnimations().length === 0);
      const tag = size.width === 640 ? 'zoom-200' : scheme;
      await page.screenshot({ path: `${SHOTS}/${name}-${tag}.png` });
    }
  }
  // The row menu, open.
  await page.setViewportSize({ width: 1280, height: 720 });
  await page.emulateMedia({ colorScheme: 'light', forcedColors: 'none' });
  await openUrls(page);
  await grid(page).getByRole('row').nth(3).click({ button: 'right' });
  await expect(page.getByRole('menu')).toBeVisible();
  await page.screenshot({ path: `${SHOTS}/row-menu-light.png` });
});
