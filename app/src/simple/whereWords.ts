// What "Where it is" says under its list (docs/v2-ui-simple.md, "One problem"), and the site's origin its addresses are
// shortened against. Pure. The closing audit's gap 12: it said "It is shown. Addresses are on gardenfurnituresets.co.uk."
// of one cause, while the list showed every address in full (the pages were on www., the start address wasn't).
import type { IssueGroup, IssueRow } from '../api/grids';
import type { Check, CheckCount, CrawlListItem } from '../api/types';
import { formatCount } from '../lib/format';
import { plainDetail } from './plainEvidence';
import { plainTitle } from './plainWords';

/**
 * What a grouped check's causes are, in Simple's words (the fixit audit: "129 causes, on 60 pages" left an owner asking
 * "129 what?"): one and more of them ("image", "images"), the heading over them, and the heading over the pages
 * they're on.
 */
export interface CauseWords {
  one: string;
  many: string;
  heading: string;
  pagesHeading: string;
}

/** Checks whose causes are named, and headed, by what the check is about rather than by its kind. */
const CAUSE_WORDS: Record<string, Partial<CauseWords>> = {
  // The alternate is where the fix is: it lacks the link back to the pages that name it.
  'hreflang-missing-return': {
    heading: 'Version that doesn’t link back',
    pagesHeading: 'Pages it should link back to',
  },
  // Each later page's canonical names the list's first page: a change, where one is wanted, is made on the later pages.
  'canonical-to-first-page': {
    one: 'list',
    many: 'lists',
    heading: 'The list’s first page',
    pagesHeading: 'Later pages that point Google at it',
  },
  'duplicate-content': { one: 'set of matching pages', many: 'sets of matching pages' },
  'near-duplicate-content': { one: 'set of nearly matching pages', many: 'sets of nearly matching pages' },
  'broken-resources': { one: 'file', many: 'files', heading: 'File' },
  'mixed-content': { one: 'file', many: 'files', heading: 'File' },
};

/** What a check's causes are, by what its groups hold: a related URL's kind, the text pages share, or what was found. */
export function causeWords(check: Pick<Check, 'id' | 'category' | 'grouping' | 'evidence' | 'subject'>): CauseWords {
  const pagesHeading = (check.subject ?? 'page') === 'page' ? 'On these pages' : 'At these addresses';
  const kind = ((): Omit<CauseWords, 'pagesHeading'> => {
    if (check.grouping !== 'relatedUrl') {
      return { one: 'kind of problem', many: 'kinds of problem', heading: 'What was found' };
    }
    const evidence = check.evidence ?? [];
    if (check.category === 'images' || check.id.startsWith('social-image-')) {
      return { one: 'image', many: 'images', heading: 'Image' };
    }
    if (evidence.includes('title')) return { one: 'title', many: 'titles', heading: 'The title they share' };
    if (evidence.includes('description')) {
      return { one: 'description', many: 'descriptions', heading: 'The description they share' };
    }
    if (evidence.includes('h1')) return { one: 'heading', many: 'headings', heading: 'The main heading they share' };
    if (check.category === 'hreflang') {
      return { one: 'language version', many: 'language versions', heading: 'Language version' };
    }
    if (check.category === 'canonicals') {
      return { one: 'main address', many: 'main addresses', heading: 'Main address they name' };
    }
    if (check.category === 'links') return { one: 'address', many: 'addresses', heading: 'Address linked to' };
    return { one: 'address', many: 'addresses', heading: 'What it’s about' };
  })();
  return { ...kind, pagesHeading, ...CAUSE_WORDS[check.id] };
}

/** "129 images on 60 pages": the check's causes, then the pages (or addresses) the headline counts. */
export function causesWords(
  check: Pick<Check, 'id' | 'category' | 'grouping' | 'evidence' | 'subject'>,
  causes: number,
  pages: number,
): string {
  const words = causeWords(check);
  const onPages = (check.subject ?? 'page') === 'page';
  const noun = onPages ? (pages === 1 ? 'page' : 'pages') : pages === 1 ? 'address' : 'addresses';
  return `${formatCount(causes)} ${causes === 1 ? words.one : words.many} on ${formatCount(pages)} ${noun}`;
}

/**
 * The categories whose addresses are fixed where they're linked from: a broken address, or one that forwards, is
 * mended by the links to it. An address search engines are told not to visit, or a page with a fault of its own, isn't
 * (the fixit audit's X2: the problem page told an owner to remove the Checkout and Cart links).
 */
const FIXED_WHERE_LINKED: ReadonlySet<string> = new Set(['links', 'responseCodes', 'redirects']);

/** The Links checks whose pages want more links, not fewer: what to do at the page linking to most of them. */
const SHARED_SOURCE_ADVICE: Record<string, string> = {
  'non-indexable-inlinks-only':
    'It’s kept out of search results itself, so link to these pages from one that shows in search too.',
  'orphan-page':
    'Search engines are told not to follow the links there, so make them ordinary links, or link to these pages from where they belong.',
};

/**
 * What the problem page tells the owner to do at the one page linking to most of a check's addresses, after "… links
 * to most of them": "Fix or remove the links there first." for a broken or forwarding address, a line of its own for
 * a Links check whose pages want more links. Null for every other check: it gets no such callout.
 */
export function sharedSourceAdvice(check: Pick<Check, 'id' | 'category'>): string | null {
  return (
    SHARED_SOURCE_ADVICE[check.id] ??
    (FIXED_WHERE_LINKED.has(check.category) ? 'Fix or remove the links there first.' : null)
  );
}

/**
 * The checks grouped by related URL whose page detail says what the cause's facts don't: the language codes that
 * disagree, the size a share image is under or over, a canonical loop's steps, a canonical forwarding back to the
 * page, the page number naming page 1. Every other one's detail is a count, or the facts again.
 */
const DETAIL_SAYS_MORE: ReadonlySet<string> = new Set([
  'hreflang-inconsistent-code',
  'social-image-too-small',
  'social-image-too-large',
  'canonical-loop',
  'canonical-to-redirect',
  'canonical-to-first-page',
]);

/**
 * Whether a page's own detail goes under it in a cause's list (the fixit index audit's X3): for a check grouped by
 * what was found, where the page's detail isn't the group's (canonical-relative's "/plants/" under "Root-relative");
 * for one grouped by related URL, where the detail says more than the facts and is about this cause (a page naming two
 * versions has the detail of its first: it goes under that one only).
 */
export function rowDetailSaysMore(
  check: Pick<Check, 'id' | 'grouping'>,
  group: Pick<IssueGroup, 'detail' | 'relatedUrl'>,
  row: Pick<IssueRow, 'detail' | 'relatedUrl'>,
): boolean {
  if (!row.detail) return false;
  if (check.grouping === 'detail') return row.detail !== group.detail;
  return DETAIL_SAYS_MORE.has(check.id) && (row.relatedUrl ?? null) === group.relatedUrl;
}

/** What a group without a detail (or, for a check grouped by related URL, without one) holds, by the check. */
const UNNAMED_CAUSES: Record<string, string> = {
  'h1-missing': 'No main heading',
  'blocked-by-robots': 'Kept out by the rules file',
  // The links to sections missing from the page they're on name no other address.
  'broken-fragment-links': 'Links to a part of the same page that isn’t there',
};

/**
 * A cause as Where it is, Copy the list and the causes fixed since name it, where it's named by its detail
 * (h1-missing's "No main heading"): its detail in plain words (plainDetails.ts: "It goes from the main heading to a
 * level-3 heading, skipping level 2", never "H1 → H3"), or as the engine wrote it in a form no check writes; a group
 * whose detail is empty is named by the check, so no cause is ever a blank.
 */
export function causeName(check: Check, detail: string | null | undefined): string {
  if (detail) return plainDetail(check, { detail }) ?? detail;
  return UNNAMED_CAUSES[check.id] ?? plainTitle(check);
}

/** What a check's files are called (X4): its rules file, the term once in brackets, or its sitemap. */
export interface FileWords {
  /** What names each file, over its address. */
  label: string;
  one: string;
  many: string;
}

/** A file a check is about, as the analysis's counts name it. */
type CheckFile = NonNullable<CheckCount['files']>[number];

/**
 * The words for a check's files (X4): "Your rules file (robots.txt)", the plain word first and the term once in
 * brackets (docs/v2-ui-simple.md, "The rules"), or "Your sitemap", a word that stays as it is.
 */
export function fileWords(about: NonNullable<Check['about']>): FileWords {
  return about === 'robotsTxt'
    ? { label: 'Your rules file (robots.txt)', one: 'rules file', many: 'rules files' }
    : { label: 'Your sitemap', one: 'sitemap', many: 'sitemaps' };
}

/**
 * The files Where it is and Copy the list name where a check's home page's row was (X4): those of a check `about` the
 * site's rules file or a sitemap, from an analysis that names them. Null otherwise: an analysis older than 16, a
 * sidecar from before it, or no file named (never an empty list where the row was). The row is shown then.
 */
export function filesOf(
  check: Pick<Check, 'about'>,
  counted: { files?: readonly CheckFile[] | null | undefined } | undefined,
): readonly CheckFile[] | null {
  const files = counted?.files ?? null;
  return check.about && files && files.length > 0 ? files : null;
}

/** The origin robots.txt in Advanced opens a rules file by: its address without "/robots.txt". */
export function robotsOrigin(url: string): string {
  return url.replace(/\/robots\.txt$/i, '');
}

const bare = (host: string) => host.toLowerCase().replace(/^www\./, '');

function parsed(url: string | null | undefined): URL | null {
  if (!url) return null;
  try {
    return new URL(url);
  } catch {
    return null;
  }
}

/**
 * The origin the list's addresses are shortened against, so the site's own show as paths ("/blog/"): the origin its
 * first address is on, when that is the site's (its host, www. or not), else the start address's. Null when neither is
 * known.
 */
export function pathOrigin(
  firstUrl: string | null | undefined,
  item: Pick<CrawlListItem, 'site' | 'startUrl'>,
): string | null {
  const start = parsed(item.startUrl);
  const first = parsed(firstUrl);
  const site = item.site ?? (start ? bare(start.hostname) : null);
  if (first && site !== null && bare(first.hostname) === bare(site)) return first.origin;
  return start?.origin ?? null;
}

/** The origin as the footer names it: "www.example.co.uk" on https, "http://example.co.uk:8080" otherwise. */
function originName(origin: string): string {
  return origin.startsWith('https://') ? origin.slice('https://'.length) : origin;
}

/**
 * The footer's sentence: how many of what the list counts are shown (its causes, by what they are, for a check that
 * groups them; its pages or addresses otherwise), then where the addresses shown as paths are.
 */
export function shownWords({
  grouped,
  listed,
  all,
  noun,
  cause = (count) => (count === 1 ? 'cause' : 'causes'),
  origin,
  shortened,
}: {
  grouped: boolean;
  listed: number;
  all: number;
  /** The check's noun for a count of what it flags: "page", "pages", "address", "addresses". */
  noun: (count: number) => string;
  /** What its causes are, for a check that groups them: "image", "images" (causeWords). */
  cause?: (count: number) => string;
  origin: string | null;
  /** Whether any address shown was shortened to its path. */
  shortened: boolean;
}): string {
  const what = (count: number) => (grouped ? cause(count) : noun(count));
  const count =
    listed >= all
      ? all === 1
        ? `The one ${what(1)} is shown.`
        : `All ${formatCount(all)} ${what(all)} are shown.`
      : `Showing ${formatCount(listed)} of ${formatCount(all)} ${what(all)}.`;
  return origin && shortened ? `${count} Addresses starting with “/” are on ${originName(origin)}.` : count;
}
