// What a check's count is called, against the real sidecar and its synthetic crawl. For every check the catalog has,
// Issues says "pages affected, of the N pages analysed" only where the catalog says the check flags only pages (its
// `subject`), and "URLs affected, of the N URLs crawled" for any other; each N is the analysis's own total, stored
// with its counts (`pagesAnalyzed`, `crawledUrlCount`), and no check's count is more than it. Overview's "Fix first"
// and the inspector word the same counts the same way, and six-digit counts keep their room. Nothing here waits on a clock: every expectation is of what the screen says in the end.
import { expect, test, type Page } from '@playwright/test';
import { chooseTab, showChecks, showGroups, siteRow } from './support';

const env = (name: string) => {
  const value = process.env[name];
  if (!value) throw new Error(`global-setup sets ${name}`);
  return value;
};
const site = () => env('SI_E2E_SITE');

interface Check {
  id: string;
  title: string;
  grouping: 'detail' | 'relatedUrl' | 'none';
  subject?: 'page' | 'url';
  siteWide?: boolean;
  reportedOnce?: boolean;
}
interface Analysis {
  pagesAnalyzed: number;
  /** The URLs crawled when the analysis ran; null or missing for an analysis by an engine from before the score. */
  crawledUrlCount?: number | null;
  counts: { checkId: string; count: number; templateCauses?: number | null }[];
  skipped: { checkId: string }[];
}
interface Item {
  id: string;
  site: string | null;
  fileName: string;
  analyzedAt: string | null;
  pageCount: number;
  fetchedUrlCount: number;
}
type Filter = Record<string, unknown>;

/** The sidecar, from the test itself (no page, so no CORS): the API's own counts, to hold the screen's against. */
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

/** How many of the crawl's URLs these filters match, and how many it has in all, as the URL grid counts them. */
const urls = (crawl: string, filters: Filter[]) =>
  api<{ matching: number; total: number }>(`/api/crawls/${crawl}/urls`, { columns: ['url'], filters, limit: 1 });

const flaggedBy = (check: string): Filter => ({ column: 'check', op: 'in', values: [check] });

/**
 * A page, as the engine counts them and every count of pages in the app does (docs/v2-ui-architecture.md, "What the
 * breakdowns count"): a URL of the site that answered 2xx with HTML the crawl read, the crawler's own probes of the
 * site's other versions left out.
 */
const PAGES: Filter[] = [
  { column: 'isInternal', op: 'is', value: true },
  { column: 'type', op: 'in', values: ['page'] },
  { column: 'source', op: 'notIn', values: ['siteVersion'] },
  { column: 'statusClass', op: 'in', values: ['2xx'] },
  { column: 'wordCount', op: 'isNotEmpty' },
  // Cloudflare's endpoint is no page of the site's in any count (round 5).
  { column: 'url', op: 'notContains', value: '/cdn-cgi/' },
];

/** The words for what a check flags: pages only where the catalog says it flags nothing else. */
const wordsOf = (check: Check) =>
  check.subject === 'page'
    ? { one: 'page', many: 'pages', Many: 'Pages', of: 'analysed' }
    : { one: 'URL', many: 'URLs', Many: 'URLs', of: 'crawled' };

const count = (value: number) => value.toLocaleString('en-GB');
const counted = (value: number, words: { one: string; many: string }) =>
  `${count(value)} ${value === 1 ? words.one : words.many}`;
const literal = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/** The seeded crawl: the one the library lists for the tests' site, analysed. */
async function seededCrawl(): Promise<Item> {
  const crawls = await api<Item[]>('/api/crawls');
  const seeded = crawls.find((crawl) => (crawl.site ?? crawl.fileName) === site() && crawl.analyzedAt !== null);
  if (!seeded) throw new Error(`The sidecar lists no analysed crawl of ${site()}`);
  return seeded;
}

/** What the API says of the crawl: its catalog, the analysis's counts, and the two counts a check's is a part of. */
async function crawlFacts() {
  const item = await seededCrawl();
  const [catalog, analysis, all, pages, crawled] = await Promise.all([
    api<{ checks: Check[] }>('/api/checks'),
    api<Analysis>(`/api/crawls/${item.id}/analysis`),
    urls(item.id, []),
    urls(item.id, PAGES),
    urls(item.id, [{ column: 'fetched', op: 'is', value: true }]),
  ]);
  return {
    item,
    checks: catalog.checks,
    counts: new Map(analysis.counts.map((entry) => [entry.checkId, entry.count])),
    /** The causes of each check the engine puts in the site's template: such a check opens grouped. */
    templateCauses: new Map(analysis.counts.map((entry) => [entry.checkId, entry.templateCauses ?? 0])),
    skipped: new Set(analysis.skipped.map((entry) => entry.checkId)),
    /** The pages the analysis ran on: what a page check's count is a part of. */
    pagesAnalyzed: analysis.pagesAnalyzed,
    /** The pages crawled: the library's page count, of the file as it is. */
    pagesCrawled: item.pageCount,
    /** The same, counted by the URL grid: the two must agree for "pages" to be one number. */
    gridPages: pages.matching,
    /** The URLs crawled when the analysis ran: what a URL check's count is a part of. */
    urlsAnalysed: analysis.crawledUrlCount ?? null,
    /** Every URL the crawl found, crawled or not. */
    urlsFound: all.total,
    /** The URLs crawled, as the URL grid counts them now: the crawl hasn't changed since its analysis. */
    urlsCrawled: crawled.matching,
  };
}

const detailOf = (page: Page) => page.locator('[data-check-detail]');

/**
 * The check's counts, one line of its head: how many it flags (the way to them in URLs), of how much, then how that
 * moved and its causes where there are some. Its parts are joined by "·", with no spaces in its text.
 */
const countsOf = (page: Page) => detailOf(page).locator('header [data-counts]');

/** From Home to the crawl's Issues, through the sidebar, as a user goes. */
async function openIssues(page: Page) {
  await page.goto('/');
  await page.getByRole('region', { name: 'Library' }).getByRole('link', { name: siteRow(), exact: true }).click();
  await page
    .getByRole('navigation', { name: 'Main' })
    .getByRole('link', { name: /^Issues/ })
    .first()
    .click();
  await expect(page.getByRole('heading', { name: 'Issues', level: 1 })).toBeVisible();
  await expect(page).toHaveURL(/\/crawls\/[0-9a-f]{16}\/issues\/[a-z0-9-]+/);
  await showChecks(page);
}

/**
 * A share as the screen writes it ("13%", "1.3%", "<0.1%", "100%") against the count and what it's a share of: the
 * whole only when it is all of them, and otherwise the true share to the figures shown.
 */
function expectShare(shown: string, part: number, whole: number, of: string) {
  const share = (part / whole) * 100;
  if (shown === '100%') expect(part, `${of}: 100% is all of them`).toBe(whole);
  else if (shown === '<0.1%') expect(share, of).toBeLessThan(0.1);
  else if (shown === '>99.9%') expect(share, of).toBeGreaterThan(99.9);
  else {
    expect(part, `${of}: a part isn’t written as the whole`).toBeLessThan(whole);
    const tolerance = shown.includes('.') ? 0.05 : 0.5;
    expect(Math.abs(Number.parseFloat(shown) - share), `${of}: ${shown} of ${part} in ${whole}`).toBeLessThanOrEqual(
      tolerance + 1e-9,
    );
  }
}

test('every check’s count is called what the check flags, as a share of the API’s own count of them', async ({
  page,
}) => {
  test.setTimeout(180_000);
  const facts = await crawlFacts();
  const { item, checks, counts, skipped, templateCauses } = facts;

  // The catalog says what every check flags, and "pages" is one number wherever it's read.
  expect(checks.length).toBeGreaterThan(0);
  expect(
    checks.filter((check) => check.subject !== 'page' && check.subject !== 'url').map((check) => check.id),
  ).toEqual([]);
  expect(facts.gridPages, 'the library’s pages are the URL grid’s').toBe(facts.pagesCrawled);
  expect(facts.pagesCrawled).toBeGreaterThan(0);
  // The analysis ran on the crawl's pages, less any it leaves out (a Cloudflare `/cdn-cgi/` page): never more.
  expect(facts.pagesAnalyzed).toBeGreaterThan(0);
  expect(facts.pagesAnalyzed).toBeLessThanOrEqual(facts.pagesCrawled);
  expect(facts.urlsFound).toBeGreaterThanOrEqual(facts.urlsCrawled);
  // The analysis kept the URLs crawled when it ran, and they are the URL grid's crawled URLs: "of the N URLs crawled"
  // is a number the grid lands on. It isn't the URLs found, which no analysis keeps.
  expect(facts.urlsAnalysed, 'the analysis’s URLs crawled are the URL grid’s').toBe(facts.urlsCrawled);

  // What each check that found issues flags, as the URL grid counts it: where its figure leads, how many of those
  // URLs are pages, and how many were crawled.
  const found = checks.filter((check) => !skipped.has(check.id) && (counts.get(check.id) ?? 0) > 0);
  const flagged = new Map(
    await Promise.all(
      found.map(async (check) => {
        const [every, pages, crawled] = await Promise.all([
          urls(item.id, [flaggedBy(check.id)]),
          urls(item.id, [flaggedBy(check.id), ...PAGES]),
          urls(item.id, [flaggedBy(check.id), { column: 'fetched', op: 'is', value: true }]),
        ]);
        return [check.id, { urls: every.matching, pages: pages.matching, crawled: crawled.matching }] as const;
      }),
    ),
  );
  // The crawl has checks of both kinds with issues, so both wordings are walked.
  expect(new Set(found.map((check) => check.subject))).toEqual(new Set(['page', 'url']));

  await openIssues(page);
  expect(page.url()).toContain(`/crawls/${item.id}/`);
  // The checks that passed are listed too, so the walk is of every check.
  const passed = checks.filter((check) => !skipped.has(check.id) && counts.get(check.id) === 0);
  if (passed.length > 0) {
    const show = page.getByRole('switch', { name: /^Show the (check|[\d,]+ checks) that passed$/ });
    await show.click();
    await expect(show).toBeChecked();
  }
  const list = page.getByRole('listbox', { name: 'Checks' });
  await expect(list.getByRole('option')).toHaveCount(checks.length);
  const detail = detailOf(page);

  for (const check of checks) {
    await test.step(`${check.id} (${check.subject})`, async () => {
      const option = list.locator(`[role="option"][data-check="${check.id}"]`);
      // A check picked puts the list back behind "All checks" where the check has the width: out again for the next.
      await showChecks(page);
      await option.click();
      await expect(page).toHaveURL(new RegExp(`/issues/${check.id}(\\?|$)`));
      await expect(detail.getByRole('heading', { name: check.title, level: 2, exact: true })).toBeVisible();
      // The check's counts are one line of its head: how many, of how much. A check that flagged nothing has none.
      const figures = countsOf(page);

      const flaggedCount = counts.get(check.id);
      if (skipped.has(check.id) || flaggedCount === undefined) {
        await expect(detail.getByText('This check was skipped on this crawl')).toBeVisible();
        await expect(figures).toHaveCount(0);
        return;
      }
      if (flaggedCount === 0) {
        await expect(detail.getByText('No URL on this crawl has this issue.')).toBeVisible();
        await expect(figures).toHaveCount(0);
        return;
      }
      await expect(figures).toHaveCount(1);

      const words = wordsOf(check);
      const whole = check.subject === 'page' ? facts.pagesAnalyzed : (facts.urlsAnalysed ?? 0);
      const mine = flagged.get(check.id);
      // The API's own counts: the check's count is the URLs it flags, every one of them a page where the catalog
      // says so, and no more than what the screen says it's a part of.
      expect(mine?.urls, 'the URL grid lists exactly the check’s count').toBe(flaggedCount);
      if (check.subject === 'page') expect(mine?.pages, 'every URL a page check flags is a page').toBe(flaggedCount);
      // And every URL any check flags was crawled: a URL check's count is a part of the URLs crawled.
      expect(mine?.crawled, 'every URL a check flags is one the crawl crawled').toBe(flaggedCount);
      expect(flaggedCount, `no more than the ${words.many} ${words.of}`).toBeLessThanOrEqual(whole);

      // The count, by what it's of, leading to its URLs.
      const affected = figures.getByRole('link').first();
      await expect(affected).toHaveText(counted(flaggedCount, words));
      await expect(affected).toHaveAttribute('title', 'Open them in URLs');
      await expect(affected).toHaveAttribute('href', new RegExp(`/crawls/${item.id}/urls\\?filter=`));
      // After the count and its share, only how many causes there are to fix, where its URLs share some.
      // A fault of the whole site is the whole site, as Simple says, never "1 page, <0.1%" (round 5, gap 28).
      if (check.siteWide) {
        await expect(figures).toHaveText(
          new RegExp(`^${literal(counted(flaggedCount, words))}·the whole site${CAUSES}$`),
        );
        await expect(figures.getByText('the whole site', { exact: true })).toHaveAttribute(
          'title',
          'A fault of the whole site, whatever its count',
        );
        await expect(figures).not.toContainText('%');
        await showChecks(page);
        await expect(option).toHaveAccessibleName(new RegExp(`, the whole site$`));
        return;
      }
      // A check reported once for the whole site isn't a share of its URLs either (round 5 review R1-9).
      if (check.reportedOnce) {
        await expect(figures).toHaveText(
          new RegExp(`^${literal(counted(flaggedCount, words))}·checked once for the whole site${CAUSES}$`),
        );
        await expect(figures.getByText('checked once for the whole site', { exact: true })).toHaveAttribute(
          'title',
          'Checked once for the whole site, not URL by URL',
        );
        await expect(figures).not.toContainText('%');
        await showChecks(page);
        await expect(option).toHaveAccessibleName(new RegExp(`, checked once for the whole site$`));
        return;
      }
      // Its share, of the right count of the right thing.
      const shareText = new RegExp(
        `^${literal(counted(flaggedCount, words))}·([<>]?[\\d.]+%) of the ${literal(counted(whole, words))} ${words.of}${CAUSES}$`,
      );
      await expect(figures).toHaveText(shareText);
      const shown = shareText.exec(((await figures.textContent()) ?? '').trim())?.[1] ?? '';
      expectShare(shown, flaggedCount, whole, check.id);
      // And nothing of it in the other's words.
      await expect(detail.getByText(check.subject === 'page' ? /URLs crawled/ : /pages analysed/)).toHaveCount(0);
      await expect(detail.getByText(/pages crawled|URLs found/)).toHaveCount(0);

      // What it flags, listed under the same word, one row each: a list, unless grouping saves work (an image, fixed
      // once for every page that shows it; a cause in the site's template), which opens grouped.
      const listed = detail.getByRole('heading', { name: `Affected ${words.many}`, level: 3, exact: true });
      await expect(listed).toBeVisible();
      const views = detail.getByRole('radiogroup', { name: 'How the rows are shown' });
      if (check.grouping === 'none') {
        await expect(views).toHaveCount(0);
        await expect(detail.locator('[role="grid"], [role="treegrid"]')).toHaveAccessibleName(
          `${check.title}: affected ${words.many}`,
        );
        await expect(listed.locator('xpath=..')).toHaveText(
          // Then the order the engine gives them in (evidence): the most linked first, the most used, or the fewest
          // links first (pages missing from the sitemaps).
          new RegExp(
            `^Affected ${words.many}\\s*${literal(counted(flaggedCount, words))}(, (most (linked|used)|fewest links) first)?$`,
          ),
        );
      } else {
        const grouped = views.getByRole('radio', { name: /^Grouped by / });
        const rows = detail.locator('[role="grid"], [role="treegrid"]');
        const opensGrouped =
          (check.grouping === 'relatedUrl' && check.id.startsWith('image-')) || (templateCauses.get(check.id) ?? 0) > 0;
        if (opensGrouped) {
          await expect(grouped).toHaveAttribute('aria-checked', 'true');
          await expect(rows).toHaveAccessibleName(
            new RegExp(`^${literal(check.title)}: affected ${words.many}, grouped by `),
          );
          await expect(rows.getByRole('columnheader', { name: words.Many, exact: true })).toBeVisible();
          await expect(listed.locator('xpath=..')).toHaveText(
            new RegExp(`^Affected ${words.many}\\s*${literal(counted(flaggedCount, words))} in [\\d,]+ groups?`),
          );
        } else {
          await expect(views.getByRole('radio', { name: 'List' })).toHaveAttribute('aria-checked', 'true');
          await expect(rows).toHaveAccessibleName(`${check.title}: affected ${words.many}`);
          await expect(listed.locator('xpath=..')).toHaveText(
            new RegExp(
              `^Affected ${words.many}\\s*${literal(counted(flaggedCount, words))}(, (most (linked|used)|fewest links) first)?List`,
            ),
          );
        }
      }
      // Its row in the list says the same.
      await showChecks(page);
      await expect(option).toHaveAccessibleName(
        // Then its share of the site, "% of total" (round 5): of the pages analysed, the URLs crawled, or the whole site.
        new RegExp(
          `${literal(check.title)} ${count(flaggedCount)} ?${flaggedCount === 1 ? words.one : words.many}(, .+)?$`,
        ),
      );
    });
  }
});

test('Fix first counts each check in what it flags, and says its order in words that fit at 1280x720', async ({
  page,
}, testInfo) => {
  await page.setViewportSize({ width: 1280, height: 720 });
  const { item, checks, counts } = await crawlFacts();
  const byId = new Map(checks.map((check) => [check.id, check]));
  await page.goto('/');
  await page.getByRole('region', { name: 'Library' }).getByRole('link', { name: siteRow(), exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/crawls/${item.id}/overview$`));
  const fix = page.getByRole('main').getByRole('region', { name: 'Fix first' });
  // The engine's order (the analysis's fixFirst), in its words.
  const order = fix.getByText('Most severe first, site-wide faults ahead, then most URLs');
  await expect(order).toBeVisible();
  const rows = fix.getByRole('listitem').getByRole('link');
  await expect(rows.first()).toBeVisible();
  const kinds = new Set<string | undefined>();
  for (const row of await rows.all()) {
    const id = /\/issues\/([a-z0-9-]+)/.exec((await row.getAttribute('href')) ?? '')?.[1] ?? '';
    const check = byId.get(id);
    if (!check) throw new Error(`Fix first lists a check the catalog doesn’t have: ${id}`);
    kinds.add(check.subject);
    // The row's own count, apart from the check's description beside it.
    await expect(row.locator('.figures')).toHaveText(counted(counts.get(id) ?? 0, wordsOf(check)));
  }
  // The synthetic crawl's errors are of both kinds, so both wordings are on screen.
  expect(kinds).toEqual(new Set(['page', 'url']));

  // The line that says the order is longer than it was: it's still one line, beside the title and inside the card.
  const room = await order.evaluate((line) => {
    const card = line.closest('section')?.getBoundingClientRect();
    const title = line.parentElement?.querySelector('h2, h3')?.getBoundingClientRect();
    const box = line.getBoundingClientRect();
    return {
      lines: Math.round(box.height / Number.parseFloat(getComputedStyle(line).lineHeight)),
      insideCard: card !== undefined && box.right <= card.right + 0.5,
      afterTitle: title !== undefined && box.left >= title.right - 0.5,
    };
  });
  await fix.screenshot({ path: testInfo.outputPath('fix-first.png') });
  expect(room).toEqual({ lines: 1, insideCard: true, afterTitle: true });
});

/** A URL that other URLs' issues of this check name, with how many of them do, as the inspector's own read says. */
async function namedUrl(crawl: string, check: Check) {
  const page = await api<{ groups: { relatedUrlId: number | null }[] }>(
    `/api/crawls/${crawl}/issues/${check.id}/groups`,
    { offset: 0, limit: 100 },
  );
  for (const group of page.groups) {
    if (group.relatedUrlId === null) continue;
    const inspected = await api<{ namedBy: { checks: { checkId: string; count: number }[] } | null }>(
      `/api/crawls/${crawl}/urls/inspect`,
      { urlId: group.relatedUrlId },
    );
    const named = inspected.namedBy?.checks.find((entry) => entry.checkId === check.id);
    if (named) return { urlId: group.relatedUrlId, count: named.count };
  }
  return null;
}

test('the inspector counts the URLs whose issues name a URL in what their check flags', async ({ page }) => {
  const { item, checks, counts } = await crawlFacts();
  for (const subject of ['page', 'url'] as const) {
    // A check of this kind whose issues name another URL (a broken link's target, where a redirect chain ends).
    let picked: { check: Check; urlId: number; count: number } | null = null;
    for (const check of checks) {
      if (check.subject !== subject || check.grouping !== 'relatedUrl' || (counts.get(check.id) ?? 0) === 0) continue;
      const named = await namedUrl(item.id, check);
      if (named) {
        picked = { check, ...named };
        break;
      }
    }
    if (!picked) throw new Error(`The synthetic crawl has no ${subject} check whose issues name another URL`);
    const words = wordsOf(picked.check);

    await page.goto(`/crawls/${item.id}/urls?inspect=${picked.urlId}`);
    const inspector = page.getByRole('complementary', { name: 'Inspector' });
    await expect(inspector).toBeVisible();
    await chooseTab(inspector, /^Issues/);
    const named = inspector.getByRole('list', { name: 'Issues on other URLs that name this one' });
    const row = named.getByRole('listitem').filter({
      has: page.getByRole('link', { name: picked.check.title, exact: true }),
    });
    await expect(row, `${picked.check.id} names URL ${picked.urlId}`).toHaveText(
      new RegExp(`^${literal(picked.check.title)}\\s*(Hidden\\s*)?${literal(counted(picked.count, words))}$`),
    );
  }
});

/** After a check's count and its share, only how many causes there are to fix, where its URLs share some. */
const CAUSES = '(·[\\d,]+ causes? to fix(, [\\d,]+ in the site’s template)?)?';

/** The checks the six-digit test opens: a page check listed flat, and a URL check that groups. */
const BIG = { page: 'not-in-sitemap', url: 'internal-client-error', pages: 987_654, urls: 1_234_567 } as const;
const BIG_COUNTS: Record<string, number> = { [BIG.page]: 654_321, [BIG.url]: 312_876 };
/** The line of counts of each, whole: the count, its share, and what it's a share of. */
const PAGE_COUNTS = new RegExp(`^654,321 pages·66% of the 987,654 pages analysed${CAUSES}$`);
const URL_COUNTS = new RegExp(`^312,876 URLs·25% of the 1,234,567 URLs crawled${CAUSES}$`);

/**
 * The crawl with six-digit counts: the sidecar's own answers with the analysis's counts and its two totals (the pages
 * it ran on, the URLs crawled when it ran) and the library's page count made large. The screen then has to hold
 * "654,321" and "of the 1,234,567 URLs crawled".
 */
async function withSixDigitCounts(page: Page, crawl: string) {
  const answer = async (pattern: RegExp, change: (body: Record<string, unknown>) => unknown) =>
    page.route(pattern, async (route) => {
      if (route.request().method() === 'OPTIONS') return route.fallback();
      const response = await route.fetch();
      if (!response.ok()) return route.fulfill({ response });
      return route.fulfill({ response, json: change((await response.json()) as Record<string, unknown>) });
    });
  await answer(new RegExp(`/api/crawls/${crawl}/analysis$`), (body) => ({
    ...body,
    pagesAnalyzed: BIG.pages,
    crawledUrlCount: BIG.urls,
    counts: (body.counts as { checkId: string; count: number }[]).map((entry) =>
      entry.count > 0 ? { ...entry, count: BIG_COUNTS[entry.checkId] ?? 100_000 + entry.count } : entry,
    ),
  }));
  await answer(new RegExp(`/api/crawls/${crawl}$`), (body) => ({ ...body, pageCount: BIG.pages }));
}

/**
 * Nothing in the check's line of counts is cut off or overprinted: each of its parts (the count, its share and what
 * it is a share of, then any more) shows whole, no two overlap, none reaches outside the line or the panel, and the
 * panel doesn't scroll sideways. Nor is a count in the checks list cut off or run into its title.
 */
async function expectFiguresFit(page: Page, where: string) {
  const problems = await page.evaluate(() => {
    const found: string[] = [];
    const detail = document.querySelector('[data-check-detail]');
    if (!detail) return ['there’s no check panel'];
    const panel = detail.getBoundingClientRect();
    const line = detail.querySelector<HTMLElement>('header [data-counts]');
    if (!line) return ['the check’s head has no line of counts'];
    const lineBox = line.getBoundingClientRect();
    if (lineBox.left < panel.left - 0.5 || lineBox.right > panel.right + 0.5) found.push('the counts leave the panel');
    if (line.scrollWidth > line.clientWidth + 1) found.push('the counts are cut off');
    // Its parts, the dots between them left out: the count and its share at least.
    const figures = [...line.children].filter(
      (part): part is HTMLElement => part instanceof HTMLElement && part.getAttribute('aria-hidden') !== 'true',
    );
    if (figures.length < 2) found.push(`the line of counts has ${figures.length} parts`);
    const boxes = figures.map((figure) => figure.getBoundingClientRect());
    figures.forEach((figure, index) => {
      const box = boxes[index];
      if (!box) return;
      const text = figure.textContent;
      if (box.width === 0 || box.height === 0) found.push(`not drawn: ${text}`);
      if (box.left < panel.left - 0.5 || box.right > panel.right + 0.5) found.push(`outside the panel: ${text}`);
      if (box.left < lineBox.left - 0.5 || box.right > lineBox.right + 0.5) found.push(`wider than its line: ${text}`);
      if (figure.scrollWidth > figure.clientWidth + 1) found.push(`cut off: ${text}`);
      boxes.forEach((other, at) => {
        const apart =
          other.left >= box.right - 0.5 ||
          other.right <= box.left + 0.5 ||
          other.top >= box.bottom - 0.5 ||
          other.bottom <= box.top + 0.5;
        if (at > index && !apart) found.push(`overlap: ${text} and ${figures[at]?.textContent}`);
      });
    });
    if (detail.scrollWidth > detail.clientWidth + 1) found.push('the check panel scrolls sideways');
    for (const option of document.querySelectorAll<HTMLElement>('[role="option"][data-check]')) {
      const number = option.querySelector<HTMLElement>('.figures');
      const title = option.querySelector<HTMLElement>('.si-list-row__title');
      if (!number || !title) continue;
      const row = option.getBoundingClientRect();
      const box = number.getBoundingClientRect();
      if (box.right > row.right + 0.5) found.push(`a count runs out of its row: ${option.textContent}`);
      if (title.getBoundingClientRect().right > box.left + 0.5)
        found.push(`a title under its count: ${option.textContent}`);
    }
    return found;
  });
  expect(problems, where).toEqual([]);
}

test('six-digit counts keep their room at 1280x720, beside the inspector, and at 200% zoom', async ({
  page,
}, testInfo) => {
  await page.setViewportSize({ width: 1280, height: 720 });
  const item = await seededCrawl();
  await withSixDigitCounts(page, item.id);
  const detail = detailOf(page);
  const inspector = page.getByRole('complementary', { name: 'Inspector' });

  // A check that flags only pages: a share of the pages analysed.
  await page.goto(`/crawls/${item.id}/issues/${BIG.page}`);
  await expect(countsOf(page)).toHaveText(PAGE_COUNTS);
  await expect(countsOf(page).getByRole('link', { name: '654,321 pages', exact: true })).toHaveAttribute(
    'title',
    'Open them in URLs',
  );
  // The count, then the order the engine gives the pages in (evidence): pages missing from the sitemaps come with the
  // fewest links first, where a sitemap helps most.
  await expect(detail.getByText('654,321 pages, fewest links first', { exact: true })).toBeVisible();
  await expectFiguresFit(page, 'a page check at 1280x720');
  await detail.locator('header').screenshot({ path: testInfo.outputPath('six-digits-page-check.png') });

  // Beside the inspector the checks stack above the check, whose panel is the narrower for it.
  const rows = detail.getByRole('grid');
  await expect(rows.getByRole('row').nth(1)).toBeVisible();
  await rows.getByRole('gridcell').first().click();
  await page.keyboard.press('Enter');
  await expect(inspector).toBeVisible();
  await expect(countsOf(page)).toHaveText(PAGE_COUNTS);
  await expectFiguresFit(page, 'a page check beside the inspector');
  await detail.locator('header').screenshot({ path: testInfo.outputPath('six-digits-page-check-narrow.png') });
  await page.keyboard.press('Escape');
  await expect(inspector).toBeHidden();

  // A check that can flag any URL: a share of the URLs crawled, a seven-digit count.
  await showChecks(page);
  await page.getByRole('listbox', { name: 'Checks' }).locator(`[role="option"][data-check="${BIG.url}"]`).click();
  await expect(countsOf(page)).toHaveText(URL_COUNTS);
  await expect(countsOf(page).getByRole('link', { name: '312,876 URLs', exact: true })).toHaveAttribute(
    'title',
    'Open them in URLs',
  );
  await expectFiguresFit(page, 'a URL check at 1280x720');
  await detail.locator('header').screenshot({ path: testInfo.outputPath('six-digits-url-check.png') });

  // It opens as a list, its count over the rows said in the same six digits; its groups from "Grouped by cause",
  // the first of them opening by itself.
  await expect(detail.getByText(/^312,876 URLs, most (linked|used) first$/)).toBeVisible();
  await expect(detail.getByRole('treegrid', { name: 'Internal URL returns 4xx: affected URLs' })).toBeVisible();
  await showGroups(page);
  const groups = detail.getByRole('treegrid', { name: /, grouped by / });
  await expect(groups.getByRole('row').nth(1)).toBeVisible();
  await expect(detail.getByText(/^312,876 URLs in [\d,]+ groups?$/)).toBeVisible();
  await expect(groups.getByRole('row', { expanded: true })).toBeVisible();
  await groups.focus();
  await expect(groups.getByRole('link', { name: /^Show (all [\d,]+|it) in URLs$/ })).toBeVisible();
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await expect(inspector).toBeVisible();
  await expect(countsOf(page)).toHaveText(URL_COUNTS);
  await expectFiguresFit(page, 'a URL check beside the inspector');
  await detail.locator('header').screenshot({ path: testInfo.outputPath('six-digits-url-check-narrow.png') });
  await page.keyboard.press('Escape');
  await expect(inspector).toBeHidden();

  // The same window at 200% zoom has half the room each way: the figures wrap rather than overprint.
  await page.setViewportSize({ width: 640, height: 360 });
  await expect(countsOf(page)).toHaveText(URL_COUNTS);
  await expectFiguresFit(page, 'a URL check at 200% zoom');
  await detail.locator('header').screenshot({ path: testInfo.outputPath('six-digits-url-check-zoomed.png') });
  await showChecks(page);
  await page.getByRole('listbox', { name: 'Checks' }).locator(`[role="option"][data-check="${BIG.page}"]`).click();
  await expect(countsOf(page)).toHaveText(PAGE_COUNTS);
  await expectFiguresFit(page, 'a page check at 200% zoom');
  await detail.locator('header').screenshot({ path: testInfo.outputPath('six-digits-page-check-zoomed.png') });
});
