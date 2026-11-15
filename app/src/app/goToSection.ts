import type { useNavigate } from '@tanstack/react-router';
import type { CrawlSection } from './sections';

/**
 * Goes to a crawl's section, at its start: Issues' list, the Sitemaps list, the first robots.txt (the keys Ctrl+1 to
 * Ctrl+8, and the sidebar's crawl switcher, which keeps the section you're on).
 */
export function goToSection(navigate: ReturnType<typeof useNavigate>, section: CrawlSection, crawl: string) {
  switch (section) {
    case 'issues':
      return navigate({ to: '/crawls/$crawl/issues/{-$check}', params: { crawl } });
    case 'urls':
      return navigate({ to: '/crawls/$crawl/urls', params: { crawl } });
    case 'links':
      return navigate({ to: '/crawls/$crawl/links', params: { crawl } });
    case 'sitemaps':
      // The list, from one sitemap's page too: left out, the router keeps the sitemap the page is on.
      return navigate({ to: '/crawls/$crawl/sitemaps/{-$sitemap}', params: { crawl, sitemap: undefined } });
    case 'robots':
      return navigate({ to: '/crawls/$crawl/robots/{-$origin}', params: { crawl, origin: undefined } });
    case 'settings':
      return navigate({ to: '/crawls/$crawl/settings', params: { crawl } });
    case 'report':
      return navigate({ to: '/crawls/$crawl/report', params: { crawl } });
    default:
      return navigate({ to: '/crawls/$crawl/overview', params: { crawl } });
  }
}
