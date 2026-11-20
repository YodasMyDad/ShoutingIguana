// "Where to fix it" as the sidecar's own answers make it (the UX audit of 2026-11-19, S-02 to S-05 and S-07): which
// checks have that card in Simple in place of "Where it is", and, for a check of dead addresses, the pages to edit, the
// links many pages share and the addresses nothing links to, worked out from the same reads the app makes
// (src/simple/fixPages.ts and WhereToFix.tsx), so a spec holds the screen to the engine and not to itself.
import { read } from './support';

/** What of a check decides its card: `/api/checks` gives each. */
export interface FixShapeCheck {
  id: string;
  grouping: string;
  references?: string;
}

/** Checks of addresses that are a dead end: each is mended at the links that lead to it. */
const DEAD_ENDS = ['internal-client-error', 'redirect-broken', 'redirect-loop'];

/** Checks of pages whose fault is a link on them. */
const LINK_FAULTS = [
  'broken-internal-links',
  'broken-external-links',
  'links-to-redirects',
  'internal-nofollow-links',
  'empty-anchor-text',
  'broken-fragment-links',
  'links-to-localhost',
  'slow-external-links',
];

/** A link on more pages than this is one row ("In the footer of 212 pages"), not a row on each page. */
const SHARED_PAGES = 10;

/** How many of a check's addresses the card lists before "Show more". */
export const FIX_LISTED = 25;

/**
 * Whether a check's card is "Where to fix it", and which way round: `addresses` for dead addresses (listed by the pages
 * that link to each), `pages` for pages that hold a faulty link. Null for every other check: it keeps "Where it is".
 */
export function fixShape(check: FixShapeCheck): 'addresses' | 'pages' | null {
  if (DEAD_ENDS.includes(check.id) && check.references === 'sources') return 'addresses';
  if (LINK_FAULTS.includes(check.id) && check.references === 'targets' && check.grouping === 'relatedUrl') {
    return 'pages';
  }
  return null;
}

interface FoundOn {
  urlId: number;
  pages: number;
  sitemaps: { total: number };
  start: boolean;
}

interface LinkRow {
  'from.id'?: number | null;
  'from.url'?: string | null;
  'from.title'?: string | null;
  'to.id'?: number | null;
  kind?: string | null;
  text?: string | null;
  position?: string | null;
}

export interface FixList {
  /** The card's count: "8 links to change on 4 pages · 1 address no page links to". */
  summary: string;
  /** The pages to edit, in the card's order, each with how many links to change it has. */
  pages: { url: string; title: string | null; links: number }[];
  /** The dead addresses a few pages link to: each is where a row of a page to edit leads. */
  linked: string[];
  /** The dead addresses more than ten pages link to: one row each. */
  shared: string[];
  /** The dead addresses no page links to. */
  unlinked: string[];
  /** Whether every address no page links to is in a sitemap: the part's heading says so. */
  unlinkedInSitemap: boolean;
}

const number = (value: number) => value.toLocaleString('en-GB');

/** A check of dead addresses as its card lists the first of them, from the sidecar. */
export async function deadEndFixList(crawlId: string, checkId: string): Promise<FixList> {
  const { rows } = await read<{ rows: { id: number; url: string | null }[] }>(
    `/api/crawls/${crawlId}/issues/${checkId}/rows`,
    { columns: ['url'], limit: FIX_LISTED },
  );
  const found = await read<{ urls: FoundOn[] }>(`/api/crawls/${crawlId}/urls/found-on`, {
    urlIds: rows.map((row) => row.id),
    limit: 5,
  });
  const where = new Map(found.urls.map((url) => [url.urlId, url]));
  const addresses = rows.flatMap((row) => (row.url ? [{ id: row.id, url: row.url, where: where.get(row.id) }] : []));
  const few = addresses.filter(({ where: at }) => at && at.pages > 0 && at.pages <= SHARED_PAGES);
  const links =
    few.length === 0
      ? []
      : (
          await read<{ rows: LinkRow[] }>(`/api/crawls/${crawlId}/links`, {
            columns: ['from.id', 'from.url', 'from.title', 'to.id', 'to.url', 'kind', 'text', 'position'],
            filters: [{ column: 'to.id', op: 'in', values: few.map((address) => address.id) }],
            sort: [],
            offset: 0,
            limit: 1_000,
          })
        ).rows;
  // A page's links to one address with the same words and place are one row.
  const pages = new Map<number, { url: string; title: string | null; links: Set<string> }>();
  const linked = new Set<string>();
  for (const address of few) {
    for (const link of links) {
      const from = link['from.id'];
      const url = link['from.url'];
      if (link['to.id'] !== address.id || typeof from !== 'number' || !url) continue;
      // An address that forwards here isn't a page with a link to change (fixPages' addressesModel).
      if (link.kind === 'redirect') continue;
      const page = pages.get(from) ?? { url, title: null, links: new Set<string>() };
      const title = link['from.title'];
      if (!page.title && title) page.title = title;
      page.links.add(`${address.id}|${link.kind ?? ''}|${link.text?.trim() ?? ''}|${link.position ?? ''}`);
      pages.set(from, page);
      linked.add(address.url);
    }
  }
  const ordered = [...pages.values()]
    .map((page) => ({ url: page.url, title: page.title, links: page.links.size }))
    .sort((a, b) => b.links - a.links || a.url.localeCompare(b.url));
  const shared = addresses.filter(({ where: at }) => at && at.pages > SHARED_PAGES);
  // No page links to it: nothing refers to it at all, or only addresses that forward to it do.
  const forwarded = few.filter((address) => !linked.has(address.url));
  const unlinked = [...addresses.filter(({ where: at }) => !at || at.pages === 0), ...forwarded];
  const linkCount = ordered.reduce((sum, page) => sum + page.links, 0);
  const summary = [
    linkCount > 0
      ? `${linkCount === 1 ? '1 link' : `${number(linkCount)} links`} to change on ${ordered.length === 1 ? '1 page' : `${number(ordered.length)} pages`}`
      : '',
    shared.length > 0
      ? shared.length === 1
        ? '1 link many pages share'
        : `${number(shared.length)} links many pages share`
      : '',
    unlinked.length > 0
      ? unlinked.length === 1
        ? '1 address no page links to'
        : `${number(unlinked.length)} addresses no page links to`
      : '',
  ]
    .filter(Boolean)
    .join(' · ');
  return {
    summary,
    pages: ordered,
    linked: [...linked],
    shared: shared.map((address) => address.url),
    unlinked: unlinked.map((address) => address.url),
    unlinkedInSitemap:
      unlinked.length > 0 &&
      unlinked.every((address) => !forwarded.includes(address) && (address.where?.sitemaps.total ?? 0) > 0),
  };
}

/** The words a link's row leads with (fixPages' `linkWords`), then how often the page has it. */
export const LINK_WORDS =
  /^(“.+”|The image “.+”|An image with no description|A file the page loads|The main address the page names|The language version “.+”|A language version the page names|The page forwards there|The next or previous page it names|The image it names for sharing|A link with no words)( · [\d,]+ times)?$/;

/** Where on the page a link is (fixPages' `placeWords`); empty where the crawl kept no place. */
export const LINK_PLACE = /^(main content|menu|header|footer|sidebar|page’s code)?$/;

/** The card's count as fixPages' `fixSummary` writes it, any part of it left out but never all three. */
export const FIX_SUMMARY =
  /^(?=.)([\d,]+ links? to change on [\d,]+ pages?)?(( · )?[\d,]+ links? many pages share)?(( · )?[\d,]+ address(es)? no page links to)?$/;
