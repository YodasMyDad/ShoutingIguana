import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { ChevronRight, Layers } from 'lucide-react';
import { useCallback, useEffect, useEffectEvent, useMemo, useRef, type ReactNode } from 'react';
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
import { formatCount } from '../../lib/format';
import { revealInScrollParent } from '../../lib/scroll';
import { Badge } from '../../ui/Badge';
import { Callout } from '../../ui/Callout';
import { FilterChip } from '../../ui/FilterChip';
import { Icon } from '../../ui/Icon';
import { ShareBar } from '../../ui/ShareBar';
import { TextLink } from '../../ui/TextLink';
import { Url } from '../../ui/Url';
import { urlDisplay } from '../../ui/urlText';
import { FIRST_GROUP_ROWS, childRows, groupFilter, groupRefOf, treeLayout, type OpenGroup } from './groupTree';
import { IssuePageMenu } from './IssuePageMenu';
import { checkUrlsReader, copyUrls } from './copyUrls';

/** A row of the tree: a group, one of the open group's first pages or URLs, or its link to all of them. */
type TreeRow =
  | { kind: 'group'; group: IssueGroup }
  | { kind: 'page'; row: IssueRow; group: IssueGroup }
  | { kind: 'more'; group: IssueGroup };

/** Wrappers kept per row object, so a row the grid has drawn keeps its identity (and its memoised cells). */
const groupRows = new WeakMap<IssueGroup, TreeRow>();
const pageRows = new WeakMap<IssueRow, TreeRow>();
const moreRows = new WeakMap<IssueGroup, TreeRow>();

/** A group's identity: its detail and its related URL, as the engine keys it. */
const groupKey = (group: IssueGroup) => `${group.detail ?? ''}|${group.relatedUrlId ?? ''}`;

/** A row's identity in the tree, so the cursor stays on it when the rows are read again (an analysis). */
function treeRowKey(row: TreeRow): string {
  switch (row.kind) {
    case 'group':
      return `g|${groupKey(row.group)}`;
    case 'page':
      return `p|${groupKey(row.group)}|${row.row.id}|${row.row.detail ?? ''}|${row.row.relatedUrlId ?? ''}`;
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
    ...issueGroupRowsQuery(crawlId, check.id, openRef ?? { detail: null, relatedUrlId: null }, FIRST_GROUP_ROWS),
    enabled: openGroup !== null,
  });
  const loadedRows = firstRows.data?.rows ?? null;
  // Its first pages couldn't be read: no placeholders that would never fill, only its link to all of them, and the
  // error above the grid with Try again.
  const rowsFailed = firstRows.isError && loadedRows === null;
  const openIndex = openGroup?.index ?? null;
  const openChildren = openGroup ? (rowsFailed ? 1 : childRows(openGroup.group, loadedRows?.length ?? null)) : 0;

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
      if (place.child === openChildren - 1) return wrap(moreRows, group, () => ({ kind: 'more', group }));
      const row = loadedRows?.[place.child];
      return row ? wrap(pageRows, row, () => ({ kind: 'page', row, group })) : undefined;
    },
    [layout, getGroup, openGroup, openChildren, loadedRows],
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
        const placed: GroupPlace = { index: place.group, group };
        queryClient.setQueryData(queryKeys.issueGroupPlace(crawlId, check.id, ref), placed);
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
                const detail = group.detail ?? NO_DETAIL_SHOWN;
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
  }, [grouping, room, showRelated, siteOrigin, total, words]);
  // The columns as the user laid them out (resized, moved, pinned), kept for this grid with the preferences.
  const laid = useColumnLayout('issues.groups', columns);

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
    [crawlId, check, grouping, siteOrigin],
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
      {insight ? <TemplateCallout group={insight} total={total} check={check} siteOrigin={siteOrigin} /> : null}
      {rowsFailed ? (
        <ErrorState error={firstRows.error} title={words.unread} onRetry={() => void firstRows.refetch()} compact />
      ) : null}
      <div ref={measureFrame} className="flex max-h-(--evidence-max-height) min-h-0 flex-col" style={{ height }}>
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
            else if (row?.kind === 'more') showAll(row.group);
          }}
          onActiveRowChange={(index) => {
            const row = onFollow && index !== null ? getRow(index) : undefined;
            if (row?.kind === 'page') onFollow?.(row.row.id);
          }}
          rowKey={treeRowKey}
          onContextMenu={rowMenu.onContextMenu}
          busy={groups.isRefreshing}
          className="flex-1 rounded-md border border-border"
        />
      </div>
      <IssuePageMenu<TreeRow>
        crawlId={crawlId}
        state={rowMenu.state}
        onOpenChange={rowMenu.onOpenChange}
        siteOrigin={siteOrigin}
        page={menuRow?.kind === 'page' && menuRow.row.url ? { id: menuRow.row.id, url: menuRow.row.url } : null}
        group={
          menuRow && menuRow.kind !== 'page'
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
        urlOf={(row) => (row.kind === 'page' ? row.row.url : null)}
        onInspect={onInspect}
      />
    </div>
  );
}

/**
 * "The same related URL on 264 of 265 pages, so it's likely in the site's template: one fix clears the group." Built
 * from what the engine says (`likelyTemplate`, the group's count and the check's total), never more, and in pages only
 * for a check that flags nothing else.
 */
function TemplateCallout({
  group,
  total,
  check,
  siteOrigin,
}: {
  group: IssueGroup;
  total: number;
  check: Check;
  siteOrigin: string | null;
}) {
  const grouping = check.grouping;
  const share = partOfAffected(check, group.pages, total);
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
          The same detail, “{group.detail ?? NO_DETAIL}”, on {share}, so it’s likely in the site’s template: one fix
          clears the group.
        </>
      )}
    </Callout>
  );
}
