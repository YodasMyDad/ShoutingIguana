// The top bar's order of giving way (docs/v2-ui-simple.md, Top bar), measured on the real app: in both modes, idle and
// with a crawl running, at 1280x720, 900x720 and 200% zoom (640x360), and across every width between. Nothing in the
// bar overprints, runs out of it or shrinks to a letter, and what never gives way is always there. Also writes the
// screenshots of this slice's screens to test-results/round3-ui/ (1280x720 in both themes, and 200% zoom).
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { libraryIds, removeCrawlsNotIn } from './startedCrawls';
import { colourTweensEnd, env, expectNoViolations, read, seededCrawl } from './support';

type Mode = 'simple' | 'advanced';

const SHOTS = path.resolve(import.meta.dirname, '..', 'test-results', 'round3-ui');
const SIZES = [
  { name: '1280', width: 1280, height: 720 },
  { name: '900', width: 900, height: 720 },
  { name: '200pct', width: 640, height: 360 },
] as const;

let seeded: string[] = [];
let running = '';

test.beforeAll(async () => {
  seeded = await libraryIds();
  // A crawl of the engine's synthetic site, slow enough to stay running for the whole spec.
  const started = await read<{ id: string }>('/api/crawls', {
    options: { startUrl: env('SI_E2E_TESTSITE'), concurrency: 1, maxRequestsPerSecond: 1 },
  });
  running = started.id;
});
test.afterAll(() => removeCrawlsNotIn(seeded));

async function openIn(page: Page, mode: Mode, route: string, size: { width: number; height: number }) {
  await page.setViewportSize(size);
  await page.addInitScript((stored) => localStorage.setItem('si.settings', stored), JSON.stringify({ mode }));
  await page.goto(route);
  await expect(page.getByRole('banner').getByRole('radiogroup', { name: 'Mode' })).toBeVisible();
}

/** The bar's parts, each as laid out: none may overlap the next or run out of the bar. */
async function barLayout(page: Page) {
  return page.evaluate(() => {
    const bar = document.querySelector<HTMLElement>('header[data-mode]');
    if (!bar) throw new Error('no top bar');
    const box = bar.getBoundingClientRect();
    const parts = [...bar.children]
      .map((child) => ({ child, rect: child.getBoundingClientRect() }))
      .filter(({ rect }) => rect.width > 0)
      .map(({ child, rect }) => ({ name: child.tagName.toLowerCase(), left: rect.left, right: rect.right }));
    const crumbs = bar.querySelector('nav');
    return {
      step: Number(bar.dataset.give ?? 0),
      width: Math.round(box.width),
      left: box.left,
      right: box.right,
      parts,
      clipped: crumbs ? crumbs.scrollWidth > crumbs.clientWidth : false,
      overflow: bar.scrollWidth > bar.clientWidth,
    };
  });
}

function expectLaidOut(layout: Awaited<ReturnType<typeof barLayout>>) {
  expect(layout.overflow, 'the bar holds all its parts').toBe(false);
  expect(layout.clipped, 'the crumbs hold all theirs').toBe(false);
  for (const [index, part] of layout.parts.entries()) {
    expect(part.left).toBeGreaterThanOrEqual(layout.left - 0.5);
    expect(part.right).toBeLessThanOrEqual(layout.right + 0.5);
    const next = layout.parts[index + 1];
    if (next) expect(part.right, `${part.name} runs into ${next.name}`).toBeLessThanOrEqual(next.left + 0.5);
  }
}

/** What never gives way: Back, the section, the status word, the controls, search and the switch. */
async function expectNeverGone(page: Page, live: boolean) {
  const bar = page.getByRole('banner');
  await expect(bar.getByRole('button', { name: 'Back' })).toBeVisible();
  await expect(bar.getByRole('navigation', { name: 'Breadcrumb' }).locator('[aria-current="page"]')).toBeVisible();
  await expect(bar.getByRole('button', { name: 'Search or run a command' })).toBeVisible();
  await expect(bar.getByRole('radio', { name: 'Simple' })).toBeVisible();
  await expect(bar.getByRole('radio', { name: 'Advanced' })).toBeVisible();
  if (live) {
    await expect(bar.getByRole('button', { name: 'Pause' })).toBeVisible();
    await expect(bar.getByRole('button', { name: 'Stop' })).toBeVisible();
    await expect(bar.getByText(/^(Running|Checking)/)).toBeVisible();
  }
}

for (const mode of ['simple', 'advanced'] as const) {
  for (const live of [false, true]) {
    for (const size of SIZES) {
      test(`${mode}, ${live ? 'a crawl running' : 'idle'}, at ${size.name}: nothing overprints, nothing that stays goes`, async ({
        page,
      }) => {
        const crawl = live ? running : (await seededCrawl()).id;
        await openIn(page, mode, `/crawls/${crawl}/overview`, size);
        await expectNeverGone(page, live);
        expectLaidOut(await barLayout(page));
        if (size.name === '200pct') await expectNoViolations(page);
      });
    }
  }
}

test('the steps, as the bar narrows, in each mode with a crawl running: the widths each part gives way at', async ({
  page,
}) => {
  test.setTimeout(240_000);
  const table: Record<string, Record<string, number>> = {};
  for (const mode of ['simple', 'advanced'] as const) {
    await openIn(page, mode, `/crawls/${running}/overview`, { width: 1700, height: 720 });
    await expectNeverGone(page, true);
    const firstAt: Record<string, number> = {};
    for (let width = 1700; width >= 600; width -= 10) {
      await page.setViewportSize({ width, height: 720 });
      await page.evaluate(() => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve))));
      const layout = await barLayout(page);
      expectLaidOut(layout);
      for (let step = 1; step <= layout.step; step++) firstAt[`step ${step}`] ??= layout.width;
    }
    table[mode] = firstAt;
  }
  mkdirSync(SHOTS, { recursive: true });
  writeFileSync(path.join(SHOTS, 'give-way.json'), JSON.stringify(table, null, 2));
  console.warn(`top bar widths each step first applies at, with a crawl running: ${JSON.stringify(table)}`);
});

test('the screenshots: this slice’s screens, at 1280x720 in both themes and at 200% zoom', async ({ page }) => {
  test.setTimeout(240_000);
  mkdirSync(SHOTS, { recursive: true });
  const crawl = (await seededCrawl()).id;
  const screens: [name: string, route: string, mode: Mode][] = [
    ['home-simple', '/', 'simple'],
    ['new-simple', `/new?url=${encodeURIComponent('https://www.example.co.uk/')}`, 'simple'],
    ['new-advanced', `/new?url=${encodeURIComponent('https://www.example.co.uk/')}&focus=headers`, 'advanced'],
    ['settings-simple', '/settings', 'simple'],
    ['settings-advanced', '/settings', 'advanced'],
    ['settings-crawl-defaults', '/settings/crawl-defaults', 'advanced'],
    ['crawl-settings', `/crawls/${crawl}/settings`, 'advanced'],
    ['overview-simple', `/crawls/${crawl}/overview`, 'simple'],
    ['overview-advanced', `/crawls/${crawl}/overview`, 'advanced'],
    ['running-simple', `/crawls/${running}/overview`, 'simple'],
    ['running-advanced', `/crawls/${running}/overview`, 'advanced'],
  ];
  for (const [name, route, mode] of screens) {
    for (const theme of ['light', 'dark'] as const) {
      await page.emulateMedia({ colorScheme: theme });
      await openIn(page, mode, route, { width: 1280, height: 720 });
      await page.waitForTimeout(600);
      await colourTweensEnd(page);
      await page.screenshot({ path: path.join(SHOTS, `${name}-1280x720-${theme}.png`) });
    }
    await page.emulateMedia({ colorScheme: 'light' });
    await openIn(page, mode, route, { width: 640, height: 360 });
    await page.waitForTimeout(600);
    await page.screenshot({ path: path.join(SHOTS, `${name}-200pct-light.png`) });
  }
});
