// A crawl's sections, in sidebar order, with their routes and Ctrl+1 to Ctrl+7 (docs/v2-ui-ia.md), and the Report,
// which joins them once its screen exists (app/simpleScreens.ts): the eighth in Advanced, the third in Simple.

export type CrawlSection =
  'overview' | 'issues' | 'urls' | 'links' | 'sitemaps' | 'robots' | 'settings' | 'extraction' | 'report';

export interface CrawlSectionEntry {
  id: CrawlSection;
  /** Its name in Advanced. */
  title: string;
  /** Its name in Simple, where Simple has it ("Summary", "What to fix"). */
  simpleTitle?: string;
  to: string;
  /** Its key in Advanced. */
  key: string;
  /** Its key in Simple, where Simple has it. */
  simpleKey?: string;
}

export const CRAWL_SECTIONS: readonly CrawlSectionEntry[] = [
  {
    id: 'overview',
    title: 'Overview',
    simpleTitle: 'Summary',
    to: '/crawls/$crawl/overview',
    key: 'mod+1',
    simpleKey: 'mod+1',
  },
  {
    id: 'issues',
    title: 'Issues',
    simpleTitle: 'What to fix',
    to: '/crawls/$crawl/issues/{-$check}',
    key: 'mod+2',
    simpleKey: 'mod+2',
  },
  { id: 'urls', title: 'URLs', to: '/crawls/$crawl/urls', key: 'mod+3' },
  { id: 'links', title: 'Links', to: '/crawls/$crawl/links', key: 'mod+4' },
  { id: 'sitemaps', title: 'Sitemaps', to: '/crawls/$crawl/sitemaps/{-$sitemap}', key: 'mod+5' },
  { id: 'robots', title: 'robots.txt', to: '/crawls/$crawl/robots/{-$origin}', key: 'mod+6' },
  // Not "Settings": that is the app's, at the foot of the sidebar. This is the record of how this crawl was made.
  { id: 'settings', title: 'This crawl’s settings', to: '/crawls/$crawl/settings', key: 'mod+7' },
  {
    id: 'extraction',
    title: 'Custom extraction',
    simpleTitle: 'Custom data',
    to: '/crawls/$crawl/extraction',
    key: 'mod+9',
    simpleKey: 'mod+4',
  },
];

/** The Report: listed after the others, below a rule, only once its screen exists. */
export const REPORT_SECTION: CrawlSectionEntry = {
  id: 'report',
  title: 'Report',
  simpleTitle: 'Report',
  to: '/crawls/$crawl/report',
  key: 'mod+8',
  simpleKey: 'mod+3',
};

export function isCrawlSection(value: string | undefined): value is CrawlSection {
  return value === REPORT_SECTION.id || CRAWL_SECTIONS.some((section) => section.id === value);
}

/** The section a crawl path is in: `/crawls/<id>/urls` is `urls`. */
export function sectionOf(pathname: string): CrawlSection | null {
  const match = /^\/crawls\/[0-9a-f]{16}\/([a-z]+)/.exec(pathname);
  return match && isCrawlSection(match[1]) ? match[1] : null;
}

/** A section's entry by its id, the Report's included. */
export function crawlSectionEntry(section: CrawlSection): CrawlSectionEntry {
  return CRAWL_SECTIONS.find((entry) => entry.id === section) ?? REPORT_SECTION;
}

export const CRAWL_ID = /^[0-9a-f]{16}$/;
