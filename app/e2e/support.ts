// What the end-to-end specs share: the sidecar the tests run against (asked from the test itself, to know what the
// crawl holds), the way into a crawl's section as a user goes, and axe with the wait for a theme's colours to settle.
import AxeBuilder from '@axe-core/playwright';
import { expect, type Locator, type Page } from '@playwright/test';
import { colourTweensEnd } from './colourTweens';

// The wait itself is one function for every spec (e2e/colourTweens.ts): still reachable from here, as it was.
export { colourTweensEnd };

export const env = (name: string) => {
  const value = process.env[name];
  if (!value) throw new Error(`global-setup sets ${name}`);
  return value;
};

/** What the library calls the crawl the tests run against. */
export const site = () => env('SI_E2E_SITE');

/** The same crawl as Home's rows name its site: its host and any port of its own ("127.0.0.1:5001"). */
export const siteRow = () => env('SI_E2E_SITE_ROW');

/** The sidecar, from the test itself (no page, so no CORS). */
export async function sidecar(route: string, init: Omit<RequestInit, 'headers'> = {}): Promise<Response> {
  return fetch(`${env('SI_E2E_SIDECAR')}${route}`, {
    ...init,
    headers: { Authorization: `Bearer ${env('SI_E2E_TOKEN')}`, 'Content-Type': 'application/json' },
  });
}

/** A read of the sidecar that must work: its JSON. */
export async function read<T>(route: string, body?: unknown): Promise<T> {
  const response = await sidecar(route, body === undefined ? {} : { method: 'POST', body: JSON.stringify(body) });
  if (!response.ok) throw new Error(`${route}: ${response.status} ${await response.text()}`);
  return (await response.json()) as T;
}

export interface SeededCrawl {
  id: string;
  site: string | null;
  fileName: string;
  startUrl: string | null;
  status: string;
  pageCount: number;
}

/** The crawl the tests run against: the one the library names as `site()`. */
export async function seededCrawl(): Promise<SeededCrawl> {
  const crawls = await read<SeededCrawl[]>('/api/crawls');
  const crawl = crawls.find((candidate) => (candidate.site ?? candidate.fileName) === site());
  if (!crawl) throw new Error(`The sidecar doesn’t list ${site()}`);
  return crawl;
}

const WORKING = ['running', 'paused', 'stopping', 'analyzing'];

/** Waits until the sidecar is done with a crawl (crawling it, or analysing it), and gives its item. */
async function settled(id: string, timeoutMs: number): Promise<SeededCrawl & { analyzedAt: string | null }> {
  const started = Date.now();
  for (;;) {
    const item = await read<SeededCrawl & { analyzedAt: string | null }>(`/api/crawls/${id}`);
    if (!WORKING.includes(item.status)) return item;
    if (Date.now() - started > timeoutMs)
      throw new Error(`The crawl ${id} was still ${item.status} after ${timeoutMs} ms`);
    await new Promise((resolve) => setTimeout(resolve, 200));
  }
}

/**
 * A crawl of the engine's synthetic test site (never a real site), made for a spec that needs what the seeded crawl
 * is too small to hold: crawled to its end at the Fast preset, then analysed. The site links to URLs its robots.txt
 * disallows from every 61st product, so a crawl of all of it is refused ten. Remove it with `removeCrawl` when the
 * spec is done: the other specs expect the library to hold the seeded crawl alone. `startUrl` is another synthetic site
 * of the spec's own (e2e/revisionSite.ts), the shared one by default.
 */
export async function crawlTestSite(startUrl: string = env('SI_E2E_TESTSITE')): Promise<SeededCrawl> {
  const started = await read<SeededCrawl>('/api/crawls', {
    options: { startUrl, concurrency: 16, maxRequestsPerSecond: 0 },
  });
  const crawled = await settled(started.id, 120_000);
  if (crawled.status !== 'completed') throw new Error(`The test site's crawl ended ${crawled.status}`);
  // The app analyses a crawl when it ends, if a window is watching: here nothing may be.
  if (crawled.analyzedAt === null) {
    const response = await sidecar(`/api/crawls/${started.id}/analyze`, { method: 'POST' });
    // 409: the app's own request got there first.
    if (!response.ok && response.status !== 409)
      throw new Error(`analyze: ${response.status} ${await response.text()}`);
  }
  return settled(started.id, 120_000);
}

/** Deletes a crawl a spec made, file and all. */
export async function removeCrawl(id: string): Promise<void> {
  await settled(id, 60_000);
  await sidecar(`/api/crawls/${id}/delete-file`, { method: 'POST' });
}

/** axe at WCAG 2.2 AA, with zero violations allowed, once the theme's colours have settled. */
export async function expectNoViolations(page: Page) {
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

/** From Home to one of the crawl's sections, through the library and the sidebar, as a user goes. */
export async function openSection(page: Page, section: RegExp) {
  await page.goto('/');
  await page.getByRole('region', { name: 'Library' }).getByRole('link', { name: siteRow(), exact: true }).click();
  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: section }).first().click();
}

/** A count as written on screen ("101,081", "2,348 URLs"), as a number. */
export const countOf = (text: string | null) => Number((text ?? '').replace(/[^\d]/g, ''));

/**
 * Chooses one of a strip's tabs by its name, as a user does: in the strip when it's shown there, from the strip's
 * "N more" menu when it isn't (polish 2: a tab is in one or the other). Tried again until chosen, as counts arriving
 * can move a tab between the two.
 */
export async function chooseTab(scope: Locator, name: RegExp) {
  const tab = scope.getByRole('tab', { name });
  await expect(async () => {
    if (await tab.isVisible()) await tab.click({ timeout: 2_000 });
    else {
      await scope.getByRole('button', { name: /^\d+ more: / }).click({ timeout: 2_000 });
      await scope.page().getByRole('menu').getByRole('menuitem', { name }).click({ timeout: 2_000 });
    }
    await expect(tab).toHaveAttribute('aria-selected', 'true', { timeout: 1_000 });
  }).toPass({ timeout: 15_000 });
}
