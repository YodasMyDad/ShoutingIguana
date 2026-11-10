// A crawl that read no pages: why, from how its start URL answered, and what to do next (docs/v2-ui-brief.md, "Every
// view has every state": Empty explains why, with the next action). Pure.
import type { UrlRow } from '../../api/grids';
import type { CrawlListItem } from '../../api/types';

export type EmptyCrawlAction = 'continue' | 'crawlAgain' | 'crawlFrom' | 'crawlSettings';

export interface EmptyCrawl {
  title: string;
  /** Why there's nothing, in a sentence or two. */
  why: string;
  action: EmptyCrawlAction | null;
  /** For `crawlFrom`: where the start URL leads. */
  url?: string;
}

const REASONS: Record<number, string> = {
  400: 'Bad Request',
  401: 'Unauthorized',
  403: 'Forbidden',
  404: 'Not Found',
  410: 'Gone',
  429: 'Too Many Requests',
  500: 'Internal Server Error',
  502: 'Bad Gateway',
  503: 'Service Unavailable',
  504: 'Gateway Timeout',
};

const statusWords = (code: number) => (REASONS[code] ? `${code} ${REASONS[code]}` : String(code));

/** Whether a crawl that isn't running has nothing to show: it read no pages. */
export function isEmptyCrawl(item: Pick<CrawlListItem, 'pageCount' | 'status'>): boolean {
  return item.pageCount === 0 && item.status !== 'unreadable' && item.status !== 'inUse';
}

/**
 * Why the crawl read no pages. `start` is the start URL's row as the crawl stored it: null when it has none, undefined
 * while it isn't known (still loading, or it couldn't be read), which says only what the library knows.
 */
export function emptyCrawl(
  item: Pick<CrawlListItem, 'status' | 'fetchedUrlCount'>,
  start: UrlRow | null | undefined,
): EmptyCrawl {
  const unfinished = item.status === 'stopped' || item.status === 'interrupted';
  const title = 'This crawl didn’t read any pages';
  if (start === undefined) {
    return unfinished
      ? {
          title,
          why: 'It stopped before it read one. Continue it to carry on from where it stopped.',
          action: 'continue',
        }
      : { title, why: 'The start URL didn’t answer with a page the crawl could read.', action: 'crawlAgain' };
  }
  const code = start?.statusCode ?? null;
  switch (start?.outcome) {
    case 'blocked':
      return {
        title,
        why: `A firewall or bot challenge answered the start URL${code === null ? '' : ` (${statusWords(code)})`}, so the crawl got no further. Crawl more slowly, add the headers the site expects, or crawl as another user agent.`,
        action: 'crawlSettings',
      };
    case 'clientError':
      return {
        title,
        why: `The start URL answered ${code === null ? 'with a client error' : statusWords(code)}, so there was nothing to follow. Check the address${code === 401 || code === 403 ? ', or add the headers the site needs to let the crawl in' : ''}.`,
        action: 'crawlAgain',
      };
    case 'serverError':
      return {
        title,
        why: `The start URL answered ${code === null ? 'with a server error' : statusWords(code)}: the site was failing when it was crawled. Crawl it again once it’s back.`,
        action: 'crawlAgain',
      };
    case 'redirect':
      return start.redirectTarget
        ? {
            title,
            why: `The start URL redirects to ${start.redirectTarget}, which this crawl didn’t follow as part of the site. Crawl from there instead.`,
            action: 'crawlFrom',
            url: start.redirectTarget,
          }
        : { title, why: 'The start URL redirects, and the crawl didn’t reach a page.', action: 'crawlAgain' };
    case 'robotsDisallowed':
      return {
        title,
        why: 'The site’s robots.txt doesn’t allow the start URL to be crawled (or couldn’t be read). Crawl again with robots.txt ignored if the site is yours to audit.',
        action: 'crawlSettings',
      };
    case 'timeout':
      return {
        title,
        why: 'The start URL didn’t answer in time. Crawl it again, with a longer timeout if the site is slow.',
        action: unfinished ? 'continue' : 'crawlAgain',
      };
    case 'networkError':
    case 'notChecked':
      return {
        title,
        why: 'The site couldn’t be reached: check the address and that the site is up, then crawl it again.',
        action: unfinished ? 'continue' : 'crawlAgain',
      };
    case 'processingError':
      return {
        title,
        why: 'The start URL answered, but what it sent couldn’t be read as a page.',
        action: 'crawlAgain',
      };
    case 'success':
      return {
        title,
        why: start.contentType
          ? `The start URL answered with ${start.contentType}, which isn’t a web page, so there were no links to follow.`
          : 'The start URL answered, but not with a web page, so there were no links to follow.',
        action: 'crawlAgain',
      };
    default:
      // No outcome: the start URL was never asked.
      return unfinished
        ? { title, why: 'It stopped before it fetched its start URL. Continue it to carry on.', action: 'continue' }
        : { title, why: 'It ended before it fetched its start URL.', action: 'crawlAgain' };
  }
}
