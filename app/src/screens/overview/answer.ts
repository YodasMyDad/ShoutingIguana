// The answer's figures and where each leads (docs/v2-ui-ia.md, Overview (done)): issues by severity, what to fix
// first, issues by category, the skipped checks, indexability and why not, depth and response times. Built from the
// checks list Issues builds (issues/checkList.ts) and the URL facets, so the two screens can't disagree, and every
// figure carries the link to exactly what it counts. Pure.
import type { FacetBucket, FacetsPage, GridFilter } from '../../api/grids';
import type { Checks, Severity } from '../../api/types';
import { formatCountOf } from '../../lib/format';
import { SEVERITIES } from '../../ui/severity';
import { analysisTotals, rankChecksToFix, type CheckEntry } from '../issues/checkList';
import { ANSWERED_SITE_PAGES, CRAWLED_SITE_PAGES, urlsLink, type IssuesLink, type UrlsLink } from './links';

// ------------------------------------------------------------------------------------------------- Issues by severity

export interface SeverityFigure {
  severity: Severity;
  /** Checks of this severity that found at least one issue. */
  checks: number;
  /** Their issues: one check on one URL. */
  issues: number;
  /**
   * The separate things to change their issues come to, and of those, how many are likely in the site's template
   * (`analysisTotals`); null when the analysis doesn't count them.
   */
  causes: number | null;
  templateCauses: number;
  /** Issues, listing this severity's checks. */
  link: IssuesLink;
}

/**
 * Checks that found issues, their issues and the things to change they come to, per severity. Hidden checks are left
 * out, as the sidebar leaves them.
 */
export function severityFigures(entries: readonly CheckEntry[]): SeverityFigure[] {
  return SEVERITIES.map((severity) => {
    const found = entries.filter((entry) => entry.outcome === severity && !entry.hidden);
    const totals = analysisTotals(found);
    return {
      severity,
      checks: found.length,
      issues: totals.issues,
      causes: found.length === 0 ? 0 : (totals.causes ?? null),
      templateCauses: totals.templateCauses ?? 0,
      link: { to: 'issues', severity },
    };
  });
}

// ----------------------------------------------------------------------------------------------------------- Fix first

/**
 * How many checks "Fix first" lists before it points to Issues for the rest. Twelve: eight left the card a third
 * blank beside its neighbours at 2560 x 1380 (the audit of 2026-11-19, H-18).
 */
export const FIX_FIRST_LIMIT = 12;

/** A check another's fix clears entirely, listed with it: its pages, every one it flagged. */
export interface ClearedWith {
  entry: CheckEntry;
  pages: number;
}

export interface FixFirstItem {
  entry: CheckEntry;
  link: IssuesLink;
  /** The checks its fix clears entirely (the analysis's `counts[].clears` with `all`), listed with it, not apart. */
  cleared: ClearedWith[];
}

export interface FixFirst {
  /**
   * The jobs to do first, in order: the engine's (`fixFirst`), or the most severe then the most affected. A check
   * another's fix clears entirely is in that one's job, at the place of whichever of them comes first.
   */
  checks: FixFirstItem[];
  /** Jobs with issues that aren't listed. */
  more: number;
}

/**
 * The checks to fix first among the ones that aren't hidden. With every check that found issues hidden there's none
 * to list: Issues still opens on one of them (it has to open on something), but here they'd sit beside figures that
 * say no check found an issue. A check whose fix clears another entirely (fixing the 4xx URLs clears every page of
 * broken links to them) is one job with it: the cleared check is named in its clearer's item, never listed apart.
 */
export function fixFirst(
  entries: readonly CheckEntry[],
  limit: number = FIX_FIRST_LIMIT,
  order?: readonly string[] | null,
): FixFirst {
  // The engine's order (the analysis's `fixFirst`) when the analysis sends it.
  const ranked = rankChecksToFix(
    entries.filter((entry) => !entry.hidden),
    order,
  );
  const byId = new Map(ranked.map((entry) => [entry.check.id, entry]));
  // Each check cleared entirely goes with the first listed check that clears it, unless that one is itself cleared.
  const clearer = new Map<string, string>();
  for (const entry of ranked) {
    for (const cleared of entry.clears) {
      if (
        cleared.all &&
        cleared.checkId !== entry.check.id &&
        byId.has(cleared.checkId) &&
        !clearer.has(cleared.checkId)
      ) {
        clearer.set(cleared.checkId, entry.check.id);
      }
    }
  }
  for (const id of [...clearer.keys()]) {
    if (clearer.has(clearer.get(id) ?? '')) clearer.delete(id);
  }
  const jobs: (FixFirstItem & { place: number })[] = [];
  ranked.forEach((entry, place) => {
    if (clearer.has(entry.check.id)) return;
    const cleared = entry.clears.flatMap((item) => {
      const other = byId.get(item.checkId);
      return other && clearer.get(item.checkId) === entry.check.id ? [{ entry: other, pages: item.pages }] : [];
    });
    const first = Math.min(place, ...cleared.map((item) => ranked.indexOf(item.entry)));
    jobs.push({ entry, link: { to: 'issues', check: entry.check.id }, cleared, place: first });
  });
  jobs.sort((a, b) => a.place - b.place);
  return {
    checks: jobs.slice(0, limit).map(({ place: _place, ...job }) => job),
    more: Math.max(0, jobs.length - limit),
  };
}

/** "Fixing it also clears Links to broken internal URLs on 4 pages.", for the checks a fix clears entirely. */
export function clearedWords(cleared: readonly ClearedWith[]): string {
  const parts = cleared.map((item) => `${item.entry.check.title} on ${formatCountOf(item.pages, 'page')}`);
  const listed = parts.length > 1 ? `${parts.slice(0, -1).join(', ')} and ${parts.at(-1) ?? ''}` : (parts[0] ?? '');
  return `Fixing it also clears ${listed}.`;
}

// ---------------------------------------------------------------------------------------------------------- Categories

export interface CategoryFigure {
  id: string;
  title: string;
  /** Checks in the category that found issues, by severity. */
  checks: Record<Severity, number>;
  /** Their issues in all. */
  issues: number;
  /** Issues, listing the category's checks: by its id, as its title is in other categories' checks' titles too. */
  link: IssuesLink;
}

/**
 * The categories with issues, in catalog order, each with its checks by severity and its issues. Hidden checks are
 * left out. A category the catalog doesn't name (a newer engine's) joins "Other checks", which its id opens in Issues
 * as it does here: every check whose category the catalog doesn't name.
 */
export function categoryFigures(checks: Checks, entries: readonly CheckEntry[]): CategoryFigure[] {
  const titles = new Map(checks.categories.map((category) => [category.id, category.title]));
  const figures = new Map<string, CategoryFigure>();
  for (const category of checks.categories) {
    figures.set(category.title, {
      id: category.id,
      title: category.title,
      checks: { error: 0, warning: 0, notice: 0 },
      issues: 0,
      link: { to: 'issues', category: category.id },
    });
  }
  for (const entry of entries) {
    if (entry.outcome === 'passed' || entry.outcome === 'skipped' || entry.hidden) continue;
    const title = titles.get(entry.check.category) ?? 'Other checks';
    let figure = figures.get(title);
    if (!figure) {
      figure = {
        id: entry.check.category,
        title,
        checks: { error: 0, warning: 0, notice: 0 },
        issues: 0,
        link: { to: 'issues', category: entry.check.category },
      };
      figures.set(title, figure);
    }
    figure.checks[entry.outcome]++;
    figure.issues += entry.count ?? 0;
  }
  return [...figures.values()].filter((figure) => figure.issues > 0);
}

// ------------------------------------------------------------------------------------------------------ Skipped checks

export interface SkippedGroup {
  /** Why, in the engine's words; null when the analysis didn't say (an engine from before E2): the notes do. */
  reason: string | null;
  /** The checks skipped for it, in catalog order. */
  checks: { id: string; title: string; link: IssuesLink }[];
}

/** The checks that didn't run, grouped by why, so one cause reads once. Never left out: skipped is not clean. */
export function skippedGroups(entries: readonly CheckEntry[]): SkippedGroup[] {
  const groups = new Map<string | null, SkippedGroup>();
  for (const entry of entries) {
    if (entry.outcome !== 'skipped') continue;
    let group = groups.get(entry.skipReason);
    if (!group) {
      group = { reason: entry.skipReason, checks: [] };
      groups.set(entry.skipReason, group);
    }
    group.checks.push({ id: entry.check.id, title: entry.check.title, link: { to: 'issues', check: entry.check.id } });
  }
  return [...groups.values()];
}

/** Whether a skip reason's or a note's remedy is a fresh crawl: what says it then carries "Crawl again…" as a control. */
export function asksForACrawl(reason: string | null): boolean {
  return reason !== null && /crawl again/i.test(reason);
}

/**
 * A group's reason as the card says it (the audit of 2026-11-19, H-11). The engine writes each reason for one check
 * ("what this check needs. Crawl again to check it."): over several checks it reads in the plural. And "an earlier
 * version" says whose: Shouting Iguana's.
 */
export function skippedReasonWords(reason: string, checks: number): string {
  const whose = reason.replace(/earlier version(?! of)/g, 'earlier version of Shouting Iguana');
  if (checks === 1) return whose;
  return whose
    .replace(/\b(this|the) check needs\b/g, 'these checks need')
    .replace(/\bthis check\b/g, 'these checks')
    .replace(/\bto check it\b/g, 'to check them')
    .replace(/\bto run it\b/g, 'to run them');
}

// -------------------------------------------------------------------------------------------------------- Indexability

/** The facets query indexability reads: the site's page URLs the crawl asked for. */
export const INDEXABILITY_FACETS = { filters: [...CRAWLED_SITE_PAGES], facets: ['indexability'] as const };

/** Why a URL isn't indexable, as a row reads. */
const NOT_INDEXABLE: Record<string, string> = {
  noindex: 'Noindex',
  canonicalised: 'Canonical points to another URL',
  redirected: 'Redirects',
  clientError: 'Answers 4xx',
  serverError: 'Answers 5xx',
  blockedByRobots: 'Blocked by robots.txt',
  blocked: 'Blocked by a firewall or bot challenge',
  unreachable: 'Didn’t answer',
};

export interface IndexabilityRow {
  /** The engine's reason, to key on. */
  reason: string;
  label: string;
  count: number;
  link: UrlsLink;
}

export interface IndexabilityFigures {
  /** The site's page URLs the crawl asked for. */
  total: number;
  indexable: number;
  indexableLink: UrlsLink;
  notIndexable: number;
  notIndexableLink: UrlsLink;
  /** Why not, the most first. */
  reasons: IndexabilityRow[];
  /** URLs nothing was judged for (a page only status-checked), with their link. Null when there are none. */
  notJudged: { count: number; link: UrlsLink } | null;
}

const within = (base: readonly GridFilter[], bucket: FacetBucket): UrlsLink => urlsLink([...base, bucket.filter]);

export function indexabilityFigures(facets: FacetsPage): IndexabilityFigures {
  const buckets = facets.facets.indexability ?? [];
  const indexable = buckets.find((bucket) => bucket.value === 'none');
  const unjudged = buckets.find((bucket) => bucket.value === null);
  const reasons = buckets
    .filter((bucket) => typeof bucket.value === 'string' && bucket.value !== 'none' && bucket.count > 0)
    .map((bucket) => ({
      reason: String(bucket.value),
      // A reason from a newer engine has no words here: it says so, rather than show the engine's id.
      label: NOT_INDEXABLE[String(bucket.value)] ?? 'Another reason',
      count: bucket.count,
      link: within(CRAWLED_SITE_PAGES, bucket),
    }))
    .sort((a, b) => b.count - a.count);
  return {
    total: facets.matching,
    indexable: indexable?.count ?? 0,
    indexableLink: urlsLink([...CRAWLED_SITE_PAGES, { column: 'indexability', op: 'in', values: ['none'] }]),
    notIndexable: reasons.reduce((sum, row) => sum + row.count, 0),
    // Every reason but "none". A negative filter matches the URLs nothing was judged for too, so they're left out.
    notIndexableLink: urlsLink([
      ...CRAWLED_SITE_PAGES,
      { column: 'indexability', op: 'notIn', values: ['none'] },
      { column: 'indexability', op: 'isNotEmpty' },
    ]),
    reasons,
    notJudged:
      unjudged && unjudged.count > 0 ? { count: unjudged.count, link: within(CRAWLED_SITE_PAGES, unjudged) } : null,
  };
}

// ----------------------------------------------------------------------------------------- Depth and response times

/** The facets query depth and response times read: the site's pages that answered. */
export const PAGE_FACETS = { filters: [...ANSWERED_SITE_PAGES], facets: ['depth', 'responseTime'] as const };

export interface ChartColumn {
  key: string;
  /** Under the column: "0", "10+", "250–499 ms". */
  label: string;
  /** For the column's name and the table: "Depth 3", "250 to 499 ms". */
  name: string;
  count: number;
  /** Null for a column with nothing to open. */
  link: UrlsLink | null;
}

/** Depths from this on share one column. */
export const DEEPEST_COLUMN = 10;

export interface DepthFigures {
  /** A column per depth from 0 to the deepest (gaps included, at 0), the last gathering every depth from 10 on. */
  columns: ChartColumn[];
  /** Pages only a sitemap lists, which no click reaches. Null when there are none. */
  unlinked: { count: number; link: UrlsLink } | null;
  /** Pages with a depth. */
  total: number;
}

export function depthFigures(facets: FacetsPage): DepthFigures {
  const buckets = facets.facets.depth ?? [];
  const byDepth = new Map<number, FacetBucket>();
  for (const bucket of buckets) if (typeof bucket.value === 'number') byDepth.set(bucket.value, bucket);
  const deepest = Math.max(-1, ...byDepth.keys());
  const columns: ChartColumn[] = [];
  for (let depth = 0; depth <= Math.min(deepest, DEEPEST_COLUMN - 1); depth++) {
    const bucket = byDepth.get(depth);
    columns.push({
      key: String(depth),
      label: String(depth),
      name: `Depth ${depth}`,
      count: bucket?.count ?? 0,
      link: bucket && bucket.count > 0 ? within(ANSWERED_SITE_PAGES, bucket) : null,
    });
  }
  if (deepest >= DEEPEST_COLUMN) {
    const count = [...byDepth].reduce(
      (sum, [depth, bucket]) => (depth >= DEEPEST_COLUMN ? sum + bucket.count : sum),
      0,
    );
    columns.push({
      key: 'deep',
      label: `${DEEPEST_COLUMN}+`,
      name: `Depth ${DEEPEST_COLUMN} or more`,
      count,
      link: urlsLink([...ANSWERED_SITE_PAGES, { column: 'depth', op: 'gte', value: DEEPEST_COLUMN }]),
    });
  }
  const unlinked = buckets.find((bucket) => bucket.value === null);
  return {
    columns,
    unlinked:
      unlinked && unlinked.count > 0 ? { count: unlinked.count, link: within(ANSWERED_SITE_PAGES, unlinked) } : null,
    total: columns.reduce((sum, column) => sum + column.count, 0),
  };
}

/** The facet's response-time buckets, always all five, so a fast site's chart still shows where slow would be. */
const RESPONSE_BUCKETS: readonly { value: string; label: string; name: string; min: number; max?: number }[] = [
  { value: '0-249', label: 'Under 250 ms', name: 'Under 250 ms', min: 0, max: 249 },
  { value: '250-499', label: '250–499 ms', name: '250 to 499 ms', min: 250, max: 499 },
  { value: '500-999', label: '500–999 ms', name: '500 to 999 ms', min: 500, max: 999 },
  { value: '1000-1999', label: '1–2 s', name: '1 to 2 s', min: 1000, max: 1999 },
  { value: '2000+', label: '2 s or more', name: '2 s or more', min: 2000 },
];

/** A page that takes this long is slow, as the slow-page check has it by default. */
export const SLOW_RESPONSE_MS = 1_000;

export interface ResponseFigures {
  columns: ChartColumn[];
  /** Pages with a response time. */
  total: number;
  /** Pages that took a second or more, with their link. */
  slow: number;
  slowLink: UrlsLink;
}

export function responseFigures(facets: FacetsPage): ResponseFigures {
  const buckets = facets.facets.responseTime ?? [];
  const columns = RESPONSE_BUCKETS.map((known) => {
    const bucket = buckets.find((candidate) => candidate.value === known.value);
    return {
      key: known.value,
      label: known.label,
      name: known.name,
      count: bucket?.count ?? 0,
      link: bucket && bucket.count > 0 ? within(ANSWERED_SITE_PAGES, bucket) : null,
    };
  });
  return {
    columns,
    total: columns.reduce((sum, column) => sum + column.count, 0),
    slow: RESPONSE_BUCKETS.reduce(
      (sum, known, index) => (known.min >= SLOW_RESPONSE_MS ? sum + (columns[index]?.count ?? 0) : sum),
      0,
    ),
    slowLink: urlsLink([...ANSWERED_SITE_PAGES, { column: 'responseMs', op: 'gte', value: SLOW_RESPONSE_MS }]),
  };
}
