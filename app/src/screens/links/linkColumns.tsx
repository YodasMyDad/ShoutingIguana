// The Links grid's columns, from the engine's list (`GET /api/link-columns`): which are shown, what the query asks
// for to draw them, and how each cell reads. A column the engine adds appears in the chooser with no work here: its
// type says how it's drawn, and its name is worded from its id until this file has better words for it.
import type { GridSort, LinkColumn, LinkRow, StatusClass, UrlColumnType, UrlOutcome } from '../../api/grids';
import type { RedirectChain } from '../../api/inspector';
import type { ChooserGroup } from '../../grid/ColumnChooser';
import type { FilterColumn, FilterGroup } from '../../grid/filterEditing';
import type { GridColumn } from '../../grid/types';
import { midPhrase } from '../../grid/urlFilterWords';
import { objectCellWords, pixelsWords } from '../../crawl/evidence';
import { FLAG_WORDS } from '../../inspector/words';
import { urlStatusWords } from '../urls/urlColumns';
import { formatBytes, formatCount, formatCountOf, formatMilliseconds } from '../../lib/format';
import { urlAtText } from '../../lib/textFragment';
import { textWidth, tokenValue } from '../../lib/textWidth';
import { ImageThumb } from '../../ui/ImageThumb';
import { isBrokenImage } from '../../ui/imageFacts';
import { StatusCode } from '../../ui/StatusCode';
import { Url } from '../../ui/Url';
import { RESOURCE_LINK_KINDS, fileLabelOf, urlDisplay } from '../../ui/urlText';
import {
  ENDS_AT_COLUMN,
  ENDS_AT_PARTS,
  FOLLOW_COLUMN,
  LINK_FACTS_COLUMN,
  LINK_FACTS_PARTS,
  LINK_GROUP_TITLES,
  PIXELS_COLUMN,
  PIXELS_PARTS,
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
export const FOLLOW_PARTS = ['followed', 'noFollow', 'sponsored', 'ugc'] as const;

/**
 * Columns that are never one of the grid's own: an end's id opens it, the follow column shows the flags, and the pixel
 * size column a target's width and height.
 */
const NOT_CHOSEN: ReadonlySet<string> = new Set(['from.id', 'to.id', ...FOLLOW_PARTS, ...PIXELS_PARTS]);

const GROUPS: readonly LinkColumnGroup[] = ['link', 'from', 'to'];

/** The link's own columns in the order the chooser lists them: what a link audit reads first. */
const OWN_ORDER = [FOLLOW_COLUMN, 'text', 'kind', 'position', 'imageLink', 'selfLink', 'fragment', 'id'];

/** Whether the engine's list (or the follow column, which is made of it) has a column. */
export function isLinkColumn(id: string, catalogue: readonly LinkColumn[]): boolean {
  if (id === FOLLOW_COLUMN) return FOLLOW_PARTS.every((part) => catalogue.some((column) => column.id === part));
  if (id === PIXELS_COLUMN) return PIXELS_PARTS.every((part) => catalogue.some((column) => column.id === part));
  if (id === ENDS_AT_COLUMN) return ENDS_AT_PARTS.every((part) => catalogue.some((column) => column.id === part));
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

/** An end's columns that name another URL of the crawl its redirect chain knows by id: drawn as links to it. */
const CHAIN_URLS: ReadonlySet<string> = new Set(['redirectTarget', 'finalUrl']);

/**
 * What the query asks for, to draw these columns: each end's id and URL always (a URL opens the inspector by its id,
 * and a row's menu opens and copies it), the link's kind and words always (the page it's on opens in the browser at
 * the link: `sourceAtLink`), the four
 * columns the follow column is made of, and, with an end's status, how it answered, so no status reads as what
 * happened ("Blocked by robots.txt") and never as a guess.
 */
export function linkQueryColumns(shown: readonly string[], images = false): string[] {
  // Each end's URL too, shown or not: a row's menu opens and copies the URL of the end it was opened in.
  const asked = new Set<string>(['from.id', 'to.id', 'from.url', 'to.url', 'kind', 'text', 'imageLink']);
  // An image view draws each image's thumbnail, broken only where the image is known to be: how it answered.
  if (images) ['to.statusCode', 'to.outcome'].forEach((column) => asked.add(column));
  for (const id of unfoldedLinkColumns(shown)) {
    if (id === FOLLOW_COLUMN) FOLLOW_PARTS.forEach((part) => asked.add(part));
    else if (id === PIXELS_COLUMN) PIXELS_PARTS.forEach((part) => asked.add(part));
    else if (id === ENDS_AT_COLUMN) ENDS_AT_PARTS.forEach((part) => asked.add(part));
    else asked.add(id);
    const end = endColumn(id);
    // Where a redirect goes and where it ends are URLs of the crawl: the chain has their ids, to open them by.
    if (end && CHAIN_URLS.has(end.column)) asked.add(`${end.end}.redirectChain`);
    if (end?.column === 'statusCode') {
      asked.add(`${end.end}.outcome`);
      asked.add(`${end.end}.fetched`);
      asked.add(`${end.end}.statusClass`);
    }
  }
  return [...asked];
}

/**
 * An end's status as text: its code, or what happened instead, in the URL grid's words for the same URL (one helper,
 * `urlStatusWords`): the crawl's own outcome ("Blocked by robots.txt", "Timed out", "Not checked"); with none, "Not
 * crawled", or "Not crawled so far" while the crawl runs. Never a guess, and never 0.
 */
export function endStatusWords(row: LinkRow, end: 'from' | 'to', crawling = false): string {
  const field = (column: string) => row[`${end}.${column}`] ?? null;
  const code = field('statusCode');
  const fetched = field('fetched');
  return urlStatusWords(
    {
      id: 0,
      statusCode: typeof code === 'number' ? code : null,
      statusClass: field('statusClass') as StatusClass | null,
      outcome: field('outcome') as UrlOutcome | null,
      fetched: typeof fetched === 'boolean' ? fetched : null,
    },
    crawling,
  );
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

/**
 * The page a link is on, as a browser opens it at the link: its URL at the link's own words (`urlAtText`, the fix
 * round's rule 4). Undefined where there's nothing a browser could find: a link that isn't a hyperlink, one with no
 * words, and one whose words are an image's alt text.
 */
export function sourceAtLink(row: LinkRow): string | undefined {
  const page = row['from.url'];
  if (typeof page !== 'string' || !/^https?:\/\//i.test(page)) return undefined;
  if (row.kind !== 'hyperlink' || row.imageLink === true || !row.text?.trim()) return undefined;
  return urlAtText(page, row.text);
}

/** What the open-in-browser button of the page a link is on says when it opens the page at the link. */
export const OPEN_AT_LINK_HINT = 'Open the page at this link';

/**
 * The columns as drawn: where the grid is narrow (D-17), a view that shows a link's kind, position and follow shows
 * them as one column, where the first of them was, so the URLs keep the room. A view without all three keeps its own.
 */
export function foldedLinkColumns(shown: readonly string[], narrow: boolean): readonly string[] {
  if (!narrow || !LINK_FACTS_PARTS.every((part) => shown.includes(part))) return shown;
  const parts: readonly string[] = LINK_FACTS_PARTS;
  const first = shown.findIndex((id) => parts.includes(id));
  return shown.flatMap((id, index) => (index === first ? [LINK_FACTS_COLUMN] : parts.includes(id) ? [] : [id]));
}

/** Drawn columns as the route, the chooser and an export have them: the folded column as the three it stands for. */
export function unfoldedLinkColumns(ids: readonly string[]): string[] {
  return ids.flatMap((id) => (id === LINK_FACTS_COLUMN ? [...LINK_FACTS_PARTS] : [id]));
}

/** A link's kind, where it sits and whether it's followed, in one line: "Hyperlink · Content · Followed". */
export function linkFactsWords(row: LinkRow): string {
  return [
    row.kind ? (linkValueName('kind', row.kind) ?? row.kind) : null,
    row.position ? (linkValueName('position', row.position) ?? row.position) : null,
    linkFollowWords(row) || null,
  ]
    .filter((part): part is string => part !== null)
    .join(' · ');
}

/** Where a target that redirects ends: the URL (of the crawl, by its id), how it answered, and how it got there. */
export interface LinkEnd {
  id: number;
  url: string;
  statusCode: number | null;
  outcome?: UrlOutcome | null | undefined;
  /** "meta refresh", "3 hops", "a loop": what a status alone doesn't say. Null for one HTTP redirect. */
  note: string | null;
}

const chainOf = (row: LinkRow, end: 'from' | 'to'): RedirectChain | null => {
  const chain = (row as unknown as Record<string, unknown>)[`${end}.redirectChain`];
  return typeof chain === 'object' && chain !== null && Array.isArray((chain as RedirectChain).hops)
    ? (chain as RedirectChain)
    : null;
};

/** Where a link's target ends, from its redirect chain. Null for a target that doesn't redirect, or isn't known to. */
export function endsAtOf(row: LinkRow): LinkEnd | null {
  const chain = chainOf(row, 'to');
  if (!chain || chain.hops.length === 0) return null;
  const note = [
    chain.hops.some((hop) => hop.via === 'metaRefresh') ? 'meta refresh' : null,
    chain.hops.length > 1 ? formatCountOf(chain.hops.length, 'hop') : null,
    chain.loop ? 'a loop' : chain.tooLong ? 'more than 20 hops' : null,
  ].filter((part): part is string => part !== null);
  return { ...chain.final, note: note.length > 0 ? note.join(', ') : null };
}

/** The same as text, for a copy: "https://example.com/thin 200 · meta refresh". */
export function endsAtWords(row: LinkRow): string {
  const end = endsAtOf(row);
  if (!end) return '';
  const status = typeof end.statusCode === 'number' ? ` ${end.statusCode}` : '';
  return `${end.url}${status}${end.note ? ` · ${end.note}` : ''}`;
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

const WIDTHS: Record<UrlColumnType, number> = { number: 112, boolean: 104, enum: 152, text: 240, object: 320 };
const OWN_WIDTHS: Record<string, number> = {
  id: 112,
  kind: 96,
  text: 176,
  position: 104,
  [FOLLOW_COLUMN]: 116,
  [LINK_FACTS_COLUMN]: 232,
  fragment: 160,
  selfLink: 168,
  imageLink: 112,
};
/** A URL column's width where the grid's own isn't known, and the least it's given where it is. */
const URL_WIDTH = 300;
const URL_MIN_WIDTH = 160;
const END_WIDTHS: Record<string, number> = {
  url: URL_WIDTH,
  endsAt: 320,
  pixels: 120,
  sizeBytes: 104,
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
  /**
   * Opens the inspector on one of a link's ends. `other` is the URL at the link's other end, so the inspector can
   * arrive on that link (the page's outlinks with the target marked, the target's inlinks with the page marked).
   */
  onOpen: (urlId: number, end: 'from' | 'to', other?: number) => void;
  sort: readonly GridSort[];
  /** The crawl is running: an end not crawled yet may still be. */
  crawling?: boolean | undefined;
  /** A width for each URL column, when the grid has room to share between them. */
  urlWidth?: number | undefined;
  /**
   * Each column's own width, where the room is shared by what the view's values take (`fittedLinkWidths`): before
   * `urlWidth`, and the column's default.
   */
  widths?: Readonly<Record<string, number>> | undefined;
  /**
   * The view's links are images alone (the Images preset): the target is "Image", shown with a picture of it, its text
   * "Alt text", and the page it's on "Shown on".
   */
  images?: boolean | undefined;
}

/** What an image view calls the columns it's about: the image, not "the target". */
const IMAGE_NAMES: Record<string, string> = {
  'to.url': 'Image',
  'to.sizeBytes': 'Size',
  [PIXELS_COLUMN]: 'Pixel size',
  text: 'Alt text',
  'from.url': 'Shown on',
  'to.statusCode': 'Image status',
};

/** The words of the composed pixel size column: "896×600 px", or nothing where either isn't known. */
export function pixelsCellWords(row: LinkRow): string {
  const width = row['to.pixelWidth'];
  const height = row['to.pixelHeight'];
  return pixelsWords(typeof width === 'number' ? width : null, typeof height === 'number' ? height : null) ?? '';
}

/** What an export writes for where a target ends: the engine's own columns for it. */
const ENDS_AT_EXPORT = ['to.finalUrl', 'to.finalStatusCode'] as const;

/** A view's columns as an export writes them: each composed column as the engine's columns it's made of. */
export function linkExportShown(shown: readonly string[]): string[] {
  return unfoldedLinkColumns(shown).flatMap((id) =>
    id === PIXELS_COLUMN ? [...PIXELS_PARTS] : id === ENDS_AT_COLUMN ? [...ENDS_AT_EXPORT] : [id],
  );
}

/** The room a URL's cell keeps beside its text: the open-in-browser button, and an image's thumbnail before it. */
const OPEN_BUTTON_ROOM = 24;
const THUMB_ROOM = 40;
/** Beside where a target ends: its status and how it got there. */
const ENDS_AT_ROOM = 136;

/** A URL as its cell shows it, for measuring: its path on the crawl's own site, a file by its name. */
function shownUrl(url: string, siteOrigin: string | null, file: boolean): string {
  if (!file) return urlDisplay(url, siteOrigin).shown;
  const label = fileLabelOf(url, siteOrigin);
  return label.rest ? `${label.name} ${label.rest}` : label.name;
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
  const name = (context.images ? IMAGE_NAMES[id] : undefined) ?? linkColumnName(id);
  const type: UrlColumnType = column?.type ?? 'text';
  const sorted = context.sort.find((key) => key.column === id);
  const base = {
    id,
    header: name,
    width: context.widths?.[id] ?? widthOf(id, type),
    ...(column?.sortable
      ? { sortable: true, ...(sorted ? { sort: sorted.desc ? ('descending' as const) : ('ascending' as const) } : {}) }
      : { sortHint: sortHint(name) }),
  };
  const end = endColumn(id);

  if (id === PIXELS_COLUMN) {
    return {
      ...base,
      cell: (row) => pixelsCellWords(row) || muted('–'),
      text: pixelsCellWords,
    };
  }
  if (id === LINK_FACTS_COLUMN) {
    return {
      ...base,
      headerHint: 'What the link is, where on its page it sits, and whether search engines follow it',
      cell: (row) =>
        linkFactsWords(row) ? <span title={linkFactsWords(row)}>{linkFactsWords(row)}</span> : muted('–'),
      text: linkFactsWords,
    };
  }
  if (id === ENDS_AT_COLUMN) {
    return {
      ...base,
      fit: 'url',
      fitText: (row) => {
        const ends = endsAtOf(row);
        return ends ? urlDisplay(ends.url, context.siteOrigin).shown : '';
      },
      fitExtra: OPEN_BUTTON_ROOM + ENDS_AT_ROOM,
      headerHint: 'Where a target that redirects ends, how that answered, and how it got there',
      cell: (row) => {
        const ends = endsAtOf(row);
        if (!ends) return muted('–');
        return (
          <span className="flex min-w-0 items-center gap-2">
            <Url
              url={ends.url}
              siteOrigin={context.siteOrigin}
              tabbable={false}
              copyable={false}
              onGo={() => context.onOpen(ends.id, 'to')}
            />
            <StatusCode code={ends.statusCode} className="shrink-0" />
            {ends.note ? <span className="shrink-0 text-text-muted">{ends.note}</span> : null}
          </span>
        );
      },
      text: endsAtWords,
    };
  }
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
    const other = end.end === 'from' ? 'to' : 'from';
    // A target that's a file a page loads (an image, a stylesheet) is named by its file, never cut in its name.
    const isFile = (row: LinkRow) =>
      end.end === 'to' &&
      (context.images === true || (typeof row.kind === 'string' && RESOURCE_LINK_KINDS.has(row.kind)));
    return {
      ...base,
      ...(context.widths?.[id] === undefined && context.urlWidth ? { width: context.urlWidth } : {}),
      // As wide as the longest URL among the rows read first, as it shows (grid/columnFit.ts).
      fit: 'url',
      fitText: (row) => {
        const url = row[`${end.end}.url`];
        return typeof url === 'string' ? shownUrl(url, context.siteOrigin, isFile(row)) : '';
      },
      fitExtra: OPEN_BUTTON_ROOM + (context.images && end.end === 'to' ? THUMB_ROOM : 0),
      cell: (row) => {
        const url = row[`${end.end}.url`];
        const urlId = row[`${end.end}.id`];
        if (typeof url !== 'string') return '';
        const file = isFile(row);
        const otherId = row[`${other}.id`];
        // The page a link is on opens in the browser at the link itself, where its words can be found there.
        const atLink = end.end === 'from' ? sourceAtLink(row) : undefined;
        const link = (
          <Url
            url={url}
            siteOrigin={context.siteOrigin}
            tabbable={false}
            copyable={false}
            label={file ? 'file' : 'address'}
            {...(atLink ? { openUrl: atLink, openHint: OPEN_AT_LINK_HINT } : {})}
            {...(typeof urlId === 'number'
              ? { onGo: () => context.onOpen(urlId, end.end, typeof otherId === 'number' ? otherId : undefined) }
              : {})}
          />
        );
        if (!context.images || end.end !== 'to') return link;
        // An image is known by sight before by its address: its picture beside it, the address always there too.
        const status = row['to.statusCode'];
        const outcome = row['to.outcome'];
        const broken = isBrokenImage({
          statusCode: typeof status === 'number' ? status : null,
          outcome: typeof outcome === 'string' ? (outcome as UrlOutcome) : null,
        });
        return (
          <span className="flex min-w-0 items-center gap-2">
            <ImageThumb url={url} broken={broken} />
            <span className="min-w-0 flex-1">{link}</span>
          </span>
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
        const words = endStatusWords(row, end.end, context.crawling);
        return (
          <span className="flex min-w-0" title={words}>
            <StatusCode code={null} reason={words} />
          </span>
        );
      },
      text: (row) => endStatusWords(row, end.end, context.crawling),
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
    case 'object': {
      // An end's redirect chain or robots.txt rule, in one line: whole in its tooltip.
      const raw = (row: LinkRow) => (row as unknown as Record<string, unknown>)[id];
      const words = (row: LinkRow) => objectCellWords(id, raw(row), context.siteOrigin);
      return {
        ...base,
        cell: (row) => (words(row) ? <span title={words(row)}>{words(row)}</span> : muted('–')),
        text: (row) => objectCellWords(id, raw(row), null),
      };
    }
    case 'text': {
      const words = (row: LinkRow) => {
        const raw = value(row);
        return raw === null || raw === undefined ? '' : String(raw);
      };
      if (end !== null && URL_TEXT.has(end.column)) {
        // Another URL an end names (where it redirects, where that ends, its canonical): a URL like any other, and
        // a link to it where the crawl knows which of its URLs it is (the redirect chain's).
        const idOf = (row: LinkRow): number | undefined => {
          const chain = CHAIN_URLS.has(end.column) ? chainOf(row, end.end) : null;
          const address = words(row);
          return [...(chain?.hops ?? []), ...(chain ? [chain.final] : [])].find((hop) => hop.url === address)?.id;
        };
        return {
          ...base,
          fit: 'url',
          fitText: (row) => (words(row) ? urlDisplay(words(row), context.siteOrigin).shown : ''),
          fitExtra: OPEN_BUTTON_ROOM,
          cell: (row) => {
            const address = words(row);
            if (!address) return muted('–');
            const urlId = idOf(row);
            return (
              <Url
                url={address}
                siteOrigin={context.siteOrigin}
                tabbable={false}
                copyable={false}
                {...(urlId === undefined ? {} : { onGo: () => context.onOpen(urlId, end.end) })}
              />
            );
          },
          text: words,
        };
      }
      return {
        ...base,
        ...(id === 'text' ? { fit: 'text' as const } : {}),
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
  /** The widths the user gave columns (the grid's column layout): the room they take is theirs. */
  widths: Readonly<Record<string, number>> = {},
): number | undefined {
  if (available === null) return undefined;
  const byId = new Map((catalogue ?? []).map((column) => [column.id, column]));
  const urls = shown.filter((id) => sharesAsUrl(id) && widths[id] === undefined);
  if (urls.length === 0) return undefined;
  const others = shown
    .filter((id) => !urls.includes(id))
    .reduce((sum, id) => sum + (widths[id] ?? widthOf(id, byId.get(id)?.type ?? 'text')), 0);
  return Math.max(URL_MIN_WIDTH, Math.floor((available - others) / urls.length));
}

/** The columns that share the grid's room as URLs do: each end's URL, and where a target ends. */
const sharesAsUrl = (id: string): boolean => endColumn(id)?.column === 'url' || id === ENDS_AT_COLUMN;

/** The least the anchor or alt text is given where the room is shared by what the view's values take: its header whole. */
const TEXT_MIN_WIDTH = 140;
/** The most a URL column, or the anchor or alt text, asks for: past it, a value is cut, and its tooltip has it. */
const URL_MAX_NEED = 720;
const TEXT_MAX_NEED = 360;
/** A cell's padding on both sides (`--grid-cell-x`), and the room a URL keeps at its end for the open button. */
const CELL_PADDING = 24;
const URL_BUTTON_ROOM = 24;

/** Which face a value is drawn in: a URL's monospace, or the cells' text. */
export type LinkFace = 'url' | 'text';

/**
 * How wide a value draws in the Links grid: a URL in the cells' monospace (13 px at the URL face's 0.93), anything else
 * in the cells' own font. The fonts are read once, for one view's measuring. `textWeight` is the weight the text is set
 * in where it isn't the cells' own (a tree's group rows are heavier, and so wider).
 */
export function linkTextMeasure(textWeight?: string): (text: string, face: LinkFace) => number {
  const size = Number.parseFloat(tokenValue('--grid-font-size', '13px')) || 13;
  const urlFont = `${(size * 0.93).toFixed(2)}px ${tokenValue('--font-mono', 'monospace')}`;
  const textFont = `${textWeight ? `${textWeight} ` : ''}${size}px ${tokenValue('--font-sans', 'sans-serif')}`;
  return (text, face) => textWidth(text, face === 'url' ? urlFont : textFont);
}

/**
 * What the URL columns and the anchor or alt text take to show their values in view whole: the widest of them, as
 * `measure` draws it, with the cell's padding (and a URL's room for its open button). A URL is measured as it shows,
 * its path where it's on the crawl's own origin. Columns with nothing in view are left out.
 */
export function linkColumnNeeds(
  rows: readonly LinkRow[],
  shown: readonly string[],
  siteOrigin: string | null,
  measure: (text: string, face: LinkFace) => number,
): Record<string, number> {
  const needs: Record<string, number> = {};
  for (const id of shown) {
    const url = sharesAsUrl(id);
    if (!url && id !== 'text') continue;
    let widest = 0;
    for (const row of rows) {
      const value = id === ENDS_AT_COLUMN ? endsAtOf(row)?.url : row[id as keyof LinkRow];
      if (typeof value !== 'string' || value === '') continue;
      widest = Math.max(widest, measure(url ? urlDisplay(value, siteOrigin).shown : value, url ? 'url' : 'text'));
    }
    if (widest > 0) {
      needs[id] = Math.ceil(
        widest + CELL_PADDING + (url ? URL_BUTTON_ROOM : 0) + (id === ENDS_AT_COLUMN ? ENDS_AT_ROOM : 0),
      );
    }
  }
  return needs;
}

/**
 * The widths of the columns that share the grid's room by what their values take (`linkColumnNeeds`): the URL
 * columns, and the anchor or alt text once its values are known. The other columns keep their own widths, and a
 * column the user sized keeps theirs. With room for every value whole, each takes what it needs and the URLs share
 * the rest, so a path is never cut in the middle while the grid has room to spare; short of room, each gets its least
 * and the rest in proportion to what it wants past that. A URL with nothing in view wants what it always did, so the
 * URLs share the room as `sharedUrlWidth` shares it. Undefined while the grid's width isn't known, or with nothing to share.
 */
export function fittedLinkWidths(
  shown: readonly string[],
  catalogue: readonly LinkColumn[] | undefined,
  available: number | null,
  /** The widths the user gave columns (the grid's column layout): the room they take is theirs. */
  widths: Readonly<Record<string, number>> = {},
  needs: Readonly<Record<string, number>> = {},
): Record<string, number> | undefined {
  if (available === null) return undefined;
  const byId = new Map((catalogue ?? []).map((column) => [column.id, column]));
  const isUrl = sharesAsUrl;
  const flexible = shown.filter(
    (id) => widths[id] === undefined && (isUrl(id) || (id === 'text' && needs[id] !== undefined)),
  );
  if (flexible.length === 0) return undefined;
  const room =
    available -
    shown
      .filter((id) => !flexible.includes(id))
      .reduce((sum, id) => sum + (widths[id] ?? widthOf(id, byId.get(id)?.type ?? 'text')), 0);
  const least = (id: string) => (isUrl(id) ? URL_MIN_WIDTH : TEXT_MIN_WIDTH);
  const wanted = (id: string) =>
    Math.max(least(id), Math.min(isUrl(id) ? URL_MAX_NEED : TEXT_MAX_NEED, needs[id] ?? URL_WIDTH));
  const sum = (part: (id: string) => number) => flexible.reduce((total, id) => total + part(id), 0);
  let width: (id: string) => number;
  if (sum(wanted) <= room) {
    // Room for every value whole: the URLs share what's left, or the anchor takes it where no URL is shown.
    const takers = flexible.some(isUrl) ? flexible.filter(isUrl) : flexible;
    const slack = room - sum(wanted);
    width = (id) => wanted(id) + (takers.includes(id) ? slack / takers.length : 0);
  } else if (room <= sum(least)) {
    width = least;
  } else {
    const spare = room - sum(least);
    const over = sum((id) => wanted(id) - least(id));
    width = (id) => least(id) + (over > 0 ? (spare * (wanted(id) - least(id))) / over : spare / flexible.length);
  }
  return Object.fromEntries(flexible.map((id) => [id, Math.floor(width(id))]));
}

const NEEDS_ANALYSIS = 'needs an analysis';

/** The chooser's groups: the link, its source page, its target, each with the columns that can be shown. */
export function linkChooserGroups(catalogue: readonly LinkColumn[], summaryAvailable: boolean): ChooserGroup[] {
  // The pixel size column stands where the target's width would.
  const ids = [
    FOLLOW_COLUMN,
    // Where a target ends stands before the chain it's read from.
    ...catalogue.flatMap((column) =>
      column.id === PIXELS_PARTS[0]
        ? [PIXELS_COLUMN]
        : column.id === ENDS_AT_PARTS[0]
          ? [ENDS_AT_COLUMN, column.id]
          : [column.id],
    ),
  ].filter((id) => isLinkColumn(id, catalogue));
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
