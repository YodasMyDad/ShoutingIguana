import { useQueries, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { ChevronRight, Layers } from 'lucide-react';
import { useCallback, useEffect, useEffectEvent, useMemo, useRef, useState, type ReactNode } from 'react';
import { api } from '../../api/client';
import type { CheckGroupRef, IssueGroup, IssueRow } from '../../api/grids';
import {
  LOCATE_GROUP_LIMIT,
  issueGroupPlaceQuery,
  issueGroupRowsQuery,
  issueTargetsQuery,
  queryKeys,
  type GroupPlace,
} from '../../api/queries';
import type { Check } from '../../api/types';
import { affectedCount, affectedWords, partOfAffected, showInUrlsWords } from '../../crawl/checkSubject';
import { ErrorState } from '../../feedback/ErrorState';
import { notifyResult } from '../../feedback/notify';
import { NO_DETAIL } from '../../grid/filterSentence';
import { GridCore } from '../../grid/GridCore';
import { useColumnLayout } from '../../grid/useColumnLayout';
import { useGridMetrics } from '../../grid/metrics';
import { useRowMenu } from '../../grid/rowMenuState';
import type { RowWindowModel } from '../../grid/rowWindow';
import type { GridColumn, GridRange, GridTree, RowFetcher } from '../../grid/types';
import { useElementWidth } from '../../grid/useElementWidth';
import { usePagedRows } from '../../grid/usePagedRows';
import {
  FACT_GAP,
  evidenceLineParts,
  namesFileSize,
  noStatusWords,
  pixelsWords,
  quoted,
  relatedFactParts,
  shownAtCellWords,
  shownAtInsight,
} from '../../crawl/evidence';
import { formatBytes, formatCount } from '../../lib/format';
import { revealInScrollParent } from '../../lib/scroll';
import { useFontsLoaded } from '../../lib/useFontsLoaded';
import { Badge } from '../../ui/Badge';
import { Callout } from '../../ui/Callout';
import { Icon } from '../../ui/Icon';
import { ImageThumb } from '../../ui/ImageThumb';
import { isBrokenImage } from '../../ui/imageFacts';
import { ShareBar } from '../../ui/ShareBar';
import { TextLink } from '../../ui/TextLink';
import { urlDisplay } from '../../ui/urlText';
import { StatusCode } from '../../ui/StatusCode';
import { linkTextMeasure } from '../links/linkColumns';
import { linksOfUrl } from '../links/linkPlaces';
import { imagesOfPage } from '../links/presets';
import {
  FIRST_GROUP_ROWS,
  childPlace,
  childRows,
  groupFilter,
  groupRefOf,
  groupsGridPageOf,
  pageChildren,
  treeLayout,
  type ChildPlace,
  type OpenGroup,
  type OpenPage,
} from './groupTree';
import { IssuePageMenu } from './IssuePageMenu';
import { checkUrlsReader, copyUrls } from './copyUrls';
import { evidenceOf, groupRowColumns } from './evidenceColumns';
import { saysShownAt, useGroupShownAt } from './useShownAt';
import { useTemplateLink, useWhereFoundOf } from './useWhereFound';
import { groupColumnWidths } from './groupColumns';
import { GRID_FOOT_HEIGHT, ISSUE_KEY_HINTS, leastFrameHeight } from './issueGrid';
import { IssueUrl } from './IssueUrl';
import { URL_OPEN_ROOM } from './issueUrlWidth';
import { checkPlace, keepCheckPlace } from './issuesPlace';
import { useKeptScroll } from './useKeptScroll';
import { MoreToggle, ReferenceFacts, ReferenceOther, type ReferenceView } from './ReferenceCells';
import { openAtLink } from './referenceWords';
import { OPEN_ALL_ROWS } from './rowsTree';
import {
  SAME_PAGE,
  SOURCES_SHOWN,
  detailSaysMore,
  foundOnRows,
  groupUrlHeader,
  linkFactsOnly,
  moreReferences,
  referencesOf,
  targetLead,
  targetRows,
  sameLink,
  templateThingWords,
  whereRowText,
  type WhereRow,
} from './whereFound';
import { evidenceFrameClass } from './evidenceFrame';
import { changeDate } from '../../changes/changeWords';
import { groupChangeCell, type ComparedPeriod } from '../compare/changeCell';
import { recheckable } from '../rechecks/rechecks';

/**
 * A row of the tree: a group, one of the open group's first pages or URLs, a row under the one expanded to show where
 * it's found (a page linking to it, a sitemap listing it, a URL it names), or the group's link to all its pages.
 */
type TreeRow =
  | { kind: 'group'; group: IssueGroup }
  | { kind: 'page'; row: IssueRow; group: IssueGroup }
  | { kind: 'where'; where: WhereRow; at: number; page: IssueRow; group: IssueGroup }
  | { kind: 'more'; group: IssueGroup };

/** Wrappers kept per row object, so a row the grid has drawn keeps its identity (and its memoised cells). */
const groupRows = new WeakMap<IssueGroup, TreeRow>();
const pageRows = new WeakMap<IssueRow, TreeRow>();

const whereTreeRows = new WeakMap<WhereRow, TreeRow>();
const moreRows = new WeakMap<IssueGroup, TreeRow>();

/** The page under the open group expanded to show where it's found, by the group it's in and its URL id. */
interface ExpandedPage {
  group: string;
  page: number;
}

/** A group's identity: its detail and its related URL, as the engine keys it. */
const groupKey = (group: IssueGroup) => `${group.detail ?? ''}|${group.relatedUrlId ?? ''}`;

/** A row's identity in the tree, so the cursor stays on it when the rows are read again (an analysis). */
/** The fix loop: the groups compared with another crawl, each with its URLs then and how it moved. */
export interface GroupsCompare {
  compareWith: string;
  period: ComparedPeriod;
}

function treeRowKey(row: TreeRow): string {
  switch (row.kind) {
    case 'group':
      return `g|${groupKey(row.group)}`;
    case 'page':
      return `p|${groupKey(row.group)}|${row.row.id}|${row.row.detail ?? ''}|${row.row.relatedUrlId ?? ''}`;
    case 'where':
      return `w|${groupKey(row.group)}|${row.page.id}|${row.where.kind}|${row.at}`;
    case 'more':
      return `m|${groupKey(row.group)}`;
  }
}

function wrap<K extends object>(cache: WeakMap<K, TreeRow>, key: K, make: () => TreeRow): TreeRow {
  let row = cache.get(key);
  if (!row) {
    row = make();
    cache.set(key, row);
  }
  return row;
}

/** The placement query's stand-in while no group is open (it doesn't run then). */
const NO_GROUP: CheckGroupRef = { detail: null, relatedUrlId: null };

/** An image's group's words narrower than this say its address alone: its type is in its tooltip and the callout. */
const IMAGE_FACTS_ROOM = 280;
/** A group's words narrower than this: its "Likely template" badge is said, not drawn (the callout shows it). */
const NARROW_ROOM = 420;
/**
 * A group without a detail, in the grid's Detail column: the chips' "Detail is none", in a cell's case; for a check
 * whose detail names what a page has, what the page lacks.
 */
function noDetailShown(check: Pick<Check, 'id'>): string {
  return check.id === 'h1-missing' ? 'No H1' : 'None';
}
/** A group that names no URL (a link to nowhere, a page that names none): said in place of one. */
const NO_URL = 'No URL named';

/** A header inside a sentence: its first letter small, the rest as written ("linked URL", never "linked url"). */
const lowerFirst = (words: string) => words.charAt(0).toLowerCase() + words.slice(1);

/** The first column's header for a check grouped by URL: the image, the shared title, or what the other URL is. */
function relatedHeader(check: Check, shared: boolean): string {
  if (shared) {
    const evidence = evidenceOf(check);
    if (evidence.includes('title')) return 'Shared title';
    if (evidence.includes('description')) return 'Shared description';
    if (evidence.includes('h1')) return 'Shared H1';
    return 'Shared text';
  }
  return groupUrlHeader(check);
}

/**
 * What a group's related URL is, as the check's `targetFacts` say it: "1.8 MB · JPEG · 4000×3000 px", "→ /new/ 200",
 * "noindex". Empty for a group without one, or a sidecar from before the evidence.
 */
function relatedFactsWords(
  group: IssueGroup,
  check: Check,
  siteOrigin: string | null,
  /** The facts that have columns of their own (the file's size, its pixels): the line says the rest. */
  inColumns: readonly string[] = [],
): string {
  if (!group.related || !check.targetFacts?.length) return '';
  const facts = check.targetFacts.filter((fact) => !inColumns.includes(fact));
  return relatedFactParts(group.related, facts, siteOrigin).join(FACT_GAP);
}

/** Whether a check's groups are images (its related URL is the image at fault): they're drawn with a thumbnail. */
function groupsAreImages(check: Check): boolean {
  return check.grouping === 'relatedUrl' && check.id.startsWith('image-');
}

/** A group's image's thumbnail, not asked for where the image is known not to answer. */
function GroupThumb({ group }: { group: IssueGroup }) {
  if (!group.relatedUrl) return null;
  return <ImageThumb url={group.relatedUrl} broken={isBrokenImage(group.related ?? {})} className="me-2" />;
}

/** A group's "Shown at" cell: the largest size its first pages show its image at, and how much wider the file is. */
function ShownAtCell({ crawlId, check, group }: { crawlId: string; check: Check; group: IssueGroup }) {
  const shownAt = useGroupShownAt(crawlId, check, group);
  if (shownAt === undefined) return <span className="text-text-muted">…</span>;
  if (shownAt === null) return <span className="text-text-muted">–</span>;
  const words = shownAtCellWords(shownAt, group.related?.pixelWidth);
  // The size whole; how much wider the file is beside it, a size smaller.
  const at = words.indexOf(' (');
  return (
    <span title={shownAtInsight(shownAt, group.related, group.pages)}>
      {at < 0 ? words : words.slice(0, at)}
      {at < 0 ? null : <span className="text-12 text-text-secondary">{words.slice(at)}</span>}
    </span>
  );
}

/**
 * The related URL's status where it's what's wrong (a 404, a redirect, no answer): a 2xx is said by nothing, as most
 * groups' URLs answer, and the check's facts then say what's wrong instead (its size, its being noindex).
 */
function relatedStatus(group: IssueGroup, check: Check): { code: number | null; reason?: string } | null {
  const related = group.related;
  if (!related || !(check.targetFacts ?? []).includes('statusCode')) return null;
  if (related.statusCode !== null) return related.statusCode >= 300 ? { code: related.statusCode } : null;
  return related.outcome ? { code: null, reason: noStatusWords(related.outcome) } : null;
}

/** A page's evidence in one line, where its column headers aren't beside it: "→ /b/ 302 → /c/ 200", "“Sofas” · 12 inlinks". */
function evidenceLine(row: IssueRow, check: Check, siteOrigin: string | null): string {
  const values = row as unknown as { url?: string | null } & Record<
    string,
    string | number | boolean | object | null | undefined
  >;
  return evidenceLineParts(values, evidenceOf(check), siteOrigin).join(FACT_GAP);
}

export interface IssueGroupsProps {
  crawlId: string;
  check: Check;
  /** The check's flagged URLs. */
  total: number;
  siteOrigin: string | null;
  /** The group in view, from the route. */
  open: CheckGroupRef | undefined;
  /** Opens one group (closing any other), or closes it: a replace navigation. */
  onOpenChange: (group: CheckGroupRef | null) => void;
  onInspect: (urlId: number) => void;
  /** While the inspector is open: the page under the grid's cursor, which it follows. */
  onFollow?: ((urlId: number) => void) | undefined;
  /** The fix loop: each group's URLs then and how it moved. Left out, none. */
  compare?: GroupsCompare | null | undefined;
  /** The keys said under the rows; false in a window too short to spare a row for them. */
  legend?: boolean | undefined;
}

/**
 * What a check flags, grouped by cause (E3a, Issue groups): one treegrid row per group, largest first, with its
 * detail or related URL, its count, and its share drawn as a bar. Opening a group shows its first rows and "Show all N
 * pages in URLs", which opens the URL grid filtered to it. The groups are paged and virtualised like any grid, as a
 * big crawl's duplicate titles can make thousands. A group that's probably in the site's template gets the callout.
 * The engine calls a group's count `pages` whatever the check flags: here it's worded by the check's `subject`
 * (crawl/checkSubject.ts), pages only for a check that flags nothing else.
 */
export function IssueGroups({
  crawlId,
  check,
  total,
  siteOrigin,
  open,
  onOpenChange,
  onInspect,
  onFollow,
  compare = null,
  legend = true,
}: IssueGroupsProps) {
  const compareWith = compare?.compareWith ?? null;
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { rowHeight, headerHeight } = useGridMetrics();
  const [measure, width] = useElementWidth();
  const grouping = check.grouping;
  // The same object for every check of one subject, so what's memoised on it holds.
  const words = affectedWords(check);

  // The comparison is part of the key: IssuesScreen reads the first page under the same one.
  const queryKey = useMemo(
    () => [...queryKeys.issueGroupsGrid(crawlId, check.id), compareWith ?? 'none'] as const,
    [crawlId, check.id, compareWith],
  );
  const fetchPage = useCallback(
    async (offset: number, limit: number, signal: AbortSignal) =>
      groupsGridPageOf(
        await api.issueGroups(crawlId, check.id, { offset, limit, ...(compareWith ? { compareWith } : {}) }, signal),
      ),
    [crawlId, check.id, compareWith],
  );
  const groups = usePagedRows<IssueGroup>({ queryKey, fetchPage, rowHeight });
  const groupCount = groups.rowCount;

  // The open group: where it is among the groups, looked for from the largest. A group opened here is placed at once
  // (setQueryData below), so only a route from elsewhere has to look.
  const openRef = open ? groupRefOf(open) : null;
  const placement = useQuery({
    ...issueGroupPlaceQuery(crawlId, check.id, openRef ?? NO_GROUP),
    enabled: openRef !== null && groupCount !== null,
  });
  const place = openRef ? placement.data : undefined;
  const openGroup = place !== undefined && place.index !== null ? place : null;

  // A group the route names that the check doesn't have (a link from before the crawl was analysed again), or that
  // can't be found: it isn't shown open, so the route stops naming it (Copy URLs then copies the check's), and says so.
  const missing = place?.index === null ? place.missing : null;
  const dropMissing = useEffectEvent((reason: 'gone' | 'beyond') => {
    notifyResult(reason === 'gone' ? 'That group isn’t in this check now' : 'That group couldn’t be found', {
      description:
        reason === 'gone'
          ? 'The crawl was analysed again since the link was made. These are the check’s groups now.'
          : `It isn’t among the check’s ${formatCount(LOCATE_GROUP_LIMIT)} largest groups.`,
    });
    onOpenChange(null);
  });
  useEffect(() => {
    if (missing) dropMissing(missing);
  }, [missing, openRef?.detail, openRef?.relatedUrlId]);

  // A check of a few groups opens its first by itself (the largest, or the one in the template), once, as the route
  // names no group, so its rows are in view on arrival (A-06). Closed after, it stays closed.
  const onlyGroup =
    groupCount !== null && groupCount >= 1 && groupCount <= OPEN_ALL_ROWS ? groups.firstPage?.rows[0] : undefined;
  const openedByItself = useRef(open !== undefined);
  useEffect(() => {
    if (openedByItself.current || open !== undefined || !onlyGroup) return;
    openedByItself.current = true;
    const ref = groupRefOf(onlyGroup);
    const placed: GroupPlace = { index: 0, group: onlyGroup };
    queryClient.setQueryData(queryKeys.issueGroupPlace(crawlId, check.id, ref), placed);
    onOpenChange(ref);
  }, [open, onlyGroup, queryClient, crawlId, check.id, onOpenChange]);

  const firstRows = useQuery({
    ...issueGroupRowsQuery(
      crawlId,
      check.id,
      openRef ?? { detail: null, relatedUrlId: null },
      FIRST_GROUP_ROWS,
      groupRowColumns(check),
    ),
    enabled: openGroup !== null,
  });
  const loadedRows = firstRows.data?.rows ?? null;
  // Its first pages couldn't be read: no placeholders that would never fill, only its link to all of them, and the
  // error above the grid with Try again.
  const rowsFailed = firstRows.isError && loadedRows === null;
  const openIndex = openGroup?.index ?? null;

  // One of the open group's pages opened to show where it's found (a check whose URLs are referred to) or what it names
  // (a check whose pages hold the bad references): its rows follow it, read when it opens.
  const references = referencesOf(check);
  const [expanded, setExpandedState] = useState<ExpandedPage | null>(
    () => checkPlace(crawlId, check.id).expanded ?? null,
  );
  // Kept with the check for the session, so Back from Links finds the row open as it was left (A-07).
  const setExpanded = useCallback(
    (next: ExpandedPage | null) => {
      setExpandedState(next);
      keepCheckPlace(crawlId, check.id, { expanded: next });
    },
    [crawlId, check.id],
  );
  const openKey = openGroup ? groupKey(openGroup.group) : null;
  const expandedId = openKey !== null && expanded?.group === openKey ? expanded.page : null;
  const expandedAt = expandedId !== null && loadedRows ? loadedRows.findIndex((row) => row.id === expandedId) : -1;
  const where = useWhereFoundOf(crawlId, check, expandedAt >= 0 ? expandedId : null);
  // Under a group of the URL a page names, the page's line says its link to that URL: what opens under it is the other
  // URLs it names, and a page that names no other has nothing to open.
  const ownTarget =
    grouping === 'relatedUrl' && references === 'targets' ? (openGroup?.group.relatedUrlId ?? null) : null;
  // The pages that name another URL, as one string (the same string while they don't change).
  const namingOthers = useQueries({
    queries: (ownTarget !== null ? (loadedRows ?? []) : []).map((row) =>
      issueTargetsQuery(crawlId, check.id, row.id, SOURCES_SHOWN),
    ),
    combine: (results) =>
      results
        .flatMap((result) =>
          result.data && result.data.total > (result.data.targets.some((target) => target.id === ownTarget) ? 1 : 0)
            ? [result.data.urlId]
            : [],
        )
        .join(','),
  });
  const namesOthers = useMemo(
    () => new Set(namingOthers === '' ? [] : namingOthers.split(',').map(Number)),
    [namingOthers],
  );
  const whereRows = useMemo<WhereRow[] | null>(() => {
    if (where.foundOn) return foundOnRows(where.foundOn);
    if (where.targets) {
      const others =
        ownTarget === null
          ? where.targets
          : {
              ...where.targets,
              targets: where.targets.targets.filter((target) => target.id !== ownTarget),
              total: Math.max(0, where.targets.total - 1),
            };
      return targetRows(others, check, siteOrigin);
    }
    if (where.error) return [{ kind: 'nothing', words: 'Where it’s found couldn’t be read' }];
    return null;
  }, [where.foundOn, where.targets, where.error, check, siteOrigin, ownTarget]);
  // While its rows are read, one row stands in for them (drawn as a placeholder).
  const openPage: OpenPage | null = expandedAt >= 0 ? { page: expandedAt, rows: whereRows?.length ?? 1 } : null;
  const openChildren = openGroup
    ? rowsFailed
      ? 1
      : loadedRows
        ? pageChildren(loadedRows.length, openPage)
        : childRows(openGroup.group, null)
    : 0;
  /** Which of the open group's rows a child is. */
  const childAt = useCallback(
    (child: number): ChildPlace =>
      loadedRows
        ? childPlace(child, loadedRows.length, openPage)
        : child === openChildren - 1
          ? { kind: 'more' }
          : { kind: 'page', page: child },
    [loadedRows, openPage?.page, openPage?.rows, openChildren], // eslint-disable-line react-hooks/exhaustive-deps
  );

  // A group the route opens (a link, back or forward) may be thousands of rows down: bring it into view, with as many
  // of its pages as fit, and the grid into view in the panel (which comes back scrolled to its top). One opened here
  // is already in view, so this moves nothing.
  const model = useRef<RowWindowModel | null>(null);
  const frame = useRef<HTMLDivElement | null>(null);
  const keepScroll = useKeptScroll(crawlId, check.id, queryKey.join('|'));
  const measureFrame = useCallback(
    (element: HTMLDivElement | null) => {
      frame.current = element;
      measure(element);
      keepScroll(element);
    },
    [measure, keepScroll],
  );
  useEffect(() => {
    if (openIndex === null) return undefined;
    model.current?.scrollToIndex(openIndex + openChildren);
    model.current?.scrollToIndex(openIndex);
    // After the grid has drawn the rows it scrolled to.
    const id = requestAnimationFrame(() => {
      const element = frame.current;
      if (!element) return;
      const row = element.querySelector<HTMLElement>(`[role="row"][aria-rowindex="${openIndex + 2}"]`);
      revealInScrollParent(element, row);
    });
    return () => cancelAnimationFrame(id);
  }, [openIndex, openChildren, groupCount]);
  const layout = useMemo(() => {
    const opened: OpenGroup | null = openIndex === null ? null : { index: openIndex, children: openChildren };
    return treeLayout(groupCount ?? 0, opened);
  }, [groupCount, openIndex, openChildren]);
  const getGroup = groups.getRow;

  const rowMenu = useRowMenu<TreeRow>();
  const getRow = useCallback(
    (index: number): TreeRow | undefined => {
      const place = layout.place(index);
      if (place.kind === 'group') {
        const group = getGroup(place.group);
        return group ? wrap(groupRows, group, () => ({ kind: 'group', group })) : undefined;
      }
      if (!openGroup) return undefined;
      const { group } = openGroup;
      const child = childAt(place.child);
      if (child.kind === 'more') return wrap(moreRows, group, () => ({ kind: 'more', group }));
      const row = loadedRows?.[child.page];
      if (!row) return undefined;
      if (child.kind === 'page') return wrap(pageRows, row, () => ({ kind: 'page', row, group }));
      const entry = whereRows?.[child.row];
      return entry
        ? wrap(whereTreeRows, entry, () => ({ kind: 'where', where: entry, at: child.row, page: row, group }))
        : undefined;
    },
    [layout, getGroup, openGroup, childAt, loadedRows, whereRows],
  );

  // Copying a selection that spans groups not read yet (Ctrl+A, Ctrl+C on thousands): their pages are fetched, as the
  // flat grids' are. Rows under the open group are its first pages, which are loaded or not there.
  const fetchGroups = groups.fetchRows;
  const fetchRows = useCallback<RowFetcher<TreeRow>>(
    async (indices) => {
      const places = indices.map((index) => layout.place(index));
      const wanted = places.flatMap((place) => (place.kind === 'group' ? [place.group] : []));
      const fetched = await fetchGroups(wanted);
      const byGroup = new Map(wanted.map((group, at) => [group, fetched[at]]));
      return indices.map((index, at) => {
        const place = places[at];
        if (place?.kind !== 'group') return getRow(index);
        const group = getGroup(place.group) ?? byGroup.get(place.group);
        return group ? wrap(groupRows, group, () => ({ kind: 'group', group })) : undefined;
      });
    },
    [layout, fetchGroups, getGroup, getRow],
  );

  const toGroups = useCallback(
    (range: GridRange): GridRange => {
      const first = layout.place(range.first);
      const last = layout.place(range.last);
      return { first: first.group, last: last.group, scrubbing: range.scrubbing };
    },
    [layout],
  );

  const tree = useMemo<GridTree>(
    () => ({
      level: (index) => {
        const place = layout.place(index);
        if (place.kind === 'group') return 1;
        return childAt(place.child).kind === 'where' ? 3 : 2;
      },
      expanded: (index) => {
        const place = layout.place(index);
        if (place.kind === 'group') return place.group === openIndex;
        // A page opens to show where it's found, for a check whose URLs are referred to, or what it names.
        const child = childAt(place.child);
        if (references === 'none' || child.kind !== 'page') return undefined;
        const row = loadedRows?.[child.page];
        if (!row || (ownTarget !== null && !namesOthers.has(row.id))) return undefined;
        return row.id === expandedId;
      },
      parent: (index) => {
        const place = layout.place(index);
        if (place.kind !== 'child') return null;
        const child = childAt(place.child);
        const groupRow = layout.rowOfGroup(place.group);
        return child.kind === 'where' ? groupRow + 1 + child.page : groupRow;
      },
      // Enter inspects a page, and opens a group: a page opens with Right, Left and its chevron.
      activates: (index) => layout.place(index).kind !== 'group',
      onToggle: (index, opening) => {
        const place = layout.place(index);
        if (place.kind === 'child') {
          const child = childAt(place.child);
          const row = child.kind === 'page' ? loadedRows?.[child.page] : undefined;
          if (!row || !openKey) return undefined;
          setExpanded(opening ? { group: openKey, page: row.id } : null);
          return index;
        }
        const group = getGroup(place.group);
        if (!group) return undefined;
        if (!opening) {
          onOpenChange(null);
          return place.group;
        }
        const ref = groupRefOf(group);
        const placed: GroupPlace = { index: place.group, group };
        queryClient.setQueryData(queryKeys.issueGroupPlace(crawlId, check.id, ref), placed);
        onOpenChange(ref);
        // Nothing above it is open now, so its row is its place among the groups.
        return place.group;
      },
    }),
    [
      layout,
      openIndex,
      getGroup,
      onOpenChange,
      queryClient,
      crawlId,
      check.id,
      childAt,
      references,
      loadedRows,
      expandedId,
      openKey,
      ownTarget,
      namesOthers,
      setExpanded,
    ],
  );

  /** What Enter (or a double-click) on a row under an expanded page does: inspects the page or URL it names, or goes on. */
  const activateWhere = useCallback(
    (row: Extract<TreeRow, { kind: 'where' }>) => {
      const { where: entry, page } = row;
      if (entry.kind === 'source') onInspect(entry.link.id);
      else if (entry.kind === 'target') onInspect(entry.target.id);
      else if (entry.kind === 'start') onInspect(page.id);
      else if (entry.kind === 'all' && entry.of === 'targets') {
        void navigate({
          to: '/crawls/$crawl/links',
          params: { crawl: crawlId },
          search:
            check.category === 'images' ? imagesOfPage(page.id) : { filter: linksOfUrl('outlinks', page.id, 'all') },
        });
      } else if (entry.kind === 'sitemap') {
        void navigate({
          to: '/crawls/$crawl/sitemaps/{-$sitemap}',
          params: { crawl: crawlId, sitemap: String(entry.sitemap.id) },
        });
      } else if (entry.kind === 'all') {
        void navigate({
          to: '/crawls/$crawl/links',
          params: { crawl: crawlId },
          search: { filter: linksOfUrl('inlinks', page.id, 'all') },
        });
      }
    },
    [onInspect, navigate, crawlId, check.category],
  );

  const showAll = useCallback(
    (group: IssueGroup) =>
      void navigate({
        to: '/crawls/$crawl/urls',
        params: { crawl: crawlId },
        search: { filter: groupFilter(check.id, groupRefOf(group)) },
      }),
    [navigate, crawlId, check.id],
  );

  // Columns share the panel's width: the words get what the figures leave.
  const showRelated =
    grouping === 'relatedUrl' ||
    (groups.firstPage?.rows ?? []).some((group) => group.relatedUrl !== null || group.relatedUrlId !== null);
  // A duplicate cluster is named by what its pages share (the title), not by its first page.
  const shared = (groups.firstPage?.rows ?? []).some((group) => group.sharedText);
  // A check of oversized files: each group's file size, the order its groups come in; an image's own pixels, and the
  // size its pages show it at, each in a column of its own so no figure is ever cut.
  const showSize = grouping === 'relatedUrl' && namesFileSize(check.targetFacts);
  // An image's pixels, where the crawl recorded any: a column of dashes says nothing (A-16).
  const pixelsRecorded = (groups.firstPage?.rows ?? []).some((group) => group.related?.pixelWidth != null);
  const namesPixels = grouping === 'relatedUrl' && (check.targetFacts ?? []).includes('pixelWidth');
  const showPixels = namesPixels && (pixelsRecorded || groups.firstPage === undefined);
  const showShownAt = grouping === 'relatedUrl' && saysShownAt(check);
  const images = groupsAreImages(check);
  // A share bar compares groups: with one group, or groups of one page each, every bar says the same and none says
  // anything the count doesn't.
  const firstGroups = groups.firstPage?.rows ?? [];
  const sharesSaySomething = groupCount !== 1 && firstGroups.some((group) => group.pages > 1);
  // The words get what the figures leave, the comparison's two columns among them (A-02).
  const sized = groupColumnWidths({
    width: width ?? 720,
    images,
    share: sharesSaySomething,
    size: showSize,
    pixels: showPixels,
    shownAt: showShownAt,
    compare: compare !== null,
  });
  const { words: room, pages: pagesWidth, share: shareWidth } = sized;
  const inColumns = useMemo(
    () => [...(showSize ? ['sizeBytes'] : []), ...(showPixels ? ['pixelWidth', 'pixelHeight'] : [])],
    [showSize, showPixels],
  );
  const columns = useMemo<GridColumn<TreeRow>[]>(() => {
    const text = (row: TreeRow) => (row.kind === 'group' ? row.group : null);
    const groupTitle = (row: TreeRow, words: ReactNode, title: string) => (
      <span className="flex min-w-0 items-center" title={title}>
        <span data-tree-toggle aria-hidden="true" className="si-tree-toggle">
          <Icon icon={ChevronRight} size="sm" />
        </span>
        <span className="min-w-0 flex-1 truncate">{words}</span>
        {row.kind === 'group' && row.group.likelyTemplate ? (
          room < NARROW_ROOM ? (
            // In a narrow column the badge gives its room to the group's URL: the callout above says it, and so does
            // this, to a screen reader.
            <span className="sr-only">Likely template</span>
          ) : (
            <Badge variant="outline" icon={Layers} className="ml-2 shrink-0">
              Likely template
            </Badge>
          )
        ) : null}
      </span>
    );
    // The URL, then what's wrong with it: its status where that's the fault, and the check's facts about it.
    const relatedCell = (group: IssueGroup) => {
      // What the pages share names the group: its first page is one of the pages under it, said there.
      if (group.sharedText) return <span className="min-w-0 truncate">{quoted(group.sharedText, 120)}</span>;
      if (!group.relatedUrl) return <span className="text-text-muted">{NO_URL}</span>;
      const relatedId = group.relatedUrlId;
      const status = relatedStatus(group, check);
      const facts = images && room < IMAGE_FACTS_ROOM ? '' : relatedFactsWords(group, check, siteOrigin, inColumns);
      // The URL first, as much of it as fits in three fifths; what it is takes the rest, cut at its end. An image's
      // figures are columns of their own, so its address takes what's left, cut in its middle to keep the file name.
      return (
        <span className="flex min-w-0 items-center gap-2">
          {images ? <GroupThumb group={group} /> : null}
          <IssueUrl
            url={group.relatedUrl}
            siteOrigin={siteOrigin}
            onInspect={relatedId === null ? undefined : () => onInspect(relatedId)}
            // An image is named by its file: its name leads, never cut in the middle.
            label={images ? 'file' : 'address'}
            className={(facts || status) && !images ? 'max-w-3/5 shrink-0' : undefined}
          />
          {status ? (
            <StatusCode code={status.code} {...(status.reason ? { reason: status.reason } : {})} className="shrink-0" />
          ) : null}
          {facts ? (
            <span
              className={
                images
                  ? 'max-w-1/2 min-w-0 shrink-3 truncate text-text-muted'
                  : 'min-w-0 flex-1 truncate text-text-muted'
              }
              title={facts}
            >
              {facts}
            </span>
          ) : null}
        </span>
      );
    };
    const relatedText = (group: IssueGroup) =>
      [
        group.sharedText ? quoted(group.sharedText, 120) : null,
        group.sharedText ? null : group.relatedUrl,
        relatedFactsWords(group, check, siteOrigin),
      ]
        .filter((part) => part)
        .join(FACT_GAP);
    const first: GridColumn<TreeRow>[] =
      grouping === 'relatedUrl'
        ? [
            {
              id: 'relatedUrl',
              header: relatedHeader(check, shared),
              width: room,
              cell: (row) => {
                const group = text(row);
                if (!group) return null;
                return groupTitle(row, relatedCell(group), relatedText(group) || NO_URL);
              },
              text: (row) =>
                row.kind === 'group'
                  ? relatedText(row.group)
                  : row.kind === 'page'
                    ? (row.row.url ?? '')
                    : row.kind === 'where'
                      ? whereRowText(row.where)
                      : '',
            },
          ]
        : [
            {
              id: 'detail',
              header: 'Detail',
              width: showRelated ? Math.round(room * 0.55) : room,
              cell: (row) => {
                const group = text(row);
                if (!group) return null;
                const detail = group.detail ?? noDetailShown(check);
                return groupTitle(row, detail, detail);
              },
              text: (row) =>
                row.kind === 'group'
                  ? (row.group.detail ?? '')
                  : row.kind === 'page'
                    ? (row.row.url ?? '')
                    : row.kind === 'where'
                      ? whereRowText(row.where)
                      : '',
            },
            ...(showRelated
              ? [
                  {
                    id: 'relatedUrl',
                    header: groupUrlHeader(check),
                    width: room - Math.round(room * 0.55),
                    cell: (row: TreeRow) => (row.kind === 'group' ? relatedCell(row.group) : null),
                    text: (row: TreeRow) =>
                      row.kind === 'group' ? relatedText(row.group) : row.kind === 'page' ? (row.row.detail ?? '') : '',
                  },
                ]
              : []),
          ];
    return [
      ...first,
      ...(showPixels
        ? [
            {
              id: 'pixels',
              header: 'Pixels',
              width: sized.pixels,
              numeric: true,
              headerHint: 'The image file’s own width and height, read from the file',
              cell: (row: TreeRow) =>
                row.kind === 'group'
                  ? (pixelsWords(row.group.related?.pixelWidth, row.group.related?.pixelHeight)?.replace(' px', '') ?? (
                      <span className="text-text-muted">–</span>
                    ))
                  : null,
              text: (row: TreeRow) =>
                row.kind === 'group'
                  ? (pixelsWords(row.group.related?.pixelWidth, row.group.related?.pixelHeight)?.replace(' px', '') ??
                    '')
                  : '',
            },
          ]
        : []),
      ...(showShownAt
        ? [
            {
              id: 'shownAt',
              header: 'Shown at',
              width: sized.shownAt,
              numeric: true,
              headerHint:
                'The largest size the group’s first pages show the image at, from their width and height attributes, and how many times wider the file is',
              cell: (row: TreeRow) =>
                row.kind === 'group' ? <ShownAtCell crawlId={crawlId} check={check} group={row.group} /> : null,
              text: () => '',
            },
          ]
        : []),
      ...(showSize
        ? [
            {
              id: 'size',
              header: 'File size',
              width: sized.size,
              numeric: true,
              headerHint: 'The image file’s size where its redirects end: the groups come largest first',
              cell: (row: TreeRow) =>
                row.kind === 'group' ? (
                  typeof row.group.related?.sizeBytes === 'number' ? (
                    formatBytes(row.group.related.sizeBytes)
                  ) : (
                    <span className="text-text-muted">–</span>
                  )
                ) : null,
              // Copied as the number itself, so a spreadsheet can add it up.
              text: (row: TreeRow) =>
                row.kind === 'group' && typeof row.group.related?.sizeBytes === 'number'
                  ? String(row.group.related.sizeBytes)
                  : '',
            },
          ]
        : []),
      {
        id: 'pages',
        header: words.column,
        width: pagesWidth,
        numeric: true,
        cell: (row) => (row.kind === 'group' ? formatCount(row.group.pages) : null),
        text: (row) => (row.kind === 'group' ? String(row.group.pages) : ''),
      },
      // The fix loop: the group's URLs then, and how it moved (New, −3, +1, ±0).
      ...(compare
        ? [
            {
              id: 'then',
              header: changeDate(compare.period.iso) || 'Then',
              width: sized.then,
              numeric: true,
              cell: (row: TreeRow) =>
                row.kind === 'group' && row.group.change
                  ? groupChangeCell(row.group.change, row.group.pages, compare.period).then
                  : null,
              text: (row: TreeRow) =>
                row.kind === 'group' && row.group.change
                  ? groupChangeCell(row.group.change, row.group.pages, compare.period).then
                  : '',
            },
            {
              id: 'move',
              header: 'Change',
              width: sized.move,
              cell: (row: TreeRow) => {
                if (row.kind !== 'group' || !row.group.change) return null;
                const cell = groupChangeCell(row.group.change, row.group.pages, compare.period);
                return (
                  <span>
                    <span aria-hidden="true">{cell.shown}</span>
                    <span className="sr-only">{cell.spoken}</span>
                  </span>
                );
              },
              text: (row: TreeRow) =>
                row.kind === 'group' && row.group.change
                  ? groupChangeCell(row.group.change, row.group.pages, compare.period).shown
                  : '',
            },
          ]
        : []),
      ...(shareWidth > 0
        ? [
            {
              id: 'share',
              header: 'Share of issues',
              width: shareWidth,
              cell: (row: TreeRow) =>
                row.kind === 'group' ? (
                  <ShareBar value={row.group.pages} total={total} label="Share of issues" />
                ) : null,
              text: (row: TreeRow) => (row.kind === 'group' ? String(row.group.share) : ''),
            },
          ]
        : []),
    ];
  }, [
    grouping,
    room,
    showRelated,
    showSize,
    showPixels,
    showShownAt,
    images,
    inColumns,
    pagesWidth,
    shareWidth,
    shared,
    check,
    crawlId,
    siteOrigin,
    total,
    words,
    compare,
    sized,
    onInspect,
  ]);
  // The columns as the user laid them out (resized, moved, pinned), kept for this grid with the preferences.
  const laid = useColumnLayout('issues.groups', columns);

  // A page's issue names what's wrong with each URL in its line ("shown at 400×300 · +1 other URL"): its detail ("2
  // images over 200 KB") says nothing more, so the line gets its room.
  const linesSayDetail = grouping === 'relatedUrl' && references === 'targets' && (check.targetFacts ?? []).length > 0;
  // The open group's pages as columns (the 3b polish: each page's type and "Found on" flowed after its URL, so they
  // started at a different x on every row): the URL as wide as the widest of them takes (a third of the row at least,
  // three fifths at most), then the facts as wide as the widest, then where it's found, which takes the rest. Measured
  // again as the fonts arrive.
  const fontsLoaded = useFontsLoaded();
  const pageColumns = useMemo(() => {
    const measure = linkTextMeasure();
    let url = 0;
    let facts = 0;
    for (const row of loadedRows ?? []) {
      // With the open button the address keeps beside it, and its gap.
      url = Math.max(url, measure(urlDisplay(row.url ?? '', siteOrigin).shown, 'url') + URL_OPEN_ROOM);
      const evidence = evidenceLine(row, check, siteOrigin);
      if (evidence) facts = Math.max(facts, measure(evidence, 'text'));
    }
    const px = (width: number) => `${Math.ceil(width) + 1}px`;
    return [
      references === 'none' ? null : 'calc(var(--chevron-box) - var(--space-2))',
      `minmax(min(${px(url)}, 30%), min(${px(url)}, 45%))`,
      facts > 0 ? `minmax(min(${px(facts)}, 10%), min(${px(facts)}, 30%))` : '0px',
      'minmax(0, 1fr)',
      'fit-content(25%)',
    ]
      .filter((track) => track !== null)
      .join(' ');
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the fonts arriving change what's measured
  }, [loadedRows, check, siteOrigin, references, fontsLoaded]);
  const view = useMemo<ReferenceView>(
    () => ({ crawlId, check, siteOrigin, onInspect }),
    [crawlId, check, siteOrigin, onInspect],
  );
  const spanRow = useCallback(
    (row: TreeRow): ReactNode => {
      if (row.kind === 'group') return null;
      if (row.kind === 'page') {
        const evidence = evidenceLine(row.row, check, siteOrigin);
        const opens = references !== 'none' && (ownTarget === null || namesOthers.has(row.row.id));
        // Every row has every column, an empty one where it has nothing to say, so each starts at the same x.
        return (
          <span className="grid min-w-0 items-center gap-3" style={{ gridTemplateColumns: pageColumns }}>
            {references === 'none' ? null : opens ? (
              <span data-tree-toggle aria-hidden="true" className="si-tree-toggle -me-2">
                <Icon icon={ChevronRight} size="sm" />
              </span>
            ) : (
              <span aria-hidden="true" />
            )}
            <PageUrl
              crawlId={crawlId}
              check={check}
              row={row.row}
              group={row.group}
              siteOrigin={siteOrigin}
              onInspect={onInspect}
            />
            <span className="si-row-evidence min-w-0 truncate text-text-secondary" title={evidence || undefined}>
              {evidence}
            </span>
            {references === 'none' ? (
              <span aria-hidden="true" />
            ) : (
              <PageWhere
                view={view}
                row={row.row}
                first={row.group.relatedUrlId}
                template={row.group.likelyTemplate ? row.group : null}
                opens={opens}
                open={row.row.id === expandedId}
              />
            )}
            {row.row.detail &&
            row.row.detail !== row.group.detail &&
            (!linesSayDetail || detailSaysMore(row.row.detail)) ? (
              // The detail gives way first: the where line beside it says what the page names.
              <span className="min-w-0 truncate text-text-muted" title={row.row.detail}>
                {row.row.detail}
              </span>
            ) : (
              <span aria-hidden="true" />
            )}
          </span>
        );
      }
      if (row.kind === 'where') return <WhereCell row={row} view={view} />;
      const { group } = row;
      // The way to all of the group's rows, in URLs: the link says what it opens (A-13).
      return (
        <span className="flex min-w-0 items-center gap-2 overflow-hidden">
          <TextLink
            to="/crawls/$crawl/urls"
            params={{ crawl: crawlId }}
            search={{ filter: groupFilter(check.id, groupRefOf(group)) }}
            tabIndex={-1}
            className="shrink-0"
          >
            {showInUrlsWords(check, group.pages)}
          </TextLink>
        </span>
      );
    },
    [
      crawlId,
      check,
      siteOrigin,
      references,
      linesSayDetail,
      ownTarget,
      namesOthers,
      onInspect,
      pageColumns,
      view,
      expandedId,
    ],
  );

  // The insight: the open group when it's probably the template's, else the largest group that is.
  const insight =
    openGroup?.group.likelyTemplate === true
      ? openGroup.group
      : (groups.firstPage?.rows.find((group) => group.likelyTemplate) ?? null);

  const rows = layout.rowCount;
  // The row as it was when the menu opened: rows can move under a running crawl while it is open.
  const menuRow = rowMenu.state?.menu.row;
  const shown = groupCount === null ? Math.min(Math.max(total, 1), 6) : Math.max(rows, 1);
  const height = shown * rowHeight + headerHeight + (legend ? GRID_FOOT_HEIGHT : 0) + 2;
  // Where the cursor was when the check was left, in this tree.
  const treeKey = queryKey.join('|');
  const start = checkPlace(crawlId, check.id).cursor;
  const startAt = start?.grid === treeKey ? start : null;

  if (groups.error && groupCount === null) {
    return <ErrorState error={groups.error} onRetry={groups.refetch} compact />;
  }

  return (
    <div className="flex min-h-0 flex-col gap-3">
      {insight ? (
        <div data-template-callout>
          <TemplateCallout
            crawlId={crawlId}
            group={insight}
            total={total}
            check={check}
            siteOrigin={siteOrigin}
            onInspect={onInspect}
          />
        </div>
      ) : null}
      {rowsFailed ? (
        <ErrorState error={firstRows.error} title={words.unread} onRetry={() => void firstRows.refetch()} compact />
      ) : null}
      {namesPixels && !showPixels ? (
        <p className="text-13 text-text-secondary">The images’ pixel sizes weren’t recorded on this crawl.</p>
      ) : null}
      <div
        ref={measureFrame}
        className={evidenceFrameClass(shown)}
        style={{ height, minHeight: Math.min(height, leastFrameHeight(rowHeight, headerHeight, legend)) }}
      >
        <GridCore<TreeRow>
          key={treeKey}
          label={`${check.title}: ${words.named}, grouped by ${grouping === 'relatedUrl' ? lowerFirst(relatedHeader(check, shared)) : 'cause'}`}
          columns={laid.columns}
          columnLayout={laid.columnLayout}
          rowCount={groupCount === null ? null : rows}
          getRow={getRow}
          rowHeight={rowHeight}
          headerHeight={headerHeight}
          onRangeChange={(range) => groups.onRangeChange(toGroups(range))}
          onScrollOffset={groups.onScrollOffset}
          tree={tree}
          fetchRows={fetchRows}
          modelRef={(value) => (model.current = value)}
          spanRow={spanRow}
          onActivate={(_index, row) => {
            if (row?.kind === 'page') onInspect(row.row.id);
            else if (row?.kind === 'where') activateWhere(row);
            else if (row?.kind === 'more') showAll(row.group);
          }}
          onActiveRowChange={(index) => {
            const row = index !== null ? getRow(index) : undefined;
            if (index !== null) {
              keepCheckPlace(crawlId, check.id, {
                cursor: { grid: treeKey, row: index, key: row ? treeRowKey(row) : undefined },
              });
            }
            const followed = row && onFollow ? urlIdOf(row) : null;
            if (followed !== null) onFollow?.(followed);
          }}
          rowKey={treeRowKey}
          {...(startAt
            ? { startRow: startAt.row, ...(startAt.key === undefined ? {} : { startKey: startAt.key }) }
            : {})}
          openUrlOf={(row) => pageOf(row)?.url ?? groupUrlOf(row)?.url ?? null}
          onContextMenu={rowMenu.onContextMenu}
          busy={groups.isRefreshing}
          {...(legend ? { keyHints: ISSUE_KEY_HINTS } : {})}
          className="si-issue-tree flex-1 rounded-md border border-border"
        />
      </div>
      <IssuePageMenu<TreeRow>
        crawlId={crawlId}
        state={rowMenu.state}
        onOpenChange={rowMenu.onOpenChange}
        siteOrigin={siteOrigin}
        page={menuRow ? pageOf(menuRow) : null}
        foundIn={
          menuRow?.kind === 'page' && references === 'sources' ? linksOfUrl('inlinks', menuRow.row.id, 'all') : null
        }
        linksOf={
          menuRow?.kind === 'page' && references === 'targets' && check.category !== 'images'
            ? linksOfUrl('outlinks', menuRow.row.id, 'all')
            : null
        }
        isPage={check.subject !== 'url'}
        imagesIn={menuRow?.kind === 'page' && check.category === 'images' ? imagesOfPage(menuRow.row.id) : null}
        recheck={{ checkId: check.id, idOf: urlIdOf, fullCheck: !recheckable(check) }}
        group={
          menuRow && (menuRow.kind === 'group' || menuRow.kind === 'more')
            ? {
                words: showInUrlsWords(check, menuRow.group.pages),
                filter: groupFilter(check.id, groupRefOf(menuRow.group)),
                urls: menuRow.group.pages,
                url: menuRow.kind === 'group' ? groupUrlOf(menuRow) : null,
                onCopyUrls: () =>
                  void copyUrls({
                    read: checkUrlsReader(crawlId, check.id, groupRefOf(menuRow.group)),
                    expected: menuRow.group.pages,
                  }),
              }
            : null
        }
        urlOf={(row) => pageOf(row)?.url ?? null}
        onInspect={onInspect}
      />
    </div>
  );
}

/** The URL a row stands for, which the inspector follows and the row menu acts on: a page, or one its row names. */
function pageOf(row: TreeRow): { id: number; url: string } | null {
  if (row.kind === 'page') return row.row.url ? { id: row.row.id, url: row.row.url } : null;
  if (row.kind !== 'where') return null;
  const { where } = row;
  if (where.kind === 'source') return { id: where.link.id, url: where.link.url };
  if (where.kind === 'target') return { id: where.target.id, url: where.target.url };
  return row.page.url ? { id: row.page.id, url: row.page.url } : null;
}

function urlIdOf(row: TreeRow): number | null {
  return pageOf(row)?.id ?? null;
}

/** The URL a group is of (the broken link, the image), which its row's menu and Shift+Enter act on; null for any other row. */
function groupUrlOf(row: TreeRow): { id: number; url: string } | null {
  if (row.kind !== 'group' || row.group.sharedText) return null;
  const { relatedUrl, relatedUrlId } = row.group;
  return relatedUrl && relatedUrlId !== null ? { id: relatedUrlId, url: relatedUrl } : null;
}

/**
 * A page's address in its group's row: a link that inspects it. Under a group of the URL its issue names, its open
 * button opens the page at its link to that URL (rule 4), once that link's words are read.
 */
function PageUrl({
  crawlId,
  check,
  row,
  group,
  siteOrigin,
  onInspect,
}: {
  crawlId: string;
  check: Check;
  row: IssueRow;
  group: IssueGroup;
  siteOrigin: string | null;
  onInspect: (urlId: number) => void;
}) {
  const targets = referencesOf(check) === 'targets';
  const where = useWhereFoundOf(crawlId, check, targets ? row.id : null);
  const own = targets ? where.targets?.targets.find((target) => target.id === group.relatedUrlId) : undefined;
  return (
    <IssueUrl
      url={row.url ?? ''}
      siteOrigin={siteOrigin}
      onInspect={() => onInspect(row.id)}
      {...(own && row.url ? openAtLink(row.url, own.text, own) : {})}
    />
  );
}

/**
 * Where a page of an open group is found, in its row, as parts (rule 3): for a URL other pages refer to, the first
 * page linking to it (a link, opened at the link's words), what the link says and where it is, then "+2 more", which
 * opens the rest under the row; for a page under a group of the URL it names, how it links to that URL.
 */
function PageWhere({
  view,
  row,
  first,
  template,
  opens,
  open,
}: {
  view: ReferenceView;
  row: IssueRow;
  /** Under a group of one URL the page names: that URL. */
  first: number | null;
  /** The row's group when it's likely the site's template (one thing on many pages), else null. */
  template: IssueGroup | null;
  /** The row opens to show more. */
  opens: boolean;
  open: boolean;
}) {
  const { crawlId, check, siteOrigin } = view;
  const where = useWhereFoundOf(crawlId, check, row.id);
  // Under a group that's likely the template's: the link its pages share, as its callout reads it (the same read).
  const shared = useTemplateLink(crawlId, check, template);
  const ownLink = first === null ? undefined : where.targets?.targets.find((target) => target.id === first);
  const same = ownLink !== undefined && sameLink(ownLink, shared);
  if (where.error) return <span className="min-w-0 truncate text-text-muted">Where it’s found couldn’t be read</span>;
  if (where.foundOn) {
    const [lead] = foundOnRows(where.foundOn);
    if (!lead) return <span aria-hidden="true" />;
    return (
      <span className="flex min-w-0 items-center gap-3" data-where-found="read">
        <ReferenceOther
          where={lead}
          subject={row}
          view={view}
          lead={lead.kind === 'source' ? 'Linked from' : undefined}
          more={opens ? <MoreToggle count={moreReferences(where)} open={open} /> : null}
        />
        <ReferenceFacts where={lead} view={view} />
      </span>
    );
  }
  if (where.targets) {
    // Under a group of the URL it names, the group's row says the URL: the page's row says its link to it.
    const own = first === null ? undefined : where.targets.targets.find((target) => target.id === first);
    if (own && same && own.id !== row.id && !opens) {
      // The template's one link again: its callout and its group say what and where, once. Not a fault of its own.
      return (
        <span className="min-w-0 truncate text-text-muted" data-where-found="read" data-same-link>
          The same link
        </span>
      );
    }
    if (own) {
      return (
        <span className="flex min-w-0 items-center gap-3" data-where-found="read">
          {own.id === row.id ? <span className="shrink-0 text-text-secondary">A link to {SAME_PAGE}</span> : null}
          {/* How this page links to it: what the URL itself is, its group's row says. */}
          <ReferenceFacts where={{ kind: 'target', target: own }} view={{ check: linkFactsOnly(check), siteOrigin }} />
          {opens ? <MoreToggle count={moreReferences(where)} open={open} /> : null}
        </span>
      );
    }
    const [lead] = targetRows(where.targets, check, siteOrigin);
    if (!lead) return <span aria-hidden="true" />;
    return (
      <span className="flex min-w-0 items-center gap-3" data-where-found="read">
        <ReferenceOther
          where={lead}
          subject={row}
          view={view}
          lead={lead.kind === 'target' ? targetLead(check, lead.target) : undefined}
          more={opens ? <MoreToggle count={moreReferences(where)} open={open} /> : null}
        />
        <ReferenceFacts where={lead} view={view} />
      </span>
    );
  }
  return (
    <span className="min-w-0 truncate text-text-muted" data-where-found="pending">
      {where.references === 'sources' ? 'Finding where it’s linked…' : 'Finding what it names…'}
    </span>
  );
}

/**
 * A row under a page expanded to show where it's found, its parts left-aligned under the page (A-05): the page linking
 * to it, a link like any other, with what the link says and where on that page it is; the sitemap listing it; its
 * being the start URL; a URL the page names, led by how it names it ("Links to", "Embeds", "Canonical to"), with its
 * status; or the way on to all of them.
 */
function WhereCell({ row, view }: { row: Extract<TreeRow, { kind: 'where' }>; view: ReferenceView }) {
  const { where, page } = row;
  const lead =
    where.kind === 'source'
      ? 'Linked from'
      : where.kind === 'target'
        ? targetLead(view.check, where.target)
        : undefined;
  return (
    <span className="flex min-w-0 items-center gap-3">
      <ReferenceOther where={where} subject={page} view={view} lead={lead} />
      <ReferenceFacts where={where} view={view} />
    </span>
  );
}

/**
 * "1 link, to /, in the header, repeated on 260 pages: one fix clears them." The count of things first, so a group of
 * 260 pages never reads as 260 faults (the owner took it for one). Built from what the engine says (`likelyTemplate`,
 * the group's count and the check's total) and the link the group's pages share, never more, and in pages only for a
 * check that flags nothing else.
 */
function TemplateCallout({
  crawlId,
  group,
  total,
  check,
  siteOrigin,
  onInspect,
}: {
  crawlId: string;
  group: IssueGroup;
  total: number;
  check: Check;
  siteOrigin: string | null;
  onInspect: (urlId: number) => void;
}) {
  const grouping = check.grouping;
  // "260 pages" for all of them, "5 of 6 pages" for a part.
  const pages = group.pages < total ? partOfAffected(check, group.pages, total) : affectedCount(check, group.pages);
  // What the shared thing is and where on the pages it is, for a check whose pages hold it: "1 link, to … in the header".
  const thing = templateThingWords(useTemplateLink(crawlId, check, group), pages);
  const facts = relatedFactsWords(group, check, siteOrigin);
  // An oversized image: the size its pages show it at, against the file's.
  const shownAt = useGroupShownAt(crawlId, check, group);
  const relatedId = group.relatedUrlId;
  return (
    <Callout tone="insight" title="Likely in the site’s template">
      {group.sharedText ? (
        <>
          1 text, {quoted(group.sharedText, 120)}, repeated on {pages}: one fix clears them.
        </>
      ) : grouping === 'relatedUrl' && group.relatedUrl ? (
        <span className="flex min-w-0 flex-wrap items-center gap-x-1.5 gap-y-0.5" data-template-thing>
          <span className="font-semibold text-text">{thing.lead}</span>
          {relatedId !== null ? (
            <IssueUrl url={group.relatedUrl} siteOrigin={siteOrigin} onInspect={() => onInspect(relatedId)} />
          ) : (
            <span className="font-mono text-12">{urlDisplay(group.relatedUrl, siteOrigin).shown}</span>
          )}
          <span>
            {facts ? `(${facts}) ` : null}
            {thing.tail}
            {shownAt ? ` It’s ${shownAtInsight(shownAt, group.related, group.pages)}.` : null}
          </span>
        </span>
      ) : grouping === 'relatedUrl' ? (
        <>1 cause, with no URL named, repeated on {pages}: one fix clears them.</>
      ) : (
        <>
          1 cause, “{group.detail ?? NO_DETAIL}”, repeated on {pages}: one fix clears them.
        </>
      )}
    </Callout>
  );
}
