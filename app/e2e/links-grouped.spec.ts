// Step 3b against the real sidecar, on the tests' synthetic crawl: Links grouped by target, the Images preset, and the
// inspector's Images. Every figure the screen shows is checked against the sidecar's own answer for the same view,
// and axe holds at zero violations in both themes. Nothing here waits for a time: every figure is polled for.
import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { mkdtempSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { readCsv } from './files';
import { colourTweensEnd } from './colourTweens';
import { chooseTab, siteRow } from './support';

const env = (name: string) => {
  const value = process.env[name];
  if (!value) throw new Error(`global-setup sets ${name}`);
  return value;
};

interface Filter {
  column: string;
  op: string;
  value?: string | number | boolean;
  values?: (string | number)[];
}
interface GroupsPage {
  total: number;
  links: number;
  pages: number;
  rows: { id: number; url: string; links: number; pages: number; text: string | null; likelyTemplate: boolean }[];
}
interface ImagesPage {
  total: number;
  links: number;
  rows: { id: number; url: string; text: string | null; target: { sizeBytes: number | null } }[];
}

async function api<T>(route: string, body?: unknown): Promise<T> {
  const response = await fetch(`${env('SI_E2E_SIDECAR')}${route}`, {
    method: body === undefined ? 'GET' : 'POST',
    headers: {
      Authorization: `Bearer ${env('SI_E2E_TOKEN')}`,
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  });
  if (!response.ok) throw new Error(`${route} answered ${response.status}: ${await response.text()}`);
  return (await response.json()) as T;
}

let crawl = '';
test.beforeAll(async () => {
  const library = await api<{ id: string; site: string | null; fileName: string }[]>('/api/crawls');
  crawl = library.find((candidate) => (candidate.site ?? candidate.fileName) === env('SI_E2E_SITE'))?.id ?? '';
  expect(crawl).not.toBe('');
});

const BROKEN: Filter[] = [{ column: 'to.statusClass', op: 'in', values: ['4xx', '5xx'] }];
const IMAGES: Filter[] = [{ column: 'kind', op: 'in', values: ['image'] }];
const groups = (filters: Filter[], more: Record<string, unknown> = {}) =>
  api<GroupsPage>(`/api/crawls/${crawl}/links/groups`, { filters, limit: 50, ...more });
const linksCount = async (filters: Filter[]) =>
  (await api<{ matching: number }>(`/api/crawls/${crawl}/links`, { filters, sort: [], limit: 1 })).matching;

const main = (page: Page) => page.getByRole('main');
const number = (text: string | null) => Number((text ?? '').replace(/[^\d]/g, ''));
const shown = async (page: Page) => number(await main(page).locator('.si-result-count__n').innerText());
const expectShown = (page: Page, expected: number) => expect.poll(() => shown(page)).toBe(expected);
const count = (value: number) => value.toLocaleString('en-GB');

async function openLinks(page: Page) {
  await page.goto('/');
  await page.getByRole('region', { name: 'Library' }).getByRole('link', { name: siteRow(), exact: true }).click();
  await page
    .getByRole('navigation', { name: 'Main' })
    .getByRole('link', { name: /^Links/ })
    .first()
    .click();
  await expect(main(page).getByRole('heading', { name: 'Links', level: 1 })).toBeVisible();
}

async function expectNoViolations(page: Page) {
  await colourTweensEnd(page);
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  expect(results.violations.map((violation) => `${violation.id}: ${violation.help}`)).toEqual([]);
}

test('groups the broken links by target: one row a target, the most linked from pages first, each opening to its links', async ({
  page,
}) => {
  await openLinks(page);
  const grouped = await groups(BROKEN);
  expect(grouped.total).toBeGreaterThan(0);
  await main(page).getByRole('button', { name: 'Broken links', exact: true }).click();
  await expectShown(page, await linksCount(BROKEN));
  await main(page).getByRole('button', { name: 'Group by target' }).click();
  await expect(page).toHaveURL(/by=target/);
  await expectShown(page, grouped.total);
  await expect(
    main(page).getByText(
      `· ${count(grouped.links)} link${grouped.links === 1 ? '' : 's'} on ${count(grouped.pages)} page${grouped.pages === 1 ? '' : 's'}`,
    ),
  ).toBeVisible();
  const grid = main(page).getByRole('treegrid', { name: 'Links grouped by target' });
  await expect(grid).toHaveAttribute('aria-rowcount', String(grouped.total + 1));
  await expect(grid.getByRole('columnheader', { name: /Linking pages/ })).toHaveAttribute('aria-sort', 'descending');

  // The rows in the sidecar's order, each with its pages and links.
  for (const [index, group] of grouped.rows.slice(0, 3).entries()) {
    const row = grid.getByRole('row').nth(index + 1);
    await expect(row.locator('.si-url__go').first()).toHaveAccessibleName(group.url);
    await expect(row.getByRole('gridcell').nth(2)).toHaveText(count(group.pages));
    await expect(row.getByRole('gridcell').nth(3)).toHaveText(count(group.links));
  }

  // The first target opens to its first links and the row that lists them all; that row lands on exactly its links.
  const first = grouped.rows[0]!;
  await grid.focus();
  await page.keyboard.press('ArrowRight');
  await expect(grid.getByRole('row').nth(1)).toHaveAttribute('aria-expanded', 'true');
  const more = grid.getByRole('row', {
    name:
      first.links === 1
        ? /Show the one link to this target/
        : new RegExp(`Show all ${count(first.links)} links to this target`),
  });
  await expect(more).toBeVisible();
  await more.getByRole('gridcell').first().click();
  await page.keyboard.press('Enter');
  await expect(page).not.toHaveURL(/by=target/);
  await expectShown(page, first.links);
  await expect(main(page).getByRole('grid', { name: 'Links' })).toHaveAttribute(
    'aria-rowcount',
    String(first.links + 1),
  );

  // Back to the grouped view; axe in both themes.
  await page.goBack();
  await expectShown(page, grouped.total);
  for (const colorScheme of ['light', 'dark'] as const) {
    await page.emulateMedia({ colorScheme });
    await expectNoViolations(page);
  }
});

test('the Images preset lists the image links the largest file first, and groups them one row an image', async ({
  page,
}) => {
  await openLinks(page);
  const images = await linksCount(IMAGES);
  expect(images).toBeGreaterThan(0);
  await main(page).getByRole('button', { name: 'Images', exact: true }).click();
  await expectShown(page, images);
  await expect(page).toHaveURL(/sort=/);
  const grid = main(page).getByRole('grid', { name: 'Links' });
  await expect(
    grid.getByRole('columnheader').evaluateAll((cells) => cells.map((cell) => cell.textContent.trim())),
  ).resolves.toEqual(expect.arrayContaining(['Image', 'Size', 'Pixel size', 'Alt text', 'Shown on']));
  await expect(grid.getByRole('columnheader', { name: /^Size/ })).toHaveAttribute('aria-sort', 'descending');
  // The first row is of the largest file the links lead to.
  const largest = await groups(IMAGES, { sort: [{ column: 'to.sizeBytes', desc: true }], limit: 1 });
  await expect(grid.getByRole('row').nth(1).locator('.si-url__go').first()).toHaveAccessibleName(largest.rows[0]!.url);

  // Grouped: one row an image, with the pages it's used on.
  const grouped = await groups(IMAGES, { sort: [{ column: 'to.sizeBytes', desc: true }] });
  await main(page).getByRole('button', { name: 'Group by target' }).click();
  await expectShown(page, grouped.total);
  const tree = main(page).getByRole('treegrid', { name: 'Images, one row an image' });
  await expect(tree.getByRole('columnheader', { name: /Used on/ })).toBeVisible();
  await expect(tree.getByRole('row').nth(1).locator('.si-url__go').first()).toHaveAccessibleName(grouped.rows[0]!.url);
  for (const colorScheme of ['light', 'dark'] as const) {
    await page.emulateMedia({ colorScheme });
    await expectNoViolations(page);
  }
});

test('the inspector lists a page’s images, each file once, the largest first, and opens them in Links', async ({
  page,
}) => {
  // A page that shows images: the page of the crawl's first image link.
  const link = await api<{ rows: { 'from.id': number }[] }>(`/api/crawls/${crawl}/links`, {
    columns: ['from.id'],
    filters: [...IMAGES, { column: 'selfLink', op: 'is', value: false }],
    sort: [],
    limit: 1,
  });
  const pageId = link.rows[0]!['from.id'];
  const images = await api<ImagesPage>(`/api/crawls/${crawl}/urls/images`, { urlId: pageId, limit: 500 });
  expect(images.total).toBeGreaterThan(0);
  await page.goto(`/crawls/${crawl}/urls?inspect=${pageId}`);
  const inspector = page.getByRole('complementary', { name: 'Inspector' });
  await expect(inspector).toBeVisible();
  await chooseTab(inspector, /^Images/);
  await expect(inspector.getByRole('tab', { name: /^Images/ })).toHaveText(new RegExp(`Images\\s*${images.total}$`));
  const grid = inspector.getByRole('grid', { name: 'Images' });
  await expect(grid).toHaveAttribute('aria-rowcount', String(images.rows.length + 1));
  await expect(grid.getByRole('row').nth(1).locator('.si-url__go')).toHaveAccessibleName(images.rows[0]!.url);
  // On a narrow sheet a long name is cut at the end of its stem: every extension shows whole, inside its cell.
  await page.setViewportSize({ width: 1024, height: 720 });
  const cut = await grid.locator('.si-url').evaluateAll((urls) =>
    urls.flatMap((url) => {
      const ext = url.querySelector('.si-url__ext');
      if (!ext) return [];
      const [box, end] = [url.getBoundingClientRect(), ext.getBoundingClientRect()];
      return end.width > 0 && end.right <= box.right + 0.5 && end.left >= box.left ? [] : [url.textContent];
    }),
  );
  expect(cut).toEqual([]);
  await page.setViewportSize({ width: 1280, height: 800 });
  for (const colorScheme of ['light', 'dark'] as const) {
    await page.emulateMedia({ colorScheme });
    await expectNoViolations(page);
  }
  // "All N in Links": the page's image links, as the Images preset shows them.
  await inspector.getByRole('link', { name: /^(All [\d,]+|Show it) in Links$/ }).click();
  await expectShown(page, images.links);
  await expect(
    main(page)
      .getByRole('grid', { name: 'Links' })
      .getByRole('columnheader', { name: /^Image/ }),
  ).toBeVisible();
});

test('exports the grouped view as it shows it: one row a target, in its order, with its counts', async ({ page }) => {
  const folder = mkdtempSync(path.join(os.tmpdir(), 'si-groups-export-'));
  try {
    await openLinks(page);
    await main(page).getByRole('button', { name: 'Broken links', exact: true }).click();
    await main(page).getByRole('button', { name: 'Group by target' }).click();
    const grouped = await groups(BROKEN, { limit: 200 });
    await expectShown(page, grouped.total);
    await page.keyboard.press('Control+e');
    const box = page.getByRole('dialog', { name: 'Export links by target' });
    await expect(box).toBeVisible();
    await expect(box).toContainText(`${count(grouped.total)} target`);
    await box.getByRole('radio', { name: 'CSV' }).check();
    const file = path.join(folder, 'broken by target.csv');
    await box.getByRole('textbox', { name: 'Save as' }).fill(file);
    await box.getByRole('button', { name: 'Export', exact: true }).click();
    await expect(box).toBeHidden();
    await expect(
      page
        .getByRole('region', { name: /Notifications/ })
        .locator('.si-export-toast')
        .filter({ hasText: path.basename(file) })
        .getByText('Saved', { exact: true }),
    ).toBeVisible({ timeout: 20_000 });
    const rows = readCsv(file);
    expect(rows[0]?.slice(0, 5)).toEqual(['Target', 'Status', 'Outcome', 'Linking pages', 'Links']);
    expect(rows.length - 1).toBe(grouped.total);
    for (const [index, group] of grouped.rows.slice(0, 5).entries()) {
      expect(rows[index + 1]?.[0]).toBe(group.url);
      expect(rows[index + 1]?.[3]).toBe(String(group.pages));
      expect(rows[index + 1]?.[4]).toBe(String(group.links));
    }
  } finally {
    rmSync(folder, { recursive: true, force: true });
  }
});

test('the inspector follows the cursor down the grouped view', async ({ page }) => {
  const grouped = await groups(BROKEN, { limit: 3 });
  test.skip(grouped.rows.length < 2, 'The crawl has one broken target.');
  await page.goto(
    `/crawls/${crawl}/links?filter=${encodeURIComponent(JSON.stringify(BROKEN))}&by=target&inspect=${grouped.rows[0]!.id}`,
  );
  const inspector = page.getByRole('complementary', { name: 'Inspector' });
  await expect(inspector.getByRole('heading', { level: 2 })).toContainText(new URL(grouped.rows[0]!.url).pathname);
  const grid = main(page).getByRole('treegrid', { name: 'Links grouped by target' });
  await grid.focus();
  await page.keyboard.press('ArrowDown');
  await expect(inspector.getByRole('heading', { level: 2 })).toContainText(new URL(grouped.rows[1]!.url).pathname);
  await expect(page).toHaveURL(new RegExp(`inspect=${grouped.rows[1]!.id}(&|$)`));
});
