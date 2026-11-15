// Round 5's part B against the budgets (docs/v2-ui-brief.md, Performance budgets): Overview with its totals row, opened
// from another screen (1 s on the 100k crawl), and Home's library, a click to a visible response (100 ms): a row's
// menu, its older crawls unfolded. Each is measured five times and the slowest kept; the numbers are logged and
// attached. On the 100k crawl:
//   SI_E2E_CRAWL="%LOCALAPPDATA%\ShoutingIguana\dev-crawls\synthetic-100k.sicrawl" npx playwright test e2e/home-overview-measure.spec.ts
import { expect, test } from '@playwright/test';
import { countOf, read, seededCrawl, siteRow } from './support';

const TIMES = 5;

test('Overview shows its totals within the budget, opened from URLs', async ({ page }) => {
  test.setTimeout(180_000);
  const crawl = await seededCrawl();
  const crawled = (await read<{ crawledUrlCount: number }>(`/api/crawls/${crawl.id}`)).crawledUrlCount;
  const times: number[] = [];
  for (let run = 0; run < TIMES; run++) {
    await page.goto(`/crawls/${crawl.id}/urls`);
    await expect(page.getByRole('main').locator('.si-result-count')).toBeVisible({ timeout: 60_000 });
    const totals = page.getByRole('region', { name: 'Totals' });
    const started = await page.evaluate(() => performance.now());
    await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Overview' }).click();
    await expect(totals.getByRole('link').first().locator('.si-stat__value')).toHaveText(/\d/, { timeout: 30_000 });
    await expect(page.getByRole('region', { name: 'Issues by severity' })).toBeVisible();
    const ms = (await page.evaluate(() => performance.now())) - started;
    expect(countOf(await totals.getByRole('link').first().locator('.si-stat__value').innerText())).toBe(crawled);
    times.push(Math.round(ms));
  }
  const slowest = Math.max(...times);
  test.info().annotations.push({ type: 'Overview with totals, ms', description: times.join(', ') });
  console.warn(`Overview with its totals: ${times.join(', ')} ms (slowest ${slowest})`);
  expect(slowest).toBeLessThan(1_000);
});

test('Home’s library answers a click within 100 ms: a row’s menu', async ({ page }) => {
  await page.goto('/');
  const library = page.getByRole('region', { name: 'Library' });
  const actions = library.getByRole('button', { name: `Actions for ${siteRow()}` });
  await expect(actions).toBeVisible();
  const times: number[] = [];
  for (let run = 0; run < TIMES; run++) {
    // From the press to the first frame that shows the menu.
    const ms = await actions.evaluate(
      (button) =>
        new Promise<number>((resolve) => {
          const started = performance.now();
          const seen = () => {
            if (document.querySelector('[role="menu"]')) resolve(performance.now() - started);
            else requestAnimationFrame(seen);
          };
          button.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, button: 0, pointerType: 'mouse' }));
          requestAnimationFrame(seen);
        }),
    );
    times.push(Math.round(ms));
    await page.keyboard.press('Escape');
    await expect(page.getByRole('menu')).toBeHidden();
  }
  const slowest = Math.max(...times);
  test.info().annotations.push({ type: 'Row menu, ms', description: times.join(', ') });
  console.warn(`Home’s row menu: ${times.join(', ')} ms (slowest ${slowest})`);
  expect(slowest).toBeLessThan(100);
});
