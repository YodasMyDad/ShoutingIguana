// Where each of a check's URLs is found, and opening any listed URL in the browser, against the real sidecar and its
// synthetic crawl (planted 404s linked from its pages, one named only by an hreflang, redirects): Advanced's Issues says
// how many pages link to a broken URL and the first, opens a row to list them (each to the inspector, all to Links); a
// page holding the bad links says what it links to; Simple's problem page lists the pages to edit, each with its links to
// a broken URL (it said "Linked from …" under each broken URL until the UX audit of 2026-11-19); and every grid's URL
// has an open-in-browser button and Shift+Enter, which open the full URL (the opener stubbed: no browser opens).
import { expect, test, type Page } from '@playwright/test';
import { deadEndFixList, LINK_PLACE, LINK_WORDS } from './fixList';
import { expectNoViolations, openSection, read, seededCrawl, showChecks } from './support';

interface FoundOn {
  urlId: number;
  links: number;
  pages: number;
  pagesExact: boolean;
  sources: { id: number; url: string }[];
  sitemaps: { total: number };
  start: boolean;
}

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
  const { found } = await brokenUrls();
  expect([...found.values()].some((url) => url.pages > 0)).toBe(true);
  await openSection(page, /^Issues/);
  await showChecks(page);
  await page.getByRole('option', { name: /Internal URL returns 4xx/ }).click();
  const groups = page.getByRole('treegrid', { name: /Internal URL returns 4xx/ });
  await groups.focus();
  await page.keyboard.press('ArrowRight');
  await expect(groups.getByRole('row', { expanded: true }).first()).toBeVisible();
  const line = groups.locator('.si-where-line').first();
  await expect(line).toHaveText(
    /^(Found on [\d,]+\+? pages? · .+|In a sitemap · .+|The start URL|Not linked from any page crawled)$/,
  );

  // The first URL linked from a page: Down onto it, Right opens it.
  const pageRows = groups.locator('[role="row"][aria-level="2"]');
  const count = await pageRows.count();
  let at = -1;
  for (let index = 0; index < count; index += 1) {
    if (((await pageRows.nth(index).textContent()) ?? '').includes('Found on')) {
      at = index;
      break;
    }
  }
  expect(at).toBeGreaterThanOrEqual(0);
  for (let step = 0; step <= at; step += 1) await page.keyboard.press('ArrowDown');
  await page.keyboard.press('ArrowRight');
  await expect(pageRows.nth(at)).toHaveAttribute('aria-expanded', 'true');
  const sources = groups.locator('[role="row"][aria-level="3"]');
  await expect(sources.first()).toContainText('Linked from');
  await expect(sources.last()).toContainText(/Show (all [\d,]+ links|the link) in Links/);
  await expectNoViolations(page);

  // Enter on the first source: the page linking to it, in the inspector.
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/inspect=\d+/);
  await page.keyboard.press('Escape');

  // Show all in Links: the links to that URL, every kind, a page's own left out.
  await sources.last().getByRole('link').click();
  await expect(page).toHaveURL(/\/links\?filter=/);
  await expect(page.getByRole('grid', { name: 'Links' })).toBeVisible();
  await expect(page.getByRole('group', { name: 'Filters' })).toContainText('Target');
});

test('a page holding broken links says which URL it links to', async ({ page }) => {
  await openSection(page, /^Issues/);
  await showChecks(page);
  await page.getByRole('option', { name: /Links to broken internal URLs/ }).click();
  const groups = page.getByRole('treegrid', { name: /Links to broken internal URLs/ });
  await groups.focus();
  await page.keyboard.press('ArrowRight');
  // The group names the broken URL with its own status: a 4xx, or a 3xx whose redirects end broken (the crawl's
  // planted /plants/redirect-broken). Each page under it says where its link to it is and what it says (evidence), or,
  // where the page's link isn't among those read, what it links to.
  await expect(groups.getByRole('row', { expanded: true }).first()).toContainText(
    /[345]\d\d|Blocked by robots\.txt|No response|Timed out/,
  );
  await expect(groups.locator('.si-where-line').first()).toHaveText(
    /^(in the (content|navigation|header|footer|sidebar|head)( · .+)?|“.+”( · .+)?|Links to .+ \(([345]\d\d|blocked by robots\.txt|no answer)\)( \+[\d,]+)?)$/,
  );
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
  await grid.locator('[role="row"][aria-rowindex="3"] [role="gridcell"]').first().click();
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
    const list = await deadEndFixList(crawl.id, 'internal-client-error');
    await page.goto(`/crawls/${crawl.id}/issues/internal-client-error`);
    // It pinned a region "Where it is". Changed in the UX audit of 2026-11-19 (S-02 to S-05, S-07): a problem that is
    // a link has "Where to fix it" in its place.
    const where = page.getByRole('region', { name: 'Where to fix it' });
    await expect(where).toBeVisible();
    await expect(page.getByRole('region', { name: 'Where it is' })).toHaveCount(0);
    const linked = [...found.values()].filter((url) => url.pages > 0);
    expect(linked.length).toBeGreaterThan(0);
    expect(list.pages.length).toBeGreaterThan(0);
    // It pinned "Linked from <page>" under a broken address. Changed in the UX audit of 2026-11-19 (S-02, S-03): the
    // card counts the links to change, then lists each page to edit once, in the order to edit them (the most links
    // first), with its links under it: the link's words, where on the page, where it leads and what's wrong there.
    await expect(where.getByText(list.summary, { exact: true })).toBeVisible({ timeout: 15_000 });
    const pages = where.getByRole('list', { name: 'Pages to edit' }).getByRole('listitem');
    await expect(pages).toHaveCount(list.pages.length);
    for (const [index, expected] of list.pages.entries()) {
      const item = pages.nth(index);
      const address = new URL(expected.url);
      const name = expected.title ? `${expected.title} ${expected.url}` : expected.url;
      await expect(item.getByRole('link', { name, exact: true })).toHaveAttribute(
        'href',
        new RegExp(`^${expected.url.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}(#:~:text=.+)?$`),
      );
      await expect(
        item.getByRole('button', {
          name: `Open page: ${expected.title ?? `${address.pathname}${address.search}`}`,
          exact: true,
        }),
      ).toBeVisible();
      await expect(
        item.getByText(expected.links === 1 ? '1 link to change' : `${expected.links} links to change`, {
          exact: true,
        }),
      ).toBeVisible();
      const links = item.locator('tbody tr');
      await expect(links).toHaveCount(expected.links);
      for (let row = 0; row < expected.links; row += 1) {
        const cells = links.nth(row).getByRole('cell');
        await expect(cells.nth(0)).toHaveText(LINK_WORDS);
        await expect(cells.nth(1)).toHaveText(LINK_PLACE);
        await expect(cells.nth(2).locator('.si-fix-leads__wrong')).toHaveText(/^[a-z’ ]+ \(4\d\d [A-Za-z ’]+\)$/);
      }
    }
    // Each broken address a few pages link to is where one of those rows leads, a link to it.
    for (const url of list.linked) {
      await expect(pages.getByRole('link', { name: url, exact: true }).first()).toBeVisible();
    }
    // It pinned "Listed in your sitemap" and "It’s the address the check started from" under an address. Changed in
    // the UX audit of 2026-11-19 (S-03): a link more than ten pages share, and an address no page links to, each have
    // a part of their own under the pages, and only when there is one.
    await expect(where.getByRole('heading', { name: /^Links (many|your) pages share$/ })).toHaveCount(
      list.shared.length > 0 ? 1 : 0,
    );
    await expect(
      where.getByRole('heading', {
        name: list.unlinkedInSitemap
          ? 'Not linked from any page, and still in your sitemap'
          : /^Not linked from any page$/,
      }),
    ).toHaveCount(list.unlinked.length > 0 ? 1 : 0);
    // Each address opens in the browser, as its full address.
    const first = rows[0]!;
    const open = where.getByRole('button', { name: `Open ${first.url} in your browser` }).first();
    await open.focus();
    await open.press('Enter');
    await expect.poll(() => opened(page)).toEqual([first.url]);
    await expectNoViolations(page);
  });
});
