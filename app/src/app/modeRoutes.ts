// Which screens each mode has, and where switching lands (docs/v2-ui-simple.md, The switch and Routes). Pure: the
// switch (mode/switchMode.ts) and the guard that turns Advanced on (mode/useModeGuard.ts) act on what this says.
//
// - A route with a Simple screen (app/simpleScreens.ts) shows it in Simple; one whose row is still empty shows its
//   Advanced screen in either mode.
// - URLs, Links, Sitemaps, robots.txt and the crawl's settings are Advanced only. Arriving at one in Simple turns
//   Advanced on; switching to Simple from one goes to the crawl's Summary.
// - Settings (`/settings/:section?`) is the same screen in both modes, every section of it (docs/v2-ui-simple.md,
//   Settings in each mode): the owner's word, "the Simple and Advanced modes are just for the UI for the user of using
//   the app, not for settings". So no Settings section is here, and switching on one stays on it.
import { CRAWL_ID, isCrawlSection, type CrawlSection } from './sections';

/** The crawl's sections Simple doesn't have, by the names the "Advanced is on" toast gives them, mid-sentence. */
const ADVANCED_ONLY_SECTIONS: Partial<Record<CrawlSection, string>> = {
  urls: 'URLs',
  links: 'Links',
  sitemaps: 'Sitemaps',
  robots: 'robots.txt',
  settings: 'the crawl’s settings',
};

/** A crawl's section by its path: `/crawls/<id>/urls/…` is `urls`, with the crawl's id. */
function crawlPlace(pathname: string): { crawl: string; section: string | null } | null {
  const match = /^\/crawls\/([^/]+)(?:\/([^/]+))?/.exec(pathname);
  if (!match?.[1] || !CRAWL_ID.test(match[1])) return null;
  return { crawl: match[1], section: match[2] ?? null };
}

/** The crawl a path is in, by its id, or null outside a crawl. */
export function crawlIdOf(pathname: string): string | null {
  return crawlPlace(pathname)?.crawl ?? null;
}

/** The Advanced-only screen a path is, by name ("URLs", "the crawl’s settings"), or null when Simple has it. */
export function advancedOnlyScreen(pathname: string): string | null {
  const crawl = crawlPlace(pathname);
  return crawl?.section && isCrawlSection(crawl.section) ? (ADVANCED_ONLY_SECTIONS[crawl.section] ?? null) : null;
}

/** Where switching to Simple takes the user from a path: null to stay on the same route, which Simple has. */
export interface SimpleLanding {
  to: '/crawls/$crawl/overview';
  params: { crawl: string };
}

/**
 * The nearest screen Simple has, from an Advanced-only one: the crawl's Summary. Null from a screen Simple has (Settings
 * among them, every section): the same route renders its other view, its search params kept (and ignored where Simple
 * has no use for them), so switching back restores them.
 */
export function simpleLanding(pathname: string): SimpleLanding | null {
  const crawl = crawlPlace(pathname);
  return crawl && advancedOnlyScreen(pathname) !== null
    ? { to: '/crawls/$crawl/overview', params: { crawl: crawl.crawl } }
    : null;
}

/** The crawl's sections Simple has, in sidebar order: Summary, What to fix, Custom data, and the Report once it exists. */
export const SIMPLE_SECTIONS: readonly CrawlSection[] = ['overview', 'issues', 'extraction', 'report'];

/** Whether Simple has a crawl's section: a crawl reopens on its last view only if the mode has it. */
export function simpleHasSection(section: CrawlSection): boolean {
  return SIMPLE_SECTIONS.includes(section);
}
