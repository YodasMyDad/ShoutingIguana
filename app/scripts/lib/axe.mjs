// axe on a page that may be running axe itself (the story check).

/** axe-core's own words for a run asked for while another is in flight on the same page. */
const BUSY = 'Axe is already running';

/**
 * Runs `analyse` (an axe run on a page), and again while the page's own axe is at work. Storybook's a11y addon
 * checks every story as it renders, with the one axe the page has: a run asked for meanwhile is refused, not queued.
 * On a busy machine a story's own check can outlast the wait before ours, and the refusal used to end the whole
 * check with no result. Any other failure is thrown as it is, at once; a page that stays busy is given up on after
 * `attempts` tries, with axe's own error.
 *
 * @template T
 * @param {() => Promise<T>} analyse
 * @param {{ attempts?: number, waitMs?: number, sleep?: (ms: number) => Promise<void> }} [options]
 * @returns {Promise<T>}
 */
export async function whenAxeIsFree(
  analyse,
  { attempts = 40, waitMs = 250, sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms)) } = {},
) {
  for (let attempt = 1; ; attempt++) {
    try {
      return await analyse();
    } catch (error) {
      const busy = String(error instanceof Error ? error.message : error).includes(BUSY);
      if (!busy || attempt >= attempts) throw error;
      await sleep(waitMs);
    }
  }
}
