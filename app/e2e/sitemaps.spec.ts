// Sitemaps against the real sidecar and its synthetic crawl, whose site plants a sitemap index (found through
// robots.txt, at an address that redirects), the sitemaps under it, and one of them missing: the list's counts land
// on exactly their URLs, one sitemap's grid filters and sorts and opens the inspector, a deep link to each survives a
// reload, the tree runs on the keyboard, and axe finds nothing in both themes and under forced colours.
import { expect, test, type Page } from '@playwright/test';
import { ANSWERED_SITE_PAGES } from '../src/screens/overview/links';
import { countOf, expectNoViolations, openSection, read, seededCrawl } from './support';

interface Sitemap {
  id: number;
  url: string;
  parentId: number | null;
  state: string;
  isIndex: boolean;
  /** `robotsDisallowed`: only from an engine that counts, for the list, the URLs robots.txt refused. */
  urls: { listed: number; fetched: number; indexable: number; nonIndexable: number; robotsDisallowed?: number };
}

const main = (page: Page) => page.getByRole('main');
const tree = (page: Page) => page.getByRole('treegrid', { name: 'Sitemaps' });
/**
 * A sitemap's row in the list, by the end of its address in its first cell: a sitemap an index lists names the index
 * in its Found via cell ("In /sitemap.xml"), so the row's whole text would find the index's sitemaps too.
 */
const row = (page: Page, sitemap: Sitemap) =>
  tree(page)
    .getByRole('row')
    .filter({ has: page.locator('[aria-colindex="1"]', { hasText: new URL(sitemap.url).pathname }) });
/** A row's counts that open something: "3 URLs listed", "1 URL blocked by robots.txt". Not the index it names. */
const countLinks = (page: Page, sitemap: Sitemap) => row(page, sitemap).getByRole('link', { name: /^[\d,]+ URLs? / });
/** The grid's own count: "264" of "264 of 4,521 URLs". */
const shown = (page: Page) => main(page).locator('.si-result-count__n');

async function sitemapsOf(): Promise<{ crawl: string; sitemaps: Sitemap[] }> {
  const crawl = await seededCrawl();
  const list = await read<{ sitemaps: Sitemap[] }>(`/api/crawls/${crawl.id}/sitemaps`);
  return { crawl: crawl.id, sitemaps: list.sitemaps };
}

async function openSitemaps(page: Page) {
  await openSection(page, /^Sitemaps/);
  await expect(page.getByRole('heading', { name: 'Sitemaps', level: 1 })).toBeVisible();
  await expect(tree(page).getByRole('row').nth(1)).toBeVisible();
}

test('the list: an index with its sitemaps under it, what became of each, and the answer in a sentence', async ({
  page,
}) => {
  const { sitemaps } = await sitemapsOf();
  const index = sitemaps.find((sitemap) => sitemap.isIndex);
  const failed = sitemaps.find((sitemap) => sitemap.state === 'failed');
  expect(index, 'the synthetic site plants a sitemap index').toBeTruthy();
  expect(failed, 'and a sitemap that answers 404').toBeTruthy();
  if (!index || !failed) return;

  await openSitemaps(page);
  await expect(
    main(page).getByText(/^The crawl found \d+ sitemaps: \d+ were read and 1 couldn’t be read\./),
  ).toBeVisible();
  // Every sitemap is a row: the index open, the others a level under it.
  await expect(tree(page).getByRole('row')).toHaveCount(sitemaps.length + 1);
  await expect(row(page, index)).toHaveAttribute('aria-expanded', 'true');
  await expect(row(page, index)).toContainText('robots.txt');
  await expect(row(page, failed)).toHaveAttribute('aria-level', '2');
  await expect(row(page, failed)).toContainText('Couldn’t be read: 404 Not Found');
  // What it lists isn't known: dashes, never 0, and no count to open. Its one link is the index that lists it, by
  // name ("In /sitemap.xml"): "Sitemap index" on every one of the index's sitemaps read as its type.
  await expect(countLinks(page, failed)).toHaveCount(0);
  await expect(row(page, failed).getByText('–')).toHaveCount(5);
  await expect(row(page, failed).getByRole('gridcell').nth(1)).toHaveText(`In ${new URL(index.url).pathname}`);
  await expect(row(page, failed).getByRole('link', { name: new URL(index.url).pathname })).toHaveAttribute(
    'href',
    new RegExp(`/sitemaps/${index.id}$`),
  );
  // Five counts: what was crawled, what robots.txt blocked and what wasn't crawled add up to what's listed.
  await expect(tree(page).getByRole('columnheader')).toHaveText([
    'Sitemap',
    'Found via',
    'Status',
    'Listed',
    'Crawled',
    'Blocked by robots.txt',
    'Not crawled',
    'Non-indexable',
  ]);
  // The index was read, and one of the sitemaps under it couldn't be: what it lists in all isn't known, so its own
  // row has dashes too, and says how many of its sitemaps the crawl read.
  const under = sitemaps.filter((sitemap) => sitemap.parentId === index.id);
  const read = under.filter((sitemap) => sitemap.state === 'read').length;
  await expect(row(page, index).getByText('–')).toHaveCount(5);
  await expect(row(page, index).getByRole('link')).toHaveCount(0);
  await expect(row(page, index)).toContainText(`Read, ${read} of its ${under.length} sitemaps`);
  // Its badge counts its sitemaps where the window leaves room for the count beside the address: a 1280 px window
  // does, the counts' columns being only as wide as their words and figures need (gallery 6); a 1024 px one doesn't.
  await expect(row(page, index).getByText(`Index of ${under.length}`, { exact: true })).toBeVisible();
  await page.setViewportSize({ width: 1024, height: 800 });
  await expect(row(page, index).getByText('Index', { exact: true })).toBeVisible();
  await page.setViewportSize({ width: 1280, height: 800 });
  await expect(row(page, index).getByText(`Index of ${under.length}`, { exact: true })).toBeVisible();
  // A sitemap that was read has all five as numbers: the engine counts, for the list, what robots.txt refused, so
  // "Crawled" is what the crawl requested (`fetched` less those) and never `fetched` under that name. The synthetic
  // site lists one URL its robots.txt disallows (/private/listed): that sitemap says 1 blocked.
  const whole = under.find((sitemap) => sitemap.state === 'read' && (sitemap.urls.robotsDisallowed ?? 0) > 0);
  expect(whole, 'the synthetic site has a sitemap that lists a URL robots.txt refuses').toBeTruthy();
  if (!whole) return;
  const refused = whole.urls.robotsDisallowed ?? 0;
  expect(refused).toBe(1);
  const cells = row(page, whole).getByRole('gridcell');
  await expect(cells.nth(3)).toHaveText(whole.urls.listed.toLocaleString('en-GB'));
  await expect(cells.nth(4)).toHaveText((whole.urls.fetched - refused).toLocaleString('en-GB'));
  await expect(cells.nth(5)).toHaveText('1');
  await expect(cells.nth(6)).toHaveText((whole.urls.listed - whole.urls.fetched).toLocaleString('en-GB'));
  await expect(row(page, whole).getByText('–')).toHaveCount(0);
  // The blocked count opens exactly that URL, which says what kept the crawl from it.
  await cells.nth(5).getByRole('link', { name: '1 URL blocked by robots.txt' }).click();
  await expect(shown(page)).toHaveText('1');
  const urls = main(page).getByRole('grid', { name: `URLs listed by ${whole.url}` });
  await expect(urls.getByRole('row')).toHaveCount(2);
  await expect(urls.getByRole('row').nth(1)).toContainText('/private/listed');
  await page.goBack();
  await expect(tree(page).getByRole('row').nth(1)).toBeVisible();
  // The site's indexable pages no sitemap lists (the not-in-sitemap check's), as a link to exactly them.
  await expect(
    main(page).getByText(/of the site’s [\d,]+ indexable pages? (aren’t|isn’t) in any sitemap\./),
  ).toBeVisible();
});

test('every count in the list lands on exactly its URLs, and back returns to the list', async ({ page }) => {
  const { sitemaps } = await sitemapsOf();
  await openSitemaps(page);
  // The first sitemaps that list something: each of their counts that opens anything. (The index has none of its
  // own to follow: one of its sitemaps couldn't be read.)
  const walked = sitemaps.filter((sitemap) => !sitemap.isIndex && sitemap.urls.listed > 0).slice(0, 3);
  expect(walked.length).toBeGreaterThan(0);
  let landings = 0;
  for (const sitemap of walked) {
    const links = countLinks(page, sitemap);
    for (let at = 0; at < (await links.count()); at++) {
      const link = links.nth(at);
      const said = countOf(await link.innerText());
      const name = await link.getAttribute('aria-label');
      await link.click();
      await expect(page, name ?? '').toHaveURL(new RegExp(`/sitemaps/${sitemap.id}(\\?|$)`));
      await expect(shown(page), `${sitemap.url}: ${name ?? ''}`).toHaveText(said.toLocaleString('en-GB'));
      landings++;
      await page.goBack();
      await expect(tree(page).getByRole('row').nth(1)).toBeVisible();
    }
  }
  expect(landings).toBeGreaterThanOrEqual(walked.length);
});

test('the pages no sitemap lists land on exactly those pages in URLs', async ({ page }) => {
  // The synthetic site's categories are in no sitemap: the engine says how many pages that is.
  const { crawl } = await sitemapsOf();
  const facets = await read<{ facets: { inSitemap?: { value: boolean | null; count: number }[] } }>(
    `/api/crawls/${crawl}/urls/facets`,
    // The indexable pages, as the line counts them (the not-in-sitemap check's).
    { filters: [...ANSWERED_SITE_PAGES, { column: 'indexable', op: 'is', value: true }], facets: ['inSitemap'] },
  );
  const missing = facets.facets.inSitemap?.find((bucket) => bucket.value === false)?.count ?? 0;
  expect(missing, 'the synthetic site has pages no sitemap lists').toBeGreaterThan(0);

  await openSitemaps(page);
  const line = main(page).getByText(/in any sitemap\./);
  // Two links: how many aren't listed, then the site's pages they're "of".
  await expect(line.getByRole('link')).toHaveCount(2);
  const link = line.getByRole('link').first();
  const said = countOf(await link.innerText());
  expect(said).toBe(missing);
  await link.click();
  await expect(page).toHaveURL(/\/urls\?filter=/);
  await expect(shown(page)).toHaveText(said.toLocaleString('en-GB'));
  // The landing reads as one sentence, the check's own: one chip, not the eight filters it used to take.
  const bar = main(page).getByRole('group', { name: 'Filters' });
  await expect(bar).toContainText('Indexable page not in a sitemap');
  await expect(bar.getByRole('button', { name: /^Remove the filter/ })).toHaveCount(1);
});

test('the tree is one Tab stop: the arrows move, Left and Right close and open an index, Enter opens', async ({
  page,
}) => {
  const { sitemaps } = await sitemapsOf();
  const index = sitemaps.find((sitemap) => sitemap.isIndex);
  if (!index) return;
  const under = sitemaps.filter((sitemap) => sitemap.parentId === index.id).length;
  await openSitemaps(page);
  await tree(page).focus();
  await expect(tree(page)).toHaveAttribute('aria-activedescendant', /.+/);
  // Its links are reached with the arrows, never Tab: Tab leaves the tree.
  await page.keyboard.press('Tab');
  await expect(tree(page)).not.toBeFocused();
  expect(await tree(page).evaluate((element) => element.contains(document.activeElement))).toBe(false);

  await tree(page).focus();
  await page.keyboard.press('ArrowLeft');
  await expect(row(page, index)).toHaveAttribute('aria-expanded', 'false');
  await expect(tree(page).getByRole('row')).toHaveCount(sitemaps.length + 1 - under);
  await page.keyboard.press('ArrowRight');
  await expect(tree(page).getByRole('row')).toHaveCount(sitemaps.length + 1);
  // Enter opens the index itself: it has URLs of its own to show, those of every sitemap under it.
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(new RegExp(`/sitemaps/${index.id}$`));
  await expect(shown(page)).toHaveText(index.urls.listed.toLocaleString('en-GB'));
});

test('one sitemap: its grid filters with a quick filter, sorts from a header and opens the inspector', async ({
  page,
}) => {
  const { crawl, sitemaps } = await sitemapsOf();
  const index = sitemaps.find((sitemap) => sitemap.isIndex);
  if (!index) return;
  await page.goto(`/crawls/${crawl}/sitemaps/${index.id}`);
  const grid = main(page).getByRole('grid');
  await expect(grid.getByRole('row').nth(1)).toBeVisible();
  await expect(shown(page)).toHaveText(index.urls.listed.toLocaleString('en-GB'));

  // The quick filter's count is the list's; choosing it lands on exactly that many.
  const show = main(page).getByRole('radiogroup', { name: 'Show' });
  const nonIndexable = show.getByRole('radio', { name: /Non-indexable/ });
  await expect(nonIndexable).toContainText(index.urls.nonIndexable.toLocaleString('en-GB'));
  await nonIndexable.click();
  await expect(page).toHaveURL(/filter=/);
  await expect(shown(page)).toHaveText(index.urls.nonIndexable.toLocaleString('en-GB'));
  await expect(main(page).getByText(`of ${index.urls.listed.toLocaleString('en-GB')} URLs`)).toBeVisible();
  // None of those is indexable.
  await expect(grid.getByRole('row').nth(1)).toBeVisible();
  await expect(grid.getByRole('gridcell', { name: 'Indexable', exact: true })).toHaveCount(0);

  // Crawled, blocked by robots.txt and not crawled are all of them, each a filter with its count; what's blocked is
  // exactly what the engine stored as refused by robots.txt, and none of it counts as crawled.
  const countOn = async (name: RegExp) => {
    const count = show.getByRole('radio', { name }).locator('.si-segment__count');
    await expect(count).toBeVisible();
    return countOf(await count.innerText());
  };
  await show.getByRole('radio', { name: /^All/ }).click();
  await expect(shown(page)).toHaveText(index.urls.listed.toLocaleString('en-GB'));
  const refused = await read<{ matching: number }>(`/api/crawls/${crawl}/sitemaps/urls`, {
    sitemapId: index.id,
    filters: [{ column: 'outcome', op: 'in', values: ['robotsDisallowed'] }],
    sort: [],
    limit: 1,
  });
  expect(await countOn(/^Blocked by robots\.txt/)).toBe(refused.matching);
  expect(await countOn(/^Crawled/)).toBe(index.urls.fetched - refused.matching);
  expect((await countOn(/^Crawled/)) + refused.matching + (await countOn(/^Not crawled/))).toBe(index.urls.listed);
  await show.getByRole('radio', { name: /^Crawled/ }).click();
  await expect(shown(page)).toHaveText((index.urls.fetched - refused.matching).toLocaleString('en-GB'));
  // A URL that answered says "Answered" in its Outcome: "Crawled" is the count's word, not an outcome's.
  await expect(grid.getByRole('row').nth(1)).toBeVisible();
  await expect(grid.getByRole('gridcell', { name: 'Crawled', exact: true })).toHaveCount(0);
  await expect(grid.getByRole('gridcell', { name: 'Answered', exact: true }).first()).toBeVisible();

  // The search keeps focus when Enter applies it, and Ctrl+F comes back to the same field.
  const search = main(page).getByRole('searchbox', { name: 'Search this sitemap’s URLs and titles' });
  await page.keyboard.press('Control+f');
  await expect(search).toBeFocused();
  await page.keyboard.type('product');
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/q=product/);
  await expect(search).toBeFocused();
  await expect(search).toHaveValue('product');
  await page.keyboard.press('Escape');
  await expect(page).not.toHaveURL(/q=product/);
  await show.getByRole('radio', { name: /Non-indexable/ }).click();
  await expect(shown(page)).toHaveText(index.urls.nonIndexable.toLocaleString('en-GB'));

  // Sort by status from its header: ascending, then descending, in the route.
  const status = grid.getByRole('columnheader', { name: /Status/ });
  await status.getByRole('button').click();
  await expect(page).toHaveURL(/sort=/);
  await expect(grid.getByRole('columnheader', { name: /Status/ })).toHaveAttribute('aria-sort', 'ascending');
  await grid
    .getByRole('columnheader', { name: /Status/ })
    .getByRole('button')
    .click();
  await expect(grid.getByRole('columnheader', { name: /Status/ })).toHaveAttribute('aria-sort', 'descending');
  await expect(shown(page)).toHaveText(index.urls.nonIndexable.toLocaleString('en-GB'));

  // Redirecting or broken: what answered with a redirect or an error, by the engine's own count of them.
  const broken = await read<{ matching: number }>(`/api/crawls/${crawl}/sitemaps/urls`, {
    sitemapId: index.id,
    filters: [
      { column: 'outcome', op: 'in', values: ['redirect', 'clientError', 'serverError', 'timeout', 'networkError'] },
    ],
    sort: [],
    limit: 1,
  });
  await show.getByRole('radio', { name: /Redirecting or broken/ }).click();
  await expect(shown(page)).toHaveText(broken.matching.toLocaleString('en-GB'));

  // Enter on a row opens the inspector, in place; Esc closes it to the grid as it was.
  await show.getByRole('radio', { name: /All/ }).click();
  await expect(shown(page)).toHaveText(index.urls.listed.toLocaleString('en-GB'));
  // (A cell that isn't the URL's: a click on the URL itself opens the inspector, below.)
  await grid.getByRole('row').nth(1).getByRole('gridcell').nth(1).click();
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/inspect=\d+/);
  await expect(page.getByRole('complementary', { name: 'Inspector' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page).not.toHaveURL(/inspect=/);
  await expect(page).toHaveURL(new RegExp(`/sitemaps/${index.id}`));

  // A URL is a link: one click on it opens the inspector on that URL, and Back closes it to the grid as it was.
  await grid.getByRole('row').nth(2).locator('.si-url__go').click();
  await expect(page).toHaveURL(/inspect=\d+/);
  await expect(page.getByRole('complementary', { name: 'Inspector' })).toBeVisible();
  await page.goBack();
  await expect(page).not.toHaveURL(/inspect=/);
  await expect(page).toHaveURL(new RegExp(`/sitemaps/${index.id}`));
});

test('a sitemap opens on one click on its name or anywhere on its row, and back returns to its row', async ({
  page,
}) => {
  const { sitemaps } = await sitemapsOf();
  const failed = sitemaps.find((sitemap) => sitemap.state === 'failed');
  const listing = sitemaps.find((sitemap) => !sitemap.isIndex && sitemap.urls.listed > 0);
  if (!failed || !listing) return;
  await openSitemaps(page);
  // The name is a link: one click, on a sitemap that couldn't be read too (it has no count to open).
  await row(page, failed).locator('.si-url__go').click();
  await expect(page).toHaveURL(new RegExp(`/sitemaps/${failed.id}$`));
  await expect(main(page).getByText('No URLs to show for this sitemap.')).toBeVisible();
  await page.goBack();
  // Back in the list, the row it was opened from is the cursor's, and marked.
  await expect(row(page, failed).locator('[data-active]')).toHaveCount(1);
  // A click anywhere else on a row opens it as well: here, on what became of it.
  await row(page, listing).getByRole('gridcell').nth(2).click();
  await expect(page).toHaveURL(new RegExp(`/sitemaps/${listing.id}$`));
  await expect(shown(page)).toHaveText(listing.urls.listed.toLocaleString('en-GB'));
  // The heading is the sitemap's whole address, never cut in the middle.
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(listing.url);
});

test('a link that names one of a sitemap’s URLs lands on its row, marked and in view, the inspector on it', async ({
  page,
}) => {
  const { crawl, sitemaps } = await sitemapsOf();
  const big = [...sitemaps].filter((sitemap) => !sitemap.isIndex).sort((a, b) => b.urls.listed - a.urls.listed)[0];
  if (!big || big.urls.listed < 60) return;
  // A URL far down the list, as the engine orders it: the inspector's "In a sitemap: Yes: extra.xml" links like this.
  const far = await read<{ rows: { id: number; url: string }[] }>(`/api/crawls/${crawl}/sitemaps/urls`, {
    sitemapId: big.id,
    columns: ['url'],
    filters: [],
    sort: [],
    offset: big.urls.listed - 5,
    limit: 1,
  });
  const target = far.rows[0];
  if (!target) throw new Error('The sitemap lists a URL near its end');
  await page.goto(`/crawls/${crawl}/sitemaps/${big.id}?inspect=${target.id}`);
  const grid = main(page).getByRole('grid', { name: `URLs listed by ${big.url}` });
  const landed = grid.getByRole('row').filter({ has: page.locator('[data-active]') });
  await expect(landed).toContainText(new URL(target.url).pathname);
  await expect(landed).toBeInViewport();
  await expect(page.getByRole('complementary', { name: 'Inspector' })).toBeVisible();
  await expect(page).toHaveURL(new RegExp(`inspect=${target.id}`));
});

test('a deep link to a sitemap, with its filter, survives a reload; a stale one says so and offers the list', async ({
  page,
}) => {
  const { crawl, sitemaps } = await sitemapsOf();
  const sitemap = sitemaps.find((candidate) => !candidate.isIndex && candidate.urls.nonIndexable > 0) ?? sitemaps[0];
  if (!sitemap) return;
  const filter = encodeURIComponent(JSON.stringify([{ column: 'indexable', op: 'is', value: false }]));
  await page.goto(`/crawls/${crawl}/sitemaps/${sitemap.id}?filter=${filter}`);
  await expect(shown(page)).toHaveText(sitemap.urls.nonIndexable.toLocaleString('en-GB'));
  await expect(page.getByRole('heading', { level: 1 })).toContainText(new URL(sitemap.url).pathname);
  await page.reload();
  await expect(shown(page)).toHaveText(sitemap.urls.nonIndexable.toLocaleString('en-GB'));
  await expect(main(page).getByRole('radio', { name: /Non-indexable/ })).toHaveAttribute('aria-checked', 'true');

  await page.goto(`/crawls/${crawl}/sitemaps/999999`);
  await expect(page.getByRole('heading', { name: 'This sitemap isn’t in this crawl' })).toBeVisible();
  await page.getByRole('button', { name: 'Show every sitemap' }).click();
  await expect(page).toHaveURL(/\/sitemaps$/);
  await expect(tree(page)).toBeVisible();
});

test('a sitemap that couldn’t be read says why, and a link that names a sitemap by its address lands on it', async ({
  page,
}) => {
  const { crawl, sitemaps } = await sitemapsOf();
  const failed = sitemaps.find((sitemap) => sitemap.state === 'failed');
  if (!failed) return;
  // As Overview's "A sitemap was left out" links: by the sitemap's address.
  await page.goto(`/crawls/${crawl}/sitemaps?url=${encodeURIComponent(failed.url)}`);
  await expect(page).toHaveURL(new RegExp(`/sitemaps/${failed.id}$`));
  await expect(main(page).getByText('No URLs to show for this sitemap.')).toBeVisible();
  await expect(main(page).getByText('The crawl couldn’t read it: 404 Not Found.')).toBeVisible();
  await main(page).getByRole('link', { name: 'All sitemaps' }).click();
  await expect(tree(page)).toBeVisible();
});

for (const colorScheme of ['light', 'dark'] as const) {
  test(`the list and one sitemap pass axe (${colorScheme})`, async ({ page }) => {
    await page.emulateMedia({ colorScheme });
    const { crawl, sitemaps } = await sitemapsOf();
    await openSitemaps(page);
    await expect(main(page).getByText(/in any sitemap\./)).toBeVisible();
    await expectNoViolations(page);
    await tree(page).focus();
    await expectNoViolations(page);
    const index = sitemaps.find((sitemap) => sitemap.isIndex) ?? sitemaps[0];
    if (!index) return;
    await page.goto(`/crawls/${crawl}/sitemaps/${index.id}`);
    await expect(main(page).getByRole('grid').getByRole('row').nth(1)).toBeVisible();
    await expectNoViolations(page);
  });
}

test('under forced colours, the list and one sitemap pass axe', async ({ page }) => {
  await page.emulateMedia({ forcedColors: 'active' });
  const { crawl, sitemaps } = await sitemapsOf();
  await openSitemaps(page);
  await expectNoViolations(page);
  const index = sitemaps.find((sitemap) => sitemap.isIndex) ?? sitemaps[0];
  if (!index) return;
  await page.goto(`/crawls/${crawl}/sitemaps/${index.id}`);
  await expect(main(page).getByRole('grid').getByRole('row').nth(1)).toBeVisible();
  await main(page)
    .getByRole('radio', { name: /Non-indexable/ })
    .click();
  await expect(page).toHaveURL(/filter=/);
  await expectNoViolations(page);
});
