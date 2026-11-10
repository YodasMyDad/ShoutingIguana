// What holds part of a crawl up now, in plain words (docs/v2-sidecar-api.md, Progress: `waits`). Pure.
import type { CrawlWait, CrawlWaitReason } from '../../api/types';
import { formatCount, formatCountOf, formatDuration } from '../../lib/format';

export interface WaitEntry {
  key: string;
  /** "Waiting for robots.txt", "The site isn’t answering". */
  title: string;
  /** Which host, how many URLs, for how long. */
  detail: string;
}

const urls = (count: number | undefined) => (count === undefined ? 'URLs' : formatCountOf(count, 'URL'));
const time = (ms: number | undefined) => (ms === undefined ? 'a while' : formatDuration(ms));

function words(wait: CrawlWait): Omit<WaitEntry, 'key'> {
  const host = wait.host ?? 'the site';
  switch (wait.reason) {
    case 'robotsTxt':
      return {
        title: 'Waiting for robots.txt',
        detail: `${urls(wait.urls)} on ${host} wait for its robots.txt, which is read before anything else there.`,
      };
    case 'otherSitesRobotsTxt':
      return {
        title: 'Waiting for other sites’ robots.txt',
        detail: `${urls(wait.urls)} on other sites wait to be checked. Nothing of this site’s is held up.`,
      };
    case 'crawlDelay':
      return {
        title: 'Keeping to the site’s Crawl-delay',
        detail: `${host} asks for ${time(wait.durationMs)} between requests in its robots.txt.`,
      };
    case 'backoff':
      return {
        title: 'Slowing down',
        detail: `${host} answered with errors or asked for less, so requests to it are ${time(wait.durationMs)} further apart for now.`,
      };
    case 'heldBack':
      return {
        title: 'Holding requests back',
        detail: `${host} asked to wait, or stopped answering: its requests go again in ${time(wait.durationMs)}.`,
      };
    case 'siteDown':
      return {
        title: 'The site isn’t answering',
        detail: `${host} hasn’t answered for ${time(wait.durationMs)}. The crawl stops after 10 minutes of it, and can be continued later.`,
      };
    case 'robotsTxtCoolDown':
      return {
        title: 'Waiting to ask for robots.txt again',
        detail:
          wait.urls !== undefined && wait.urls > 0
            ? `It couldn’t be read, so ${urls(wait.urls)} wait. It’s asked again in ${time(wait.durationMs)}.`
            : `It couldn’t be read. It’s asked again in ${time(wait.durationMs)}.`,
      };
  }
}

const MORE_NOUNS: Record<CrawlWaitReason, readonly [string, string]> = {
  robotsTxt: ['host', 'hosts'],
  otherSitesRobotsTxt: ['wait', 'waits'],
  crawlDelay: ['host', 'hosts'],
  backoff: ['host', 'hosts'],
  heldBack: ['host', 'hosts'],
  siteDown: ['origin', 'origins'],
  robotsTxtCoolDown: ['wait', 'waits'],
};

/** The waits to list, as the engine ordered them (the longest first), then "and 212 more hosts" where it says so. */
export function waitEntries(
  waits: readonly CrawlWait[],
  moreWaits: Readonly<Partial<Record<CrawlWaitReason, number>>> = {},
): { entries: WaitEntry[]; more: string | null } {
  const entries = waits.map((wait, index) => ({ key: `${wait.reason}:${wait.host ?? ''}:${index}`, ...words(wait) }));
  const more = (Object.entries(moreWaits) as [CrawlWaitReason, number][])
    .filter(([, count]) => count > 0)
    .map(([reason, count]) => `${formatCount(count)} more ${MORE_NOUNS[reason][count === 1 ? 0 : 1]}`);
  return { entries, more: more.length > 0 ? `and ${more.join(', ')}` : null };
}
