import { linkOptions, type LinkProps } from '@tanstack/react-router';
import type { OverviewLink } from './links';

/**
 * A figure's link as the router takes it: the URL grid with its filters, or Issues at a check, a severity or a text.
 * Each is checked against its own route here (`linkOptions`), then handed on as any link's props, which is what the
 * components that draw links take.
 */
export function linkProps(crawlId: string, link: OverviewLink): LinkProps {
  if (link.to === 'urls') {
    return linkOptions({
      to: '/crawls/$crawl/urls',
      params: { crawl: crawlId },
      search: link.filters.length > 0 ? { filter: link.filters } : {},
    }) as LinkProps;
  }
  return linkOptions({
    to: '/crawls/$crawl/issues/{-$check}',
    params: { crawl: crawlId, check: link.check },
    search: {
      ...(link.severity ? { severity: [link.severity] } : {}),
      ...(link.q ? { q: link.q } : {}),
    },
  }) as LinkProps;
}
