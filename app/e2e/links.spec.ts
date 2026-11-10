// Links against the real sidecar, on the tests' synthetic crawl, which plants broken links, redirect chains, nofollow
// links and a link with no text (engine/src/ShoutingIguana.TestSite/SyntheticSite.Plants.cs). Every count the screen
// shows is checked against the sidecar's own count for the same filters, so a preset, a facet's value and the
// inspector's "All N in Links" each land on exactly what they said. Axe at zero violations in both themes and under
// forced colours. Nothing here waits for a time: every figure is polled for.
import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Locator, type Page } from '@playwright/test';
import { colourTweensEnd } from './colourTweens';

const env = (name: string) => {
  const value = process.env[name];
  if (!value) throw new Error(`global-setup sets ${name}`);
  return value;
};
const site = () => env('SI_E2E_SITE');

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

/** The sidecar, from the test itself: what it counts for a view is what the screen must show. */
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
  crawl = library.find((item) => (item.site ?? item.fileName) === site())?.id ?? '';
  expect(crawl).not.toBe('');
});

const links = (filters: Filter[], more: Record<string, unknown> = {}) =>
  api<LinksPage>(`/api/crawls/${crawl}/links`, { filters, sort: [], limit: 50, ...more });

const PRESETS: { name: string; filters: Filter[]; chip: RegExp; planted: string | RegExp }[] = [
  {
    name: 'Broken links',
    filters: [{ column: 'to.statusClass', op: 'in', values: ['4xx', '5xx'] }],
    chip: /Target status class is 4xx or 5xx/,
    // A product page's link to a page that isn't there.
    planted: 'Broken link',
  },
  {
    name: 'Links to redirects',
    filters: [{ column: 'to.statusClass', op: 'in', values: ['3xx'] }],
    chip: /Target status class is 3xx/,
    // The hub's link into a chain of three redirects.
    planted: 'Planted page chain-a',
  },
  {
    name: 'Nofollow links',
    filters: [{ column: 'noFollow', op: 'is', value: true }],
    chip: /Nofollow is yes/,
    planted: 'A gallery photo',
  },
  {
    name: 'No anchor text',
    filters: [
      { column: 'kind', op: 'in', values: ['hyperlink'] },
      { column: 'text', op: 'isEmpty' },
    ],
    chip: /Anchor or alt text is empty/,
    // The link has no text to find it by: the page it's on is the planted one.
    planted: /\/plants\/empty-anchor$/,
  },
  {
    name: 'External links',
    filters: [{ column: 'to.isInternal', op: 'is', value: false }],
    chip: /Target is on another site/,
    planted: 'Partner site',
  },
];

const main = (page: Page) => page.getByRole('main');
const grid = (page: Page) => page.getByRole('grid', { name: 'Links' });
const inspectorOf = (page: Page) => page.getByRole('complementary', { name: 'Inspector' });
const number = (text: string | null) => Number((text ?? '').replace(/[^\d]/g, ''));
/** The header's count: the "31" of "31 of 5,410 links". */
const shown = async (page: Page) => number(await main(page).locator('.si-result-count__n').innerText());
const expectShown = (page: Page, expected: number) => expect.poll(() => shown(page)).toBe(expected);

/** From Home to the seeded crawl's Links, by the sidebar, as a user goes. */
async function openLinks(page: Page) {
  await page.goto('/');
  await page.getByRole('region', { name: 'Library' }).getByRole('link', { name: site(), exact: true }).click();
  await page
    .getByRole('navigation', { name: 'Main' })
    .getByRole('link', { name: /^Links/ })
    .first()
    .click();
  await expect(page).toHaveURL(/\/crawls\/[0-9a-f]{16}\/links$/);
  await expect(main(page).getByRole('heading', { name: 'Links', level: 1 })).toBeVisible();
  await expect(grid(page).getByRole('row').nth(1)).toBeVisible();
}

async function expectNoViolations(page: Page) {
  await colourTweensEnd(page);
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

test('opens from the sidebar on every link, in crawl order, with what each is and how its target answered', async ({
  page,
}) => {
  await openLinks(page);
  const all = await links([], { columns: ['from.url', 'to.url', 'kind', 'text'] });
  await expectShown(page, all.total);
  await expect(main(page).locator('.si-result-count')).toHaveText(
    new RegExp(`^${all.total.toLocaleString('en-GB')}\\s*links$`),
  );
  await expect(
    grid(page)
      .getByRole('columnheader')
      .evaluateAll((cells) => cells.map((cell) => cell.textContent)),
  ).resolves.toEqual(['Source URL', 'Target URL', 'Anchor or alt text', 'Kind', 'Position', 'Follow', 'Target status']);
  // The first rows are the crawl's first links, each end by its path on the site.
  const origin = new URL(String(all.rows[0]?.['from.url'])).origin;
  for (const [index, link] of all.rows.slice(0, 5).entries()) {
    const row = grid(page)
      .getByRole('row')
      .nth(index + 1);
    for (const [cell, end] of [
      [0, 'from.url'],
      [1, 'to.url'],
    ] as const) {
      // The whole URL is the cell's name; what shows of it is its path on the site.
      const url = row.getByRole('gridcell').nth(cell).getByRole('button');
      await expect(url).toHaveAccessibleName(String(link[end]));
      await expect(url.locator('.si-url__shown')).toHaveText(String(link[end]).replace(origin, ''));
    }
    await expect(row.getByRole('gridcell').nth(2)).toHaveText(
      link.text === null || link.text === '' ? 'None' : String(link.text),
    );
  }
  // All links is the view: no filter, and its preset says so.
  await expect(main(page).getByRole('button', { name: 'All links' })).toHaveAttribute('aria-pressed', 'true');
  await expect(main(page).getByRole('button', { name: 'Add filter' })).toBeVisible();
});

test('each preset lands on its planted count, with its filters as chips and its planted link among the rows', async ({
  page,
}) => {
  await openLinks(page);
  const total = (await links([])).total;
  for (const preset of PRESETS) {
    const planted = await links(preset.filters);
    expect(planted.matching, preset.name).toBeGreaterThan(0);
    await main(page).getByRole('button', { name: preset.name, exact: true }).click();
    await expectShown(page, planted.matching);
    await expect(main(page).locator('.si-result-count__of')).toHaveText(`of ${total.toLocaleString('en-GB')} links`);
    await expect(main(page).getByRole('button', { name: preset.name, exact: true })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    await expect(main(page).getByRole('group', { name: 'Filters' })).toContainText(preset.chip);
    await expect(
      typeof preset.planted === 'string'
        ? grid(page).getByText(preset.planted).first()
        : grid(page).getByRole('button', { name: preset.planted }).first(),
    ).toBeVisible();
    // The grid holds as many rows as the count says (its row count has the header's row in it).
    await expect(grid(page)).toHaveAttribute('aria-rowcount', String(planted.matching + 1));
  }
  await main(page).getByRole('button', { name: 'All links' }).click();
  await expectShown(page, total);
  await expect(page).toHaveURL(/\/links$/);
});

test('a facet counts the links in view by value, and a value narrows to exactly its count', async ({ page }) => {
  await openLinks(page);
  const total = (await links([])).total;
  await main(page).getByRole('button', { name: 'Filter by kind' }).click();
  const menu = page.getByRole('menu');
  await expect(menu.getByText(`Kind, of ${total.toLocaleString('en-GB')} links`)).toBeVisible();
  // The values' counts add up to the links in view.
  const counts = await menu
    .getByRole('menuitem')
    .evaluateAll((items) => items.map((item) => item.querySelector('[aria-hidden="true"]')?.textContent ?? ''));
  expect(counts.map(number).reduce((sum, count) => sum + count, 0)).toBe(total);
  const image = menu.getByRole('menuitem', { name: /^Image/ });
  const images = number((await image.getAttribute('aria-label')) ?? (await image.innerText()).split('\n')[1] ?? '');
  expect(images).toBe((await links([{ column: 'kind', op: 'in', values: ['image'] }])).matching);
  await image.click();
  await expectShown(page, images);
  await expect(main(page).getByRole('group', { name: 'Filters' })).toContainText('Kind is Image');
  // The next facet is of the images alone, and a value of it narrows them.
  await main(page).getByRole('button', { name: 'Filter by target status' }).click();
  await expect(
    page.getByRole('menu').getByText(`Target status, of ${images.toLocaleString('en-GB')} links`),
  ).toBeVisible();
  const broken = await links([
    { column: 'kind', op: 'in', values: ['image'] },
    { column: 'to.statusClass', op: 'in', values: ['4xx'] },
  ]);
  await page.getByRole('menu').getByRole('menuitem', { name: /^4xx/ }).click();
  await expectShown(page, broken.matching);
  await expect(grid(page).getByText('A photo that is missing').first()).toBeVisible();
  // Back is the images again, then every link: each view was a place.
  await page.goBack();
  await expectShown(page, images);
  await page.goBack();
  await expectShown(page, total);
});

test('sorts by the target’s status from its header, and says why a link’s own column doesn’t sort', async ({
  page,
}) => {
  await openLinks(page);
  const header = grid(page).getByRole('columnheader', { name: /Target status/ });
  await header.getByRole('button').click();
  await expect(header).toHaveAttribute('aria-sort', 'ascending');
  await expect(page).toHaveURL(/sort=/);
  const lowest = await links([], { columns: ['to.statusCode'], sort: [{ column: 'to.statusCode', desc: false }] });
  await expect(grid(page).getByRole('row').nth(1).getByRole('gridcell').last()).toHaveText(
    String(lowest.rows[0]?.['to.statusCode']),
  );
  await grid(page)
    .getByRole('columnheader', { name: /Target status/ })
    .getByRole('button')
    .click();
  await expect(grid(page).getByRole('columnheader', { name: /Target status/ })).toHaveAttribute(
    'aria-sort',
    'descending',
  );
  const highest = await links([], { columns: ['to.statusCode'], sort: [{ column: 'to.statusCode', desc: true }] });
  expect(Number(highest.rows[0]?.['to.statusCode'])).toBeGreaterThanOrEqual(400);
  await expect(grid(page).getByRole('row').nth(1).getByRole('gridcell').last()).toHaveText(
    String(highest.rows[0]?.['to.statusCode']),
  );
  // A link's own column has no button, and its header says why.
  const text = grid(page).getByRole('columnheader', { name: 'Anchor or alt text' });
  await expect(text.getByRole('button')).toHaveCount(0);
  await expect(text).toHaveAttribute('title', /Links can’t be sorted by anchor or alt text/);
  // The third press is crawl order again, and the route says nothing of a sort.
  await grid(page)
    .getByRole('columnheader', { name: /Target status/ })
    .getByRole('button')
    .click();
  await expect(page).not.toHaveURL(/sort=/);
});

/** The Links grid on the broken links, the inspector opened on a broken page of the site by its URL in a row. */
async function inspectBrokenTarget(page: Page): Promise<{ inspector: Locator; target: string; broken: number }> {
  await openLinks(page);
  const broken = await links(PRESETS[0]!.filters, { columns: ['to.url', 'to.id', 'to.isInternal', 'kind'] });
  await main(page).getByRole('button', { name: 'Broken links' }).click();
  await expectShown(page, broken.matching);
  // A hyperlink to a page of the site that isn't there: the product pages' planted "Broken link".
  const link = broken.rows.find((row) => row['to.isInternal'] === true && row.kind === 'hyperlink');
  if (!link) throw new Error('The crawl has no broken hyperlink to its own site');
  const target = String(link['to.url']);
  await grid(page).getByRole('button', { name: target, exact: true }).first().click();
  const inspector = inspectorOf(page);
  await expect(inspector).toBeVisible();
  await expect(inspector.getByRole('heading', { level: 2 })).toContainText(new URL(target).pathname);
  await expect(page).toHaveURL(new RegExp(`inspect=${String(link['to.id'])}(&|$)`));
  return { inspector, target, broken: broken.matching };
}

test('opens a link’s target in the inspector, walks to a page that links to it, and walks back', async ({ page }) => {
  const { inspector, target, broken } = await inspectBrokenTarget(page);
  await inspector.getByRole('tab', { name: /^Inlinks/ }).click();
  const inlinks = inspector.getByRole('grid', { name: 'Inlinks' });
  const source = inlinks.getByRole('row').nth(1).getByRole('button').first();
  const sourceUrl = (await source.getAttribute('aria-label')) ?? (await source.innerText());
  await source.click();
  // A step of the walk: the inspector is on the page the link is on, still on Inlinks.
  await expect(inspector.getByRole('heading', { level: 2 })).not.toContainText(new URL(target).pathname);
  await expect(inspector.getByRole('tab', { name: /^Inlinks/ })).toHaveAttribute('aria-selected', 'true');
  expect(sourceUrl).not.toBe('');
  await page.goBack();
  await expect(inspector.getByRole('heading', { level: 2 })).toContainText(new URL(target).pathname);
  // Esc closes it to the view as it was: the broken links, with nothing inspected.
  await page.keyboard.press('Escape');
  await expect(inspector).toBeHidden();
  await expect(page).not.toHaveURL(/inspect=/);
  await expectShown(page, broken);
});

test('opens the page a link is on from the row’s source, with Enter in its column', async ({ page }) => {
  await openLinks(page);
  const first = await links([], { columns: ['from.id', 'to.id'] });
  await grid(page).getByRole('row').nth(2).getByRole('gridcell').nth(2).click();
  // The cursor is in a column of the link itself: Enter opens its target.
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(new RegExp(`inspect=${String(first.rows[1]?.['to.id'])}(&|$)`));
  await expect(inspectorOf(page)).toBeVisible();
  // In the source's column, the page the link is on; and the inspector then follows the cursor down that end.
  await page.keyboard.press('Home');
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(new RegExp(`inspect=${String(first.rows[1]?.['from.id'])}(&|$)`));
  await page.keyboard.press('ArrowRight');
  await page.keyboard.press('Enter');
  await page.keyboard.press('ArrowDown');
  await expect(page).toHaveURL(new RegExp(`inspect=${String(first.rows[2]?.['to.id'])}(&|$)`));
});

test('“All N in Links” from the inspector lists exactly the links its section counted', async ({ page }) => {
  const { inspector, target } = await inspectBrokenTarget(page);
  for (const [tab, section, end] of [
    [/^Inlinks/, 'Inlinks', 'Target URL'],
    [/^Outlinks/, 'Outlinks', 'Source URL'],
  ] as const) {
    await inspector.getByRole('tab', { name: tab }).click();
    await expect(inspector.getByRole('tab', { name: tab })).toHaveAttribute('aria-selected', 'true');
    // The section says how many links it has once it has counted them: only then is there a link, or none.
    const said = inspector.getByText(new RegExp(`^[\\d,]+ ${section.toLowerCase().slice(0, -1)}s?$`));
    await expect(said).toBeVisible();
    const all = inspector.getByRole('link', { name: /^All [\d,]+ in Links$/ });
    // A URL with no links one way has nothing to list in Links: its section says so instead.
    if (number(await said.innerText()) === 0) {
      await expect(inspector.getByText(`No ${section.toLowerCase()}`)).toBeVisible();
      await expect(all).toHaveCount(0);
      continue;
    }
    const counted = number(await all.innerText());
    expect(counted).toBe(number(await said.innerText()));
    await all.click();
    await expect(inspectorOf(page)).toBeHidden();
    await expectShown(page, counted);
    const bar = main(page).getByRole('group', { name: 'Filters' });
    await expect(bar).toContainText(`${end} is ${new URL(target).pathname}`);
    await expect(grid(page)).toHaveAttribute('aria-rowcount', String(counted + 1));
    // Back is the inspector on the URL, where it was.
    await page.goBack();
    await expect(inspectorOf(page).getByRole('heading', { level: 2 })).toContainText(new URL(target).pathname);
  }
});

test('“All N in Links” keeps the kind chosen, and leaves out a page’s links to itself as the section does', async ({
  page,
}) => {
  // The home page: hundreds of inlinks of several kinds, and a canonical and links to itself.
  const home = await api<{ rows: { id: number; url: string }[] }>(`/api/crawls/${crawl}/urls`, {
    columns: ['url'],
    filters: [{ column: 'source', op: 'in', values: ['seed'] }],
    sort: [],
    limit: 1,
  });
  const id = home.rows[0]?.id ?? 0;
  await openLinks(page);
  await page.goto(`${new URL(page.url()).pathname}?inspect=${id}`);
  const inspector = inspectorOf(page);
  await inspector.getByRole('tab', { name: /^Inlinks/ }).click();
  await inspector.getByRole('button', { name: /^Kind of link/ }).click();
  await page.getByRole('menuitemradio', { name: 'Hyperlink' }).click();
  const all = inspector.getByRole('link', { name: /^All [\d,]+ in Links$/ });
  const counted = number(await all.innerText());
  const withSelf = await links([
    { column: 'to.id', op: 'eq', value: id },
    { column: 'kind', op: 'in', values: ['hyperlink'] },
  ]);
  // The grid has more links to the page than the section lists: the page's own links to itself.
  expect(withSelf.matching).toBeGreaterThan(counted);
  await all.click();
  await expectShown(page, counted);
  const bar = main(page).getByRole('group', { name: 'Filters' });
  await expect(bar).toContainText('Target URL is /');
  await expect(bar).toContainText('Link doesn’t lead to its own page');
  await expect(bar).toContainText('Kind is Hyperlink');
});

test('filters on the page a link is on, from the filter’s editor', async ({ page }) => {
  await openLinks(page);
  await main(page).getByRole('button', { name: 'Add filter' }).click();
  const editor = page.getByRole('dialog', { name: 'Add a filter' });
  await editor.getByRole('button', { name: /^About:/ }).click();
  await page.getByRole('menuitemradio', { name: 'Its source page' }).click();
  await editor.getByRole('button', { name: /^Column:/ }).click();
  await page.getByRole('menuitemradio', { name: 'URL', exact: true }).click();
  await editor.getByRole('button', { name: /^Condition:/ }).click();
  await page.getByRole('menuitemradio', { name: 'ends with' }).click();
  await editor.getByRole('textbox', { name: 'Text' }).fill('/plants/');
  await editor.getByRole('textbox', { name: 'Text' }).press('Enter');
  const fromHub = await links([{ column: 'from.url', op: 'endsWith', value: '/plants/' }]);
  expect(fromHub.matching).toBeGreaterThan(0);
  await expectShown(page, fromHub.matching);
  await expect(editor).toBeHidden();
  const chip = main(page).getByRole('button', { name: 'Source URL ends with /plants/', exact: true });
  await expect(chip).toBeVisible();
  // The editor gave the keyboard back to the bar; the chip opens it again, on the filter as it is.
  await chip.click();
  await expect(
    page.getByRole('dialog', { name: 'Change the filter' }).getByRole('textbox', { name: 'Text' }),
  ).toHaveValue('/plants/');
  await page.keyboard.press('Escape');
  await expect(chip).toBeFocused();
  await chip.press('Delete');
  await expectShown(page, (await links([])).total);
  await expect(main(page).getByRole('button', { name: 'Add filter' })).toBeFocused();
});

test('searches with Ctrl+F, shows more columns from the chooser, and its presets are in the palette', async ({
  page,
}) => {
  await openLinks(page);
  await page.keyboard.press('Control+f');
  const search = main(page).getByRole('searchbox', { name: 'Search anchor text and URLs' });
  await expect(search).toBeFocused();
  await search.fill('chain-a');
  await search.press('Enter');
  const found = await links([], { search: 'chain-a' });
  expect(found.matching).toBeGreaterThan(0);
  await expectShown(page, found.matching);
  await expect(search).toBeFocused();
  // Another column, from the chooser: the grid keeps its count while the rows come again with it.
  await main(page).getByRole('button', { name: 'Columns' }).click();
  const chooser = page.getByRole('dialog', { name: 'Columns' });
  await chooser.getByRole('searchbox', { name: 'Find a column' }).fill('depth');
  await chooser.getByRole('group', { name: 'Its target' }).getByRole('checkbox', { name: 'Depth' }).click();
  await expect(grid(page).getByRole('columnheader', { name: /Target depth/ })).toBeVisible();
  await expectShown(page, found.matching);
  await page.keyboard.press('Escape');
  await expect(chooser).toBeHidden();
  // The palette has the screen's views.
  await page.keyboard.press('Control+k');
  await page.getByRole('combobox').fill('Show broken links');
  await page.keyboard.press('Enter');
  await expectShown(page, (await links(PRESETS[0]!.filters)).matching);
  await expect(main(page).getByRole('button', { name: 'Broken links' })).toHaveAttribute('aria-pressed', 'true');
});

test('has no accessibility violations, in both themes and under forced colours', async ({ page }) => {
  await openLinks(page);
  await main(page).getByRole('button', { name: 'Broken links' }).click();
  await expectShown(page, (await links(PRESETS[0]!.filters)).matching);
  for (const colorScheme of ['light', 'dark'] as const) {
    await page.emulateMedia({ colorScheme });
    await expectNoViolations(page);
  }
  // With a facet's menu, the filter's editor and the column chooser open in turn.
  await page.emulateMedia({ colorScheme: 'light' });
  await main(page).getByRole('button', { name: 'Filter by kind' }).click();
  await expect(page.getByRole('menu')).toBeVisible();
  await expectNoViolations(page);
  await page.keyboard.press('Escape');
  await main(page).getByRole('button', { name: 'Add filter' }).click();
  await expect(page.getByRole('dialog', { name: 'Add a filter' })).toBeVisible();
  await expectNoViolations(page);
  await page.keyboard.press('Escape');
  await main(page).getByRole('button', { name: 'Columns' }).click();
  await expect(page.getByRole('dialog', { name: 'Columns' })).toBeVisible();
  await page.emulateMedia({ colorScheme: 'dark' });
  await expectNoViolations(page);
  await page.keyboard.press('Escape');
  await page.emulateMedia({ colorScheme: 'light', forcedColors: 'active' });
  await expectNoViolations(page);
  // With the inspector open on a link's target, under forced colours and then in the dark theme.
  await grid(page).getByRole('row').nth(1).getByRole('button').nth(1).click();
  await expect(inspectorOf(page)).toBeVisible();
  await expectNoViolations(page);
  await page.emulateMedia({ colorScheme: 'dark', forcedColors: 'none' });
  await expectNoViolations(page);
});

// 200% zoom of a 1280 x 720 window: a viewport half as wide, in CSS pixels, as WCAG 1.4.4 and 1.4.10 measure it.
const SIZES = [
  { width: 1280, height: 720 },
  { width: 640, height: 360 },
];

test('holds at 1280x720 and at 200% zoom: nothing overprints, and the toolbar wraps', async ({ page }) => {
  await page.setViewportSize(SIZES[0]!);
  await openLinks(page);
  await main(page).getByRole('button', { name: 'Broken links' }).click();
  await expectShown(page, (await links(PRESETS[0]!.filters)).matching);
  for (const size of SIZES) {
    await page.setViewportSize(size);
    // The sidebar narrows over its transition, and the toolbar wraps: measure once it's done.
    await page.waitForFunction(() => document.getAnimations().length === 0);
    await expect(grid(page).getByRole('row').nth(1)).toBeVisible();
    // Nothing of the screen is wider than the window: the page never scrolls sideways, only the grid does.
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    // The toolbar's controls are all in the window, none under another.
    const boxes = await main(page)
      .locator('header input, [role="group"] button, button:has-text("Columns"), button:has-text("Copy")')
      .evaluateAll((controls) =>
        controls.map((control) => {
          const box = control.getBoundingClientRect();
          return {
            name: control.textContent !== '' ? control.textContent : (control.getAttribute('aria-label') ?? ''),
            left: box.left,
            right: box.right,
            top: box.top,
            bottom: box.bottom,
          };
        }),
      );
    expect(boxes.length).toBeGreaterThanOrEqual(14);
    for (const [index, a] of boxes.entries()) {
      expect(a.right, `${a.name} at ${size.width}`).toBeLessThanOrEqual(size.width + 1);
      expect(a.left, `${a.name} at ${size.width}`).toBeGreaterThanOrEqual(0);
      for (const b of boxes.slice(index + 1)) {
        const overlap = a.left < b.right - 1 && b.left < a.right - 1 && a.top < b.bottom - 1 && b.top < a.bottom - 1;
        // A chip's remove button sits inside its chip's own box: that's one control, not two overprinting.
        const nested =
          (a.left <= b.left && a.right >= b.right && a.top <= b.top && a.bottom >= b.bottom) ||
          (b.left <= a.left && b.right >= a.right && b.top <= a.top && b.bottom >= a.bottom);
        expect(overlap && !nested, `${a.name} and ${b.name} overlap at ${size.width}`).toBe(false);
      }
    }
    // The count is whole, on one line.
    const count = main(page).locator('.si-result-count');
    expect(await count.evaluate((element) => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
  }
});
