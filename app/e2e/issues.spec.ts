// The Issues screen against the real sidecar and its synthetic crawl, which has planted issues: the triage flow of
// docs/v2-ui-ia.md (pick a check, read it, open a group, its URLs, inspect one, Esc, Alt+Left), the list's keyboard,
// and axe at zero violations in both themes and under forced colours.
import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { siteRow } from './support';

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

/** From Home to the crawl's Issues, through the sidebar, as a user goes. */
async function openIssues(page: Page) {
  await page.goto('/');
  await page.getByRole('region', { name: 'Library' }).getByRole('link', { name: siteRow(), exact: true }).click();
  await page
    .getByRole('navigation', { name: 'Main' })
    .getByRole('link', { name: /^Issues/ })
    .first()
    .click();
  await expect(page.getByRole('heading', { name: 'Issues', level: 1 })).toBeVisible();
  // The first check to fix is selected, in the path.
  await expect(page).toHaveURL(/\/crawls\/[0-9a-f]{16}\/issues\/[a-z0-9-]+/);
  await expect(page.getByRole('listbox', { name: 'Checks' })).toBeVisible();
}

test('triage: pick a check, read it, open a group, its URLs, inspect one, then Esc and Alt+Left', async ({ page }) => {
  await openIssues(page);
  await expect(
    page.getByText(
      /^(About [\d,]+ things to change|1 thing to change)[^:]*: \d+ checks found [\d,]+ issues on this crawl\./,
    ),
  ).toBeVisible();

  await page.getByRole('option', { name: /Duplicate title/ }).click();
  await expect(page).toHaveURL(/\/issues\/title-duplicate$/);
  await expect(page.getByRole('heading', { name: 'Duplicate title', level: 2 })).toBeVisible();
  await expect(page.getByText('What’s wrong')).toBeVisible();
  await expect(page.getByText('Why it matters')).toBeVisible();
  await expect(page.getByText('How to fix it')).toBeVisible();

  // Its groups: open the first with the keyboard, as the treegrid pattern has it.
  const groups = page.getByRole('treegrid', { name: /Duplicate title: affected pages/ });
  await groups.focus();
  await page.keyboard.press('ArrowRight');
  await expect(page).toHaveURL(/group=/);
  await expect(groups.getByRole('row', { expanded: true })).toBeVisible();
  await expectNoViolations(page);

  await groups.getByRole('link', { name: /^Show (all \d+ pages|the page) in URLs$/ }).click();
  await expect(page).toHaveURL(/\/urls\?filter=/);
  const chips = page.getByRole('group', { name: 'Filters' });
  await expect(chips).toContainText('Check is Duplicate title');
  await expect(chips).toContainText('Related URL is');

  const urls = page.getByRole('grid', { name: 'URLs' });
  await expect(urls.getByRole('row').nth(1)).toBeVisible();
  // A row picked, then Enter: the inspector.
  await urls.getByRole('gridcell').first().click();
  await page.keyboard.press('Enter');
  const inspector = page.getByRole('complementary', { name: 'Inspector' });
  await expect(inspector).toBeVisible();
  await expect(page).toHaveURL(/inspect=\d+/);

  await page.keyboard.press('Escape');
  await expect(inspector).toBeHidden();
  await page.keyboard.press('Alt+ArrowLeft');
  await expect(page).toHaveURL(/\/issues\/title-duplicate\?group=/);
  await expect(page.getByRole('treegrid').getByRole('row', { expanded: true })).toBeVisible();
});

test('back from a group’s URLs on a small window, the open group is in view in the check’s panel', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 720 });
  await openIssues(page);
  await page.getByRole('option', { name: /Duplicate title/ }).click();
  const groups = page.getByRole('treegrid', { name: /Duplicate title: affected pages/ });
  // The last group, opened from the keyboard.
  await groups.focus();
  await page.keyboard.press('Control+End');
  await page.keyboard.press('Home');
  await page.keyboard.press('ArrowRight');
  await expect(page).toHaveURL(/group=/);
  await groups.getByRole('link', { name: /^Show (all \d+ pages|the page) in URLs$/ }).click();
  await expect(page).toHaveURL(/\/urls\?filter=/);
  await page.keyboard.press('Alt+ArrowLeft');
  await expect(page).toHaveURL(/\/issues\/title-duplicate\?group=/);
  // The panel came back scrolled to its top: the group the route opens is brought into view in it, once the check
  // has drawn (the row can pass through the window while the panel fills in above it).
  const tree = page.getByRole('treegrid');
  await expect(tree.getByRole('row', { level: 2 }).first()).toBeVisible();
  await page.waitForTimeout(500);
  await expect(tree.getByRole('row', { expanded: true })).toBeInViewport();
});

test('the inspector follows the URL grid’s cursor in place: Esc, then Alt+Left, leaves URLs', async ({ page }) => {
  await openIssues(page);
  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: /^URLs/ }).click();
  const urls = page.getByRole('grid', { name: 'URLs' });
  await expect(urls.getByRole('row').nth(2)).toBeVisible();
  const entries = await page.evaluate(() => history.length);
  await urls.getByRole('gridcell').first().click();
  await page.keyboard.press('Enter');
  const inspector = page.getByRole('complementary', { name: 'Inspector' });
  await expect(inspector).toBeVisible();
  const first = new URL(page.url()).searchParams.get('inspect');
  await page.keyboard.press('ArrowDown');
  await expect.poll(() => new URL(page.url()).searchParams.get('inspect')).not.toBe(first);
  // Opening and following are in place: no new history entries.
  expect(await page.evaluate(() => history.length)).toBe(entries);
  await page.keyboard.press('Escape');
  await expect(inspector).toBeHidden();
  await page.keyboard.press('Alt+ArrowLeft');
  await expect(page).toHaveURL(/\/issues\//);
});

test('the inspector follows a flat check’s cursor in place: Esc, then Alt+Left, goes to the check before', async ({
  page,
}) => {
  await openIssues(page);
  const before = page.url();
  await page.getByRole('option', { name: /Indexable page not in a sitemap/ }).click();
  const grid = page.getByRole('grid', { name: /Indexable page not in a sitemap: affected pages/ });
  await expect(grid.getByRole('row').nth(2)).toBeVisible();
  await grid.getByRole('gridcell').first().click();
  await page.keyboard.press('Enter');
  const inspector = page.getByRole('complementary', { name: 'Inspector' });
  await expect(inspector).toBeVisible();
  const first = new URL(page.url()).searchParams.get('inspect');
  await page.keyboard.press('ArrowDown');
  await expect.poll(() => new URL(page.url()).searchParams.get('inspect')).not.toBe(first);
  await page.keyboard.press('Escape');
  await expect(inspector).toBeHidden();
  await page.keyboard.press('Alt+ArrowLeft');
  await expect(page).toHaveURL(before);
});

test('the checks list runs on the keyboard: arrows move the selection and the route, Tab goes to the check', async ({
  page,
}) => {
  await openIssues(page);
  const list = page.getByRole('listbox', { name: 'Checks' });
  const selected = list.getByRole('option', { selected: true });
  await selected.focus();
  const before = page.url();
  await page.keyboard.press('ArrowDown');
  await expect(page).not.toHaveURL(before);
  await expect(list.getByRole('option', { selected: true })).toBeFocused();
  await page.keyboard.press('End');
  await expect(list.getByRole('option').last()).toHaveAttribute('aria-selected', 'true');
  await page.keyboard.press('Tab');
  // Out of the list, into the selected check: its first action.
  await expect(page.locator(':focus')).toHaveAccessibleName(/Copy URLs|Hide this check|Show this check|Learn more/);
  await page.keyboard.press('Control+f');
  await expect(page.getByRole('searchbox', { name: 'Filter checks' })).toBeFocused();
  await page.keyboard.type('sitemap');
  await expect(list.getByRole('option')).not.toHaveCount(0);
  for (const name of await list.getByRole('option').allTextContents()) expect(name.toLowerCase()).toContain('sitemap');
});

test('a command chosen in the palette keeps the focus it takes: “Filter the checks” leaves the keyboard in its field', async ({
  page,
}) => {
  await openIssues(page);
  const list = page.getByRole('listbox', { name: 'Checks' });
  await page.keyboard.press('Control+k');
  const palette = page.getByRole('dialog', { name: 'Command palette' });
  await palette.getByRole('combobox').fill('filter the checks');
  await expect(palette.getByRole('option', { selected: true })).toContainText('Filter the checks');
  await page.keyboard.press('Enter');
  await expect(palette).toBeHidden();
  // The palette gives focus back first, then the command takes it. Run a frame after the close, as it once was,
  // the command had the field for a moment and the closing palette took the focus back to the page.
  const field = page.getByRole('searchbox', { name: 'Filter checks' });
  await expect(field).toBeFocused();
  await page.keyboard.type('sitemap');
  await expect(field).toHaveValue('sitemap');
  await expect(field).toBeFocused();
  for (const name of await list.getByRole('option').allTextContents()) expect(name.toLowerCase()).toContain('sitemap');
});

test('a check that doesn’t group lists its URLs flat, sortable by its detail', async ({ page }) => {
  await openIssues(page);
  await page.getByRole('option', { name: /Indexable page not in a sitemap/ }).click();
  const grid = page.getByRole('grid', { name: /Indexable page not in a sitemap: affected pages/ });
  await expect(grid.getByRole('row').nth(1)).toBeVisible();
  await grid.getByRole('button', { name: 'URL' }).click();
  await expect(page).toHaveURL(/sort=/);
  await expect(grid.getByRole('columnheader', { name: 'URL' })).toHaveAttribute('aria-sort', 'ascending');
  await expectNoViolations(page);
});

for (const colorScheme of ['light', 'dark'] as const) {
  test(`Issues passes axe with a group open (${colorScheme})`, async ({ page }) => {
    await page.emulateMedia({ colorScheme });
    await openIssues(page);
    await page.getByRole('option', { name: /Duplicate title/ }).click();
    const groups = page.getByRole('treegrid');
    await groups.focus();
    await page.keyboard.press('ArrowRight');
    await expect(groups.getByRole('row', { expanded: true })).toBeVisible();
    await expectNoViolations(page);
  });
}

test('under forced colours, the selected check and the open group stay visible, with no violations', async ({
  page,
}) => {
  await page.emulateMedia({ forcedColors: 'active' });
  await openIssues(page);
  const selected = page.getByRole('listbox', { name: 'Checks' }).getByRole('option', { selected: true });
  // Selected is ringed in Highlight (drawn by its ::after, beside the focus ring), not told by a tint that forced
  // colours remove.
  const ring = await selected.evaluate((element) => getComputedStyle(element, '::after').borderTopStyle);
  expect(ring).toBe('solid');
  await page.getByRole('option', { name: /Duplicate title/ }).click();
  const groups = page.getByRole('treegrid');
  await groups.focus();
  await page.keyboard.press('ArrowRight');
  await expect(groups.getByRole('row', { expanded: true })).toBeVisible();
  await expectNoViolations(page);
});
