// Home and checking a site in Simple (docs/v2-ui-simple.md, Home; While it checks the site; The report at the end),
// against the real sidecar and the engine's synthetic site (never a real site). Every figure of a row lands on itself:
// the score is the analysis's, the problems are What to fix's count, the pages are the crawl's. A check started from
// Home is watched through its three steps to the Summary, with the finished toast. Run on a dev crawl's copy with
// SI_E2E_CRAWL (garden, the 100k synthetic crawl): on garden the row also has the mockup's numbers (85, Good, 2
// serious, 4 worth fixing, 265 pages). Writes this slice's screenshots to test-results/r4-home/.
import path from 'node:path';
import { expect, test, type Locator, type Page } from '@playwright/test';
import type { Analysis, CrawlListItem } from '../src/api/types';
import { libraryIds, removeCrawlsNotIn } from './startedCrawls';
import { colourTweensEnd, countOf, env, expectNoViolations, read, seededCrawl, sidecar, site } from './support';

const SHOTS = path.resolve(import.meta.dirname, '..', 'test-results', 'r4-home');
const BANDS = { veryGood: 'Very good', good: 'Good', needsWork: 'Needs work', poor: 'Poor' } as const;

// A first start: nothing stored, so Simple.
test.use({ storageState: { cookies: [], origins: [] } });

let seeded: string[] = [];
test.beforeAll(async () => {
  seeded = await libraryIds();
});
test.afterEach(async () => {
  await removeCrawlsNotIn(seeded);
  await sidecar('/api/crawl-options/defaults', { method: 'PUT', body: JSON.stringify({}) });
});

const main = (page: Page) => page.locator('#main');
const sites = (page: Page) => main(page).getByRole('list', { name: /^Your sites/ });
const rowOf = (page: Page, name: string): Locator =>
  sites(page)
    .getByRole('listitem')
    .filter({ has: page.getByRole('link', { name, exact: true }) })
    .first();

async function shoot(page: Page, name: string) {
  await colourTweensEnd(page);
  await page.screenshot({ path: path.join(SHOTS, `${name}.png`), fullPage: true });
}

test('each row of Your sites lands on itself: its score, its problems and its pages', async ({ page }) => {
  const seededItem = await seededCrawl();
  const item = await read<CrawlListItem>(`/api/crawls/${seededItem.id}`);
  const analysis = await read<Analysis>(`/api/crawls/${item.id}/analysis`);
  expect(analysis.score, 'the seeded crawl is scored by the current engine').toBeTruthy();
  const score = analysis.score!;

  const started = Date.now();
  await page.goto('/');
  const row = rowOf(page, site());
  await expect(row).toBeVisible();
  test
    .info()
    .annotations.push({ type: 'Home first paint with its data (ms)', description: String(Date.now() - started) });

  // The score is the analysis's own, with its word; the library's brief of it agrees.
  expect(item.score?.value).toBe(score.value);
  const word = `${BANDS[score.band]}${score.partial ? ' so far' : ''}`;
  await expect(
    row.getByRole('img', { name: `Score ${score.value} out of 100${score.partial ? ' so far' : ''}, ${word}` }),
  ).toBeVisible();
  // The problems: Serious and Worth fixing, never the issue count.
  const serious = item.issues?.error ?? 0;
  const worthFixing = item.issues?.warning ?? 0;
  if (serious + worthFixing === 0) {
    await expect(row.getByText('Nothing to fix')).toBeVisible();
  } else {
    const words = [
      serious ? `${serious.toLocaleString('en-GB')} serious` : '',
      worthFixing ? `${worthFixing.toLocaleString('en-GB')} worth fixing` : '',
    ]
      .filter(Boolean)
      .join(', ');
    await expect(row.getByRole('img', { name: words })).toBeVisible();
  }
  await expect(
    row.getByText(`${item.pageCount.toLocaleString('en-GB')} ${item.pageCount === 1 ? 'page' : 'pages'}`, {
      exact: false,
    }),
  ).toBeVisible();

  // On the garden crawl, the mockup's numbers.
  if (site() === 'gardenfurnituresets.co.uk') {
    expect([score.value, score.band, serious, worthFixing, item.pageCount]).toEqual([85, 'good', 2, 4, 265]);
  }

  await expectNoViolations(page);
  await shoot(page, 'home-1280-light');

  // The row opens the check: its Summary, where What to fix counts the same problems and the pages read are the same.
  await row.getByRole('link', { name: site(), exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/crawls/${item.id}/overview$`));
  const whatToFix = page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: /^What to fix/ });
  if (serious + worthFixing > 0) {
    await expect(whatToFix).toHaveAccessibleName(new RegExp(`\\b${serious + worthFixing}\\b`));
  }
  // Home's pages are the library's, as Overview's "pages read" and Advanced's Home say them. The analysis's own count,
  // which the score covers, is said beside it: the engine has given them apart (reported).
  test.info().annotations.push({
    type: 'pages',
    description: `library pageCount ${item.pageCount}, analysis pagesAnalyzed ${analysis.pagesAnalyzed}, score covers ${score.covers.pages}`,
  });
});

test('Home passes axe in the dark theme and in forced colours, and nothing scrolls sideways at 200% zoom', async ({
  page,
}) => {
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.goto('/');
  await expect(rowOf(page, site())).toBeVisible();
  await expectNoViolations(page);
  await shoot(page, 'home-1280-dark');
  await page.emulateMedia({ colorScheme: 'light', forcedColors: 'active' });
  await expectNoViolations(page);
  await page.emulateMedia({ forcedColors: 'none' });

  await page.setViewportSize({ width: 640, height: 360 });
  await expect(rowOf(page, site())).toBeVisible();
  const overflow = await page.evaluate(() => {
    const main = document.getElementById('main');
    return {
      page: document.scrollingElement!.scrollWidth - document.scrollingElement!.clientWidth,
      main: main ? main.scrollWidth - main.clientWidth : 0,
    };
  });
  expect(overflow).toEqual({ page: 0, main: 0 });
  await expectNoViolations(page);
  await rowOf(page, site()).scrollIntoViewIfNeeded();
  await shoot(page, 'home-200pct-light');
});

test('a check started from Home with Enter goes through its three steps to the Summary, with the finished toast', async ({
  page,
}) => {
  test.setTimeout(240_000);
  // Saved defaults: Fast, and a limit, so the check ends soon; Home's line says so.
  await sidecar('/api/crawl-options/defaults', {
    method: 'PUT',
    body: JSON.stringify({ concurrency: 16, maxRequestsPerSecond: 0, maxUrls: 1_500 }),
  });
  await page.goto('/');
  await expect(main(page).getByText('Uses your saved settings: Fast speed and up to 1,500 addresses.')).toBeVisible();
  const address = main(page).getByRole('textbox', { name: 'Web address' });
  await address.fill(env('SI_E2E_TESTSITE'));
  await address.press('Enter');
  await expect(page).toHaveURL(/\/crawls\/[0-9a-f]{16}\/overview$/);
  const id = /\/crawls\/([0-9a-f]{16})\//.exec(page.url())?.[1] ?? '';
  await expect(main(page).getByRole('heading', { level: 1, name: /^Checking / })).toBeVisible();

  const steps = main(page).getByRole('list', { name: 'Steps' });
  await expect(steps.getByRole('listitem').first()).toHaveAttribute('aria-current', 'step');
  const figure = async (label: string) =>
    countOf(
      await main(page)
        .locator('.si-stat')
        .filter({ has: page.getByText(label, { exact: true }) })
        .locator('.si-stat__value')
        .innerText(),
    );
  await expect.poll(() => figure('Addresses checked')).toBeGreaterThan(0);

  // Paused: the figures stand still once the requests on their way have landed, and then each is the crawl's own.
  await page.keyboard.press('F7');
  await expect(main(page).getByText('Paused. Nothing is lost.')).toBeVisible();
  await expect(steps.getByRole('listitem').first()).toHaveText(/Paused/);
  let before = -1;
  await expect
    .poll(
      async () => {
        const now = await figure('Addresses checked');
        const still = now === before;
        before = now;
        return still;
      },
      { intervals: [2_000] },
    )
    .toBe(true);
  // Addresses checked is the URL grid's crawled count; still to check, the queue; pages read, the pages read.
  const crawled = await read<{ matching: number }>(`/api/crawls/${id}/urls`, {
    filters: [{ column: 'fetched', op: 'is', value: true }],
    sort: [],
    limit: 1,
  });
  expect(await figure('Addresses checked')).toBe(crawled.matching);
  const stats = await read<{ queuedCount: number; htmlPageCount: number }>(`/api/crawls/${id}/stats`);
  expect(await figure('Still to check')).toBe(stats.queuedCount);
  expect(await figure('Pages read')).toBe(stats.htmlPageCount);
  // The top bar's count is the same figure.
  await expect(
    page.getByRole('banner').getByText(`${(await figure('Addresses checked')).toLocaleString('en-GB')} checked`),
  ).toBeVisible();
  await expectNoViolations(page);
  await shoot(page, 'checking-paused-1280-light');
  await page.emulateMedia({ colorScheme: 'dark' });
  await expectNoViolations(page);
  await shoot(page, 'checking-paused-1280-dark');
  await page.setViewportSize({ width: 640, height: 360 });
  await shoot(page, 'checking-paused-200pct-dark');
  await page.setViewportSize({ width: 1280, height: 720 });
  await page.emulateMedia({ colorScheme: 'light' });

  await main(page)
    .getByRole('button', { name: /Resume/ })
    .click();
  await expect(main(page).getByText('Paused. Nothing is lost.')).toBeHidden();
  await expect(steps.getByRole('listitem').first()).toHaveAttribute('aria-current', 'step');

  // It ends, its results are worked out on the second step, and the screen becomes the Summary in place.
  await expect(main(page).getByRole('heading', { level: 1, name: /^Checking / })).toBeHidden({ timeout: 200_000 });
  await expect(page.getByRole('list', { name: 'Steps' })).toBeHidden({ timeout: 120_000 });
  await expect(page).toHaveURL(new RegExp(`/crawls/${id}/overview$`));
  // The Summary, part B's, in its place: focus that was on the checking screen (Pause) is on its title, and it has
  // its score and its Open the report.
  await expect(main(page).locator('h1.si-simple-title')).toBeFocused();
  await expect(main(page).getByRole('img', { name: /^Score \d+ out of 100/ })).toBeVisible({ timeout: 20_000 });
  await expect(main(page).getByRole('link', { name: 'Open the report' }).first()).toBeVisible();
  const item = await read<CrawlListItem>(`/api/crawls/${id}`);
  expect(item.score).toBeTruthy();
  const problems = (item.issues?.error ?? 0) + (item.issues?.warning ?? 0);
  const toFix = problems === 0 ? 'nothing to fix' : `${problems} ${problems === 1 ? 'problem' : 'problems'} to fix`;
  const toast = page.locator('.si-toast').filter({ hasText: `${item.site} is checked` });
  await expect(toast).toBeVisible();
  await expect(toast).toContainText(`Score ${item.score!.value}${item.score!.partial ? ' so far' : ''}: ${toFix}`);
  await expect(toast.getByRole('button', { name: 'See the summary' })).toBeVisible();
  // The toast's Open the report lands on the Report screen, part C's.
  await toast.getByRole('button', { name: 'Open the report' }).click();
  await expect(page).toHaveURL(new RegExp(`/crawls/${id}/report$`));
  await expect(page.frameLocator('iframe[title^="The report for"]').getByRole('heading', { level: 1 })).toBeVisible({
    timeout: 20_000,
  });
});

test('the finished toast in Advanced keeps its words', async ({ page }) => {
  test.setTimeout(180_000);
  await page.addInitScript(() => localStorage.setItem('si.settings', JSON.stringify({ mode: 'advanced' })));
  await page.goto('/');
  const started = await read<{ id: string }>('/api/crawls', {
    options: { startUrl: env('SI_E2E_TESTSITE'), concurrency: 16, maxRequestsPerSecond: 0, maxUrls: 300 },
  });
  await page.goto(`/crawls/${started.id}/overview`);
  const toast = page.locator('.si-toast').filter({ hasText: 'Crawl finished' });
  await expect(toast).toBeVisible({ timeout: 150_000 });
  await expect(toast.getByRole('button', { name: 'View issues' })).toBeVisible();
  await toast.getByRole('button', { name: 'Open the report' }).click();
  await expect(page).toHaveURL(new RegExp(`/crawls/${started.id}/report$`));
  await expect(page.frameLocator('iframe[title^="The report for"]').getByRole('heading', { level: 1 })).toBeVisible({
    timeout: 60_000,
  });
});

test('the switch on Home shows its other view within 100 ms', async ({ page }) => {
  await page.goto('/');
  await expect(rowOf(page, site())).toBeVisible();
  const modeSwitch = page.getByRole('banner').getByRole('radiogroup', { name: 'Mode' });
  const timings: number[] = [];
  for (let round = 0; round < 5; round++) {
    for (const [target, title] of [
      ['Advanced', 'Crawl a site'],
      ['Simple', 'Check a website'],
    ] as const) {
      const elapsed = await modeSwitch.getByRole('radio', { name: target }).evaluate(
        (element, heading) =>
          new Promise<number>((resolve) => {
            const started = performance.now();
            const done = () => document.querySelector('#main h1')?.textContent === heading;
            const observer = new MutationObserver(() => {
              if (!done()) return;
              observer.disconnect();
              requestAnimationFrame(() => resolve(performance.now() - started));
            });
            observer.observe(document.body, { subtree: true, childList: true, characterData: true });
            (element as HTMLElement).click();
          }),
        title,
      );
      timings.push(elapsed);
    }
  }
  timings.sort((a, b) => a - b);
  const median = timings[Math.floor(timings.length / 2)] ?? 0;
  test.info().annotations.push({
    type: 'Home switch-ms',
    description: `median ${median.toFixed(1)}, max ${(timings.at(-1) ?? 0).toFixed(1)}`,
  });
  expect(median).toBeLessThan(100);
});
