// Round 5, part C, against the real sidecar's seeded crawl of the engine's test site: columns resized, moved and pinned
// (kept over a reload); the row menu at its point, every time; filtering by the value under it; Ctrl+F on the
// inspector's Source; the URL grid at 200% zoom with its toolbars folded; the search's placeholder fitting its field;
// hidden checks left out of the workbook, an Issues view's workbook of its checks, and the inspector's inlinks
// exported. Screenshots go to test-results/r5-grid (1280x720 in both themes, and 200%).
import { mkdtempSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { readCsv, readXlsx } from './files';
import { gridInView } from './gridView';
import { colourTweensEnd, expectNoViolations, read, seededCrawl } from './support';

interface UrlPage {
  rows: Record<string, string | number | boolean | null>[];
  matching: number;
}
interface Analysis {
  counts: { checkId: string; count: number }[];
  fixFirst?: string[];
}
interface Check {
  id: string;
  title: string;
  severity: string;
}

const SHOTS = 'test-results/r5-grid';
let crawl = '';
let folder = '';
test.beforeAll(async () => {
  crawl = (await seededCrawl()).id;
  folder = mkdtempSync(path.join(os.tmpdir(), 'si-e2e-r5-'));
});
test.afterAll(() => rmSync(folder, { recursive: true, force: true }));

const main = (page: Page) => page.getByRole('main');
const grid = (page: Page) => page.getByRole('grid', { name: 'URLs' });
const header = (page: Page, name: string) =>
  grid(page)
    .getByRole('columnheader')
    .filter({ hasText: new RegExp(`^${name}`) });
const toasts = (page: Page) => page.getByRole('region', { name: /Notifications/ });
const shown = async (page: Page) =>
  Number((await main(page).locator('.si-result-count__n').innerText()).replace(/\D/g, ''));

async function openUrls(page: Page, search = '') {
  await page.goto(`/crawls/${crawl}/urls${search}`);
  await expect(grid(page).getByRole('row').nth(1)).toBeVisible();
}

/** Ctrl+E on a screen, the choice and the path: the file's path once its toast says it's saved. */
async function exportChoice(page: Page, choice: RegExp, file: string) {
  await page.keyboard.press('Control+e');
  const box = page.getByRole('dialog', { name: /^Export/ });
  await expect(box).toBeVisible();
  await box.getByRole('radio', { name: choice }).check();
  await box.getByRole('textbox', { name: 'Save as' }).fill(file);
  await box.getByRole('button', { name: 'Export', exact: true }).click();
  await expect(box).toBeHidden();
  await expect(
    toasts(page)
      .locator('.si-export-toast')
      .filter({ hasText: path.basename(file) })
      .getByText('Saved', { exact: true }),
  ).toBeVisible({ timeout: 30_000 });
  return file;
}

test('resizes, moves and pins a column with the pointer, and keeps it over a reload (gap 10)', async ({ page }) => {
  await openUrls(page, `?cols=${encodeURIComponent(JSON.stringify(['url', 'statusCode', 'title', 'depth']))}`);
  const title = header(page, 'Title');
  const before = (await title.boundingBox())!;
  // The right edge, dragged 120 px wider.
  const edge = title.locator('.si-grid-resize');
  const edgeBox = (await edge.boundingBox())!;
  await page.mouse.move(edgeBox.x + edgeBox.width / 2, edgeBox.y + edgeBox.height / 2);
  await page.mouse.down();
  await page.mouse.move(edgeBox.x + 60, edgeBox.y + 10, { steps: 4 });
  await page.mouse.move(edgeBox.x + 120 + edgeBox.width / 2, edgeBox.y + 10, { steps: 4 });
  await page.mouse.up();
  await expect.poll(async () => Math.round((await title.boundingBox())!.width)).toBe(Math.round(before.width) + 120);

  // Depth dragged by its header to before Status: the route's columns follow, and the drag doesn't sort.
  const depth = (await header(page, 'Depth').boundingBox())!;
  const status = (await header(page, 'Status').boundingBox())!;
  await page.mouse.move(depth.x + 20, depth.y + depth.height / 2);
  await page.mouse.down();
  await page.mouse.move(depth.x - 40, depth.y + depth.height / 2, { steps: 5 });
  await page.mouse.move(status.x + 5, status.y + status.height / 2, { steps: 5 });
  await page.mouse.up();
  await expect
    .poll(() => JSON.parse(new URL(page.url()).searchParams.get('cols') ?? '[]') as unknown)
    .toEqual(['url', 'depth', 'statusCode', 'title']);
  expect(new URL(page.url()).searchParams.get('sort')).toBeNull();

  // Status pinned from its header's menu: it comes first and stays in view as the grid scrolls sideways.
  await header(page, 'Status').click({ button: 'right' });
  await page.getByRole('menuitem', { name: 'Pin the column to the left' }).click();
  await expect(grid(page).getByRole('columnheader').first()).toHaveAttribute('data-pinned', 'true');
  await page.setViewportSize({ width: 900, height: 720 });
  await grid(page).evaluate((element) => (element.scrollLeft = element.scrollWidth));
  // Its header sits at the grid's left edge, wherever the narrower window put the grid.
  await expect
    .poll(async () => {
      const frame = (await grid(page).boundingBox())!;
      return Math.abs((await header(page, 'Status').boundingBox())!.x - frame.x) <= 2;
    })
    .toBe(true);
  await page.screenshot({ path: `${SHOTS}/columns-laid-out-light.png` });

  await page.setViewportSize({ width: 1280, height: 720 });
  await page.reload();
  await expect(grid(page).getByRole('row').nth(1)).toBeVisible();
  await expect(grid(page).getByRole('columnheader').first()).toHaveAttribute('data-pinned', 'true');
  expect(Math.round((await header(page, 'Title').boundingBox())!.width)).toBe(Math.round(before.width) + 120);
  await expectNoViolations(page);
});

test('opens the row menu at the pointer every time, and at the row from the keys (G4)', async ({ page }) => {
  await openUrls(page);
  const cell = (row: number, col: number) => grid(page).locator(`[data-row="${row}"][data-col="${col}"]`);
  const menuBox = async () => (await page.getByRole('menu').boundingBox())!;
  const near = (menu: { x: number; y: number; height: number }, x: number, y: number) =>
    Math.abs(menu.x - x) <= 2 && (Math.abs(menu.y - y) <= 2 || Math.abs(menu.y + menu.height - y) <= 2);
  // Right-clicks one after another, the menu closed between some and open over others.
  for (const [row, col, close] of [
    [1, 0, true],
    [4, 2, false],
    [2, 1, false],
    [6, 0, true],
  ] as const) {
    const box = (await cell(row, col).boundingBox())!;
    const x = box.x + 12;
    const y = box.y + 8;
    await page.mouse.click(x, y, { button: 'right' });
    await expect(page.getByRole('menu')).toBeVisible();
    await expect
      .poll(async () => {
        const menu = await menuBox();
        return near(menu, x, y) ? 'at the pointer' : `menu at ${menu.x},${menu.y} (${menu.height} tall) for ${x},${y}`;
      })
      .toBe('at the pointer');
    if (close) {
      await page.keyboard.press('Escape');
      await expect(page.getByRole('menu')).toBeHidden();
    }
  }
  await page.keyboard.press('Escape');
  // From the keys: at the cursor's row.
  await grid(page).focus();
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Shift+F10');
  // The cursor's row (the right-clicks above moved the cursor), as drawn: its whole height, not its cell's text.
  const row = (await grid(page).locator('[role="row"]:has(> [data-active])').first().boundingBox())!;
  await expect
    .poll(async () => {
      const menu = await menuBox();
      return Math.abs(menu.y - (row.y + row.height)) <= 2
        ? 'at the row'
        : `menu at ${menu.y}, row ${row.y}+${row.height}`;
    })
    .toBe('at the row');
  await page.keyboard.press('Escape');
});

test('filters by the value under the row menu, landing on the sidecar’s count (gap 9)', async ({ page }) => {
  await openUrls(page);
  const status = grid(page).locator('[data-row="0"][data-col="1"]');
  await status.click({ button: 'right' });
  const item = page.getByRole('menuitem', { name: /^Filter by this value/ });
  await expect(item).toBeEnabled();
  await item.click();
  const filter = JSON.parse(new URL(page.url()).searchParams.get('filter') ?? '[]') as unknown[];
  expect(filter).toHaveLength(1);
  const expected = await read<UrlPage>(`/api/crawls/${crawl}/urls`, { filters: filter, sort: [], limit: 1 });
  await expect.poll(() => shown(page)).toBe(expected.matching);
});

test('searches the source on Ctrl+F from the Source tab and its panel (gap 8)', async ({ page }) => {
  await openUrls(page, '?inspect=1');
  const tab = page.getByRole('tab', { name: /^Source/ });
  await tab.click();
  const field = page.getByRole('searchbox', { name: 'Search the HTML source' });
  await expect(field).toBeVisible();
  await tab.focus();
  await page.keyboard.press('Control+f');
  await expect(field).toBeFocused();
  await page.keyboard.type('html');
  await page.getByRole('tabpanel').focus();
  await page.keyboard.press('Control+f');
  await expect(field).toBeFocused();
  // The URL grid's search was never touched.
  await expect(main(page).getByRole('searchbox', { name: 'Search URLs and titles' })).toHaveValue('');
});

test('says the URLs found whole beside the folded toolbars at 200% (B’s count, C’s fold)', async ({ page }) => {
  await page.setViewportSize({ width: 640, height: 360 });
  // Filtered, so the count is its longest: "N of M URLs found".
  const filter = [{ column: 'statusCode', op: 'eq', value: 200 }];
  await openUrls(page, `?filter=${encodeURIComponent(JSON.stringify(filter))}`);
  const toggle = main(page).getByRole('button', { name: /^Show the columns and filters/ });
  await expect(toggle).toBeVisible();
  const count = main(page).locator('.si-result-count');
  await expect(count).toHaveText(/^[\d,]+ of [\d,]+ URLs found$/);
  const fits = await count.evaluate((element) => {
    const box = element.getBoundingClientRect();
    const main = element.closest('main')!.getBoundingClientRect();
    return {
      whole: element.scrollWidth <= Math.ceil(box.width) && box.right <= main.right + 0.5 && box.left >= main.left,
      box: { left: box.left, right: box.right, top: box.top, bottom: box.bottom },
    };
  });
  expect(fits.whole, 'the count is whole and inside the screen').toBe(true);
  const toggleBox = (await toggle.boundingBox())!;
  const overlaps =
    fits.box.left < toggleBox.x + toggleBox.width &&
    toggleBox.x < fits.box.right &&
    fits.box.top < toggleBox.y + toggleBox.height &&
    toggleBox.y < fits.box.bottom;
  expect(overlaps, 'the count never lies under the fold’s button').toBe(false);
  await page.screenshot({ path: `${SHOTS}/urls-200-folded-filtered.png` });
});

test('keeps rows in view at 200% zoom, with and without the inspector (gap 13)', async ({ page }) => {
  await page.setViewportSize({ width: 640, height: 360 });
  await openUrls(page);
  const toggle = main(page).getByRole('button', { name: /^Show the columns and filters/ });
  await expect(toggle).toBeVisible();
  await expect.poll(async () => (await gridInView(grid(page))).rowsInView).toBeGreaterThanOrEqual(5);
  await page.screenshot({ path: `${SHOTS}/urls-200-folded.png` });
  await expectNoViolations(page);
  // The folded parts come back on a press.
  await toggle.click();
  await expect(main(page).getByRole('group', { name: 'Column presets' })).toBeVisible();
  await main(page).getByRole('button', { name: 'Hide the columns and filters' }).click();
  // Beside the inspector.
  await grid(page).locator('[data-row="1"][data-col="0"]').dblclick();
  await expect(page.getByRole('tab', { name: /^Summary/ })).toBeVisible();
  await expect.poll(async () => (await gridInView(grid(page))).rowsInView).toBeGreaterThanOrEqual(4);
  await page.screenshot({ path: `${SHOTS}/urls-200-inspector.png` });
});

test('fits the search’s placeholder in its field, and says Enter applies it (gap 23)', async ({ page }) => {
  await openUrls(page);
  const field = main(page).getByRole('searchbox', { name: 'Search URLs and titles' });
  const fit = await field.evaluate((input: HTMLInputElement) => {
    const style = getComputedStyle(input);
    const context = document.createElement('canvas').getContext('2d')!;
    context.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
    return {
      text: context.measureText(input.placeholder).width,
      room: input.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight),
    };
  });
  expect(fit.text).toBeLessThanOrEqual(fit.room);
  await field.fill('product');
  await expect(main(page).locator('.si-field__hint[title="Press Enter to search"]')).toBeVisible();
  await field.press('Enter');
  await expect(main(page).locator('.si-field__hint[title="Press Enter to search"]')).toBeHidden();
});

test('opens the Type facet on the first click after another facet’s menu (the audit’s Minor)', async ({ page }) => {
  await openUrls(page);
  await main(page).getByRole('button', { name: 'Filter by status', exact: true }).click();
  await expect(page.getByRole('menu')).toBeVisible();
  await main(page).getByRole('button', { name: 'Filter by type', exact: true }).click();
  await expect(page.getByRole('menu')).toContainText(/^Type/);
});

test('leaves a check hidden in this crawl out of the workbook, and exports an Issues view’s checks (gaps 19)', async ({
  page,
}) => {
  const analysis = await read<Analysis>(`/api/crawls/${crawl}/analysis`);
  const checks = (await read<{ checks: Check[] }>('/api/checks')).checks;
  const found = analysis.counts.filter((count) => count.count > 0).map((count) => count.checkId);
  const hidden = checks.find((check) => check.id === found[0])!;
  await page.goto(`/crawls/${crawl}/issues/${hidden.id}`);
  await page.getByRole('button', { name: 'Hide this check' }).click();
  await page.getByRole('menuitem', { name: /Hide in this crawl/ }).click();
  await expect(page.getByRole('button', { name: /Show 1 hidden check/ })).toBeVisible();

  const everything = await exportChoice(page, /Every check/, path.join(folder, 'everything.xlsx'));
  const sheets = readXlsx(everything);
  expect(sheets.length).toBe(found.length); // the summary, and every check that found something but the hidden one
  expect(sheets[0]?.rows.some((row) => row[0] === 'Hidden checks left out')).toBe(true);
  expect(sheets[0]?.rows.some((row) => row.includes(hidden.id))).toBe(false);

  // The list filtered to errors: its workbook holds the errors' sheets alone.
  const errors = found.filter(
    (id) => id !== hidden.id && checks.find((check) => check.id === id)?.severity === 'error',
  );
  await page.goto(`/crawls/${crawl}/issues?severity=${encodeURIComponent(JSON.stringify(['error']))}`);
  await expect(page.getByRole('listbox')).toBeVisible();
  const listed = await exportChoice(page, /The checks listed/, path.join(folder, 'errors.xlsx'));
  expect(readXlsx(listed).length).toBe(errors.length + 1);
});

test('exports the inspector’s inlinks as its list holds them (gap 22)', async ({ page }) => {
  await openUrls(page, '?inspect=1');
  await page.getByRole('tab', { name: /^Inlinks/ }).click();
  const figure = async () =>
    Number((await page.getByRole('tabpanel').locator('p.figures').first().innerText()).replace(/\D/g, ''));
  await expect.poll(figure).toBeGreaterThan(0);
  const count = await figure();
  await page.getByRole('button', { name: 'Export these inlinks…' }).click();
  const box = page.getByRole('dialog', { name: 'Export inlinks' });
  await box.getByRole('radio', { name: 'CSV' }).check();
  const file = path.join(folder, 'inlinks.csv');
  await box.getByRole('textbox', { name: 'Save as' }).fill(file);
  await box.getByRole('button', { name: 'Export', exact: true }).click();
  await expect(
    toasts(page).locator('.si-export-toast').filter({ hasText: 'inlinks.csv' }).getByText('Saved', { exact: true }),
  ).toBeVisible({ timeout: 30_000 });
  const [head = [], ...rows] = readCsv(file);
  expect(head[0]).toBe('Source URL');
  expect(rows).toHaveLength(count);
});

test('screenshots: laid-out columns and the footer at 1280x720 in both themes', async ({ page }) => {
  await openUrls(page);
  for (const theme of ['light', 'dark'] as const) {
    await page.emulateMedia({ colorScheme: theme });
    await colourTweensEnd(page);
    await page.screenshot({ path: `${SHOTS}/urls-${theme}.png` });
    await expectNoViolations(page);
  }
});
