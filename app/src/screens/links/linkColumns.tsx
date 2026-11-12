// The Links grid's columns, from the engine's list (`GET /api/link-columns`): which are shown, what the query asks
// for to draw them, and how each cell reads. A column the engine adds appears in the chooser with no work here: its
// type says how it's drawn, and its name is worded from its id until this file has better words for it.
import type { GridSort, LinkColumn, LinkRow, UrlColumnType, UrlOutcome } from '../../api/grids';
import type { ChooserGroup } from '../../grid/ColumnChooser';
import type { FilterColumn, FilterGroup } from '../../grid/filterEditing';
import type { GridColumn } from '../../grid/types';
import { midPhrase } from '../../grid/urlFilterWords';
import { FLAG_WORDS, OUTCOME_WORDS } from '../../inspector/words';
import { formatBytes, formatCount, formatMilliseconds } from '../../lib/format';
import { StatusCode } from '../../ui/StatusCode';
import { Url } from '../../ui/Url';
import {
  FOLLOW_COLUMN,
  LINK_GROUP_TITLES,
  endColumn,
  linkColumnGroup,
  linkColumnName,
  linkColumnShortName,
  linkValueName,
  type LinkColumnGroup,
} from './linkWords';

/** What the grid shows until the user chooses otherwise: the link, where it is, and how its target answered. */
export const DEFAULT_LINK_COLUMNS: readonly string[] = [
  'from.url',
  'to.url',
  'text',
  'kind',
  'position',
  FOLLOW_COLUMN,
  'to.statusCode',
];

/** The engine's columns the follow column is drawn from. */
const FOLLOW_PARTS = ['followed', 'noFollow', 'sponsored', 'ugc'] as const;

/** Columns that are never one of the grid's own: an end's id opens it, and the follow column shows the flags. */
const NOT_CHOSEN: ReadonlySet<string> = new Set(['from.id', 'to.id', ...FOLLOW_PARTS]);

const GROUPS: readonly LinkColumnGroup[] = ['link', 'from', 'to'];

/** The link's own columns in the order the chooser lists them: what a link audit reads first. */
const OWN_ORDER = [FOLLOW_COLUMN, 'text', 'kind', 'position', 'imageLink', 'selfLink', 'fragment', 'id'];

/** Whether the engine's list (or the follow column, which is made of it) has a column. */
export function isLinkColumn(id: string, catalogue: readonly LinkColumn[]): boolean {
  if (id === FOLLOW_COLUMN) return FOLLOW_PARTS.every((part) => catalogue.some((column) => column.id === part));
  return !NOT_CHOSEN.has(id) && catalogue.some((column) => column.id === id);
}

/**
 * The columns shown: the route's, without any this engine doesn't have (a link from another version), or the default
 * set. Before the engine's list is read (`catalogue` undefined) the route's are taken as they are.
 */
export function shownLinkColumns(
  chosen: readonly string[] | undefined,
  catalogue: readonly LinkColumn[] | undefined,
): readonly string[] {
  if (!chosen) return DEFAULT_LINK_COLUMNS;
  const known = catalogue ? chosen.filter((id) => isLinkColumn(id, catalogue)) : chosen;
  return known.length > 0 ? known : DEFAULT_LINK_COLUMNS;
}

/**
 * What the query asks for, to draw these columns: each end's id always (a URL opens the inspector by it), the four
 * columns the follow column is made of, and, with an end's status, how it answered, so no status reads as what
 * happened ("Blocked by robots.txt") and never as a guess.
 */
export function linkQueryColumns(shown: readonly string[]): string[] {
  const asked = new Set<string>(['from.id', 'to.id']);
  for (const id of shown) {
    if (id === FOLLOW_COLUMN) FOLLOW_PARTS.forEach((part) => asked.add(part));
    else asked.add(id);
    const end = endColumn(id);
    if (end?.column === 'statusCode') {
      asked.add(`${end.end}.outcome`);
      asked.add(`${end.end}.fetched`);
    }
  }
  return [...asked];
}

/** A URL the crawl has an outcome for that this version has no words for. */
const NO_STATUS = 'No status';
/** A URL the crawl has no answer from: queued, never reached, not followed. Which, only the inspector can say. */
const NOT_FETCHED = 'Not fetched';

/**
 * An end's status as text: its code, or what happened instead. With no code the crawl's own outcome says why
 * ("Blocked by robots.txt", "Timed out", "Not checked"); with no outcome either, the URL was never fetched.
 */
export function endStatusWords(row: LinkRow, end: 'from' | 'to'): string {
  const code = row[`${end}.statusCode`];
  if (typeof code === 'number') return String(code);
  const outcome = row[`${end}.outcome`];
  if (typeof outcome === 'string' && outcome in OUTCOME_WORDS) return OUTCOME_WORDS[outcome as UrlOutcome];
  return row[`${end}.fetched`] === true ? NO_STATUS : NOT_FETCHED;
}

/**
 * Whether search engines follow the link, then its other flags: "Followed", "nofollow, sponsored", "Not followed" (a
 * nofollow on its page stops it, with none on the link itself). Only a hyperlink is followed or not: any other kind
 * has its flags alone. The inspector's link grids say the same of the same link.
 */
export function linkFollowWords(row: LinkRow): string {
  const flags = [row.sponsored ? FLAG_WORDS.sponsored : null, row.ugc ? FLAG_WORDS.ugc : null].filter(
    (flag): flag is string => flag !== null,
  );
  if (row.followed === null || row.followed === undefined) {
    return [row.noFollow ? FLAG_WORDS.noFollow : null, ...flags].filter((flag) => flag !== null).join(', ');
  }
  const lead = row.followed ? 'Followed' : row.noFollow ? FLAG_WORDS.noFollow : 'Not followed';
  return [lead, ...flags].join(', ');
}

/** The distinct URLs of one end of some links, in the order the links have them, one per line: what a copy takes. */
export function endUrls(rows: readonly LinkRow[], end: 'from' | 'to'): { text: string; count: number } {
  const urls = [
    ...new Set(rows.map((row) => row[`${end}.url`]).filter((url): url is string => typeof url === 'string')),
  ];
  return { text: urls.join('\n'), count: urls.length };
}

/** Columns of an end that hold a URL: drawn in the URL's face. */
const URL_TEXT: ReadonlySet<string> = new Set(['redirectTarget', 'canonicalUrl', 'finalUrl']);

const WIDTHS: Record<UrlColumnType, number> = { number: 112, boolean: 104, enum: 152, text: 240 };
const OWN_WIDTHS: Record<string, number> = {
  id: 112,
  kind: 96,
  text: 176,
  position: 104,
  [FOLLOW_COLUMN]: 116,
  fragment: 160,
  selfLink: 168,
  imageLink: 112,
};
/** A URL column's width where the grid's own isn't known, and the least it's given where it is. */
const URL_WIDTH = 300;
const URL_MIN_WIDTH = 160;
const END_WIDTHS: Record<string, number> = {
  url: URL_WIDTH,
  statusCode: 128,
  indexability: 168,
  title: 280,
  type: 112,
};

function widthOf(id: string, type: UrlColumnType): number {
  const end = endColumn(id);
  return (end ? END_WIDTHS[end.column] : OWN_WIDTHS[id]) ?? WIDTHS[type];
}

const muted = (text: string) => <span className="text-text-muted">{text}</span>;

/** What a row holds for a column: a value of the column's type, or null when it's unknown. */
type CellValue = string | number | boolean | null | undefined;

/** A number as its column means it: a time with its unit, a size in bytes, else a count. */
function numberWords(column: string, value: number): string {
  if (column === 'responseMs' || column === 'ttfbMs') return formatMilliseconds(value);
  if (column === 'sizeBytes') return formatBytes(value);
  return formatCount(value);
}

export interface LinkColumnContext {
  /** URLs on the crawl's own origin show their path. */
  siteOrigin: string | null;
  /** Opens the inspector on one of a link's ends. */
  onOpen: (urlId: number, end: 'from' | 'to') => void;
  sort: readonly GridSort[];
  /** A width for each URL column, when the grid has room to share between them. */
  urlWidth?: number | undefined;
}

/** Why a column can't be sorted by: its header's tooltip, and what Enter on it says. */
function sortHint(name: string): string {
  return `Links can’t be sorted by ${midPhrase(name)}: they sort by crawl order, or by a column of the target.`;
}

/**
 * One column of the grid. `column` is the engine's entry for it, or undefined before the engine's list is read (it's
 * then drawn as text, and doesn't sort) and for the follow column, which is the screen's own.
 */
function linkGridColumn(id: string, column: LinkColumn | undefined, context: LinkColumnContext): GridColumn<LinkRow> {
  const name = linkColumnName(id);
  const type: UrlColumnType = column?.type ?? 'text';
  const sorted = context.sort.find((key) => key.column === id);
  const base = {
    id,
    header: name,
    width: widthOf(id, type),
    ...(column?.sortable
      ? { sortable: true, ...(sorted ? { sort: sorted.desc ? ('descending' as const) : ('ascending' as const) } : {}) }
      : { sortHint: sortHint(name) }),
  };
  const end = endColumn(id);

  if (id === FOLLOW_COLUMN) {
    return {
      ...base,
      // Several flags can be more than the column shows: the whole of it is the tooltip.
      cell: (row) =>
        linkFollowWords(row) ? <span title={linkFollowWords(row)}>{linkFollowWords(row)}</span> : muted('–'),
      text: linkFollowWords,
    };
  }
  if (end?.column === 'url') {
    return {
      ...base,
      ...(context.urlWidth ? { width: context.urlWidth } : {}),
      cell: (row) => {
        const url = row[`${end.end}.url`];
        const urlId = row[`${end.end}.id`];
        if (typeof url !== 'string') return '';
        return (
          <Url
            url={url}
            siteOrigin={context.siteOrigin}
            openable={false}
            tabbable={false}
            {...(typeof urlId === 'number' ? { onGo: () => context.onOpen(urlId, end.end) } : {})}
          />
        );
      },
      text: (row) => row[`${end.end}.url`] ?? '',
    };
  }
  if (end?.column === 'statusCode') {
    return {
      ...base,
      // Words take the room a code doesn't: left with the text, as the inspector's link grids have it.
      cell: (row) => {
        const code = row[`${end.end}.statusCode`];
        if (typeof code === 'number') return <StatusCode code={code} />;
        // What happened instead of a status can be longer than the column: whole in its tooltip.
        const words = endStatusWords(row, end.end);
        return (
          <span className="flex min-w-0" title={words}>
            <StatusCode code={null} reason={words} />
          </span>
        );
      },
      text: (row) => endStatusWords(row, end.end),
    };
  }
  const value = (row: LinkRow) => (row as unknown as Record<string, CellValue>)[id];
  switch (type) {
    case 'number': {
      const words = (row: LinkRow) => {
        const number = value(row);
        return typeof number === 'number' ? numberWords(end?.column ?? id, number) : '';
      };
      return {
        ...base,
        numeric: true,
        cell: (row) => words(row) || muted('–'),
        // Copied as the number itself, so a spreadsheet can add it up.
        text: (row) => (typeof value(row) === 'number' ? String(value(row)) : ''),
      };
    }
    case 'boolean': {
      const words = (row: LinkRow) => (value(row) === true ? 'Yes' : value(row) === false ? 'No' : '');
      return { ...base, cell: (row) => words(row) || muted('–'), text: words };
    }
    case 'enum': {
      const words = (row: LinkRow) => {
        const raw = value(row);
        if (raw === null || raw === undefined || typeof raw === 'boolean') return '';
        return linkValueName(id, raw) ?? String(raw);
      };
      return { ...base, cell: (row) => words(row) || muted('–'), text: words };
    }
    case 'text': {
      const words = (row: LinkRow) => {
        const raw = value(row);
        return raw === null || raw === undefined ? '' : String(raw);
      };
      return {
        ...base,
        mono: end !== null && URL_TEXT.has(end.column),
        // The whole text is the cell's tooltip: an anchor text can be a paragraph, and the cell shows its start.
        cell: (row) =>
          words(row) ? <span title={words(row)}>{words(row)}</span> : muted(id === 'text' ? 'None' : '–'),
        text: words,
      };
    }
  }
}

/** The grid's columns, in the order shown. */
export function linkGridColumns(
  shown: readonly string[],
  catalogue: readonly LinkColumn[] | undefined,
  context: LinkColumnContext,
): GridColumn<LinkRow>[] {
  const byId = new Map((catalogue ?? []).map((column) => [column.id, column]));
  return shown.map((id) => linkGridColumn(id, byId.get(id), context));
}

/**
 * The width each URL column gets: the room the other columns leave, shared between them, so the default view fills
 * a 1280 px window with nothing to scroll sideways for, and a wider one gives the URLs the room. Never under
 * URL_MIN_WIDTH: with more columns than fit, the grid scrolls. Undefined while the grid's width isn't known.
 */
export function sharedUrlWidth(
  shown: readonly string[],
  catalogue: readonly LinkColumn[] | undefined,
  available: number | null,
): number | undefined {
  if (available === null) return undefined;
  const byId = new Map((catalogue ?? []).map((column) => [column.id, column]));
  const urls = shown.filter((id) => endColumn(id)?.column === 'url');
  if (urls.length === 0) return undefined;
  const others = shown
    .filter((id) => !urls.includes(id))
    .reduce((sum, id) => sum + widthOf(id, byId.get(id)?.type ?? 'text'), 0);
  return Math.max(URL_MIN_WIDTH, Math.floor((available - others) / urls.length));
}

const NEEDS_ANALYSIS = 'needs an analysis';

/** The chooser's groups: the link, its source page, its target, each with the columns that can be shown. */
export function linkChooserGroups(catalogue: readonly LinkColumn[], summaryAvailable: boolean): ChooserGroup[] {
  const ids = [FOLLOW_COLUMN, ...catalogue.map((column) => column.id)].filter((id) => isLinkColumn(id, catalogue));
  const byId = new Map(catalogue.map((column) => [column.id, column]));
  const rank = (id: string) => (OWN_ORDER.includes(id) ? OWN_ORDER.indexOf(id) : OWN_ORDER.length);
  return GROUPS.map((group) => ({
    id: group,
    title: LINK_GROUP_TITLES[group],
    columns: ids
      .filter((id) => linkColumnGroup(id) === group)
      .sort((a, b) => (group === 'link' ? rank(a) - rank(b) : 0))
      .map((id) => ({
        id,
        name: linkColumnShortName(id),
        keywords: linkColumnName(id),
        ...(byId.get(id)?.needsAnalysis && !summaryAvailable ? { note: `It ${NEEDS_ANALYSIS}.` } : {}),
      })),
  }));
}

export const LINK_FILTER_GROUPS: readonly FilterGroup[] = GROUPS.map((group) => ({
  id: group,
  title: LINK_GROUP_TITLES[group],
}));

/**
 * What a filter can be on: every column the engine filters by, but a link's id and an end's (a filter on one comes
 * from a link, not from typing a number). A column the analysis fills is listed, and can't be chosen without one.
 */
export function linkFilterColumns(catalogue: readonly LinkColumn[], summaryAvailable: boolean): FilterColumn[] {
  return catalogue
    .filter((column) => column.filterable && column.id !== 'id' && endColumn(column.id)?.column !== 'id')
    .map((column) => ({
      id: column.id,
      name: linkColumnName(column.id),
      shortName: linkColumnShortName(column.id),
      group: linkColumnGroup(column.id),
      type: column.type,
      values: column.values.map((value) => ({ value, label: linkValueName(column.id, value) ?? value })),
      ...(column.needsAnalysis && !summaryAvailable ? { unavailable: NEEDS_ANALYSIS } : {}),
    }));
}
