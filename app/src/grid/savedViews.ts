// Saved views (docs/v2-ui-ia.md, URLs: "Quick search and saved views"): a grid's view kept under a name, with the
// app's other preferences, never in a crawl file. A view is what the route holds of it (filters, search, sort and
// columns), so a saved one is a route: it opens on any crawl, and survives a restart. Pure: what a view is, when two
// are the same, and what's wrong with a name, all unit-tested. Stored values come from disk: each is checked here.
import type { GridFilter, GridSort } from '../api/grids';
import { sameFilters } from '../screens/links/presets';
import { validateGridSearch, type LinkEndsSearch } from '../routing/search';

/** The grids that keep saved views. Only the URL grid does for now; the field keeps room for the others. */
export type SavedViewGrid = 'urls';

/** What a saved view holds: the route's view, without the URL inspected. Undefined fields are the grid's defaults. */
export interface SavedViewSearch {
  filter?: GridFilter[] | undefined;
  sort?: GridSort[] | undefined;
  /** The columns shown, in order; undefined for the grid's default set. */
  cols?: string[] | undefined;
  q?: string | undefined;
  /** Narrowed to one end of a links view's links: part of the view, as the route has it. */
  links?: LinkEndsSearch | undefined;
}

export interface SavedView {
  /** Short and unique among the saved views: `[a-z0-9-]`. */
  id: string;
  grid: SavedViewGrid;
  /** What the user called it: unique among a grid's views, ignoring case. */
  name: string;
  search: SavedViewSearch;
}

/** As many as a menu lists at a glance, and few enough that the preferences file stays small. */
export const MAX_SAVED_VIEWS = 24;
export const MAX_VIEW_NAME = 60;
/**
 * A view's route as JSON is at most this many bytes of UTF-8, and all the saved views together at most
 * MAX_VIEWS_BYTES: the shell keeps a preferences file of at most 64 KB, and the views must leave room for the rest
 * whatever script their text is in (a character of Japanese is three bytes). A view or a set of views over either says
 * so instead of being cut.
 */
export const MAX_VIEW_LENGTH = 4_096;
export const MAX_VIEWS_BYTES = 32 * 1024;

const encoder = new TextEncoder();
/** The bytes a value takes as JSON in UTF-8: what the preferences file holds of it. */
export function jsonBytes(value: unknown): number {
  return encoder.encode(JSON.stringify(value)).length;
}

const ID = /^[a-z0-9-]{1,64}$/;

/** The view of a route's search: its filters, sort, columns and search, each left out when it's the default. */
export function viewSearchOf(search: SavedViewSearch): SavedViewSearch {
  return {
    ...(search.filter && search.filter.length > 0 ? { filter: search.filter } : {}),
    ...(search.sort && search.sort.length > 0 ? { sort: search.sort } : {}),
    ...(search.cols && search.cols.length > 0 ? { cols: search.cols } : {}),
    ...(search.q ? { q: search.q } : {}),
    ...(search.links ? { links: search.links } : {}),
  };
}

function sameList<T>(a: readonly T[] | undefined, b: readonly T[] | undefined, same: (x: T, y: T) => boolean) {
  const left = a ?? [];
  const right = b ?? [];
  return (
    left.length === right.length && left.every((item, index) => index < right.length && same(item, right[index] as T))
  );
}

const sameSort = (a: readonly GridSort[] | undefined, b: readonly GridSort[] | undefined) =>
  sameList(a, b, (x, y) => x.column === y.column && x.desc === y.desc);

/** The same end of the same links (its view's filters in any order, and its search), or neither narrowed. */
function sameLinks(a: LinkEndsSearch | undefined, b: LinkEndsSearch | undefined): boolean {
  if (!a || !b) return !a && !b;
  return a.end === b.end && sameFilters(a.filter ?? [], b.filter ?? []) && (a.q ?? '') === (b.q ?? '');
}

/**
 * Whether two views show the same rows the same way: the same filters (in any order), the same sort keys in the same
 * order, the same columns in the same order, the same search, and the same links' ends, if any.
 */
export function sameView(a: SavedViewSearch, b: SavedViewSearch): boolean {
  return (
    sameFilters(a.filter ?? [], b.filter ?? []) &&
    sameSort(a.sort, b.sort) &&
    sameList(a.cols, b.cols, (x, y) => x === y) &&
    (a.q ?? '') === (b.q ?? '') &&
    sameLinks(a.links, b.links)
  );
}

/** The saved view the route is on, or null: one whose view is exactly the route's. */
export function currentSavedView(views: readonly SavedView[], grid: string, search: SavedViewSearch): SavedView | null {
  return views.find((view) => view.grid === grid && sameView(view.search, search)) ?? null;
}

/** What's wrong with a name for a view, or null: it's needed, short enough, and not another view's. */
export function viewNameProblem(
  name: string,
  views: readonly SavedView[],
  grid: string,
  except?: string,
): string | null {
  const trimmed = name.trim();
  if (trimmed === '') return 'Give the view a name.';
  if (trimmed.length > MAX_VIEW_NAME) return `A name takes ${MAX_VIEW_NAME} characters at most.`;
  const taken = views.find(
    (view) => view.grid === grid && view.id !== except && view.name.toLowerCase() === trimmed.toLowerCase(),
  );
  return taken ? `A view is called “${taken.name}” already. Choose another name.` : null;
}

/** What of a view takes the most room, in words for "This view is too long to keep: …". */
function biggestPart(search: SavedViewSearch): string {
  const parts: [string, number][] = [
    ['its filters take', search.filter ? jsonBytes(search.filter) : 0],
    ['its search takes', search.q ? jsonBytes(search.q) : 0],
    ['its columns take', search.cols ? jsonBytes(search.cols) : 0],
    ['its sort takes', search.sort ? jsonBytes(search.sort) : 0],
    ['its links take', search.links ? jsonBytes(search.links) : 0],
  ];
  const [words] = parts.reduce((most, part) => (part[1] > most[1] ? part : most));
  return words;
}

/** Why the view can't be saved as it is, or null: there's room for it, and it isn't too long to keep. */
export function saveProblem(views: readonly SavedView[], search: SavedViewSearch): string | null {
  if (views.length >= MAX_SAVED_VIEWS) {
    return `${MAX_SAVED_VIEWS} views are saved, as many as the app keeps. Delete one to save another.`;
  }
  const view = viewSearchOf(search);
  if (jsonBytes(view) > MAX_VIEW_LENGTH) {
    return `This view is too long to keep: ${biggestPart(view)} more room than a saved view has. Shorten it, or remove some.`;
  }
  // The new view with a name of the longest kind, beside the ones there are.
  const added: SavedView = { id: 'v-00000000', grid: 'urls', name: 'x'.repeat(MAX_VIEW_NAME), search: view };
  if (jsonBytes([...views, added]) > MAX_VIEWS_BYTES) {
    return 'The saved views take all the room the app keeps for them. Delete one, or a long one, to save this view.';
  }
  return null;
}

/** An id no saved view has. */
export function newViewId(views: readonly SavedView[], random: () => number = Math.random): string {
  for (;;) {
    const id = `v-${Math.floor(random() * 36 ** 8)
      .toString(36)
      .padStart(8, '0')}`;
    if (!views.some((view) => view.id === id)) return id;
  }
}

/**
 * Saved views as stored, of any shape: each is checked as the route checks its search, so a view saved by an earlier
 * version opens as well as it can, and one that can't is dropped. Ids and names are kept unique.
 */
export function readSavedViews(value: unknown): SavedView[] {
  if (!Array.isArray(value)) return [];
  const views: SavedView[] = [];
  for (const entry of value.slice(0, MAX_SAVED_VIEWS)) {
    if (typeof entry !== 'object' || entry === null) continue;
    const record = entry as Record<string, unknown>;
    const id = typeof record.id === 'string' && ID.test(record.id) ? record.id : null;
    const name = typeof record.name === 'string' ? record.name.trim().slice(0, MAX_VIEW_NAME) : '';
    if (!id || name === '' || record.grid !== 'urls') continue;
    if (views.some((view) => view.id === id || view.name.toLowerCase() === name.toLowerCase())) continue;
    const raw = typeof record.search === 'object' && record.search !== null ? record.search : {};
    const checked = validateGridSearch(raw as Record<string, unknown>);
    const view: SavedView = {
      id,
      grid: 'urls',
      name,
      search: viewSearchOf({
        filter: checked.filter,
        sort: checked.sort,
        cols: checked.cols,
        q: checked.q,
        links: checked.links,
      }),
    };
    // Only what the app could have saved: within a view's room, and the views' room all together.
    if (jsonBytes(view.search) > MAX_VIEW_LENGTH || jsonBytes([...views, view]) > MAX_VIEWS_BYTES) continue;
    views.push(view);
  }
  return views;
}
