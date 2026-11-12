// The crawl's alerts in plain words, each with what to do (docs/v2-sidecar-api.md, Alerts; docs/v2-ui-ia.md, Overview
// (live)). The words and the action are keyed on the alert's stable code; the engine's own message is shown only for a
// code this app doesn't know. Pure.
import type { AlertEvent, AlertLevel } from '../../api/types';
import type { SettingFocus } from '../../routing/search';
import { failureOf } from '../../lib/failure';
import { formatCount, formatCountOf, formatDuration } from '../../lib/format';

/** What an alert offers to do about it. The screen turns it into a link or a button. */
export type AlertAction =
  /** `focus`: the setting the crawl's settings open on (Continue more slowly, Add headers). */
  | { kind: 'crawlSettings'; label: string; focus?: SettingFocus }
  /**
   * `url`: the start URL to crawl from. `again`: with this crawl's settings (and its start URL, when there's no `url`).
   * `focus`: the setting New crawl opens on.
   */
  | { kind: 'newCrawl'; label: string; url?: string; again?: boolean; focus?: SettingFocus }
  /** `url`: the sitemap the alert is about, by its address: the link lands on that sitemap. */
  | { kind: 'sitemaps'; label: string; url?: string }
  | { kind: 'continue'; label: string };

export interface AlertEntry {
  /** The alert's id and time together, which is what tells one alert from another. */
  key: string;
  code: string;
  level: AlertLevel;
  at: string;
  /** What happened, in a few words. */
  title: string;
  /** The facts: which host, how many, why. Null when the title says it all. */
  detail: string | null;
  /**
   * How a failure in the facts was recorded by the crawl, when that says more than the plain words (lib/failure): for
   * the facts' tooltip and accessible description, never their sentence.
   */
  recorded?: string;
  /**
   * What it came to in the whole crawl, for an alert whose count goes on rising while the crawl runs (a blocked
   * host's responses): "37 responses blocked.". It's the crawl file's count as it was when the alerts were read, and
   * nothing reads them again while the crawl runs, so it's said only of a crawl that isn't running: beside a live
   * crawl it stood still, as "so far", while the crawl went on being blocked. Null when there's none.
   */
  tally: string | null;
  /** What to do about it. Null for one that needs nothing done. */
  advice: string | null;
  action: AlertAction | null;
  /** More ways to do something about it, after `action`: a blocked crawl's three. */
  more?: AlertAction[];
}

export const alertKey = (alert: Pick<AlertEvent, 'id' | 'at' | 'code'>): string =>
  `${alert.id ?? alert.code}@${alert.at}`;

const SITEMAPS: AlertAction = { kind: 'sitemaps', label: 'Open Sitemaps' };

/** A blocked crawl's three ways on: the crawl's settings on its speed or its headers, or a new crawl as another agent. */
const SLOWER: AlertAction = { kind: 'crawlSettings', label: 'Continue more slowly', focus: 'speed' };
const BLOCKED_MORE: AlertAction[] = [
  { kind: 'crawlSettings', label: 'Add headers', focus: 'headers' },
  { kind: 'newCrawl', label: 'Crawl as another user agent', again: true, focus: 'userAgent' },
];

const BLOCKED_ADVICE =
  'Stop the crawl and continue it more slowly, add the headers the site expects, or crawl as another user agent.';

function words(
  alert: AlertEvent,
): Pick<AlertEntry, 'title' | 'detail' | 'advice' | 'action' | 'more'> & { tally?: string; recorded?: string } {
  const data = alert.data ?? {};
  const where = data.host ?? data.origin ?? 'The site';
  switch (alert.code) {
    case 'blocked':
      return {
        title: 'The site is blocking the crawl',
        detail: `${where} answered with ${data.reason ? withArticle(data.reason) : 'a block page'}.`,
        ...(data.count !== undefined && data.count > 1
          ? { tally: `${formatCountOf(data.count, 'response')} blocked.` }
          : {}),
        advice: BLOCKED_ADVICE,
        action: SLOWER,
        more: BLOCKED_MORE,
      };
    case 'mostly-forbidden':
      return {
        title: 'Most of the site’s first URLs answered 403 Forbidden',
        detail:
          data.count !== undefined && data.sampled !== undefined
            ? `${formatCount(data.count)} of the first ${formatCountOf(data.sampled, 'URL')} on ${where}.`
            : null,
        advice: BLOCKED_ADVICE,
        action: SLOWER,
        more: BLOCKED_MORE,
      };
    case 'start-redirects-away':
      return {
        title: 'The start URL redirects to another site',
        detail: data.url ? `It leads to ${data.url}, which this crawl treats as another site.` : null,
        advice: 'Crawl from there instead.',
        action: data.url ? { kind: 'newCrawl', label: 'Crawl from there', url: data.url } : null,
      };
    case 'robots-unreachable': {
      // A status is an answer; anything else is how the request failed, said by its kind.
      const failure = failureOf(data.reason);
      return {
        title: 'robots.txt can’t be read',
        detail: [
          `${where}${
            failure === null
              ? ' didn’t give its robots.txt'
              : failure.kind === 'status'
                ? ` answered ${failure.words}`
                : ` didn’t give its robots.txt: ${failure.words}`
          }.`,
          data.durationMs !== undefined ? `It’s asked again in ${formatDuration(data.durationMs)}.` : '',
          'Its URLs aren’t fetched until it can be read; they get one more try after that.',
        ]
          .filter(Boolean)
          .join(' '),
        advice: 'If it stays unreadable, crawl again with robots.txt ignored.',
        action: { kind: 'newCrawl', label: 'Crawl again with robots.txt ignored', again: true, focus: 'robots' },
        ...(failure?.recorded ? { recorded: failure.recorded } : {}),
      };
    }
    case 'site-unreachable': {
      const failure = failureOf(data.reason);
      return {
        title: 'The site isn’t answering',
        detail: [
          `${where}${failure ? `: ${failure.words}` : ' stopped answering'}.`,
          data.durationMs !== undefined ? `The crawl tries again every ${formatDuration(data.durationMs)}.` : '',
        ]
          .filter(Boolean)
          .join(' '),
        advice: 'Wait for it to come back, or stop the crawl and continue it later.',
        action: null,
        ...(failure?.recorded ? { recorded: failure.recorded } : {}),
      };
    }
    case 'site-down':
      return {
        title: 'The site stopped answering, so the crawl stopped',
        detail:
          data.durationMs !== undefined
            ? `${where} hadn’t answered for ${formatDuration(data.durationMs)}.`
            : `${where} hadn’t answered for too long.`,
        advice: 'Continue the crawl once the site is back: it carries on from where it stopped.',
        action: { kind: 'continue', label: 'Continue' },
      };
    case 'sitemaps':
      return {
        title: 'Sitemaps read',
        detail:
          data.count !== undefined && data.urlCount !== undefined
            ? `${formatCountOf(data.count, 'sitemap')} ${data.count === 1 ? 'lists' : 'list'} ${formatCountOf(data.urlCount, 'URL')}.`
            : null,
        advice: null,
        action: SITEMAPS,
      };
    case 'sitemaps-not-read':
      return {
        title: 'A sitemap was left out',
        detail: data.url ? `${data.url}${data.reason ? `: ${data.reason}` : ''}` : (data.reason ?? null),
        advice: 'The checks that compare pages with the sitemaps may be skipped.',
        action: data.url ? { kind: 'sitemaps', label: 'Open this sitemap', url: data.url } : SITEMAPS,
      };
    case 'sitemaps-failed':
      return {
        title: 'The sitemaps couldn’t be read',
        detail: data.reason ?? null,
        advice: 'The checks that compare pages with the sitemaps are skipped.',
        action: SITEMAPS,
      };
    default:
      // A code from a newer engine: its own words, which it writes for people.
      return { title: 'A note from the crawl', detail: alert.message, advice: null, action: null };
  }
}

/**
 * When an alert was raised, as an instant. Never its `at` as text: the sidecar writes a time without its trailing
 * zeros ("…:57.5Z", "…:57.52Z", "…:57Z"), so within a second the text's order isn't time's. A time that can't be read
 * sorts as the oldest.
 */
function raisedAt(alert: Pick<AlertEvent, 'at'>): number {
  const at = Date.parse(alert.at);
  return Number.isNaN(at) ? 0 : at;
}

/** "a Cloudflare challenge", "an HTTP 403": the engine names the firewall or challenge without an article. */
function withArticle(reason: string): string {
  return /^(a|an|the)\s/i.test(reason) ? reason : `${/^[aeiou]/i.test(reason) ? 'an' : 'a'} ${reason}`;
}

/**
 * The alerts to show, newest first. An alert is its id and its time together, so one heard live and read back from the
 * file is shown once. `blocked` is raised once per host in each run, each time with the host's count so far: it shows
 * once per host, as the latest, and its count is the entry's `tally`, for a crawl that isn't running.
 */
export function alertEntries(...sources: readonly (readonly AlertEvent[] | undefined)[]): AlertEntry[] {
  const byKey = new Map<string, AlertEvent>();
  for (const source of sources) {
    for (const alert of source ?? []) {
      const key = alertKey(alert);
      const known = byKey.get(key);
      // The file's copy of a blocked alert has the count so far; the live one says 1.
      if (!known || (alert.data?.count ?? 0) > (known.data?.count ?? 0)) byKey.set(key, alert);
    }
  }
  const newestFirst = [...byKey.values()].sort((a, b) => raisedAt(b) - raisedAt(a) || (b.id ?? 0) - (a.id ?? 0));
  const blockedHosts = new Set<string>();
  const entries: AlertEntry[] = [];
  for (const alert of newestFirst) {
    if (alert.code === 'blocked') {
      const host = alert.data?.host ?? '';
      if (blockedHosts.has(host)) continue;
      blockedHosts.add(host);
    }
    entries.push({
      key: alertKey(alert),
      code: alert.code,
      level: alert.level,
      at: alert.at,
      tally: null,
      ...words(alert),
    });
  }
  return entries;
}
