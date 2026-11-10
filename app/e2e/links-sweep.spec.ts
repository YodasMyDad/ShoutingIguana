// The sweep of Links: every count the screen shows, against where it lands and against the sidecar's own count for
// the view it lands on. The header's count under each view, every value of every facet under each view (its menu's
// counts add up to the view, and each value narrows to exactly its count), and the inspector's "All N in Links" both
// ways for a few URLs. A number that doesn't land on itself fails here by name.
//
// It runs on whatever crawl the tests run on. On the synthetic crawl it sweeps a few views; with SI_E2E_CRAWL set to
// a real crawl's file (the garden crawl, the 100k crawl) it sweeps every preset as well:
//   SI_E2E_CRAWL="%LOCALAPPDATA%\ShoutingIguana\dev-crawls\synthetic-100k.sicrawl" npx playwright test e2e/links-sweep.spec.ts
import { expect, test, type Page } from '@playwright/test';

const env = (name: string) => {
  const value = process.env[name];
  if (!value) throw new Error(`global-setup sets ${name}`);
  return value;
};

interface Filter {
  column: string;
  op: string;
  value?: string | number | boolean;
  values?: (string | number)[];
}
interface LinksPage {
  rows: Record<string, string | number | boolean | null>[];
  matching: number;
  total: number;
}

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

let crawl = '';
test.beforeAll(async () => {
  const library = await api<{ id: string; site: string | null; fileName: string }[]>('/api/crawls');
  crawl = library.find((item) => (item.site ?? item.fileName) === env('SI_E2E_SITE'))?.id ?? '';
  expect(crawl).not.toBe('');
});

/** The sidecar's count of the links a view matches. */
const counted = async (filters: Filter[], search?: string) =>
  (
    await api<LinksPage>(`/api/crawls/${crawl}/links`, {
      filters,
      sort: [],
      limit: 1,
      ...(search ? { search } : {}),
    })
  ).matching;

const main = (page: Page) => page.getByRole('main');
const number = (text: string | null) => Number((text ?? '').replace(/[^\d]/g, ''));
const shown = async (page: Page) => number(await main(page).locator('.si-result-count__n').innerText());
/** A read that passes over every link of a big crawl takes a second or two: the count is waited for, not timed. */
const expectShown = (page: Page, expected: number, what: string) =>
  expect.poll(() => shown(page), { message: what, timeout: 30_000 }).toBe(expected);

/** The view the page is on, from its address: what the sidecar is asked to count. */
function viewOf(page: Page): { filters: Filter[]; search: string | undefined } {
  const params = new URL(page.url()).searchParams;
  const filter = params.get('filter');
  const q = params.get('q');
  return {
    filters: filter ? (JSON.parse(filter) as Filter[]) : [],
    // The router writes a word as it is, and quotes only what would read as something else (a number, `true`).
    search: q === null ? undefined : q.replace(/^"(.*)"$/, '$1'),
  };
}

const FACETS = ['kind', 'position', 'follow', 'target status', 'target site'];
const PRESETS = ['Broken links', 'Links to redirects', 'Nofollow links', 'No anchor text', 'External links'];
/** Every preset on a real crawl; on the tests' synthetic crawl, enough to cover a filtered view and a search. */
const FULL = process.env.SI_E2E_CRAWL !== undefined || process.env.SI_E2E_SWEEP === 'full';

let figures = 0;

/** Every value of every facet under the view the page is on: its count, where it lands, and what the sidecar says. */
async function sweepFacets(page: Page, view: string) {
  const inView = await shown(page);
  const here = page.url();
  for (const facet of FACETS) {
    const button = main(page).getByRole('button', { name: `Filter by ${facet}` });
    await button.click();
    const menu = page.getByRole('menu');
    // The counts are of the links in view, and add up to them.
    await expect(menu.getByText(/, of [\d,]+ links?$/), `${view}: ${facet}`).toBeVisible({ timeout: 30_000 });
    expect(
      number((await menu.getByText(/, of [\d,]+ links?$/).innerText()).split(', of ')[1] ?? ''),
      `${view}: ${facet}`,
    ).toBe(inView);
    const values = await menu.getByRole('menuitem').evaluateAll((items) =>
      items.map((item) => ({
        words: item.querySelector('.si-menu-item__label')?.textContent ?? '',
        count: Number(
          (item.querySelector('.si-menu-item__detail [aria-hidden="true"]')?.textContent ?? '').replace(/[^\d]/g, ''),
        ),
        applied: item.getAttribute('aria-disabled') === 'true',
      })),
    );
    expect(
      values.reduce((sum, value) => sum + value.count, 0),
      `${view}: the ${facet} values add up to the links in view`,
    ).toBe(inView);
    await page.keyboard.press('Escape');
    for (const [index, value] of values.entries()) {
      figures++;
      const what = `${view}: ${facet} “${value.words}” said ${value.count}`;
      if (value.applied) {
        // The view is already this value's: its count is every link in view.
        expect(value.count, what).toBe(inView);
        continue;
      }
      await button.click();
      // By its place in the list, which is the engine's order: "Head" is in "Header" too.
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

test('every count on Links lands on itself: the header, each preset, and every facet value under each view', async ({
  page,
}) => {
  test.setTimeout(20 * 60_000);
  await page.goto(`/crawls/${crawl}/links`);
  const total = await counted([]);
  await expectShown(page, total, 'All links');
  figures++;
  await sweepFacets(page, 'All links');

  for (const preset of FULL ? PRESETS : PRESETS.slice(0, 1)) {
    await page.goto(`/crawls/${crawl}/links`);
    await expectShown(page, total, 'All links');
    await main(page).getByRole('button', { name: preset, exact: true }).click();
    await expect(main(page).getByRole('button', { name: preset, exact: true })).toHaveAttribute('aria-pressed', 'true');
    const view = viewOf(page);
    const expected = await counted(view.filters);
    await expectShown(page, expected, preset);
    figures++;
    // A preset with no links on this crawl has nothing to count by: its grid says so.
    if (expected === 0) {
      await expect(main(page).getByText('No links match these filters.')).toBeVisible();
      continue;
    }
    await sweepFacets(page, preset);
  }

  // A search, which the engine counts by reading every link: its counts land too.
  await page.goto(`/crawls/${crawl}/links`);
  await expectShown(page, total, 'All links');
  const search = main(page).getByRole('searchbox', { name: 'Search anchor text and URLs' });
  await search.fill('about');
  await search.press('Enter');
  const found = await counted([], 'about');
  await expectShown(page, found, 'A search for “about”');
  figures++;
  if (found > 0) await sweepFacets(page, 'A search for “about”');

  process.stdout.write(
    `Links sweep: ${figures} figures checked on ${env('SI_E2E_SITE')} (${total.toLocaleString('en-GB')} links).\n`,
  );
  expect(figures).toBeGreaterThan(30);
});

test('the inspector’s “All N in Links” lands on its count, both ways, for the start URL and each end of a broken link', async ({
  page,
}) => {
  test.setTimeout(5 * 60_000);
  const seed = await api<{ rows: { id: number }[] }>(`/api/crawls/${crawl}/urls`, {
    filters: [{ column: 'source', op: 'in', values: ['seed'] }],
    sort: [],
    limit: 1,
  });
  const broken = await api<LinksPage>(`/api/crawls/${crawl}/links`, {
    columns: ['from.id', 'to.id'],
    filters: [{ column: 'to.statusClass', op: 'in', values: ['4xx', '5xx'] }],
    sort: [],
    limit: 1,
  });
  const urls = [seed.rows[0]?.id, broken.rows[0]?.['from.id'], broken.rows[0]?.['to.id']].filter(
    (id): id is number => typeof id === 'number',
  );
  let checked = 0;
  for (const id of [...new Set(urls)]) {
    for (const [tab, column] of [
      [/^Inlinks/, 'to.id'],
      [/^Outlinks/, 'from.id'],
    ] as const) {
      await page.goto(`/crawls/${crawl}/links?inspect=${id}`);
      const inspector = page.getByRole('complementary', { name: 'Inspector' });
      await inspector.getByRole('tab', { name: tab }).click();
      await expect(inspector.getByRole('tab', { name: tab })).toHaveAttribute('aria-selected', 'true');
      // The section says how many links it has once it has counted them: only then is there a link, or none.
      const count = inspector.getByText(/^[\d,]+ (in|out)links?$/);
      await expect(count).toBeVisible({ timeout: 30_000 });
      if (number(await count.innerText()) === 0) continue;
      const all = inspector.getByRole('link', { name: /^All [\d,]+ in Links$/ });
      const said = number(await all.innerText());
      expect(said, `URL ${id}: the link says the section's count`).toBe(number(await count.innerText()));
      await all.click();
      await expectShown(page, said, `URL ${id}: ${column}`);
      const landed = viewOf(page);
      expect(landed.filters[0]).toEqual({ column, op: 'eq', value: id });
      expect(await counted(landed.filters), `URL ${id}: the sidecar counts ${column}`).toBe(said);
      checked++;
    }
  }
  process.stdout.write(`Links sweep: ${checked} “All N in Links” checked on ${env('SI_E2E_SITE')}.\n`);
  expect(checked).toBeGreaterThan(0);
});
