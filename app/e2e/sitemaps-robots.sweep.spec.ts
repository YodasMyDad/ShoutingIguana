// The sweep: every count on Sitemaps and robots.txt against where it lands, on whatever crawl the tests run against.
// `npm run e2e` runs it on the synthetic crawl; SI_E2E_CRAWL=<file> runs it on a real one (the garden crawl, the 100k
// crawl). A count is read off the screen, its link is followed, and the list it opens must hold exactly that many. A
// count that's 0 has no link: the engine is asked for what its link would open, which must be nothing. What a line of
// robots.txt blocked, and the two cases no line accounts for, must add up to what the origin blocked.
import { expect, test, type Locator, type Page } from '@playwright/test';
import { ANSWERED_SITE_PAGES } from '../src/screens/overview/links';
import { COUNT_FILTERS, type SitemapCount } from '../src/screens/sitemaps/sitemapLinks';
import { countOf, crawlTestSite, read, removeCrawl, seededCrawl, type SeededCrawl } from './support';

// On a crawl named by SI_E2E_CRAWL the sweep is of that crawl. Otherwise the seeded crawl of 40 pages is too small to
// have a URL robots.txt blocked (every 61st product links to one), so the sweep crawls the synthetic test site itself.
let made: SeededCrawl | null = null;
test.beforeAll(async () => {
  if (!process.env.SI_E2E_CRAWL) made = await crawlTestSite();
});
test.afterAll(async () => {
  if (made) await removeCrawl(made.id);
});
const sweptCrawl = async () => made ?? (await seededCrawl());

/** The most links one sweep follows through the app; past that, the engine is asked what each would open. */
const MAX_LANDINGS = 160;
/** The most rows of a list the sweep walks. */
const MAX_ROWS = 1_200;

const main = (page: Page) => page.getByRole('main');
const shownIn = (scope: Locator) => scope.locator('.si-result-count__n').first();
const number = (value: number) => value.toLocaleString('en-GB');
/** What a sweep covered, in the run's output: how many counts it followed says what it's worth. */
const report = (line: string) => void process.stdout.write(`${line}\n`);

interface WalkedRow {
  cells: string[];
  /** Each link in the row: its text, its accessible name and where it leads. */
  links: { text: string; name: string; href: string }[];
}

/** Every row of a grid, read through the keyboard: the cursor's row is always drawn, however long the list. */
async function walk(page: Page, grid: Locator, rowCount: number): Promise<WalkedRow[]> {
  await grid.focus();
  await page.keyboard.press('Control+Home');
  const rows: WalkedRow[] = [];
  for (let index = 0; index < Math.min(rowCount, MAX_ROWS); index++) {
    if (index > 0) await page.keyboard.press('ArrowDown');
    rows.push(
      await grid.evaluate((element, expected) => {
        const active = document.getElementById(element.getAttribute('aria-activedescendant') ?? '');
        const row = active?.closest('[role="row"]');
        if (row?.getAttribute('aria-rowindex') !== String(expected + 2)) throw new Error(`row ${expected}`);
        return {
          cells: [...row.querySelectorAll('[role="gridcell"]')].map(
            (cell) => cell.querySelector('.si-url__full')?.textContent ?? cell.textContent,
          ),
          links: [...row.querySelectorAll('a')].map((link) => ({
            text: link.textContent,
            name: link.getAttribute('aria-label') ?? '',
            href: link.getAttribute('href') ?? '',
          })),
        };
      }, index),
    );
  }
  return rows;
}

interface Sitemap {
  id: number;
  url: string;
  parentId: number | null;
  state: string;
  entries: number;
  isIndex: boolean;
  /** `robotsDisallowed` and `redirectingOrBroken`: only from an engine that counts them for the list. */
  urls: {
    listed: number;
    fetched: number;
    indexable: number;
    nonIndexable: number;
    robotsDisallowed?: number;
    redirectingOrBroken?: number;
  };
}

const LIST_COUNTS: readonly SitemapCount[] = ['listed', 'crawled', 'blocked', 'notCrawled', 'nonIndexable'];

/**
 * What each count must be, from the API's own numbers and the screen's stated meaning (never the app's code):
 * "Crawled" is what the crawl requested, so `fetched` less the URLs robots.txt refused; "Blocked by robots.txt" is
 * those; "Not crawled" is what's listed and not `fetched`. Null where the engine doesn't count the refused URLs for
 * the list: the screen must then show a dash, never `fetched` under the name "Crawled".
 */
function expectedCount(sitemap: Sitemap, count: SitemapCount): number | null {
  const { listed, fetched, nonIndexable, indexable, robotsDisallowed } = sitemap.urls;
  switch (count) {
    case 'listed':
      return listed;
    case 'crawled':
      return robotsDisallowed === undefined ? null : fetched - robotsDisallowed;
    case 'blocked':
      return robotsDisallowed ?? null;
    case 'notCrawled':
      return listed - fetched;
    case 'indexable':
      return indexable;
    case 'nonIndexable':
      return nonIndexable;
  }
}

test('sweep: every count on Sitemaps lands on exactly its URLs', async ({ page }) => {
  test.setTimeout(600_000);
  const crawl = await sweptCrawl();
  const list = await read<{ discovery: string; sitemaps: Sitemap[] }>(`/api/crawls/${crawl.id}/sitemaps`);
  const matching = async (sitemapId: number, filters: readonly unknown[]) =>
    (
      await read<{ matching: number }>(`/api/crawls/${crawl.id}/sitemaps/urls`, {
        sitemapId,
        filters,
        sort: [],
        limit: 1,
      })
    ).matching;

  // Whether a sitemap's counts are known: its own, once it was read (or failed after an entry); an index's, which
  // are those of the sitemaps under it, only once the crawl has them all and has read each.
  const ownKnown = (sitemap: Sitemap) => sitemap.state === 'read' || sitemap.entries > 0 || sitemap.urls.listed > 0;
  const under = (id: number, seen = new Set<number>([id])): Sitemap[] =>
    list.sitemaps
      .filter((sitemap) => sitemap.parentId === id && !seen.has(sitemap.id))
      .flatMap((child) => {
        seen.add(child.id);
        return [child, ...under(child.id, seen)];
      });
  const known = (sitemap: Sitemap) => {
    if (!ownKnown(sitemap)) return false;
    const below = under(sitemap.id);
    if (!sitemap.isIndex && below.length === 0) return true;
    return list.discovery === 'done' && below.every((each) => each.state !== 'notRead' && ownKnown(each));
  };

  await page.goto(`/crawls/${crawl.id}/sitemaps`);
  await expect(page.getByRole('heading', { name: 'Sitemaps', level: 1 })).toBeVisible();
  let landings = 0;
  let asked = 0;
  let dashes = 0;
  if (list.sitemaps.length > 0) {
    const tree = page.getByRole('treegrid', { name: 'Sitemaps' });
    await expect(tree.getByRole('row').nth(1)).toBeVisible();
    const rows = await walk(page, tree, list.sitemaps.length);
    expect(rows.length).toBe(Math.min(list.sitemaps.length, MAX_ROWS));

    for (const row of rows) {
      const sitemap = list.sitemaps.find((candidate) => candidate.url === row.cells[0]?.trim());
      expect(sitemap, `the row for ${row.cells[0] ?? ''}`).toBeTruthy();
      if (!sitemap) continue;
      const said: Partial<Record<SitemapCount, number>> = {};
      for (const [at, count] of LIST_COUNTS.entries()) {
        const cell = (row.cells[3 + at] ?? '').trim();
        const where = `${sitemap.url}: ${count}`;
        const expected = known(sitemap) ? expectedCount(sitemap, count) : null;
        const link = row.links.find((candidate) => candidate.name.endsWith(` ${labelOf(count)}`));
        if (expected === null) {
          // What it lists isn't known, or the engine doesn't count it: a dash, never a 0, and nothing to follow.
          expect(cell, where).toBe('–');
          expect(link, where).toBeUndefined();
          dashes++;
          continue;
        }
        expect(countOf(cell), where).toBe(expected);
        said[count] = expected;
        if (expected === 0) {
          // Nothing to open, so no link: what its link would open must be nothing.
          expect(link, where).toBeUndefined();
          expect(await matching(sitemap.id, COUNT_FILTERS[count]), where).toBe(0);
          asked++;
          continue;
        }
        expect(link, where).toBeTruthy();
        if (!link) continue;
        expect(countOf(link.text), where).toBe(expected);
        if (landings < MAX_LANDINGS) {
          await page.goto(link.href);
          await expect(shownIn(main(page)), where).toHaveText(number(expected));
          landings++;
        } else {
          const filter = new URL(link.href, 'http://app').searchParams.get('filter');
          expect(await matching(sitemap.id, filter ? (JSON.parse(filter) as unknown[]) : []), where).toBe(expected);
          asked++;
        }
      }
      // Listed is what was crawled, what robots.txt blocked and what wasn't crawled, added up.
      if (said.crawled !== undefined && said.blocked !== undefined && said.notCrawled !== undefined) {
        expect(said.crawled + said.blocked + said.notCrawled, `${sitemap.url}: the three add up`).toBe(said.listed);
      }
    }
  }

  // One sitemap's quick filters: each count on a filter is what choosing it lists, and "All" is the three that make
  // it up. Where the list doesn't carry "Crawled" and "Blocked by robots.txt", the page counts them itself.
  for (const sitemap of list.sitemaps.filter((candidate) => candidate.urls.listed > 0).slice(0, 15)) {
    await page.goto(`/crawls/${crawl.id}/sitemaps/${sitemap.id}`);
    const show = main(page).getByRole('radiogroup', { name: 'Show' });
    await expect(shownIn(main(page))).toHaveText(number(sitemap.urls.listed));
    const saidOf: Record<string, number> = {};
    for (const name of ['All', 'Crawled', 'Blocked by robots.txt', 'Not crawled', 'Non-indexable']) {
      const option = show.getByRole('radio', { name: new RegExp(`^${name.replace('.', '\\.')}`) });
      // A count the page reads for itself comes a moment after the page.
      await expect(option.locator('.si-segment__count'), `${sitemap.url}: ${name}`).toBeVisible();
      const said = countOf(await option.locator('.si-segment__count').innerText());
      saidOf[name] = said;
      await option.click();
      await expect(option).toHaveAttribute('aria-checked', 'true');
      await expect(shownIn(main(page)), `${sitemap.url}: ${name}`).toHaveText(number(said));
      landings++;
    }
    expect(
      (saidOf.Crawled ?? 0) + (saidOf['Blocked by robots.txt'] ?? 0) + (saidOf['Not crawled'] ?? 0),
      `${sitemap.url}: crawled, blocked and not crawled add up to all`,
    ).toBe(saidOf.All);
    // What robots.txt blocked is exactly the URLs the engine stored as refused, and none of them is "crawled".
    expect(saidOf['Blocked by robots.txt'], `${sitemap.url}: blocked`).toBe(
      await matching(sitemap.id, [{ column: 'outcome', op: 'in', values: ['robotsDisallowed'] }]),
    );
    expect(saidOf.Crawled, `${sitemap.url}: crawled`).toBe(
      (await matching(sitemap.id, [{ column: 'fetched', op: 'is', value: true }])) -
        (saidOf['Blocked by robots.txt'] ?? 0),
    );
  }

  // The site's pages no sitemap lists: said once a sitemap was read. What the sentence must say is the engine's own
  // count, asked here, so the sweep waits for the sentence it knows is coming rather than pass for want of one.
  if (list.sitemaps.some((sitemap) => sitemap.state === 'read')) {
    const facets = await read<{
      matching: number;
      unavailable: string[];
      facets: { inSitemap?: { value: boolean | null; count: number }[] };
    }>(`/api/crawls/${crawl.id}/urls/facets`, { filters: ANSWERED_SITE_PAGES, facets: ['inSitemap'] });
    await page.goto(`/crawls/${crawl.id}/sitemaps`);
    const unplaced = facets.facets.inSitemap?.find((bucket) => bucket.value === null)?.count ?? 0;
    if (facets.unavailable.includes('inSitemap')) {
      await expect(main(page).getByText(/^How many of the site’s pages no sitemap lists is counted/)).toBeVisible();
    } else if (unplaced > 0) {
      // Pages the engine couldn't place (the analysis skipped the comparison): the line says so, and never counts
      // them as in a sitemap.
      await expect(main(page).getByText(/^The analysis didn’t count the site’s pages no sitemap lists/)).toBeVisible();
      await expect(main(page).getByText(/in (a|any) sitemap\./)).toHaveCount(0);
    } else if (facets.matching === 0) {
      await expect(main(page).getByText(/^The crawl read no pages on this site/)).toBeVisible();
    } else {
      // "N of the site's M pages": M is the crawl's pages, as the library counts them.
      expect(facets.matching).toBe(crawl.pageCount);
      const missing = facets.facets.inSitemap?.find((bucket) => bucket.value === false)?.count ?? 0;
      const sentence = main(page).getByText(/the site’s [\d,]+ pages? (is|are|isn’t|aren’t) in (a|any) sitemap\./);
      await expect(sentence).toBeVisible();
      // Each count in it is a link to exactly its URLs: the pages no sitemap lists (when there are some), and the
      // site's pages.
      const expected = missing > 0 ? [missing, facets.matching] : [facets.matching];
      await expect(sentence.getByRole('link')).toHaveCount(expected.length);
      for (const [at, count] of expected.entries()) {
        const link = sentence.getByRole('link').nth(at);
        expect(countOf(await link.innerText()), `the sentence's count ${at}`).toBe(count);
        await link.click();
        await expect(page).toHaveURL(/\/urls\?filter=/);
        await expect(shownIn(main(page)), `the sentence's link ${at}`).toHaveText(number(count));
        landings++;
        await page.goBack();
        await expect(sentence).toBeVisible();
      }
    }
  }
  report(
    `Sitemaps sweep: ${list.sitemaps.length} sitemaps, ${landings} landings followed, ${asked} asked of the engine, ${dashes} dashes`,
  );
});

function labelOf(count: SitemapCount): string {
  return {
    listed: 'listed',
    crawled: 'crawled',
    blocked: 'blocked by robots.txt',
    notCrawled: 'not crawled',
    indexable: 'indexable',
    nonIndexable: 'non-indexable',
  }[count];
}

interface Origin {
  origin: string;
  state: string | null;
  blocked: number;
}

interface RobotsFile extends Origin {
  lines: { number: number; text: string; blocked: number | null }[];
  unmatched: { unreachable: number; unknown: number };
}

test('sweep: every count on robots.txt lands on exactly its URLs, and the lines add up', async ({ page }) => {
  test.setTimeout(600_000);
  const crawl = await sweptCrawl();
  const first = await read<{ total: number; blocked: number; origins: Origin[] }>(`/api/crawls/${crawl.id}/robots`, {
    offset: 0,
    limit: 1_000,
  });
  const all = [...first.origins];
  while (all.length < first.total) {
    const page_ = await read<{ origins: Origin[] }>(`/api/crawls/${crawl.id}/robots`, {
      offset: all.length,
      limit: 1_000,
    });
    if (page_.origins.length === 0) break;
    all.push(...page_.origins);
  }
  // What the origins blocked adds up to what the crawl was kept from.
  expect(all.reduce((sum, origin) => sum + origin.blocked, 0)).toBe(first.blocked);

  await page.goto(`/crawls/${crawl.id}/robots`);
  await expect(page.getByRole('heading', { name: 'robots.txt', level: 1 })).toBeVisible();
  if (first.total === 0) return;
  await expect(
    main(page).getByText(new RegExp(`asked ${number(first.total)} origins? for robots\\.txt`)),
  ).toBeVisible();
  if (first.blocked > 0) {
    // What the crawl was kept from, in all: a link to exactly those URLs in the URL grid.
    const kept = main(page).getByRole('link', { name: new RegExp(`^${number(first.blocked)} URLs?$`) });
    await expect(kept).toBeVisible();
    await kept.click();
    await expect(page).toHaveURL(/\/urls\?filter=/);
    await expect(shownIn(main(page)), 'every URL robots.txt kept the crawl from').toHaveText(number(first.blocked));
    await page.goBack();
  }
  const grid = main(page).getByRole('grid', { name: 'Origins' });
  await expect(grid.getByRole('row').nth(1)).toBeVisible();
  const rows = await walk(page, grid, first.total);

  let landings = 0;
  let lines = 0;
  for (const [index, row] of rows.entries()) {
    const origin = all[index];
    expect(row.cells[0]?.replace('This site', '').trim(), `row ${index}`).toBe(origin?.origin);
    if (!origin) continue;
    const said = countOf(row.cells[2] ?? '');
    expect(said, origin.origin).toBe(origin.blocked);
    const at = `/crawls/${crawl.id}/robots/${encodeURIComponent(origin.origin)}`;
    if (said === 0) {
      // Nothing blocked, so no link.
      expect(row.links, origin.origin).toEqual([]);
      continue;
    }
    expect(countOf(row.links[0]?.text ?? ''), origin.origin).toBe(said);
    if (landings >= MAX_LANDINGS) continue;

    // The count's link opens the origin's file, whose list holds exactly that many.
    await page.goto(row.links[0]?.href ?? at);
    const list = main(page).getByRole('region', { name: 'Blocked URLs' });
    await expect(shownIn(list), origin.origin).toHaveText(number(said));
    landings++;

    // Its lines, and the two cases no line accounts for, add up to it; each lands on exactly its own.
    const file = await read<RobotsFile>(`/api/crawls/${crawl.id}/robots/file`, { origin: origin.origin });
    const byLines = file.lines.reduce((sum, line) => sum + (line.blocked ?? 0), 0);
    expect(byLines + file.unmatched.unreachable + file.unmatched.unknown, origin.origin).toBe(said);
    for (const line of file.lines.filter((each) => (each.blocked ?? 0) > 0).slice(0, 25)) {
      await page.goto(`${at}?line=${line.number}`);
      await expect(shownIn(list), `${origin.origin} line ${line.number}`).toHaveText(number(line.blocked ?? 0));
      await expect(main(page).locator('.cm-si-line-chosen'), `${origin.origin} line ${line.number}`).toContainText(
        line.text.trim() === '' ? /.*/ : line.text.trim(),
      );
      lines++;
    }
    await page.goto(at);
    for (const reason of ['unreachable', 'unknown'] as const) {
      if (file.unmatched[reason] === 0) continue;
      const link = list.getByRole('link', { name: new RegExp(`^${number(file.unmatched[reason])} URLs? `) });
      await link.click();
      await expect(page).toHaveURL(new RegExp(`reason=${reason}`));
      await expect(shownIn(list), `${origin.origin} ${reason}`).toHaveText(number(file.unmatched[reason]));
      lines++;
      await page.goto(at);
    }
  }
  report(`robots.txt sweep: ${first.total} origins, ${landings} origins opened, ${lines} lines and reasons followed`);
});
