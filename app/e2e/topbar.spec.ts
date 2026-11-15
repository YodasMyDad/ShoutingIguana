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
/** The tests' own crawl, ended: taken before the running one starts, which has the same site's name. */
let idle = '';

test.beforeAll(async () => {
  seeded = await libraryIds();
  idle = (await seededCrawl()).id;
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
  await expect(bar.getByRole('button', { name: /^Search or run a command/ })).toBeVisible();
  await expect(bar.getByRole('radio', { name: 'Simple' })).toBeVisible();
  await expect(bar.getByRole('radio', { name: 'Advanced' })).toBeVisible();
  if (live) {
    await expect(bar.getByRole('button', { name: 'Pause', exact: true })).toBeVisible();
    await expect(bar.getByRole('button', { name: 'Stop', exact: true })).toBeVisible();
    await expect(bar.getByText(/^(Running|Checking)/)).toBeVisible();
  }
}

for (const mode of ['simple', 'advanced'] as const) {
  for (const live of [false, true]) {
    for (const size of SIZES) {
      test(`${mode}, ${live ? 'a crawl running' : 'idle'}, at ${size.name}: nothing overprints, nothing that stays goes`, async ({
        page,
      }) => {
        const crawl = live ? running : idle;
        await openIn(page, mode, `/crawls/${crawl}/overview`, size);
        await expectNeverGone(page, live);
        expectLaidOut(await barLayout(page));
        if (size.name === '200pct') await expectNoViolations(page);
      });
    }
  }
}

/**
 * What a sweep checks at each width: the bar holds its parts, and the crumbs theirs; the switch is whole and in the
 * window; and no text the bar shows is cut to a letter or two (a shortened part keeps 40 px at least).
 */
async function sweepState(page: Page) {
  return page.evaluate(() => {
    const bar = document.querySelector<HTMLElement>('header[data-mode]');
    if (!bar) throw new Error('no top bar');
    const box = bar.getBoundingClientRect();
    const radios = [...bar.querySelectorAll<HTMLElement>('[role="radiogroup"] [role="radio"]')].map((radio) => {
      const rect = radio.getBoundingClientRect();
      return {
        name: radio.textContent,
        whole: radio.scrollWidth <= radio.clientWidth + 0.5,
        inside: rect.left >= box.left - 0.5 && rect.right <= Math.min(box.right, window.innerWidth) + 0.5,
        shown: rect.width > 1,
      };
    });
    const cut = [...bar.querySelectorAll<HTMLElement>('*')]
      .filter((element) => {
        const own = [...element.childNodes].some((node) => node.nodeType === 3 && (node.textContent ?? '').trim());
        if (!own) return false;
        const rect = element.getBoundingClientRect();
        // Shown (not read-only), and narrower than its words.
        return rect.width > 1 && element.scrollWidth > element.clientWidth + 1 && element.clientWidth < 40;
      })
      .map((element) => `${element.textContent}: ${element.clientWidth}px`);
    const crumbs = bar.querySelector('nav');
    return {
      step: Number(bar.dataset.give ?? 0),
      width: Math.round(box.width),
      overflow: bar.scrollWidth > bar.clientWidth,
      clipped: crumbs ? crumbs.scrollWidth > crumbs.clientWidth : false,
      radios,
      cut,
    };
  });
}

for (const zoom of [1, 2] as const) {
  test(`the steps, every 10 px from 1440 to 400, in both modes, idle and running, at ${zoom * 100}%`, async ({
    browser,
  }) => {
    test.setTimeout(300_000);
    const context = await browser.newContext({ deviceScaleFactor: zoom });
    const page = await context.newPage();
    const table: Record<string, Record<string, number>> = {};
    for (const mode of ['simple', 'advanced'] as const) {
      for (const live of [false, true]) {
        await openIn(page, mode, `/crawls/${live ? running : idle}/overview`, { width: 1440, height: 720 });
        await expectNeverGone(page, live);
        const firstAt: Record<string, number> = {};
        for (let width = 1440; width >= 400; width -= 10) {
          await page.setViewportSize({ width, height: 720 });
          await page.evaluate(
            () => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve))),
          );
          const state = await sweepState(page);
          const at = `${mode}, ${live ? 'running' : 'idle'}, ${width} px`;
          expect(state.overflow, `${at}: the bar holds its parts`).toBe(false);
          expect(state.clipped, `${at}: the crumbs hold theirs`).toBe(false);
          expect(state.radios, `${at}: the switch`).toEqual([
            { name: 'Simple', whole: true, inside: true, shown: true },
            { name: 'Advanced', whole: true, inside: true, shown: true },
          ]);
          expect(state.cut, `${at}: nothing cut to a letter`).toEqual([]);
          for (let step = 1; step <= state.step; step++) firstAt[`step ${step}`] ??= state.width;
        }
        table[`${mode}, ${live ? 'running' : 'idle'}`] = firstAt;
      }
    }
    await context.close();
    mkdirSync(SHOTS, { recursive: true });
    writeFileSync(path.join(SHOTS, `give-way-${zoom * 100}.json`), JSON.stringify(table, null, 2));
    console.warn(`top bar widths each step first applies at, ${zoom * 100}%: ${JSON.stringify(table)}`);
  });
}

// The gallery's G1 again, in the release shell (round 5's polish): under reduced motion every property change was a
// 0.01 ms transition, so the measure read each part at its old width and the bar flipped between two steps every frame,
// the search field's "Ctrl K" left poking out under the switch. Watched frame by frame, at widths either side of the
// search field's step, with the crawl's count moving: one step, held, and the field's keys go with the field.
test('under reduced motion the bar settles on one step, and the search field’s keys go with it', async ({ browser }) => {
  test.setTimeout(120_000);
  const context = await browser.newContext({ reducedMotion: 'reduce' });
  const page = await context.newPage();
  await openIn(page, 'advanced', `/crawls/${running}/overview`, { width: 1440, height: 720 });
  for (const width of [1440, 1280, 1180, 1100, 1024, 960]) {
    await page.setViewportSize({ width, height: 720 });
    await page.waitForTimeout(300);
    const frames = await page.evaluate(
      () =>
        new Promise<{ steps: number[]; bad: string[] }>((resolve) => {
          const bar = document.querySelector<HTMLElement>('header[data-mode]');
          const search = [...(bar?.querySelectorAll('button') ?? [])].find((button) =>
            button.textContent.includes('Search or run a command'),
          );
          const steps: number[] = [];
          const bad: string[] = [];
          let left = 40;
          const frame = () => {
            if (!bar) return resolve({ steps, bad: ['no bar'] });
            steps.push(Number(bar.dataset.give ?? 0));
            const keys = search?.querySelector('kbd')?.getBoundingClientRect();
            const field = search?.getBoundingClientRect();
            if (bar.scrollWidth > bar.clientWidth) bad.push(`overflow ${bar.scrollWidth - bar.clientWidth}`);
            if (field && keys && keys.width > 0 && keys.right > field.right + 0.5)
              bad.push(`the keys run ${Math.round(keys.right - field.right)} px out of the field`);
            if (--left > 0) requestAnimationFrame(frame);
            else resolve({ steps, bad });
          };
          requestAnimationFrame(frame);
        }),
    );
    expect(new Set(frames.steps).size, `${width} px: the steps seen, frame by frame: ${frames.steps.join(',')}`).toBe(
      1,
    );
    expect(frames.bad, `${width} px`).toEqual([]);
  }
  await context.close();
});

test('at the bar’s narrowest, the crawl’s controls and search are in More, each with its key', async ({ page }) => {
  await openIn(page, 'advanced', `/crawls/${running}/overview`, { width: 470, height: 720 });
  await page.evaluate(() => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  const bar = page.getByRole('banner');
  const more = bar.getByRole('button', { name: 'More: Pause, Stop, Search' });
  await expect(more).toBeVisible();
  await expect(bar.getByRole('button', { name: 'Pause', exact: true })).toBeHidden();
  // Home and Forward have given way, and are still there for a screen reader (and shown when they have focus).
  await expect(bar.getByRole('button', { name: 'Forward' })).toHaveCount(1);
  await expect(bar.getByRole('link', { name: 'Home' })).toHaveCount(1);
  await bar.getByRole('button', { name: 'Forward' }).focus();
  expect(
    await bar.getByRole('button', { name: 'Forward' }).evaluate((element) => element.getBoundingClientRect().width),
  ).toBeGreaterThan(1);
  await more.click();
  const menu = page.getByRole('menu');
  await expect(menu.getByRole('menuitem', { name: 'Pause' })).toHaveAttribute('aria-keyshortcuts', 'F7');
  await expect(menu.getByRole('menuitem', { name: 'Stop' })).toHaveAttribute('aria-keyshortcuts', 'F8');
  await menu.getByRole('menuitem', { name: 'Search or run a command' }).click();
  await expect(page.getByRole('dialog', { name: 'Command palette' })).toBeVisible();
  await expectNoViolations(page);
});

test('the screenshots: this slice’s screens, at 1280x720 in both themes and at 200% zoom', async ({ page }) => {
  test.setTimeout(240_000);
  mkdirSync(SHOTS, { recursive: true });
  const crawl = idle;
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
