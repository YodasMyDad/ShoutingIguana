import { useQuery } from '@tanstack/react-query';
import { ChevronRight, ExternalLink, Layers, List, PanelRight } from 'lucide-react';
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { api } from '../../api/client';
import type { GridFilter, GridSort, LinkGroup, LinkRow } from '../../api/grids';
import { queryKeys } from '../../api/queries';
import { pixelsWords } from '../../crawl/evidence';
import { GridCore, type GridCopyResult } from '../../grid/GridCore';
import { useGridMetrics } from '../../grid/metrics';
import { RowMenu, UrlRowItems } from '../../grid/RowMenu';
import { copyRowUrls, rowMenuLabel, useRowMenu } from '../../grid/rowMenuState';
import type { GridColumn, GridRange, GridTree, RowFetcher } from '../../grid/types';
import { usePagedRows } from '../../grid/usePagedRows';
import { useElementWidth } from '../../grid/useElementWidth';
import { OUTCOME_WORDS, POSITION_WORDS } from '../../inspector/words';
import { formatBytes, formatCount } from '../../lib/format';
import { openInBrowser } from '../../shell/os';
import { Badge } from '../../ui/Badge';
import { Icon } from '../../ui/Icon';
import { ImageThumb } from '../../ui/ImageThumb';
import { isBrokenImage } from '../../ui/imageFacts';
import { MenuItem, MenuSeparator } from '../../ui/Menu';
import { StatusCode } from '../../ui/StatusCode';
import { Url } from '../../ui/Url';
import { isResourceType, urlDisplay } from '../../ui/urlText';
import { treeLayout, type OpenGroup } from '../issues/groupTree';
import { fittedGroupWidths, groupColumnNeeds } from './groupColumns';
import { linkFollowWords, linkTextMeasure } from './linkColumns';
import {
  FIRST_TARGET_LINKS,
  GROUP_COLUMN_SORTS,
  groupsAsGridPage,
  groupSortOf,
  groupTextLine,
  nextGroupSort,
  otherTextsWords,
  positionsWords,
  showAllLinksWords,
  targetLinksFilters,
  type LinkGroupsGridPage,
} from './linkGroups';

/** A row of the tree: a target, one of the open target's links, or the row that lists all of them. */
export type GroupTreeRow =
  | { kind: 'group'; group: LinkGroup }
  | { kind: 'link'; link: LinkRow; group: LinkGroup }
  | { kind: 'more'; group: LinkGroup };

const groupRows = new WeakMap<LinkGroup, GroupTreeRow>();
const linkRows = new WeakMap<LinkRow, GroupTreeRow>();
const moreRows = new WeakMap<LinkGroup, GroupTreeRow>();

/** The same row object for the same data, so the grid keeps its rows' identity between renders. */
function wrap<K extends object>(cache: WeakMap<K, GroupTreeRow>, key: K, make: () => GroupTreeRow): GroupTreeRow {
  let row = cache.get(key);
  if (!row) {
    row = make();
    cache.set(key, row);
  }
  return row;
}

/** The columns an open target's links are read with: where each is, and what it says. */
const CHILD_COLUMNS = ['from.id', 'from.url', 'text', 'position', 'kind', 'followed', 'noFollow', 'sponsored', 'ugc'];

const FRAME_SLACK = 24;
const WIDTHS: Record<string, number> = {
  status: 128,
  size: 96,
  pixels: 120,
  pages: 112,
  links: 96,
  text: 216,
  where: 256,
};
/**
 * An image view has two columns more (its size and pixels) and a short "Where" (an image sits in the content, mostly):
 * its columns give the image's address the room.
 */
const IMAGE_WIDTHS: Record<string, number> = { ...WIDTHS, status: 96, pages: 96, links: 80, text: 200, where: 152 };

const muted = (text: string) => <span className="text-text-muted">{text}</span>;

/** A target's status: its code, or what happened instead, as the URL grid says it. */
function statusWords(group: LinkGroup): string {
  if (typeof group.target.statusCode === 'number') return String(group.target.statusCode);
  return group.target.outcome ? OUTCOME_WORDS[group.target.outcome] : 'Not crawled';
}

export interface LinkGroupsGridProps {
  crawlId: string;
  filters: readonly GridFilter[];
  search: string | undefined;
  /** The route's sort: a key the grouped view takes, or none for the most linked from pages first. */
  sort: readonly GridSort[];
  onSortChange: (next: GridSort[]) => void;
  siteOrigin: string | null;
  /** The view is of images alone: each row shows its picture, size and pixels, and its text is alt text. */
  images: boolean;
  crawling: boolean;
  /** Opens the inspector on a link's end. */
  onInspect: (urlId: number, end: 'from' | 'to') => void;
  /**
   * While the inspector is open, the end it follows the cursor down (as the flat list's does): a target's row is its
   * target; one of its links is that link's page with `from`, else the target again. Left out while it's closed.
   */
  following?: 'from' | 'to' | undefined;
  /** The view one row a link, of one target's links. */
  onShowLinks: (filters: GridFilter[]) => void;
  /**
   * The view's counts, as each first page is read, for the screen's header: null while none is, and whether the read
   * failed (the grid then shows `errorState`).
   */
  onCounted: (counted: { targets: number; links: number; pages: number } | null, failed: boolean) => void;
  onCopy?: ((result: GridCopyResult) => void) | undefined;
  emptyState: ReactNode;
  loadingNote?: ReactNode;
  /** What shows in the grid's place when its first page couldn't be read: the screen's words for why, and Try again. */
  errorState: (error: unknown, retry: () => void) => ReactNode;
  keyHints?: Parameters<typeof GridCore>[0]['keyHints'];
  minRows?: number | undefined;
}

/**
 * The Links screen grouped by target: one row a target of the view's links, with how many pages and links lead to it,
 * the text they say, where they sit, and whether that's the template; the most linked from pages first. A target opens
 * to its first links (the page each is on, its text, where it sits) and a row that lists them all, one a row. The
 * engine groups (`/links/groups`); the grid reads a page of targets at a time.
 */
export function LinkGroupsGrid({
  crawlId,
  filters,
  search,
  sort,
  onSortChange,
  siteOrigin,
  images,
  crawling,
  onInspect,
  following,
  onShowLinks,
  onCounted,
  onCopy,
  emptyState,
  loadingNote,
  errorState,
  keyHints,
  minRows,
}: LinkGroupsGridProps) {
  const { rowHeight, headerHeight } = useGridMetrics();
  const order = groupSortOf(sort);
  const view = `${crawlId}|${JSON.stringify({ filters, q: search ?? '', order })}`;
  const queryKey = useMemo(
    () => queryKeys.linkGroupsGrid(crawlId, { filters, q: search ?? '', sort: [order] }),
    [crawlId, filters, search, order],
  );
  const fetchPage = useCallback(
    async (offset: number, limit: number, signal: AbortSignal) =>
      groupsAsGridPage(
        await api.linkGroups(
          crawlId,
          { filters: [...filters], sort: [order], offset, limit, ...(search ? { search } : {}) },
          signal,
        ),
      ),
    [crawlId, filters, search, order],
  );
  const groups = usePagedRows<LinkGroup>({ queryKey, fetchPage, rowHeight });
  const groupCount = groups.rowCount;
  const first = groups.firstPage as LinkGroupsGridPage | undefined;
  const counted = first && groupCount !== null ? { targets: groupCount, links: first.links, pages: first.pages } : null;
  const failed = groups.error !== null && groups.error !== undefined && groupCount === null;
  const countedKey = `${counted ? `${counted.targets}|${counted.links}|${counted.pages}` : '-'}|${failed}`;
  // Said again for each view the screen names (its callback is the view's), and whenever the counts change.
  useEffect(() => onCounted(counted, failed), [countedKey, onCounted]); // eslint-disable-line react-hooks/exhaustive-deps

  // The open target: one at a time, of this view, at the place it was opened. A running crawl that moves it closes it.
  const [open, setOpen] = useState<{ view: string; index: number; id: number } | null>(null);
  const openAt = open?.view === view && groups.getRow(open.index)?.id === open.id ? open : null;
  const openGroup = openAt ? groups.getRow(openAt.index) : undefined;
  const childFilters = useMemo(
    () => (openAt ? targetLinksFilters(filters, openAt.id) : null),
    [filters, openAt?.id], // eslint-disable-line react-hooks/exhaustive-deps
  );
  const children = useQuery({
    queryKey: ['crawl', crawlId, 'linkGroupLinks', { filters: childFilters, q: search ?? '' }],
    queryFn: ({ signal }) =>
      api.links(
        crawlId,
        {
          columns: CHILD_COLUMNS,
          filters: childFilters ?? [],
          sort: [],
          limit: FIRST_TARGET_LINKS,
          ...(search ? { search } : {}),
        },
        signal,
      ),
    enabled: childFilters !== null,
  });
  const loaded = children.data?.rows ?? null;
  const openChildren = openGroup ? (loaded ? loaded.length : Math.min(openGroup.links, FIRST_TARGET_LINKS)) + 1 : 0;
  const layout = useMemo(() => {
    const opened: OpenGroup | null = openAt ? { index: openAt.index, children: openChildren } : null;
    return treeLayout(groupCount ?? 0, opened);
  }, [groupCount, openAt, openChildren]);

  const getGroup = groups.getRow;
  const getRow = useCallback(
    (index: number): GroupTreeRow | undefined => {
      const place = layout.place(index);
      if (place.kind === 'group') {
        const group = getGroup(place.group);
        return group ? wrap(groupRows, group, () => ({ kind: 'group', group })) : undefined;
      }
      if (!openGroup) return undefined;
      if (place.child === openChildren - 1)
        return wrap(moreRows, openGroup, () => ({ kind: 'more', group: openGroup }));
      const link = loaded?.[place.child];
      return link ? wrap(linkRows, link, () => ({ kind: 'link', link, group: openGroup })) : undefined;
    },
    [layout, getGroup, openGroup, openChildren, loaded],
  );

  const fetchGroups = groups.fetchRows;
  const fetchRows = useCallback<RowFetcher<GroupTreeRow>>(
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
  const onGroupsRange = groups.onRangeChange;
  const onRangeChange = useCallback(
    (range: GridRange) =>
      onGroupsRange({
        first: layout.place(range.first).group,
        last: layout.place(range.last).group,
        scrubbing: range.scrubbing,
      }),
    [layout, onGroupsRange],
  );

  const tree = useMemo<GridTree>(
    () => ({
      level: (index) => (layout.place(index).kind === 'group' ? 1 : 2),
      expanded: (index) => {
        const place = layout.place(index);
        return place.kind === 'group' ? place.group === openAt?.index : undefined;
      },
      parent: (index) => {
        const place = layout.place(index);
        return place.kind === 'group' ? null : layout.rowOfGroup(place.group);
      },
      // Enter opens and closes a target; on one of its links it inspects the page the link is on.
      activates: (index) => layout.place(index).kind !== 'group',
      onToggle: (index, opening) => {
        const place = layout.place(index);
        if (place.kind !== 'group') return undefined;
        const group = getGroup(place.group);
        if (!group) return undefined;
        setOpen(opening ? { view, index: place.group, id: group.id } : null);
        // Nothing above it is open now, so its row is its place among the targets.
        return place.group;
      },
    }),
    [layout, openAt?.index, getGroup, view],
  );

  const [measure, width] = useElementWidth();
  // A crawl made before images' pixel sizes were read has none: no column of dashes, one line that says so.
  const unmeasured = images && first?.pixelSizes === false;
  const shownColumns = useMemo(
    () => [
      'target',
      'status',
      ...(images ? ['size', ...(unmeasured ? [] : ['pixels'])] : []),
      'pages',
      'links',
      'text',
      'where',
    ],
    [images, unmeasured],
  );
  // The room shared by what the first page's targets, texts and places take, as the flat grid shares it: read once a
  // view, so scrolling never moves a column, and a target's address is never cut while the grid has room for it.
  const groupsInView = first?.rows;
  // The fonts the widths are measured in may still be on their way (an address measured in a fallback monospace is
  // narrower than it draws): measured again as each arrives.
  const [fontsLoaded, setFontsLoaded] = useState(0);
  useEffect(() => {
    if (typeof document.fonts === 'undefined') return undefined;
    let current = true;
    const loaded = () => {
      if (current) setFontsLoaded((count) => count + 1);
    };
    void document.fonts.ready.then(loaded);
    document.fonts.addEventListener('loadingdone', loaded);
    return () => {
      current = false;
      document.fonts.removeEventListener('loadingdone', loaded);
    };
  }, []);
  const needs = useMemo(
    () => groupColumnNeeds(groupsInView ?? [], images, siteOrigin, linkTextMeasure()),
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the fonts arriving change what's measured
    [groupsInView, images, siteOrigin, fontsLoaded],
  );
  const widths = useMemo(
    () => fittedGroupWidths(shownColumns, images ? IMAGE_WIDTHS : WIDTHS, (width ?? 1000) - FRAME_SLACK, needs),
    [shownColumns, images, width, needs],
  );
  const columns = useMemo<GridColumn<GroupTreeRow>[]>(() => {
    const group = (row: GroupTreeRow) => (row.kind === 'group' ? row.group : null);
    const sortable = (
      id: string,
      header: string,
      extra: Pick<GridColumn<GroupTreeRow>, 'cell' | 'text'> & Partial<GridColumn<GroupTreeRow>>,
    ): GridColumn<GroupTreeRow> => {
      const key = GROUP_COLUMN_SORTS[id];
      return {
        id,
        header,
        width: widths[id] ?? 120,
        ...(key
          ? {
              sortable: true,
              ...(order.column === key ? { sort: order.desc ? ('descending' as const) : ('ascending' as const) } : {}),
            }
          : { sortHint: `Targets can’t be sorted by ${header.toLowerCase()}.` }),
        ...extra,
      };
    };
    const all: Record<string, GridColumn<GroupTreeRow>> = {
      target: sortable('target', images ? 'Image' : 'Target', {
        headerHint: images ? 'The image file, once however many pages show it' : 'Where the links lead, once each',
        cell: (row) => {
          const value = group(row);
          if (!value) return null;
          return (
            <span className="flex min-w-0 items-center gap-2">
              <span data-tree-toggle aria-hidden="true" className="si-tree-toggle">
                <Icon icon={ChevronRight} size="sm" />
              </span>
              {images ? <ImageThumb url={value.url} broken={isBrokenImage(value.target)} /> : null}
              <Url
                url={value.url}
                siteOrigin={siteOrigin}
                tabbable={false}
                onGo={() => onInspect(value.id, 'to')}
                // An image, a stylesheet, a script: named by its file, never cut in the middle of its name.
                label={images || isResourceType(value.target.contentType) ? 'file' : 'address'}
                className="min-w-0 flex-1"
              />
            </span>
          );
        },
        text: (row) => group(row)?.url ?? '',
      }),
      status: sortable('status', 'Status', {
        cell: (row) => {
          const value = group(row);
          if (!value) return null;
          if (typeof value.target.statusCode === 'number') return <StatusCode code={value.target.statusCode} />;
          const words = statusWords(value);
          return (
            <span className="flex min-w-0" title={words}>
              <StatusCode code={null} reason={words} />
            </span>
          );
        },
        text: (row) => {
          const value = group(row);
          return value ? statusWords(value) : '';
        },
      }),
      size: sortable('size', 'Size', {
        numeric: true,
        headerHint: 'The file’s size as transferred, after any redirects',
        cell: (row) => {
          const value = group(row);
          if (!value) return null;
          return typeof value.target.sizeBytes === 'number' ? formatBytes(value.target.sizeBytes) : muted('–');
        },
        text: (row) => String(group(row)?.target.sizeBytes ?? ''),
      }),
      pixels: sortable('pixels', 'Pixel size', {
        headerHint: 'The image’s own width and height, from the file',
        cell: (row) => {
          const value = group(row);
          if (!value) return null;
          return pixelsWords(value.target.pixelWidth, value.target.pixelHeight) ?? muted('–');
        },
        text: (row) => {
          const value = group(row);
          return value ? (pixelsWords(value.target.pixelWidth, value.target.pixelHeight) ?? '') : '';
        },
      }),
      pages: sortable('pages', images ? 'Used on' : 'Linking pages', {
        numeric: true,
        headerHint: images
          ? 'The pages that show the image, each once'
          : 'The pages these links are on, each once: how widely one fix reaches',
        cell: (row) => {
          const value = group(row);
          return value ? formatCount(value.pages) : null;
        },
        text: (row) => String(group(row)?.pages ?? ''),
      }),
      links: sortable('links', images ? 'Uses' : 'Links', {
        numeric: true,
        headerHint: images ? 'Every use of the image on those pages' : 'Every link to it on those pages',
        cell: (row) => {
          const value = group(row);
          return value ? formatCount(value.links) : null;
        },
        text: (row) => String(group(row)?.links ?? ''),
      }),
      text: sortable('text', images ? 'Alt text' : 'Anchor text', {
        headerHint: 'What most of its links say, and how many other texts they have',
        cell: (row) => {
          const value = group(row);
          if (!value) return null;
          const others = otherTextsWords(value);
          return (
            <span className="flex min-w-0 items-baseline gap-1.5" title={groupTextLine(value)}>
              <span className="min-w-0 truncate">{value.text ?? muted('None')}</span>
              {others ? <span className="shrink-0 text-text-muted">{others}</span> : null}
            </span>
          );
        },
        text: (row) => {
          const value = group(row);
          return value ? groupTextLine(value) : '';
        },
      }),
      where: sortable('where', 'Where', {
        headerHint: 'Where on their pages the links sit; "Likely template" when it’s on most of the view’s pages',
        cell: (row) => {
          const value = group(row);
          if (!value) return null;
          const words = positionsWords(value.positions);
          return (
            <span className="flex min-w-0 items-center gap-2" title={words}>
              {value.likelyTemplate ? (
                <Badge variant="outline" icon={Layers} className="shrink-0">
                  Likely template
                </Badge>
              ) : null}
              <span className="min-w-0 truncate">{words || muted('–')}</span>
            </span>
          );
        },
        text: (row) => {
          const value = group(row);
          if (!value) return '';
          return [value.likelyTemplate ? 'Likely template' : null, positionsWords(value.positions)]
            .filter((part) => part)
            .join(', ');
        },
      }),
    };
    return shownColumns.flatMap((id) => all[id] ?? []);
  }, [shownColumns, images, order, siteOrigin, onInspect, widths]);

  // An open target's links span the row: the page each is on, what it says, where it sits, whether it's followed.
  const spanRow = useCallback(
    (row: GroupTreeRow): ReactNode => {
      if (row.kind === 'group') return null;
      if (row.kind === 'more') {
        return (
          <span className="flex min-w-0 items-center gap-2 text-accent-text">
            <Icon icon={List} size="sm" />
            <span className="truncate">{showAllLinksWords(row.group.links, images)}</span>
          </span>
        );
      }
      const { link } = row;
      const follow = linkFollowWords(link);
      return (
        <span className="flex min-w-0 items-center gap-3">
          <span className="shrink-0 text-text-muted">on</span>
          <Url
            url={link['from.url'] ?? ''}
            siteOrigin={siteOrigin}
            copyable={false}
            tabbable={false}
            className="min-w-1/4 flex-1"
          />
          <span className="max-w-2/5 min-w-0 shrink truncate" title={link.text ?? ''}>
            {link.text ? `“${link.text}”` : muted(images ? 'No alt text' : 'No text')}
          </span>
          {link.position ? <span className="shrink-0 text-text-secondary">{POSITION_WORDS[link.position]}</span> : null}
          {follow ? <span className="shrink-0 text-text-muted">{follow}</span> : null}
        </span>
      );
    },
    [siteOrigin, images],
  );

  const showAll = (group: LinkGroup) => onShowLinks(targetLinksFilters(filters, group.id));
  const rowMenu = useRowMenu<GroupTreeRow>();
  const menuRow = rowMenu.state?.menu.row;
  const menuGroup = menuRow?.group;
  const menuCount = rowMenu.state?.menu.indices.length ?? 1;
  const menuUrl = menuRow?.kind === 'link' ? menuRow.link['from.url'] : menuGroup?.url;

  if (failed) return <>{errorState(groups.error, groups.refetch)}</>;
  return (
    <div ref={measure} className="flex min-h-0 flex-1 flex-col">
      {unmeasured ? (
        <p className="mb-2 text-13 text-text-muted">Image dimensions weren’t measured in this crawl.</p>
      ) : null}
      <GridCore<GroupTreeRow>
        key={view}
        label={images ? 'Images, one row an image' : 'Links grouped by target'}
        minRows={minRows}
        columns={columns}
        keyHints={keyHints}
        rowNoun={images ? ['image', 'images'] : ['target', 'targets']}
        rowCount={groupCount === null ? null : layout.rowCount}
        getRow={getRow}
        rowHeight={rowHeight}
        headerHeight={headerHeight}
        onRangeChange={onRangeChange}
        onScrollOffset={groups.onScrollOffset}
        fetchRows={fetchRows}
        {...(onCopy ? { onCopy } : {})}
        tree={tree}
        spanRow={spanRow}
        onSort={(column) => onSortChange(nextGroupSort(order, GROUP_COLUMN_SORTS[column] ?? column))}
        // While it's open, the inspector follows the cursor, in place: the target of the row the cursor is on.
        onActiveRowChange={(index) => {
          const row = following && index !== null ? getRow(index) : undefined;
          if (!row) return;
          const page = row.kind === 'link' ? row.link['from.id'] : undefined;
          if (following === 'from' && typeof page === 'number') onInspect(page, 'from');
          else onInspect(row.group.id, 'to');
        }}
        onActivate={(_index, row) => {
          if (row?.kind === 'link' && typeof row.link['from.id'] === 'number') onInspect(row.link['from.id'], 'from');
          else if (row?.kind === 'more') showAll(row.group);
        }}
        openUrlOf={(row) => (row.kind === 'link' ? (row.link['from.url'] ?? null) : row.group.url)}
        rowKey={(row) =>
          row.kind === 'group' ? `g${row.group.id}` : row.kind === 'more' ? `m${row.group.id}` : `l${row.link.id}`
        }
        onContextMenu={rowMenu.onContextMenu}
        busy={groups.isRefreshing && !crawling}
        loadingNote={loadingNote}
        emptyState={emptyState}
        className="flex-1 rounded-lg border border-border-card"
      />
      <RowMenu
        state={rowMenu.state}
        onOpenChange={rowMenu.onOpenChange}
        label={rowMenuLabel(
          menuCount,
          menuUrl ? urlDisplay(menuUrl, siteOrigin).shown : undefined,
          images ? ['image', 'images'] : ['target', 'targets'],
        )}
      >
        <UrlRowItems
          count={menuCount}
          url={menuUrl}
          onCopyUrls={() => {
            if (rowMenu.state)
              void copyRowUrls(rowMenu.state.menu, (row) =>
                row.kind === 'link' ? row.link['from.url'] : row.group.url,
              );
          }}
          onCopyRows={() => void rowMenu.state?.menu.copy()}
          rowNoun={images ? ['image', 'images'] : ['target', 'targets']}
        />
        <MenuSeparator />
        <MenuItem icon={PanelRight} disabled={!menuGroup} onSelect={() => menuGroup && onInspect(menuGroup.id, 'to')}>
          {images ? 'Inspect the image' : 'Inspect the target'}
        </MenuItem>
        <MenuItem
          icon={ExternalLink}
          disabled={!menuGroup || !/^https?:/i.test(menuGroup.url)}
          onSelect={() => {
            if (menuGroup) void openInBrowser(menuGroup.url);
          }}
        >
          {images ? 'Open the image in your browser' : 'Open the target in your browser'}
        </MenuItem>
        <MenuItem icon={List} disabled={!menuGroup} onSelect={() => menuGroup && showAll(menuGroup)}>
          {menuGroup ? showAllLinksWords(menuGroup.links, images) : 'Show its links'}
        </MenuItem>
      </RowMenu>
    </div>
  );
}
