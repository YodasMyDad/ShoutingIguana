// The Links screen grouped by target (step 3b): one row a target, the engine's `/links/groups`. What the route's sort
// means in each mode, what a grouped row says in words, and which views are of images. Pure, so each is unit-tested.
import type { GridFilter, GridPage, GridSort, LinkGroup, LinkGroupsPage, LinkPosition } from '../../api/grids';
import type { GridKeyHint } from '../../grid/gridFoot';
import { pixelsWords } from '../../crawl/evidence';
import { POSITION_WORDS } from '../../inspector/words';
import { formatBytes, formatCount, formatCountOf } from '../../lib/format';

/** The engine's own keys of a grouped view's order, beside the target's columns. */
export const GROUP_PAGES = 'pages';
export const GROUP_LINKS = 'links';

/** The order a grouped view is in when the route names none: the target the most pages link to first. */
export const DEFAULT_GROUP_SORT: GridSort = { column: GROUP_PAGES, desc: true };

/** The grouped grid's columns, by id, with the key each sorts by (none for those that don't). */
export const GROUP_COLUMN_SORTS: Readonly<Record<string, string | undefined>> = {
  target: 'to.url',
  status: 'to.statusCode',
  size: 'to.sizeBytes',
  pixels: undefined,
  pages: GROUP_PAGES,
  links: GROUP_LINKS,
  text: undefined,
  where: undefined,
};

/** Keys that read best the largest first: a press on their header sorts them descending first. */
const LARGEST_FIRST: ReadonlySet<string> = new Set([GROUP_PAGES, GROUP_LINKS, 'to.sizeBytes']);

/** Whether a sort key is one the grouped view takes: its own counts, or a column of the target. */
export function isGroupSort(column: string): boolean {
  return column === GROUP_PAGES || column === GROUP_LINKS || column.startsWith('to.');
}

/** Whether a sort key is one the flat grid takes: a link's order (`id`), or a column of the target. */
export function isFlatSort(column: string): boolean {
  return column === 'id' || column.startsWith('to.');
}

/**
 * The route's sort as the other mode reads it, when the view switches between one row a link and one row a target: a
 * sort by a column of the target stays (both sort by it), one only the other mode has is dropped, and the view then
 * takes that mode's own order (crawl order, or the most linked from pages first).
 */
export function sortForMode(sort: readonly GridSort[] | undefined, grouped: boolean): GridSort[] | undefined {
  const kept = (sort ?? []).filter((key) => (grouped ? isGroupSort(key.column) : isFlatSort(key.column)));
  return kept.length > 0 ? kept : undefined;
}

/** The order a grouped view reads by: the route's key it takes, or the default. */
export function groupSortOf(sort: readonly GridSort[] | undefined): GridSort {
  return sort?.find((key) => isGroupSort(key.column)) ?? DEFAULT_GROUP_SORT;
}

/**
 * The sort after a grouped column's header is pressed: its key, the largest first for a count or a size and A to Z for
 * the rest; pressed again, the other way; a third time, the default order.
 */
export function nextGroupSort(current: GridSort, key: string): GridSort[] {
  if (current.column !== key) return [{ column: key, desc: LARGEST_FIRST.has(key) }];
  const firstWay = LARGEST_FIRST.has(key);
  if (current.desc === firstWay) return [{ column: key, desc: !firstWay }];
  return [];
}

/**
 * Whether a view's links are images alone (the Images preset, or a kind filter of images and nothing else): its rows
 * then show each file's picture, size and pixels, and its text is alt text.
 */
export function isImageView(filters: readonly GridFilter[]): boolean {
  return filters.some(
    (filter) =>
      filter.column === 'kind' && filter.op === 'in' && filter.values.length === 1 && filter.values[0] === 'image',
  );
}

/** Where a target's links sit, in words: "Footer, Navigation". Empty when not known. */
export function positionsWords(positions: readonly LinkPosition[]): string {
  return positions.map((position) => POSITION_WORDS[position]).join(', ');
}

/** The other texts a target's links have, beside the one shown: "+2 other texts", or null when there are none. */
export function otherTextsWords(group: Pick<LinkGroup, 'text' | 'texts'>): string | null {
  // `texts` counts no text as one of them: the text shown is one, and only its others are said (no text among them).
  // None shown means none of its links has one: no text is all they have, and there's no other.
  const others = group.text === null ? 0 : group.texts - 1;
  return others > 0 ? `+${formatCountOf(others, 'other text', 'other texts')}` : null;
}

/** A target's text as one line, for copying and a screen reader: the text, or "None", and its others. */
export function groupTextLine(group: Pick<LinkGroup, 'text' | 'texts'>): string {
  const others = otherTextsWords(group);
  return [group.text ?? 'None', others].filter((part): part is string => part !== null).join(' ');
}

/** What a grouped view holds in all, beside its count of targets: "312 links on 265 pages". */
export function groupedTotalsWords(links: number, pages: number): string {
  return `${formatCountOf(links, 'link')} on ${formatCountOf(pages, 'page')}`;
}

/** A grouped image's file in one line: "1.8 MB · 4000×3000 px", what of it is known. */
export function imageFileLine(group: Pick<LinkGroup, 'target'>): string {
  return [
    typeof group.target.sizeBytes === 'number' ? formatBytes(group.target.sizeBytes) : null,
    pixelsWords(group.target.pixelWidth, group.target.pixelHeight),
  ]
    .filter((part): part is string => part !== null)
    .join(' · ');
}

/** "Show all 265 links to this target": the row under an open target that lists its links one a row. */
export function showAllLinksWords(links: number, images: boolean): string {
  if (links === 1)
    return images ? 'Show the one use of this image, as a row' : 'Show the one link to this target, as a row';
  return images
    ? `Show all ${formatCount(links)} uses of this image, one a row`
    : `Show all ${formatCount(links)} links to this target, one a row`;
}

/** The flat view of one target's links: the view's own filters, and the target named by its id. */
export function targetLinksFilters(filters: readonly GridFilter[], targetId: number): GridFilter[] {
  return [
    ...filters.filter((filter) => !(filter.column === 'to.id' && filter.op === 'eq' && filter.value === targetId)),
    { column: 'to.id', op: 'eq', value: targetId },
  ];
}

/**
 * What the grid says while a grouped view's first read is on its way after 150 ms: it reads every link once, whatever
 * the filters, to count the pages each target's links are on. `total` is the crawl's links when a read has said so.
 */
export function groupedReadWords(total: number | null): string {
  return `${total === null ? 'Reading every link' : `Reading all ${formatCount(total)} links`} to group them by target: each one is read once.`;
}

/** Links shown under an open target before the row that lists all of them. */
export const FIRST_TARGET_LINKS = 10;

/** A page of the grouped view as the paged rows keep it: the grid's page, with what the view holds in all. */
export interface LinkGroupsGridPage extends GridPage<LinkGroup> {
  links: number;
  pages: number;
}

export function groupsAsGridPage(page: LinkGroupsPage): LinkGroupsGridPage {
  return {
    columns: [],
    rows: page.rows,
    offset: page.offset,
    matching: page.total,
    total: page.total,
    next: null,
    summary: page.summary,
    unavailable: [],
    links: page.links,
    pages: page.pages,
    ...(page.pixelSizes === undefined ? {} : { pixelSizes: page.pixelSizes }),
  };
}

/** The grouped grid's footer: Enter and Right open a target to its links, Left closes it; Enter on a link inspects its page. */
export const GROUP_KEY_HINTS: readonly GridKeyHint[] = [
  { chord: 'arrowup arrowdown', words: 'Move' },
  { chord: 'arrowright arrowleft', words: 'Open or close' },
  { chord: 'space', words: 'Select' },
  { chord: 'mod+c', words: 'Copy for Excel' },
];
