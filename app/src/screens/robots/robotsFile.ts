// One origin's robots.txt as the viewer shows it, and the link both ways between a blocked URL and the line that
// decided it. The engine says what each line is, whether it's part of the group the crawl obeyed and how many URLs it
// refused (docs/v2-sidecar-api.md, robots.txt): nothing is parsed or matched again here, but for one thing the
// engine doesn't give: the address a Sitemap line names, read from its text, to link the line to that sitemap's page.
// Pure, so it's unit-tested.
import type { RobotsBlockedQuery, RobotsBlockedUrl, RobotsFile, RobotsLine, Sitemap } from '../../api/grids';
import { robotsOriginOf } from '../../inspector/placeLinks';
import type { SourceLineMark } from '../../inspector/sourceLines';
import { formatCount, formatCountOf } from '../../lib/format';
import type { RobotsReasonSearch, RobotsSearch } from '../../routing/search';
import { lineProblemWords, type Obeyed } from './robotsWords';

/** The file's text as the crawl kept it: its lines, joined as they were split. */
export function fileText(lines: readonly Pick<RobotsLine, 'text'>[]): string {
  return lines.map((line) => line.text).join('\n');
}

/** Where a directive's name ends: before its colon. 0 when the line has none. */
function nameEnd(text: string): number {
  const colon = text.indexOf(':');
  return colon > 0 ? colon : 0;
}

/** The address a Sitemap line names: what follows its colon, a comment after it left out. Null when it names none. */
export function sitemapLineUrl(text: string): string | null {
  const colon = text.indexOf(':');
  if (colon <= 0) return null;
  const value = text
    .slice(colon + 1)
    .replace(/\s#.*$/, '')
    .trim();
  return value === '' ? null : value;
}

/** An address as a URL spells it (scheme and host in lower case), so two spellings of one address match. */
function sameAddress(a: string, b: string): boolean {
  if (a === b) return true;
  try {
    return new URL(a).href === new URL(b).href;
  } catch {
    return false;
  }
}

/** The sitemap of the crawl's that a Sitemap line names, or undefined: another kind of line, or one it doesn't have. */
export function sitemapOfLine<S extends Pick<Sitemap, 'id' | 'url'>>(
  line: Pick<RobotsLine, 'kind' | 'text'>,
  sitemaps: readonly S[],
): S | undefined {
  if (line.kind !== 'sitemap') return undefined;
  const url = sitemapLineUrl(line.text);
  return url === null ? undefined : sitemaps.find((sitemap) => sameAddress(sitemap.url, url));
}

/**
 * How the viewer draws each line: the group the crawl obeyed marked and the other groups quiet, a directive's name
 * set apart, a comment as one, why a line was ignored after it, and how many URLs a rule refused, as a link. A
 * Sitemap line that names a sitemap the crawl has (`sitemaps`) is a link to that sitemap's page.
 */
export function fileMarks(
  lines: readonly RobotsLine[],
  sitemaps: readonly Pick<Sitemap, 'id' | 'url'>[] = [],
): SourceLineMark[] {
  return lines.flatMap((line): SourceLineMark[] => {
    if (line.kind === 'blank') return [];
    const mark: SourceLineMark = { line: line.number };
    if (line.kind === 'comment') mark.comment = true;
    else if (line.kind !== 'invalid') {
      const end = nameEnd(line.text);
      if (end > 0) mark.nameEnd = end;
    }
    // A sitemap line belongs to no group: it applies to everyone, and reads as neither obeyed nor passed over.
    if (line.kind !== 'sitemap') {
      if (line.applies === true) mark.tone = 'strong';
      else if (line.applies === false) mark.tone = 'quiet';
    }
    const problem = lineProblemWords(line);
    if (problem !== null) mark.note = problem;
    // A rule that blocked nothing says nothing: a real file has hundreds, and a note on each would bury the few
    // that did. The list beside the file says so when none did.
    if (line.blocked !== null && line.blocked > 0) {
      mark.action = `Blocked ${formatCountOf(line.blocked, 'URL')}`;
      mark.actionTitle = `List the ${formatCountOf(line.blocked, 'URL')} line ${formatCount(line.number)} blocked`;
    }
    const sitemap = sitemapOfLine(line, sitemaps);
    if (sitemap) {
      mark.action = 'Open this sitemap';
      mark.actionTitle = `Open ${sitemap.url} in Sitemaps`;
    }
    return [mark];
  });
}

/** "line 5", "lines 1 to 2": a run of lines, in words. */
const run = ([first, last]: readonly [number, number]) =>
  first === last ? `line ${formatCount(first)}` : `lines ${formatCount(first)} to ${formatCount(last)}`;

/** The runs of lines that are part of the group the crawl obeyed. A sitemap line belongs to no group. */
export function obeyedLines(lines: readonly Pick<RobotsLine, 'number' | 'kind' | 'applies'>[]): [number, number][] {
  const runs: [number, number][] = [];
  let open: [number, number] | null = null;
  for (const line of lines) {
    const obeyed = line.applies === true && line.kind !== 'sitemap';
    if (obeyed) {
      if (open) open[1] = line.number;
      else {
        open = [line.number, line.number];
        runs.push(open);
      }
      // A blank line or a comment inside a group doesn't end it; another group's line does.
    } else if (line.applies === false) open = null;
  }
  return runs;
}

/**
 * Which of the file's lines the crawl obeyed, in a sentence: the marks in the file say the same by their rule. A crawl
 * told to ignore robots.txt (`obeyed` false) obeyed none: the lines marked are the ones it would have. When that isn't
 * known (null), the sentence says whose group they are, and not what the crawl did with them.
 */
export function obeyedWords(
  lines: readonly Pick<RobotsLine, 'number' | 'kind' | 'applies'>[],
  userAgent: string,
  obeyed: Obeyed = true,
): string {
  const runs = obeyedLines(lines);
  if (runs.length === 0) {
    if (!lines.some((line) => line.kind === 'userAgent')) return 'It has no group of rules, so nothing is refused.';
    const none = `No group here is for ${userAgent} or for every crawler (*)`;
    return obeyed === true ? `${none}, so none of its rules applied.` : `${none}, so none of its rules applies.`;
  }
  const words = runs.map(run);
  const listed = words.length > 1 ? `${words.slice(0, -1).join(', ')} and ${words.at(-1) ?? ''}` : (words[0] ?? '');
  if (obeyed === false) {
    return `The crawl was told to ignore robots.txt. As ${userAgent}, it would have obeyed ${listed}.`;
  }
  return obeyed === null ? `The rules for ${userAgent} are ${listed}.` : `As ${userAgent}, the crawl obeyed ${listed}.`;
}

/**
 * What the blocked URLs are narrowed to: one line's, one reason's, the conflicts (a sitemap lists them or a menu links
 * to them, yet robots.txt blocks them), or all of the origin's.
 */
export type Narrowing =
  { by: 'line'; line: number } | { by: 'reason'; reason: RobotsReasonSearch } | { by: 'conflict' } | { by: 'none' };

export function narrowingOf(search: Pick<RobotsSearch, 'line' | 'reason' | 'conflict'>): Narrowing {
  if (search.line !== undefined) return { by: 'line', line: search.line };
  if (search.reason !== undefined) return { by: 'reason', reason: search.reason };
  if (search.conflict === true) return { by: 'conflict' };
  return { by: 'none' };
}

/** The read behind the list of blocked URLs, as the route narrows it. */
export function blockedQueryOf(origin: string, narrowing: Narrowing): Omit<RobotsBlockedQuery, 'offset' | 'limit'> {
  if (narrowing.by === 'line') return { origin, line: narrowing.line };
  if (narrowing.by === 'reason') return { origin, reason: narrowing.reason };
  if (narrowing.by === 'conflict') return { origin, conflict: true };
  return { origin };
}

/** The line that decided a blocked URL, or null when no line did (robots.txt couldn't be read; nothing kept does). */
export function lineOf(row: Pick<RobotsBlockedUrl, 'reason' | 'line'> | undefined): number | null {
  return row?.reason === 'rule' ? row.line : null;
}

/**
 * The line the file marks: the one the list is narrowed to, else the one that decided the URL the list's cursor is
 * on. So choosing a line lists its URLs, and choosing a URL shows its line.
 */
export function markedLine(
  narrowing: Narrowing,
  cursor: Pick<RobotsBlockedUrl, 'reason' | 'line'> | undefined,
): number | null {
  return narrowing.by === 'line' ? narrowing.line : lineOf(cursor);
}

/** A line of the file by its number, or undefined for one the file doesn't have (a link from before a re-crawl). */
export function lineAt(file: Pick<RobotsFile, 'lines'>, number: number): RobotsLine | undefined {
  const at = file.lines[number - 1];
  return at?.number === number ? at : file.lines.find((line) => line.number === number);
}

/**
 * What the list is narrowed to, as its chip reads: "Line is 2: Disallow: /private/". Null for all of them. A link can
 * name any line: one that's no rule (a User-agent line, a comment), or one the file doesn't have. So the chip says
 * "Line", which is true of them all, never "Rule".
 */
export function narrowingWords(
  narrowing: Narrowing,
  file: Pick<RobotsFile, 'lines'> | undefined,
): { field: string; value: string } | null {
  switch (narrowing.by) {
    case 'line': {
      const line = file ? lineAt(file, narrowing.line) : undefined;
      const at = formatCount(narrowing.line);
      const text = line?.text.trim() ?? '';
      return { field: 'Line', value: text === '' ? at : `${at}: ${text}` };
    }
    case 'reason':
      return {
        field: 'Reason',
        value: narrowing.reason === 'unreachable' ? 'robots.txt couldn’t be read' : 'no matching rule',
      };
    case 'conflict':
      return { field: 'Conflict', value: 'in your sitemap or menu but blocked' };
    default:
      return null;
  }
}

/**
 * Why no URLs are listed for the line the list is narrowed to: the file has no such line, the line isn't a rule (only
 * an Allow or a Disallow line is), or it's a rule that blocked nothing. `file` undefined: the file isn't there to
 * say which, so no more is said than that nothing is listed.
 */
export function emptyLineWords(file: Pick<RobotsFile, 'lines'> | undefined, number: number): string {
  const at = formatCount(number);
  if (!file) return `No blocked URLs are listed for line ${at}.`;
  const line = lineAt(file, number);
  if (!line) return `This file has no line ${at}.`;
  return line.kind === 'allow' || line.kind === 'disallow'
    ? `Line ${at} blocked no URLs.`
    : `Line ${at} isn’t a rule, so it blocked no URLs.`;
}

// --------------------------------------------------------------------------------- The file's box where the panes stack

/** A line of the file in the viewer, px, and what its box holds besides the lines: the search, the line under it, its padding. */
export const FILE_LINE_PX = 18;
const FILE_CHROME_PX = 108;
/** The fewest lines' room the box keeps: what its empty state needs, where there's no text to show. */
const FILE_MIN_LINES = 3;

/**
 * How tall the file's box is where the two panes stack (a narrow window), px: as tall as its lines, so a file of four
 * lines doesn't hold a tall empty box over the blocked URLs. The styles never let it pass the pane's own height
 * (`--robots-pane-height`), past which the file scrolls in its box as before.
 */
export function fileBoxHeight(lines: number): number {
  return FILE_CHROME_PX + Math.max(lines, FILE_MIN_LINES) * FILE_LINE_PX;
}

// ------------------------------------------------------------------------------------------- The origin in the route

/**
 * The origin the route's `:origin` names (the router encodes and decodes it whole), or null for the list of origins.
 * A link made by hand can spell an origin the crawl has in another way: in capitals, with a slash or a path after it,
 * with its scheme's own port, or with an international name as it's written rather than as it's sent. The engine
 * keeps one spelling (scheme and host in lower case, the host as punycode, no default port), which is a URL's own
 * `origin`: so the param is read as a URL and its origin is what's asked for. What can't be read as an http(s) URL
 * is passed on as it is, and the engine says it has no origin like it (`origin-not-found`).
 */
export function originFromParam(param: string | undefined): string | null {
  if (param === undefined || param.trim() === '') return null;
  return robotsOriginOf(param.trim()) ?? param;
}
