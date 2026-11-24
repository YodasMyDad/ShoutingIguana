// Simple's results against the real sidecar (docs/v2-ui-simple.md, "The Summary", "What to fix", "One problem", "The
// score"): the Summary, the score's breakdown, What to fix and one problem, on the seeded crawl or, with
// SI_E2E_CRAWL=<file>, on a real one (the garden crawl, the 100k crawl). The sweep walks every figure of the three
// screens and follows it: what it lands on must hold exactly that many, and say the same number the engine gives. On the
// garden crawl (31 October 2026) the figures are also held to what today's engine gives it (87, Good; 2, 3 and 23).
import { expect, test, type Locator, type Page } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import path from 'node:path';
import { deadEndFixList, FIX_LISTED, FIX_SUMMARY, fixShape } from './fixList';
import { countOf, expectNoViolations, read, seededCrawl } from './support';

test.use({ storageState: { cookies: [], origins: [] } });

interface Analysis {
  pagesAnalyzed: number;
  crawledUrlCount: number | null;
  counts: { checkId: string; count: number; clears?: { checkId: string; all: boolean }[] }[];
  skipped: { checkId: string }[];
  fixFirst: string[];
  score: {
    value: number;
    band: string;
    lost: number;
    covers: { pages: number; urls: number; checksRun: number; checksSkipped: number };
    lines: { checkId: string; points: number; severity: string }[];
    ifFixed: { checkIds: string[]; value: number };
  } | null;
}
interface Check {
  id: string;
  severity: 'error' | 'warning' | 'notice';
  plainTitle: string | null;
  title: string;
  subject?: 'page' | 'url';
  siteWide?: boolean;
  reportedOnce?: boolean;
  grouping: string;
  references?: string;
}

const BANDS: Record<string, string> = { veryGood: 'Very good', good: 'Good', needsWork: 'Needs work', poor: 'Poor' };
const SHOTS = path.join(import.meta.dirname, '..', 'test-results', 'simple-results');
const report = (line: string) => void process.stdout.write(`${line}\n`);
const main = (page: Page) => page.getByRole('main');
const number = (value: number) => value.toLocaleString('en-GB');

async function facts() {
  const crawl = await seededCrawl();
  const analysis = await read<Analysis>(`/api/crawls/${crawl.id}/analysis`);
  const { checks } = await read<{ checks: Check[] }>('/api/checks');
  const byId = new Map(checks.map((check) => [check.id, check]));
  const counts = new Map(analysis.counts.map((entry) => [entry.checkId, entry.count]));
  const found = (severity: Check['severity']) =>
    analysis.counts.filter((entry) => entry.count > 0 && byId.get(entry.checkId)?.severity === severity).length;
  return { crawl, analysis, checks, byId, counts, found };
}

/**
 * "9 addresses", as a row in Simple's lists writes a check's count (without what it's a share of, so rows compare),
 * then the pages of a problem merged into its row ("9 addresses on 5 pages"), then what fixing it adds to the score,
 * when it adds some: "9 addresses +2 to your score".
 */
function rowWords(check: Check, count: number, points: number, alsoPages = 0): string {
  const counted = check.siteWide
    ? 'The whole site'
    : check.reportedOnce
      ? 'Once, for the whole site'
      : `${number(count)} ${(check.subject ?? 'page') === 'page' ? (count === 1 ? 'page' : 'pages') : count === 1 ? 'address' : 'addresses'}`;
  const words = alsoPages > 0 ? `${counted} on ${alsoPages === 1 ? '1 page' : `${number(alsoPages)} pages`}` : counted;
  // It pinned "9 addresses 2 points back". Changed in the UX audit of 2026-11-19 (S-19): the points read "+2 to your
  // score".
  return points > 0 ? `${words} +${number(points)} to your score` : words;
}

/**
 * "9 of 265 pages", as Simple writes a check of pages' count and what it is a share of; a check of addresses its count
 * alone, "9 addresses". It pinned "9 of 4,521 addresses" for those. Changed in the UX audit of 2026-11-19 (S-19):
 * every image, script and link is an address, so the whole says nothing of the site.
 */
function shareOf(check: Check, count: number, analysis: Analysis): string {
  if (check.siteWide) return 'The whole site';
  if (check.reportedOnce) return 'Once, for the whole site';
  if ((check.subject ?? 'page') !== 'page') return `${number(count)} ${count === 1 ? 'address' : 'addresses'}`;
  const whole = analysis.score?.covers.pages ?? analysis.pagesAnalyzed;
  return `${number(count)} of ${number(whole)} ${whole === 1 ? 'page' : 'pages'}`;
}

async function openSummary(page: Page, id: string) {
  const started = Date.now();
  await page.goto(`/crawls/${id}/overview`);
  await expect(main(page).getByRole('img', { name: /^Score \d+ out of 100/ })).toBeVisible({ timeout: 20_000 });
  return Date.now() - started;
}

async function shoot(page: Page, name: string) {
  mkdirSync(SHOTS, { recursive: true });
  await page.screenshot({ path: path.join(SHOTS, `${name}.png`) });
}

test('the Summary says the engine’s score, word and counts, and passes axe in both themes', async ({ page }) => {
  const { crawl, analysis, byId, found } = await facts();
  test.skip(analysis.score === null, 'The crawl has no score');
  const score = analysis.score!;
  await page.setViewportSize({ width: 1280, height: 720 });
  const firstPaint = await openSummary(page, crawl.id);
  report(`Summary: score and lists on screen ${firstPaint} ms after the navigation (${crawl.site ?? crawl.fileName})`);
  await expect(
    main(page).getByRole('img', { name: `Score ${score.value} out of 100, ${BANDS[score.band]}` }),
  ).toBeVisible();
  const tally = main(page).getByRole('list', { name: 'What the checks found' }).getByRole('listitem');
  await expect(tally.nth(0)).toContainText(number(found('error')));
  await expect(tally.nth(1)).toContainText(number(found('warning')));
  await expect(tally.nth(2)).toContainText(number(found('notice')));
  if (crawl.site === 'gardenfurnituresets.co.uk') {
    // The crawl of 31 October 2026 as the engine scores it now (the mockups' 85 and 2, 4, 13 were the design's).
    expect(score.value).toBe(87);
    expect(score.band).toBe('good');
    expect([found('error'), found('warning'), found('notice')]).toEqual([2, 3, 23]);
    // The sentence counts the jobs: a problem another's fix clears is folded under it.
    const five = analysis.fixFirst.filter((id) => byId.get(id)?.severity !== 'notice').slice(0, 5);
    const numbered = fixJobs(five, analysis, byId).rows.filter((job) => !job.nested).length;
    const these = ['this one', 'these two', 'these three', 'these four', 'these five'][numbered - 1] ?? '';
    await expect(main(page).getByText(`Fixing ${these} takes the score from 87 to 100.`)).toBeVisible();
  }
  await expectNoViolations(page);
  await shoot(page, 'summary-light');
  await page.emulateMedia({ colorScheme: 'dark' });
  await expectNoViolations(page);
  await shoot(page, 'summary-dark');
  await page.emulateMedia({ colorScheme: 'light' });
  // 200% zoom of a 1280x720 window.
  await page.setViewportSize({ width: 640, height: 360 });
  await shoot(page, 'summary-200');
  await expectNoViolations(page);
});

test('the score’s breakdown: the engine’s lines add up to its score, and Esc closes it', async ({ page }) => {
  const { crawl, analysis } = await facts();
  test.skip(analysis.score === null, 'The crawl has no score');
  const score = analysis.score!;
  await page.setViewportSize({ width: 1280, height: 720 });
  await openSummary(page, crawl.id);
  await main(page).getByRole('link', { name: 'How the score is worked out' }).click();
  const dialog = page.getByRole('dialog', { name: 'How the score is worked out' });
  await expect(dialog).toBeVisible();
  await expect(page).toHaveURL(/score=1/);
  const costing = score.lines.filter((line) => line.points > 0);
  await expect(dialog.locator('tbody tr').filter({ hasText: '−' })).toHaveCount(costing.length);
  expect(costing.reduce((sum, line) => sum + line.points, 0)).toBe(score.lost);
  await expect(dialog.getByRole('row', { name: `Score 100 − ${number(score.lost)} ${score.value}` })).toBeVisible();
  await expectNoViolations(page);
  await shoot(page, 'breakdown-light');
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(page).not.toHaveURL(/score=1/);
  await expect(main(page).getByRole('link', { name: 'How the score is worked out' })).toBeVisible();
});

/**
 * A list of problems as jobs, as the Summary lists its five and What to fix its parts (simple/fixRows.ts): a check a
 * fix of another of its severity in the list clears entirely (the analysis's `clears[].all`) under that one, which
 * carries the points of both. The rows in order, each with the points it says.
 *
 * It pinned every such check as a row folded under the other. Changed in the UX audit of 2026-11-19 (S-04): the pages
 * that link to dead addresses, cleared by the dead addresses' own check, are that check from its other end, so the two
 * are one row ("9 addresses on 5 pages") and the pages' check has no row: it is in `merged`, and its pages in the
 * row's `alsoPages`.
 */
function fixJobs(
  ids: readonly string[],
  analysis: Analysis,
  byId: ReadonlyMap<string, Check>,
  severities: readonly Check['severity'][] = ['error', 'warning'],
): { rows: { id: string; points: number; nested: boolean; alsoPages: number }[]; merged: string[] } {
  const pointsOf = (id: string) => analysis.score?.lines.find((line) => line.checkId === id)?.points ?? 0;
  const found = (id: string) => analysis.counts.find((count) => count.checkId === id)?.count ?? 0;
  const under = new Map<string, string[]>();
  const folded = new Set<string>();
  for (const severity of severities) {
    const part = ids.filter((id) => byId.get(id)?.severity === severity);
    for (const id of part) {
      if (folded.has(id)) continue;
      for (const cleared of analysis.counts.find((count) => count.checkId === id)?.clears ?? []) {
        const other = cleared.checkId;
        if (!cleared.all || !part.includes(other) || other === id || folded.has(other) || under.has(other)) continue;
        folded.add(other);
        under.set(id, [...(under.get(id) ?? []), other]);
      }
    }
  }
  const mirrors = (id: string, other: string) =>
    fixShape(byId.get(id)!) === 'addresses' && fixShape(byId.get(other)!) === 'pages';
  const rows = ids
    .filter((id) => !folded.has(id))
    .flatMap((id) => {
      const all = under.get(id) ?? [];
      const merged = all.filter((other) => mirrors(id, other));
      return [
        {
          id,
          points: [id, ...all].reduce((sum, each) => sum + pointsOf(each), 0),
          nested: false,
          alsoPages: merged.reduce((most, each) => Math.max(most, found(each)), 0),
        },
        ...all
          .filter((each) => !merged.includes(each))
          .map((each) => ({ id: each, points: 0, nested: true, alsoPages: 0 })),
      ];
    });
  return { rows, merged: [...under].flatMap(([id, all]) => all.filter((other) => mirrors(id, other))) };
}

/** The rows a list shows, with each one's link and count. */
async function rowsOf(list: Locator) {
  return list.getByRole('link').evaluateAll((links) =>
    links.map((link) => ({
      href: link.getAttribute('href') ?? '',
      count: link.querySelector('.si-plain-row__count')?.textContent ?? '',
    })),
  );
}

test('the sweep: every figure of the Summary, What to fix and one problem lands on exactly itself', async ({
  page,
}) => {
  test.setTimeout(600_000);
  const { crawl, analysis, byId, counts, found } = await facts();
  test.skip(analysis.score === null, 'The crawl has no score');
  const passed = analysis.counts.filter((entry) => entry.count === 0).length;
  const skipped = analysis.skipped.length;
  await page.setViewportSize({ width: 1280, height: 720 });
  await openSummary(page, crawl.id);
  let followed = 0;
  // What to fix as jobs: a problem merged into another's row has no row of its own (S-04).
  const listed = fixJobs(analysis.fixFirst, analysis, byId, ['error', 'warning', 'notice']);
  const mergedOf = (severity: Check['severity']) =>
    listed.merged.filter((id) => byId.get(id)?.severity === severity).length;

  // The five counts beside the score: each lands on its part of What to fix, holding exactly that many.
  // It pinned as many links in a part as its heading counts. Changed in the UX audit of 2026-11-19 (S-04): the heading
  // still counts every problem, and a problem merged into another's row is in that row, so the part has one link fewer
  // for each.
  const parts: [string, string, number, number][] = [
    ['Serious problem', 'Serious', found('error'), mergedOf('error')],
    ['worth fixing', 'Worth fixing', found('warning'), mergedOf('warning')],
    ['worth a look', 'Worth a look', found('notice'), mergedOf('notice')],
    ['found nothing wrong', 'Nothing wrong', passed, 0],
    ['couldn’t run', 'Couldn’t check', skipped, 0],
  ];
  for (const [words, heading, count, merged] of parts) {
    await openSummary(page, crawl.id);
    const row = main(page)
      .getByRole('list', { name: 'What the checks found' })
      .getByRole('listitem')
      .filter({ hasText: words });
    expect(countOf(await row.textContent())).toBe(count);
    if (count === 0) {
      await expect(row.getByRole('link')).toHaveCount(0);
      continue;
    }
    await row.getByRole('link').click();
    const section = main(page).getByRole('region', { name: `${heading} ${number(count)}` });
    await expect(section).toBeVisible();
    await expect(section.getByRole('link')).toHaveCount(count - merged);
    followed++;
  }

  // Fix these first: the engine's first five errors and warnings, in its order, each count the engine's; a problem
  // another's fix clears entirely is folded under it, as What to fix folds it, the points of both on the row above.
  await openSummary(page, crawl.id);
  const fixFirst = analysis.fixFirst.filter((id) => byId.get(id)?.severity !== 'notice').slice(0, 5);
  const jobs = fixJobs(fixFirst, analysis, byId).rows;
  const firstRows = await rowsOf(main(page).getByRole('region', { name: 'Fix these first' }).getByRole('list'));
  expect(firstRows.map((row) => row.href.split('/').pop())).toEqual(jobs.map((job) => job.id));
  for (const [index, row] of firstRows.entries()) {
    const id = row.href.split('/').pop() ?? '';
    const check = byId.get(id)!;
    // It pinned "The whole site 60 points back". Changed in the UX audit of 2026-11-19 (S-19, and S-04 for a row
    // that holds two problems): "The whole site +60 to your score", "9 addresses on 5 pages +6 to your score".
    expect(row.count).toBe(rowWords(check, counts.get(id) ?? 0, jobs[index]?.points ?? 0, jobs[index]?.alsoPages ?? 0));
  }

  // What to fix: every problem's count is the engine's, and its page says the same and lists exactly that many.
  await page.goto(`/crawls/${crawl.id}/issues#worth-a-look`);
  await expect(main(page).getByRole('heading', { level: 1, name: 'What to fix' })).toBeVisible();
  // The lists come after the heading, once the results are read: rows read before then were none (the Serious part
  // came back empty on a busy machine, and the count below was 12 short).
  // The three parts are drawn together; a crawl needn't have each.
  await expect(
    main(page)
      .getByRole('region', { name: /^(Serious|Worth fixing|Worth a look) / })
      .first(),
  ).toBeVisible();
  const problems = [
    ...(await rowsOf(main(page).getByRole('region', { name: /^Serious / }))),
    ...(await rowsOf(main(page).getByRole('region', { name: /^Worth fixing / }))),
    ...(await rowsOf(main(page).getByRole('region', { name: /^Worth a look / }))),
  ];
  // It pinned a row for every problem found. Changed in the UX audit of 2026-11-19 (S-04): all but those merged into
  // another's row.
  expect(problems).toHaveLength(found('error') + found('warning') + found('notice') - listed.merged.length);
  expect(problems.filter((row) => listed.merged.includes(row.href.split('/').pop() ?? ''))).toEqual([]);
  for (const row of problems) {
    const id = row.href.split('/').pop() ?? '';
    const check = byId.get(id)!;
    const count = counts.get(id) ?? 0;
    const share = shareOf(check, count, analysis);
    // A row folded under another says no points (they're on that row); any other its own, or with what's folded under
    // it; a row that holds a merged problem says its pages after its own count.
    const alsoPages = listed.rows.find((job) => job.id === id)?.alsoPages ?? 0;
    expect(row.count.startsWith(rowWords(check, count, 0, alsoPages)), `${id}: ${row.count}`).toBe(true);
    await page.goto(row.href);
    await expect(main(page).getByRole('heading', { level: 1 })).toBeVisible();
    // It pinned the share somewhere in the head's facts ("9 of 4,521 addresses"). Changed in the UX audit of
    // 2026-11-19 (S-19): the first fact is the count, alone for a check of addresses (shareOf).
    await expect(main(page).getByRole('list').first().getByRole('listitem').first()).toHaveText(share);
    const shape = fixShape(check);
    if (shape) {
      // It pinned a region "Where it is" with the check's count and a table. Changed in the UX audit of 2026-11-19
      // (S-02 to S-05, S-07): a problem that is a link has "Where to fix it", counted in links to change on pages to
      // edit, links many pages share and addresses no page links to; no region "Where it is".
      const fix = main(page).getByRole('region', { name: 'Where to fix it' });
      const summary = fix.locator('h2 + span');
      await expect(summary).toHaveText(FIX_SUMMARY, { timeout: 15_000 });
      await expect(main(page).getByRole('region', { name: 'Where it is' })).toHaveCount(0);
      const said = (await summary.textContent()) ?? '';
      const edited = fix.getByRole('list', { name: 'Pages to edit' }).getByRole('listitem');
      if (shape === 'addresses' && count <= FIX_LISTED) {
        // Dead addresses: the three parts hold exactly the check's count of them, as the sidecar says where each is.
        const list = await deadEndFixList(crawl.id, id);
        expect(said).toBe(list.summary);
        expect(list.linked.length + list.shared.length + list.unlinked.length, id).toBe(count);
        await expect(edited).toHaveCount(list.pages.length);
      } else if (shape === 'pages' && count <= FIX_LISTED && !said.includes('share')) {
        // Pages that hold the links, none of them shared by many: each page the check counts is a page to edit.
        await expect(edited).toHaveCount(count);
        expect(said.endsWith(count === 1 ? ' on 1 page' : ` on ${number(count)} pages`), `${id}: ${said}`).toBe(true);
      }
      followed++;
      continue;
    }
    // Where it is: the causes and pages it lists add up to the check's count.
    const where = main(page).getByRole('region', { name: 'Where it is' });
    if (!check.siteWide && !check.reportedOnce) {
      const pagesWord = (check.subject ?? 'page') === 'page' ? 'page' : 'address';
      await expect(where.locator('.text-text-muted').first()).toContainText(number(count));
      await expect(where.locator('.text-text-muted').first()).toContainText(pagesWord.slice(0, 4));
    }
    await expect(where.getByRole('table')).toBeVisible();
    followed++;
  }
  report(`Sweep of ${crawl.site ?? crawl.fileName}: ${followed} figures followed, ${problems.length} problems walked`);
});

test('J and K move through the problems on What to fix, and Enter opens one', async ({ page }) => {
  const { crawl, found, byId } = await facts();
  test.skip(found('error') + found('warning') + found('notice') < 2, 'Fewer than two problems');
  await page.goto(`/crawls/${crawl.id}/issues`);
  await expect(main(page).getByRole('heading', { level: 1, name: 'What to fix' })).toBeVisible();
  await expect(main(page).locator('.si-plain-row').first()).toBeVisible();
  await page.keyboard.press('j');
  const first = main(page).locator('a.si-plain-row').first();
  await expect(first).toBeFocused();
  await page.keyboard.press('j');
  await expect(main(page).locator('a.si-plain-row').nth(1)).toBeFocused();
  await page.keyboard.press('k');
  await expect(first).toBeFocused();
  const href = await first.getAttribute('href');
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(new RegExp(`${href}$`));
  // It pinned a region "Where it is" on whichever problem is first. Changed in the UX audit of 2026-11-19 (S-02 to
  // S-05, S-07): a problem that is a link has "Where to fix it" in its place.
  const opened = byId.get((href ?? '').split('/').pop() ?? '');
  await expect(
    main(page).getByRole('region', { name: opened && fixShape(opened) ? 'Where to fix it' : 'Where it is' }),
  ).toBeVisible();
  await expectNoViolations(page);
  await page.setViewportSize({ width: 1280, height: 720 });
  await shoot(page, 'problem-light');
  await page.emulateMedia({ colorScheme: 'dark' });
  await expectNoViolations(page);
  await shoot(page, 'problem-dark');
  await page.setViewportSize({ width: 640, height: 360 });
  await shoot(page, 'problem-200');
});

test('What to fix passes axe in both themes and at 200%', async ({ page }) => {
  const { crawl, analysis } = await facts();
  await page.setViewportSize({ width: 1280, height: 720 });
  let started = Date.now();
  await page.goto(`/crawls/${crawl.id}/issues`);
  await expect(main(page).getByRole('heading', { level: 1, name: 'What to fix' })).toBeVisible();
  await expect(main(page).locator('.si-plain-row').first()).toBeVisible();
  const fixPaint = Date.now() - started;
  const first = analysis.fixFirst[0];
  if (first) {
    started = Date.now();
    await page.goto(`/crawls/${crawl.id}/issues/${first}`);
    await expect(main(page).getByRole('region', { name: 'Where it is' }).locator('tbody tr').first()).toBeVisible();
    report(
      `First paint with data: What to fix ${fixPaint} ms, the first problem’s Where it is ${Date.now() - started} ms`,
    );
    await page.goto(`/crawls/${crawl.id}/issues`);
    await expect(main(page).locator('.si-plain-row').first()).toBeVisible();
  }
  await expectNoViolations(page);
  await shoot(page, 'fix-light');
  await page.emulateMedia({ colorScheme: 'dark' });
  await expectNoViolations(page);
  await shoot(page, 'fix-dark');
  await page.setViewportSize({ width: 640, height: 360 });
  await expectNoViolations(page);
  await shoot(page, 'fix-200');
});

test('switching mode on the Summary shows Overview within the budget, and back', async ({ page }) => {
  const { crawl } = await facts();
  await openSummary(page, crawl.id);
  // Let the other mode's chunk preload, as the app does once it's idle.
  await page.waitForTimeout(1_500);
  const times: number[] = [];
  for (const target of ['Overview', crawl.site ?? crawl.fileName, 'Overview', crawl.site ?? crawl.fileName]) {
    const elapsed = await page.evaluate(async (heading) => {
      const started = performance.now();
      document.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'A', code: 'KeyA', ctrlKey: true, shiftKey: true, bubbles: true }),
      );
      for (;;) {
        await new Promise((resolve) => requestAnimationFrame(resolve));
        const shown = document.querySelector('main h1')?.textContent ?? '';
        if (shown.startsWith(heading)) return performance.now() - started;
        if (performance.now() - started > 5_000) return -1;
      }
    }, target);
    times.push(Math.round(elapsed));
  }
  report(`Mode switch on the Summary to a visible screen: ${times.join(', ')} ms`);
  expect(times.every((time) => time >= 0)).toBe(true);
});

test('a title or description too long says where Google will likely cut it, one too short its characters, and never pixels', async ({
  page,
}) => {
  const { crawl, counts } = await facts();
  // It pinned, for one too long, "Google will likely cut this title off after “…”" on every page listed. Changed in
  // the UX audit of 2026-11-19 (S-13): the whole text in quotes, the part Google cuts in grey (a span the column's
  // heading explains), then its length and how far over it is. One too short says "Only 16 characters long", as it did.
  const lines: [string, RegExp, 'title' | 'description' | null][] = [
    ['title-too-long', /^“.+”[\d,]+ characters: Google will likely cut off about the last [\d,]+\.$/, 'title'],
    [
      'description-too-long',
      /^“.+”[\d,]+ characters: Google will likely cut off about the last [\d,]+\.$/,
      'description',
    ],
    ['title-too-short', /^Only [\d,]+ characters? long$/, null],
    // A short description is quoted under its length: it is short enough to show whole.
    ['description-too-short', /^Only [\d,]+ characters? long\s*Search description: “.+”$/, null],
  ];
  let shown = 0;
  for (const [id, line, cut] of lines) {
    const count = counts.get(id) ?? 0;
    if (count === 0) continue;
    await page.goto(`/crawls/${crawl.id}/issues/${id}`);
    const where = main(page).getByRole('region', { name: 'Where it is' });
    await expect(where.getByRole('table')).toBeVisible();
    // Every page listed says it plainly, in place of the engine's "612 px, over 580 (64 characters)".
    await expect(where.getByRole('cell').filter({ hasText: line })).toHaveCount(Math.min(count, 25));
    if (cut) {
      await expect(
        where.getByRole('columnheader', {
          name: `Its ${cut}: the words in grey are the ones Google will likely cut off`,
          exact: true,
        }),
      ).toBeVisible();
      await expect(where).not.toContainText('Google will likely cut this');
      // The grey part is what the line counts: "about 20 more" is the 20 characters in grey.
      const cuts = await where.locator('tbody tr').evaluateAll((rows) =>
        rows.map((row) => {
          const parts = row.querySelectorAll('.si-cut-text');
          const grey = (parts[0]?.textContent ?? '').trim();
          const said = /about the last ([\d,]+)\./.exec(row.textContent)?.[1] ?? '';
          return {
            parts: parts.length,
            grey: Array.from(new Intl.Segmenter(undefined, { granularity: 'grapheme' }).segment(grey)).length,
            said: Number(said.replace(/,/g, '')),
          };
        }),
      );
      expect(cuts).toHaveLength(Math.min(count, 25));
      expect(cuts.filter((each) => each.parts !== 1 || each.grey === 0 || each.grey !== each.said)).toEqual([]);
    }
    await expect(main(page)).not.toContainText(/\bpx\b/);
    shown++;
  }
  report(`Length checks said plainly on ${crawl.site ?? crawl.fileName}: ${shown} of 4 had pages`);
});
