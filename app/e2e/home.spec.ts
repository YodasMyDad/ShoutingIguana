import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { siteRow } from './support';

/** axe with the brief's bar: WCAG 2.2 AA, zero violations allowed. */
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

test('Home lists the crawl in the data folder, with no accessibility violations', async ({ page }) => {
  await page.goto('/');
  const library = page.getByRole('region', { name: 'Library' });
  await expect(library.getByRole('link', { name: siteRow(), exact: true })).toBeVisible();
  await expect(library.getByRole('row')).toHaveCount(2);
  await expectNoViolations(page);
});

test('Home passes in the dark theme too', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.goto('/');
  await expect(
    page.getByRole('region', { name: 'Library' }).getByRole('link', { name: siteRow(), exact: true }),
  ).toBeVisible();
  await expectNoViolations(page);
});

test('the palette opens with Ctrl+K, finds a command and closes with Esc', async ({ page }) => {
  await page.goto('/');
  await expect(
    page.getByRole('region', { name: 'Library' }).getByRole('link', { name: siteRow(), exact: true }),
  ).toBeVisible();
  await page.keyboard.press('Control+k');
  const palette = page.getByRole('dialog', { name: 'Command palette' });
  await expect(palette).toBeVisible();
  await page.keyboard.type('density');
  await expect(palette.getByRole('option', { name: /Density: compact/ })).toBeVisible();
  await expectNoViolations(page);
  await page.keyboard.press('Escape');
  await expect(palette).toBeHidden();
});

test('opening a crawl shows its overview with the sidebar sections', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('region', { name: 'Library' }).getByRole('link', { name: siteRow(), exact: true }).click();
  await expect(page).toHaveURL(/\/crawls\/[0-9a-f]{16}\/overview$/);
  await expect(page.getByRole('heading', { name: 'Overview', level: 1 })).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: /Issues/ })).toBeVisible();
  await expectNoViolations(page);
});

test('at 200% zoom (640x360) the library keeps its Site column and nothing scrolls sideways', async ({ page }) => {
  await page.setViewportSize({ width: 640, height: 360 });
  await page.goto('/');
  const library = page.getByRole('region', { name: 'Library' });
  const name = library.getByRole('link', { name: siteRow(), exact: true });
  await expect(name).toBeVisible();
  const widths = await page.evaluate(() => {
    const main = document.querySelector('main');
    const table = document.querySelector('main table');
    const site = document.querySelector('main table tbody td');
    return {
      page: document.scrollingElement!.scrollWidth - document.scrollingElement!.clientWidth,
      main: main ? main.scrollWidth - main.clientWidth : -1,
      table: table?.parentElement ? table.parentElement.scrollWidth - table.parentElement.clientWidth : -1,
      site: site?.getBoundingClientRect().width ?? 0,
    };
  });
  // Neither the page, nor the screen, nor the library's own box scrolls sideways at this width.
  expect(widths).toMatchObject({ page: 0, main: 0, table: 0 });
  // The Site column holds its least: room for the favicon and a name.
  expect(widths.site).toBeGreaterThanOrEqual(144);
  // Pages and Started are second lines now, not columns: still said.
  await expect(library.getByRole('columnheader', { name: 'Pages' })).toBeHidden();
  await expect(library.getByText(/ pages?$/).first()).toBeVisible();
  await expect(library.getByText(/^Started /).first()).toBeVisible();
  await expectNoViolations(page);
});

test('at 200% zoom with the sidebar open, the library scrolls inside its card, never the screen', async ({ page }) => {
  // A 1,120 px window at 200%: with the sidebar open, the library's columns are wider than its card.
  await page.setViewportSize({ width: 560, height: 360 });
  await page.goto('/');
  const library = page.getByRole('region', { name: 'Library' });
  await expect(library.getByRole('link', { name: siteRow(), exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Expand the sidebar' }).click();
  await expect(page.getByRole('button', { name: 'Collapse the sidebar' })).toBeVisible();
  const overflow = () =>
    page.evaluate(() => {
      const main = document.querySelector('main');
      const card = document.querySelector('main table')?.closest('section');
      return {
        page: document.scrollingElement!.scrollWidth - document.scrollingElement!.clientWidth,
        main: main ? main.scrollWidth - main.clientWidth : -1,
        // The card ends inside the screen, its right edge and gutter on show.
        card: main && card ? Math.max(0, card.getBoundingClientRect().right - main.getBoundingClientRect().right) : -1,
      };
    });
  // The sidebar opens with a transition: measured once it has settled. The table is wider than the card, and
  // scrolls in it.
  await expect
    .poll(() =>
      page.evaluate(() => {
        const box = document.querySelector('main table')?.parentElement;
        return box ? box.scrollWidth > box.clientWidth : false;
      }),
    )
    .toBe(true);
  expect(await overflow()).toEqual({ page: 0, main: 0, card: 0 });
  // What's placed absolutely inside the table (the Actions header's hidden words) is held by the table's scroller, so
  // it can't reach past the card when the table is scrolled out of view: with more or wider crawls than this test
  // site's, it took the screen 49 px past the window (the mock app's library, 465 px in 416).
  const loose = await page.evaluate(() => {
    const box = document.querySelector('main table')?.parentElement;
    return (
      [...(box?.querySelectorAll('*') ?? [])]
        // Laid out (not in a folded, hidden column), and placed absolutely.
        .filter((element) => element.getClientRects().length > 0 && getComputedStyle(element).position === 'absolute')
        .filter(
          (element) => !(element instanceof HTMLElement && element.offsetParent && box?.contains(element.offsetParent)),
        )
        .map((element) => element.textContent)
    );
  });
  expect(loose).toEqual([]);
  // The search keeps its place in the card, on a line of its own.
  const search = library.getByRole('searchbox', { name: 'Search the library' });
  await expect(search).toBeVisible();
  const box = await search.boundingBox();
  const cardBox = await library.boundingBox();
  expect(box && cardBox && box.x + box.width <= cardBox.x + cardBox.width).toBe(true);
  await expectNoViolations(page);
});
