// The Overview against the real sidecar: the answer on the tests' synthetic crawl (its links land on exactly what they
// count), and a crawl of the engine's synthetic test site watched live to its answer, paused, resumed, stopped and
// continued (docs/v2-ui-ia.md, flows 1 and 2), with axe at zero violations in both themes and under forced colours.
// Never a real site. Every crawl a test starts is stopped and its file deleted, so the library is as the other tests
// expect it.
import { expect, test, type Locator, type Page } from '@playwright/test';
import { libraryIds, removeCrawlsNotIn } from './startedCrawls';
import { expectNoViolations, read, siteRow } from './support';

const env = (name: string) => {
  const value = process.env[name];
  if (!value) throw new Error(`global-setup sets ${name}`);
  return value;
};

let seeded: string[] = [];
test.beforeAll(async () => {
  seeded = await libraryIds();
});

/** Stops and deletes every crawl the tests started: the library is the one seeded crawl again. */
test.afterEach(() => removeCrawlsNotIn(seeded));

// axe, with the wait for a theme's colours to settle (`colourTweensEnd`), is the specs' own: e2e/support.ts.

const main = (page: Page) => page.getByRole('main');
const count = (text: string | null) => Number((text ?? '').replace(/[^\d]/g, ''));

/** From Home to the seeded crawl's Overview, as a user goes. */
async function openOverview(page: Page) {
  await page.goto('/');
  await page.getByRole('region', { name: 'Library' }).getByRole('link', { name: siteRow(), exact: true }).click();
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
  await expect(main(page).locator('.si-page-header__subtitle')).toHaveText(
    /^(About [\d,]+ things to fix|1 thing to fix|Nothing to fix)[^·]*( · (about )?[\d,]+ (more|things?) worth a look)? · \d+ checks found [\d,]+ issues on this crawl\./,
  );
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
  await expect(
    page.getByRole('group', { name: 'Filters' }).getByRole('button', { name: /^Status class is / }),
  ).toBeVisible();
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
  await expect(page.getByRole('region', { name: 'Status codes' }).getByText(/^[\d,]+ URLs? so far$/)).toBeVisible();
  await expect(page.getByRole('region', { name: 'Alerts' })).toBeVisible();
  await expectNoViolations(page);

  // F7 pauses, from anywhere; the screen's banner carries Resume.
  await page.keyboard.press('F7');
  await expect(main(page).getByText('The crawl is paused.', { exact: false })).toBeVisible();
  // The figure updates at most four times a second, and on a loaded machine its last update before the pause can
  // land after the banner: take the paused count once two reads a second apart agree.
  let paused = await crawled(page);
  await expect
    .poll(
      async () => {
        const now = await crawled(page);
        const settled = now === paused;
        paused = now;
        return settled;
      },
      { intervals: [1_000], timeout: 15_000 },
    )
    .toBe(true);
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
  await expect(main(page).locator('.si-page-header__subtitle')).toHaveText(
    /^(About [\d,]+ things to fix|1 thing to fix|Nothing to fix)[^·]*( · (about )?[\d,]+ (more|things?) worth a look)? · \d+ checks found [\d,]+ issues on this crawl\./,
  );
  await expect(main(page).getByRole('list', { name: 'Status codes by class' })).toBeVisible();
  // "Pages" is one count everywhere: the test site has an iframe's target, which answers 2xx with HTML but was only
  // status-checked, so it isn't one of the pages read, nor one of the pages by depth.
  const pagesRead = count(
    /([\d,]+) pages? read/.exec(
      await main(page)
        // The line about the crawl ("305 URLs crawled, 155 pages read"), not the totals' "HTML pages read".
        .getByText(/[\d,]+ pages? read/)
        .innerText(),
    )?.[1] ?? null,
  );
  expect(pagesRead).toBeGreaterThan(0);
  const depthCard = page.getByRole('region', { name: 'Pages by depth' });
  await expect(depthCard.getByRole('list', { name: 'Pages by depth' })).toBeVisible();
  await expect(depthCard.getByText(/^[\d,]+ pages?$/)).toHaveText(
    new RegExp(`^${pagesRead.toLocaleString('en-GB')} pages?$`),
  );
  // The "Crawl finished" toast has faded in: its colours are its own, not half way there.
  const toast = page.locator('.si-toast').filter({ hasText: 'Crawl finished' });
  await expect(toast).toBeVisible();
  await expect(toast).toHaveCSS('opacity', '1');
  await expectNoViolations(page);
});

/** The URL grid's count for a link opened from the live Overview, then back to the Overview. */
async function landsOn(page: Page, link: Locator): Promise<number> {
  await link.click();
  await expect(page).toHaveURL(/\/urls\?filter=/);
  await expect(page.getByRole('heading', { name: 'URLs', level: 1 })).toBeVisible();
  // The grid's count settles once its read has answered.
  let last = -1;
  await expect
    .poll(async () => {
      const now = count(await page.locator('.si-result-count__n').innerText());
      const settled = now === last;
      last = now;
      return settled;
    })
    .toBe(true);
  await page.goBack();
  await expect(main(page).getByText('Live', { exact: true })).toBeVisible();
  return last;
}

test('while a crawl is paused, every live figure lands on exactly its count in the URL grid', async ({ page }) => {
  test.setTimeout(180_000);
  await startCrawl(page);
  // The crawl starts the site slowly (1, 2, 4 requests a second at first), so 200 take a few seconds.
  await expect.poll(() => crawled(page), { timeout: 60_000 }).toBeGreaterThan(200);
  await page.keyboard.press('F7');
  await expect(main(page).getByText('The crawl is paused.', { exact: false })).toBeVisible();
  // The requests on their way when it paused land: then nothing moves.
  let before = -1;
  await expect
    .poll(
      async () => {
        const now = await crawled(page);
        const still = now === before;
        before = now;
        return still;
      },
      { intervals: [2_000] },
    )
    .toBe(true);

  const progress = page.getByRole('region', { name: 'Progress' });
  const crawledLink = progress.getByRole('link', { name: /^Crawled/ });
  expect(await landsOn(page, crawledLink)).toBe(await crawled(page));
  const pagesLink = progress.getByRole('link', { name: /^Pages/ });
  const pages = count(/Pages\s*([\d,]+)/.exec(await pagesLink.innerText())?.[1] ?? null);
  expect(await landsOn(page, pagesLink)).toBe(pages);

  // Every class of the status codes, by its part of the bars, which says its number: the site's own URLs.
  const status = page.getByRole('region', { name: 'Status codes' });
  const names = new Set<string>();
  for (const part of await status.getByRole('link', { name: /^[^:]+: [\d,]+ URLs?$/ }).all()) {
    names.add((await part.getAttribute('aria-label')) ?? (await part.innerText()));
  }
  expect(names.size).toBeGreaterThan(0);
  for (const name of names) {
    const figure = count(name.split(':')[1] ?? '');
    expect(await landsOn(page, status.getByRole('link', { name, exact: true }).first()), name).toBe(figure);
  }
  // The total the classes add up to: the site's own URLs crawled, and the probes of its other versions apart, which
  // together are what Crawled counts.
  const total = count(
    /([\d,]+) URLs? so far/.exec(await status.getByText(/^[\d,]+ URLs? so far$/).innerText())?.[1] ?? null,
  );
  const probeLine = status
    .locator('p', { hasText: 'the crawler tried for the site’s other versions' })
    .getByRole('link');
  const probes = (await probeLine.count()) > 0 ? count(await probeLine.innerText()) : 0;
  if (probes > 0) expect(await landsOn(page, probeLine)).toBe(probes);
  const notCheckedLine = status.locator('p', { hasText: /checked because/ }).getByRole('link');
  const notChecked = (await notCheckedLine.count()) > 0 ? count(await notCheckedLine.innerText()) : 0;
  if (notChecked > 0) expect(await landsOn(page, notCheckedLine)).toBe(notChecked);
  expect(total + probes).toBe(await crawled(page));
  test.info().annotations.push({
    type: 'figures',
    description: `crawled ${await crawled(page)}, pages ${pages}, classes ${[...names].join(' | ')}, probes ${probes}, not checked ${notChecked}`,
  });
});

test('a stopped crawl says how far it got, and continues from there', async ({ page }) => {
  test.setTimeout(120_000);
  await startCrawl(page);
  await expect.poll(() => crawled(page)).toBeGreaterThan(0);

  // F8 stops it: at the crawl's slow start that can be after its first URL alone.
  await page.keyboard.press('F8');
  const stopped = main(page).getByText(/This crawl was stopped after [\d,]+ URLs?/);
  await expect(stopped).toBeVisible({ timeout: 30_000 });
  await expect(main(page).getByText('Live', { exact: true })).toBeHidden();
  // A stopped crawl is analysed when it stops (round 5, gap 2): its results so far, never "hasn't been analysed".
  const id = /\/crawls\/([0-9a-f]{16})\//.exec(page.url())?.[1] ?? '';
  await expect
    .poll(
      async () => {
        const item = await read<{ status: string; analyzedAt: string | null }>(`/api/crawls/${id}`);
        return item.status === 'stopped' && item.analyzedAt !== null;
      },
      { timeout: 60_000 },
    )
    .toBe(true);
  await expect(main(page).getByRole('heading', { name: 'This crawl hasn’t been analysed yet' })).toBeHidden();
  // The "Crawl stopped" toast has faded in: its colours are its own, not half way there.
  const toast = page.locator('.si-toast').filter({ hasText: 'Crawl stopped' });
  await expect(toast).toBeVisible();
  await expect(toast).toHaveCSS('opacity', '1');
  await expectNoViolations(page);

  await main(page).getByRole('button', { name: 'Continue' }).first().click();
  await expect(main(page).getByText('Live', { exact: true })).toBeVisible({ timeout: 30_000 });
  await expect(stopped).toBeHidden();
});
