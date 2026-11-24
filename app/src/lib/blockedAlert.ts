// What the browser fallback did about a host that blocks the crawl, read from the `blocked` alert's facts (its
// `tried`, docs/v2-sidecar-api.md, Alerts) and said in each mode's words. A blocked crawl that only says "blocked"
// leaves its reader guessing whether a browser was tried, whether they were asked to complete a check, and how that
// ended. A crawl file from before 2.2 has no `tried`: nothing is said of the browser then. Pure.
import type { AlertData, AlertEvent } from '../api/types';

export const BLOCK_TRIED = [
  'off',
  'needsPerson',
  'notCompleted',
  'windowClosed',
  'refusedAfterCheck',
  'wentElsewhere',
  'noDisplay',
  'noBrowser',
  'ownBrowserOff',
  'refused',
  'notTried',
] as const;
export type BlockTried = (typeof BLOCK_TRIED)[number];

/** What to do next about it. The screen turns it into a link or a button. */
export type BlockedNext = 'turnOnFallback' | 'again' | 'from' | 'otherWays';

export interface BlockedWords {
  tried: BlockTried;
  /** What the browser did, as a sentence to follow "example.com answered with a Cloudflare challenge." */
  what: string;
  /** What to do about it. */
  advice: string;
  next: BlockedNext;
  /** For `from`: where the page went once its check had passed. */
  url?: string;
}

export function triedOf(data: AlertData | null | undefined): BlockTried | null {
  return BLOCK_TRIED.find((tried) => tried === data?.tried) ?? null;
}

/** The facts of the latest `blocked` alert, which says what the browser did in the crawl's last run. Null when none. */
export function latestBlocked(alerts: readonly AlertEvent[] | undefined): AlertData | null {
  let latest: AlertEvent | null = null;
  for (const alert of alerts ?? []) {
    if (alert.code !== 'blocked') continue;
    if (!latest || Date.parse(alert.at) > Date.parse(latest.at) || (alert.id ?? 0) > (latest.id ?? 0)) latest = alert;
  }
  return latest?.data ?? null;
}

const OWNER =
  'Or ask the site’s owner to let the crawler in: a firewall rule that allows its user agent or this computer’s IP address.';
const OWNER_SIMPLE = 'Or ask whoever runs the site to let the check through its security.';

/** "A Chrome window", "An Edge window". */
export function aWindow(browser: string | null | undefined): string {
  const name = browser ?? 'browser';
  return `${/^[aeiou]/i.test(name) ? 'An' : 'A'} ${name} window`;
}

/** Advanced's words. Null when the alert doesn't say what was tried, or no browser was. */
export function blockedWords(data: AlertData | null | undefined): BlockedWords | null {
  const tried = triedOf(data);
  const browser = data?.browser ?? 'browser';
  switch (tried) {
    case 'off':
      return {
        tried,
        what: 'Browser fallback is off for this crawl, so no browser was tried.',
        advice: `Turn on Browser fallback and crawl again: the site is then read in a real browser, which most checks let through. ${OWNER}`,
        next: 'turnOnFallback',
      };
    case 'needsPerson':
      return {
        tried,
        what: 'Its check needs a person, and this crawl wasn’t set to ask one.',
        advice: `Crawl again from the app: a browser window opens on the page, you complete the check, and the crawl carries on. ${OWNER}`,
        next: 'again',
      };
    case 'notCompleted':
      return {
        tried,
        what: `${aWindow(browser)} was opened for you to complete its check, and the check wasn’t completed in the time it was given. The site’s pages are recorded as blocked.`,
        advice: `Crawl again to be asked once more: the window comes to the front, and the crawl carries on when the check is done. ${OWNER}`,
        next: 'again',
      };
    case 'windowClosed':
      return {
        tried,
        what: `${aWindow(browser)} was opened for you to complete its check, and it was closed before the check was completed. The site’s pages are recorded as blocked.`,
        advice: `Crawl again to be asked once more, and leave the window open until the site’s page shows. ${OWNER}`,
        next: 'again',
      };
    case 'refusedAfterCheck':
      return {
        tried,
        what: `The check was completed in the ${browser} window, and the site then refused the page all the same.`,
        advice: `Stop the crawl and continue it more slowly. ${OWNER}`,
        next: 'otherWays',
      };
    case 'wentElsewhere':
      return {
        tried,
        what: `The check passed in ${browser}, and the page then went to ${data?.url ?? 'another address'}, which isn’t this address’s own answer.`,
        advice: 'Crawl from there instead.',
        next: data?.url ? 'from' : 'again',
        ...(data?.url ? { url: data.url } : {}),
      };
    case 'noDisplay':
      return {
        tried,
        what: 'Getting past it needs a browser window, and this computer has no screen to show one on.',
        advice: `Crawl from a desktop computer. ${OWNER}`,
        next: 'otherWays',
      };
    case 'noBrowser':
      return {
        tried,
        what: 'Getting past it needs Chrome or Edge, and none could be started on this computer.',
        advice: `Install Chrome or Edge and crawl again. ${OWNER}`,
        next: 'again',
      };
    case 'ownBrowserOff':
      return {
        tried,
        what: 'The browser the crawler downloads was blocked too, and this crawl was told not to use the computer’s own Chrome or Edge, which most checks let through.',
        advice: `Stop the crawl and continue it more slowly. ${OWNER}`,
        next: 'otherWays',
      };
    case 'refused':
      return {
        tried,
        what: 'It was tried in a real browser too, and the site refused that as well.',
        advice: `Stop the crawl and continue it more slowly, add the headers the site expects, or crawl as another user agent. ${OWNER}`,
        next: 'otherWays',
      };
    default:
      return null;
  }
}

/** Simple's words: no "crawl", no "user agent", and what the person did or was asked to do said to them. */
export function blockedPlainWords(data: AlertData | null | undefined): BlockedWords | null {
  const tried = triedOf(data);
  const browser = data?.browser ?? 'browser';
  switch (tried) {
    case 'off':
      return {
        tried,
        what: 'Reading a blocked site in a browser is turned off for this check.',
        advice: `Turn on “If a site blocks the check” and check again. ${OWNER_SIMPLE}`,
        next: 'turnOnFallback',
      };
    case 'needsPerson':
      return {
        tried,
        what: 'The site wants to see that you’re a person, and this check wasn’t set to ask you.',
        advice: `Check again: a browser window opens for you to do what the site asks. ${OWNER_SIMPLE}`,
        next: 'again',
      };
    case 'notCompleted':
      return {
        tried,
        what: `The site wanted to see that you’re a person. ${aWindow(browser)} was opened for you, and what it asked wasn’t done in the time it was given.`,
        advice: `Check again to be asked once more: the window comes to the front, and reading carries on by itself once it’s done. ${OWNER_SIMPLE}`,
        next: 'again',
      };
    case 'windowClosed':
      return {
        tried,
        what: `The site wanted to see that you’re a person. ${aWindow(browser)} was opened for you, and it was closed before what the site asked was done.`,
        advice: `Check again to be asked once more, and leave the window open until the site’s page shows. ${OWNER_SIMPLE}`,
        next: 'again',
      };
    case 'refusedAfterCheck':
      return {
        tried,
        what: `You did what the site asked in the ${browser} window, and it still turned the check away.`,
        advice: OWNER_SIMPLE.replace(/^Or ask/, 'Ask'),
        next: 'otherWays',
      };
    case 'wentElsewhere':
      return {
        tried,
        what: `Once the site had let the browser in, the address went on to ${data?.url ?? 'another address'}.`,
        advice: 'Check that address instead.',
        next: data?.url ? 'from' : 'again',
        ...(data?.url ? { url: data.url } : {}),
      };
    case 'noDisplay':
      return {
        tried,
        what: 'Getting past it needs a browser window, and this computer has no screen to show one on.',
        advice: OWNER_SIMPLE.replace(/^Or ask/, 'Ask'),
        next: 'otherWays',
      };
    case 'noBrowser':
      return {
        tried,
        what: 'Getting past it needs Chrome or Edge, and neither could be started on this computer.',
        advice: `Install Chrome or Edge and check again. ${OWNER_SIMPLE}`,
        next: 'again',
      };
    case 'ownBrowserOff':
    case 'refused':
      return {
        tried,
        what: 'It was tried in a real browser too, and the site turned that away as well.',
        advice: OWNER_SIMPLE.replace(/^Or ask/, 'Ask'),
        next: 'otherWays',
      };
    default:
      return null;
  }
}
