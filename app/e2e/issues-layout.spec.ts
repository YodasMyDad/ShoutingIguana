// When a check is open, the check is the work (fix-it pass, links lane, #3 and #4): under a 1600 px window, or with the
// inspector open, the checks list goes behind "All checks" and the check takes the width, so its grid isn't cut and
// opening the inspector never stacks the check under the list, off-screen. Against the real sidecar's synthetic crawl.
import { expect, test, type Page } from '@playwright/test';
import { openSection } from './support';

const inspectorOf = (page: Page) => page.getByRole('complementary', { name: 'Inspector' });
const checksOf = (page: Page) => page.getByRole('listbox', { name: 'Checks' });
const allChecks = (page: Page) => page.getByRole('button', { name: 'All checks' });

/** Issues, on one check, from the sidebar. */
async function openCheck(page: Page, check: string) {
  await openSection(page, /^Issues/);
  await expect(page).toHaveURL(/\/crawls\/[0-9a-f]{16}\/issues\/[a-z0-9-]+/);
  const crawl = /\/crawls\/([0-9a-f]{16})\//.exec(page.url())?.[1] ?? '';
  await page.goto(`/crawls/${crawl}/issues/${check}`);
  await expect(page.getByRole('heading', { name: 'Title too long', level: 2 })).toBeVisible();
}

/** Inspects the first of the check's URLs. Its grid's URL is not a link: the cell selects the row, Enter opens it. */
async function inspectFirst(page: Page) {
  const grid = page.getByRole('grid', { name: /^Title too long: / });
  await grid.getByRole('row').nth(1).getByRole('gridcell').first().click();
  await page.keyboard.press('Enter');
  await expect(inspectorOf(page)).toBeVisible();
}

/** The check's panel: in view, and at least this wide. */
async function expectCheckInView(page: Page, width: number) {
  const title = page.getByRole('heading', { name: 'Title too long', level: 2 });
  await expect(title).toBeInViewport();
  const panel = await page.locator('[data-check-detail]').boundingBox();
  expect(panel?.width ?? 0).toBeGreaterThanOrEqual(width);
}

test('at 1440 px the open check takes the width, the list is behind All checks, and the inspector keeps the check in view', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openCheck(page, 'title-too-long');
  await expect(checksOf(page)).toBeHidden();
  await expect(allChecks(page)).toHaveAttribute('aria-expanded', 'false');
  await expectCheckInView(page, 1000);

  // The list comes out beside the check; picking a check puts it back, and the check takes the width again.
  await allChecks(page).click();
  await expect(checksOf(page)).toBeVisible();
  await checksOf(page).locator('[role="option"][data-check="title-too-long"]').click();
  await expect(checksOf(page)).toBeHidden();
  await expect(page.getByRole('heading', { name: 'Title too long', level: 2 })).toBeFocused();

  await inspectFirst(page);
  await expectCheckInView(page, 640);
  await expect(checksOf(page)).toBeHidden();
});

// 3b final: with the list out from behind "All checks", inspecting a row stacked the check under the list, off-screen.
// The inspector folds the list away: the check is what was being worked on.
test('at 1440 px, inspecting a row with All checks open folds the list away and keeps the check beside the inspector', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openCheck(page, 'title-too-long');
  await allChecks(page).click();
  await expect(checksOf(page)).toBeVisible();

  await inspectFirst(page);
  await expect(checksOf(page)).toBeHidden();
  await expect(allChecks(page)).toHaveAttribute('aria-expanded', 'false');
  await expectCheckInView(page, 640);
});

test('at 1920 px the list stays beside the check, until the inspector needs the room', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 });
  await openCheck(page, 'title-too-long');
  await expect(checksOf(page)).toBeVisible();
  await expect(allChecks(page)).toBeHidden();
  await expectCheckInView(page, 1100);

  await inspectFirst(page);
  await expect(checksOf(page)).toBeHidden();
  await expect(allChecks(page)).toBeVisible();
  // Beside the inspector the check is still the wide pane (measured 998: the 2 px is the frame).
  await expectCheckInView(page, 990);
});

// The 3b polish: in a check's group, each page's type and "Found on" flowed after its URL, so they started at a
// different x on every row. They're columns now, and no URL is cut while the row has room for it.
test('a group’s pages line their type and where they’re found up in columns, each URL whole', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openSection(page, /^Issues/);
  await expect(page).toHaveURL(/\/crawls\/[0-9a-f]{16}\/issues\/[a-z0-9-]+/);
  const crawl = /\/crawls\/([0-9a-f]{16})\//.exec(page.url())?.[1] ?? '';
  await page.goto(`/crawls/${crawl}/issues/internal-client-error`);
  const grid = page.getByRole('treegrid', { name: /^Internal URL returns 4xx: / });
  const group = grid.locator('[role="row"][aria-level="1"]').filter({ hasText: '404 Not Found' });
  if ((await group.getAttribute('aria-expanded')) !== 'true') await group.locator('[data-tree-toggle]').click();
  const pages = grid.locator('[role="row"][aria-level="2"]');
  await expect(pages.nth(3)).toBeVisible();
  await expect(grid.locator('[data-where-found="pending"]')).toHaveCount(0);
  const lines = await pages.evaluateAll((rows) =>
    rows
      .filter((row) => row.querySelector('.si-url__full'))
      .map((row) => {
        const left = (selector: string) => {
          const element = row.querySelector<HTMLElement>(selector);
          return element && element.textContent !== '' ? Math.round(element.getBoundingClientRect().left) : null;
        };
        const shown = row.querySelector<HTMLElement>('.si-url__shown');
        return {
          url: row.querySelector('.si-url__full')?.textContent ?? '',
          type: left('.si-row-evidence'),
          where: left('.si-where-line'),
          cut: !shown || shown.scrollWidth > shown.clientWidth + 0.5 || shown.textContent.includes('…'),
        };
      }),
  );
  expect(lines.length).toBeGreaterThanOrEqual(4);
  expect(new Set(lines.map((line) => line.type)).size, JSON.stringify(lines)).toBe(1);
  expect(new Set(lines.map((line) => line.where)).size, JSON.stringify(lines)).toBe(1);
  expect(lines[0]?.type).not.toBeNull();
  expect(lines.filter((line) => line.cut).map((line) => line.url)).toEqual([]);
});
