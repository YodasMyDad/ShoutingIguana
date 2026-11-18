// The URL grid's columns, from the engine's list (`GET /api/url-columns`): the presets, which are shown, what the query
// asks for to draw them, how each cell reads, the chooser's groups and what a filter can be on. A column a newer
// engine adds appears in the chooser and the filter bar with no work here: its type says how it's drawn, and its name
// is worded from its id until this file has words for it.
import type { GridSort, UrlColumn, UrlColumnType, UrlRow } from '../../api/grids';
import type { ChooserGroup } from '../../grid/ColumnChooser';
import type { FilterColumn, FilterGroup } from '../../grid/filterEditing';
import type { GridColumn } from '../../grid/types';
import { midPhrase, urlColumnWords } from '../../grid/urlFilterWords';
import { objectCellWords, redirectKindWords } from '../../crawl/evidence';
import { OUTCOME_WORDS } from '../../inspector/words';
import { formatBytes, formatCount, formatMilliseconds } from '../../lib/format';
import { StatusCode } from '../../ui/StatusCode';
import { Url } from '../../ui/Url';
import { RESOURCE_URL_TYPES } from '../../ui/urlText';
import { outcomeWords } from '../sitemaps/urlOutcome';

/** A column preset (docs/v2-ui-ia.md, URLs): the columns an audit of one kind reads, in its order. */
export interface UrlPreset {
  id: 'overview' | 'indexability' | 'content' | 'performance' | 'links';
  title: string;
  /** What it's for, for its tooltip. */
  hint: string;
  columns: readonly string[];
}

/**
 * The presets. Each leads with the URL and what an expert reads first for that kind of audit; a window that has room
 * shows them all, and a narrower one scrolls the grid sideways (never the page). Overview is the default view.
 */
export const URL_PRESETS: readonly UrlPreset[] = [
  {
    id: 'overview',
    title: 'Overview',
    hint: 'Status, indexability, title, depth, inlinks and issues, then speed and size',
    columns: [
      'url',
      'statusCode',
      'indexability',
      'title',
      'depth',
      'inlinks',
      'issues',
      'responseMs',
      'sizeBytes',
      'type',
      'wordCount',
    ],
  },
  {
    id: 'indexability',
    title: 'Indexability',
    hint: 'Why each URL can or can’t be indexed: robots, canonical, sitemaps, redirects',
    columns: [
      'url',
      'statusCode',
      'indexability',
      'canonicalUrl',
      'noindex',
      'nofollow',
      'inSitemap',
      'metaRobots',
      'xRobotsTag',
      'redirectTarget',
    ],
  },
  {
    id: 'content',
    title: 'Content',
    hint: 'Titles, descriptions, headings and words',
    columns: [
      'url',
      'title',
      'titleLength',
      'titleWidth',
      'h1',
      'h1Count',
      'wordCount',
      'description',
      'descriptionLength',
      'descriptionWidth',
    ],
  },
  {
    id: 'performance',
    title: 'Performance',
    hint: 'How fast each URL answered, and how big it is: an image’s file and its pixels',
    columns: [
      'url',
      'statusCode',
      'type',
      'responseMs',
      'ttfbMs',
      'sizeBytes',
      'pixelWidth',
      'pixelHeight',
      'contentType',
      'depth',
    ],
  },
  {
    id: 'links',
    title: 'Links',
    hint: 'Links to each URL and from it, the pages a file is used on, and where its redirects lead',
    columns: [
      'url',
      'statusCode',
      'depth',
      'inlinks',
      'usedOn',
      'followedInlinks',
      'nofollowInlinks',
      'linkingPages',
      'outlinks',
      'redirectTarget',
      'finalUrl',
      'finalStatusCode',
    ],
  },
];

export const DEFAULT_URL_COLUMNS: readonly string[] = URL_PRESETS[0]?.columns ?? ['url'];

const sameColumns = (a: readonly string[], b: readonly string[]) =>
  a.length === b.length && a.every((id, index) => id === b[index]);

/** The preset the view is on: the one whose columns are exactly the view's, in order. Null for the user's own set. */
export function activeUrlPreset(shown: readonly string[]): UrlPreset | null {
  return URL_PRESETS.find((preset) => sameColumns(preset.columns, shown)) ?? null;
}

/**
 * The order of the preset whose columns these are, in any order: what "Put every column back" restores after the
 * columns were moved. Null for the user's own set, whose order is theirs.
 */
export function presetOrderOf(shown: readonly string[]): readonly string[] | null {
  const set = new Set(shown);
  return (
    URL_PRESETS.find((preset) => preset.columns.length === set.size && preset.columns.every((id) => set.has(id)))
      ?.columns ?? null
  );
}

/** The route's `cols` for a set of columns: none for the default set, so the address stays short. */
export function colsParam(shown: readonly string[]): string[] | undefined {
  return sameColumns(shown, DEFAULT_URL_COLUMNS) ? undefined : [...shown];
}

/** Columns that are a check's own (`detail`) or a URL's id: never one of the grid's columns. */
const NOT_SHOWN: ReadonlySet<string> = new Set(['id']);

/** Whether the engine's list has a column this grid can show. */
export function isUrlColumn(id: string, catalogue: readonly UrlColumn[]): boolean {
  return !NOT_SHOWN.has(id) && catalogue.some((column) => column.id === id && !column.issueRowsOnly);
}

/**
 * The columns shown: the route's, without any this engine doesn't have (a link from another version) and each once,
 * or the default set. Before the engine's list is read (`catalogue` undefined) the route's are taken as they are.
 */
export function shownUrlColumns(
  chosen: readonly string[] | undefined,
  catalogue: readonly UrlColumn[] | undefined,
): readonly string[] {
  if (!chosen) return DEFAULT_URL_COLUMNS;
  const unique = chosen.filter((id, index) => chosen.indexOf(id) === index && !NOT_SHOWN.has(id));
  const known = catalogue ? unique.filter((id) => isUrlColumn(id, catalogue)) : unique;
  return known.length > 0 ? known : DEFAULT_URL_COLUMNS;
}

/**
 * What the query asks for, to draw these columns: the URL always (a row's menu opens and copies it), and with the
 * status, how the request went, so a URL with no status says what happened instead ("Blocked by robots.txt").
 */
export function urlQueryColumns(shown: readonly string[]): string[] {
  const asked = new Set<string>(['url', ...shown]);
  if (shown.includes('statusCode')) ['statusClass', 'outcome', 'fetched'].forEach((id) => asked.add(id));
  return [...asked];
}

// ------------------------------------------------------------------------------------------------------- Groups

export type UrlColumnGroup = 'url' | 'response' | 'indexing' | 'content' | 'links' | 'issues';

const GROUP_TITLES: Record<UrlColumnGroup, string> = {
  url: 'The URL',
  response: 'Its response',
  indexing: 'Indexability',
  content: 'Content',
  links: 'Links',
  issues: 'Issues',
};

const GROUP_OF: Record<string, UrlColumnGroup> = {
  url: 'url',
  type: 'url',
  source: 'url',
  depth: 'url',
  isInternal: 'url',
  fetched: 'url',
  contentType: 'url',
  statusCode: 'response',
  statusClass: 'response',
  outcome: 'response',
  redirectTarget: 'response',
  redirectTargetStatusCode: 'response',
  redirectKind: 'response',
  finalUrl: 'response',
  finalStatusCode: 'response',
  responseMs: 'response',
  ttfbMs: 'response',
  sizeBytes: 'response',
  pixelWidth: 'response',
  pixelHeight: 'response',
  fetchError: 'response',
  redirectChain: 'response',
  indexable: 'indexing',
  indexability: 'indexing',
  noindex: 'indexing',
  nofollow: 'indexing',
  metaRobots: 'indexing',
  xRobotsTag: 'indexing',
  canonicalUrl: 'indexing',
  inSitemap: 'indexing',
  robotsRule: 'indexing',
  title: 'content',
  titleLength: 'content',
  titleWidth: 'content',
  titleInResults: 'content',
  description: 'content',
  descriptionLength: 'content',
  descriptionWidth: 'content',
  descriptionInResults: 'content',
  h1: 'content',
  h1Count: 'content',
  language: 'content',
  wordCount: 'content',
  inlinks: 'links',
  followedInlinks: 'links',
  nofollowInlinks: 'links',
  linkingPages: 'links',
  usedOn: 'links',
  outlinks: 'links',
  issues: 'issues',
  errors: 'issues',
  warnings: 'issues',
  notices: 'issues',
};

const GROUPS: readonly UrlColumnGroup[] = ['url', 'response', 'indexing', 'content', 'links', 'issues'];
/** The order the chooser lists a group's columns in: GROUP_OF's, then any the engine adds. */
const ORDER = Object.keys(GROUP_OF);

/** A column's group; one this version has no place for is the URL's. */
export function urlColumnGroup(id: string): UrlColumnGroup {
  return GROUP_OF[id] ?? 'url';
}

// -------------------------------------------------------------------------------------------------------- Words

/** A column's name, from its id: "fooBarCount" reads "Foo bar count" until there are better words for it. */
function wordedId(id: string): string {
  const words = id.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

/** A column's name: in a filter's sentence, the chooser and the copied header. */
export function urlColumnName(id: string): string {
  return urlColumnWords(id) ?? wordedId(id);
}

/** A few columns' headers are shorter than their names, where the grid's room is dear: the name is their tooltip. */
const SHORT_HEADERS: Record<string, string> = {
  responseMs: 'Response',
  ttfbMs: 'First byte',
  redirectTargetStatusCode: 'Redirect status',
};

/** A column's header in the grid. */
export function urlColumnHeader(id: string): string {
  return SHORT_HEADERS[id] ?? urlColumnName(id);
}

/** What a column holds, where its name alone doesn't say: its header's tooltip. */
const HINTS: Record<string, string> = {
  usedOn:
    'The pages that use it, by any kind of link: an image, a script, a stylesheet, a canonical. Inlinks count hyperlinks only',
  inlinks: 'Hyperlinks to it on the pages audited, each counted, a page’s links to itself left out',
  pixelWidth: 'The image file’s own width in pixels, read from the file: not the size a page shows it at. Shown only',
  pixelHeight: 'The image file’s own height in pixels, read from the file: not the size a page shows it at. Shown only',
  fetchError:
    'What went wrong, as the crawl recorded it, for a URL that didn’t succeed: a timeout, a connection that failed, robots.txt that couldn’t be read',
  redirectChain: 'Each redirect from it, with the status there, to where they end',
  redirectKind: 'What its redirect changes: HTTP to HTTPS, www, a trailing slash, the case, or another page or host',
  language: 'The language the page gives in <html lang>, as it writes it',
  robotsRule: 'The robots.txt line that refused it, and where it is in the file',
  titleLength: 'The title’s characters, as a reader counts them: an emoji or an accented letter once',
  descriptionLength: 'The meta description’s characters, as a reader counts them: an emoji or an accented letter once',
  titleWidth:
    'How wide the title shows in Google’s desktop results, in pixels: what “Title too long” and “too short” judge',
  descriptionWidth:
    'How wide the meta description shows in Google’s desktop results, in pixels: what “Meta description too long” and “too short” judge',
  titleInResults:
    'The title as Google’s desktop results will likely show it: cut with “…” where it’s too wide. Shown only',
  descriptionInResults:
    'The meta description as Google’s desktop results will likely show it: cut with “…” where it’s too wide. Shown only',
  metaRobots: 'The robots meta tags’ content, as the page wrote it',
  xRobotsTag: 'The X-Robots-Tag header, as the server sent it',
};

/** A column's header tooltip, where it has one. */
export function urlColumnHint(id: string): string | undefined {
  return HINTS[id];
}

/**
 * Each column's type as this version knows it, for a grid drawn before the engine's list is read: a number is then
 * right-aligned from the first row, never drawn as text first.
 */
const KNOWN_TYPES: Record<string, UrlColumnType> = {
  depth: 'number',
  statusCode: 'number',
  redirectTargetStatusCode: 'number',
  h1Count: 'number',
  wordCount: 'number',
  outlinks: 'number',
  sizeBytes: 'number',
  responseMs: 'number',
  ttfbMs: 'number',
  titleLength: 'number',
  descriptionLength: 'number',
  titleWidth: 'number',
  descriptionWidth: 'number',
  finalStatusCode: 'number',
  inlinks: 'number',
  followedInlinks: 'number',
  nofollowInlinks: 'number',
  linkingPages: 'number',
  issues: 'number',
  errors: 'number',
  warnings: 'number',
  notices: 'number',
  usedOn: 'number',
  pixelWidth: 'number',
  pixelHeight: 'number',
  indexability: 'enum',
  type: 'enum',
  source: 'enum',
  statusClass: 'enum',
  outcome: 'enum',
  fetched: 'boolean',
  isInternal: 'boolean',
  indexable: 'boolean',
  noindex: 'boolean',
  nofollow: 'boolean',
  inSitemap: 'boolean',
  redirectChain: 'object',
  robotsRule: 'object',
};

/** A column's type: the engine's, else this version's, else text. */
export function urlColumnType(id: string, column: UrlColumn | undefined): UrlColumnType {
  return column?.type ?? KNOWN_TYPES[id] ?? 'text';
}

/**
 * An enum value as a cell shows it where the engine gives no words for it (`valueTitles`, which win): the engine's own
 * words as this version knew them, so the grid, its facets, its chips and the export say the same.
 */
const CELL_VALUES: Record<string, Record<string, string>> = {
  indexability: {
    none: 'Indexable',
    noindex: 'Noindex',
    canonicalised: 'Canonicalised',
    redirected: 'Redirected',
    clientError: 'Client error',
    serverError: 'Server error',
    blockedByRobots: 'Blocked by robots.txt',
    blocked: 'Blocked',
    unreachable: 'Unreachable',
  },
  type: {
    page: 'Page',
    image: 'Image',
    script: 'Script',
    stylesheet: 'Stylesheet',
    font: 'Font',
    media: 'Media file',
    pdf: 'PDF',
    other: 'File',
  },
  source: {
    seed: 'The start URL',
    link: 'A link',
    sitemap: 'A sitemap',
    siteVersion: 'A probe of the site’s other versions',
  },
  statusClass: { other: 'Another code', noResponse: 'No response' },
  outcome: OUTCOME_WORDS,
};

/** An enum value in a cell's words: the engine's (`titles`, its `valueTitles`), else this version's, else the value. */
export function urlCellValue(column: string, value: string, titles?: Readonly<Record<string, string>>): string {
  return titles?.[value] ?? CELL_VALUES[column]?.[value] ?? value;
}

/** Columns that hold a URL: drawn in the URL's face, a path where it's on the crawl's own site. */
const URL_TEXT: ReadonlySet<string> = new Set(['redirectTarget', 'canonicalUrl', 'finalUrl']);

/** A number as its column means it: a time with its unit, a size in bytes, else a count. */
function numberWords(column: string, value: number): string {
  if (column === 'responseMs' || column === 'ttfbMs') return formatMilliseconds(value);
  if (column === 'sizeBytes') return formatBytes(value);
  if (column === 'pixelWidth' || column === 'pixelHeight' || column === 'titleWidth' || column === 'descriptionWidth') {
    return `${formatCount(value)} px`;
  }
  return formatCount(value);
}

// ------------------------------------------------------------------------------------------------------- Widths

const TYPE_WIDTHS: Record<UrlColumnType, number> = { number: 96, boolean: 96, enum: 136, text: 240, object: 320 };
const WIDTHS: Record<string, number> = {
  statusCode: 104,
  statusClass: 96,
  outcome: 168,
  indexability: 132,
  indexable: 96,
  type: 88,
  source: 136,
  depth: 64,
  isInternal: 112,
  fetched: 88,
  contentType: 200,
  title: 240,
  description: 320,
  h1: 240,
  h1Count: 64,
  language: 96,
  wordCount: 104,
  titleLength: 112,
  descriptionLength: 160,
  titleWidth: 112,
  descriptionWidth: 160,
  titleInResults: 320,
  descriptionInResults: 360,
  inlinks: 80,
  followedInlinks: 136,
  nofollowInlinks: 136,
  linkingPages: 120,
  outlinks: 96,
  responseMs: 96,
  ttfbMs: 96,
  sizeBytes: 88,
  issues: 72,
  errors: 72,
  warnings: 96,
  notices: 88,
  noindex: 88,
  nofollow: 88,
  inSitemap: 112,
  metaRobots: 176,
  xRobotsTag: 176,
  redirectTarget: 240,
  redirectTargetStatusCode: 136,
  redirectKind: 200,
  canonicalUrl: 240,
  finalUrl: 240,
  finalStatusCode: 104,
  usedOn: 88,
  pixelWidth: 120,
  pixelHeight: 120,
  fetchError: 240,
  redirectChain: 360,
  robotsRule: 240,
};
/** The URL column's width before the grid's is known, and the least it's given where it is. */
const URL_WIDTH = 360;
export const URL_MIN_WIDTH = 240;

function widthOf(id: string, type: UrlColumnType): number {
  return WIDTHS[id] ?? TYPE_WIDTHS[type];
}

/**
 * The URL column's width: the room the other columns leave, so a preset fills the window with nothing to scroll
 * sideways for where it fits, and a wider window gives the URLs the room. Never under URL_MIN_WIDTH: with more columns
 * than fit, the grid scrolls sideways. Undefined while the grid's width isn't known.
 */
export function urlColumnWidth(
  shown: readonly string[],
  catalogue: readonly UrlColumn[] | undefined,
  available: number | null,
  /** The widths the user gave columns (the grid's column layout): the room they take is theirs. */
  widths: Readonly<Record<string, number>> = {},
): number | undefined {
  if (available === null || !shown.includes('url')) return undefined;
  const byId = new Map((catalogue ?? []).map((column) => [column.id, column]));
  const others = shown
    .filter((id) => id !== 'url')
    .reduce((sum, id) => sum + (widths[id] ?? widthOf(id, byId.get(id)?.type ?? 'text')), 0);
  return Math.max(URL_MIN_WIDTH, Math.floor(available - others));
}

/** How many columns are wholly in view from the left, at a grid's width: what a preset shows without scrolling. */
export function columnsInView(widths: readonly number[], available: number): number {
  let used = 0;
  let count = 0;
  for (const width of widths) {
    used += width;
    if (used > available) break;
    count++;
  }
  return count;
}

// ------------------------------------------------------------------------------------------------------ Columns

export interface UrlColumnContext {
  /** The crawl's own scheme and host: URLs on it show their path, the host named once in the header. */
  siteOrigin: string | null;
  /** "Show full URLs": every URL whole, the host included. */
  fullUrls: boolean;
  sort: readonly GridSort[];
  /** The URL column's width, when the grid has room to give it. */
  urlWidth?: number | undefined;
  /** The crawl is running: a URL not fetched yet may still be. */
  crawling: boolean;
}

/** Why a column can't be sorted by: its header's tooltip, and what Enter on it says. */
export function urlSortHint(name: string, type: UrlColumnType): string {
  const what = midPhrase(name);
  return type === 'text'
    ? `URLs can’t be sorted by ${what}: a sort by text would read every page. Filter by it instead.`
    : `URLs can’t be sorted by ${what}: it’s read from each response, so a sort by it would read every one. Filter by it instead.`;
}

/** What a row holds for a column: a value of the column's type, or null when it's unknown. */
type CellValue = string | number | boolean | null | undefined;
const valueOf = (row: UrlRow, id: string) => (row as unknown as Record<string, CellValue>)[id];

const muted = (text: string) => <span className="text-text-muted">{text}</span>;

/**
 * A URL's status as text: its code, or what happened instead, from the crawl's own outcome ("Blocked by robots.txt",
 * "Timed out", "Not checked"); with neither, whether it was crawled at all ("Not crawled so far" while the crawl runs).
 */
export function urlStatusWords(row: UrlRow, crawling: boolean): string {
  if (typeof row.statusCode === 'number') return String(row.statusCode);
  if (row.statusClass === 'noResponse' && !row.outcome) return 'No response';
  return outcomeWords(row, crawling);
}

function urlGridColumn(id: string, column: UrlColumn | undefined, context: UrlColumnContext): GridColumn<UrlRow> {
  const name = urlColumnName(id);
  const type = urlColumnType(id, column);
  const at = context.sort.findIndex((key) => key.column === id);
  const sorted = at >= 0 ? context.sort[at] : undefined;
  const header = urlColumnHeader(id);
  const hint = urlColumnHint(id);
  const base = {
    id,
    header,
    width: widthOf(id, type),
    ...(hint ? { headerHint: hint } : {}),
    ...(column?.sortable
      ? {
          sortable: true,
          ...(sorted
            ? {
                sort: sorted.desc ? ('descending' as const) : ('ascending' as const),
                ...(context.sort.length > 1 ? { sortOrder: at + 1 } : {}),
              }
            : {}),
        }
      : { sortHint: urlSortHint(name, type) }),
  };

  if (id === 'url') {
    const origin = context.fullUrls ? null : context.siteOrigin;
    return {
      ...base,
      ...(context.urlWidth ? { width: context.urlWidth } : { width: URL_WIDTH }),
      minWidth: URL_MIN_WIDTH,
      mono: true,
      // The host is named once, here, where the rows show its URLs as paths.
      ...(origin ? { headerDetail: `on ${new URL(origin).host}` } : {}),
      cell: (row) =>
        row.url ? (
          <Url
            url={row.url}
            siteOrigin={origin}
            copyable={false}
            tabbable={false}
            // A file a page loads (an image, a stylesheet) is named by its file, not cut in the middle of its name.
            label={row.type && RESOURCE_URL_TYPES.has(row.type) ? 'file' : 'address'}
          />
        ) : (
          muted('–')
        ),
      text: (row) => row.url ?? '',
    };
  }
  if (id === 'statusCode') {
    return {
      ...base,
      cell: (row) => {
        if (typeof row.statusCode === 'number') return <StatusCode code={row.statusCode} />;
        // What happened instead of a status can be longer than the column: whole in its tooltip.
        const words = urlStatusWords(row, context.crawling);
        return (
          <span className="flex min-w-0" title={words}>
            <StatusCode code={null} reason={words} />
          </span>
        );
      },
      text: (row) => urlStatusWords(row, context.crawling),
    };
  }
  switch (type) {
    case 'number': {
      const words = (row: UrlRow) => {
        const number = valueOf(row, id);
        return typeof number === 'number' ? numberWords(id, number) : '';
      };
      return {
        ...base,
        numeric: true,
        cell: (row) => words(row) || muted('–'),
        // Copied as the number itself, so a spreadsheet can add it up.
        text: (row) => (typeof valueOf(row, id) === 'number' ? String(valueOf(row, id)) : ''),
      };
    }
    case 'boolean': {
      const words = (row: UrlRow) => (valueOf(row, id) === true ? 'Yes' : valueOf(row, id) === false ? 'No' : '');
      return { ...base, cell: (row) => words(row) || muted('–'), text: words };
    }
    case 'enum': {
      const words = (row: UrlRow) => {
        const raw = valueOf(row, id);
        return typeof raw === 'string' ? urlCellValue(id, raw, column?.valueTitles) : '';
      };
      return {
        ...base,
        cell: (row) => (words(row) ? <span title={words(row)}>{words(row)}</span> : muted('–')),
        text: words,
      };
    }
    case 'object': {
      // A redirect chain or a robots.txt rule, in one line: whole in its tooltip; copied with its URLs whole.
      const origin = context.fullUrls ? null : context.siteOrigin;
      const raw = (row: UrlRow) => (row as unknown as Record<string, unknown>)[id];
      const words = (row: UrlRow) => objectCellWords(id, raw(row), origin);
      return {
        ...base,
        cell: (row) => (words(row) ? <span title={words(row)}>{words(row)}</span> : muted('–')),
        text: (row) => objectCellWords(id, raw(row), null),
      };
    }
    case 'text': {
      const words = (row: UrlRow) => {
        const raw = valueOf(row, id);
        if (raw === null || raw === undefined) return '';
        return id === 'redirectKind' ? redirectKindWords(String(raw)) : String(raw);
      };
      if (URL_TEXT.has(id)) {
        const origin = context.fullUrls ? null : context.siteOrigin;
        return {
          ...base,
          mono: true,
          cell: (row) =>
            words(row) ? <Url url={words(row)} siteOrigin={origin} copyable={false} tabbable={false} /> : muted('–'),
          text: words,
        };
      }
      return {
        ...base,
        // The whole text is the cell's tooltip: a title or a description can be longer than the column.
        cell: (row) => (words(row) ? <span title={words(row)}>{words(row)}</span> : muted('–')),
        text: words,
      };
    }
  }
}

/** The grid's columns, in the order shown. */
export function urlGridColumns(
  shown: readonly string[],
  catalogue: readonly UrlColumn[] | undefined,
  context: UrlColumnContext,
): GridColumn<UrlRow>[] {
  const byId = new Map((catalogue ?? []).map((column) => [column.id, column]));
  return shown.map((id) => urlGridColumn(id, byId.get(id), context));
}

// ---------------------------------------------------------------------------------------- The chooser and filters

const NEEDS_ANALYSIS = 'needs an analysis';

/** The chooser's groups, each with the columns the grid can show, in a reading order. */
export function urlChooserGroups(catalogue: readonly UrlColumn[], summaryAvailable: boolean): ChooserGroup[] {
  const columns = catalogue.filter((column) => isUrlColumn(column.id, catalogue));
  const rank = (id: string) => (ORDER.includes(id) ? ORDER.indexOf(id) : ORDER.length);
  return GROUPS.map((group) => ({
    id: group,
    title: GROUP_TITLES[group],
    columns: columns
      .filter((column) => urlColumnGroup(column.id) === group)
      .sort((a, b) => rank(a.id) - rank(b.id))
      .map((column) => ({
        id: column.id,
        name: urlColumnName(column.id),
        ...(column.needsAnalysis && !summaryAvailable ? { note: `It ${NEEDS_ANALYSIS}.` } : {}),
      })),
  })).filter((group) => group.columns.length > 0);
}

/**
 * "All columns" (docs/v2-ui-discovery.md, Export: "an 'All columns' preset gives the full dump"): the URL, then every
 * column the engine has for the grid, in the chooser's groups and order, so an export of the view is everything.
 */
export const ALL_COLUMNS_PRESET = {
  id: 'all',
  title: 'All columns',
  hint: 'Every column there is, for a full export: the grid scrolls sideways',
} as const;

/** The columns of "All columns": the URL first, then the chooser's, group by group. */
export function allUrlColumns(catalogue: readonly UrlColumn[]): string[] {
  const ids = urlChooserGroups(catalogue, true).flatMap((group) => group.columns.map((column) => column.id));
  return ['url', ...ids.filter((id) => id !== 'url')];
}

export const URL_FILTER_GROUPS: readonly FilterGroup[] = GROUPS.map((group) => ({
  id: group,
  title: GROUP_TITLES[group],
}));

/**
 * What a filter can be on: every column the engine filters by but a URL's id (a filter on one comes from a link, not
 * from typing a number) and a check's own. A column the analysis fills is listed, and can't be chosen without one.
 */
export function urlFilterColumns(catalogue: readonly UrlColumn[], summaryAvailable: boolean): FilterColumn[] {
  const rank = (id: string) => (ORDER.includes(id) ? ORDER.indexOf(id) : ORDER.length);
  return catalogue
    .filter((column) => column.filterable && !column.issueRowsOnly && column.id !== 'id')
    .sort((a, b) => rank(a.id) - rank(b.id))
    .map((column) => ({
      id: column.id,
      name: urlColumnName(column.id),
      group: urlColumnGroup(column.id),
      type: column.type,
      values: column.values.map((value) => ({ value, label: urlCellValue(column.id, value, column.valueTitles) })),
      ...(column.needsAnalysis && !summaryAvailable ? { unavailable: NEEDS_ANALYSIS } : {}),
    }));
}
