// The sweep of the URL grid: every count it shows, against where it lands and against the sidecar's own count for the
// view it lands on. The header's count under each view and each column preset (a preset chooses columns, never rows:
// its count is the view's), and every value of every facet under each view: its menu's counts add up to the view
// (but Issues, where a URL counts once for each severity it has), and each value narrows to exactly its count.
//
// It runs on whatever crawl the tests run on. With SI_E2E_CRAWL set to a real crawl's file (the garden crawl, the
// 100k crawl) it sweeps more views:
//   SI_E2E_CRAWL="%LOCALAPPDATA%\ShoutingIguana\dev-crawls\synthetic-100k.sicrawl" npx playwright test e2e/urls-sweep.spec.ts
import { expect, test, type Page } from '@playwright/test';
import { read, seededCrawl, site } from './support';

interface Filter {
  column: string;
  op: string;
  value?: string | number | boolean;
  values?: (string | number)[];
}

let crawl = '';
test.beforeAll(async () => {
  crawl = (await seededCrawl()).id;
});

/** The sidecar's count of the URLs a view matches. */
const counted = async (filters: Filter[], search?: string) =>
  (
    await read<{ matching: number }>(`/api/crawls/${crawl}/urls`, {
      filters,
      sort: [],
      limit: 1,
      ...(search ? { search } : {}),
    })
  ).matching;

const main = (page: Page) => page.getByRole('main');
const number = (text: string | null) => Number((text ?? '').replace(/[^\d]/g, ''));
const shown = async (page: Page) => number(await main(page).locator('.si-result-count__n').innerText());
const expectShown = (page: Page, expected: number, what: string) =>
  expect.poll(() => shown(page), { message: what, timeout: 60_000 }).toBe(expected);

function viewOf(page: Page): { filters: Filter[]; search: string | undefined } {
  const params = new URL(page.url()).searchParams;
  const filter = params.get('filter');
  const q = params.get('q');
  return {
    filters: filter ? (JSON.parse(filter) as Filter[]) : [],
    search: q === null ? undefined : q.replace(/^"(.*)"$/, '$1'),
  };
}

const FACETS = ['status', 'status code', 'indexability', 'type', 'depth', 'response time', 'sitemaps', 'issues'];
/** Facets whose values can overlap: a URL with an error and a warning counts under both. */
const OVERLAPPING = new Set(['issues']);
const PRESETS = ['Overview', 'Indexability', 'Content', 'Performance', 'Links'];
const FULL = process.env.SI_E2E_CRAWL !== undefined || process.env.SI_E2E_SWEEP === 'full';

let figures = 0;

async function sweepFacets(page: Page, view: string) {
  const inView = await shown(page);
  const here = page.url();
  for (const facet of FACETS) {
    const button = main(page).getByRole('button', { name: `Filter by ${facet}`, exact: true });
    await button.click();
    const menu = page.getByRole('menu');
    const heading = menu.getByText(/, of [\d,]+ URLs?$/);
    // A facet the analysis counts, on a crawl without one, says so: nothing to land on.
    const unavailable = menu.getByText(/^Counted by the analysis/);
    await expect(heading.or(unavailable), `${view}: ${facet}`).toBeVisible({ timeout: 60_000 });
    if (await unavailable.isVisible()) {
      await page.keyboard.press('Escape');
      continue;
    }
    expect(number((await heading.innerText()).split(', of ')[1] ?? ''), `${view}: ${facet}`).toBe(inView);
    const values = await menu.getByRole('menuitem').evaluateAll((items) =>
      items.map((item) => ({
        words: item.querySelector('.si-menu-item__label')?.textContent ?? '',
        count: Number(
          (item.querySelector('.si-menu-item__detail [aria-hidden="true"]')?.textContent ?? '').replace(/[^\d]/g, ''),
        ),
        applied: item.getAttribute('aria-disabled') === 'true',
      })),
    );
    if (!OVERLAPPING.has(facet)) {
      // The Sitemaps menu's offer of the not-in-sitemap check's pages (the fix-it pass) is some of "In no sitemap" again.
      expect(
        values
          .filter((value) => value.words !== 'Indexable pages in no sitemap')
          .reduce((sum, value) => sum + value.count, 0),
        `${view}: the ${facet} values add up to the URLs in view`,
      ).toBe(inView);
    }
    await page.keyboard.press('Escape');
    for (const [index, value] of values.entries()) {
      figures++;
      const what = `${view}: ${facet} “${value.words}” said ${value.count}`;
      if (value.applied) {
        expect(value.count, what).toBe(inView);
        continue;
      }
      await button.click();
      const item = page.getByRole('menu').getByRole('menuitem').nth(index);
      await expect(item.locator('.si-menu-item__label'), what).toHaveText(value.words);
      await item.click();
      await expectShown(page, value.count, what);
      const landed = viewOf(page);
      expect(await counted(landed.filters, landed.search), `${what}, and the sidecar counts its view`).toBe(
        value.count,
      );
      await page.goto(here);
      await expectShown(page, inView, `${view}: back from ${facet} “${value.words}”`);
    }
  }
}

test('every count on URLs lands on itself: the header, each preset, and every facet value under each view', async ({
  page,
}) => {
  test.setTimeout(40 * 60_000);
  await page.goto(`/crawls/${crawl}/urls`);
  const total = await counted([]);
  await expectShown(page, total, 'All URLs');
  figures++;

  // A preset chooses columns: the count stays the view's.
  for (const preset of PRESETS) {
    await main(page)
      .getByRole('group', { name: 'Column presets' })
      .getByRole('button', { name: preset, exact: true })
      .click();
    await expect(
      main(page).getByRole('group', { name: 'Column presets' }).getByRole('button', { name: preset, exact: true }),
    ).toHaveAttribute('aria-pressed', 'true');
    await expectShown(page, total, preset);
    figures++;
  }

  await page.goto(`/crawls/${crawl}/urls`);
  await expectShown(page, total, 'All URLs');
  await sweepFacets(page, 'All URLs');

  const views: [name: string, filters: Filter[]][] = [
    ['The pages read', [{ column: 'wordCount', op: 'isNotEmpty' }]],
    ...(FULL
      ? ([
          ['On this site', [{ column: 'isInternal', op: 'is', value: true }]],
          ['Not 2xx', [{ column: 'statusClass', op: 'notIn', values: ['2xx'] }]],
        ] as [string, Filter[]][])
      : []),
  ];
  for (const [name, filters] of views) {
    const expected = await counted(filters);
    await page.goto(`/crawls/${crawl}/urls?filter=${encodeURIComponent(JSON.stringify(filters))}`);
    await expectShown(page, expected, name);
    figures++;
    if (expected > 0) await sweepFacets(page, name);
  }

  process.stdout.write(
    `URLs sweep: ${figures} figures checked on ${site()} (${total.toLocaleString('en-GB')} URLs).\n`,
  );
  expect(figures).toBeGreaterThan(20);
});
