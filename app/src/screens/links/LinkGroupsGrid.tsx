import { useQueries, useQuery, useQueryClient } from '@tanstack/react-query';
import { ChevronRight, ClipboardList, Copy, ExternalLink, Layers, List, PanelRight } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState, type MouseEvent, type ReactNode } from 'react';
import { api } from '../../api/client';
import type { GridFilter, GridPage, GridSort, LinkGroup, LinkRow } from '../../api/grids';
import { queryKeys } from '../../api/queries';
import { pixelsWords } from '../../crawl/evidence';
import { GridCore, type GridCopyResult } from '../../grid/GridCore';
import { useGridMetrics } from '../../grid/metrics';
import { RowMenu } from '../../grid/RowMenu';
import { copyRowUrls, rowMenuLabel, rowsWords, useRowMenu } from '../../grid/rowMenuState';
import type { GridColumn, GridRange, GridTree, RowFetcher } from '../../grid/types';
import { usePagedRows } from '../../grid/usePagedRows';
import { useElementWidth } from '../../grid/useElementWidth';
import { OUTCOME_WORDS, POSITION_WORDS } from '../../inspector/words';
import { formatBytes, formatCount, formatCountOf } from '../../lib/format';
import { useFontsLoaded } from '../../lib/useFontsLoaded';
import type { InspectShow } from '../../routing/inspector';
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
import { MEASURED_GROUPS, fittedGroupWidths, groupColumnNeeds } from './groupColumns';
import { OPEN_AT_LINK_HINT, linkFollowWords, linkTextMeasure, sourceAtLink } from './linkColumns';
import {
  FIRST_TARGET_LINKS,
  GROUP_COLUMN_SORTS,
  groupsAsGridPage,
  groupSortOf,
  groupTextLine,
  linkedFromLine,
  morePagesWords,
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
const CHILD_COLUMNS = [
  'from.id',
  'from.url',
  'text',
  'position',
  'kind',
  'imageLink',
  'followed',
  'noFollow',
  'sponsored',
  'ugc',
];

/** The columns a target's first link is read with, for its row: the page it's on, and its words to open the page at. */
const FIRST_LINK_COLUMNS = ['from.id', 'from.url', 'text', 'kind', 'imageLink'];

/** What reads a target's first link among the view's: one link, on the links' index by target. */
interface FirstLinkContext {
  crawlId: string;
  filters: readonly GridFilter[];
  search: string | undefined;
  crawling: boolean;
}

/**
 * The read of the first of the view's links to a target, in crawl order (the one an open target lists first): the
 * page that links to it, for the target's own row. The engine walks only that target's links for it.
 */
function firstLinkQuery({ crawlId, filters, search, crawling }: FirstLinkContext, targetId: number) {
  const named = targetLinksFilters(filters, targetId);
  return {
    queryKey: ['crawl', crawlId, 'linkGroupFirst', { filters: named, q: search ?? '' }] as const,
    queryFn: ({ signal }: { signal: AbortSignal }): Promise<GridPage<LinkRow>> =>
      api.links(
        crawlId,
        { columns: FIRST_LINK_COLUMNS, filters: named, sort: [], limit: 1, ...(search ? { search } : {}) },
        signal,
      ),
    // A crawl that stands still links to a target from the same page first, however often its row is drawn.
    staleTime: crawling ? 0 : Number.POSITIVE_INFINITY,
  };
}

/**
 * A target's "Linked from": the page that holds the link, a link like any other URL (it inspects the page at this
 * link, and its open-in-browser button opens the page at the link's words), then "+3 more" where more pages link to
 * it, which opens the target to them in place. The fix round's rule 3: never a count behind a chevron.
 */
function LinkedFrom({
  group,
  context,
  siteOrigin,
  onInspect,
}: {
  group: LinkGroup;
  context: FirstLinkContext;
  siteOrigin: string | null;
  onInspect: LinkGroupsGridProps['onInspect'];
}) {
  const first = useQuery(firstLinkQuery(context, group.id));
  const link = first.data?.rows[0];
  const page = link?.['from.url'];
  const pageId = link?.['from.id'];
  const more = morePagesWords(group.pages);
  const atLink = link ? sourceAtLink(link) : undefined;
  return (
    <span className="flex min-w-0 items-center gap-2">
      {typeof page === 'string' ? (
        <Url
          url={page}
          siteOrigin={siteOrigin}
          copyable={false}
          tabbable={false}
          {...(atLink ? { openUrl: atLink, openHint: OPEN_AT_LINK_HINT } : {})}
          {...(typeof pageId === 'number'
            ? { onGo: () => onInspect(pageId, 'from', { section: 'outlinks', link: group.id }) }
            : {})}
        />
      ) : first.isPending ? (
        <span aria-hidden="true" className="skeleton-static block h-(--skeleton-text) w-3/4" />
      ) : (
        // The page couldn't be read: how many there are still can be, and the row still opens to them.
        <span className="text-text-muted">{formatCountOf(group.pages, 'page')}</span>
      )}
      {more ? (
        <span data-tree-toggle className="si-link shrink-0" title={`Show the ${formatCount(group.pages)} pages`}>
          {more}
        </span>
      ) : null}
    </span>
  );
}

const FRAME_SLACK = 24;
/** How far the pointer may move between its press and its release and still be a click on a row, px. */
const DRAG_SLOP = 4;
const WIDTHS: Record<string, number> = {
  target: 420,
  status: 128,
  size: 96,
  pixels: 120,
  pages: 280,
  links: 80,
  text: 216,
  where: 256,
};
/**
 * An image view has two columns more (its size and pixels) and a short "Where" (an image sits in the content, mostly):
 * its columns give the image's address the room.
 */
const IMAGE_WIDTHS: Record<string, number> = { ...WIDTHS, status: 96, links: 72, text: 200, where: 152 };

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
  /**
   * Opens the inspector on a link's end. `show` is what it arrives on: the page's outlinks or the target's inlinks,
   * with the link's other end marked. Left out where the cursor moves an inspector that's already open.
   */
  onInspect: (urlId: number, end: 'from' | 'to', show?: InspectShow) => void;
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
 * the text they say, where they sit, and whether that's the template; the most linked from pages first. The page that
 * holds the link is in the target's row, a link like any other URL (the first of them, and "+3 more"). A target opens
 * on a click anywhere on its row to its first links (the page each is on, its text, where it sits) and a row that
 * lists them all in the flat list. The engine groups (`/links/groups`); the grid reads a page of targets at a time.
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
  const queryClient = useQueryClient();
  const order = groupSortOf(sort);
  const view = `${crawlId}|${JSON.stringify({ filters, q: search ?? '', order })}`;
  const firstLinks = useMemo<FirstLinkContext>(
    () => ({ crawlId, filters, search, crawling }),
    [crawlId, filters, search, crawling],
  );
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
  const fontsLoaded = useFontsLoaded();
  // The page that links to each of those targets, as their rows read it (the same reads, by the same keys): measured
  // once they've all come, so "Linked from" takes its room in one step, not a row at a time.
  const measured = useMemo(() => (groupsInView ?? []).slice(0, MEASURED_GROUPS), [groupsInView]);
  const firstPages = useQueries({
    queries: measured.map((target) => firstLinkQuery(firstLinks, target.id)),
    combine: (reads) =>
      reads.every((read) => !read.isPending) ? reads.map((read) => read.data?.rows[0]?.['from.url'] ?? null) : null,
  });
  const needs = useMemo(
    () => {
      const pageOf = firstPages ? new Map(measured.map((target, index) => [target.id, firstPages[index]])) : null;
      return groupColumnNeeds(
        measured,
        images,
        siteOrigin,
        linkTextMeasure(),
        pageOf ? (target) => pageOf.get(target.id) : undefined,
      );
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the fonts arriving change what's measured
    [measured, firstPages, images, siteOrigin, fontsLoaded],
  );
  const widths = useMemo(
    () => fittedGroupWidths(shownColumns, images ? IMAGE_WIDTHS : WIDTHS, (width ?? 1000) - FRAME_SLACK, needs),
    [shownColumns, images, width, needs],
  );
  /** The one page that links to a target, where it has one and it's read: the link the inspector marks on arriving. */
  const onlyPageOf = useCallback(
    (group: LinkGroup): { link?: number } => {
      if (group.pages !== 1) return {};
      const read = queryClient.getQueryData<GridPage<LinkRow>>(firstLinkQuery(firstLinks, group.id).queryKey);
      const page = read?.rows[0]?.['from.id'];
      return typeof page === 'number' ? { link: page } : {};
    },
    [queryClient, firstLinks],
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
                copyable={false}
                tabbable={false}
                // The target arrives on who links to it: its one page marked, where it has only the one.
                onGo={() => onInspect(value.id, 'to', { section: 'inlinks', ...onlyPageOf(value) })}
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
      pages: sortable('pages', images ? 'Used on' : 'Linked from', {
        headerHint: images
          ? 'The page that shows the image, and how many more do. Sorts by how many pages.'
          : 'The page the link is on, and how many more link to it: how widely one fix reaches. Sorts by how many pages.',
        cell: (row) => {
          const value = group(row);
          return value ? (
            <LinkedFrom group={value} context={firstLinks} siteOrigin={siteOrigin} onInspect={onInspect} />
          ) : null;
        },
        // A copy takes the page as far as it's read: the first, and how many more.
        text: (row) => {
          const value = group(row);
          if (!value) return '';
          const read = queryClient.getQueryData<GridPage<LinkRow>>(firstLinkQuery(firstLinks, value.id).queryKey);
          return linkedFromLine(read?.rows[0]?.['from.url'], value.pages);
        },
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
  }, [shownColumns, images, order, siteOrigin, onInspect, widths, firstLinks, queryClient, onlyPageOf]);

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
      const pageId = link['from.id'];
      const atLink = sourceAtLink(link);
      return (
        <span className="flex min-w-0 items-center gap-3">
          <span className="shrink-0 text-text-muted">on</span>
          <Url
            url={link['from.url'] ?? ''}
            siteOrigin={siteOrigin}
            copyable={false}
            tabbable={false}
            // The page that holds the link: a link like any other URL, opening at the link itself.
            {...(atLink ? { openUrl: atLink, openHint: OPEN_AT_LINK_HINT } : {})}
            {...(typeof pageId === 'number'
              ? { onGo: () => onInspect(pageId, 'from', { section: 'outlinks', link: row.group.id }) }
              : {})}
            // As wide as its address: what the link says comes straight after it, never across the row from it.
            className="min-w-1/4 shrink"
          />
          <span className="max-w-2/5 min-w-0 shrink truncate" title={link.text ?? ''}>
            {link.text ? `“${link.text}”` : muted(images ? 'No alt text' : 'No text')}
          </span>
          {link.position ? <span className="shrink-0 text-text-secondary">{POSITION_WORDS[link.position]}</span> : null}
          {follow ? <span className="shrink-0 text-text-muted">{follow}</span> : null}
        </span>
      );
    },
    [siteOrigin, images, onInspect],
  );

  const showAll = (group: LinkGroup) => onShowLinks(targetLinksFilters(filters, group.id));
  const rowMenu = useRowMenu<GroupTreeRow>();
  const menuRow = rowMenu.state?.menu.row;
  const menuGroup = menuRow?.group;
  const menuCount = rowMenu.state?.menu.indices.length ?? 1;
  // One of an open target's links: the menu has the page it's on too, by name.
  const menuLink = menuRow?.kind === 'link' ? menuRow.link : undefined;
  const menuPage = menuLink?.['from.url'];
  const menuPageId = menuLink?.['from.id'];
  const menuUrl = menuLink ? menuPage : menuGroup?.url;
  const targetNoun: [string, string] = images ? ['image', 'images'] : ['target', 'targets'];

  /**
   * A click anywhere on a target's row opens or closes it (the chevron is where that shows, not the only place to
   * press). Its links, its buttons and the chevron keep their own press; a click that extends the selection is the
   * selection's.
   */
  const pressedAt = useRef<{ x: number; y: number } | null>(null);
  const onRowClick = (event: MouseEvent<HTMLDivElement>) => {
    if (event.button !== 0 || event.detail > 1 || event.shiftKey || event.ctrlKey || event.metaKey) return;
    const target = event.target instanceof Element ? event.target : null;
    if (!target || target.closest('button, a, [data-tree-toggle]')) return;
    // The end of a drag over the row's text is a selection, not a press.
    const from = pressedAt.current;
    if (from && Math.hypot(event.clientX - from.x, event.clientY - from.y) > DRAG_SLOP) return;
    const cell = target.closest<HTMLElement>('[role="row"][aria-level="1"] [data-row]');
    if (!cell) return;
    const index = Number(cell.dataset.row);
    const expanded = tree.expanded(index);
    if (expanded !== undefined) tree.onToggle(index, !expanded);
  };
  /** The click has opened or closed the row already: a double-click's own toggle would undo it. */
  const onRowDoubleClick = (event: MouseEvent<HTMLDivElement>) => {
    if (event.target instanceof Element && event.target.closest('[role="row"][aria-level="1"]')) {
      event.stopPropagation();
    }
  };

  if (failed) return <>{errorState(groups.error, groups.refetch)}</>;
  return (
    // The handlers are the rows' (a press anywhere on a target's row), on the element that holds the grid: the grid's
    // own keys do the same from the keyboard (Enter, Right and Left).
    // eslint-disable-next-line jsx-a11y-x/no-static-element-interactions, jsx-a11y-x/click-events-have-key-events
    <div
      ref={measure}
      className="flex min-h-0 flex-1 flex-col"
      onPointerDownCapture={(event) => {
        pressedAt.current = { x: event.clientX, y: event.clientY };
      }}
      onClick={onRowClick}
      onDoubleClickCapture={onRowDoubleClick}
    >
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
          // The link the cursor is on is marked, on the tab the reader has open.
          if (following === 'from' && typeof page === 'number') onInspect(page, 'from', { link: row.group.id });
          else onInspect(row.group.id, 'to', typeof page === 'number' ? { link: page } : undefined);
        }}
        onActivate={(_index, row) => {
          if (row?.kind === 'link' && typeof row.link['from.id'] === 'number') {
            onInspect(row.link['from.id'], 'from', { section: 'outlinks', link: row.group.id });
          } else if (row?.kind === 'more') showAll(row.group);
        }}
        openUrlOf={(row) => (row.kind === 'link' ? (row.link['from.url'] ?? null) : row.group.url)}
        rowKey={(row) =>
          row.kind === 'group' ? `g${row.group.id}` : row.kind === 'more' ? `m${row.group.id}` : `l${row.link.id}`
        }
        onContextMenu={rowMenu.onContextMenu}
        busy={groups.isRefreshing && !crawling}
        loadingNote={loadingNote}
        emptyState={emptyState}
        // A big grid: a URL's open-in-browser button shows on the row under the pointer and the cursor's.
        className="si-open-on-row si-link-groups flex-1 rounded-lg border border-border-card"
      />
      <RowMenu
        state={rowMenu.state}
        onOpenChange={rowMenu.onOpenChange}
        label={rowMenuLabel(menuCount, menuUrl ? urlDisplay(menuUrl, siteOrigin).shown : undefined, targetNoun)}
      >
        {/* Every item names the end it's for: the page a line's link is on, then the target. */}
        {menuLink ? (
          <>
            <MenuItem
              icon={PanelRight}
              shortcut="enter"
              disabled={typeof menuPageId !== 'number'}
              onSelect={() => {
                if (typeof menuPageId === 'number' && menuGroup) {
                  onInspect(menuPageId, 'from', { section: 'outlinks', link: menuGroup.id });
                }
              }}
            >
              Inspect the source page
            </MenuItem>
            <MenuItem
              icon={ExternalLink}
              shortcut="shift+enter"
              disabled={!menuPage || !/^https?:/i.test(menuPage)}
              onSelect={() => {
                if (menuPage) void openInBrowser(sourceAtLink(menuLink) ?? menuPage);
              }}
            >
              Open the source page in your browser
            </MenuItem>
            <MenuItem
              icon={Copy}
              disabled={!menuPage}
              onSelect={() => {
                if (rowMenu.state) {
                  void copyRowUrls(rowMenu.state.menu, (row) => (row.kind === 'link' ? row.link['from.url'] : null));
                }
              }}
            >
              {`Copy ${rowsWords(menuCount, ['source URL', 'source URLs'])}`}
            </MenuItem>
            <MenuSeparator />
          </>
        ) : null}
        <MenuItem
          icon={PanelRight}
          disabled={!menuGroup}
          onSelect={() => {
            if (!menuGroup) return;
            const page = typeof menuPageId === 'number' ? { link: menuPageId } : onlyPageOf(menuGroup);
            onInspect(menuGroup.id, 'to', { section: 'inlinks', ...page });
          }}
        >
          {images ? 'Inspect the image' : 'Inspect the target'}
        </MenuItem>
        <MenuItem
          icon={ExternalLink}
          {...(menuLink ? {} : { shortcut: 'shift+enter' })}
          disabled={!menuGroup || !/^https?:/i.test(menuGroup.url)}
          onSelect={() => {
            if (menuGroup) void openInBrowser(menuGroup.url);
          }}
        >
          {images ? 'Open the image in your browser' : 'Open the target in your browser'}
        </MenuItem>
        <MenuItem
          icon={Copy}
          disabled={!menuGroup}
          onSelect={() => {
            if (rowMenu.state) void copyRowUrls(rowMenu.state.menu, (row) => row.group.url);
          }}
        >
          {`Copy ${rowsWords(menuLink ? 1 : menuCount, images ? ['image URL', 'image URLs'] : ['target URL', 'target URLs'])}`}
        </MenuItem>
        {menuLink ? null : (
          <MenuItem icon={ClipboardList} shortcut="mod+c" onSelect={() => void rowMenu.state?.menu.copy()}>
            {`Copy ${rowsWords(menuCount, targetNoun)}`}
          </MenuItem>
        )}
        <MenuSeparator />
        <MenuItem icon={List} disabled={!menuGroup} onSelect={() => menuGroup && showAll(menuGroup)}>
          {menuGroup ? showAllLinksWords(menuGroup.links, images) : 'Show its links'}
        </MenuItem>
      </RowMenu>
    </div>
  );
}
