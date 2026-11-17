import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { ChevronRight, Layers } from 'lucide-react';
import { useCallback, useEffect, useEffectEvent, useMemo, useRef, useState, type ReactNode } from 'react';
import { api } from '../../api/client';
import type { CheckGroupRef, IssueGroup, IssueRow } from '../../api/grids';
import {
  LOCATE_GROUP_LIMIT,
  groupsAsGridPage,
  issueGroupPlaceQuery,
  issueGroupRowsQuery,
  queryKeys,
  type GroupPlace,
} from '../../api/queries';
import type { Check } from '../../api/types';
import { affectedWords, partOfAffected, showInUrlsWords } from '../../crawl/checkSubject';
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
import { FACT_GAP, evidenceValueWords, namesFileSize, quoted, relatedFactParts } from '../../crawl/evidence';
import { formatBytes, formatCount } from '../../lib/format';
import { revealInScrollParent } from '../../lib/scroll';
import { Badge } from '../../ui/Badge';
import { Callout } from '../../ui/Callout';
import { FilterChip } from '../../ui/FilterChip';
import { Icon } from '../../ui/Icon';
import { ShareBar } from '../../ui/ShareBar';
import { TextLink } from '../../ui/TextLink';
import { Url } from '../../ui/Url';
import { urlDisplay } from '../../ui/urlText';
import { followWords } from '../../inspector/links';
import { OUTCOME_WORDS } from '../../inspector/words';
import { StatusCode } from '../../ui/StatusCode';
import { linksOfUrl } from '../links/linkPlaces';
import {
  FIRST_GROUP_ROWS,
  childPlace,
  childRows,
  groupFilter,
  groupRefOf,
  pageChildren,
  treeLayout,
  type ChildPlace,
  type OpenGroup,
  type OpenPage,
} from './groupTree';
import { IssuePageMenu } from './IssuePageMenu';
import { checkUrlsReader, copyUrls } from './copyUrls';
import { evidenceOf, groupRowColumns } from './evidenceColumns';
import { useTemplateLink, useWhereFoundOf } from './useWhereFound';
import { WhereFoundLine } from './WhereFoundLine';
import {
  allWords,
  foundOnRows,
  linkPlaceWords,
  referencesOf,
  targetRows,
  templateLinkWords,
  whereRowText,
  type WhereRow,
} from './whereFound';
import { evidenceFrameClass } from './evidenceFrame';

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

const PAGES_WIDTH = 88;
const SHARE_WIDTH = 176;
/** A group's file size, for a check of oversized files: "1.8 MB". */
const SIZE_WIDTH = 104;
/** A group's words narrower than this: its "Likely template" badge is its icon. */
const NARROW_ROOM = 420;
const MIN_TEXT_WIDTH = 200;
/** The frame's edges and the room a scrollbar may take. */
const FRAME_SLACK = 24;
/** A group without a detail, in the grid's Detail column: the chips' "Detail is none", in a cell's case. */
const NO_DETAIL_SHOWN = 'None';
/** A group's chip this short keeps its words beside the check's. */
const SHORT_CHIP = 32;

/** What the "Related URL" means here is the check's to say; the column says only that it's the other URL involved. */
function relatedWords(group: IssueGroup, siteOrigin: string | null): string {
  return group.relatedUrl ? urlDisplay(group.relatedUrl, siteOrigin).shown : 'No other URL';
}

/** The first column's header for a check grouped by URL: the image, the shared title, or the other URL. */
function relatedHeader(check: Check, shared: boolean): string {
  if (shared) {
    const evidence = evidenceOf(check);
    if (evidence.includes('title')) return 'Shared title';
    if (evidence.includes('description')) return 'Shared description';
    if (evidence.includes('h1')) return 'Shared H1';
    return 'Shared text';
  }
  return check.id.startsWith('image-') ? 'Image' : 'Related URL';
}

/**
 * What a group's related URL is, as the check's `targetFacts` say it: "1.8 MB · JPEG · 4000×3000 px", "→ /new/ 200",
 * "noindex". Empty for a group without one, or a sidecar from before the evidence.
 */
function relatedFactsWords(
  group: IssueGroup,
  check: Check,
  siteOrigin: string | null,
  /** The file's size has a column of its own: the line says the rest. */
  sizeShown = false,
): string {
  if (!group.related || !check.targetFacts?.length) return '';
  const facts = sizeShown ? check.targetFacts.filter((fact) => fact !== 'sizeBytes') : check.targetFacts;
  return relatedFactParts(group.related, facts, siteOrigin).join(FACT_GAP);
}

/**
 * The related URL's status where it's what's wrong (a 404, a redirect, no answer): a 2xx is said by nothing, as most
 * groups' URLs answer, and the check's facts then say what's wrong instead (its size, its being noindex).
 */
function relatedStatus(group: IssueGroup, check: Check): { code: number | null; reason?: string } | null {
  const related = group.related;
  if (!related || !(check.targetFacts ?? []).includes('statusCode')) return null;
  if (related.statusCode !== null) return related.statusCode >= 300 ? { code: related.statusCode } : null;
  return related.outcome ? { code: null, reason: OUTCOME_WORDS[related.outcome] } : null;
}

/** A page's evidence in one line, where its column headers aren't beside it: "→ /b/ 302 → /c/ 200", "“Sofas” · 12 inlinks". */
function evidenceLine(row: IssueRow, check: Check, siteOrigin: string | null): string {
  const values = row as unknown as Record<string, string | number | boolean | object | null | undefined>;
  return evidenceOf(check)
    .map((column) => evidenceValueWords(column, values[column], siteOrigin))
    .filter((words): words is string => words !== null)
    .join(FACT_GAP);
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
}: IssueGroupsProps) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { rowHeight, headerHeight } = useGridMetrics();
  const [measure, width] = useElementWidth();
  const grouping = check.grouping;
  // The same object for every check of one subject, so what's memoised on it holds.
  const words = affectedWords(check);

  const queryKey = useMemo(() => queryKeys.issueGroupsGrid(crawlId, check.id), [crawlId, check.id]);
  const fetchPage = useCallback(
    async (offset: number, limit: number, signal: AbortSignal) =>
      groupsAsGridPage(await api.issueGroups(crawlId, check.id, { offset, limit }, signal)),
    [crawlId, check.id],
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
  const [expanded, setExpanded] = useState<ExpandedPage | null>(null);
  const openKey = openGroup ? groupKey(openGroup.group) : null;
  const expandedId = openKey !== null && expanded?.group === openKey ? expanded.page : null;
  const expandedAt = expandedId !== null && loadedRows ? loadedRows.findIndex((row) => row.id === expandedId) : -1;
  const where = useWhereFoundOf(crawlId, check, expandedAt >= 0 ? expandedId : null);
  const whereRows = useMemo<WhereRow[] | null>(() => {
    if (where.foundOn) return foundOnRows(where.foundOn);
    if (where.targets) return targetRows(where.targets, check, siteOrigin);
    if (where.error) return [{ kind: 'nothing', words: 'Where it’s found couldn’t be read' }];
    return null;
  }, [where.foundOn, where.targets, where.error, check, siteOrigin]);
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
  const measureFrame = useCallback(
    (element: HTMLDivElement | null) => {
      frame.current = element;
      measure(element);
    },
    [measure],
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
        return row ? row.id === expandedId : undefined;
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
    ],
  );

  /** What Enter (or a double-click) on a row under an expanded page does: inspects the page or URL it names, or goes on. */
  const activateWhere = useCallback(
    (row: Extract<TreeRow, { kind: 'where' }>) => {
      const { where: entry, page } = row;
      if (entry.kind === 'source') onInspect(entry.link.id);
      else if (entry.kind === 'target') onInspect(entry.target.id);
      else if (entry.kind === 'start' || (entry.kind === 'all' && entry.of === 'targets')) onInspect(page.id);
      else if (entry.kind === 'sitemap') {
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
    [onInspect, navigate, crawlId],
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
  // A check of oversized files: each group's file size, the order its groups come in.
  const showSize = grouping === 'relatedUrl' && namesFileSize(check.targetFacts);
  const room = Math.max(
    MIN_TEXT_WIDTH,
    (width ?? 720) - PAGES_WIDTH - SHARE_WIDTH - (showSize ? SIZE_WIDTH : 0) - FRAME_SLACK,
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
          // In a narrow column the badge is its icon, so the group's URL keeps the room: the words are its name.
          <span className="ml-2 shrink-0" title={room < NARROW_ROOM ? 'Likely template' : undefined}>
            <Badge variant="outline" icon={Layers}>
              {room < NARROW_ROOM ? <span className="sr-only">Likely template</span> : 'Likely template'}
            </Badge>
          </span>
        ) : null}
      </span>
    );
    // The URL, then what's wrong with it: its status where that's the fault, and the check's facts about it.
    const relatedCell = (group: IssueGroup) => {
      if (group.sharedText) {
        return (
          <span className="flex min-w-0 items-baseline gap-2">
            <span className="min-w-0 shrink truncate">{quoted(group.sharedText, 120)}</span>
            {group.relatedUrl ? (
              <Url
                url={group.relatedUrl}
                siteOrigin={siteOrigin}
                copyable={false}
                tabbable={false}
                className="max-w-1/3 min-w-0 shrink text-text-muted"
              />
            ) : null}
          </span>
        );
      }
      if (!group.relatedUrl) return <span className="text-text-muted">No other URL</span>;
      const status = relatedStatus(group, check);
      const facts = relatedFactsWords(group, check, siteOrigin, showSize);
      // The URL first, as much of it as fits in three fifths; what it is takes the rest, cut at its end.
      return (
        <span className="flex min-w-0 items-center gap-2">
          <Url
            url={group.relatedUrl}
            siteOrigin={siteOrigin}
            copyable={false}
            tabbable={false}
            className={facts || status ? 'max-w-3/5 min-w-0 shrink-0' : 'min-w-0 flex-1'}
          />
          {status ? (
            <StatusCode code={status.code} {...(status.reason ? { reason: status.reason } : {})} className="shrink-0" />
          ) : null}
          {facts ? (
            <span className="si-group-facts min-w-0 flex-1 truncate text-text-muted" title={facts}>
              {facts}
            </span>
          ) : null}
        </span>
      );
    };
    const relatedText = (group: IssueGroup) =>
      [
        group.sharedText ? quoted(group.sharedText, 120) : null,
        group.relatedUrl,
        relatedFactsWords(group, check, siteOrigin, showSize),
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
                return groupTitle(row, relatedCell(group), relatedText(group) || 'No other URL');
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
                const detail = group.detail ?? NO_DETAIL_SHOWN;
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
                    header: 'Related URL',
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
      ...(showSize
        ? [
            {
              id: 'size',
              header: 'File size',
              width: SIZE_WIDTH,
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
        width: PAGES_WIDTH,
        numeric: true,
        cell: (row) => (row.kind === 'group' ? formatCount(row.group.pages) : null),
        text: (row) => (row.kind === 'group' ? String(row.group.pages) : ''),
      },
      {
        id: 'share',
        header: 'Share of issues',
        width: SHARE_WIDTH,
        cell: (row) =>
          row.kind === 'group' ? <ShareBar value={row.group.pages} total={total} label="Share of issues" /> : null,
        text: (row) => (row.kind === 'group' ? String(row.group.share) : ''),
      },
    ];
  }, [grouping, room, showRelated, showSize, shared, check, siteOrigin, total, words]);
  // The columns as the user laid them out (resized, moved, pinned), kept for this grid with the preferences.
  const laid = useColumnLayout('issues.groups', columns);

  const spanRow = useCallback(
    (row: TreeRow): ReactNode => {
      if (row.kind === 'group') return null;
      if (row.kind === 'page') {
        const evidence = evidenceLine(row.row, check, siteOrigin);
        return (
          <span className="flex min-w-0 items-center gap-3">
            {references === 'none' ? null : (
              <span data-tree-toggle aria-hidden="true" className="si-tree-toggle -me-2">
                <Icon icon={ChevronRight} size="sm" />
              </span>
            )}
            <Url
              url={row.row.url ?? ''}
              siteOrigin={siteOrigin}
              copyable={false}
              tabbable={false}
              className="min-w-1/3 flex-1"
            />
            {evidence ? (
              <span className="si-row-evidence max-w-2/5 min-w-0 shrink truncate text-text-secondary" title={evidence}>
                {evidence}
              </span>
            ) : null}
            {references === 'none' ? null : (
              <WhereFoundLine
                crawlId={crawlId}
                check={check}
                urlId={row.row.id}
                siteOrigin={siteOrigin}
                first={row.group.relatedUrlId}
                className="max-w-1/2 shrink"
              />
            )}
            {row.row.detail && row.row.detail !== row.group.detail ? (
              // The detail gives way first: the where line beside it says what the page names.
              <span className="max-w-1/4 min-w-0 shrink-3 truncate text-text-muted" title={row.row.detail}>
                {row.row.detail}
              </span>
            ) : null}
            <Icon icon={ChevronRight} size="sm" className="shrink-0 text-text-muted" />
          </span>
        );
      }
      if (row.kind === 'where') return <WhereCell row={row} crawlId={crawlId} siteOrigin={siteOrigin} />;
      const { group } = row;
      // The group's own parts, as the URL grid's chips will read them: its related URL, or its detail and any URL.
      const parts =
        grouping === 'relatedUrl'
          ? [{ field: 'Related URL', value: relatedWords(group, siteOrigin) }]
          : [
              { field: 'Detail', value: group.detail ?? NO_DETAIL },
              ...(group.relatedUrl ? [{ field: 'Related URL', value: relatedWords(group, siteOrigin) }] : []),
            ];
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
          <FilterChip field="Check" value={check.title} variant="preview" size="sm" truncate />
          {/* The group's chips are usually short ("/", "HTTP 404"): they keep their words, and the check's gives way. */}
          {parts.map((part) => (
            <FilterChip
              key={part.field}
              field={part.field}
              value={part.value}
              variant="preview"
              size="sm"
              truncate
              className={part.value.length <= SHORT_CHIP ? 'shrink-0' : undefined}
            />
          ))}
        </span>
      );
    },
    [crawlId, check, grouping, siteOrigin, references],
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
  const height = shown * rowHeight + headerHeight + 2;

  if (groups.error && groupCount === null) {
    return <ErrorState error={groups.error} onRetry={groups.refetch} compact />;
  }

  return (
    <div className="flex flex-col gap-3">
      {insight ? (
        <TemplateCallout crawlId={crawlId} group={insight} total={total} check={check} siteOrigin={siteOrigin} />
      ) : null}
      {rowsFailed ? (
        <ErrorState error={firstRows.error} title={words.unread} onRetry={() => void firstRows.refetch()} compact />
      ) : null}
      <div ref={measureFrame} className={evidenceFrameClass(shown)} style={{ height }}>
        <GridCore<TreeRow>
          key={queryKey.join('|')}
          label={`${check.title}: ${words.named}, grouped by ${grouping === 'relatedUrl' ? 'related URL' : 'detail'}`}
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
            const row = onFollow && index !== null ? getRow(index) : undefined;
            const followed = row ? urlIdOf(row) : null;
            if (followed !== null) onFollow?.(followed);
          }}
          rowKey={treeRowKey}
          openUrlOf={(row) => pageOf(row)?.url ?? null}
          onContextMenu={rowMenu.onContextMenu}
          busy={groups.isRefreshing}
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
        group={
          menuRow && (menuRow.kind === 'group' || menuRow.kind === 'more')
            ? {
                words: showInUrlsWords(check, menuRow.group.pages),
                filter: groupFilter(check.id, groupRefOf(menuRow.group)),
                urls: menuRow.group.pages,
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

/**
 * A row under a page expanded to show where it's found: the page linking to it with where on that page the link is,
 * what it says and whether it's followed; the sitemap listing it; its being the start URL; a URL the page names, with
 * its status; or the way on to all of them.
 */
function WhereCell({
  row,
  crawlId,
  siteOrigin,
}: {
  row: Extract<TreeRow, { kind: 'where' }>;
  crawlId: string;
  siteOrigin: string | null;
}) {
  const { where, page } = row;
  switch (where.kind) {
    case 'source': {
      const place = linkPlaceWords(where.link);
      const follow = followWords(where.link);
      return (
        <span className="si-where-row flex min-w-0 items-center gap-3" data-where="source">
          <span className="shrink-0 text-text-muted">Linked from</span>
          <Url
            url={where.link.url}
            siteOrigin={siteOrigin}
            copyable={false}
            tabbable={false}
            className="min-w-0 flex-1"
          />
          {place ? (
            <span className="max-w-1/2 shrink truncate text-text-muted" title={place}>
              {place}
            </span>
          ) : null}
          {follow ? <span className="shrink-0 text-text-muted">{follow}</span> : null}
        </span>
      );
    }
    case 'target': {
      // The check's facts where it says them (evidence), else where on the page the link is.
      const place = where.facts ?? linkPlaceWords(where.target);
      return (
        <span className="si-where-row flex min-w-0 items-center gap-3" data-where="target">
          <span className="shrink-0 text-text-muted">Names</span>
          <Url
            url={where.target.url}
            siteOrigin={siteOrigin}
            copyable={false}
            tabbable={false}
            className="min-w-0 flex-1"
          />
          <StatusCode code={where.target.statusCode} className="shrink-0" />
          {place ? (
            <span className="max-w-3/5 shrink truncate text-text-muted" title={place}>
              {place}
            </span>
          ) : null}
        </span>
      );
    }
    case 'sitemap':
      return (
        <span className="si-where-row flex min-w-0 items-center gap-3" data-where="sitemap">
          <span className="shrink-0 text-text-muted">Listed in the sitemap</span>
          <Url
            url={where.sitemap.url}
            siteOrigin={siteOrigin}
            copyable={false}
            tabbable={false}
            className="min-w-0 flex-1"
          />
        </span>
      );
    case 'start':
      return (
        <span className="si-where-row text-text-muted" data-where="start">
          The URL the crawl started from
        </span>
      );
    case 'all':
      return (
        <span className="si-where-row flex min-w-0 items-center" data-where="all">
          {where.of === 'links' ? (
            <TextLink
              to="/crawls/$crawl/links"
              params={{ crawl: crawlId }}
              search={{ filter: linksOfUrl('inlinks', page.id, 'all') }}
              tabIndex={-1}
            >
              {allWords(where)}
            </TextLink>
          ) : (
            <span className="text-text-muted">{allWords(where)}</span>
          )}
        </span>
      );
    case 'nothing':
      return (
        <span className="si-where-row text-text-muted" data-where="nothing">
          {where.words}
        </span>
      );
  }
}

/**
 * "The same related URL on 264 of 265 pages, so it's likely in the site's template: one fix clears the group." Built
 * from what the engine says (`likelyTemplate`, the group's count and the check's total), never more, and in pages only
 * for a check that flags nothing else.
 */
function TemplateCallout({
  crawlId,
  group,
  total,
  check,
  siteOrigin,
}: {
  crawlId: string;
  group: IssueGroup;
  total: number;
  check: Check;
  siteOrigin: string | null;
}) {
  const grouping = check.grouping;
  const share = partOfAffected(check, group.pages, total);
  // Where on the pages the shared link is, for a check whose pages hold it: "It’s a link in the footer."
  const place = templateLinkWords(useTemplateLink(crawlId, check, group));
  const facts = relatedFactsWords(group, check, siteOrigin);
  return (
    <Callout tone="insight" title="Likely in the site’s template">
      {group.sharedText ? (
        <>
          The same text, {quoted(group.sharedText, 120)}, on {share}, so it’s likely in the site’s template: one fix
          clears the group.
        </>
      ) : grouping === 'relatedUrl' && group.relatedUrl ? (
        <>
          The same URL, <span className="font-mono text-12">{urlDisplay(group.relatedUrl, siteOrigin).shown}</span>
          {facts ? ` (${facts})` : null}, on {share}, so it’s likely in the site’s template: one fix clears the group.
          {place ? ` ${place}.` : null}
        </>
      ) : grouping === 'relatedUrl' ? (
        <>
          The same issue on {share}, naming no other URL, so it’s likely in the site’s template: one fix clears the
          group.
        </>
      ) : (
        <>
          The same detail, “{group.detail ?? NO_DETAIL}”, on {share}, so it’s likely in the site’s template: one fix
          clears the group.
        </>
      )}
    </Callout>
  );
}
