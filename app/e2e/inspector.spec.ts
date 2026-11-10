// The URL inspector against the real sidecar and its synthetic crawl, whose planted pages have known redirects,
// hreflang, broken JSON-LD and stored HTML: flow 4 of docs/v2-ui-ia.md (inspect a URL, walk its inlinks and outlinks,
// back and forward retrace the walk, Esc closes it to the view as it was) with flow 3 (from a check's pages), the
// redirect chain, the source's search, "Go to URL…", and axe at zero violations in both themes and under forced
// colours.
import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Locator, type Page } from '@playwright/test';

const site = () => {
  const value = process.env.SI_E2E_SITE;
  if (!value) throw new Error('global-setup sets SI_E2E_SITE');
  return value;
};

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

const inspectorOf = (page: Page) => page.getByRole('complementary', { name: 'Inspector' });
const inspected = (page: Page) => new URL(page.url()).searchParams.get('inspect');

/** From Home to one of the crawl's sections, through the sidebar, as a user goes. */
async function openSection(page: Page, name: RegExp) {
  await page.goto('/');
  await page.getByRole('region', { name: 'Library' }).getByRole('link', { name: site(), exact: true }).click();
  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name }).first().click();
}

/** The crawl's URLs, searched for a planted page, the inspector opened on its row with Enter. */
async function inspectPlant(page: Page, plant: string): Promise<Locator> {
  await openSection(page, /^URLs/);
  const search = page.getByRole('searchbox', { name: 'Search URLs and titles' });
  await search.fill(`/plants/${plant}`);
  await search.press('Enter');
  await expect(page).toHaveURL(/q=/);
  const grid = page.getByRole('grid', { name: 'URLs' });
  // The row's cells run together as text: the plant's own row is the one whose address ends there.
  const row = grid
    .getByRole('row')
    .filter({ hasText: new RegExp(`/plants/${plant}(?![a-z/-])`) })
    .first();
  await row.getByRole('gridcell').first().click();
  await page.keyboard.press('Enter');
  const inspector = inspectorOf(page);
  await expect(inspector).toBeVisible();
  await expect(inspector.getByRole('heading', { level: 2 })).toContainText(`/plants/${plant}`);
  return inspector;
}

const showTab = async (inspector: Locator, name: RegExp) => {
  await inspector.getByRole('tab', { name }).click();
  await expect(inspector.getByRole('tab', { name })).toHaveAttribute('aria-selected', 'true');
};

test('inspect a URL: walk an inlink, then an outlink; back and forward retrace it; Esc closes to the grid', async ({
  page,
}) => {
  const inspector = await inspectPlant(page, 'hreflang-en');
  await expect(page).toHaveURL(/inspect=\d+/);
  const opened = inspected(page);
  // The summary: how it answered, and whether it can be indexed, with the four tests.
  await expect(inspector.getByRole('heading', { name: 'Indexable', exact: true })).toBeVisible();
  await expect(inspector.getByRole('list', { name: 'Indexability tests' }).getByRole('listitem')).toHaveCount(4);
  await expect(inspector.getByRole('meter', { name: 'Title length' })).toBeVisible();

  // Its inlinks are links themselves: Enter on a row walks to it, a new place in history.
  await showTab(inspector, /^Inlinks/);
  const inlinks = inspector.getByRole('grid', { name: 'Inlinks' });
  await expect(inlinks.getByRole('row').nth(1)).toBeVisible();
  await inlinks.focus();
  await page.keyboard.press('Enter');
  await expect.poll(() => inspected(page)).not.toBe(opened);
  const first = inspected(page);
  // Still on Inlinks, with the keyboard in the new URL's grid.
  await expect(inspector.getByRole('tab', { name: /^Inlinks/ })).toHaveAttribute('aria-selected', 'true');
  await expect(inspector.getByRole('grid', { name: 'Inlinks' })).toBeFocused();

  // Then an outlink, with the pointer.
  await showTab(inspector, /^Outlinks/);
  const outlinks = inspector.getByRole('grid', { name: 'Outlinks' });
  const target = outlinks.getByRole('row').nth(1).getByRole('button').first();
  await expect(target).toBeVisible();
  await target.click();
  await expect.poll(() => inspected(page)).not.toBe(first);
  const second = inspected(page);

  // Back and forward retrace the walk.
  await page.keyboard.press('Alt+ArrowLeft');
  await expect.poll(() => inspected(page)).toBe(first);
  await page.keyboard.press('Alt+ArrowLeft');
  await expect.poll(() => inspected(page)).toBe(opened);
  await expect(inspector.getByRole('heading', { level: 2 })).toContainText('/plants/hreflang-en');
  await page.keyboard.press('Alt+ArrowRight');
  await page.keyboard.press('Alt+ArrowRight');
  await expect.poll(() => inspected(page)).toBe(second);

  // Esc closes it to the grid as it was, however far the walk went; Alt+Left then leaves that view.
  await page.keyboard.press('Escape');
  await expect(inspector).toBeHidden();
  await expect(page).toHaveURL(/\/urls\?q=/);
  expect(inspected(page)).toBeNull();
  await page.keyboard.press('Alt+ArrowLeft');
  await expect(page).not.toHaveURL(/q=/);
  await expect(inspector).toBeHidden();
});

/** The steps the inspector has walked to this history entry, as the history model counts them. */
const walkedSteps = (page: Page) =>
  page.evaluate(() => (history.state as { inspectSteps?: number } | null)?.inspectSteps ?? 0);

/**
 * Walks `steps` inlinks with Enter on the Inlinks grid's first row: every page has a page linking to it, and the
 * keyboard stays in the grid. Each URL's links are a grid of their own, so a step is done when a new grid has rows.
 */
async function walkInlinks(page: Page, inspector: Locator, steps: number) {
  await showTab(inspector, /^Inlinks/);
  const from = await walkedSteps(page);
  for (let step = 1; step <= steps; step++) {
    const grid = inspector.locator('[role="grid"]:not([data-walked-from])');
    await expect(grid.getByRole('row').nth(1)).toBeVisible();
    await grid.focus();
    await grid.evaluate((element) => element.setAttribute('data-walked-from', ''));
    await page.keyboard.press('Enter');
    await expect.poll(() => walkedSteps(page)).toBe(from + step);
  }
  await expect(inspector.locator('[role="grid"]:not([data-walked-from])').getByRole('row').nth(1)).toBeVisible();
}

test('a walk longer than the browser keeps history for still closes with Esc', async ({ page }) => {
  test.setTimeout(120_000);
  const inspector = await inspectPlant(page, 'hreflang-en');
  await walkInlinks(page, inspector, 60);
  // Chromium keeps 50 entries: the one the inspector opened on is gone, and going back 60 would do nothing.
  expect(await page.evaluate(() => history.length)).toBeLessThan(60);
  await page.keyboard.press('Escape');
  await expect(inspector).toBeHidden();
  await expect(page).toHaveURL(/\/urls\?q=/);
  expect(inspected(page)).toBeNull();
});

test('Esc handled twice before history has gone back closes the inspector once, and stays on the view', async ({
  page,
}) => {
  const inspector = await inspectPlant(page, 'hreflang-en');
  await walkInlinks(page, inspector, 2);
  // Two presses handled back to back, as when the app was busy while they were made: the second comes before history
  // has gone back for the first. Had the close run twice, history would go back over the walk twice, out of the view.
  await page.evaluate(() => {
    for (let press = 0; press < 2; press++) {
      (document.activeElement ?? document.body).dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', bubbles: true, cancelable: true }),
      );
    }
  });
  await expect(inspector).toBeHidden();
  await page.waitForTimeout(500);
  await expect(page).toHaveURL(/\/urls\?q=/);
  expect(inspected(page)).toBeNull();

  // The same with real key events, 80 ms apart, while the main thread is busy for 400 ms.
  await page.getByRole('grid', { name: 'URLs' }).focus();
  await page.keyboard.press('Enter');
  await expect(inspector).toBeVisible();
  await walkInlinks(page, inspector, 2);
  const session = await page.context().newCDPSession(page);
  const escape = async () => {
    const key = { key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27 };
    await session.send('Input.dispatchKeyEvent', { type: 'rawKeyDown', ...key });
    await session.send('Input.dispatchKeyEvent', { type: 'keyUp', ...key });
  };
  void page.evaluate(() => {
    setTimeout(() => {
      const end = performance.now() + 400;
      while (performance.now() < end);
    }, 0);
  });
  await page.waitForTimeout(30);
  const first = escape();
  await page.waitForTimeout(80);
  await Promise.all([first, escape()]);
  await expect(inspector).toBeHidden();
  await page.waitForTimeout(500);
  await expect(page).toHaveURL(/\/urls\?q=/);
  expect(inspected(page)).toBeNull();
});

test('from a check’s pages: Enter inspects one, its issue leads back to the check, Esc and Alt+Left', async ({
  page,
}) => {
  await openSection(page, /^Issues/);
  await expect(page.getByRole('listbox', { name: 'Checks' })).toBeVisible();
  await page.getByRole('option', { name: /Invalid JSON-LD/ }).click();
  await expect(page).toHaveURL(/\/issues\/structured-data-invalid$/);
  const groups = page.getByRole('treegrid', { name: /Invalid JSON-LD: affected pages/ });
  await groups.focus();
  await page.keyboard.press('ArrowRight');
  await expect(groups.getByRole('row', { expanded: true })).toBeVisible();
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  const inspector = inspectorOf(page);
  await expect(inspector).toBeVisible();
  await expect(inspector.getByRole('heading', { level: 2 })).toContainText('/plants/jsonld-invalid');

  // The check that flagged it, by the catalog's title, with its severity and detail.
  const issues = inspector.getByRole('list', { name: 'This URL’s issues' });
  await expect(issues.getByRole('link', { name: 'Invalid JSON-LD' })).toBeVisible();
  // The block that doesn't parse: said so, with the parser's words, and its text as text.
  await showTab(inspector, /^Structured data/);
  await expect(inspector.getByText('Doesn’t parse', { exact: true })).toBeVisible();
  await expect(inspector.getByText('It isn’t valid JSON')).toBeVisible();
  await expect(inspector.getByRole('list', { name: 'This URL’s structured data issues' })).toBeVisible();

  // Its issue leads to the check on Issues; back returns to the URL, in the inspector.
  await showTab(inspector, /^Issues/);
  await inspector
    .getByRole('list', { name: 'This URL’s issues' })
    .getByRole('link', { name: 'Invalid JSON-LD' })
    .click();
  await expect(inspector).toBeHidden();
  await expect(page).toHaveURL(/\/issues\/structured-data-invalid/);
  await page.keyboard.press('Alt+ArrowLeft');
  await expect(inspector).toBeVisible();
  await expect(inspector.getByRole('heading', { level: 2 })).toContainText('/plants/jsonld-invalid');

  await page.keyboard.press('Escape');
  await expect(inspector).toBeHidden();
  await expect(page).toHaveURL(/\/issues\/structured-data-invalid\?group=/);
  await page.keyboard.press('Alt+ArrowLeft');
  await expect(page).not.toHaveURL(/inspect=/);
  await expect(inspector).toBeHidden();
});

test('the redirect chain is drawn hop by hop, each hop a link; a loop is called out', async ({ page }) => {
  const inspector = await inspectPlant(page, 'chain-a');
  await showTab(inspector, /^Redirects/);
  await expect(inspector.getByRole('heading', { name: 'Redirects 3 times' })).toBeVisible();
  const steps = inspector.getByRole('list', { name: 'Redirect chain' }).getByRole('listitem');
  await expect(steps).toHaveCount(4);
  await expect(steps.nth(0)).toContainText('301');
  await expect(steps.nth(0)).toContainText('This URL');
  await expect(steps.nth(1)).toContainText('/plants/chain-b');
  await expect(steps.nth(3)).toContainText('200');
  await expect(steps.nth(3)).toContainText('Where it ends');
  // The last hop walks to where the chain ends.
  await steps.nth(3).getByRole('button').first().click();
  await expect(inspector.getByRole('heading', { level: 2 })).toContainText('/plants/thin');
  await expect(inspector.getByRole('heading', { name: 'Doesn’t redirect' })).toBeVisible();

  const looping = await inspectPlant(page, 'loop-a');
  await showTab(looping, /^Redirects/);
  await expect(looping.getByText('A redirect loop')).toBeVisible();
  await expect(looping.getByText(/Back at this URL|the loop closes here/)).toBeVisible();
});

test('the source is the stored HTML as text, highlighted, and its search steps through the matches', async ({
  page,
}) => {
  const inspector = await inspectPlant(page, 'hreflang-en');
  await showTab(inspector, /^Source/);
  const source = inspector.getByRole('textbox', { name: 'HTML source' });
  await expect(source).toBeVisible();
  await expect(source).toContainText('<title>');
  await expect(source).toHaveAttribute('aria-readonly', 'true');
  // The page's markup is text here, never elements; the highlighter's spans are the only markup.
  expect(await source.locator('title, h1, script, link, a').count()).toBe(0);

  const search = inspector.getByRole('searchbox', { name: 'Search the HTML source' });
  await search.fill('hreflang');
  // Where the search is among its matches, as it's read out.
  const count = inspector.getByRole('tabpanel').getByRole('status');
  await expect(count).toHaveText(/^1 of \d+$/);
  await expect(source.locator('.cm-si-match').first()).toBeVisible();
  await expect(source.locator('.cm-si-match-current')).toHaveCount(1);
  await search.press('Enter');
  await expect(count).toHaveText(/^2 of \d+$/);
  await search.press('Shift+Enter');
  await expect(count).toHaveText(/^1 of \d+$/);
  await inspector.getByRole('button', { name: 'Next match' }).click();
  await expect(count).toHaveText(/^2 of \d+$/);

  // Read-only: typing in it changes nothing. Ctrl+F from inside it goes to the search field.
  const before = await source.textContent();
  await source.click();
  await page.keyboard.type('never typed');
  expect(await source.textContent()).toBe(before);
  await page.keyboard.press('Control+f');
  await expect(search).toBeFocused();

  // No match says so; Esc clears the search, and the inspector stays open.
  await search.fill('zzzz-not-in-the-page');
  await expect(count).toHaveText('No matches');
  await search.press('Escape');
  await expect(search).toHaveValue('');
  await expect(inspector).toBeVisible();
});

test('“Go to URL…” looks a path up and opens the inspector on it, from a view without one', async ({ page }) => {
  await openSection(page, /^Overview/);
  await page.keyboard.press('Control+k');
  const palette = page.getByRole('dialog', { name: 'Command palette' });
  await palette.getByRole('combobox').fill('go to url');
  await page.keyboard.press('Enter');
  const field = palette.getByRole('textbox', { name: 'Go to URL' });
  await expect(field).toBeFocused();
  await field.fill('/plants/nowhere-at-all');
  await page.keyboard.press('Enter');
  await expect(palette.getByRole('alert')).toHaveText('This crawl doesn’t have that URL.');
  await field.fill('/plants/thin');
  await page.keyboard.press('Enter');
  await expect(palette).toBeHidden();
  await expect(page).toHaveURL(/\/urls\?inspect=\d+/);
  const inspector = inspectorOf(page);
  await expect(inspector.getByRole('heading', { level: 2 })).toContainText('/plants/thin');
  // Back leaves the URL it went to: the view it was asked from.
  await page.keyboard.press('Alt+ArrowLeft');
  await expect(page).toHaveURL(/\/overview/);
});

test('the sheet resizes from the keyboard, docks at the bottom, and remembers both', async ({ page }) => {
  const inspector = await inspectPlant(page, 'thin');
  const splitter = inspector.getByRole('separator', { name: 'Resize the inspector' });
  const width = async () => (await inspector.boundingBox())?.width ?? 0;
  const before = await width();
  await splitter.focus();
  await page.keyboard.press('ArrowLeft');
  await page.keyboard.press('ArrowLeft');
  await expect.poll(width).toBe(before + 32);
  await inspector.getByRole('button', { name: 'Dock at the bottom' }).click();
  await expect(splitter).toHaveAttribute('aria-orientation', 'horizontal');
  const docked = await inspector.boundingBox();
  expect(docked?.width ?? 0).toBeGreaterThan(before * 2);
  // Remembered across a reload.
  await page.reload();
  await expect(inspectorOf(page).getByRole('button', { name: 'Dock on the right' })).toBeVisible();
  await inspectorOf(page).getByRole('button', { name: 'Dock on the right' }).click();
  await expect.poll(async () => (await inspectorOf(page).boundingBox())?.width ?? 0).toBe(before + 32);
});

const SECTIONS = [
  /^Summary/,
  /^Issues/,
  /^Inlinks/,
  /^Redirects/,
  /^Headers/,
  /^Structured data/,
  /^Hreflang/,
  /^Source/,
];

for (const colorScheme of ['light', 'dark'] as const) {
  test(`the inspector passes axe in every section (${colorScheme})`, async ({ page }) => {
    await page.emulateMedia({ colorScheme });
    const inspector = await inspectPlant(page, 'hreflang-broken');
    for (const section of SECTIONS) {
      await showTab(inspector, section);
      if (section.source.includes('Source')) {
        await expect(inspector.getByRole('textbox', { name: 'HTML source' })).toBeVisible();
        await inspector.getByRole('searchbox', { name: 'Search the HTML source' }).fill('hreflang');
        await expect(inspector.locator('.cm-si-match-current')).toHaveCount(1);
      } else if (section.source.includes('Inlinks')) {
        await expect(inspector.getByRole('grid', { name: 'Inlinks' }).getByRole('row').nth(1)).toBeVisible();
      }
      await expectNoViolations(page);
    }
  });
}

test('under forced colours the inspector keeps its marks, with no violations', async ({ page }) => {
  await page.emulateMedia({ forcedColors: 'active' });
  const inspector = await inspectPlant(page, 'jsonld-invalid');
  await expectNoViolations(page);
  await showTab(inspector, /^Structured data/);
  await expectNoViolations(page);
  await showTab(inspector, /^Source/);
  await expect(inspector.getByRole('textbox', { name: 'HTML source' })).toBeVisible();
  await inspector.getByRole('searchbox', { name: 'Search the HTML source' }).fill('json');
  // The match the search is on is the system's highlight; the others are outlined in it.
  const current = inspector.locator('.cm-si-match-current').first();
  await expect(current).toBeVisible();
  const colours = await current.evaluate((element) => {
    const style = getComputedStyle(element);
    return { background: style.backgroundColor, forced: style.forcedColorAdjust };
  });
  expect(colours.forced).toBe('none');
  expect(colours.background).not.toBe('rgba(0, 0, 0, 0)');
  await expectNoViolations(page);
});
