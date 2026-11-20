import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import clsx from 'clsx';
import { api } from '../../api/client';
import { sitemapsQuery } from '../../api/queries';
import { notifyResult } from '../../feedback/notify';
import { robotsOriginOf } from '../../inspector/placeLinks';
import { IssueUrl } from './IssueUrl';
import { detailParts } from './detailParts';

export interface DetailWordsProps {
  crawlId: string;
  detail: string;
  /** The files the row is already about (its own column names them): said by name here, not in full again. */
  files?: readonly string[] | undefined;
  siteOrigin: string | null;
  /** Over the list, where there's room for more than a line; in a row it's one line. */
  wrap?: boolean | undefined;
  /** Inspects a URL of the crawl: what a click on any other address it names does, once it's looked up. */
  onInspect?: ((urlId: number) => void) | undefined;
}

/**
 * A row's detail (A-18): its words, with each address it names a link like any other, to its own screen where it has
 * one (a sitemap in Sitemaps, a robots.txt of its origin), else to the inspector (the address is looked up in the crawl
 * when it's pressed, and a toast says so if the crawl doesn't have it), with its open-in-browser button after it. A
 * detail that names no address is its words alone, as it always was.
 */
export function DetailWords({ crawlId, detail, files = [], siteOrigin, wrap = false, onInspect }: DetailWordsProps) {
  const queryClient = useQueryClient();
  const parts = detailParts(detail, files);
  const names = parts.some((part) => part.kind === 'url');
  const navigate = useNavigate();
  // The crawl's sitemaps, to know one by its address: read only for a detail that names an address.
  const sitemaps = useQuery({ ...sitemapsQuery(crawlId), enabled: names }).data?.sitemaps;
  if (!parts.some((part) => part.kind !== 'text')) return <span title={detail}>{detail}</span>;
  return (
    <span
      className={clsx('flex min-w-0 items-center gap-x-1', wrap ? 'flex-wrap gap-y-0.5' : 'overflow-hidden')}
      title={detail}
      data-detail-words
    >
      {parts.map((part, at) => {
        if (part.kind === 'text') {
          return (
            <span key={at} className={clsx('whitespace-pre', !wrap && 'min-w-0 shrink truncate')}>
              {part.text}
            </span>
          );
        }
        if (part.kind === 'file') {
          return (
            <span key={at} className="shrink-0">
              {part.name}
            </span>
          );
        }
        const sitemap = sitemaps?.find((candidate) => candidate.url === part.url);
        const origin = /\/robots\.txt$/i.test(part.url) ? robotsOriginOf(part.url) : null;
        const go = sitemap
          ? () =>
              void navigate({
                to: '/crawls/$crawl/sitemaps/{-$sitemap}',
                params: { crawl: crawlId, sitemap: String(sitemap.id) },
              })
          : origin
            ? () =>
                void navigate({
                  to: '/crawls/$crawl/robots/{-$origin}',
                  params: { crawl: crawlId, origin },
                  search: {},
                })
            : onInspect && robotsOriginOf(part.url) !== null
              ? () =>
                  void queryClient
                    .query({
                      queryKey: ['crawl', crawlId, 'lookup', part.url],
                      queryFn: ({ signal }) => api.lookupUrl(crawlId, part.url, signal),
                      retry: false,
                      staleTime: Infinity,
                    })
                    .then(
                      (found) => onInspect(found.id),
                      () => notifyResult('The crawl doesn’t have this URL', { description: part.url }),
                    )
              : undefined;
        return (
          <IssueUrl
            key={at}
            url={part.url}
            siteOrigin={siteOrigin}
            onInspect={go}
            goHint={sitemap ? 'Open in Sitemaps' : origin ? 'Open robots.txt' : 'Inspect'}
            className="shrink-0"
          />
        );
      })}
    </span>
  );
}
