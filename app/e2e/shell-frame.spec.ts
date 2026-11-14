// The shell's frame (round 5, part D): the gallery's G1, G2 and G6 and the audit's gap 26, on the real app.
// - G1: the top bar never cuts the Simple | Advanced switch, also when its parts grow with nothing in the bar changing
//   (a font face or a style sheet arriving late, as in the real shell once a crawl started), at 1280, 1024 and 200%.
// - G2: no screen scrolls the whole window, at 1280x720 and 200% zoom, in both modes.
// - G6: the Report's subtitle is one or two lines at 1280 px, and its frame scrolls with the app's bars.
// - Gap 26: each screen names the window.
import { expect, test, type Page } from '@playwright/test';
import { libraryIds, removeCrawlsNotIn } from './startedCrawls';
import { env, read, seededCrawl, site } from './support';

type Mode = 'simple' | 'advanced';

let seeded: string[] = [];
let idle = '';
let running = '';
let check = '';

test.beforeAll(async () => {
  seeded = await libraryIds();
  idle = (await seededCrawl()).id;
  const analysis = await read<{ counts: { checkId: string; count: number }[] }>(`/api/crawls/${idle}/analysis`);
  check = analysis.counts.find((count) => count.count > 0)?.checkId ?? '';
  // A crawl of the engine's synthetic site, slow enough to stay running for the whole spec.
  running = (
    await read<{ id: string }>('/api/crawls', {
      options: { startUrl: env('SI_E2E_TESTSITE'), concurrency: 1, maxRequestsPerSecond: 1 },
    })
  ).id;
});
test.afterAll(() => removeCrawlsNotIn(seeded));

async function openIn(page: Page, mode: Mode, route: string, size: { width: number; height: number }) {
  await page.setViewportSize(size);
  await page.addInitScript((stored) => localStorage.setItem('si.settings', stored), JSON.stringify({ mode }));
  await page.goto(route);
  await expect(page.getByRole('banner').getByRole('radiogroup', { name: 'Mode' })).toBeVisible();
}

/** The switch, whole and inside the bar and the window; and the bar holding all its parts. */
async function switchState(page: Page) {
  return page.evaluate(() => {
    const bar = document.querySelector<HTMLElement>('header[data-mode]');
    if (!bar) throw new Error('no top bar');
    const box = bar.getBoundingClientRect();
    const group = bar.querySelector<HTMLElement>('[role="radiogroup"]');
    const rect = group?.getBoundingClientRect();
    return {
      step: Number(bar.dataset.give ?? 0),
      overflow: bar.scrollWidth - bar.clientWidth,
      inside: rect ? rect.left >= box.left - 0.5 && rect.right <= Math.min(box.right, window.innerWidth) + 0.5 : false,
    };
  });
}

const SIZES = [
  { name: '1280', width: 1280, height: 720 },
  { name: '1024', width: 1024, height: 720 },
  { name: '200%', width: 640, height: 360 },
] as const;

test.describe('G1: the switch is never cut', () => {
  for (const size of SIZES) {
    test(`Advanced, a crawl running, at ${size.name}: parts that grow late still give way`, async ({ page }) => {
      await openIn(page, 'advanced', `/crawls/${running}/urls`, size);
      await expect(page.getByRole('banner').getByRole('button', { name: 'Pause', exact: true })).toBeAttached();
      let state = await switchState(page);
      expect(state.overflow, 'the bar holds its parts').toBeLessThanOrEqual(0);
      expect(state.inside, 'the switch is whole').toBe(true);
      const before = state.step;
      // The parts grow with nothing in the bar changing: what a font face arriving late does. The bar must measure
      // again: before the fix it stayed where it was and the switch ran out of it.
      await page.addStyleTag({ content: 'header[data-mode] > * { letter-spacing: 0.12em; }' });
      await expect.poll(async () => (await switchState(page)).overflow).toBeLessThanOrEqual(0);
      state = await switchState(page);
      expect(state.inside, 'the switch is whole once the parts have grown').toBe(true);
      expect(state.step, 'more gave way').toBeGreaterThanOrEqual(before);
    });
  }

  test('Advanced at 1280: a crawl started from the app leaves the switch whole', async ({ page }) => {
    await openIn(page, 'advanced', '/', { width: 1280, height: 720 });
    const started = await read<{ id: string }>('/api/crawls', {
      options: { startUrl: env('SI_E2E_TESTSITE'), concurrency: 1, maxRequestsPerSecond: 1 },
    });
    await page.goto(`/crawls/${started.id}/overview`);
    await expect(page.getByRole('banner').getByText(/^Running/)).toBeVisible();
    await expect.poll(async () => (await switchState(page)).overflow).toBeLessThanOrEqual(0);
    expect((await switchState(page)).inside).toBe(true);
  });
});

/** The whole window's scroll: none, ever. */
async function windowScroll(page: Page) {
  return page.evaluate(() => {
    const scrolling = document.scrollingElement ?? document.documentElement;
    return { down: scrolling.scrollHeight - window.innerHeight, across: scrolling.scrollWidth - window.innerWidth };
  });
}

function routes(mode: Mode): { name: string; path: string; ready: (page: Page) => Promise<void> }[] {
  const main = (page: Page) => expect(page.locator('#main')).not.toBeEmpty();
  const crawl = `/crawls/${idle}`;
  const shared = [
    { name: 'Home', path: '/', ready: main },
    { name: 'New', path: '/new', ready: main },
    { name: 'Settings', path: '/settings', ready: main },
    { name: 'Settings › About', path: '/settings/about', ready: main },
    { name: 'Overview', path: `${crawl}/overview`, ready: main },
    { name: 'Issues', path: `${crawl}/issues`, ready: main },
    { name: 'A check', path: `${crawl}/issues/${check}`, ready: main },
    { name: 'Report', path: `${crawl}/report`, ready: main },
  ];
  if (mode === 'simple') return shared;
  return [
    ...shared,
    { name: 'Settings › Updates', path: '/settings/updates', ready: main },
    { name: 'URLs', path: `${crawl}/urls`, ready: main },
    { name: 'Links', path: `${crawl}/links`, ready: main },
    { name: 'Sitemaps', path: `${crawl}/sitemaps`, ready: main },
    { name: 'robots.txt', path: `${crawl}/robots`, ready: main },
    { name: 'Crawl settings', path: `${crawl}/settings`, ready: main },
  ];
}

test.describe('G2: no screen scrolls the window', () => {
  for (const mode of ['simple', 'advanced'] as const) {
    for (const size of [
      { name: '1280x720', width: 1280, height: 720 },
      { name: '200%', width: 640, height: 360 },
    ]) {
      test(`${mode} at ${size.name}`, async ({ page }) => {
        test.setTimeout(120_000);
        await page.setViewportSize(size);
        await page.addInitScript((stored) => localStorage.setItem('si.settings', stored), JSON.stringify({ mode }));
        const scrolled: string[] = [];
        for (const route of routes(mode)) {
          await page.goto(route.path);
          await route.ready(page);
          // Let the screen settle: what loads late can still push the window. (Never 'networkidle': the event stream
          // stays open.)
          await expect(page.locator('[aria-busy="true"]')).toHaveCount(0);
          await page.waitForTimeout(400);
          const scroll = await windowScroll(page);
          if (scroll.down > 0 || scroll.across > 0) scrolled.push(`${route.name}: ${JSON.stringify(scroll)}`);
          // The wheel over the sidebar moves nothing.
          await page.mouse.move(20, size.height / 2);
          await page.mouse.wheel(0, 600);
          const offset = await page.evaluate(() => (document.scrollingElement ?? document.documentElement).scrollTop);
          if (offset !== 0) scrolled.push(`${route.name}: the wheel scrolled the window ${offset}px`);
        }
        expect(scrolled).toEqual([]);
      });
    }
  }
});

test.describe('G6: the Report', () => {
  for (const mode of ['simple', 'advanced'] as const) {
    test(`${mode} at 1280: the subtitle is one or two lines, and the frame has the app's scroll bars`, async ({
      page,
    }) => {
      await openIn(page, mode, `/crawls/${idle}/report`, { width: 1280, height: 720 });
      const frame = page.locator('iframe[title^="The report for"]');
      await expect(frame).toBeVisible();
      const subtitle = page.locator('#main header p').first();
      await expect(subtitle).toContainText(site());
      const lines = await subtitle.evaluate((element) => {
        const lineHeight = Number.parseFloat(getComputedStyle(element).lineHeight);
        return Math.round(element.getBoundingClientRect().height / lineHeight);
      });
      expect(lines).toBeGreaterThanOrEqual(1);
      expect(lines).toBeLessThanOrEqual(2);
      const bars = await frame.evaluate((element: HTMLIFrameElement) => {
        const style = element.contentDocument?.getElementById('si-app-scroll-bars');
        return style?.textContent ?? '';
      });
      expect(bars).toContain('::-webkit-scrollbar');
    });
  }
});

test.describe('Gap 26: each screen names the window', () => {
  test('Advanced', async ({ page }) => {
    await openIn(page, 'advanced', `/crawls/${idle}/issues`, { width: 1280, height: 720 });
    await expect(page).toHaveTitle(`Issues · ${site()} · Shouting Iguana`);
    await page.goto(`/crawls/${idle}/urls`);
    await expect(page).toHaveTitle(`URLs · ${site()} · Shouting Iguana`);
    await page.goto('/new');
    await expect(page).toHaveTitle('New crawl · Shouting Iguana');
    await page.goto('/');
    await expect(page).toHaveTitle('Home · Shouting Iguana');
  });

  test('Simple', async ({ page }) => {
    await openIn(page, 'simple', `/crawls/${idle}/issues`, { width: 1280, height: 720 });
    await expect(page).toHaveTitle(`What to fix · ${site()} · Shouting Iguana`);
    await page.goto(`/crawls/${idle}/overview`);
    await expect(page).toHaveTitle(`Summary · ${site()} · Shouting Iguana`);
    await page.goto('/new');
    await expect(page).toHaveTitle('Check a website · Shouting Iguana');
  });
});
