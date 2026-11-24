// A site check that read no pages, in Simple's words (docs/v2-ui-simple.md, "Every state": Nothing could be read): how
// the start address answered, and the next action. Advanced says the same in its words (screens/overview/emptyCrawl.ts).
import type { RobotsState, UrlRow } from '../api/grids';
import type { AlertData, CrawlListItem } from '../api/types';
import { blockedPlainWords } from '../lib/blockedAlert';

export type NothingReadAction = 'continue' | 'checkAgain' | 'checkThere' | 'robotsInAdvanced' | 'blockedInAdvanced';

export interface NothingRead {
  /** How the start address answered, in a sentence or two. */
  why: string;
  action: NothingReadAction;
  /** For `checkThere`: where the address forwards to. */
  url?: string;
}

/** How the start address's own robots.txt answered: its state and status, as `/robots/file` gives them. */
export interface StartRobots {
  state: RobotsState | null;
  statusCode: number | null;
}

/**
 * Why a site check read no pages. `start` is the start address's row as the check stored it: null when it has none,
 * undefined while it isn't known. `robots` is how its origin's robots.txt answered, for a start address robots.txt
 * kept the check from: a file whose rules refuse it, one that answered an error, and one that didn't answer are three
 * different sentences (slice rules, item 2), never "tells checkers to stay out" for a file that couldn't be read.
 * `blocked` is the facts of the check's `blocked` alert: whether a browser was tried on a site whose security turned
 * the check away, whether the person was asked to show they're one, and how that ended.
 */
export function nothingRead(
  item: Pick<CrawlListItem, 'status'>,
  start: UrlRow | null | undefined,
  robots?: StartRobots,
  blocked?: AlertData | null,
): NothingRead {
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
    case 'blocked': {
      const turnedAway =
        'The site’s security turned the check away before it read a page: it took the check for a robot.';
      const browser = blockedPlainWords(blocked);
      if (browser) {
        return {
          why: `${turnedAway} ${browser.what} ${browser.advice}`,
          action:
            browser.next === 'from' ? 'checkThere' : browser.next === 'otherWays' ? 'blockedInAdvanced' : 'checkAgain',
          ...(browser.url ? { url: browser.url } : {}),
        };
      }
      return { why: turnedAway, action: 'blockedInAdvanced' };
    }
    case 'robotsDisallowed':
      return robotsKeptOut(robots, unfinished);
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

/** The start address robots.txt kept the check from, by how that robots.txt answered. */
function robotsKeptOut(robots: StartRobots | undefined, unfinished: boolean): NothingRead {
  const again = unfinished ? 'continue' : 'checkAgain';
  if (robots?.state === 'unreachable' && robots.statusCode !== null) {
    const answered =
      robots.statusCode === 429
        ? 'answered “too many requests” (a 429)'
        : `answered with a server error (a ${robots.statusCode})`;
    return {
      why: `The site’s rules file for search engines (its “robots.txt”) couldn’t be read: it ${answered}. Until it can be read, search engines and this check stay out, so nothing was read. Try again later.`,
      action: again,
    };
  }
  if (robots?.state === 'unreachable' || robots?.state === 'unanswered') {
    return {
      why: 'The site didn’t answer when the check asked for its rules file for search engines (its “robots.txt”), so nothing was read. Check the address and that the site is up, then try again later.',
      action: again,
    };
  }
  if (robots?.state === 'read') {
    return {
      why: 'The site’s rules file for search engines (its “robots.txt”) tells checkers to stay out, so nothing was read. Ask the site’s owner, or check it in Advanced with the rules file ignored if the site is yours.',
      action: 'robotsInAdvanced',
    };
  }
  // Not known yet (or no file kept): true whichever way the file answered.
  return {
    why: 'The site’s rules file for search engines (its “robots.txt”) kept the check out, so nothing was read.',
    action: again,
  };
}
