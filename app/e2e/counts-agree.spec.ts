// One number everywhere (docs/v2-ui-ia.md, "One number everywhere"; the closing audit's gap 3): a crawl's URLs
// crawled, its pages and its URLs found, as every screen says them (Home, Overview's totals and its line about the
// crawl, URLs, Issues' shares, Simple's Summary, the report) and as the figures that open URLs land, read against the
// engine's own counts. It runs on whatever crawl the tests run on: with SI_E2E_CRAWL set to the garden crawl or the 100k
// crawl it checks those (the audit found 4,521 against 6,286, 110,251 against 112,577 and 101,107 against 101,106
// there):
//   SI_E2E_CRAWL="%LOCALAPPDATA%\ShoutingIguana\dev-crawls\synthetic-100k.sicrawl" npx playwright test e2e/counts-agree.spec.ts
// and on a crawl of the engine's test site, paused part way (every count of a running crawl, from the same moment) and
// then finished.
import { expect, test, type Page } from '@playwright/test';
import { countOf, env, read, seededCrawl, showChecks, sidecar } from './support';
import { libraryIds, removeCrawlsNotIn } from './startedCrawls';

interface Item {
  id: string;
  site: string | null;
  status: string;
  crawledUrlCount: number;
  pageCount: number;
  pagesAnalyzed: number | null;
  outOfDate: boolean;
}
interface Analysis {
  pagesAnalyzed: number;
  crawledUrlCount: number | null;
  counts: { checkId: string; count: number }[];
}
interface Check {
  id: string;
  severity: 'error' | 'warning' | 'notice';
  subject?: 'page' | 'url';
  siteWide?: boolean;
  reportedOnce?: boolean;
}

const n = (value: number) => value.toLocaleString('en-GB');

/** How many URLs the grid lists under these filters, and in all: the engine's own count. */
async function gridCount(id: string, filters: unknown[] = []) {
  return read<{ matching: number; total: number }>(`/api/crawls/${id}/urls`, {
    columns: ['url'],
    filters,
    sort: [],
    limit: 1,
  });
}

/** A figure of a card of figures (Overview's totals), by its label: its number. */
async function figure(page: Page, region: string, label: string) {
  const link = page
    .getByRole('region', { name: region })
    .getByRole('link')
    .filter({ hasText: new RegExp(`^${label}`) });
  return countOf(await link.locator('.si-totals__value').innerText());
}

/** The URL grid's header count, once it has settled on its number. */
async function shownUrls(page: Page) {
  const count = page.getByRole('main').locator('.si-result-count');
  await expect(count).toBeVisible({ timeout: 60_000 });
  return count;
}

/** Every screen's count of a crawl at rest, against the engine's. `hidden`: the checks the user has hidden in it. */
async function expectOneCount(page: Page, id: string, hidden: readonly string[] = []) {
  const item = await read<Item>(`/api/crawls/${id}`);
  const crawled = item.crawledUrlCount;
  const pages = item.pageCount;
  const found = (await gridCount(id)).total;
  expect(crawled).toBe((await gridCount(id, [{ column: 'fetched', op: 'is', value: true }])).matching);

  // The analysis's own counts are the library's, while the crawl hasn't changed since it ran.
  const analysis = await read<Analysis>(`/api/crawls/${id}/analysis`);
  if (!item.outOfDate) {
    expect(analysis.pagesAnalyzed, 'the pages analysed are the pages read').toBe(pages);
    expect(item.pagesAnalyzed).toBe(pages);
    if (analysis.crawledUrlCount !== null) expect(analysis.crawledUrlCount).toBe(crawled);
  }

  // Home: the row of the crawl, its URLs and pages.
  await page.goto('/');
  const library = page.getByRole('region', { name: 'Library' });
  const row = library.getByRole('row').filter({ has: page.locator(`a[href$="/crawls/${id}"]`) });
  await expect(row).toBeVisible();
  const cells = row.getByRole('cell');
  expect(countOf(await cells.nth(2).innerText()), 'Home’s URLs').toBe(crawled);
  expect(countOf(await cells.nth(3).innerText()), 'Home’s pages').toBe(pages);

  // Overview's totals and its line about the crawl.
  await page.goto(`/crawls/${id}/overview`);
  const totals = page.getByRole('region', { name: 'Totals' });
  await expect(totals.locator('.si-totals__value').first()).toHaveText(/\d/, { timeout: 60_000 });
  await expect.poll(() => figure(page, 'Totals', 'Crawled'), { timeout: 30_000 }).toBe(crawled);
  expect(await figure(page, 'Totals', 'Pages')).toBe(pages);
  expect(await figure(page, 'Totals', 'Found')).toBe(found);
  expect((await figure(page, 'Totals', 'Internal')) + (await figure(page, 'Totals', 'External'))).toBe(crawled);
  await expect(page.getByText(`${n(crawled)} URLs crawled, ${n(pages)} pages read`)).toBeVisible();

  // Each total lands on itself in URLs.
  for (const [label, expected] of [
    ['Crawled', crawled],
    ['Pages', pages],
    ['Found', found],
  ] as const) {
    await page.goto(`/crawls/${id}/overview`);
    await expect.poll(() => figure(page, 'Totals', label), { timeout: 30_000 }).toBe(expected);
    await totals
      .getByRole('link')
      .filter({ hasText: new RegExp(`^${label}`) })
      .click();
    const count = await shownUrls(page);
    await expect(count.locator('.si-result-count__n')).toHaveText(n(expected), { timeout: 60_000 });
    // Every URL found is "URLs found", never a bare "URLs": that is the URLs crawled, everywhere.
    await expect(count).toHaveText(
      expected === found ? `${n(found)} URLs found` : `${n(expected)} of ${n(found)} URLs found`,
    );
  }

  // Issues: a page check's share is of the pages, a URL check's of the URLs crawled.
  const checks = (await read<{ checks: Check[] }>('/api/checks')).checks;
  const flagged = (subject: 'page' | 'url') =>
    analysis.counts.find((count) => {
      const check = checks.find((candidate) => candidate.id === count.checkId);
      return (
        count.count > 0 &&
        check &&
        !hidden.includes(check.id) &&
        (check.subject ?? 'url') === subject &&
        !check.siteWide &&
        !check.reportedOnce
      );
    });
  for (const [subject, whole] of [
    ['page', `of the ${n(analysis.pagesAnalyzed)} ${analysis.pagesAnalyzed === 1 ? 'page' : 'pages'} analysed`],
    ['url', `of the ${n(analysis.crawledUrlCount ?? crawled)} URLs crawled`],
  ] as const) {
    const check = flagged(subject);
    if (!check || (subject === 'url' && analysis.crawledUrlCount === null)) continue;
    await page.goto(`/crawls/${id}/issues/${check.checkId}`);
    await expect(page.locator('[data-check-detail]').getByText(whole)).toBeVisible({ timeout: 60_000 });
    // The checks list says the same beside the count ("% of Total").
    await expect(page.locator(`[data-check="${check.checkId}"] span[title]`).first()).toHaveAttribute(
      'title',
      new RegExp(`${whole}$`),
    );
  }

  // The report, the engine's own page: the same pages read and addresses checked.
  const report = await (await sidecar(`/api/crawls/${id}/report`)).text();
  if (!item.outOfDate && analysis.crawledUrlCount !== null) {
    expect(report).toContain(`${n(pages)} ${pages === 1 ? 'page' : 'pages'} read · ${n(crawled)} addresses checked`);
  }

  // Simple's Summary says them in its words.
  await page.goto(`/crawls/${id}/overview`);
  await expect(page.getByRole('heading', { level: 1, name: 'Overview' })).toBeVisible();
  await modeSwitch(page).getByRole('radio', { name: 'Simple' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Overview' })).toBeHidden();
  if (!item.outOfDate && analysis.crawledUrlCount !== null) {
    await expect(
      page.getByText(`Shouting Iguana read ${n(pages)} pages and checked ${n(crawled)} addresses in all`),
    ).toBeVisible({ timeout: 60_000 });
  }
  await expect(page.getByText(new RegExp(`${n(pages)} pages read`))).toBeVisible();
  await modeSwitch(page).getByRole('radio', { name: 'Advanced' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Overview' })).toBeVisible();
}

/**
 * Hides a check that found something, as the user does on its page in Issues, in this crawl or in every crawl. Of the
 * Warnings, so Simple's problems count it too.
 */
async function hideACheck(page: Page, id: string, scope: 'crawl' | 'all') {
  const analysis = await read<Analysis>(`/api/crawls/${id}/analysis`);
  const checks = (await read<{ checks: Check[] }>('/api/checks')).checks;
  const found = analysis.counts.filter((count) => count.count > 0).map((count) => count.checkId);
  const hidden = checks.find((check) => check.severity === 'warning' && found.includes(check.id));
  expect(hidden, 'a Warning that found something, to hide').toBeDefined();
  await page.goto(`/crawls/${id}/issues/${hidden!.id}`);
  await page.getByRole('button', { name: 'Hide this check' }).click();
  await page.getByRole('menuitem', { name: scope === 'crawl' ? /Hide in this crawl/ : /Hide in every crawl/ }).click();
  await showChecks(page);
  await expect(page.getByRole('button', { name: /Show 1 hidden check/ })).toBeVisible();
  return hidden!.id;
}

/**
 * The figures of a crawl's checks with issues, the hidden ones left out everywhere (round 5: part C's hidden checks in
 * part A's Simple and part B's Home): Advanced's Home and sidebar, Simple's Your sites, Summary and What to fix, and the
 * report, against the engine's analysis less the hidden checks.
 */
async function expectHiddenLeftOut(page: Page, id: string, hidden: string) {
  const analysis = await read<Analysis>(`/api/crawls/${id}/analysis`);
  const checks = (await read<{ checks: Check[] }>('/api/checks')).checks;
  const shown = { error: 0, warning: 0, notice: 0 };
  for (const count of analysis.counts) {
    const check = checks.find((candidate) => candidate.id === count.checkId);
    if (check && count.count > 0 && check.id !== hidden) shown[check.severity]++;
  }
  const severityWords = (counts: typeof shown) =>
    (['error', 'warning', 'notice'] as const).map(
      (severity) =>
        // Not the end of a longer number: 30 warnings isn't 130 warnings.
        new RegExp(`(?<![\\d,])${n(counts[severity])}\\s*${severity}${counts[severity] === 1 ? '' : 's'}`),
    );

  // Advanced's Home: the crawl's row.
  await page.goto('/');
  const row = page
    .getByRole('region', { name: 'Library' })
    .getByRole('row')
    .filter({ has: page.locator(`a[href$="/crawls/${id}"]`) });
  const issues = row.getByRole('cell').nth(4);
  for (const words of severityWords(shown)) await expect(issues).toHaveText(words, { timeout: 30_000 });

  // Advanced's sidebar, on the crawl's screens: Errors, Warnings, Notices.
  await page.goto(`/crawls/${id}/overview`);
  const bySeverity = page.getByRole('list', { name: 'Issues by severity' });
  for (const [label, count] of [
    ['Errors', shown.error],
    ['Warnings', shown.warning],
    ['Notices', shown.notice],
  ] as const) {
    await expect(bySeverity.getByRole('link', { name: new RegExp(`^${label}`) })).toHaveText(
      new RegExp(`${n(count)}$`),
      { timeout: 30_000 },
    );
  }

  // The report says it left the check out.
  await page.goto(`/crawls/${id}/report`);
  await expect(
    page.frameLocator('iframe[title^="The report for"]').getByText(/is hidden, so it’s left out/),
  ).toBeVisible({
    timeout: 60_000,
  });

  // Simple: What to fix and Your sites' row count Serious and Worth fixing without it.
  await page.goto(`/crawls/${id}/overview`);
  await modeSwitch(page).getByRole('radio', { name: 'Simple' }).click();
  const problems =
    shown.error + shown.warning === 0
      ? 'Nothing to fix'
      : [
          shown.error > 0 ? `${n(shown.error)} serious` : '',
          shown.warning > 0 ? `${n(shown.warning)} worth fixing` : '',
        ]
          .filter(Boolean)
          .join(', ');
  // What to fix (Simple's Issues) leaves it out and says so; the Summary counts from the same results.
  await page.goto(`/crawls/${id}/issues`);
  await expect(page.getByText('1 check is hidden.', { exact: false })).toBeVisible({ timeout: 60_000 });
  await page.goto('/');
  const site = page
    .getByRole('list', { name: 'Your sites, the checks running now first' })
    .getByRole('listitem')
    .filter({ has: page.locator(`a[href*="/crawls/${id}"]`) })
    .first();
  await expect(site.getByRole('img', { name: new RegExp(`^${problems}`) })).toBeVisible({ timeout: 30_000 });
  await modeSwitch(page).getByRole('radio', { name: 'Advanced' }).click();
}

const modeSwitch = (page: Page) => page.getByRole('banner').getByRole('radiogroup', { name: 'Mode' });

test('every screen says the crawl’s one count of URLs crawled, of pages and of URLs found', async ({ page }) => {
  test.setTimeout(240_000);
  const crawl = await seededCrawl();
  await expectOneCount(page, crawl.id);
});

test.describe('with a check hidden (round 5: hidden checks and one count)', () => {
  for (const scope of ['crawl', 'all'] as const) {
    test(`says one count, and leaves a check hidden ${scope === 'crawl' ? 'in this crawl' : 'in every crawl'} out of every figure of checks`, async ({
      page,
    }) => {
      test.setTimeout(300_000);
      const crawl = await seededCrawl();
      const hidden = await hideACheck(page, crawl.id, scope);
      await expectOneCount(page, crawl.id, [hidden]);
      await expectHiddenLeftOut(page, crawl.id, hidden);
    });
  }
});

test.describe('a crawl of the test site', () => {
  let seeded: string[] = [];
  test.beforeAll(async () => {
    seeded = await libraryIds();
  });
  test.afterAll(async () => {
    await removeCrawlsNotIn(seeded);
  });

  test('says one count wherever it shows, paused part way and once it has finished', async ({ page }) => {
    test.setTimeout(300_000);
    const started = await read<Item>('/api/crawls', {
      options: { startUrl: env('SI_E2E_TESTSITE'), concurrency: 4, maxRequestsPerSecond: 0 },
    });
    await page.goto(`/crawls/${started.id}/overview`);
    const progress = page.getByRole('region', { name: 'Progress' });
    await expect
      .poll(async () => countOf(await progress.locator('.si-stat__value').first().innerText()), { timeout: 60_000 })
      .toBeGreaterThan(200);
    await sidecar(`/api/crawls/${started.id}/pause`, { method: 'POST' });
    await expect(page.getByText('The crawl is paused.', { exact: false })).toBeVisible({ timeout: 30_000 });

    // Paused, every live count is of one moment: Overview's, the sidebar's, the top bar's and Home's card.
    const crawledLive = async () => countOf(await progress.locator('.si-stat__value').first().innerText());
    await expect.poll(crawledLive, { timeout: 10_000 }).toBeGreaterThan(0);
    // The requests on their way when it paused finish and are counted: the live count stands still (a request paced at
    // the site's gate can land a moment later), then it's the grid's.
    let before = -1;
    await expect
      .poll(
        async () => {
          const now = await crawledLive();
          const still = now === before;
          before = now;
          return still;
        },
        { intervals: [2_000], timeout: 30_000 },
      )
      .toBe(true);
    const inGrid = async () => (await gridCount(started.id, [{ column: 'fetched', op: 'is', value: true }])).matching;
    await expect.poll(async () => (await crawledLive()) - (await inGrid()), { timeout: 15_000 }).toBe(0);
    const live = await crawledLive();
    const sidebar = page.getByRole('region', { name: 'Running crawls' });
    await expect(sidebar.getByRole('heading', { name: '1 paused' })).toBeVisible();
    expect(countOf(await sidebar.locator('.si-nav-item__count').first().innerText())).toBe(live);
    await page.goto('/');
    const card = page.getByRole('article').filter({ has: page.getByRole('button', { name: /^Resume/ }) });
    await expect.poll(async () => countOf(await card.locator('.si-stat__value').first().innerText())).toBe(live);

    // Finished, every screen says the library's counts.
    await sidecar(`/api/crawls/${started.id}/resume`, { method: 'POST' });
    for (;;) {
      const item = await read<Item & { analyzedAt: string | null }>(`/api/crawls/${started.id}`);
      if (item.status === 'completed' && item.analyzedAt !== null) break;
      if (item.status === 'completed' && item.analyzedAt === null) {
        await sidecar(`/api/crawls/${started.id}/analyze`, { method: 'POST' });
      }
      await page.waitForTimeout(500);
    }
    await expectOneCount(page, started.id);
  });
});
