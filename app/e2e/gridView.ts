// Shared by the specs that measure a grid in a short window (200% zoom of 1280x720 is 640x360 CSS pixels): how tall the
// grid is, how many of its rows are whole in the window, and the way to its foot.
import type { Locator, Page } from '@playwright/test';

/** The grid's box and the rows of it that are wholly inside the window, header left out. */
export async function gridInView(grid: Locator) {
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
export const scrollToFoot = (page: Page) =>
  page.getByRole('main').evaluate((element) => {
    element.scrollTop = element.scrollHeight;
  });
