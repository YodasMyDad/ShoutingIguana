import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

const site = () => {
  const value = process.env.SI_E2E_SITE;
  if (!value) throw new Error('global-setup sets SI_E2E_SITE');
  return value;
};

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
  await expect(library.getByRole('link', { name: site(), exact: true })).toBeVisible();
  await expect(library.getByRole('row')).toHaveCount(2);
  await expectNoViolations(page);
});

test('Home passes in the dark theme too', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.goto('/');
  await expect(
    page.getByRole('region', { name: 'Library' }).getByRole('link', { name: site(), exact: true }),
  ).toBeVisible();
  await expectNoViolations(page);
});

test('the palette opens with Ctrl+K, finds a command and closes with Esc', async ({ page }) => {
  await page.goto('/');
  await expect(
    page.getByRole('region', { name: 'Library' }).getByRole('link', { name: site(), exact: true }),
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
  await page.getByRole('region', { name: 'Library' }).getByRole('link', { name: site(), exact: true }).click();
  await expect(page).toHaveURL(/\/crawls\/[0-9a-f]{16}\/overview$/);
  await expect(page.getByRole('heading', { name: 'Overview', level: 1 })).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: /Issues/ })).toBeVisible();
  await expectNoViolations(page);
});

test('at 200% zoom (640x360) the library keeps its Site column and nothing scrolls sideways', async ({ page }) => {
  await page.setViewportSize({ width: 640, height: 360 });
  await page.goto('/');
  const library = page.getByRole('region', { name: 'Library' });
  const name = library.getByRole('link', { name: site(), exact: true });
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
