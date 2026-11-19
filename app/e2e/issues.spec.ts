// The Issues screen against the real sidecar and its synthetic crawl, which has planted issues: the triage flow of
// docs/v2-ui-ia.md (pick a check, read it, open a group, its URLs, inspect one, Esc, Alt+Left), the list's keyboard,
// and axe at zero violations in both themes and under forced colours.
import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Locator, type Page } from '@playwright/test';
import { read, seededCrawl, showChecks, showGroups, siteRow } from './support';

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
  // Under a 1600 px window the open check has the width, and the list is behind "All checks".
  await showChecks(page);
}

interface CatalogCheck {
  id: string;
  title: string;
  description: string;
  category: string;
}

/** The checks the list shows, by their ids, in its order. */
const listedChecks = (list: Locator) =>
  list.getByRole('option').evaluateAll((options) => options.map((option) => option.getAttribute('data-check') ?? ''));

/**
 * The checks filter narrows the list to the checks that mention a word: in the title, the category, what the check
 * says is wrong, or its id (as written, and as words). Worked out from the sidecar's own catalog: exactly those of the
 * checks listed before stay, so one that names the word in its title is in, one that only describes it is in too
 * ("Redirect chain" tells of sitemaps), and one that never mentions it is out.
 */
async function expectNarrowedTo(list: Locator, word: string, before: readonly string[]) {
  const catalog = await read<{ checks: CatalogCheck[]; categories: { id: string; title: string }[] }>('/api/checks');
  const byId = new Map(catalog.checks.map((check) => [check.id, check]));
  const categoryOf = new Map(catalog.categories.map((category) => [category.id, category.title]));
  const mentions = (id: string) => {
    const check = byId.get(id);
    if (!check) return false;
    const category = categoryOf.get(check.category) ?? '';
    return `${check.title} ${category} ${check.description} ${id} ${id.replace(/-/g, ' ')}`
      .toLowerCase()
      .includes(word);
  };
  const kept = before.filter(mentions);
  // The list is narrowed, to something: some checks stay and some go.
  expect(kept.length).toBeGreaterThan(0);
  expect(kept.length).toBeLessThan(before.length);
  await expect.poll(() => listedChecks(list)).toEqual(kept);
  // Every check listed before with the word in its title is still listed, and there is one.
  const titled = before.filter((id) => byId.get(id)?.title.toLowerCase().includes(word));
  expect(titled.length).toBeGreaterThan(0);
  for (const id of titled) await expect(list.locator(`[role="option"][data-check="${id}"]`)).toBeVisible();
}

test('triage: pick a check, read it, open a group, its URLs, inspect one, then Esc and Alt+Left', async ({ page }) => {
  await openIssues(page);
  // What moves the score leads the answer, then the notices' things in the same unit, then the counts (rounds 3 and 4).
  await expect(page.locator('.si-page-header__subtitle')).toHaveText(
    /^(About [\d,]+ things to fix|1 thing to fix|Nothing to fix)[^·]*( · (about )?[\d,]+ (more|things?) worth a look)? · \d+ checks found [\d,]+ issues on this crawl\./,
  );

  await page.getByRole('option', { name: /Duplicate title/ }).click();
  // The check opens as a list of its pages, one row a page: no group in the address.
  await expect(page).toHaveURL(/\/issues\/title-duplicate$/);
  const detail = page.locator('[data-check-detail]');
  await expect(detail.getByRole('heading', { name: 'Duplicate title', level: 2 })).toBeVisible();
  const views = detail.getByRole('radiogroup', { name: 'How the rows are shown' });
  await expect(views.getByRole('radio', { name: 'List' })).toHaveAttribute('aria-checked', 'true');
  const rows = detail.getByRole('grid', { name: 'Duplicate title: affected pages' });
  await expect(rows.getByRole('row').nth(1)).toBeVisible();

  // What's wrong, why it matters and how to fix it are in the check's head, before its rows. A window this short
  // (1280 x 800) folds all three behind one button that names them; opened, they have the pane, and the rows wait
  // behind "Show the rows".
  const explain = detail.locator('header [data-explain]');
  const fold = explain.getByRole('button', { name: 'What’s wrong, why it matters and how to fix it' });
  await expect(fold).toHaveAttribute('aria-expanded', 'false');
  const wrong = explain.getByRole('heading', { name: 'What’s wrong', level: 3 });
  await expect(wrong).toBeHidden();
  await fold.click();
  await expect(fold).toHaveAttribute('aria-expanded', 'true');
  await expect(wrong).toBeVisible();
  await expect(explain.getByRole('heading', { name: 'Why it matters', level: 3 })).toBeVisible();
  await expect(explain.getByRole('heading', { name: 'How to fix it', level: 3 })).toBeVisible();
  await expect(rows).toBeHidden();
  await detail.getByRole('button', { name: 'Show the rows' }).click();
  await expect(rows.getByRole('row').nth(1)).toBeVisible();
  await expect(wrong).toBeHidden();
  await expect(fold).toHaveAttribute('aria-expanded', 'false');

  // Its groups, from "Grouped by …": the first of a few opens by itself, in the address. It closes and opens with
  // the keyboard, as the treegrid pattern has it.
  await showGroups(page);
  const groups = page.getByRole('treegrid', { name: /Duplicate title: affected pages, grouped by / });
  await expect(page).toHaveURL(/group=/);
  await expect(groups.getByRole('row', { expanded: true })).toBeVisible();
  await groups.focus();
  await page.keyboard.press('ArrowLeft');
  await expect(groups.getByRole('row', { expanded: true })).toHaveCount(0);
  await expect(page).not.toHaveURL(/group=/);
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
  await showGroups(page);
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
  // Out of the list, into the selected check: its first action (the fix loop's Recheck leads them).
  // Where the list is behind "All checks", the way back to it leads the check.
  await expect(page.locator(':focus')).toHaveAccessibleName(
    /^All checks|^Recheck|Check the site again|Copy URLs|Hide this check|Show this check|Learn more/,
  );
  await page.keyboard.press('Control+f');
  await expect(page.getByRole('searchbox', { name: 'Filter checks' })).toBeFocused();
  const every = await listedChecks(list);
  await page.keyboard.type('sitemap');
  await expectNarrowedTo(list, 'sitemap', every);
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
  const every = await listedChecks(list);
  await page.keyboard.type('sitemap');
  await expect(field).toHaveValue('sitemap');
  await expect(field).toBeFocused();
  await expectNarrowedTo(list, 'sitemap', every);
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

test('a check’s head keeps Recheck, Copy URLs and Hide on one row, Recheck’s line beneath them at its end', async ({
  page,
}) => {
  await openIssues(page);
  await page.getByRole('option', { name: /Duplicate title/ }).click();
  const head = page.getByRole('article', { name: 'Duplicate title' }).locator('header');
  const recheck = head.getByRole('button', { name: /^Recheck/ }).first();
  // Once the plan is read, its line (what it fetches, or why it can't run now) describes it.
  await expect(recheck).toHaveAttribute('aria-describedby', /.+/, { timeout: 20_000 });
  const line = page.locator(`[id="${await recheck.getAttribute('aria-describedby')}"]`);
  const box = async (locator: Locator) => (await locator.boundingBox())!;
  const first = await box(recheck);
  const copy = await box(head.getByRole('button', { name: 'Copy URLs' }));
  const hide = await box(head.getByRole('button', { name: 'Hide this check' }));
  const words = await box(line);
  const middle = (of: typeof first) => of.y + of.height / 2;
  expect(Math.abs(middle(copy) - middle(first))).toBeLessThan(1);
  expect(Math.abs(middle(hide) - middle(first))).toBeLessThan(1);
  expect(words.y).toBeGreaterThanOrEqual(Math.max(first.y + first.height, copy.y + copy.height, hide.y + hide.height));
  expect(Math.abs(words.x + words.width - (hide.x + hide.width))).toBeLessThan(1);
});

for (const colorScheme of ['light', 'dark'] as const) {
  test(`Issues passes axe with a group open (${colorScheme})`, async ({ page }) => {
    await page.emulateMedia({ colorScheme });
    await openIssues(page);
    await page.getByRole('option', { name: /Duplicate title/ }).click();
    // The check opens as a list: its groups from "Grouped by …", the first of a few opening by itself.
    await showGroups(page);
    const groups = page.getByRole('treegrid');
    await expect(groups.getByRole('row', { expanded: true })).toBeVisible();
    await groups.focus();
    await page.keyboard.press('ArrowDown');
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
  await showGroups(page);
  const groups = page.getByRole('treegrid');
  await expect(groups.getByRole('row', { expanded: true })).toBeVisible();
  await groups.focus();
  await page.keyboard.press('ArrowDown');
  await expect(groups.getByRole('row', { expanded: true })).toBeVisible();
  await expectNoViolations(page);
});

// The owner's task (the UX round of 2026-11-19, A-01, A-06, A-07): from a broken URL to the page that holds the link,
// at the link, with no expanding and one click; and back from Links, the check is as it was left.
test('the 404s name the page that holds each broken link, open it at the link in one click, and keep their place', async ({
  page,
}) => {
  const crawl = await seededCrawl();
  await page.goto(`/crawls/${crawl.id}/issues/internal-client-error`);
  const grid = page.getByRole('treegrid', { name: /Internal URL returns 4xx: affected URLs/ });
  // On arrival, a list: each broken URL with the page linking to it, the link's words and where on the page it is.
  await expect(grid.getByRole('columnheader', { name: 'Linked from' })).toBeVisible();
  await expect(grid.getByRole('columnheader', { name: 'Link text' })).toBeVisible();
  const source = grid.locator('[data-where="source"]').first();
  await expect(source).toBeVisible();
  // The linking page is a link, with its open button directly after it, always there: it opens the page, at the
  // link's words where the link has some.
  const open = source.locator('[data-open-url]');
  await expect(open).toBeVisible();
  const target = (await open.getAttribute('data-open-url')) ?? '';
  expect(target).toMatch(/^https?:\/\//);
  const [popup] = await Promise.all([page.waitForEvent('popup'), open.click()]);
  await popup.close();
  // Its text inspects the linking page: one click, and the check stays.
  await source.locator('.si-url__go').click();
  await expect(page).toHaveURL(/\/issues\/internal-client-error\?.*inspect=\d+/);
  await expect(page.getByRole('complementary', { name: 'Inspector' })).toBeVisible();
  await page.keyboard.press('Escape');

  // One scroller: the rows' own. The card around them never scrolls.
  const scrollers = await page.locator('[data-check-detail]').evaluate((card) =>
    [card, ...card.querySelectorAll('*')]
      .filter((element) => {
        const style = getComputedStyle(element);
        return /auto|scroll/.test(style.overflowY) && element.scrollHeight > element.clientHeight + 1;
      })
      .map((element): string => (element === card ? 'card' : (element.getAttribute('class') ?? ''))),
  );
  expect(scrollers.filter((name) => !name.includes('si-grid'))).toEqual([]);

  // Away to Links and back: the rows are where they were left.
  // The check with the most rows on the seeded crawl (the pages its sitemaps leave out): more than its pane holds.
  const titles = page.getByRole('grid', { name: /Indexable page not in a sitemap: affected pages/ });
  await page.goto(`/crawls/${crawl.id}/issues/not-in-sitemap`);
  await expect(titles.getByRole('row').nth(2)).toBeVisible();
  const rows = page.locator('[data-check-detail] .si-grid').first();
  const most = await rows.evaluate((element) => element.scrollHeight - element.clientHeight);
  expect(most, 'the check’s rows scroll in their own pane').toBeGreaterThanOrEqual(64);
  const offset = Math.min(most, 96);
  await rows.evaluate((element, top) => {
    element.dispatchEvent(new WheelEvent('wheel'));
    element.scrollTop = top;
  }, offset);
  await expect.poll(() => rows.evaluate((element) => element.scrollTop)).toBe(offset);
  await page
    .getByRole('navigation', { name: 'Main' })
    .getByRole('link', { name: /^Links/ })
    .click();
  await expect(page).toHaveURL(/\/links/);
  await page.goBack();
  await expect(page).toHaveURL(/\/issues\/not-in-sitemap/);
  await expect(titles.getByRole('row').nth(2)).toBeVisible();
  await expect.poll(() => rows.evaluate((element) => element.scrollTop)).toBe(offset);
});
