// The crawl's alerts in plain words, each with what to do (docs/v2-sidecar-api.md, Alerts; docs/v2-ui-ia.md, Overview
// (live)). The words and the action are keyed on the alert's stable code; the engine's own message is shown only for a
// code this app doesn't know. Pure.
import type { AlertEvent, AlertLevel } from '../../api/types';
import { formatCount, formatCountOf, formatDuration } from '../../lib/format';

/** What an alert offers to do about it. The screen turns it into a link or a button. */
export type AlertAction =
  | { kind: 'crawlSettings'; label: string }
  | { kind: 'newCrawl'; label: string; url: string }
  | { kind: 'sitemaps'; label: string }
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
  /** What to do about it. Null for one that needs nothing done. */
  advice: string | null;
  action: AlertAction | null;
}

export const alertKey = (alert: Pick<AlertEvent, 'id' | 'at' | 'code'>): string =>
  `${alert.id ?? alert.code}@${alert.at}`;

const SETTINGS: AlertAction = { kind: 'crawlSettings', label: 'Open the crawl’s settings' };
const SITEMAPS: AlertAction = { kind: 'sitemaps', label: 'Open Sitemaps' };

const BLOCKED_ADVICE =
  'Stop the crawl and continue it more slowly, add the headers the site expects, or crawl as another user agent.';

function words(alert: AlertEvent): Pick<AlertEntry, 'title' | 'detail' | 'advice' | 'action'> {
  const data = alert.data ?? {};
  const where = data.host ?? data.origin ?? 'The site';
  switch (alert.code) {
    case 'blocked':
      return {
        title: 'The site is blocking the crawl',
        detail: [
          `${where} answered with ${data.reason ? withArticle(data.reason) : 'a block page'}.`,
          data.count !== undefined && data.count > 1 ? `${formatCountOf(data.count, 'response')} blocked so far.` : '',
        ]
          .filter(Boolean)
          .join(' '),
        advice: BLOCKED_ADVICE,
        action: SETTINGS,
      };
    case 'mostly-forbidden':
      return {
        title: 'Most of the site’s first URLs answered 403 Forbidden',
        detail:
          data.count !== undefined && data.sampled !== undefined
            ? `${formatCount(data.count)} of the first ${formatCountOf(data.sampled, 'URL')} on ${where}.`
            : null,
        advice: BLOCKED_ADVICE,
        action: SETTINGS,
      };
    case 'start-redirects-away':
      return {
        title: 'The start URL redirects to another site',
        detail: data.url ? `It leads to ${data.url}, which this crawl treats as another site.` : null,
        advice: 'Crawl from there instead.',
        action: data.url ? { kind: 'newCrawl', label: 'Crawl from there', url: data.url } : null,
      };
    case 'robots-unreachable':
      return {
        title: 'robots.txt can’t be read',
        detail: [
          `${where}${data.reason ? ` answered ${data.reason}` : ' didn’t give its robots.txt'}.`,
          data.durationMs !== undefined ? `It’s asked again in ${formatDuration(data.durationMs)}.` : '',
          'Its URLs aren’t fetched until it can be read; they get one more try after that.',
        ]
          .filter(Boolean)
          .join(' '),
        advice: 'If it stays unreadable, crawl again with robots.txt ignored.',
        action: SETTINGS,
      };
    case 'site-unreachable':
      return {
        title: 'The site isn’t answering',
        detail: [
          `${where}${data.reason ? `: ${data.reason}` : ' stopped answering'}.`,
          data.durationMs !== undefined ? `The crawl tries again every ${formatDuration(data.durationMs)}.` : '',
        ]
          .filter(Boolean)
          .join(' '),
        advice: 'Wait for it to come back, or stop the crawl and continue it later.',
        action: null,
      };
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
        action: SITEMAPS,
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

/** "a Cloudflare challenge", "an HTTP 403": the engine names the firewall or challenge without an article. */
function withArticle(reason: string): string {
  return /^(a|an|the)\s/i.test(reason) ? reason : `${/^[aeiou]/i.test(reason) ? 'an' : 'a'} ${reason}`;
}

/**
 * The alerts to show, newest first. An alert is its id and its time together, so one heard live and read back from the
 * file is shown once. `blocked` is raised once per host in each run, each time with the host's count so far: it shows
 * once per host, as the latest.
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
  const newestFirst = [...byKey.values()].sort((a, b) => b.at.localeCompare(a.at) || (b.id ?? 0) - (a.id ?? 0));
  const blockedHosts = new Set<string>();
  const entries: AlertEntry[] = [];
  for (const alert of newestFirst) {
    if (alert.code === 'blocked') {
      const host = alert.data?.host ?? '';
      if (blockedHosts.has(host)) continue;
      blockedHosts.add(host);
    }
    entries.push({ key: alertKey(alert), code: alert.code, level: alert.level, at: alert.at, ...words(alert) });
  }
  return entries;
}
