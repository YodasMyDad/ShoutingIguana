// What can only be seen in a real browser: state indicators' contrast in both themes and under forced colours (WCAG
// 2.2 AA, 1.4.11 and 1.4.3), focus coming back after an overlay, the grid keeping its cursor in view, and toasts in
// the app's own look. Against the real sidecar, with axe at zero violations.
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

/** WCAG 2.x contrast between two computed colours (`rgb(...)` or `rgba(...)` over white). */
function contrast(foreground: string, background: string): number {
  const channels = (colour: string) => (colour.match(/[\d.]+/g) ?? []).slice(0, 3).map(Number);
  const luminance = (colour: string) => {
    const [r = 0, g = 0, b = 0] = channels(colour).map((value) => {
      const c = value / 255;
      return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
    });
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
  };
  const [light, dark] = [luminance(foreground), luminance(background)].sort((a, b) => b - a);
  return ((light ?? 0) + 0.05) / ((dark ?? 0) + 0.05);
}

/** The colour of an inset box-shadow rule, as computed: `rgb(31, 134, 245) 2px 0px 0px 0px inset`. */
const shadowColour = (shadow: string) => /rgba?\([^)]*\)/.exec(shadow)?.[0] ?? '';

const style = (locator: Locator, ...properties: string[]) =>
  locator.evaluate(
    (element, names) =>
      Object.fromEntries(names.map((name) => [name, getComputedStyle(element).getPropertyValue(name)])),
    properties,
  );

/** The background a translucent or transparent element shows: its nearest ancestor's that paints one. */
const shownBackground = (locator: Locator) =>
  locator.evaluate((element) => {
    for (let node: Element | null = element; node; node = node.parentElement) {
      const colour = getComputedStyle(node).backgroundColor;
      if (colour !== 'rgba(0, 0, 0, 0)' && colour !== 'transparent') return colour;
    }
    return 'rgb(255, 255, 255)';
  });

async function openUrlGrid(page: Page) {
  await page.goto('/');
  await page.getByRole('region', { name: 'Library' }).getByRole('link', { name: site(), exact: true }).click();
  await expect(page).toHaveURL(/\/crawls\/[0-9a-f]{16}\/overview$/);
  // The crawl's commands (Ctrl+1 to Ctrl+7) are there once its view is.
  await expect(page.getByRole('heading', { level: 1, name: 'Overview' })).toBeVisible();
  await page.keyboard.press('Control+3');
  const grid = page.getByRole('grid', { name: 'URLs' });
  await expect(grid.getByRole('row').nth(3)).toBeVisible();
  return grid;
}

/** Selects the grid's second row with a click, and moves the pointer off the rows. */
async function selectRow(page: Page, grid: Locator) {
  const row = grid.locator('[role="row"][aria-rowindex="3"]');
  await row.getByRole('gridcell').first().click();
  await expect(row).toHaveAttribute('aria-selected', 'true');
  await page.mouse.move(0, 0);
  return row;
}

for (const colorScheme of ['light', 'dark'] as const) {
  test(`what's selected, current or highlighted stands out at 3:1, its text at 4.5:1 (${colorScheme})`, async ({
    page,
  }) => {
    await page.emulateMedia({ colorScheme });
    const grid = await openUrlGrid(page);
    const row = await selectRow(page, grid);
    const plainRow = grid.locator('[role="row"][aria-rowindex="5"]');

    // The selected row: its accent rule against the row and against the grid around it.
    const mark = grid.locator('.si-grid-mark').first();
    await expect(mark).toBeVisible();
    const markColour = (await style(mark, 'background-color'))['background-color'] ?? '';
    const rowColours = await style(row, 'background-color', 'color');
    const plainBackground = await shownBackground(plainRow);
    expect(contrast(markColour, rowColours['background-color'] ?? '')).toBeGreaterThanOrEqual(3);
    expect(contrast(markColour, plainBackground)).toBeGreaterThanOrEqual(3);
    expect(contrast(rowColours.color ?? '', rowColours['background-color'] ?? '')).toBeGreaterThanOrEqual(4.5);

    // Hovering it keeps it selected.
    await row.getByRole('gridcell').first().hover();
    await expect(row).toHaveCSS('background-color', rowColours['background-color'] ?? '');
    await plainRow.getByRole('gridcell').first().hover();
    await expect(plainRow).not.toHaveCSS('background-color', plainBackground);
    await page.mouse.move(0, 0);

    // The current section in the sidebar.
    const current = page.getByRole('navigation', { name: 'Main' }).locator('a[aria-current="page"]');
    await expect(current).toHaveText(/URLs/);
    const place = await style(current, 'box-shadow', 'background-color', 'color');
    expect(contrast(shadowColour(place['box-shadow'] ?? ''), place['background-color'] ?? '')).toBeGreaterThanOrEqual(
      3,
    );
    expect(contrast(place.color ?? '', place['background-color'] ?? '')).toBeGreaterThanOrEqual(4.5);

    // A placeholder is a field's only visible label.
    const search = page.getByRole('searchbox', { name: 'Search URLs and titles' });
    const placeholder = await search.evaluate((element) => getComputedStyle(element, '::placeholder').color);
    expect(contrast(placeholder, await shownBackground(search))).toBeGreaterThanOrEqual(4.5);

    // The palette's highlighted option.
    await page.keyboard.press('Control+k');
    const option = page
      .getByRole('dialog', { name: 'Command palette' })
      .locator('[role="option"][aria-selected="true"]');
    await expect(option).toBeVisible();
    const highlight = await style(option, 'box-shadow', 'background-color', 'color');
    expect(
      contrast(shadowColour(highlight['box-shadow'] ?? ''), highlight['background-color'] ?? ''),
    ).toBeGreaterThanOrEqual(3);
    expect(contrast(highlight.color ?? '', highlight['background-color'] ?? '')).toBeGreaterThanOrEqual(4.5);
    await page.keyboard.press('Escape');

    // The windowed grid's thumb isn't there for 40 rows; the native one is the app's colour.
    const thumb = (await style(grid, 'scrollbar-color'))['scrollbar-color'] ?? '';
    expect(contrast(shadowColour(thumb), await shownBackground(grid))).toBeGreaterThanOrEqual(3);
    await expectNoViolations(page);
  });
}

// The design system's own indicators (src/ui): the chosen segment's control edge, a field's edge and its focus ring.
for (const colorScheme of ['light', 'dark'] as const) {
  test(`the chosen segment and a field's edge and focus ring stand out at 3:1 (${colorScheme})`, async ({ page }) => {
    await page.emulateMedia({ colorScheme });
    await page.goto('/settings');
    const density = page.getByRole('radiogroup', { name: 'Density' });
    const chosen = density.getByRole('radio', { checked: true });
    await expect(chosen).toBeVisible();
    const segment = await style(chosen, 'border-top-color', 'background-color', 'color');
    const track = (await style(density, 'background-color'))['background-color'] ?? '';
    const edge = segment['border-top-color'] ?? '';
    // The edge against the track around it and against the segment's own fill; its words on that fill.
    expect(contrast(edge, track)).toBeGreaterThanOrEqual(3);
    expect(contrast(edge, segment['background-color'] ?? '')).toBeGreaterThanOrEqual(3);
    expect(contrast(segment.color ?? '', segment['background-color'] ?? '')).toBeGreaterThanOrEqual(4.5);
    const other = density.getByRole('radio', { checked: false }).first();
    expect(contrast((await style(other, 'color')).color ?? '', track)).toBeGreaterThanOrEqual(4.5);
    await expectNoViolations(page);

    await page.goto('/');
    const field = page.getByRole('textbox', { name: 'Start URL' });
    const box = field.locator('xpath=..');
    const around = await shownBackground(box.locator('xpath=..'));
    expect(contrast((await style(box, 'border-top-color'))['border-top-color'] ?? '', around)).toBeGreaterThanOrEqual(
      3,
    );
    await field.focus();
    const ring = await style(box, 'outline-style', 'outline-color');
    expect(ring['outline-style']).toBe('solid');
    expect(contrast(ring['outline-color'] ?? '', around)).toBeGreaterThanOrEqual(3);
  });
}

test('under forced colours, the chosen segment is outlined and every segment stays readable', async ({ page }) => {
  await page.emulateMedia({ forcedColors: 'active' });
  await page.goto('/settings');
  const density = page.getByRole('radiogroup', { name: 'Density' });
  const chosen = density.getByRole('radio', { checked: true });
  const outline = await style(chosen, 'outline-style', 'outline-color', 'color');
  expect(outline['outline-style']).toBe('solid');
  expect(contrast(outline['outline-color'] ?? '', await shownBackground(chosen))).toBeGreaterThanOrEqual(3);
  for (const segment of await density.getByRole('radio').all()) {
    expect(
      contrast((await style(segment, 'color')).color ?? '', await shownBackground(segment)),
    ).toBeGreaterThanOrEqual(4.5);
  }
});

test('under forced colours, what is selected or current is outlined, and every state stays readable', async ({
  page,
}) => {
  await page.emulateMedia({ forcedColors: 'active' });
  const grid = await openUrlGrid(page);
  const row = await selectRow(page, grid);
  const selected = await style(row, 'outline-style', 'outline-color', 'color', 'background-color');
  expect(selected['outline-style']).toBe('solid');
  expect(contrast(selected['outline-color'] ?? '', await shownBackground(row))).toBeGreaterThanOrEqual(3);
  expect(contrast(selected.color ?? '', await shownBackground(row))).toBeGreaterThanOrEqual(4.5);
  // Hovered, it's still readable (no HighlightText on Canvas).
  await row.hover();
  const hovered = await style(row, 'color');
  expect(contrast(hovered.color ?? '', await shownBackground(row))).toBeGreaterThanOrEqual(4.5);
  await page.mouse.move(0, 0);

  // The grid's own focus, and the cursor on the selected row, by keyboard. Tab goes from the search to the grid when
  // the crawl's analysis stands, as global-setup sees to: an out-of-date one puts its offer to analyse again between
  // them, a Tab stop of its own, in the order they're shown.
  await expect(
    page.getByRole('button', { name: /^Analyse/ }),
    'the crawl’s analysis is out of date: the e2e setup should have made it again or analysed its copy',
  ).toHaveCount(0);
  await page.getByRole('searchbox', { name: 'Search URLs and titles' }).focus();
  await page.keyboard.press('Tab');
  await expect(grid).toBeFocused();
  expect((await style(grid, 'outline-style'))['outline-style']).toBe('solid');
  const cursor = row.locator('[data-active]');
  await expect(cursor).toHaveCount(1);
  const cell = await style(cursor, 'outline-style', 'outline-color');
  expect(cell['outline-style']).toBe('solid');
  expect(contrast(cell['outline-color'] ?? '', await shownBackground(cursor))).toBeGreaterThanOrEqual(3);

  const current = page.getByRole('navigation', { name: 'Main' }).locator('a[aria-current="page"]');
  const place = await style(current, 'outline-style', 'color');
  expect(place['outline-style']).toBe('solid');
  expect(contrast(place.color ?? '', await shownBackground(current))).toBeGreaterThanOrEqual(4.5);

  await page.keyboard.press('Control+k');
  const option = page.getByRole('dialog', { name: 'Command palette' }).locator('[role="option"][aria-selected="true"]');
  await expect(option).toBeVisible();
  const highlight = await style(option, 'outline-style', 'color');
  expect(highlight['outline-style']).toBe('solid');
  expect(contrast(highlight.color ?? '', await shownBackground(option))).toBeGreaterThanOrEqual(4.5);
  await page.keyboard.press('Escape');
});

test('Esc gives focus back to the grid after the palette and the shortcut sheet, which passes axe', async ({
  page,
}) => {
  const grid = await openUrlGrid(page);
  await grid.focus();
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Control+k');
  await expect(page.getByRole('dialog', { name: 'Command palette' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(grid).toBeFocused();

  await page.keyboard.press('Shift+?');
  await expect(page.getByRole('dialog', { name: 'Keyboard shortcuts' })).toBeVisible();
  await expectNoViolations(page);
  await page.keyboard.press('Escape');
  await expect(grid).toBeFocused();
  // The cursor is where it was: the next key moves it.
  const active = await grid.getAttribute('aria-activedescendant');
  await page.keyboard.press('ArrowDown');
  expect(await grid.getAttribute('aria-activedescendant')).not.toBe(active);
});

test('the grid keeps its cursor in view sideways at 1280×720', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 720 });
  const grid = await openUrlGrid(page);
  await grid.focus();
  await page.keyboard.press('End');
  const view = await grid.boundingBox();
  const id = await grid.getAttribute('aria-activedescendant');
  const cell = await page.locator(`[id="${id ?? ''}"]`).boundingBox();
  expect(view && cell).toBeTruthy();
  if (!view || !cell) return;
  expect(cell.x).toBeGreaterThanOrEqual(view.x);
  expect(cell.x + cell.width).toBeLessThanOrEqual(view.x + view.width + 1);
  await page.keyboard.press('Home');
  const first = await page.locator(`[id="${(await grid.getAttribute('aria-activedescendant')) ?? ''}"]`).boundingBox();
  expect(first && first.x >= view.x).toBe(true);
});

test('toasts take the app’s look and focus ring, and follow the OS theme', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'light' });
  await page.goto('/');
  const library = page.getByRole('region', { name: 'Library' });
  await expect(library.getByRole('link', { name: site(), exact: true })).toBeVisible();
  await library.getByRole('button', { name: `Remove ${site()} from the library` }).click();
  const toast = page.locator('[data-sonner-toast]').first();
  await expect(toast).toBeVisible();
  // Sonner fades a toast in: measure it once it's all there.
  await expect(toast).toHaveCSS('opacity', '1');
  const description = toast.locator('[data-title]');
  const read = async () => {
    const colours = await style(toast, 'background-color');
    const text = await style(description, 'color');
    return { background: colours['background-color'] ?? '', text: text.color ?? '' };
  };
  const light = await read();
  expect(contrast(light.text, light.background)).toBeGreaterThanOrEqual(4.5);
  // The OS turns dark while the toast shows: the toast turns with it.
  await page.emulateMedia({ colorScheme: 'dark' });
  const dark = await read();
  expect(dark.background).not.toBe(light.background);
  expect(contrast(dark.text, dark.background)).toBeGreaterThanOrEqual(4.5);
  // Its action, focused from the keyboard, shows the app's focus ring.
  const undo = toast.getByRole('button', { name: 'Undo' });
  await undo.focus();
  await page.keyboard.press('Shift+Tab');
  await page.keyboard.press('Tab');
  await expect(undo).toBeFocused();
  const ring = await style(undo, 'outline-style', 'outline-color');
  expect(ring['outline-style']).toBe('solid');
  expect(contrast(ring['outline-color'] ?? '', dark.background)).toBeGreaterThanOrEqual(3);
  await expect(toast).toHaveCSS('opacity', '1');
  await expectNoViolations(page);
  await undo.click();
  await expect(library.getByRole('link', { name: site(), exact: true })).toBeVisible();
});
