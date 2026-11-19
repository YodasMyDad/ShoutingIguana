// Hand over (docs/v2-ui-ia.md, flow 5) against the real sidecar: a filtered, sorted URL view exported as CSV and as
// Excel and read back row for row against the grid's own read; a refused path, in the dialog; the report and the
// workbook saved from Overview; an export that outlives its screen and a reload, cancelled from its toast and leaving
// no file; the site's icon on Home. A browser has no save dialog, so the path is typed in the dialog's own field (the
// shell's dialog is the shell smoke walk's). Screenshots go to test-results/handover.
import { expect, test, type Page } from '@playwright/test';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { readCsv, readXlsx } from './files';
import { crawlTestSite, expectNoViolations, read, removeCrawl, seededCrawl } from './support';

interface UrlColumn {
  id: string;
  title?: string;
  unit?: string | null;
}
interface UrlPage {
  rows: { url: string }[];
  matching: number;
}

const SHOTS = 'test-results/handover';
let crawl = '';
let folder = '';
test.beforeAll(async () => {
  crawl = (await seededCrawl()).id;
  folder = mkdtempSync(path.join(os.tmpdir(), 'si-e2e-export-'));
});
test.afterAll(() => rmSync(folder, { recursive: true, force: true }));

const FILTERS = [{ column: 'statusClass', op: 'in', values: ['2xx'] }];
const SORT = [
  { column: 'depth', desc: true },
  { column: 'url', desc: false },
];
/** The view's columns, in the order shown: the file's are these, the status's outcome beside its code. */
const COLS = ['url', 'statusCode', 'title', 'depth', 'responseMs'];
const EXPORTED = ['url', 'statusCode', 'outcome', 'title', 'depth', 'responseMs'];
const VIEW = `?filter=${encodeURIComponent(JSON.stringify(FILTERS))}&sort=${encodeURIComponent(
  JSON.stringify(SORT),
)}&cols=${encodeURIComponent(JSON.stringify(COLS))}`;

const toasts = (page: Page) => page.getByRole('region', { name: /Notifications/ });
const dialog = (page: Page, name: string | RegExp = /^Export/) => page.getByRole('dialog', { name });

/** The grid's own read of the view, every row, in its order. */
async function gridUrls(): Promise<string[]> {
  const urls: string[] = [];
  for (let offset = 0; ; offset += 500) {
    const page = await read<UrlPage>(`/api/crawls/${crawl}/urls`, {
      columns: ['url'],
      filters: FILTERS,
      sort: SORT,
      offset,
      limit: 500,
    });
    urls.push(...page.rows.map((row) => row.url));
    if (urls.length >= page.matching || page.rows.length === 0) return urls;
  }
}

async function openView(page: Page) {
  await page.goto(`/crawls/${crawl}/urls${VIEW}`);
  await expect(page.getByRole('grid', { name: 'URLs' }).getByRole('row').nth(1)).toBeVisible();
}

/** Ctrl+E, the format, the path, Export: the dialog closes and the toast takes over. */
async function exportAs(page: Page, format: 'CSV' | 'Excel', file: string) {
  await page.keyboard.press('Control+e');
  const box = dialog(page, 'Export URLs');
  await expect(box).toBeVisible();
  await box.getByRole('radio', { name: format }).check();
  await box.getByRole('textbox', { name: 'Save as' }).fill(file);
  await box.getByRole('button', { name: 'Export', exact: true }).click();
  await expect(box).toBeHidden();
}

for (const format of ['CSV', 'Excel'] as const) {
  test(`exports a filtered, sorted URL view as ${format}, row for row the grid’s`, async ({ page }) => {
    const file = path.join(folder, `view.${format === 'CSV' ? 'csv' : 'xlsx'}`);
    await openView(page);
    await exportAs(page, format, file);
    const toast = toasts(page)
      .locator('.si-export-toast')
      .filter({ hasText: path.basename(file) });
    await expect(toast.getByText('Saved', { exact: true })).toBeVisible({ timeout: 20_000 });

    const rows = format === 'CSV' ? readCsv(file) : (readXlsx(file)[0]?.rows ?? []);
    const [header = [], ...data] = rows;
    const expected = await gridUrls();
    // Every row the grid has, in its order: the URL is the first column.
    expect(data.map((row) => row[0])).toEqual(expected);
    await expect(toast).toContainText(`${expected.length.toLocaleString('en-GB')} URLs`);
    // The header row is the engine's titles for the grid's columns, in the grid's order, a unit in brackets.
    const catalogue = await read<UrlColumn[]>('/api/url-columns');
    const titleOf = (id: string) => {
      const column = catalogue.find((candidate) => candidate.id === id);
      return column?.unit ? `${column.title} (${column.unit})` : column?.title;
    };
    expect(header).toEqual(EXPORTED.map(titleOf));
    // The next export's default name is in the folder this one went to.
    await page.keyboard.press('Control+e');
    await expect(dialog(page, 'Export URLs').getByRole('textbox', { name: 'Save as' })).toHaveValue(
      new RegExp(`^${folder.replace(/[\\.]/g, (char) => `\\${char}`)}`),
    );
    await page.keyboard.press('Escape');
  });
}

/** Ctrl+E on a screen, its dialog, a format and a path: the file once its toast says it's saved. */
async function exportFrom(page: Page, title: string, format: 'CSV' | 'Excel', file: string) {
  await page.keyboard.press('Control+e');
  const box = dialog(page, title);
  await expect(box).toBeVisible();
  await box.getByRole('radio', { name: format }).check();
  await box.getByRole('textbox', { name: 'Save as' }).fill(file);
  await box.getByRole('button', { name: 'Export', exact: true }).click();
  await expect(box).toBeHidden();
  await expect(
    toasts(page)
      .locator('.si-export-toast')
      .filter({ hasText: path.basename(file) })
      .getByText('Saved', { exact: true }),
  ).toBeVisible({ timeout: 20_000 });
  return format === 'CSV' ? readCsv(file) : (readXlsx(file)[0]?.rows ?? []);
}

test('exports the sitemaps and the robots.txt origins as their screens list them, row for row their reads', async ({
  page,
}) => {
  await page.goto(`/crawls/${crawl}/sitemaps`);
  await expect(page.getByRole('treegrid', { name: 'Sitemaps' }).getByRole('row').nth(1)).toBeVisible();
  const [sitemapHeader = [], ...sitemapRows] = await exportFrom(
    page,
    'Export sitemaps',
    'CSV',
    path.join(folder, 'sitemaps.csv'),
  );
  expect(sitemapHeader).toEqual([
    'Sitemap',
    'Index',
    'Listed by',
    'Found via',
    'Status',
    'Error',
    'Listed',
    'Crawled',
    'Blocked by robots.txt',
    'Not crawled',
    'Indexable',
    'Non-indexable',
    'Redirecting or broken',
  ]);
  const sitemaps = await read<{ sitemaps: { url: string }[] }>(`/api/crawls/${crawl}/sitemaps`);
  expect(sitemapRows.map((row) => row[0])).toEqual(sitemaps.sitemaps.map((sitemap) => sitemap.url));

  await page.goto(`/crawls/${crawl}/robots`);
  await expect(page.getByRole('grid', { name: 'Origins' }).getByRole('row').nth(1)).toBeVisible();
  const [originHeader = [], ...originRows] = await exportFrom(
    page,
    'Export robots.txt',
    'Excel',
    path.join(folder, 'origins.xlsx'),
  );
  expect(originHeader).toEqual([
    'Origin',
    'This site',
    'robots.txt',
    'Status',
    'Error',
    'Blocked URLs',
    'Read',
    'Size (bytes)',
  ]);
  const origins = await read<{ origins: { origin: string }[] }>(`/api/crawls/${crawl}/robots`, { limit: 1_000 });
  expect(originRows.map((row) => row[0])).toEqual(origins.origins.map((origin) => origin.origin));
});

test('exports a URL view narrowed to a links view’s targets, row for row the grid’s', async ({ page }) => {
  const links = { end: 'to' as const, filters: [{ column: 'kind', op: 'in', values: ['hyperlink'] }] };
  await page.goto(
    `/crawls/${crawl}/urls?links=${encodeURIComponent(JSON.stringify({ end: links.end, filter: links.filters }))}`,
  );
  await expect(page.getByRole('grid', { name: 'URLs' }).getByRole('row').nth(1)).toBeVisible();
  const [, ...rows] = await exportFrom(page, 'Export URLs', 'CSV', path.join(folder, 'targets.csv'));
  const expected: string[] = [];
  for (let offset = 0; ; offset += 500) {
    const read500 = await read<UrlPage>(`/api/crawls/${crawl}/urls`, {
      columns: ['url'],
      linkEnds: links,
      offset,
      limit: 500,
    });
    expected.push(...read500.rows.map((row) => row.url));
    if (expected.length >= read500.matching || read500.rows.length === 0) break;
  }
  const all = await read<UrlPage>(`/api/crawls/${crawl}/urls`, { columns: ['url'], limit: 1 });
  expect(expected.length).toBeGreaterThan(0);
  expect(expected.length).toBeLessThan(all.matching);
  expect(rows.map((row) => row[0])).toEqual(expected);
});

test('says a refused path in the dialog, in the app’s words, and exports nothing', async ({ page }) => {
  await openView(page);
  await page.keyboard.press('Control+e');
  const box = dialog(page, 'Export URLs');
  const field = box.getByRole('textbox', { name: 'Save as' });
  await field.fill('\\\\server\\share\\view.csv');
  await box.getByRole('button', { name: 'Export', exact: true }).click();
  await expect(box.getByText(/a network folder .* isn’t allowed/)).toBeVisible();
  await expect(field).toHaveAttribute('aria-invalid', 'true');

  await field.fill(path.join(folder, 'no-such-folder', 'view.xlsx'));
  await box.getByRole('button', { name: 'Export', exact: true }).click();
  await expect(
    box.getByText('That folder isn’t there, or this account can’t save files in it. Choose another folder.'),
  ).toBeVisible();
  await expect(box).toBeVisible();
  await expectNoViolations(page);
  expect(existsSync(path.join(folder, 'no-such-folder'))).toBe(false);
});

test('saves the report and the spreadsheet of everything from Overview', async ({ page }) => {
  await page.goto(`/crawls/${crawl}/overview`);
  await expect(page.getByRole('button', { name: 'Export' })).toBeVisible();
  await page.keyboard.press('Control+e');
  const box = dialog(page);
  await expect(box.getByRole('radio', { name: /The report/ })).toBeChecked();
  const report = path.join(folder, 'report.html');
  await box.getByRole('textbox', { name: 'Save as' }).fill(report);
  await box.getByRole('button', { name: 'Export', exact: true }).click();
  await expect(
    toasts(page).locator('.si-export-toast').filter({ hasText: 'report.html' }).getByText('Saved', { exact: true }),
  ).toBeVisible({
    timeout: 20_000,
  });
  const html = readFileSync(report, 'utf8');
  expect(html).toMatch(/^<!DOCTYPE html>/i);
  expect(html).toContain('Website check');

  // The palette lists the workbook, which opens the dialog on it.
  await page.keyboard.press('Control+k');
  await page.getByRole('combobox').fill('spreadsheet of everything');
  await page.keyboard.press('Enter');
  await expect(box.getByRole('radio', { name: /Spreadsheet of everything/ })).toBeChecked();
  const workbook = path.join(folder, 'everything.xlsx');
  await box.getByRole('textbox', { name: 'Save as' }).fill(workbook);
  await box.getByRole('button', { name: 'Export', exact: true }).click();
  await expect(
    toasts(page).locator('.si-export-toast').filter({ hasText: 'everything.xlsx' }).getByText('Saved', { exact: true }),
  ).toBeVisible({
    timeout: 30_000,
  });
  const sheets = readXlsx(workbook);
  expect(sheets[0]?.name).toBe('Summary');
  expect(sheets.length).toBeGreaterThan(1);
});

test('an export outlives its screen and a reload, and Cancel leaves no file; the site’s icon shows on Home', async ({
  page,
}) => {
  test.setTimeout(5 * 60_000);
  const big = await crawlTestSite();
  try {
    // The site's icon, as the crawl kept it, beside its name in the library.
    const item = await read<{ hasFavicon?: boolean }>(`/api/crawls/${big.id}`);
    expect(item.hasFavicon).toBe(true);
    await page.goto('/');
    const row = page.getByRole('row').filter({ has: page.locator(`a[href="/crawls/${big.id}"]`) });
    await expect(row.locator('.si-favicon img')).toBeVisible();
    expect(
      await row.locator('.si-favicon img').evaluate((image: HTMLImageElement) => image.naturalWidth),
    ).toBeGreaterThan(0);

    await page.goto(`/crawls/${big.id}/links`);
    await expect(page.getByRole('grid', { name: 'Links' }).getByRole('row').nth(1)).toBeVisible();
    await page.keyboard.press('Control+e');
    const box = dialog(page, 'Export links');
    await box.getByRole('radio', { name: 'Excel' }).check();
    const file = path.join(folder, 'links.xlsx');
    await box.getByRole('textbox', { name: 'Save as' }).fill(file);
    await box.getByRole('button', { name: 'Export', exact: true }).click();
    const toast = toasts(page).locator('.si-export-toast').filter({ hasText: 'links.xlsx' });
    await expect(toast.getByRole('button', { name: 'Cancel' })).toBeVisible();

    // Another screen: the sidebar keeps it within reach.
    await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Overview' }).click();
    const list = page.getByRole('region', { name: /^Exporting 1 file/ });
    await expect(list).toBeVisible();
    await page.screenshot({ path: `${SHOTS}/export-running-light.png` });

    // A reload: the export running in the engine is taken up from hello, and its toast comes back.
    await page.reload();
    await expect(toast.getByRole('button', { name: 'Cancel' })).toBeVisible();
    await toast.getByRole('button', { name: 'Cancel' }).click();
    await expect(toast.getByText('Cancelled', { exact: true })).toBeVisible({ timeout: 20_000 });
    await expect(list).toBeHidden();
    expect(existsSync(file)).toBe(false);
  } finally {
    await removeCrawl(big.id);
  }
});

test('screenshots: the dialog and a finished export at 1280x720 in both themes and at 200% zoom', async ({ page }) => {
  test.setTimeout(2 * 60_000);
  for (const [size, scheme] of [
    [{ width: 1280, height: 720 }, 'light'],
    [{ width: 1280, height: 720 }, 'dark'],
    [{ width: 640, height: 360 }, 'light'],
  ] as const) {
    const tag = size.width === 640 ? 'zoom-200' : scheme;
    await page.setViewportSize(size);
    await page.emulateMedia({ colorScheme: scheme, forcedColors: 'none' });
    await openView(page);
    await page.keyboard.press('Control+e');
    await expect(dialog(page, 'Export URLs')).toBeVisible();
    await page.waitForFunction(() => document.getAnimations().length === 0);
    await page.screenshot({ path: `${SHOTS}/dialog-${tag}.png` });
    await dialog(page, 'Export URLs')
      .getByRole('textbox', { name: 'Save as' })
      .fill(path.join(folder, `shot-${tag}.csv`));
    await dialog(page, 'Export URLs').getByRole('radio', { name: 'CSV' }).check();
    await dialog(page, 'Export URLs').getByRole('button', { name: 'Export', exact: true }).click();
    await expect(toasts(page).getByText('Saved', { exact: true }).first()).toBeVisible({ timeout: 20_000 });
    await page.waitForFunction(() => document.getAnimations().length === 0);
    await page.screenshot({ path: `${SHOTS}/saved-${tag}.png` });
    if (scheme === 'dark') await expectNoViolations(page);
  }
});

test('a check that doesn’t group exports its list in the grid’s sort (the review’s finding 2)', async ({ page }) => {
  // A check of the seeded crawl that lists its pages flat, with more than one page.
  const checks = await read<{ checks: { id: string; grouping: string }[] }>('/api/checks');
  const analysis = await read<{ counts: { checkId: string; count: number }[] }>(`/api/crawls/${crawl}/analysis`);
  const flat = checks.checks.find(
    (check) =>
      check.grouping === 'none' && (analysis.counts.find((found) => found.checkId === check.id)?.count ?? 0) > 1,
  );
  if (!flat) throw new Error('The seeded crawl has no flat check with more than one page');
  const sort = [{ column: 'url', desc: true }];
  await page.goto(`/crawls/${crawl}/issues/${flat.id}?sort=${encodeURIComponent(JSON.stringify(sort))}`);
  // Its list: a grid, or a treegrid where its rows open in place to show where each is found.
  const list = page.getByRole('main').locator('[data-check-detail]').locator('[role="grid"], [role="treegrid"]');
  await expect(list.getByRole('row').nth(2)).toBeVisible();
  await expect(list.getByRole('columnheader').first()).toHaveAttribute('aria-sort', 'descending');
  await page.keyboard.press('Control+e');
  const box = dialog(page, 'Export issues');
  await box.getByRole('radio', { name: /^This check/ }).check();
  await box.getByRole('radio', { name: 'CSV' }).check();
  const file = path.join(folder, 'flat-check.csv');
  await box.getByRole('textbox', { name: 'Save as' }).fill(file);
  await box.getByRole('button', { name: 'Export', exact: true }).click();
  await expect(
    toasts(page).locator('.si-export-toast').filter({ hasText: 'flat-check.csv' }).getByText('Saved', { exact: true }),
  ).toBeVisible({ timeout: 20_000 });
  const [, ...data] = readCsv(file);
  const rows: { url: string }[] = [];
  for (let offset = 0; ; offset += 500) {
    const page_ = await read<{ rows: { url: string }[] }>(`/api/crawls/${crawl}/issues/${flat.id}/rows`, {
      columns: ['url', 'detail'],
      filters: [],
      sort,
      offset,
      limit: 500,
    });
    rows.push(...page_.rows);
    if (page_.rows.length < 500) break;
  }
  expect(data.map((row) => row[0])).toEqual(rows.map((row) => row.url));
});

test('Ctrl+E opens Export from a grid’s search field (the review’s finding 3)', async ({ page }) => {
  await openView(page);
  const field = page.getByRole('main').getByRole('searchbox', { name: 'Search URLs and titles' });
  await field.fill('c');
  await field.focus();
  await page.keyboard.press('Control+e');
  await expect(dialog(page, 'Export URLs')).toBeVisible();
  await page.keyboard.press('Escape');
});
