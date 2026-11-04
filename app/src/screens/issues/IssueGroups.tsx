import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { ChevronRight, Layers } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, type ReactNode } from 'react';
import { api } from '../../api/client';
import type { CheckGroupRef, IssueGroup, IssueRow } from '../../api/grids';
import { groupsAsGridPage, issueGroupRowsQuery, queryKeys } from '../../api/queries';
import type { Check } from '../../api/types';
import { ErrorState } from '../../feedback/ErrorState';
import { GridCore } from '../../grid/GridCore';
import { useGridMetrics } from '../../grid/metrics';
import type { RowWindowModel } from '../../grid/rowWindow';
import type { GridColumn, GridRange, GridTree } from '../../grid/types';
import { useElementWidth } from '../../grid/useElementWidth';
import { usePagedRows } from '../../grid/usePagedRows';
import { formatCount } from '../../lib/format';
import { Badge } from '../../ui/Badge';
import { Callout } from '../../ui/Callout';
import { FilterChip } from '../../ui/FilterChip';
import { Icon } from '../../ui/Icon';
import { ShareBar } from '../../ui/ShareBar';
import { TextLink } from '../../ui/TextLink';
import { Url } from '../../ui/Url';
import { urlDisplay } from '../../ui/urlText';
import {
  FIRST_GROUP_ROWS,
  childRows,
  groupFilter,
  groupRefOf,
  sameGroup,
  treeLayout,
  type OpenGroup,
} from './groupTree';

/** A row of the tree: a group, one of the open group's first pages, or its link to all of them. */
type TreeRow =
  | { kind: 'group'; group: IssueGroup }
  | { kind: 'page'; row: IssueRow; group: IssueGroup }
  | { kind: 'more'; group: IssueGroup };

/** Wrappers kept per row object, so a row the grid has drawn keeps its identity (and its memoised cells). */
const groupRows = new WeakMap<IssueGroup, TreeRow>();
const pageRows = new WeakMap<IssueRow, TreeRow>();
const moreRows = new WeakMap<IssueGroup, TreeRow>();

function wrap<K extends object>(cache: WeakMap<K, TreeRow>, key: K, make: () => TreeRow): TreeRow {
  let row = cache.get(key);
  if (!row) {
    row = make();
    cache.set(key, row);
  }
  return row;
}

/** Where one of a check's groups is: read in pages of 1,000 from the largest, as far as 50,000 groups. */
const LOCATE_PAGE = 1_000;
const LOCATE_PAGES = 50;

const PAGES_WIDTH = 88;
const SHARE_WIDTH = 176;
const MIN_TEXT_WIDTH = 200;
/** The frame's edges and the room a scrollbar may take. */
const FRAME_SLACK = 24;
/** A group's chip this short keeps its words beside the check's. */
const SHORT_CHIP = 32;

/** What the "Related URL" means here is the check's to say; the column says only that it's the other URL involved. */
function relatedWords(group: IssueGroup, siteOrigin: string | null): string {
  return group.relatedUrl ? urlDisplay(group.relatedUrl, siteOrigin).shown : 'No other URL';
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
}

/**
 * A check's affected pages grouped by their cause (E3a, Issue groups): one treegrid row per group, largest first, with
 * its detail or related URL, pages, and share drawn as a bar. Opening a group shows its first pages and "Show all N
 * pages in URLs", which opens the URL grid filtered to it. The groups are paged and virtualised like any grid, as a
 * big crawl's duplicate titles can make thousands. A group that's probably in the site's template gets the callout.
 */
export function IssueGroups({ crawlId, check, total, siteOrigin, open, onOpenChange, onInspect }: IssueGroupsProps) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { rowHeight, headerHeight } = useGridMetrics();
  const [measure, width] = useElementWidth();
  const grouping = check.grouping;

  const queryKey = useMemo(() => queryKeys.issueGroupsGrid(crawlId, check.id), [crawlId, check.id]);
  const fetchPage = useCallback(
    async (offset: number, limit: number, signal: AbortSignal) =>
      groupsAsGridPage(await api.issueGroups(crawlId, check.id, { offset, limit }, signal)),
    [crawlId, check.id],
  );
  const groups = usePagedRows<IssueGroup>({ queryKey, fetchPage, rowHeight });
  const groupCount = groups.rowCount;

  // The open group: where it is among the groups, read from the pages loaded, else looked for from the largest. A
  // group opened here is placed at once (setQueryData below), so only a route from elsewhere has to look.
  const openRef = open ? groupRefOf(open) : null;
  const placement = useQuery({
    queryKey: queryKeys.issueGroupPlace(crawlId, check.id, openRef),
    queryFn: async ({ signal }): Promise<{ index: number; group: IssueGroup } | null> => {
      if (!openRef) return null;
      for (let page = 0; page < LOCATE_PAGES; page++) {
        const answer = await api.issueGroups(
          crawlId,
          check.id,
          { offset: page * LOCATE_PAGE, limit: LOCATE_PAGE },
          signal,
        );
        const at = answer.groups.findIndex((group) => sameGroup(groupRefOf(group), openRef));
        const group = answer.groups[at];
        if (group) return { index: page * LOCATE_PAGE + at, group };
        if (answer.groups.length < LOCATE_PAGE) return null;
      }
      return null;
    },
    enabled: openRef !== null && groupCount !== null,
    staleTime: Infinity,
  });
  const openGroup = openRef ? (placement.data ?? null) : null;

  const firstRows = useQuery({
    ...issueGroupRowsQuery(crawlId, check.id, openRef ?? { detail: null, relatedUrlId: null }, FIRST_GROUP_ROWS),
    enabled: openGroup !== null,
  });
  const loadedRows = firstRows.data?.rows ?? null;
  const openIndex = openGroup?.index ?? null;
  const openChildren = openGroup ? childRows(openGroup.group, loadedRows?.length ?? null) : 0;

  // A group the route opens (a link, back or forward) may be thousands of rows down: bring it into view, with as many
  // of its pages as fit. One opened here is already in view, so this moves nothing.
  const model = useRef<RowWindowModel | null>(null);
  useEffect(() => {
    if (openIndex === null) return;
    model.current?.scrollToIndex(openIndex + openChildren);
    model.current?.scrollToIndex(openIndex);
  }, [openIndex, openChildren, groupCount]);
  const layout = useMemo(() => {
    const opened: OpenGroup | null = openIndex === null ? null : { index: openIndex, children: openChildren };
    return treeLayout(groupCount ?? 0, opened);
  }, [groupCount, openIndex, openChildren]);
  const getGroup = groups.getRow;

  const getRow = useCallback(
    (index: number): TreeRow | undefined => {
      const place = layout.place(index);
      if (place.kind === 'group') {
        const group = getGroup(place.group);
        return group ? wrap(groupRows, group, () => ({ kind: 'group', group })) : undefined;
      }
      if (!openGroup) return undefined;
      const { group } = openGroup;
      if (place.child === openChildren - 1) return wrap(moreRows, group, () => ({ kind: 'more', group }));
      const row = loadedRows?.[place.child];
      return row ? wrap(pageRows, row, () => ({ kind: 'page', row, group })) : undefined;
    },
    [layout, getGroup, openGroup, openChildren, loadedRows],
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
      level: (index) => (layout.place(index).kind === 'group' ? 1 : 2),
      expanded: (index) => {
        const place = layout.place(index);
        return place.kind === 'group' ? place.group === openIndex : undefined;
      },
      parent: (index) => {
        const place = layout.place(index);
        return place.kind === 'child' ? layout.rowOfGroup(place.group) : null;
      },
      onToggle: (index, opening) => {
        const place = layout.place(index);
        const group = getGroup(place.group);
        if (!group) return undefined;
        if (!opening) {
          onOpenChange(null);
          return place.group;
        }
        const ref = groupRefOf(group);
        queryClient.setQueryData(queryKeys.issueGroupPlace(crawlId, check.id, ref), {
          index: place.group,
          group,
        });
        onOpenChange(ref);
        // Nothing above it is open now, so its row is its place among the groups.
        return place.group;
      },
    }),
    [layout, openIndex, getGroup, onOpenChange, queryClient, crawlId, check.id],
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
  const room = Math.max(MIN_TEXT_WIDTH, (width ?? 720) - PAGES_WIDTH - SHARE_WIDTH - FRAME_SLACK);
  const columns = useMemo<GridColumn<TreeRow>[]>(() => {
    const text = (row: TreeRow) => (row.kind === 'group' ? row.group : null);
    const groupTitle = (row: TreeRow, words: ReactNode, title: string) => (
      <span className="flex min-w-0 items-center" title={title}>
        <span data-tree-toggle aria-hidden="true" className="si-tree-toggle">
          <Icon icon={ChevronRight} size="sm" />
        </span>
        <span className="min-w-0 flex-1 truncate">{words}</span>
        {row.kind === 'group' && row.group.likelyTemplate ? (
          <Badge variant="outline" icon={Layers} className="ml-2 shrink-0">
            Likely template
          </Badge>
        ) : null}
      </span>
    );
    const relatedCell = (group: IssueGroup) =>
      group.relatedUrl ? (
        <Url url={group.relatedUrl} siteOrigin={siteOrigin} copyable={false} openable={false} />
      ) : (
        <span className="text-text-muted">No other URL</span>
      );
    const first: GridColumn<TreeRow>[] =
      grouping === 'relatedUrl'
        ? [
            {
              id: 'relatedUrl',
              header: 'Related URL',
              width: room,
              cell: (row) => {
                const group = text(row);
                if (!group) return null;
                return groupTitle(row, relatedCell(group), group.relatedUrl ?? 'No other URL');
              },
              text: (row) =>
                row.kind === 'group' ? (row.group.relatedUrl ?? '') : row.kind === 'page' ? (row.row.url ?? '') : '',
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
                const detail = group.detail ?? 'No detail';
                return groupTitle(row, detail, detail);
              },
              text: (row) =>
                row.kind === 'group' ? (row.group.detail ?? '') : row.kind === 'page' ? (row.row.url ?? '') : '',
            },
            ...(showRelated
              ? [
                  {
                    id: 'relatedUrl',
                    header: 'Related URL',
                    width: room - Math.round(room * 0.55),
                    cell: (row: TreeRow) => (row.kind === 'group' ? relatedCell(row.group) : null),
                    text: (row: TreeRow) =>
                      row.kind === 'group'
                        ? (row.group.relatedUrl ?? '')
                        : row.kind === 'page'
                          ? (row.row.detail ?? '')
                          : '',
                  },
                ]
              : []),
          ];
    return [
      ...first,
      {
        id: 'pages',
        header: 'Pages',
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
  }, [grouping, room, showRelated, siteOrigin, total]);

  const spanRow = useCallback(
    (row: TreeRow): ReactNode => {
      if (row.kind === 'group') return null;
      if (row.kind === 'page') {
        return (
          <span className="flex min-w-0 items-center gap-3">
            <Url
              url={row.row.url ?? ''}
              siteOrigin={siteOrigin}
              copyable={false}
              openable={false}
              className="min-w-0 flex-1"
            />
            {row.row.detail && row.row.detail !== row.group.detail ? (
              <span className="max-w-1/2 shrink-0 truncate text-text-muted" title={row.row.detail}>
                {row.row.detail}
              </span>
            ) : null}
            <Icon icon={ChevronRight} size="sm" className="shrink-0 text-text-muted" />
          </span>
        );
      }
      const { group } = row;
      const pages = group.pages === 1 ? 'Show the page in URLs' : `Show all ${formatCount(group.pages)} pages in URLs`;
      const groupWords = grouping === 'relatedUrl' ? relatedWords(group, siteOrigin) : (group.detail ?? 'none');
      return (
        <span className="flex min-w-0 items-center gap-2 overflow-hidden">
          <TextLink
            to="/crawls/$crawl/urls"
            params={{ crawl: crawlId }}
            search={{ filter: groupFilter(check.id, groupRefOf(group)) }}
            tabIndex={-1}
            className="shrink-0"
          >
            {pages}
          </TextLink>
          <FilterChip field="Check" value={check.title} variant="preview" size="sm" truncate />
          {/* The group's chip is usually short ("/", "HTTP 404"): it keeps its words, and the check's gives way. */}
          <FilterChip
            field={grouping === 'relatedUrl' ? 'Related URL' : 'Detail'}
            value={groupWords}
            variant="preview"
            size="sm"
            truncate
            className={groupWords.length <= SHORT_CHIP ? 'shrink-0' : undefined}
          />
        </span>
      );
    },
    [crawlId, check.id, check.title, grouping, siteOrigin],
  );

  // The insight: the open group when it's probably the template's, else the largest group that is.
  const insight =
    openGroup?.group.likelyTemplate === true
      ? openGroup.group
      : (groups.firstPage?.rows.find((group) => group.likelyTemplate) ?? null);

  const rows = layout.rowCount;
  const shown = groupCount === null ? Math.min(Math.max(total, 1), 6) : Math.max(rows, 1);
  const height = shown * rowHeight + headerHeight + 2;

  if (groups.error && groupCount === null) {
    return <ErrorState error={groups.error} onRetry={groups.refetch} compact />;
  }

  return (
    <div className="flex flex-col gap-3">
      {insight ? <TemplateCallout group={insight} total={total} grouping={grouping} siteOrigin={siteOrigin} /> : null}
      <div ref={measure} className="flex max-h-(--evidence-max-height) min-h-0 flex-col" style={{ height }}>
        <GridCore<TreeRow>
          key={queryKey.join('|')}
          label={`${check.title}: affected pages, grouped by ${grouping === 'relatedUrl' ? 'related URL' : 'detail'}`}
          columns={columns}
          rowCount={groupCount === null ? null : rows}
          getRow={getRow}
          rowHeight={rowHeight}
          headerHeight={headerHeight}
          onRangeChange={(range) => groups.onRangeChange(toGroups(range))}
          onScrollOffset={groups.onScrollOffset}
          tree={tree}
          modelRef={(value) => (model.current = value)}
          spanRow={spanRow}
          onActivate={(_index, row) => {
            if (row?.kind === 'page') onInspect(row.row.id);
            else if (row?.kind === 'more') showAll(row.group);
          }}
          busy={groups.isRefreshing}
          className="flex-1 rounded-md border border-border"
        />
      </div>
    </div>
  );
}

/**
 * "The same related URL on 264 of 265 pages, so it's likely in the site's template: one fix clears the group." Built
 * from what the engine says (`likelyTemplate`, the group's pages and the check's total), never more.
 */
function TemplateCallout({
  group,
  total,
  grouping,
  siteOrigin,
}: {
  group: IssueGroup;
  total: number;
  grouping: Check['grouping'];
  siteOrigin: string | null;
}) {
  const share = `${formatCount(group.pages)} of ${formatCount(total)} pages`;
  return (
    <Callout tone="insight" title="Likely in the site’s template">
      {grouping === 'relatedUrl' && group.relatedUrl ? (
        <>
          The same URL, <span className="font-mono text-12">{urlDisplay(group.relatedUrl, siteOrigin).shown}</span>, on{' '}
          {share}, so it’s likely in the site’s template: one fix clears the group.
        </>
      ) : grouping === 'relatedUrl' ? (
        <>
          The same issue on {share}, naming no other URL, so it’s likely in the site’s template: one fix clears the
          group.
        </>
      ) : (
        <>
          The same detail, “{group.detail ?? 'none'}”, on {share}, so it’s likely in the site’s template: one fix clears
          the group.
        </>
      )}
    </Callout>
  );
}
