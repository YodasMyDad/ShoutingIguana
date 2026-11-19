// Checking a site in Simple's words (docs/v2-ui-simple.md, "While it checks the site"): what's in the way, said
// plainly with what to do about it; how long is left, as a floor; and the analysis's phases. Pure.
import type { AlertEvent, AlertLevel, CrawlStatus, CrawlWait, HistoryPoint, ProgressEvent } from '../../api/types';
import type { SettingFocus } from '../../routing/search';
import { formatCount, formatCountOf } from '../../lib/format';
import { simpleReason } from '../../shopify/refusals';

/** What a plain alert offers. Each lands where Advanced's alert does; one to an Advanced-only screen says so. */
export type SimpleAlertAction =
  /** The crawl's settings (Advanced only), on one setting. */
  | { kind: 'crawlSettings'; label: string; focus: SettingFocus }
  /** New crawl, from another address: in Simple it's Check a website. */
  | { kind: 'newCrawl'; label: string; url: string }
  /** Sitemaps (Advanced only), on the sitemap the alert is about. */
  | { kind: 'sitemaps'; label: string; url?: string }
  /** Text to copy: what to tell whoever runs the site. */
  | { kind: 'copy'; label: string; text: string }
  /** The top bar's Stop, here too. */
  | { kind: 'stop'; label: string }
  /** A Shopify signature, added in Simple: the check stops, takes it, and continues (lane shopify). */
  | { kind: 'shopifySign'; label: string; host: string }
  | { kind: 'continue'; label: string };

export interface SimpleAlert {
  /** One of each kind is shown: the slowdown, the block, robots.txt, the site down. */
  key: string;
  level: AlertLevel;
  text: string;
  actions: SimpleAlertAction[];
  /** Shopify's steps to a signature follow the text, its button names quoted (lane shopify). */
  shopifySteps?: boolean;
}

/** What gives a site's alert its kind: a wait and an alert of one kind say the same thing, so it's said once. */
type Kind =
  'blocked' | 'away' | 'robots' | 'down' | 'stopped' | 'sitemaps' | 'slow' | 'section' | 'delay' | 'shopify' | 'note';

const SLOW = 'The site asked the check to slow down, so it has. It will take longer.';
const SECTION_SLOW =
  'The site only lets some of its pages be read slowly, so the check reads those one at a time. The rest isn’t held up.';
const DELAY = 'The site asks visitors like this one to go slowly, so the check waits between pages.';
const ROBOTS =
  'The site’s rules file for search engines isn’t answering, so the check can’t tell where it may go. It’s waiting, and will try again.';
const DOWN = 'The site has stopped answering. The check is waiting, and will carry on by itself when it’s back.';
const STOP: SimpleAlertAction = { kind: 'stop', label: 'Stop' };

/** "example.org", from an address: the host, without "www.". */
export function hostOf(url: string): string {
  try {
    return new URL(url).hostname.replace(/^www\./i, '');
  } catch {
    return url;
  }
}

/** What to send whoever runs a site that turns the check away: what happened, and what would let it in. */
export function blockedMessage(site: string, pages: number | null): string {
  return [
    `I checked ${site} with Shouting Iguana, a site checker that reads pages the way a search engine does.`,
    `The site’s security turned it away${pages === null ? '' : ` after ${formatCountOf(pages, 'page')}`}, taking it for a robot.`,
    'Could you let it through the firewall or rate limit while I check the site, or tell me what it needs to send to be let in?',
  ].join(' ');
}

/** An alert as one thing, across reads: its number and its time (E4). */
export function alertKey(alert: Pick<AlertEvent, 'id' | 'code' | 'at'>): string {
  return `${alert.id ?? alert.code}@${alert.at}`;
}

/**
 * The pages read at a moment, from the crawl's history: its last point at or before it. Null when the history doesn't
 * reach that moment (it ends before it, or starts after it), so it can't say.
 */
export function pagesWhenRaised(points: readonly HistoryPoint[] | undefined, at: string): number | null {
  const moment = instant(at);
  if (!points?.length || Number.isNaN(moment)) return null;
  const last = points.at(-1);
  if (!last || instant(last.at) < moment) return null;
  const before = points.filter((point) => instant(point.at) <= moment).at(-1);
  return before ? before.htmlPages : null;
}

function alertWords(
  alert: AlertEvent,
  { site, pages }: { site: string; pages: number | null },
): { kind: Kind; level: AlertLevel; text: string; actions: SimpleAlertAction[]; shopifySteps?: boolean } | null {
  switch (alert.code) {
    case 'blocked':
    case 'mostly-forbidden':
      return {
        kind: 'blocked',
        level: 'error',
        text: `The site is turning the check away: its security takes it for a robot.${
          pages === null ? '' : ` ${formatCountOf(pages, 'page')} ${pages === 1 ? 'was' : 'were'} read first.`
        }`,
        actions: [
          { kind: 'crawlSettings', label: 'Continue more gently (Polite), in Advanced', focus: 'speed' },
          { kind: 'copy', label: 'What to tell whoever runs the site', text: blockedMessage(site, pages) },
          { kind: 'crawlSettings', label: 'More ways in Advanced', focus: 'headers' },
        ],
      };
    case 'shopify-unsigned': {
      const count = alert.data?.count;
      const reason = alert.data?.reason;
      return {
        kind: 'shopify',
        level: 'warning',
        text: [
          `This site runs on Shopify, and Shopify is turning some pages away because the check isn’t signed${
            count === undefined ? '' : `: ${formatCountOf(count, 'page')} so far`
          }.`,
          reason ? simpleReason(reason) : '',
          'They’ll be marked not checked, not broken.',
        ]
          .filter(Boolean)
          .join(' '),
        actions: [{ kind: 'shopifySign', label: 'Add a Shopify signature', host: alert.data?.host ?? site }],
        shopifySteps: true,
      };
    }
    case 'shopify-signature-refused':
      return {
        kind: 'shopify',
        level: 'warning',
        text: `Shopify turned the check away even with the signature. ${simpleReason(alert.data?.reason ?? '')} They’ll be marked not checked, not broken.`,
        actions: [{ kind: 'shopifySign', label: 'Change the signature', host: alert.data?.host ?? site }],
        shopifySteps: true,
      };
    case 'start-redirects-away': {
      const url = alert.data?.url;
      if (!url) return { kind: 'away', level: 'warning', text: 'This address forwards to another site.', actions: [] };
      const host = hostOf(url);
      return {
        kind: 'away',
        level: 'warning',
        text: `This address forwards to another site, ${host}.`,
        actions: [{ kind: 'newCrawl', label: `Check ${host} instead`, url }],
      };
    }
    // The engine's notice that a section is limited: the same thing as its wait, so it's said once, in plain words.
    case 'section-rate-limited':
      return { kind: 'section', level: 'info', text: SECTION_SLOW, actions: [] };
    case 'robots-unreachable':
      return { kind: 'robots', level: 'warning', text: ROBOTS, actions: [STOP] };
    case 'site-unreachable':
      return { kind: 'down', level: 'warning', text: DOWN, actions: [STOP] };
    case 'site-down':
      return {
        kind: 'stopped',
        level: 'error',
        text: 'The site stopped answering for too long, so the check stopped. It can carry on from where it stopped once the site is back.',
        actions: [{ kind: 'continue', label: 'Continue checking' }],
      };
    case 'sitemaps-failed':
    case 'sitemaps-not-read': {
      const url = alert.code === 'sitemaps-not-read' ? alert.data?.url : undefined;
      return {
        kind: 'sitemaps',
        level: 'info',
        text: 'The site’s sitemap, its list of pages for search engines, couldn’t be read. The checks that compare pages with it may not run.',
        actions: [{ kind: 'sitemaps', label: 'See it in Advanced', ...(url ? { url } : {}) }],
      };
    }
    case 'sitemaps':
      // Good news, not something in the way.
      return null;
    default:
      // A code from a newer engine: its own words, which it writes for people.
      return { kind: 'note', level: alert.level, text: alert.message, actions: [] };
  }
}

function waitWords(wait: CrawlWait): { kind: Kind; text: string; actions: SimpleAlertAction[] } | null {
  switch (wait.reason) {
    case 'backoff':
    case 'heldBack':
      return { kind: 'slow', text: SLOW, actions: [] };
    // Its own kind: the site slowing the whole check down is another thing, said beside it.
    case 'sectionLimited':
      return { kind: 'section', text: SECTION_SLOW, actions: [] };
    case 'crawlDelay':
      return { kind: 'delay', text: DELAY, actions: [] };
    case 'siteDown':
      return { kind: 'down', text: DOWN, actions: [STOP] };
    case 'robotsTxtCoolDown':
      return { kind: 'robots', text: ROBOTS, actions: [STOP] };
    // Reading robots.txt first is how every check starts, and other sites' rules hold nothing of this one's up.
    case 'robotsTxt':
    case 'otherSitesRobotsTxt':
      return null;
  }
}

/** An alert's time as an instant, never as text (a time without its trailing zeros sorts wrongly as text). */
const instant = (iso: string) => {
  const at = Date.parse(iso);
  return Number.isNaN(at) ? 0 : at;
};

/**
 * What's in the way now, in plain words: the alerts of this run (an earlier run's block or outage isn't in the way of
 * this one), newest first, then what holds the check up at this moment (the progress's waits). One of each kind: a
 * site that stopped answering is one line, whether an alert or a wait said so.
 */
/** The answers from the site itself a check has before it says the site is slow: a few seconds of a Polite check. */
export const ANSWERS_TO_JUDGE_BY = 10;

/** The site's own URLs with an outcome so far: the progress's, less its probes of the site's other versions. */
export function siteAnswers(progress: {
  fetched: number | null;
  siteVersions?: Pick<NonNullable<ProgressEvent['siteVersions']>, 'fetched'> | null | undefined;
}): number {
  return (progress.fetched ?? 0) - (progress.siteVersions?.fetched ?? 0);
}

export function inTheWay({
  alerts,
  waits,
  judged = true,
  runStartedAt,
  site,
  pages,
  pagesOf,
}: {
  alerts: readonly AlertEvent[];
  waits: readonly CrawlWait[];
  /**
   * Whether the site has answered enough to judge its pace by (ANSWERS_TO_JUDGE_BY): before then, a slowdown is said
   * of nothing. Its first requests failing (an http-only site's https version, a twin that isn't there) slow the
   * check for a moment, and that isn't the site answering slowly.
   */
  judged?: boolean;
  /** When this run began; alerts before it are an earlier run's. Null when it isn't known: then every alert counts. */
  runStartedAt: string | null;
  site: string;
  pages: number | null;
  /** The pages read when an alert was raised, which its words quote ("312 pages were read first"); `pages` without. */
  pagesOf?: ((alert: AlertEvent) => number | null) | undefined;
}): SimpleAlert[] {
  const from = runStartedAt === null ? null : instant(runStartedAt);
  const seen = new Set<Kind>();
  const entries: SimpleAlert[] = [];
  const newestFirst = [...alerts]
    .filter((alert) => from === null || instant(alert.at) >= from)
    .sort((a, b) => instant(b.at) - instant(a.at) || (b.id ?? 0) - (a.id ?? 0));
  for (const alert of newestFirst) {
    const words = alertWords(alert, { site, pages: pagesOf?.(alert) ?? pages });
    if (!words || (words.kind !== 'note' && seen.has(words.kind))) continue;
    seen.add(words.kind);
    entries.push({ key: `${words.kind}:${alert.id ?? alert.code}@${alert.at}`, ...words });
  }
  for (const wait of waits) {
    const words = waitWords(wait);
    if (!words || seen.has(words.kind) || (words.kind === 'slow' && !judged)) continue;
    seen.add(words.kind);
    entries.push({ key: `wait:${words.kind}`, level: words.kind === 'down' ? 'warning' : 'info', ...words });
  }
  return entries;
}

// -------------------------------------------------------------------------------------------------- Time left

/**
 * The addresses waiting in the sections the site limits (the progress's `sectionLimited` waits), read one at a time:
 * the engine counts them in flight, so they're in what's still to check. Sections beyond the waits listed hold one each
 * at least.
 */
export function sectionHeld(
  waits: readonly CrawlWait[],
  moreWaits: Readonly<Partial<Record<CrawlWait['reason'], number>>>,
): number {
  const listed = waits
    .filter((wait) => wait.reason === 'sectionLimited')
    .reduce((sum, wait) => sum + Math.max(1, wait.urls ?? 1), 0);
  return listed + (moreWaits.sectionLimited ?? 0);
}

/** "12 minutes", "1 hour and 5 minutes", "2 hours". */
export function minutesInWords(totalMinutes: number): string {
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  const h = hours === 0 ? '' : `${formatCount(hours)} ${hours === 1 ? 'hour' : 'hours'}`;
  const m = minutes === 0 ? '' : `${formatCount(minutes)} ${minutes === 1 ? 'minute' : 'minutes'}`;
  return h && m ? `${h} and ${m}` : h || m;
}

export interface TimeLeftWords {
  /** In bold: "At least 12 minutes to go." */
  lead: string;
  /** After it: "It's still finding new pages, so this can grow. It's reading about 2 addresses a second." */
  rest: string;
}

/**
 * How long is left, said as a floor (docs/v2-ui-simple.md): what is still to check over the current pace. The total
 * isn't known until the end, as the check finds more as it reads, so there's no percentage and no "about".
 */
export function timeLeftWords({
  status,
  queued,
  urlsPerSecond,
  atLimit,
  limit = null,
  limitLeft = null,
  internalFound = null,
  heldInSections = 0,
}: {
  status: CrawlStatus | null;
  queued: number | null | undefined;
  urlsPerSecond: number | null | undefined;
  /** It has read as many addresses as its limit allows. */
  atLimit: boolean;
  /** Its URL limit, null without one. */
  limit?: number | null | undefined;
  /** How many more of the site's addresses its limit lets it read; null without a limit. */
  limitLeft?: number | null;
  /** The site's own addresses found so far (the limit counts only these); null when not known. */
  internalFound?: number | null | undefined;
  /** The addresses waiting in the sections the site limits (sectionHeld), among `queued`. */
  heldInSections?: number;
}): TimeLeftWords {
  if (status === 'paused') return { lead: 'Paused.', rest: 'The time left starts again when you resume the check.' };
  if (status === 'stopping') return { lead: 'Stopping.', rest: 'The pages on their way are finishing.' };
  // Only the sections the site limits are left: read one at a time at the site's pace, not the check's, so neither
  // "Nearly done" nor a time over the pace so far would be true (gallery 7's views 06 and 31).
  if (heldInSections > 0 && (atLimit || (queued ?? 0) <= heldInSections)) {
    return {
      lead: 'Reading the last pages slowly.',
      rest:
        heldInSections === 1
          ? '1 address is left. The site only lets it be read slowly, so this can take a while.'
          : `${formatCountOf(heldInSections, 'address', 'addresses')} are left. The site only lets them be read one at a time, so this can take a while.`,
    };
  }
  if (atLimit) return { lead: 'Nearly done.', rest: 'It has read as many addresses as its limit allows.' };
  const rate = urlsPerSecond ?? 0;
  if (queued === null || queued === undefined || rate <= 0) {
    return { lead: 'Working out how long it will take.', rest: 'It needs a few seconds of reading to tell.' };
  }
  const reading = `It’s reading about ${formatCount(Math.max(1, Math.round(rate)))} ${Math.round(rate) <= 1 ? 'address' : 'addresses'} a second.`;
  if (queued === 0) return { lead: 'Nearly done.', rest: `The last addresses are being read. ${reading}` };
  // Once the site has more addresses of its own than its limit (the limit counts only these, as Advanced's time left
  // does, overview/liveFigures.ts), the limit is what's left: no more can be found to read, so it's said as "about",
  // not as a floor. The queue alone can't say so: it holds other sites' addresses too.
  if (limit && limitLeft !== null && internalFound !== null && internalFound >= limit && limitLeft <= queued) {
    const left = Math.floor(limitLeft / rate / 60);
    const stops = `It stops at its limit of ${formatCount(limit)} addresses.`;
    return left < 1
      ? { lead: 'Under a minute to go.', rest: `${stops} ${reading}` }
      : { lead: `About ${minutesInWords(left)} to go.`, rest: `${stops} ${reading}` };
  }
  const minutes = Math.floor(queued / rate / 60);
  const grow = 'It’s still finding new pages, so this can grow.';
  return minutes < 1
    ? { lead: 'Under a minute for what it has found so far.', rest: `${grow} ${reading}` }
    : { lead: `At least ${minutesInWords(minutes)} to go.`, rest: `${grow} ${reading}` };
}

// ------------------------------------------------------------------------------------------- Working it out

const SIMPLE_PHASES: Record<string, { words: string; unit: readonly [string, string] | null }> = {
  reading: { words: 'Reading what it found', unit: ['address', 'addresses'] },
  pageRules: { words: 'Checking each page', unit: ['page', 'pages'] },
  siteRules: { words: 'Checking across the pages', unit: ['check', 'checks'] },
  urlSummary: { words: 'Summing up each address', unit: ['address', 'addresses'] },
  writing: { words: 'Saving the results', unit: null },
};

/** The analysis's phase in Simple's words: "Checking each page"; "Working out the results" for one this app doesn't know. */
export function simplePhaseWords(phase: string | null): string {
  return (phase ? SIMPLE_PHASES[phase]?.words : undefined) ?? 'Working out the results';
}

/** "1,204 of 4,521 pages", or null where the phase counts nothing worth saying. */
export function simplePhaseProgress(event: { phase: string; done: number; total: number } | null): string | null {
  const unit = event ? SIMPLE_PHASES[event.phase]?.unit : null;
  if (!event || !unit || event.total <= 0) return null;
  return `${formatCount(event.done)} of ${formatCount(event.total)} ${event.total === 1 ? unit[0] : unit[1]}`;
}
