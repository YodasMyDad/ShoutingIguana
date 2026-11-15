// The framed workspace (docs/v2-ui-design.md): the window is the frame, the sidebar sits on it, and the top bar and the
// screen are one panel inset from the window's edge, the screen scrolling inside it. The sidebar's crawl is a switcher
// to the library's recent crawls. What's current is a neutral fill, never the accent. Capped screens grow with a wide
// window to --content-max, and only centre past it. Against the real sidecar.
import { expect, test, type Locator, type Page } from '@playwright/test';
import { crawlTestSite, expectNoViolations, removeCrawl, seededCrawl, site, type SeededCrawl } from './support';

const style = (locator: Locator, ...properties: string[]) =>
  locator.evaluate(
    (element, names) =>
      Object.fromEntries(names.map((name) => [name, getComputedStyle(element).getPropertyValue(name)])),
    properties,
  );

async function openIn(page: Page, mode: 'simple' | 'advanced', route: string) {
  await page.addInitScript((stored) => localStorage.setItem('si.settings', stored), JSON.stringify({ mode }));
  await page.goto(route);
  await expect(page.getByRole('banner').getByRole('radiogroup', { name: 'Mode' })).toBeVisible();
}

const panelOf = (page: Page) => page.locator('[data-panel]');

const escape = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/** The panel's box, and its inset from the window's top, right and bottom edges. */
const insets = (page: Page) =>
  panelOf(page).evaluate((panel) => {
    const box = panel.getBoundingClientRect();
    return {
      top: box.top,
      right: window.innerWidth - box.right,
      bottom: window.innerHeight - box.bottom,
      radius: parseFloat(getComputedStyle(panel).borderTopRightRadius),
      border: getComputedStyle(panel).borderTopStyle,
    };
  });

let seeded: SeededCrawl;

test.beforeAll(async () => {
  seeded = await seededCrawl();
});

test.describe('the panel', () => {
  for (const colorScheme of ['light', 'dark'] as const) {
    test(`holds the top bar and the screen, inset from the window, on the frame (${colorScheme})`, async ({ page }) => {
      await page.emulateMedia({ colorScheme });
      await page.setViewportSize({ width: 1280, height: 720 });
      await openIn(page, 'advanced', `/crawls/${seeded.id}/overview`);
      const panel = panelOf(page);
      await expect(panel.getByRole('banner')).toBeVisible();
      await expect(panel.locator('main#main')).toBeVisible();
      // The sidebar is beside it, on the frame, not in it.
      await expect(panel.getByRole('navigation', { name: 'Main' })).toHaveCount(0);

      const seen = await insets(page);
      expect(seen.top).toBe(8);
      expect(seen.right).toBe(8);
      expect(seen.bottom).toBe(8);
      expect(seen.radius).toBe(10);
      expect(seen.border).toBe('solid');
      const frame = (await style(page.locator('body'), 'background-color'))['background-color'];
      const sidebar = (await style(page.getByRole('navigation', { name: 'Main' }), 'background-color'))[
        'background-color'
      ];
      const inside = (await style(panel, 'background-color'))['background-color'];
      expect(sidebar).toBe(frame);
      expect(inside).not.toBe(frame);
      await expectNoViolations(page);
    });
  }

  test('the screen scrolls inside it; the window and the top bar stay put', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 720 });
    await openIn(page, 'advanced', `/crawls/${seeded.id}/overview`);
    await expect(page.getByRole('heading', { level: 1, name: 'Overview' })).toBeVisible();
    const main = page.locator('main#main');
    const bar = page.getByRole('banner');
    const before = await bar.boundingBox();
    expect(await main.evaluate((element) => element.scrollHeight > element.clientHeight)).toBe(true);
    await main.evaluate((element) => element.scrollTo({ top: 400 }));
    expect(await main.evaluate((element) => element.scrollTop)).toBeGreaterThan(0);
    expect(await page.evaluate(() => document.scrollingElement?.scrollTop ?? 0)).toBe(0);
    expect(await bar.boundingBox()).toEqual(before);
    // What scrolls is clipped by the panel's corners, never drawn past its edge.
    expect((await style(panelOf(page), 'overflow')).overflow).toBe('hidden');
  });

  test('at 200% zoom (640 px) the inset goes, and the panel keeps only its edge against the sidebar', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 640, height: 360 });
    await openIn(page, 'advanced', `/crawls/${seeded.id}/overview`);
    const seen = await insets(page);
    expect(seen).toMatchObject({ top: 0, right: 0, bottom: 0, radius: 0 });
    const edges = await style(panelOf(page), 'border-left-width', 'border-top-width', 'border-right-width');
    expect(edges).toEqual({ 'border-left-width': '1px', 'border-top-width': '0px', 'border-right-width': '0px' });
  });

  test('under forced colours, its edge, the cards and the current place still show', async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await page.setViewportSize({ width: 1280, height: 720 });
    await openIn(page, 'advanced', `/crawls/${seeded.id}/overview`);
    await expect(page.getByRole('heading', { level: 1, name: 'Overview' })).toBeVisible();
    expect((await style(panelOf(page), 'border-top-style'))['border-top-style']).toBe('solid');
    const card = page.locator('main .si-card').first();
    await expect(card).toBeVisible();
    expect((await style(card, 'border-top-style'))['border-top-style']).toBe('solid');
    const figure = page.getByRole('region', { name: 'Totals' }).locator('.si-stat').first();
    expect((await style(figure, 'border-top-style'))['border-top-style']).toBe('solid');
    const current = page.getByRole('navigation', { name: 'Main' }).locator('a[aria-current="page"]');
    await expect(current).toHaveText(/Overview/);
    const ring = await current.evaluate((element) => {
      const after = getComputedStyle(element, '::after');
      return { style: after.borderTopStyle, width: parseFloat(after.borderTopWidth) };
    });
    expect(ring.style).toBe('solid');
    expect(ring.width).toBeGreaterThanOrEqual(2);
    await expectNoViolations(page);
  });
});

test.describe('the current place', () => {
  // The fills and text the owner chose (docs/v2-ui-design.md): no accent rule, no accent text, at 600.
  const LOOK = {
    light: { fill: 'rgb(220, 220, 226)', text: 'rgb(17, 17, 19)' },
    dark: { fill: 'rgb(35, 35, 40)', text: 'rgb(255, 255, 255)' },
  } as const;
  for (const colorScheme of ['light', 'dark'] as const) {
    for (const mode of ['simple', 'advanced'] as const) {
      test(`is the sidebar's neutral fill, in ${mode} (${colorScheme})`, async ({ page }) => {
        await page.emulateMedia({ colorScheme });
        await openIn(page, mode, `/crawls/${seeded.id}/overview`);
        const current = page.getByRole('navigation', { name: 'Main' }).locator('a[aria-current="page"]');
        await expect(current).toHaveText(mode === 'simple' ? /Summary/ : /Overview/);
        const seen = await style(current, 'background-color', 'color', 'box-shadow', 'font-weight');
        expect(seen).toEqual({
          'background-color': LOOK[colorScheme].fill,
          color: LOOK[colorScheme].text,
          'box-shadow': 'none',
          'font-weight': '600',
        });
        const icon = current.locator('.si-nav-item__icon');
        expect((await style(icon, 'color')).color).toBe(LOOK[colorScheme].text);
      });
    }
  }

  test("Settings' section list has the same look, on the panel's fill", async ({ page }) => {
    await openIn(page, 'advanced', '/settings/analysis');
    const current = page.getByRole('navigation', { name: 'Settings' }).locator('a[aria-current="page"]');
    await expect(current).toHaveText('Analysis');
    const seen = await style(current, 'box-shadow', 'font-weight', 'color');
    expect(seen['box-shadow']).toBe('none');
    expect(seen['font-weight']).toBe('600');
    expect(seen.color).toBe('rgb(17, 17, 19)');
  });
});

test.describe('the crawl switcher', () => {
  let other: SeededCrawl | null = null;
  test.beforeAll(async () => {
    test.setTimeout(240_000);
    other = await crawlTestSite();
  });
  test.afterAll(async () => {
    if (other) await removeCrawl(other.id);
  });
  const otherId = () => other?.id ?? '';

  const switcher = (page: Page) =>
    page.getByRole('navigation', { name: 'Main' }).getByRole('button', { name: /^Switch (crawl|site):/ });

  test('opens from the keyboard, lists the recent crawls with the open one ticked, and switches, keeping the section', async ({
    page,
  }) => {
    await openIn(page, 'advanced', `/crawls/${seeded.id}/issues`);
    const button = switcher(page);
    await expect(button).toHaveAccessibleName(new RegExp(`^Switch crawl:\\s*${escape(site())}\\s*,`));
    await expect(button).toHaveAttribute('aria-haspopup', 'menu');
    await button.focus();
    await page.keyboard.press('Enter');
    const menu = page.getByRole('menu');
    await expect(menu).toBeVisible();
    const crawls = menu.getByRole('menuitemradio');
    await expect(crawls).toHaveCount(2);
    // Newest first: the test site's crawl, just made, then the seeded one, which is open and ticked.
    await expect(crawls.nth(1)).toHaveAttribute('aria-checked', 'true');
    await expect(crawls.first()).toHaveAttribute('aria-checked', 'false');
    await expect(menu.getByRole('menuitem', { name: 'All crawls, on Home' })).toBeVisible();
    await expectNoViolations(page);

    // The arrows move through the crawls (the newest first: the test site's), Enter opens it on the same section.
    await expect(crawls.first()).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(new RegExp(`/crawls/${otherId()}/issues`));
    // The crawl switched to is the one ticked now. Esc closes the menu and gives focus back to the button.
    await switcher(page).click();
    await expect(menu.getByRole('menuitemradio').first()).toHaveAttribute('aria-checked', 'true');
    await expect(menu).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(menu).toBeHidden();
    await expect(switcher(page)).toBeFocused();

    // Its last item goes to Home, for the whole library.
    await switcher(page).click();
    await page.getByRole('menuitem', { name: 'All crawls, on Home' }).click();
    await expect(page).toHaveURL(/\/$/);
  });

  test('works with the sidebar collapsed, from its icon, and in Simple', async ({ page }) => {
    await openIn(page, 'simple', `/crawls/${seeded.id}/overview`);
    await page.getByRole('button', { name: 'Collapse the sidebar' }).click();
    const button = switcher(page);
    await expect(button).toHaveAccessibleName(/^Switch site:/);
    await button.click();
    const menu = page.getByRole('menu');
    await expect(menu.getByRole('menuitemradio')).toHaveCount(2);
    await expect(menu.getByRole('menuitem', { name: 'All your sites, on Home' })).toBeVisible();
    await menu.getByRole('menuitemradio', { checked: false }).click();
    await expect(page).toHaveURL(new RegExp(`/crawls/${otherId()}/overview`));
    await page.getByRole('button', { name: 'Expand the sidebar' }).click();
  });
});

test.describe('capped screens grow with a wide window', () => {
  /** The Overview's column, and the main area it sits in, by their content boxes. */
  const widths = (page: Page) =>
    page.locator('main#main').evaluate((main) => {
      const column = main.querySelector('h1')?.closest<HTMLElement>('.mx-auto') ?? main;
      const box = column.getBoundingClientRect();
      const styles = getComputedStyle(column);
      const content = box.width - parseFloat(styles.paddingLeft) - parseFloat(styles.paddingRight);
      return { main: main.clientWidth, column: box.width, content, left: box.left - main.getBoundingClientRect().left };
    });

  test('at 1920 x 1080 the Overview fills the main area', async ({ page }) => {
    await page.setViewportSize({ width: 1920, height: 1080 });
    await openIn(page, 'advanced', `/crawls/${seeded.id}/overview`);
    await expect(page.getByRole('heading', { level: 1, name: 'Overview' })).toBeVisible();
    const seen = await widths(page);
    expect(seen.column).toBeCloseTo(seen.main, 0);
    expect(seen.content).toBeGreaterThan(1136);
  });

  test('at 2560 x 1362 it stops at 1,800 px and centres', async ({ page }) => {
    await page.setViewportSize({ width: 2560, height: 1362 });
    await openIn(page, 'advanced', `/crawls/${seeded.id}/overview`);
    await expect(page.getByRole('heading', { level: 1, name: 'Overview' })).toBeVisible();
    const seen = await widths(page);
    expect(seen.content).toBeCloseTo(1800, 0);
    expect(seen.main).toBeGreaterThan(seen.column);
    // Centred: the room left over is shared either side.
    expect(seen.left).toBeCloseTo((seen.main - seen.column) / 2, 0);
    // The figures spread across it: five in a row.
    const figures = page.getByRole('region', { name: 'Totals' }).locator('.si-stat');
    const tops = await figures.evaluateAll((elements) =>
      elements.map((element) => element.getBoundingClientRect().top),
    );
    expect(new Set(tops).size).toBe(1);
  });
});
