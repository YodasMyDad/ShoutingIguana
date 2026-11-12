// Which screens each mode has, and where switching lands (docs/v2-ui-simple.md, The switch and Routes). Pure: the
// switch (mode/switchMode.ts) and the guard that turns Advanced on (mode/useModeGuard.ts) act on what this says.
//
// - A route with a Simple screen (app/simpleScreens.ts) shows it in Simple; one whose row is still empty shows its
//   Advanced screen in either mode.
// - URLs, Links, Sitemaps, robots.txt, the crawl's settings and four of Settings' sections are Advanced only. Arriving
//   at one in Simple turns Advanced on; switching to Simple from one goes to the nearest screen Simple has.
import { CRAWL_ID, isCrawlSection, type CrawlSection } from './sections';

/** The crawl's sections Simple doesn't have, by the names the "Advanced is on" toast gives them, mid-sentence. */
const ADVANCED_ONLY_SECTIONS: Partial<Record<CrawlSection, string>> = {
  urls: 'URLs',
  links: 'Links',
  sitemaps: 'Sitemaps',
  robots: 'robots.txt',
  settings: 'the crawl’s settings',
};

/** Settings' sections Simple doesn't list (by their id in `/settings/:section`). */
export const ADVANCED_ONLY_SETTINGS: Readonly<Record<string, string>> = {
  'crawl-defaults': 'Crawl defaults',
  analysis: 'Analysis',
  shortcuts: 'Shortcuts',
  diagnostics: 'Diagnostics',
};

export function isAdvancedOnlySetting(section: string): boolean {
  return Object.hasOwn(ADVANCED_ONLY_SETTINGS, section);
}

/** A crawl's section by its path: `/crawls/<id>/urls/…` is `urls`, with the crawl's id. */
function crawlPlace(pathname: string): { crawl: string; section: string | null } | null {
  const match = /^\/crawls\/([^/]+)(?:\/([^/]+))?/.exec(pathname);
  if (!match?.[1] || !CRAWL_ID.test(match[1])) return null;
  return { crawl: match[1], section: match[2] ?? null };
}

/** The Advanced-only screen a path is, by name ("URLs", "Crawl defaults"), or null when Simple has it. */
export function advancedOnlyScreen(pathname: string): string | null {
  const crawl = crawlPlace(pathname);
  if (crawl?.section && isCrawlSection(crawl.section)) return ADVANCED_ONLY_SECTIONS[crawl.section] ?? null;
  const setting = /^\/settings\/([^/]+)/.exec(pathname)?.[1];
  return setting && isAdvancedOnlySetting(setting) ? (ADVANCED_ONLY_SETTINGS[setting] ?? null) : null;
}

/** Where switching to Simple takes the user from a path: null to stay on the same route, which Simple has. */
export type SimpleLanding =
  | { to: '/crawls/$crawl/overview'; params: { crawl: string } }
  | { to: '/settings/{-$section}'; params: { section?: undefined } };

/**
 * The nearest screen Simple has, from an Advanced-only one: the crawl's Summary, or Settings > General. Null from a
 * screen Simple has: the same route renders its other view, its search params kept (and ignored where Simple has no use
 * for them), so switching back restores them.
 */
export function simpleLanding(pathname: string): SimpleLanding | null {
  if (advancedOnlyScreen(pathname) === null) return null;
  const crawl = crawlPlace(pathname);
  return crawl
    ? { to: '/crawls/$crawl/overview', params: { crawl: crawl.crawl } }
    : { to: '/settings/{-$section}', params: { section: undefined } };
}

/** The crawl's sections Simple has, in sidebar order: Summary, What to fix, and the Report once it exists. */
export const SIMPLE_SECTIONS: readonly CrawlSection[] = ['overview', 'issues', 'report'];

/** Whether Simple has a crawl's section: a crawl reopens on its last view only if the mode has it. */
export function simpleHasSection(section: CrawlSection): boolean {
  return SIMPLE_SECTIONS.includes(section);
}
