// What can only be seen in a real browser: state indicators' contrast in both themes and under forced colours (WCAG
// 2.2 AA, 1.4.11 and 1.4.3), focus coming back after an overlay, the grid keeping its cursor in view, and toasts in
// the app's own look. Against the real sidecar, with axe at zero violations. A state no screen shows yet is measured on
// markup with the design system's classes (`fixture`), against the app's own stylesheet.
import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Locator, type Page } from '@playwright/test';
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

/** A colour with no hue (the framed workspace's neutral fills and text): its channels within a few steps of each other. */
const neutral = (colour: string) => {
  const [r = 0, g = 0, b = 0] = (colour.match(/[\d.]+/g) ?? []).slice(0, 3).map(Number);
  return Math.max(r, g, b) - Math.min(r, g, b) <= 8;
};

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

/** Focuses an element as the keyboard does, so `:focus-visible` matches as it would after Tab. */
async function keyboardFocus(locator: Locator) {
  await locator.page().keyboard.press('Shift');
  await locator.focus();
  expect(await locator.evaluate((element) => element.matches(':focus-visible'))).toBe(true);
}

/**
 * Markup with the design system's classes, added to the app's page: for a state no screen shows yet, measured against
 * the app's own stylesheet. Removed by the next navigation.
 */
async function fixture(page: Page, html: string) {
  await page.evaluate((markup) => {
    document.getElementById('si-fixture')?.remove();
    const host = document.createElement('div');
    host.id = 'si-fixture';
    host.style.padding = '16px';
    host.innerHTML = markup;
    document.body.append(host);
  }, html);
  return page.locator('#si-fixture');
}

/** The focus ring and the selection's ring of a focused element: an outline, and the border of its `::after`. */
const rings = (locator: Locator) =>
  locator.evaluate((element) => {
    const own = getComputedStyle(element);
    const after = getComputedStyle(element, '::after');
    return {
      outlineStyle: own.outlineStyle,
      outlineColour: own.outlineColor,
      outlineOffset: parseFloat(own.outlineOffset),
      ringStyle: after.borderTopStyle,
      ringColour: after.borderTopColor,
      ringWidth: parseFloat(after.borderTopWidth),
    };
  });

async function openUrlGrid(page: Page) {
  await page.goto('/');
  await page.getByRole('region', { name: 'Library' }).getByRole('link', { name: siteRow(), exact: true }).click();
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
  // The URL's own cell opens the inspector. The next cell only selects the row.
  await row.getByRole('gridcell').nth(1).click();
  await expect(row).toHaveAttribute('aria-selected', 'true');
  await page.mouse.move(0, 0);
  return row;
}

for (const colorScheme of ['light', 'dark'] as const) {
  test(`what's selected, current or highlighted stands out with no accent rule, its text at 4.5:1 (${colorScheme})`, async ({
    page,
  }) => {
    await page.emulateMedia({ colorScheme });
    const grid = await openUrlGrid(page);
    const row = await selectRow(page, grid);
    const plainRow = grid.locator('[role="row"][aria-rowindex="5"]');

    // The selected row: the selection's tint alone (the framed workspace took the accent rule from its edge), its own
    // colour beside a plain row and beside a hovered one, its text at 4.5:1.
    await expect(grid.locator('.si-grid-mark')).toHaveCount(0);
    const rowColours = await style(row, 'background-color', 'color', 'box-shadow');
    const plainBackground = await shownBackground(plainRow);
    expect(rowColours['box-shadow']).toBe('none');
    expect(rowColours['background-color']).not.toBe(plainBackground);
    expect(contrast(rowColours.color ?? '', rowColours['background-color'] ?? '')).toBeGreaterThanOrEqual(4.5);

    // Hovering it keeps it selected; a hovered plain row takes another colour than the selection's.
    await row.getByRole('gridcell').first().hover();
    await expect(row).toHaveCSS('background-color', rowColours['background-color'] ?? '');
    await plainRow.getByRole('gridcell').first().hover();
    await expect(plainRow).not.toHaveCSS('background-color', plainBackground);
    await expect(plainRow).not.toHaveCSS('background-color', rowColours['background-color'] ?? '');
    await page.mouse.move(0, 0);

    // The current section in the sidebar: a solid neutral fill on the frame, its text near-black (white) at 600, no
    // accent rule and no accent text.
    const current = page.getByRole('navigation', { name: 'Main' }).locator('a[aria-current="page"]');
    await expect(current).toHaveText(/URLs/);
    const place = await style(current, 'box-shadow', 'background-color', 'color', 'font-weight');
    const frame = await shownBackground(page.getByRole('navigation', { name: 'Main' }));
    expect(place['box-shadow']).toBe('none');
    expect(place['background-color']).not.toBe(frame);
    expect(neutral(place['background-color'] ?? '')).toBe(true);
    expect(neutral(place.color ?? '')).toBe(true);
    expect(place['font-weight']).toBe('600');
    expect(contrast(place.color ?? '', place['background-color'] ?? '')).toBeGreaterThanOrEqual(4.5);
    const plainPlace = page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Links', exact: true });
    expect((await style(plainPlace, 'font-weight'))['font-weight']).toBe('500');

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
    const dialog = await shownBackground(page.getByRole('dialog', { name: 'Command palette' }));
    expect(highlight['box-shadow']).toBe('none');
    expect(highlight['background-color']).not.toBe(dialog);
    expect(neutral(highlight['background-color'] ?? '')).toBe(true);
    expect(neutral(highlight.color ?? '')).toBe(true);
    expect(contrast(highlight.color ?? '', highlight['background-color'] ?? '')).toBeGreaterThanOrEqual(4.5);
    await page.keyboard.press('Escape');

    // The windowed grid's thumb isn't there for 40 rows; the native one is the app's colour: Chromium draws it by its
    // ::-webkit-scrollbar parts (base.css), which apply only while the element sets no scrollbar-color.
    expect((await style(grid, 'scrollbar-color'))['scrollbar-color']).toBe('auto');
    const thumb = await grid.evaluate((element) => {
      const probe = document.createElement('span');
      probe.style.color = 'var(--color-border-control)';
      element.append(probe);
      const colour = getComputedStyle(probe).color;
      probe.remove();
      return colour;
    });
    expect(contrast(thumb, await shownBackground(grid))).toBeGreaterThanOrEqual(3);
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

test('under forced colours, the chosen segment is ringed and every segment stays readable', async ({ page }) => {
  await page.emulateMedia({ forcedColors: 'active' });
  await page.goto('/settings');
  const density = page.getByRole('radiogroup', { name: 'Density' });
  const chosen = density.getByRole('radio', { checked: true });
  const ring = await rings(chosen);
  expect(ring.ringStyle).toBe('solid');
  expect(ring.ringWidth).toBeGreaterThanOrEqual(2);
  expect(contrast(ring.ringColour, await shownBackground(chosen))).toBeGreaterThanOrEqual(3);
  for (const segment of await density.getByRole('radio').all()) {
    expect(
      contrast((await style(segment, 'color')).color ?? '', await shownBackground(segment)),
    ).toBeGreaterThanOrEqual(4.5);
  }
});

for (const colorScheme of ['light', 'dark'] as const) {
  test(`under forced colours, focus shows beside what's chosen, pressed, current or selected (${colorScheme})`, async ({
    page,
  }) => {
    await page.emulateMedia({ forcedColors: 'active', colorScheme });
    await page.goto('/settings');
    const background = await shownBackground(page.locator('main'));
    const chosen = page.getByRole('radiogroup', { name: 'Density' }).getByRole('radio', { checked: true });
    const current = page.getByRole('navigation', { name: 'Settings' }).locator('a[aria-current="page"]');
    await expect(current).toHaveCount(1);
    const host = await fixture(
      page,
      `<button type="button" class="si-btn si-btn--secondary" aria-pressed="true" id="pressed">Show passed</button>
       <div style="width:240px"><button type="button" class="si-list-row si-list-row--plain" aria-selected="true" id="row">
         <span class="si-list-row__title">Robots.txt</span><span></span></button>
       <a href="#here" class="si-list-row si-list-row--plain" aria-current="page" id="place">
         <span class="si-list-row__title">Sitemaps</span><span></span></a></div>`,
    );
    for (const target of [chosen, current, host.locator('#pressed'), host.locator('#row'), host.locator('#place')]) {
      await keyboardFocus(target);
      const seen = await rings(target);
      const name = (await target.textContent()) ?? '';
      // The focus ring: an outline outside the edge. The selection: a ring of its own inside it, in another colour.
      expect(seen.outlineStyle, name).toBe('solid');
      expect(seen.outlineOffset, name).toBeGreaterThanOrEqual(0);
      expect(seen.ringStyle, name).toBe('solid');
      expect(seen.ringWidth, name).toBeGreaterThanOrEqual(2);
      expect(seen.ringColour, name).not.toBe(seen.outlineColour);
      expect(contrast(seen.outlineColour, background), name).toBeGreaterThanOrEqual(3);
      expect(contrast(seen.ringColour, background), name).toBeGreaterThanOrEqual(3);
    }
  });
}

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

  // The grid's own focus, and the cursor on the selected row, by keyboard. Tab goes from the filter bar's last
  // control (Add filter, with no filter on) to the grid when the crawl's analysis stands, as global-setup sees to: an
  // out-of-date one puts its offer to analyse again between them, a Tab stop of its own, in the order they're shown.
  await expect(
    page.getByRole('button', { name: /^Analyse/ }),
    'the crawl’s analysis is out of date: the e2e setup should have made it again or analysed its copy',
  ).toHaveCount(0);
  await page.getByRole('button', { name: 'Add filter' }).focus();
  await page.keyboard.press('Tab');
  await expect(grid).toBeFocused();
  // The grid's focus ring is drawn by its frame, over the rows and the scroll bar (gallery 6): an outline still.
  const ring = await grid.evaluate((element) => {
    const after = getComputedStyle(element.parentElement as Element, '::after');
    return { style: after.outlineStyle, width: Number.parseFloat(after.outlineWidth) };
  });
  expect(ring.style).toBe('solid');
  expect(ring.width).toBeGreaterThan(0);
  const cursor = row.locator('[data-active]');
  await expect(cursor).toHaveCount(1);
  const cell = await style(cursor, 'outline-style', 'outline-color');
  expect(cell['outline-style']).toBe('solid');
  expect(contrast(cell['outline-color'] ?? '', await shownBackground(cursor))).toBeGreaterThanOrEqual(3);

  const current = page.getByRole('navigation', { name: 'Main' }).locator('a[aria-current="page"]');
  const place = await rings(current);
  expect(place.ringStyle).toBe('solid');
  expect(place.ringWidth).toBeGreaterThanOrEqual(2);
  expect(contrast(place.ringColour, await shownBackground(current))).toBeGreaterThanOrEqual(3);
  expect(contrast((await style(current, 'color')).color ?? '', await shownBackground(current))).toBeGreaterThanOrEqual(
    4.5,
  );

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
  await expect(library.getByRole('link', { name: siteRow(), exact: true })).toBeVisible();
  await library.getByRole('button', { name: `Actions for ${siteRow()}` }).click();
  await page.getByRole('menuitem', { name: 'Remove from library' }).click();
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
  await expect(library.getByRole('link', { name: siteRow(), exact: true })).toBeVisible();
});

// 200% zoom of a 1280 × 800 window: a viewport half as wide, in CSS pixels, as WCAG 1.4.4 and 1.4.10 measure it.
const ZOOMED = { width: 640, height: 400 };

/** Whether anything on the page reaches past the window's right edge, and what. */
const pastTheEdge = (page: Page) =>
  page.evaluate(() =>
    [...document.querySelectorAll<HTMLElement>('header *, main *')]
      .filter((element) => {
        const box = element.getBoundingClientRect();
        return box.width > 0 && box.right > window.innerWidth + 0.5 && getComputedStyle(element).position !== 'fixed';
      })
      .map((element) => `${element.tagName.toLowerCase()}.${[...element.classList].join('.')}`),
  );

test('at 200% zoom, the top bar keeps the palette button in the window and its crumbs out from under it', async ({
  page,
}) => {
  await page.goto('/');
  await page.getByRole('region', { name: 'Library' }).getByRole('link', { name: siteRow(), exact: true }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Overview' })).toBeVisible();
  await page.setViewportSize(ZOOMED);
  // The bar measures itself before the next frame is painted (layout/useGiveWay.ts): look at the frame that is.
  await page.evaluate(() => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  await expect(page.getByRole('button', { name: 'Expand the sidebar' })).toHaveAttribute('aria-expanded', 'false');
  await page.waitForFunction(() => document.getAnimations().length === 0);
  const header = page.locator('header').first();
  // Nothing reaches past the window's edge or out of the bar.
  expect(await pastTheEdge(page)).toEqual([]);
  expect(await header.evaluate((element) => element.scrollWidth <= element.clientWidth)).toBe(true);
  // The palette button gives way to its magnifier (never to a letter of its words), all of it in the window, its
  // name kept.
  const palette = header.getByRole('button', { name: /^Search or run a command/ });
  const button = await palette.boundingBox();
  expect(button && button.x + button.width).toBeLessThanOrEqual(ZOOMED.width);
  expect(button?.width).toBeLessThanOrEqual(40);
  // The crumbs keep to their own room, before the palette button, and hold all they show: the date has given way
  // (still read), and the section you're on shows in full.
  const crumbs = header.getByRole('navigation', { name: 'Breadcrumb' });
  const room = await crumbs.boundingBox();
  expect(room && button && room.x + room.width).toBeLessThanOrEqual(button?.x ?? 0);
  expect(await crumbs.evaluate((element) => element.scrollWidth <= element.clientWidth)).toBe(true);
  const date = crumbs.getByRole('listitem').filter({ hasText: / \d{4}, / });
  expect(await date.evaluate((element) => element.getBoundingClientRect().width <= 1)).toBe(true);
  const section = crumbs.locator('[aria-current="page"]');
  await expect(section).toHaveText('Overview');
  expect(await section.evaluate((element) => element.scrollWidth <= element.clientWidth)).toBe(true);
  expect(await pastTheEdge(page)).toEqual([]);
});

// The design system's states that only a browser shows, on markup with its classes (see `fixture`).
test.describe('the design system’s states', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  });

  test('a long section title wraps in a narrow column instead of running out of it', async ({ page }) => {
    const host = await fixture(
      page,
      `<div style="width:320px" id="column"><div class="si-section-header">
         <h2 class="si-section-header__title">Pages whose canonical points at a redirected URL<span class="si-section-header__meta">82,904</span></h2>
         <div class="si-section-header__actions"><button type="button" class="si-btn si-btn--ghost">Export</button></div>
       </div></div>`,
    );
    const column = await host.locator('#column').boundingBox();
    const title = host.locator('h2');
    const box = await title.boundingBox();
    const action = await host.getByRole('button', { name: 'Export' }).boundingBox();
    expect(column && box && action).toBeTruthy();
    if (!column || !box || !action) return;
    expect(box.x + box.width).toBeLessThanOrEqual(column.x + column.width + 0.5);
    expect(action.x + action.width).toBeLessThanOrEqual(column.x + column.width + 0.5);
    // All of it shows: wrapped, not cut.
    expect(await title.evaluate((element) => element.scrollWidth <= element.clientWidth)).toBe(true);
  });

  test('tabs past the edge are clipped, never scrolled either way, and a tab left to the menu is unseen and last', async ({
    page,
  }) => {
    const tabs = ['Overview', 'Response', 'Headers', 'Links', 'Images', 'Structured data', 'Hreflang', 'Source'];
    // Which tabs go to the "N more" menu is Tabs.tsx's job (tabsFit); here the third is marked, as Tabs.tsx marks one.
    const markup = (id: string) =>
      `<div class="si-tabs" role="tablist" id="${id}">${tabs
        .map(
          (name, index) =>
            `<button type="button" class="si-tab" role="tab" aria-selected="${String(index === 0)}"${index === 2 ? ' data-overflow=""' : ''}>${name}</button>`,
        )
        .join('')}</div>`;
    const host = await fixture(
      page,
      `<div style="width:300px">${markup('narrow')}</div><div style="width:1000px">${markup('wide')}</div>`,
    );
    const measure = (locator: Locator) =>
      locator.evaluate((element) => {
        // A strip that scrolled cut a tab in half at its end (polish 2): it clips, so nothing can scroll it sideways.
        element.scrollLeft = 200;
        return {
          overflowing: element.scrollWidth > element.clientWidth,
          overflowX: getComputedStyle(element).overflowX,
          scrolledSideways: element.scrollLeft,
          scrollsUpAndDown: element.scrollHeight > element.clientHeight,
        };
      });
    const narrow = await measure(host.locator('#narrow'));
    expect(narrow).toEqual({ overflowing: true, overflowX: 'clip', scrolledSideways: 0, scrollsUpAndDown: false });
    expect((await measure(host.locator('#wide'))).scrollsUpAndDown).toBe(false);
    // The tab left to the menu stays laid out, so the strip can measure it, but unseen and after the others.
    const left = await host.locator('#wide').evaluate((list) => {
      const marked = list.querySelector('[data-overflow]');
      const last = list.lastElementChild;
      if (!marked || !last) return null;
      return {
        visibility: getComputedStyle(marked).visibility,
        width: marked.getBoundingClientRect().width > 0,
        afterTheLast: marked.getBoundingClientRect().left > last.getBoundingClientRect().left,
      };
    });
    expect(left).toEqual({ visibility: 'hidden', width: true, afterTheLast: true });
    // The chosen tab's rule is drawn in full, at the list's very bottom, over its hairline.
    const rule = await host.locator('#wide [aria-selected="true"]').evaluate((element) => ({
      bottom: getComputedStyle(element, '::after').bottom,
      tab: element.getBoundingClientRect().bottom,
      list: element.parentElement?.getBoundingClientRect().bottom,
    }));
    expect(rule.bottom).toBe('0px');
    expect(rule.tab).toBe(rule.list);
  });

  test('an indeterminate bar slides by translate, and under reduced motion stripes the whole track', async ({
    page,
  }) => {
    const markup = `<div style="width:300px"><div class="si-bar-track" role="progressbar" aria-label="Working"><span class="si-bar-fill si-bar-fill--indeterminate" id="fill"></span></div></div>`;
    let host = await fixture(page, markup);
    const motion = await host.locator('#fill').evaluate((element) => {
      const [animation] = element.getAnimations();
      const effect = animation?.effect;
      if (!animation || !(effect instanceof KeyframeEffect)) return null;
      const timing = ['offset', 'easing', 'composite', 'computedOffset'];
      const properties = effect
        .getKeyframes()
        .flatMap((frame) => Object.keys(frame).filter((key) => !timing.includes(key)));
      const track = element.parentElement?.getBoundingClientRect();
      animation.pause();
      animation.currentTime = 0;
      const start = element.getBoundingClientRect();
      // Just before the end: at the end itself, the next pass has begun.
      animation.currentTime = Number(effect.getComputedTiming().duration) - 1;
      const end = element.getBoundingClientRect();
      return { properties: [...new Set(properties)], startRight: start.right, endLeft: end.left, track };
    });
    expect(motion).not.toBeNull();
    // No layout every frame: the block moves with translate, from off the track's left to off its right.
    expect(motion?.properties).toEqual(['translate']);
    expect(motion?.startRight).toBeLessThanOrEqual((motion?.track?.left ?? 0) + 0.5);
    expect(motion?.endLeft).toBeGreaterThanOrEqual((motion?.track?.right ?? 0) - 0.5);

    for (const forcedColors of ['none', 'active'] as const) {
      await page.emulateMedia({ reducedMotion: 'reduce', forcedColors });
      host = await fixture(page, markup);
      const still = await host.locator('#fill').evaluate((element) => ({
        animations: element.getAnimations().length,
        width: element.getBoundingClientRect().width,
        track: element.parentElement?.getBoundingClientRect().width ?? 0,
        image: getComputedStyle(element).backgroundImage,
      }));
      // Still, and across the whole track as stripes: never a solid third that reads as "33% done".
      expect(still.animations, forcedColors).toBe(0);
      expect(still.width, forcedColors).toBeCloseTo(still.track, 0);
      expect(still.image, forcedColors).toContain('repeating-linear-gradient');
    }
  });

  test('hover never takes away a state’s edge: a pressed toggle, an invalid field, a checked box', async ({ page }) => {
    const host = await fixture(
      page,
      `<button type="button" class="si-btn si-btn--secondary" aria-pressed="true" id="pressed">Show passed</button>
       <button type="button" class="si-btn si-btn--secondary" aria-pressed="false" id="unpressed">Show skipped</button>
       <div class="si-field" data-invalid="" id="invalid" style="width:240px"><input class="si-field__input" aria-label="Start URL" aria-invalid="true" value="x"></div>
       <button type="button" class="si-checkbox" role="checkbox" aria-checked="true" data-state="checked" aria-label="Render" id="checked"></button>`,
    );
    // Once the hover's transition is done.
    const edge = async (id: string) => {
      await page.waitForFunction(() => document.getAnimations().length === 0);
      return (await style(host.locator(`#${id}`), 'border-top-color'))['border-top-color'];
    };
    for (const id of ['pressed', 'invalid', 'checked']) {
      const rest = await edge(id);
      await host.locator(`#${id}`).hover();
      expect(await edge(id), id).toBe(rest);
      await page.mouse.move(0, 0);
    }
    // Hover itself still shows where there's no state to keep.
    const rest = await edge('unpressed');
    await host.locator('#unpressed').hover();
    expect(await edge('unpressed')).not.toBe(rest);
  });

  test('tabbing to the button inside a field shows one focus ring, the button’s', async ({ page }) => {
    const field = page.getByRole('textbox', { name: 'Start URL' });
    const box = field.locator('xpath=..');
    await field.focus();
    expect((await style(box, 'outline-style'))['outline-style']).toBe('solid');
    await page.keyboard.press('Tab');
    const crawl = box.locator('button[type="submit"]');
    await expect(crawl).toBeFocused();
    expect((await style(crawl, 'outline-style'))['outline-style']).toBe('solid');
    expect((await style(box, 'outline-style'))['outline-style']).toBe('none');
  });

  for (const colorScheme of ['light', 'dark'] as const) {
    test(`a link in running text has an underline at 3:1 (${colorScheme})`, async ({ page }) => {
      await page.emulateMedia({ colorScheme });
      const host = await fixture(
        page,
        `<p class="text-text" style="background:var(--color-surface)">Read <a href="#guide" class="si-link si-link--inline">the guide</a>.</p>`,
      );
      const link = host.getByRole('link', { name: 'the guide' });
      const underline = (await style(link, 'text-decoration-color'))['text-decoration-color'] ?? '';
      expect(contrast(underline, await shownBackground(link))).toBeGreaterThanOrEqual(3);
    });
  }

  for (const colorScheme of ['light', 'dark'] as const) {
    test(`the app’s messages take their own tones, not severity’s, readable at 4.5:1 (${colorScheme})`, async ({
      page,
    }) => {
      await page.emulateMedia({ colorScheme });
      const host = await fixture(
        page,
        `<div class="si-banner si-banner--warning" id="caution"><span class="si-banner__body">Restarting the engine (2 of 3)…</span></div>
         <div class="si-callout si-callout--warning" id="caution-callout"><span class="si-callout__body">3 checks were skipped</span></div>
         <div class="si-banner si-banner--error" id="failure"><span class="si-banner__body">The engine keeps stopping.</span></div>
         <span class="si-sev-badge si-sev-badge--warning" id="severity">Warning</span>`,
      );
      const background = async (id: string) =>
        (await style(host.locator(`#${id}`), 'background-color'))['background-color'] ?? '';
      const severity = await background('severity');
      for (const id of ['caution', 'caution-callout']) {
        expect(await background(id), id).not.toBe(severity);
      }
      for (const id of ['caution', 'caution-callout', 'failure']) {
        const text = (await style(host.locator(`#${id}`), 'color')).color ?? '';
        expect(contrast(text, await background(id)), id).toBeGreaterThanOrEqual(4.5);
      }
    });
  }

  test('under forced colours, what’s disabled is GrayText: menu and palette items, checkboxes, switches', async ({
    page,
  }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    const host = await fixture(
      page,
      `<span id="gray" style="color:GrayText">GrayText</span>
       <div class="si-menu" role="menu" aria-label="Row" style="width:200px">
         <div class="si-menu-item" role="menuitem" id="item">Copy the URL</div>
         <div class="si-menu-item" role="menuitem" data-disabled="" aria-disabled="true" id="item-off">Export this view</div>
       </div>
       <div role="listbox" aria-label="Commands"><div class="si-palette-item" role="option" aria-selected="false" aria-disabled="true" id="option-off">Pause the crawl</div></div>
       <div class="si-choice" data-disabled=""><button type="button" class="si-checkbox" role="checkbox" aria-checked="false" data-state="unchecked" disabled aria-label="a" id="box-off"></button></div>
       <div class="si-choice" data-disabled=""><button type="button" class="si-checkbox" role="checkbox" aria-checked="true" data-state="checked" disabled aria-label="b" id="box-on-off"></button></div>
       <div class="si-choice" data-disabled=""><button type="button" class="si-switch" role="switch" aria-checked="false" data-state="unchecked" disabled aria-label="c" id="switch-off"><span class="si-switch__thumb" data-state="unchecked"></span></button></div>
       <div class="si-choice" data-disabled=""><button type="button" class="si-switch" role="switch" aria-checked="true" data-state="checked" disabled aria-label="d" id="switch-on-off"><span class="si-switch__thumb" data-state="checked"></span></button></div>`,
    );
    const get = async (selector: string, property: string) => (await style(host.locator(selector), property))[property];
    const gray = await get('#gray', 'color');
    expect(await get('#item', 'color')).not.toBe(gray);
    expect(await get('#item-off', 'color')).toBe(gray);
    expect(await get('#option-off', 'color')).toBe(gray);
    expect(await get('#box-off', 'border-top-color')).toBe(gray);
    expect(await get('#box-on-off', 'background-color')).toBe(gray);
    expect(await get('#switch-off', 'border-top-color')).toBe(gray);
    expect(await get('#switch-off .si-switch__thumb', 'background-color')).toBe(gray);
    expect(await get('#switch-on-off', 'background-color')).toBe(gray);
  });
});

test('a crawl’s error in the library wraps in its status column instead of widening the page', async ({ page }) => {
  await page.goto('/');
  const library = page.getByRole('region', { name: 'Library' });
  const row = library.getByRole('row').filter({ has: page.getByRole('link', { name: siteRow(), exact: true }) });
  await expect(row).toBeVisible();
  // The line a file that couldn't be read gets under its status (HomeScreen's LibraryRow), at its longest.
  const line = await row
    .getByRole('cell')
    .nth(1)
    .evaluate((cell) => {
      const error = document.createElement('span');
      error.className = 'block text-12 text-text-muted';
      error.textContent = 'Another program has the file open, so it couldn’t be read: close it, then try again.';
      (cell.firstElementChild ?? cell).append(error);
      return { height: error.getBoundingClientRect().height, line: parseFloat(getComputedStyle(error).lineHeight) };
    });
  expect(line.height).toBeGreaterThan(line.line);
  const main = page.locator('main');
  expect(await main.evaluate((element) => element.scrollWidth <= element.clientWidth)).toBe(true);
});
