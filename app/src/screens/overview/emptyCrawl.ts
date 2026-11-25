// A crawl that read no pages: why, from how its start URL answered, and what to do next (docs/v2-ui-brief.md, "Every
// view has every state": Empty explains why, with the next action). Pure.
import type { UrlRow } from '../../api/grids';
import type { AlertData, CrawlListItem } from '../../api/types';
import { blockedWords, slowedDown } from '../../lib/blockedAlert';
import type { StartRobots } from '../../simple/nothingRead';

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
 * while it isn't known (still loading, or it couldn't be read), which says only what the library knows. `robots` is
 * how the start URL's robots.txt answered, for a start URL robots.txt kept the crawl from: rules that refuse it, an
 * error and no answer are three sentences, as Simple's "No pages could be read" says them (round 5's gap 1).
 * `blocked` is the facts of the crawl's `blocked` alert, for a start URL a firewall or a check answered: whether a
 * browser was tried, whether a person was asked to complete the check, and how that ended.
 */
export function emptyCrawl(
  item: Pick<CrawlListItem, 'status' | 'fetchedUrlCount'>,
  start: UrlRow | null | undefined,
  robots?: StartRobots,
  blocked?: AlertData | null,
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
    case 'blocked': {
      const answered = `A firewall or bot challenge answered the start URL${code === null ? '' : ` (${statusWords(code)})`}, so the crawl got no further.`;
      const browser = blockedWords(blocked);
      if (browser) {
        return {
          title,
          why: `${answered} ${browser.what} ${browser.advice}`,
          action: browser.next === 'from' ? 'crawlFrom' : browser.next === 'otherWays' ? 'crawlSettings' : 'crawlAgain',
          ...(browser.url ? { url: browser.url } : {}),
        };
      }
      return {
        title,
        why: `${answered} ${
          // A host the crawl already slowed down for by itself: going more slowly is no way on.
          slowedDown(blocked) ? 'Add' : 'Crawl more slowly, add'
        } the headers the site expects, or crawl as another user agent.`,
        action: 'crawlSettings',
      };
    }
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
      return robotsKeptOut(title, robots, unfinished);
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

/** The start URL robots.txt kept the crawl from, by how that robots.txt answered. */
function robotsKeptOut(title: string, robots: StartRobots | undefined, unfinished: boolean): EmptyCrawl {
  const again = unfinished ? 'continue' : 'crawlAgain';
  if (robots?.state === 'unreachable' && robots.statusCode !== null) {
    return {
      title,
      why: `The site’s robots.txt answered ${statusWords(robots.statusCode)}, so the crawl kept out of the whole site, as search engines do, and read nothing. Crawl it again once robots.txt answers.`,
      action: again,
    };
  }
  if (robots?.state === 'unreachable' || robots?.state === 'unanswered') {
    return {
      title,
      why: 'The site didn’t answer when the crawl asked for its robots.txt, so it read nothing. Check the address and that the site is up, then crawl it again.',
      action: again,
    };
  }
  if (robots?.state === 'read') {
    return {
      title,
      why: 'The site’s robots.txt doesn’t allow the start URL to be crawled. Crawl again with robots.txt ignored if the site is yours to audit.',
      action: 'crawlSettings',
    };
  }
  // Not known yet (or no file kept): true whichever way the file answered.
  return {
    title,
    why: 'The site’s robots.txt kept the crawl from the start URL, so it read nothing. Crawl again with robots.txt ignored if the site is yours to audit.',
    action: 'crawlSettings',
  };
}
