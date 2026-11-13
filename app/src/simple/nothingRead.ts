// A site check that read no pages, in Simple's words (docs/v2-ui-simple.md, "Every state": Nothing could be read): how
// the start address answered, and the next action. Advanced says the same in its words (screens/overview/emptyCrawl.ts).
import type { UrlRow } from '../api/grids';
import type { CrawlListItem } from '../api/types';

export type NothingReadAction = 'continue' | 'checkAgain' | 'checkThere' | 'robotsInAdvanced' | 'blockedInAdvanced';

export interface NothingRead {
  /** How the start address answered, in a sentence or two. */
  why: string;
  action: NothingReadAction;
  /** For `checkThere`: where the address forwards to. */
  url?: string;
}

/**
 * Why a site check read no pages. `start` is the start address's row as the check stored it: null when it has none,
 * undefined while it isn't known.
 */
export function nothingRead(item: Pick<CrawlListItem, 'status'>, start: UrlRow | null | undefined): NothingRead {
  const unfinished = item.status === 'stopped' || item.status === 'interrupted';
  const code = start?.statusCode ?? null;
  switch (start?.outcome) {
    case 'clientError':
      if (code === 401) {
        return {
          why: 'The address asks for a password, so the site is private and nothing could be read.',
          action: 'checkAgain',
        };
      }
      if (code === 403) {
        return {
          why: 'The site refused to show the address to the check (a 403, “forbidden”). It may be private, or its security may be turning visitors like this one away.',
          action: 'blockedInAdvanced',
        };
      }
      return {
        why:
          code === 404 || code === 410
            ? `The address answered “not found” (a ${code}). Check the address, then try again.`
            : `The address answered with an error${code === null ? '' : ` (a ${code})`}. Check the address, then try again.`,
        action: 'checkAgain',
      };
    case 'blocked':
      return {
        why: 'The site’s security turned the check away before it read a page: it took the check for a robot.',
        action: 'blockedInAdvanced',
      };
    case 'robotsDisallowed':
      return {
        why: 'The site’s rules file for search engines (its “robots.txt”) tells checkers to stay out, so nothing was read. Ask the site’s owner, or check it in Advanced with the rules file ignored if the site is yours.',
        action: 'robotsInAdvanced',
      };
    case 'serverError':
      return {
        why: `The site answered with a server error${code === null ? '' : ` (a ${code})`}: it was failing when it was checked. Try again later.`,
        action: 'checkAgain',
      };
    case 'redirect':
      return start.redirectTarget
        ? {
            why: `This address forwards to another site, ${start.redirectTarget}, which the check doesn’t read as part of this one.`,
            action: 'checkThere',
            url: start.redirectTarget,
          }
        : { why: 'This address forwards elsewhere, and the check didn’t reach a page.', action: 'checkAgain' };
    case 'timeout':
    case 'networkError':
    case 'notChecked':
      return {
        why: 'The site didn’t answer. Check the address and that the site is up, then try again later.',
        action: unfinished ? 'continue' : 'checkAgain',
      };
    case 'processingError':
      return { why: 'The address answered, but what it sent couldn’t be read as a page.', action: 'checkAgain' };
    case 'success':
      return {
        why: 'The address answered with a file, not a web page, so there were no pages to read.',
        action: 'checkAgain',
      };
    default:
      return unfinished
        ? { why: 'The check was stopped before it read a page. Continue it to carry on.', action: 'continue' }
        : { why: 'The check ended before the site’s first page answered.', action: 'checkAgain' };
  }
}
