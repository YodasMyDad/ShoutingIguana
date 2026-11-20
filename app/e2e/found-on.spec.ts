// Where each of a check's URLs is found, and opening any listed URL in the browser, against the real sidecar and its
// synthetic crawl (planted 404s linked from its pages, one named only by an hreflang, redirects): Advanced's Issues says
// how many pages link to a broken URL and the first, opens a row to list them (each to the inspector, all to Links); a
// page holding the bad links says what it links to; Simple's problem page says "Linked from …"; and every grid's URL
// has an open-in-browser button and Shift+Enter, which open the full URL (the opener stubbed: no browser opens).
import { expect, test, type Locator, type Page } from '@playwright/test';
import { urlAtText } from '../src/lib/textFragment';
import { expectNoViolations, openSection, read, seededCrawl, showChecks, showGroups } from './support';

interface FoundOn {
  urlId: number;
  links: number;
  pages: number;
  pagesExact: boolean;
  sources: SourceLink[];
  sitemaps: { total: number };
  start: boolean;
}

/** A link to a URL, as the sidecar says it: the page it is on, what kind of link it is, where, and what it says. */
interface SourceLink {
  id: number;
  url: string;
  kind: string | null;
  position: string | null;
  text: string | null;
}

/**
 * What the open button beside a linking page opens: the page at the link's words (a text fragment) only where the
 * page shows them, a hyperlink with text outside its head; the page's plain URL for any other link (a canonical or
 * hreflang link, an og:image, an image's alt text, a link with no words), which a browser could only mislead with.
 */
const openedAt = (link: SourceLink) =>
  link.kind === 'hyperlink' && link.position !== 'head' && (link.text ?? '').trim() !== ''
    ? urlAtText(link.url, link.text)
    : link.url;

/** Every window.open the app makes, kept on the page instead of opening a window: the plain browser's opener. */
async function stubOpener(page: Page) {
  await page.addInitScript(() => {
    const opened: string[] = [];
    (window as unknown as { __opened: string[] }).__opened = opened;
    window.open = (url?: string | URL) => {
      opened.push(String(url));
      return null;
    };
  });
}

const literal = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

const opened = (page: Page) => page.evaluate(() => (window as unknown as { __opened: string[] }).__opened);

/** The first broken URLs of the seeded crawl, with where each is found, as the sidecar says. */
async function brokenUrls() {
  const crawl = await seededCrawl();
  const rows = await read<{ rows: { id: number; url: string }[] }>(
    `/api/crawls/${crawl.id}/issues/internal-client-error/rows`,
    { columns: ['url'], limit: 25 },
  );
  const found = await read<{ urls: FoundOn[] }>(`/api/crawls/${crawl.id}/urls/found-on`, {
    urlIds: rows.rows.map((row) => row.id),
    limit: 5,
  });
  return { crawl, rows: rows.rows, found: new Map(found.urls.map((url) => [url.urlId, url])) };
}

test('a broken URL says where it is found, opens to list the pages linking to it, and each opens in the inspector', async ({
  page,
}) => {
  const { rows, found } = await brokenUrls();
  expect([...found.values()].some((url) => url.pages > 0)).toBe(true);
  // The seeded crawl's 404s are few enough for the list to open every row on arrival, and to draw them all.
  expect(rows.length).toBeLessThanOrEqual(12);
  await openSection(page, /^Issues/);
  await showChecks(page);
  await page.getByRole('option', { name: /Internal URL returns 4xx/ }).click();

  // It opens as a list, one row a broken URL, where it is found in columns: the page linking to it, the link's words
  // and where on that page the link is.
  const list = page.getByRole('treegrid', { name: 'Internal URL returns 4xx: affected URLs' });
  const headers = list.getByRole('columnheader');
  await expect(headers.first()).toBeVisible();
  const columns = (await headers.allTextContents()).map((header) => header.trim());
  expect(columns.slice(0, 5)).toEqual(['URL', 'Status', 'Linked from', 'Link text', 'Where on the page']);
  const urlRows = list.locator('[role="row"][aria-level="1"]');
  await expect(urlRows).toHaveCount(rows.length);
  await expect(list.locator('[data-where-found="pending"]')).toHaveCount(0);
  const cell = (row: Locator, column: string) => row.getByRole('gridcell').nth(columns.indexOf(column));
  const fullUrl = (within: Locator) => within.locator('.si-url__full').first();

  // Every row against the sidecar's own answer: its URL and status, then the first page linking to it (a link, with
  // its open button), or, with no page linking to it, what else names it.
  const opensAtLink: boolean[] = [];
  for (const [index, row] of rows.entries()) {
    const shown = urlRows.nth(index);
    const where = found.get(row.id)!;
    await expect(fullUrl(cell(shown, 'URL'))).toHaveText(row.url);
    await expect(cell(shown, 'Status')).toHaveText(/^4\d\d\b/);
    const other = cell(shown, 'Linked from').locator('[data-where]');
    if (where.sources.length > 0) {
      await expect(other).toHaveAttribute('data-where', 'source');
      await expect(fullUrl(other)).toHaveText(where.sources[0]!.url);
      const opensAt = openedAt(where.sources[0]!);
      opensAtLink.push(opensAt !== where.sources[0]!.url);
      await expect(other.locator('[data-open-url]')).toHaveAttribute('data-open-url', opensAt);
      await expect(cell(shown, 'Where on the page')).toHaveText(/\S/);
    } else {
      await expect(other).toHaveAttribute('data-where', where.sitemaps.total > 0 ? 'sitemap' : /^(start|nothing)$/);
    }
  }
  // Both sides of the rule are on the crawl: a link in a page's words, and one a page only has in its markup.
  expect(new Set(opensAtLink)).toEqual(new Set([true, false]));

  // A URL more than one page links to: its other pages are open under it on arrival (a list this short opens them
  // all), each a row of the second level. Its "Hide N" closes them to "+N more", and Right opens them again.
  const at = rows.findIndex((row) => (found.get(row.id)?.sources.length ?? 0) > 1);
  expect(at, 'a broken URL more than one page links to').toBeGreaterThanOrEqual(0);
  const many = found.get(rows[at]!.id)!;
  const more = many.sources.length - 1;
  const manyRow = urlRows.nth(at);
  const toggle = manyRow.locator('[data-tree-toggle]');
  await expect(manyRow).toHaveAttribute('aria-expanded', 'true');
  await expect(toggle).toHaveText(`Hide ${more}`);
  const others = list.locator('[role="row"][aria-level="2"]');
  const openOnArrival = await others.count();
  await toggle.click();
  await expect(manyRow).toHaveAttribute('aria-expanded', 'false');
  await expect(toggle).toHaveText(`+${more} more`);
  await expect(others).toHaveCount(openOnArrival - more);
  await page.keyboard.press('ArrowRight');
  await expect(manyRow).toHaveAttribute('aria-expanded', 'true');
  await expect(others).toHaveCount(openOnArrival);
  const second = list.locator(
    `[role="row"][aria-rowindex="${Number(await manyRow.getAttribute('aria-rowindex')) + 1}"]`,
  );
  await expect(second).toHaveAttribute('aria-level', '2');
  await expect(fullUrl(second.locator('[data-where="source"]'))).toHaveText(many.sources[1]!.url);
  await expectNoViolations(page);

  // Down onto that page and Enter: the page linking to it, in the inspector.
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(new RegExp(`inspect=${many.sources[1]!.id}(&|$)`));
  await page.keyboard.press('Escape');
  await expect(page.getByRole('complementary', { name: 'Inspector' })).toBeHidden();

  // The row's menu goes on to Links: the links to that URL, every kind, a page's own left out.
  await cell(manyRow, 'Status').click({ button: 'right' });
  await page.getByRole('menuitem', { name: 'Show where it’s found in Links' }).click();
  await expect(page).toHaveURL(/\/links\?filter=/);
  await expect(page.getByRole('grid', { name: 'Links' })).toBeVisible();
  await expect(page.getByRole('group', { name: 'Filters' })).toContainText('Target');
  await page.goBack();
  await expect(page).toHaveURL(/\/issues\/internal-client-error/);

  // Grouped by cause, a URL's row says the first page linking to it and opens to list them all, ending with the way
  // on to every link to it in Links.
  await showGroups(page);
  const groups = page.getByRole('treegrid', { name: 'Internal URL returns 4xx: affected URLs, grouped by cause' });
  await expect(groups.getByRole('row', { expanded: true }).first()).toBeVisible();
  const pageRow = groups
    .locator('[role="row"][aria-level="2"]')
    .filter({ has: page.locator('.si-url__full', { hasText: new RegExp(`^${literal(rows[at]!.url)}$`) }) });
  const whereRead = pageRow.locator('[data-where-found="read"]');
  await expect(whereRead).toContainText('Linked from');
  await expect(fullUrl(whereRead.locator('[data-where="source"]'))).toHaveText(many.sources[0]!.url);
  await expect(whereRead.locator('[data-tree-toggle]')).toHaveText(/^\+[\d,]+ more$/);
  await whereRead.locator('[data-tree-toggle]').click();
  await expect(pageRow).toHaveAttribute('aria-expanded', 'true');
  const sources = groups.locator('[role="row"][aria-level="3"]');
  await expect(sources.first()).toContainText('Linked from');
  await expect(sources.locator('[data-where="source"]')).toHaveCount(many.sources.length);
  await expect(sources.last()).toHaveText(
    many.links === 1 ? 'Show the link in Links' : `Show all ${many.links.toLocaleString('en-GB')} links in Links`,
  );
  await expectNoViolations(page);
  await sources.last().getByRole('link').click();
  await expect(page).toHaveURL(/\/links\?filter=/);
  await expect(page.getByRole('grid', { name: 'Links' })).toBeVisible();
  await expect(page.getByRole('group', { name: 'Filters' })).toContainText('Target');
});

test('a page holding broken links says which URL it links to', async ({ page }) => {
  await openSection(page, /^Issues/);
  await showChecks(page);
  await page.getByRole('option', { name: /Links to broken internal URLs/ }).click();
  await expect(page).toHaveURL(/\/issues\/broken-internal-links$/);
  const crawl = await seededCrawl();
  const flagged = await read<{ rows: { id: number; url: string }[] }>(
    `/api/crawls/${crawl.id}/issues/broken-internal-links/rows`,
    { columns: ['url'], limit: 25 },
  );
  const first = flagged.rows[0]!;
  const named = await read<{ urls: { urlId: number; total: number; targets: { id: number; url: string }[] }[] }>(
    `/api/crawls/${crawl.id}/issues/broken-internal-links/targets`,
    { urlIds: [first.id], limit: 50 },
  );
  const targets = named.urls[0]?.targets ?? [];
  expect(targets.length).toBeGreaterThan(0);
  const status = /[345]\d\d|Blocked by robots\.txt|No response|Timed out/;
  const place =
    /^(Content|Navigation|Header|Footer|Sidebar|Head|.+ in the (content|navigation|header|footer|sidebar|head))( · .+)?$/;

  // It opens as a list, one row a page that holds a bad link: the URL it links to with that URL's own status (a 4xx,
  // or a 3xx whose redirects end broken, the crawl's planted /plants/redirect-broken), what the link says and where
  // on the page it is, each in a column of its own.
  const list = page.getByRole('treegrid', { name: 'Links to broken internal URLs: affected pages' });
  const headers = list.getByRole('columnheader');
  await expect(headers.first()).toBeVisible();
  const columns = (await headers.allTextContents()).map((header) => header.trim());
  expect(columns[0]).toBe('Page');
  const whereColumns = ['Links to', 'Link text', 'Where on the page'];
  expect(columns.filter((column) => whereColumns.includes(column))).toEqual(whereColumns);
  const cell = (row: Locator, column: string) => row.getByRole('gridcell').nth(columns.indexOf(column));
  const pageRows = list.locator('[role="row"][aria-level="1"]');
  await expect(pageRows).toHaveCount(flagged.rows.length);
  await expect(list.locator('[data-where-found="pending"]')).toHaveCount(0);
  const firstRow = pageRows.first();
  await expect(cell(firstRow, 'Page').locator('.si-url__full').first()).toHaveText(first.url);
  const linksTo = cell(firstRow, 'Links to').locator('[data-where="target"]');
  await expect(linksTo).toHaveText(status);
  expect(targets.map((target) => target.url)).toContain(await linksTo.locator('.si-url__full').first().textContent());
  await expect(cell(firstRow, 'Link text')).toHaveText(/^(“.+”|No text|–)$/);
  await expect(cell(firstRow, 'Where on the page')).toHaveText(place);
  // Every page's row names a URL, each with its status; a page with more than one says how many more.
  await expect(pageRows.locator('[data-where="target"]')).toHaveCount(flagged.rows.length);
  for (const text of await pageRows.locator('[data-where="target"]').allTextContents()) expect(text).toMatch(status);

  // Grouped by linked URL, the group names the broken URL with its own status, and each page under it says what its
  // link to it says and where on the page the link is.
  await showGroups(page);
  const groups = page.getByRole('treegrid', {
    name: 'Links to broken internal URLs: affected pages, grouped by linked URL',
  });
  await groups.focus();
  if ((await groups.getByRole('row', { expanded: true }).count()) === 0) await page.keyboard.press('ArrowRight');
  await expect(groups.getByRole('row', { expanded: true }).first()).toContainText(status);
  const under = groups.locator('[role="row"][aria-level="2"] [data-where-found="read"]').first();
  await expect(under).toContainText(/“.+”|No text/);
  await expect(under).toContainText(/Content|Navigation|Header|Footer|Sidebar|Head|in the /);
  await expect(groups.locator('[data-where-found="pending"]')).toHaveCount(0);
});

test('every grid’s URL opens in the browser from its button and from Shift+Enter, as its full address', async ({
  page,
}) => {
  await stubOpener(page);
  const { crawl } = await brokenUrls();
  await openSection(page, /^URLs/);
  const grid = page.getByRole('grid', { name: 'URLs' });
  const first = grid.locator('[role="row"][aria-rowindex="2"]');
  await first.hover();
  const button = first.getByRole('button', { name: /^Open https?:\/\/.+ in your browser$/ });
  await expect(button).toBeVisible();
  // A button, named by the full URL, and no Tab stop: the grid is one.
  expect(await button.getAttribute('tabindex')).toBe('-1');
  const name = (await button.getAttribute('aria-label')) ?? '';
  const url = name.replace(/^Open /, '').replace(/ in your browser$/, '');
  expect(url).toMatch(
    new RegExp(`^${(crawl.startUrl ?? '').replace(/\/$/, '').replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}`),
  );
  await button.click();
  await expect.poll(() => opened(page)).toEqual([url]);

  // The keyboard: the row under the cursor, Shift+Enter; the row stays selected, the button shown on it.
  await grid.focus();
  await page.keyboard.press('Shift+Enter');
  await expect.poll(() => opened(page)).toHaveLength(2);
  expect((await opened(page))[1]).toMatch(/^https?:\/\//);
  await page.mouse.move(0, 0);
  await expectNoViolations(page);

  // In forced colours it still shows, in the link colour, on the selected row.
  await page.emulateMedia({ forcedColors: 'active' });
  // A cell that isn't the URL's: the URL's own text is a link that opens the inspector.
  await grid.locator('[role="row"][aria-rowindex="3"] [role="gridcell"]').nth(1).click();
  await page.mouse.move(0, 0);
  const selected = grid.locator('[role="row"][data-selected]').first().locator('.si-url__open').first();
  await expect(selected).toBeVisible();
  await expect(selected).toHaveCSS('opacity', '1');
  await expectNoViolations(page);
  await page.emulateMedia({ forcedColors: 'none' });

  // Links: each end has its own, and the menu opens either.
  await openSection(page, /^Links/);
  const links = page.getByRole('grid', { name: 'Links' });
  const linkRow = links.locator('[role="row"][aria-rowindex="2"]');
  await linkRow.hover();
  await expect(linkRow.getByRole('button', { name: /in your browser$/ })).toHaveCount(2);
});

test.describe('Simple', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('the problem page for broken addresses says which pages link to each', async ({ page }) => {
    await stubOpener(page);
    const { crawl, rows, found } = await brokenUrls();
    await page.goto(`/crawls/${crawl.id}/issues/internal-client-error`);
    const where = page.getByRole('region', { name: 'Where it is' });
    await expect(where).toBeVisible();
    const linked = [...found.values()].filter((url) => url.pages > 0);
    expect(linked.length).toBeGreaterThan(0);
    await expect(where.getByText(/^Linked from \S+/).first()).toBeVisible();
    await expect(where.locator('.si-found-on').first()).toHaveText(
      /^(Linked from .+|Listed in your sitemaps?|It’s the address the check started from)$/,
    );
    // Each address opens in the browser, as its full address.
    const first = rows[0]!;
    const open = where.getByRole('button', { name: `Open ${first.url} in your browser` }).first();
    await open.focus();
    await open.press('Enter');
    await expect.poll(() => opened(page)).toEqual([first.url]);
    await expectNoViolations(page);
  });
});
