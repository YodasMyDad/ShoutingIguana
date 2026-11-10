// Sitemaps against the real sidecar and its synthetic crawl, whose site plants a sitemap index (found through
// robots.txt, at an address that redirects), the sitemaps under it, and one of them missing: the list's counts land
// on exactly their URLs, one sitemap's grid filters and sorts and opens the inspector, a deep link to each survives a
// reload, the tree runs on the keyboard, and axe finds nothing in both themes and under forced colours.
import { expect, test, type Page } from '@playwright/test';
import { countOf, expectNoViolations, openSection, read, seededCrawl } from './support';

interface Sitemap {
  id: number;
  url: string;
  parentId: number | null;
  state: string;
  isIndex: boolean;
  urls: { listed: number; fetched: number; indexable: number; nonIndexable: number };
}

const main = (page: Page) => page.getByRole('main');
const tree = (page: Page) => page.getByRole('treegrid', { name: 'Sitemaps' });
/** A sitemap's row in the list, by the end of its address. */
const row = (page: Page, sitemap: Sitemap) =>
  tree(page)
    .getByRole('row')
    .filter({ hasText: new URL(sitemap.url).pathname });
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
  await expect(row(page, failed)).toContainText('Couldn’t be read: HTTP 404');
  // What it lists isn't known: dashes, never 0.
  await expect(row(page, failed).getByRole('link')).toHaveCount(0);
  await expect(row(page, failed).getByText('–')).toHaveCount(4);
  // The site's pages no sitemap lists, as a link to exactly them.
  await expect(main(page).getByText(/of the site’s [\d,]+ pages (aren’t|isn’t) in any sitemap\./)).toBeVisible();
});

test('every count in the list lands on exactly its URLs, and back returns to the list', async ({ page }) => {
  const { sitemaps } = await sitemapsOf();
  await openSitemaps(page);
  // The index, and the first two sitemaps that list something: each of their counts that opens anything.
  const walked = sitemaps.filter((sitemap) => sitemap.urls.listed > 0).slice(0, 3);
  expect(walked.length).toBeGreaterThan(0);
  let landings = 0;
  for (const sitemap of walked) {
    const links = row(page, sitemap).getByRole('link');
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
  expect(landings).toBeGreaterThanOrEqual(3);
});

test('the pages no sitemap lists land on exactly those pages in URLs', async ({ page }) => {
  await openSitemaps(page);
  const line = main(page).getByText(/in any sitemap\./);
  const link = line.getByRole('link');
  test.skip((await link.count()) === 0, 'Every page of this crawl is in a sitemap');
  const said = countOf(await link.innerText());
  await link.click();
  await expect(page).toHaveURL(/\/urls\?filter=/);
  await expect(shown(page)).toHaveText(said.toLocaleString('en-GB'));
  // The landing reads as a sentence: the last chip is the one that makes it "not in a sitemap".
  await expect(main(page).getByRole('group', { name: 'Filters' })).toContainText('In a sitemap is no');
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
  await grid.getByRole('row').nth(1).getByRole('gridcell').first().click();
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/inspect=\d+/);
  await expect(page.getByRole('complementary', { name: 'Inspector' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page).not.toHaveURL(/inspect=/);
  await expect(page).toHaveURL(new RegExp(`/sitemaps/${index.id}`));
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
  await expect(main(page).getByText('The crawl couldn’t read it: HTTP 404.')).toBeVisible();
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
