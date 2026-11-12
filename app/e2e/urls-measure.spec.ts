// Measures the URL grid against its budgets (docs/v2-ui-brief.md, Performance budgets): the first page of each preset
// and of ten filtered views (300 ms), a sort, a filter and a column changed to a visible response (100 ms), and the
// end of every row reached with memory flat; and what each preset shows at 1280x720. Only when asked, on a big crawl:
//   SI_E2E_MEASURE=1 SI_E2E_CRAWL=<the 100k crawl> npx playwright test e2e/urls-measure.spec.ts
// Each time is from the input to the frame after the condition holds, measured in the page.
import { expect, test, type Page } from '@playwright/test';
import { read, seededCrawl } from './support';

test.skip(!process.env.SI_E2E_MEASURE, 'Measures only when SI_E2E_MEASURE is set');

let crawl = '';
test.beforeAll(async () => {
  crawl = (await seededCrawl()).id;
});

const PRESETS: Record<string, string[]> = {
  Overview: [],
  Indexability: [
    'url',
    'statusCode',
    'indexability',
    'canonicalUrl',
    'noindex',
    'nofollow',
    'inSitemap',
    'metaRobots',
    'xRobotsTag',
    'redirectTarget',
  ],
  Content: ['url', 'title', 'titleLength', 'h1', 'h1Count', 'wordCount', 'description', 'descriptionLength'],
  Performance: ['url', 'statusCode', 'type', 'responseMs', 'ttfbMs', 'sizeBytes', 'contentType', 'depth'],
  Links: [
    'url',
    'statusCode',
    'depth',
    'inlinks',
    'followedInlinks',
    'nofollowInlinks',
    'linkingPages',
    'outlinks',
    'redirectTarget',
    'finalUrl',
    'finalStatusCode',
  ],
};

const VIEWS: [string, unknown[]][] = [
  ['4xx', [{ column: 'statusClass', op: 'in', values: ['4xx'] }]],
  ['3xx', [{ column: 'statusClass', op: 'in', values: ['3xx'] }]],
  ['pages', [{ column: 'type', op: 'in', values: ['page'] }]],
  ['depth 3', [{ column: 'depth', op: 'eq', value: 3 }]],
  ['slow', [{ column: 'responseMs', op: 'gte', value: 1000 }]],
  ['not indexable', [{ column: 'indexable', op: 'is', value: false }]],
  ['with errors', [{ column: 'errors', op: 'gte', value: 1 }]],
  ['no inlinks', [{ column: 'inlinks', op: 'eq', value: 0 }]],
  ['in no sitemap', [{ column: 'inSitemap', op: 'is', value: false }]],
  ['title contains', [{ column: 'title', op: 'contains', value: 'product' }]],
];

const lines: string[] = [];
const say = (line: string) => {
  lines.push(line);
  process.stdout.write(`${line}\n`);
};

/** Runs `act` in the page and waits, frame by frame, for `done`: the time from the act to the frame after. */
async function timed(page: Page, act: string, done: string): Promise<number> {
  return page.evaluate(
    async ({ act, done }) => {
      const check = new Function(`return (${done});`) as () => boolean;
      const start = performance.now();
      (new Function(act) as () => void)();
      for (let frames = 0; frames < 2000 && !check(); frames++) {
        await new Promise((resolve) => requestAnimationFrame(resolve));
      }
      await new Promise((resolve) => requestAnimationFrame(resolve));
      return performance.now() - start;
    },
    { act, done },
  );
}

const FIRST_ROW = `!!document.querySelector('[role="grid"][aria-label="URLs"] [role="row"][aria-rowindex="2"] [role="gridcell"]')`;

async function sidecarPage(body: unknown): Promise<number[]> {
  const times: number[] = [];
  for (let run = 0; run < 4; run++) {
    const start = performance.now();
    await read(`/api/crawls/${crawl}/urls`, body);
    times.push(Math.round(performance.now() - start));
  }
  return times;
}

test('the URL grid against its budgets on this crawl', async ({ page }) => {
  test.setTimeout(20 * 60_000);
  await page.setViewportSize({ width: 1280, height: 720 });
  await page.goto(`/crawls/${crawl}/urls`);
  await expect(page.getByRole('grid', { name: 'URLs' }).getByRole('row').nth(1)).toBeVisible({ timeout: 60_000 });

  // What each preset shows at 1280x720, at the default density.
  for (const [name, cols] of Object.entries(PRESETS)) {
    const columns = cols.length > 0 ? cols : undefined;
    await page.goto(`/crawls/${crawl}/urls${columns ? `?cols=${encodeURIComponent(JSON.stringify(columns))}` : ''}`);
    await expect(page.getByRole('grid', { name: 'URLs' }).getByRole('row').nth(1)).toBeVisible({ timeout: 60_000 });
    await page.waitForFunction(() => document.getAnimations().length === 0);
    const seen = await page.getByRole('grid', { name: 'URLs' }).evaluate((grid) => {
      const box = grid.getBoundingClientRect();
      const rows = [...grid.querySelectorAll('[role="row"]')].filter((row) => {
        const at = row.getBoundingClientRect();
        return !row.querySelector('[role="columnheader"]') && at.top >= box.top && at.bottom <= box.bottom - 10;
      });
      const heads = [...grid.querySelectorAll('[role="columnheader"]')];
      const whole = heads.filter((head) => head.getBoundingClientRect().right <= box.right - 10);
      return { rows: rows.length, columns: whole.length, of: heads.length };
    });
    say(`${name}: ${seen.rows} rows by ${seen.columns} columns in view at 1280x720 (${seen.of} columns in the preset)`);
  }

  // First page of each preset, from the press to the frame with its first row.
  await page.goto(`/crawls/${crawl}/urls`);
  await expect(page.getByRole('grid', { name: 'URLs' }).getByRole('row').nth(1)).toBeVisible({ timeout: 60_000 });
  for (const name of [...Object.keys(PRESETS).slice(1), 'Overview']) {
    const header =
      name === 'Links'
        ? 'Followed inlinks'
        : name === 'Content'
          ? 'Title length'
          : name === 'Performance'
            ? 'First byte'
            : name === 'Indexability'
              ? 'Canonical'
              : 'Issues';
    const ms = await timed(
      page,
      `[...document.querySelectorAll('[aria-label="Column presets"] button')].find((b) => b.textContent === ${JSON.stringify(name)}).click()`,
      `[...document.querySelectorAll('[role="columnheader"]')].some((h) => h.textContent.startsWith(${JSON.stringify(header)})) && ${FIRST_ROW}`,
    );
    const asked = PRESETS[name]!.length > 0 ? PRESETS[name] : undefined;
    const sidecar = await sidecarPage({
      filters: [],
      sort: [],
      offset: 0,
      limit: 200,
      ...(asked ? { columns: asked } : {}),
    });
    say(
      `Preset ${name}: its columns and rows on screen in ${Math.round(ms)} ms; sidecar's page 0 ${sidecar.join(', ')} ms`,
    );
  }

  // First page of ten filtered views: a view opened from its address, from navigation to the frame with its rows.
  for (const [name, filters] of VIEWS) {
    const sidecar = await sidecarPage({ filters, sort: [], offset: 0, limit: 200 });
    const start = Date.now();
    await page.goto(`/crawls/${crawl}/urls?filter=${encodeURIComponent(JSON.stringify(filters))}`);
    await page.locator('.si-result-count__n').waitFor({ timeout: 60_000 });
    const total = Date.now() - start;
    say(`View “${name}”: sidecar's page 0 ${sidecar.join(', ')} ms; the app loaded and showing it ${total} ms`);
  }

  // A sort, a filter and a column, each to its first visible response (the header's arrow, the chip, the header).
  await page.goto(`/crawls/${crawl}/urls`);
  await expect(page.getByRole('grid', { name: 'URLs' }).getByRole('row').nth(1)).toBeVisible({ timeout: 60_000 });
  for (let run = 0; run < 3; run++) {
    const sort = await timed(
      page,
      `[...document.querySelectorAll('.si-grid-sort')].find((b) => b.textContent.startsWith('Depth')).click()`,
      `!!document.querySelector('[role="columnheader"][aria-sort]')`,
    );
    const rows = await timed(page, '', FIRST_ROW);
    say(`Sort by Depth: the header's arrow in ${Math.round(sort)} ms, then rows in ${Math.round(rows)} ms more`);
    await page.goto(`/crawls/${crawl}/urls`);
    await expect(page.getByRole('grid', { name: 'URLs' }).getByRole('row').nth(1)).toBeVisible({ timeout: 60_000 });
  }
  await page.getByRole('button', { name: 'Filter by status', exact: true }).click();
  await expect(page.getByRole('menu').getByRole('menuitem').first().locator('.si-menu-item__detail')).toBeVisible({
    timeout: 60_000,
  });
  const filter = await timed(
    page,
    `document.querySelector('[role="menu"] [role="menuitem"]').click()`,
    `!!document.querySelector('[aria-label="Filters"] .si-chip__main')`,
  );
  const filtered = await timed(page, '', FIRST_ROW);
  say(`A facet's value: its chip in ${Math.round(filter)} ms, then rows in ${Math.round(filtered)} ms more`);
  await page.goto(`/crawls/${crawl}/urls`);
  await expect(page.getByRole('grid', { name: 'URLs' }).getByRole('row').nth(1)).toBeVisible({ timeout: 60_000 });
  await page.getByRole('button', { name: /^Columns/ }).click();
  const column = await timed(
    page,
    `[...document.querySelectorAll('[role="dialog"] label')].find((l) => l.textContent.startsWith('Outlinks')).click()`,
    `[...document.querySelectorAll('[role="columnheader"]')].some((h) => h.textContent.startsWith('Outlinks'))`,
  );
  say(`A column ticked in the chooser: its header in ${Math.round(column)} ms`);
  await page.keyboard.press('Escape');

  // The end of every row, and memory before and after.
  await page.goto(`/crawls/${crawl}/urls`);
  const grid = page.getByRole('grid', { name: 'URLs' });
  await expect(grid.getByRole('row').nth(1)).toBeVisible({ timeout: 60_000 });
  const heap = () =>
    page.evaluate(() =>
      Math.round(
        ((performance as unknown as { memory?: { usedJSHeapSize: number } }).memory?.usedJSHeapSize ?? 0) / 1e6,
      ),
    );
  const before = await heap();
  await grid.focus();
  const count = await read<{ matching: number }>(`/api/crawls/${crawl}/urls`, { filters: [], sort: [], limit: 1 });
  const last = count.matching + 1;
  const end = await timed(
    page,
    `document.querySelector('[role="grid"][aria-label="URLs"]').dispatchEvent(new KeyboardEvent('keydown', { key: 'End', ctrlKey: true, bubbles: true }))`,
    `!!document.querySelector('[role="row"][aria-rowindex="${last}"] [role="gridcell"]') && !document.querySelector('[role="row"][aria-rowindex="${last}"] .si-skeleton')`,
  );
  for (let jump = 0; jump < 20; jump++) {
    await page.keyboard.press(jump % 2 === 0 ? 'Control+Home' : 'Control+End');
    await page.keyboard.press('PageUp');
  }
  await page.waitForTimeout(1500);
  const after = await heap();
  say(
    `Ctrl+End to the last of ${count.matching.toLocaleString('en-GB')} rows drawn in ${Math.round(end)} ms; heap ${before} MB before, ${after} MB after 20 more jumps`,
  );
});
