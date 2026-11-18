// What a crawl is compared with in Advanced (docs/v2-fix-loop.md, "Comparing with another crawl (Advanced)"): its
// previous crawl by default, another analysed crawl of its site picked in Compare with…, or none. The crawls the picker
// offers, newest first, each that can't be compared with its reason; and a comparison with a later crawl read forward,
// from this crawl until that one ("since" read as "until", the arrows the other way). Pure.
import type {
  CheckChange,
  CheckChangeState,
  CheckStateCounts,
  ComparisonDifference,
  CrawlComparison,
  RowSplit,
} from '../../api/fixLoop';
import type { CrawlListItem, CrawlStatus } from '../../api/types';
import { changeDate, clockWords } from '../../changes/changeWords';
import type { CompareChoice } from '../../settings/compareWith';
import { startedAt } from '../../simple/home/sites';

/** One crawl the picker offers. */
export interface CompareCandidate {
  item: CrawlListItem;
  status: CrawlStatus;
  /** Why it can't be compared now, in Advanced's words; null when it can. */
  reason: string | null;
  /** It started after the crawl: the comparison reads "until". */
  later: boolean;
  /** The crawl's previous crawl: what it's compared with by default. */
  previous: boolean;
}

/** Home's order: the newer first, and of two that started at the same moment, the smaller id. */
const newestFirst = (a: CrawlListItem, b: CrawlListItem) => startedAt(b) - startedAt(a) || a.id.localeCompare(b.id);

/**
 * Why a crawl can't be compared with (the sidecar's `crawl-not-comparable`: never analysed or no page read, live,
 * interrupted, missing or unreadable), or null when it can.
 */
export function notComparableReason(item: CrawlListItem, status: CrawlStatus): string | null {
  if (item.missing) return 'File not found';
  switch (status) {
    case 'running':
      return 'Crawling now';
    case 'paused':
      return 'Paused';
    case 'stopping':
      return 'Stopping';
    case 'analyzing':
      return 'Being analysed';
    case 'rechecking':
      return 'Being rechecked';
    case 'unreadable':
      return 'Can’t be read';
    case 'inUse':
      return 'In use by another program';
    case 'interrupted':
      return 'Interrupted: continue it to compare';
    default:
      break;
  }
  if (item.analyzedAt === null) return 'Not analysed';
  if (item.pagesAnalyzed === 0) return 'No page was analysed';
  return null;
}

/** The site's other crawls, newest first, as the picker lists them; none for a crawl with no site. */
export function compareCandidates(
  item: CrawlListItem,
  library: readonly CrawlListItem[],
  statusOf: (item: CrawlListItem) => CrawlStatus,
): CompareCandidate[] {
  if (!item.siteKey) return [];
  return library
    .filter((other) => other.id !== item.id && other.siteKey === item.siteKey)
    .sort(newestFirst)
    .map((other) => {
      const status = statusOf(other);
      return {
        item: other,
        status,
        reason: notComparableReason(other, status),
        later: newestFirst(other, item) < 0,
        previous: other.id === item.previous?.id,
      };
    });
}

/** What a crawl is compared with now: nothing (a site's first crawl, or Don't compare), its previous, or a crawl picked. */
export type Comparing =
  | { kind: 'none'; why: 'first' | 'off' }
  | { kind: 'previous' | 'picked'; id: string; startedAt: string | null; later: boolean };

/**
 * From the preference: the crawl picked while it can be compared, else the previous crawl (a picked crawl deleted since
 * gives way to it, and the picker shows it disabled); none after Don't compare.
 */
export function comparingOf(
  item: Pick<CrawlListItem, 'previous'>,
  choice: CompareChoice,
  candidates: readonly CompareCandidate[],
): Comparing {
  if (choice === 'none') return { kind: 'none', why: 'off' };
  if (choice !== 'previous') {
    const picked = candidates.find((candidate) => candidate.item.id === choice && candidate.reason === null);
    if (picked) {
      return { kind: 'picked', id: picked.item.id, startedAt: picked.item.startedAt, later: picked.later };
    }
  }
  const previous = item.previous;
  return previous
    ? { kind: 'previous', id: previous.id, startedAt: previous.startedAt, later: false }
    : { kind: 'none', why: 'first' };
}

/** `compareWith` for the sidecar's reads: `previous`, a crawl's id, or null for none. */
export function compareWithParam(comparing: Comparing): string | null {
  if (comparing.kind === 'none') return null;
  return comparing.kind === 'previous' ? 'previous' : comparing.id;
}

/** "3 Nov, 09:12": when a crawl compared with started. */
export function crawlWhen(iso: string | null): string {
  const date = changeDate(iso);
  const clock = clockWords(iso);
  return date && clock ? `${date}, ${clock}` : date || 'another crawl';
}

/** The control's words: "Compared with 3 Nov, 09:12", "Not compared", or "Compare with…" with nothing to compare. */
export function comparedWithWords(comparing: Comparing): string {
  if (comparing.kind === 'none') return comparing.why === 'off' ? 'Not compared' : 'Compare with…';
  return `Compared with ${crawlWhen(comparing.startedAt)}`;
}

/** The comparison's period: "Since 3 Nov, 09:12", or "Until 20 Nov, 09:12" for a later crawl. */
export function periodWords(iso: string | null, later: boolean): string {
  return `${later ? 'Until' : 'Since'} ${crawlWhen(iso)}`;
}

// ---------------------------------------------------------------------------------------------- Read forward

const TURNED: Record<CheckChangeState, CheckChangeState> = {
  new: 'fixed',
  fixed: 'new',
  fewer: 'more',
  more: 'fewer',
  same: 'same',
  newCheck: 'notCheckedNow',
  notCheckedNow: 'newCheck',
};

function turnedSplit(split: RowSplit): RowSplit {
  return {
    fixed: split.new,
    gone: split.newUrl,
    notChecked: split.notComparedThen,
    still: split.still,
    new: split.fixed,
    newUrl: split.gone,
    notComparedThen: split.notChecked,
  };
}

function turnedCheck(check: CheckChange): CheckChange {
  return {
    checkId: check.checkId,
    severity: check.severityThen,
    severityThen: check.severity,
    state: TURNED[check.state],
    then: check.now,
    now: check.then,
    fixed: check.new,
    gone: check.newUrl,
    notChecked: check.notComparedThen,
    still: check.still,
    new: check.fixed,
    newUrl: check.gone,
    notComparedThen: check.notChecked,
  };
}

function turnedCounts(counts: CheckStateCounts): CheckStateCounts {
  return {
    fixed: counts.new,
    new: counts.fixed,
    fewer: counts.more,
    more: counts.fewer,
    same: counts.same,
    newCheck: counts.notCheckedNow,
    notCheckedNow: counts.newCheck,
  };
}

function turnedDifference(difference: ComparisonDifference): ComparisonDifference {
  const side = difference.side === 'then' ? 'now' : difference.side === 'now' ? 'then' : difference.side;
  const { then, now, ...rest } = difference;
  return {
    ...rest,
    ...(side ? { side } : {}),
    ...('now' in difference ? { then: now ?? null } : {}),
    ...('then' in difference ? { now: then ?? null } : {}),
  };
}

/**
 * A comparison with a later crawl, read forward from this crawl to that one: the sidecar's "then" is the later crawl,
 * so its fixed is what came since (new), its fewer what grew (more), and the score's change is the other way. Every
 * count stays the sidecar's; only the side each is told from moves.
 */
export function forwardComparison(comparison: CrawlComparison): CrawlComparison {
  const { score, totals } = comparison;
  return {
    ...comparison,
    differences: comparison.differences.map(turnedDifference),
    score: score
      ? {
          then: score.now,
          now: score.then,
          change: score.change === null ? null : -score.change,
          lines: score.lines.map((line) => ({ checkId: line.checkId, then: line.now, now: line.then })),
        }
      : null,
    totals: { checks: turnedCounts(totals.checks), issues: totals.issues ? turnedSplit(totals.issues) : null },
    checks: comparison.checks.map(turnedCheck),
  };
}
