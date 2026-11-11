// A screen that is a grid under its toolbars (Links, URLs), at 200% zoom of a 1280x720 window: 640x360 CSS pixels.
// The toolbars wrap onto more rows there, and once took all the height: the Links grid was left 22 px under three
// chips, no row of it in view, and "No links match" with its Clear couldn't be seen. The grid keeps room for five
// rows now, and for all its empty state says; the screen scrolls to it. Against the real sidecar's synthetic crawl.
// Nothing here waits for a time: every size is polled for.
import { expect, test, type Locator, type Page } from '@playwright/test';

const env = (name: string) => {
  const value = process.env[name];
  if (!value) throw new Error(`global-setup sets ${name}`);
  return value;
};

interface Filter {
  column: string;
  op: string;
  value?: string | number | boolean;
  values?: (string | number)[];
}

async function seededCrawl(): Promise<string> {
  const response = await fetch(`${env('SI_E2E_SIDECAR')}/api/crawls`, {
    headers: { Authorization: `Bearer ${env('SI_E2E_TOKEN')}` },
  });
  const crawls = (await response.json()) as { id: string; site: string | null; fileName: string }[];
  const seeded = crawls.find((crawl) => (crawl.site ?? crawl.fileName) === env('SI_E2E_SITE'));
  if (!seeded) throw new Error(`The sidecar lists no crawl of ${env('SI_E2E_SITE')}`);
  return seeded.id;
}

const withFilters = (filters: Filter[]) => `?filter=${encodeURIComponent(JSON.stringify(filters))}`;
const main = (page: Page) => page.getByRole('main');

/** The grid's box and the rows of it that are wholly inside the window, header left out. */
async function gridInView(grid: Locator) {
  return grid.evaluate((element) => {
    const box = element.getBoundingClientRect();
    const top = Math.max(box.top, 0);
    const bottom = Math.min(box.bottom, window.innerHeight);
    const rows = [...element.querySelectorAll('[role="row"]')].filter((row) => {
      const at = row.getBoundingClientRect();
      return (
        row.querySelector('[role="columnheader"]') === null &&
        at.height > 0 &&
        at.top >= top - 0.5 &&
        at.bottom <= bottom + 0.5
      );
    });
    const header = element.querySelector('[role="columnheader"]')?.getBoundingClientRect().height ?? 0;
    const row = rows[0]?.getBoundingClientRect().height ?? 0;
    return { height: box.height, rowsInView: rows.length, header, row };
  });
}

/** Scrolls the screen to its foot, where the grid is, as a user scrolls to it. */
const scrollToFoot = (page: Page) =>
  main(page).evaluate((element) => {
    element.scrollTop = element.scrollHeight;
  });

test.beforeEach(async ({ page }) => {
  await page.setViewportSize({ width: 640, height: 360 });
});

for (const screen of [
  {
    name: 'Links',
    path: 'links',
    chips: [
      { column: 'to.statusClass', op: 'in', values: ['2xx'] },
      { column: 'kind', op: 'in', values: ['hyperlink'] },
      { column: 'to.isInternal', op: 'is', value: true },
    ] satisfies Filter[],
    none: [
      { column: 'to.statusClass', op: 'in', values: ['5xx'] },
      { column: 'kind', op: 'in', values: ['image'] },
      { column: 'text', op: 'contains', value: 'a rather long piece of anchor text that no link has' },
    ] satisfies Filter[],
    noMatch: 'No links match these filters.',
  },
  {
    name: 'URLs',
    path: 'urls',
    chips: [
      { column: 'statusClass', op: 'in', values: ['2xx'] },
      { column: 'isInternal', op: 'is', value: true },
      { column: 'type', op: 'in', values: ['page'] },
    ] satisfies Filter[],
    none: [
      { column: 'statusClass', op: 'in', values: ['5xx'] },
      { column: 'isInternal', op: 'is', value: false },
      { column: 'type', op: 'in', values: ['font'] },
    ] satisfies Filter[],
    noMatch: 'No URLs match these filters.',
  },
]) {
  test(`${screen.name} keeps five rows of its grid at 200% zoom with three chips on, and scrolls to them`, async ({
    page,
  }) => {
    const crawl = await seededCrawl();
    await page.goto(`/crawls/${crawl}/${screen.path}${withFilters(screen.chips)}`);
    const grid = page.getByRole('grid', { name: screen.name });
    await expect(grid.getByRole('row').nth(5)).toBeAttached();
    await expect(main(page).getByRole('group', { name: 'Filters' }).locator('.si-chip__main')).toHaveCount(3);
    // The grid has room for five rows under its header, whatever the toolbars took.
    await expect
      .poll(async () => {
        const view = await gridInView(grid);
        return view.height - (view.header + 5 * view.row);
      })
      .toBeGreaterThanOrEqual(0);
    // The window is too short for the toolbars and that: the screen scrolls, and at its foot five rows are whole.
    await scrollToFoot(page);
    await expect.poll(async () => (await gridInView(grid)).rowsInView).toBeGreaterThanOrEqual(5);
    // Nothing is squeezed to make the room: no toolbar's content runs out of its own box.
    const squeezed = await main(page).evaluate((element) =>
      [...element.querySelectorAll<HTMLElement>('.si-banner, [role="group"]')]
        .filter((part) => part.scrollHeight > part.clientHeight + 1)
        .map((part) => part.textContent.slice(0, 60)),
    );
    expect(squeezed).toEqual([]);
  });

  test(`${screen.name} shows that nothing matches, with its Clear, whole at 200% zoom with three chips on`, async ({
    page,
  }) => {
    const crawl = await seededCrawl();
    await page.goto(`/crawls/${crawl}/${screen.path}${withFilters(screen.none)}`);
    const grid = page.getByRole('grid', { name: screen.name });
    const said = main(page).getByText(screen.noMatch);
    await expect(said).toBeAttached();
    await scrollToFoot(page);
    // All of it in the window at once: what it says, the filters that did it, and the one press that clears them.
    const empty = main(page).locator('.si-empty').filter({ hasText: screen.noMatch });
    await expect(empty).toBeInViewport({ ratio: 1 });
    await expect(empty.getByRole('button', { name: /^Clear/ })).toBeInViewport({ ratio: 1 });
    // And none of it scrolls inside the grid: the grid made room for it.
    expect(
      await grid.evaluate((element) => {
        const region = element.parentElement?.querySelector<HTMLElement>(':scope > .absolute.overflow-auto');
        return region ? region.scrollHeight - region.clientHeight : 0;
      }),
    ).toBeLessThanOrEqual(1);
    await empty.getByRole('button', { name: /^Clear/ }).click();
    await expect(grid.getByRole('row').nth(1)).toBeVisible();
  });
}
