// The palette's search of the crawl's URLs to the inspector, and Settings > Analysis: a limit changed turns the crawl's
// analysis out of date where the app shows it, and putting it back makes it stand again. Against the real sidecar.
// Screenshots go to test-results/handover.
import { expect, test, type Page } from '@playwright/test';
import { expectNoViolations, read, seededCrawl, sidecar } from './support';

interface UrlPage {
  rows: { id: number; url: string; title: string | null }[];
}

const SHOTS = 'test-results/handover';
let crawl = '';
test.beforeAll(async () => {
  crawl = (await seededCrawl()).id;
});
// Whatever a test left, the engine's limits are its own again: the other specs expect the crawl's analysis to stand.
test.afterAll(async () => {
  await sidecar('/api/analysis-settings/reset', { method: 'POST' });
});

const palette = (page: Page) => page.getByRole('dialog', { name: 'Command palette' });

test('searches the crawl’s URLs as it’s typed, marks the match, and Enter opens the inspector on one', async ({
  page,
}) => {
  // A page of the crawl, by a piece of its path.
  const { rows } = await read<UrlPage>(`/api/crawls/${crawl}/urls`, {
    columns: ['url', 'title'],
    filters: [{ column: 'type', op: 'in', values: ['page'] }],
    limit: 50,
  });
  const target = rows.find((row) => new URL(row.url).pathname.length > 4) ?? rows[0];
  if (!target) throw new Error('The crawl has no pages');
  const piece = new URL(target.url).pathname.replace(/\/$/, '').split('/').pop() ?? '';

  await page.goto(`/crawls/${crawl}/overview`);
  await expect(page.getByRole('heading', { level: 1, name: 'Overview' })).toBeVisible();
  await page.keyboard.press('Control+k');
  await expect(palette(page)).toBeVisible();
  await page.keyboard.type(piece);
  const option = palette(page).getByRole('option').filter({ hasText: target.url });
  await expect(option).toBeVisible();
  await expect(option.locator('mark').first()).toHaveText(
    new RegExp(piece.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'i'),
  );
  await expect(option).toContainText(/\d{3}|Blocked|Not crawled/);
  await expectNoViolations(page);
  await page.screenshot({ path: `${SHOTS}/palette-search-light.png` });

  // Highlight it and press Enter.
  for (let step = 0; step < 40; step += 1) {
    if ((await option.getAttribute('aria-selected')) === 'true') break;
    await page.keyboard.press('ArrowDown');
  }
  await page.keyboard.press('Enter');
  await expect(palette(page)).toBeHidden();
  await expect(page).toHaveURL(new RegExp(`/crawls/${crawl}/urls\\?.*inspect=${target.id}`));
  await expect(page.getByRole('complementary', { name: /Inspector|URL inspector/ })).toBeVisible();
});

test('says when nothing matches', async ({ page }) => {
  await page.goto(`/crawls/${crawl}/overview`);
  await expect(page.getByRole('heading', { level: 1, name: 'Overview' })).toBeVisible();
  await page.keyboard.press('Control+k');
  await expect(palette(page)).toBeVisible();
  await page.keyboard.type('zz-no-url-has-this-zz');
  await expect(palette(page).getByText('Nothing matches that.')).toBeVisible();
});

test('a limit changed turns the crawl’s analysis out of date in the library, and putting it back makes it stand', async ({
  page,
}) => {
  await page.goto('/settings/analysis');
  const field = page.getByRole('textbox', { name: 'Widest title' });
  await expect(field).toHaveValue('580');
  // A width says the characters it holds, from what's typed.
  await expect(field).toHaveAccessibleDescription(/About 60 characters of ordinary text\.$/);

  // Refused as it's typed, beside the limit.
  await field.fill('100');
  await expect(page.getByText(/must be more than the narrowest/)).toBeVisible();
  await expect(field).toHaveAttribute('aria-invalid', 'true');
  await page.screenshot({ path: `${SHOTS}/analysis-refused-light.png` });

  await field.fill('700');
  await expect(field).toHaveAccessibleDescription(/About 75 characters of ordinary text\.$/);
  await expect(page.getByText('Saved', { exact: true })).toBeVisible({ timeout: 10_000 });
  await expect(page.getByRole('button', { name: 'Put back 580 px' })).toBeVisible();
  const library = page.getByRole('region', { name: 'Library' });
  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Home' }).click();
  await expect(library.getByText('Analysis out of date')).toBeVisible();

  // Reloaded, the change is remembered: the engine keeps it.
  await page.goto('/settings/analysis');
  await expect(page.getByRole('textbox', { name: 'Widest title' })).toHaveValue('700');
  await page.getByRole('button', { name: 'Put back every limit' }).click();
  await expect(page.getByRole('textbox', { name: 'Widest title' })).toHaveValue('580');
  await expect(
    page.getByRole('region', { name: /Notifications/ }).getByText('Every limit is the engine’s again'),
  ).toBeVisible();
  await expectNoViolations(page);
  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Home' }).click();
  await expect(page.getByRole('region', { name: 'Library' }).getByRole('table')).toBeVisible();
  await expect(library.getByText('Analysis out of date')).toBeHidden();
});

test('Show in URLs, then Back to the links, lands on the links view as it was, sort and columns too (the review’s finding 10)', async ({
  page,
}) => {
  const filter = [{ column: 'kind', op: 'in', values: ['hyperlink'] }];
  const sort = [{ column: 'to.url', desc: true }];
  const cols = ['from.url', 'to.url', 'text'];
  await page.goto(
    `/crawls/${crawl}/links?filter=${encodeURIComponent(JSON.stringify(filter))}&sort=${encodeURIComponent(
      JSON.stringify(sort),
    )}&cols=${encodeURIComponent(JSON.stringify(cols))}`,
  );
  await expect(page.getByRole('grid', { name: 'Links' }).getByRole('row').nth(1)).toBeVisible();
  await page.getByRole('button', { name: 'Show in URLs' }).click();
  await page.getByRole('menuitem', { name: /^Their targets/ }).click();
  await expect(page).toHaveURL(/\/urls\?links=/);
  await page.getByRole('button', { name: 'Back to the links' }).click();
  await expect(page).toHaveURL(/\/links\?/);
  const search = new URL(page.url()).searchParams;
  expect(JSON.parse(search.get('sort') ?? 'null')).toEqual(sort);
  expect(JSON.parse(search.get('cols') ?? 'null')).toEqual(cols);
  expect(JSON.parse(search.get('filter') ?? 'null')).toEqual(filter);
});
