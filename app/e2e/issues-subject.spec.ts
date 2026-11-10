// What a check's count is called, against the real sidecar and its synthetic crawl. For every check the catalog has,
// Issues says "pages affected, of the N pages crawled" only where the catalog says the check flags only pages (its
// `subject`), and "URLs affected, of the N URLs found" for any other; each N is the API's own count, and no check's
// count is more than it. Overview's "Fix first" and the inspector word the same counts the same way, and six-digit
// counts keep their room. Nothing here waits on a clock: every expectation is of what the screen says in the end.
import { expect, test, type Page } from '@playwright/test';

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
}
interface Analysis {
  pagesAnalyzed: number;
  counts: { checkId: string; count: number }[];
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
];

/** The words for what a check flags: pages only where the catalog says it flags nothing else. */
const wordsOf = (check: Check) =>
  check.subject === 'page'
    ? { one: 'page', many: 'pages', Many: 'Pages', of: 'crawled' }
    : { one: 'URL', many: 'URLs', Many: 'URLs', of: 'found' };

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
    skipped: new Set(analysis.skipped.map((entry) => entry.checkId)),
    pagesAnalyzed: analysis.pagesAnalyzed,
    /** The pages crawled: the library's page count. */
    pagesCrawled: item.pageCount,
    /** The same, counted by the URL grid: the two must agree for "pages" to be one number. */
    gridPages: pages.matching,
    /** Every URL the crawl found, crawled or not. */
    urlsFound: all.total,
    urlsCrawled: crawled.matching,
  };
}

const detailOf = (page: Page) => page.locator('[data-check-detail]');

/** A figure of the check's header (its label, value and line), by its label. */
const figure = (page: Page, label: string) => detailOf(page).getByText(label, { exact: true }).locator('xpath=..');

/** From Home to the crawl's Issues, through the sidebar, as a user goes. */
async function openIssues(page: Page) {
  await page.goto('/');
  await page.getByRole('region', { name: 'Library' }).getByRole('link', { name: site(), exact: true }).click();
  await page
    .getByRole('navigation', { name: 'Main' })
    .getByRole('link', { name: /^Issues/ })
    .first()
    .click();
  await expect(page.getByRole('heading', { name: 'Issues', level: 1 })).toBeVisible();
  await expect(page).toHaveURL(/\/crawls\/[0-9a-f]{16}\/issues\/[a-z0-9-]+/);
  await expect(page.getByRole('listbox', { name: 'Checks' })).toBeVisible();
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
  const { item, checks, counts, skipped } = facts;

  // The catalog says what every check flags, and "pages" is one number wherever it's read.
  expect(checks.length).toBeGreaterThan(0);
  expect(
    checks.filter((check) => check.subject !== 'page' && check.subject !== 'url').map((check) => check.id),
  ).toEqual([]);
  expect(facts.gridPages, 'the library’s pages are the URL grid’s').toBe(facts.pagesCrawled);
  expect(facts.pagesCrawled).toBeGreaterThan(0);
  expect(facts.urlsFound).toBeGreaterThanOrEqual(facts.urlsCrawled);

  // What each check that found issues flags, as the URL grid counts it: where its figure leads, and how many of
  // those URLs are pages.
  const found = checks.filter((check) => !skipped.has(check.id) && (counts.get(check.id) ?? 0) > 0);
  const flagged = new Map(
    await Promise.all(
      found.map(async (check) => {
        const [every, pages] = await Promise.all([
          urls(item.id, [flaggedBy(check.id)]),
          urls(item.id, [flaggedBy(check.id), ...PAGES]),
        ]);
        return [check.id, { urls: every.matching, pages: pages.matching }] as const;
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
      await option.click();
      await expect(page).toHaveURL(new RegExp(`/issues/${check.id}(\\?|$)`));
      await expect(detail.getByRole('heading', { name: check.title, level: 2, exact: true })).toBeVisible();
      const figures = detail.getByText(/^(Pages|URLs) affected$|^Share of (pages|URLs)$/);

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

      const words = wordsOf(check);
      const whole = check.subject === 'page' ? facts.pagesCrawled : facts.urlsFound;
      const mine = flagged.get(check.id);
      // The API's own counts: the check's count is the URLs it flags, every one of them a page where the catalog
      // says so, and no more than what the screen says it's a part of.
      expect(mine?.urls, 'the URL grid lists exactly the check’s count').toBe(flaggedCount);
      if (check.subject === 'page') expect(mine?.pages, 'every URL a page check flags is a page').toBe(flaggedCount);
      expect(flaggedCount, `no more than the ${words.many} ${words.of}`).toBeLessThanOrEqual(whole);

      // The count, by what it's of, leading to its URLs.
      const affected = detail.getByRole('link', { name: new RegExp(`^${words.Many} affected`) });
      await expect(affected).toHaveText(
        new RegExp(`^${words.Many} affected\\s*${count(flaggedCount)}\\s*Open them in URLs$`),
      );
      // Its share, of the right count of the right thing.
      const share = figure(page, `Share of ${words.many}`);
      const shareText = new RegExp(
        `^Share of ${words.many}\\s*([<>]?[\\d.]+%)\\s*of the ${literal(counted(whole, words))} ${words.of}$`,
      );
      await expect(share).toHaveText(shareText);
      const shown = shareText.exec(((await share.textContent()) ?? '').trim())?.[1] ?? '';
      expectShare(shown, flaggedCount, whole, check.id);
      // And nothing of it in the other's words.
      await expect(figures).toHaveCount(2);
      await expect(detail.getByText(check.subject === 'page' ? /URLs found/ : /pages crawled/)).toHaveCount(0);

      // What it flags, listed under the same word.
      const listed = detail.getByRole('heading', { name: `Affected ${words.many}`, level: 3, exact: true });
      await expect(listed).toBeVisible();
      if (check.grouping === 'none') {
        await expect(detail.getByRole('grid')).toHaveAccessibleName(`${check.title}: affected ${words.many}`);
        await expect(listed.locator('xpath=..')).toHaveText(
          new RegExp(`^Affected ${words.many}\\s*${literal(counted(flaggedCount, words))}$`),
        );
      } else {
        const groups = detail.getByRole('treegrid');
        await expect(groups).toHaveAccessibleName(
          `${check.title}: affected ${words.many}, grouped by ${check.grouping === 'relatedUrl' ? 'related URL' : 'detail'}`,
        );
        await expect(groups.getByRole('columnheader', { name: words.Many, exact: true })).toBeVisible();
      }
      // Its row in the list says the same.
      await expect(option).toHaveAccessibleName(
        new RegExp(`${literal(check.title)} ${count(flaggedCount)} ?${flaggedCount === 1 ? words.one : words.many}$`),
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
  await page.getByRole('region', { name: 'Library' }).getByRole('link', { name: site(), exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/crawls/${item.id}/overview$`));
  const fix = page.getByRole('main').getByRole('region', { name: 'Fix first' });
  const order = fix.getByText('The most severe first, then the most pages or URLs affected');
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
    await inspector.getByRole('tab', { name: /^Issues/ }).click();
    const named = inspector.getByRole('list', { name: 'Issues on other URLs that name this one' });
    const row = named.getByRole('listitem').filter({
      has: page.getByRole('link', { name: picked.check.title, exact: true }),
    });
    await expect(row, `${picked.check.id} names URL ${picked.urlId}`).toHaveText(
      new RegExp(`^${literal(picked.check.title)}\\s*(Hidden\\s*)?${literal(counted(picked.count, words))}$`),
    );
  }
});

/** The checks the six-digit test opens: a page check listed flat, and a URL check that groups. */
const BIG = { page: 'not-in-sitemap', url: 'internal-client-error', pages: 987_654, urls: 1_234_567 } as const;
const BIG_COUNTS: Record<string, number> = { [BIG.page]: 654_321, [BIG.url]: 312_876 };

/**
 * The crawl with six-digit counts: the sidecar's own answers with the analysis's counts, the library's page count
 * and the URL grid's total made large. The screen then has to hold "654,321" and "of the 1,234,567 URLs found".
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
    counts: (body.counts as { checkId: string; count: number }[]).map((entry) =>
      entry.count > 0 ? { ...entry, count: BIG_COUNTS[entry.checkId] ?? 100_000 + entry.count } : entry,
    ),
  }));
  await answer(new RegExp(`/api/crawls/${crawl}$`), (body) => ({ ...body, pageCount: BIG.pages }));
  await answer(new RegExp(`/api/crawls/${crawl}/urls/facets$`), (body) => ({ ...body, total: BIG.urls }));
}

/**
 * Nothing in the check's header is cut off or overprinted: each figure's label, value and line show whole, no two
 * figures overlap, none reaches outside the panel, and the panel doesn't scroll sideways. Nor is a count in the
 * checks list cut off or run into its title.
 */
async function expectFiguresFit(page: Page, where: string) {
  const problems = await page.evaluate(() => {
    const found: string[] = [];
    const detail = document.querySelector('[data-check-detail]');
    if (!detail) return ['there’s no check panel'];
    const panel = detail.getBoundingClientRect();
    const figures = [...detail.querySelectorAll<HTMLElement>('.si-stat')];
    if (figures.length < 2) found.push(`the header has ${figures.length} figures`);
    const boxes = figures.map((figure) => figure.getBoundingClientRect());
    figures.forEach((figure, index) => {
      const box = boxes[index];
      if (!box) return;
      const text = figure.textContent;
      if (box.width === 0 || box.height === 0) found.push(`not drawn: ${text}`);
      if (box.left < panel.left - 0.5 || box.right > panel.right + 0.5) found.push(`outside the panel: ${text}`);
      for (const part of figure.children) {
        if (part.scrollWidth > part.clientWidth + 1) found.push(`cut off: ${part.textContent}`);
        const inner = part.getBoundingClientRect();
        if (inner.right > box.right + 0.5) found.push(`wider than its figure: ${part.textContent}`);
      }
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

  // A check that flags only pages: a share of the pages crawled.
  await page.goto(`/crawls/${item.id}/issues/${BIG.page}`);
  await expect(figure(page, 'Pages affected')).toHaveText(/^Pages affected\s*654,321\s*Open them in URLs$/);
  await expect(figure(page, 'Share of pages')).toHaveText(/^Share of pages\s*66%\s*of the 987,654 pages crawled$/);
  await expect(detail.getByText('654,321 pages', { exact: true })).toBeVisible();
  await expectFiguresFit(page, 'a page check at 1280x720');
  await detail.locator('header').screenshot({ path: testInfo.outputPath('six-digits-page-check.png') });

  // Beside the inspector the checks stack above the check, whose panel is the narrower for it.
  const rows = detail.getByRole('grid');
  await expect(rows.getByRole('row').nth(1)).toBeVisible();
  await rows.getByRole('gridcell').first().click();
  await page.keyboard.press('Enter');
  await expect(inspector).toBeVisible();
  await expect(figure(page, 'Share of pages')).toHaveText(/of the 987,654 pages crawled$/);
  await expectFiguresFit(page, 'a page check beside the inspector');
  await detail.locator('header').screenshot({ path: testInfo.outputPath('six-digits-page-check-narrow.png') });
  await page.keyboard.press('Escape');
  await expect(inspector).toBeHidden();

  // A check that can flag any URL: a share of the URLs found, a seven-digit count.
  await page.getByRole('listbox', { name: 'Checks' }).locator(`[role="option"][data-check="${BIG.url}"]`).click();
  await expect(figure(page, 'URLs affected')).toHaveText(/^URLs affected\s*312,876\s*Open them in URLs$/);
  await expect(figure(page, 'Share of URLs')).toHaveText(/^Share of URLs\s*25%\s*of the 1,234,567 URLs found$/);
  await expectFiguresFit(page, 'a URL check at 1280x720');
  await detail.locator('header').screenshot({ path: testInfo.outputPath('six-digits-url-check.png') });

  const groups = detail.getByRole('treegrid');
  await expect(groups.getByRole('row').nth(1)).toBeVisible();
  await groups.focus();
  await page.keyboard.press('ArrowRight');
  await expect(groups.getByRole('row', { expanded: true })).toBeVisible();
  await expect(groups.getByRole('link', { name: /^Show (all [\d,]+|it) in URLs$/ })).toBeVisible();
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await expect(inspector).toBeVisible();
  await expect(figure(page, 'Share of URLs')).toHaveText(/of the 1,234,567 URLs found$/);
  await expectFiguresFit(page, 'a URL check beside the inspector');
  await detail.locator('header').screenshot({ path: testInfo.outputPath('six-digits-url-check-narrow.png') });
  await page.keyboard.press('Escape');
  await expect(inspector).toBeHidden();

  // The same window at 200% zoom has half the room each way: the figures wrap rather than overprint.
  await page.setViewportSize({ width: 640, height: 360 });
  await expect(figure(page, 'Share of URLs')).toHaveText(/^Share of URLs\s*25%\s*of the 1,234,567 URLs found$/);
  await expectFiguresFit(page, 'a URL check at 200% zoom');
  await detail.locator('header').screenshot({ path: testInfo.outputPath('six-digits-url-check-zoomed.png') });
  await page.getByRole('listbox', { name: 'Checks' }).locator(`[role="option"][data-check="${BIG.page}"]`).click();
  await expect(figure(page, 'Share of pages')).toHaveText(/^Share of pages\s*66%\s*of the 987,654 pages crawled$/);
  await expectFiguresFit(page, 'a page check at 200% zoom');
  await detail.locator('header').screenshot({ path: testInfo.outputPath('six-digits-page-check-zoomed.png') });
});
