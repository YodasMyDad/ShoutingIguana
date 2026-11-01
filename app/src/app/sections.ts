// A crawl's sections, in sidebar order, with their routes and Ctrl+1 to Ctrl+7 (docs/v2-ui-ia.md).

export type CrawlSection = 'overview' | 'issues' | 'urls' | 'links' | 'sitemaps' | 'robots' | 'settings';

export const CRAWL_SECTIONS: readonly { id: CrawlSection; title: string; to: string; key: string }[] = [
  { id: 'overview', title: 'Overview', to: '/crawls/$crawl/overview', key: 'mod+1' },
  { id: 'issues', title: 'Issues', to: '/crawls/$crawl/issues/{-$check}', key: 'mod+2' },
  { id: 'urls', title: 'URLs', to: '/crawls/$crawl/urls', key: 'mod+3' },
  { id: 'links', title: 'Links', to: '/crawls/$crawl/links', key: 'mod+4' },
  { id: 'sitemaps', title: 'Sitemaps', to: '/crawls/$crawl/sitemaps/{-$sitemap}', key: 'mod+5' },
  { id: 'robots', title: 'robots.txt', to: '/crawls/$crawl/robots/{-$origin}', key: 'mod+6' },
  { id: 'settings', title: 'Settings', to: '/crawls/$crawl/settings', key: 'mod+7' },
];

export function isCrawlSection(value: string | undefined): value is CrawlSection {
  return CRAWL_SECTIONS.some((section) => section.id === value);
}

/** The section a crawl path is in: `/crawls/<id>/urls` is `urls`. */
export function sectionOf(pathname: string): CrawlSection | null {
  const match = /^\/crawls\/[0-9a-f]{16}\/([a-z]+)/.exec(pathname);
  return match && isCrawlSection(match[1]) ? match[1] : null;
}

export const CRAWL_ID = /^[0-9a-f]{16}$/;
