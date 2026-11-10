// The Overview against the real sidecar: the answer on the tests' synthetic crawl (its links land on exactly what they
// count), and a crawl of the engine's synthetic test site watched live to its answer, paused, resumed, stopped and
// continued (docs/v2-ui-ia.md, flows 1 and 2), with axe at zero violations in both themes and under forced colours.
// Never a real site. Every crawl a test starts is stopped and its file deleted, so the library is as the other tests
// expect it.
import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Locator, type Page } from '@playwright/test';

const env = (name: string) => {
  const value = process.env[name];
  if (!value) throw new Error(`global-setup sets ${name}`);
  return value;
};
const site = () => env('SI_E2E_SITE');

interface Crawl {
  id: string;
  status: string;
}

/** The sidecar, from the test itself (no page, so no CORS): to tidy up the crawls a test started. */
async function sidecar(route: string, init: RequestInit = {}): Promise<Response> {
  return fetch(`${env('SI_E2E_SIDECAR')}${route}`, {
    ...init,
    headers: { Authorization: `Bearer ${env('SI_E2E_TOKEN')}` },
  });
}

const crawls = async () => (await (await sidecar('/api/crawls')).json()) as Crawl[];
const LIVE = ['running', 'paused', 'stopping', 'analyzing'];

let seeded: string[] = [];
test.beforeAll(async () => {
  seeded = (await crawls()).map((crawl) => crawl.id);
});

/** Stops and deletes every crawl the tests started: the library is the one seeded crawl again. */
test.afterEach(async () => {
  const started = Date.now();
  for (;;) {
    const mine = (await crawls()).filter((crawl) => !seeded.includes(crawl.id));
    if (mine.length === 0) return;
    for (const crawl of mine) {
      if (crawl.status === 'running' || crawl.status === 'paused') {
        await sidecar(`/api/crawls/${crawl.id}/stop`, { method: 'POST' });
      } else if (crawl.status === 'analyzing') {
        await sidecar(`/api/crawls/${crawl.id}/analyze/cancel`, { method: 'POST' });
      } else if (!LIVE.includes(crawl.status)) {
        await sidecar(`/api/crawls/${crawl.id}/delete-file`, { method: 'POST' });
      }
    }
    if (Date.now() - started > 60_000) throw new Error('The crawls the test started couldn’t be removed');
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
});

async function expectNoViolations(page: Page) {
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  expect(
    results.violations.map(
      (violation) =>
        `${violation.id}: ${violation.help}: ${violation.nodes.map((node) => `${node.target.join(' ')} ${node.failureSummary ?? ''}`).join('; ')}`,
    ),
  ).toEqual([]);
}

const main = (page: Page) => page.getByRole('main');
const count = (text: string | null) => Number((text ?? '').replace(/[^\d]/g, ''));

/** From Home to the seeded crawl's Overview, as a user goes. */
async function openOverview(page: Page) {
  await page.goto('/');
  await page.getByRole('region', { name: 'Library' }).getByRole('link', { name: site(), exact: true }).click();
  await expect(page).toHaveURL(/\/crawls\/[0-9a-f]{16}\/overview$/);
  await expect(main(page).getByRole('heading', { name: 'Fix first' })).toBeVisible();
  // The breakdowns have arrived too.
  await expect(main(page).getByRole('list', { name: 'Status codes by class' })).toBeVisible();
  await expect(main(page).getByRole('list', { name: 'Pages by depth' })).toBeVisible();
}

/** Pastes the test site's URL on Home and presses Enter: flow 1, which lands on the crawl's Overview, live. */
async function startCrawl(page: Page) {
  await page.goto('/');
  await page.getByPlaceholder('Paste a URL, then press Enter').fill(env('SI_E2E_TESTSITE'));
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/crawls\/[0-9a-f]{16}\/overview$/);
  await expect(main(page).getByText('Live', { exact: true })).toBeVisible();
}

/** The live "Crawled" figure. */
const crawled = async (page: Page) =>
  count(
    /Crawled\s*([\d,]+)/.exec(
      await page
        .getByRole('region', { name: 'Progress' })
        .getByRole('link', { name: /^Crawled/ })
        .innerText(),
    )?.[1] ?? null,
  );

test('the answer comes first, and Fix first leads to its check in Issues', async ({ page }) => {
  await openOverview(page);
  await expect(main(page).getByText(/^\d+ checks found [\d,]+ issues on this crawl\./)).toBeVisible();
  await expect(main(page).getByText('Live', { exact: true })).toBeHidden();

  const first = page.getByRole('region', { name: 'Fix first' }).getByRole('listitem').first().getByRole('link');
  // The most severe first: the synthetic crawl has errors.
  await expect(first).toHaveAccessibleName(/^Error:/);
  await first.click();
  await expect(page).toHaveURL(/\/issues\/[a-z0-9-]+$/);
  await expect(page.getByRole('heading', { name: 'Issues', level: 1 })).toBeVisible();
  await page.goBack();
  await expect(main(page).getByRole('heading', { name: 'Fix first' })).toBeVisible();
});

/** Opens a figure's link and checks URLs lists exactly as many as the figure said, with the filters in words. */
async function expectLandsOn(page: Page, link: Locator, expected: number) {
  await link.click();
  await expect(page).toHaveURL(/\/urls\?filter=/);
  await expect(page.getByRole('heading', { name: 'URLs', level: 1 })).toBeVisible();
  await expect.poll(async () => count(await page.locator('.si-result-count__n').innerText())).toBe(expected);
}

test('every number opens its URLs: a status class, the indexable pages and a depth land on exactly their count', async ({
  page,
}) => {
  await openOverview(page);
  const status = page.getByRole('region', { name: 'Status codes' });
  // A class that didn't answer 2xx, by its part of the magnified bar, which says its number.
  const part = status.getByRole('link', { name: /^(3xx|4xx|5xx): [\d,]+ URLs?$/ }).first();
  const name = (await part.getAttribute('aria-label')) ?? '';
  await expectLandsOn(page, part, count(name.split(':')[1] ?? ''));
  // The chips read as sentences, never as the engine's ids.
  await expect(page.getByText('Status class', { exact: true })).toBeVisible();
  await expect(page.getByText('Outcome', { exact: true })).toBeVisible();
  await expect(page.getByText('statusClass')).toHaveCount(0);
  await page.goBack();

  const indexable = page.getByRole('region', { name: 'Indexability' }).getByRole('link', { name: /^Indexable/ });
  const indexableCount = count(/Indexable\s*([\d,]+)/.exec(await indexable.innerText())?.[1] ?? null);
  expect(indexableCount).toBeGreaterThan(0);
  await expectLandsOn(page, indexable, indexableCount);
  await page.goBack();

  const depth = page.getByRole('list', { name: 'Pages by depth' }).getByRole('link').last();
  const depthName = await depth.innerText();
  await expectLandsOn(page, depth, count(/:\s*([\d,]+) pages?/.exec(depthName)?.[1] ?? null));
});

for (const colorScheme of ['light', 'dark'] as const) {
  test(`the answer passes axe (${colorScheme})`, async ({ page }) => {
    await page.emulateMedia({ colorScheme });
    await openOverview(page);
    await expectNoViolations(page);
  });
}

test('under forced colours, the answer passes axe, and the status bar’s parts are told apart without colour', async ({
  page,
}) => {
  await page.emulateMedia({ forcedColors: 'active' });
  await openOverview(page);
  await expectNoViolations(page);
  // Every class's colour is the same system colour now: the parts are apart, and each has its name.
  const bar = page.locator('.si-split__bar--magnified');
  await expect(bar).toHaveCSS('column-gap', /^[1-9]/);
  for (const part of await bar.getByRole('link').all()) await expect(part).toHaveAccessibleName(/: [\d,]+ URLs?$/);
});

test('a crawl started from Home is watched live to its answer: paused, resumed, analysed, with no reload', async ({
  page,
}) => {
  test.setTimeout(180_000);
  await startCrawl(page);

  // The figures move, in place.
  await expect.poll(() => crawled(page)).toBeGreaterThan(0);
  const before = await crawled(page);
  await expect.poll(() => crawled(page)).toBeGreaterThan(before);
  await expect(page.getByRole('region', { name: 'Status codes' }).getByText(/URLs? so far/)).toBeVisible();
  await expect(page.getByRole('region', { name: 'Alerts' })).toBeVisible();
  await expectNoViolations(page);

  // F7 pauses, from anywhere; the screen's banner carries Resume.
  await page.keyboard.press('F7');
  await expect(main(page).getByText('The crawl is paused.', { exact: false })).toBeVisible();
  const paused = await crawled(page);
  await page.emulateMedia({ colorScheme: 'dark' });
  await expectNoViolations(page);
  await page.emulateMedia({ colorScheme: 'light', forcedColors: 'active' });
  await expectNoViolations(page);
  await page.emulateMedia({ forcedColors: 'none' });
  // Nothing was fetched while it was paused (the requests on their way when it paused may have landed).
  expect((await crawled(page)) - paused).toBeLessThan(50);
  await main(page)
    .getByRole('button', { name: /Resume/ })
    .click();
  await expect(main(page).getByText('The crawl is paused.', { exact: false })).toBeHidden();
  await expect.poll(() => crawled(page)).toBeGreaterThan(paused);

  // It ends; the analysis runs on its own; the answer appears. Nothing here reloads the page.
  const answer = main(page).getByRole('heading', { name: 'Fix first' });
  await expect(answer).toBeVisible({ timeout: 150_000 });
  await expect(main(page).getByText('Live', { exact: true })).toBeHidden();
  await expect(main(page).getByText(/^\d+ checks found [\d,]+ issues on this crawl\./)).toBeVisible();
  await expect(main(page).getByRole('list', { name: 'Status codes by class' })).toBeVisible();
  await expectNoViolations(page);
});

test('a stopped crawl says how far it got, and continues from there', async ({ page }) => {
  test.setTimeout(120_000);
  await startCrawl(page);
  await expect.poll(() => crawled(page)).toBeGreaterThan(0);

  // F8 stops it.
  await page.keyboard.press('F8');
  const stopped = main(page).getByText(/This crawl was stopped after [\d,]+ URLs/);
  await expect(stopped).toBeVisible({ timeout: 30_000 });
  await expect(main(page).getByText('Live', { exact: true })).toBeHidden();
  // A stopped crawl isn't analysed on its own, and says so, rather than show an answer.
  await expect(main(page).getByRole('heading', { name: 'This crawl hasn’t been analysed yet' })).toBeVisible();
  await expectNoViolations(page);

  await main(page).getByRole('button', { name: 'Continue' }).first().click();
  await expect(main(page).getByText('Live', { exact: true })).toBeVisible({ timeout: 30_000 });
  await expect(stopped).toBeHidden();
});
