// One origin's robots.txt as the viewer shows it, and the link both ways between a blocked URL and the line that
// decided it. The engine says what each line is, whether it's part of the group the crawl obeyed and how many URLs it
// refused (docs/v2-sidecar-api.md, robots.txt): nothing is parsed or matched again here. Pure, so it's unit-tested.
import type { RobotsBlockedQuery, RobotsBlockedUrl, RobotsFile, RobotsLine } from '../../api/grids';
import type { SourceLineMark } from '../../inspector/sourceLines';
import { formatCount, formatCountOf } from '../../lib/format';
import type { RobotsReasonSearch, RobotsSearch } from '../../routing/search';
import { LINE_PROBLEM_WORDS } from './robotsWords';

/** The file's text as the crawl kept it: its lines, joined as they were split. */
export function fileText(lines: readonly Pick<RobotsLine, 'text'>[]): string {
  return lines.map((line) => line.text).join('\n');
}

/** Where a directive's name ends: before its colon. 0 when the line has none. */
function nameEnd(text: string): number {
  const colon = text.indexOf(':');
  return colon > 0 ? colon : 0;
}

/**
 * How the viewer draws each line: the group the crawl obeyed marked and the other groups quiet, a directive's name
 * set apart, a comment as one, why a line was ignored after it, and how many URLs a rule refused, as a link.
 */
export function fileMarks(lines: readonly RobotsLine[]): SourceLineMark[] {
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
    if (line.problem !== null) mark.note = LINE_PROBLEM_WORDS[line.problem];
    // A rule that blocked nothing says nothing: a real file has hundreds, and a note on each would bury the few
    // that did. The list beside the file says so when none did.
    if (line.blocked !== null && line.blocked > 0) {
      mark.action = `Blocked ${formatCountOf(line.blocked, 'URL')}`;
      mark.actionTitle = `List the ${formatCountOf(line.blocked, 'URL')} line ${formatCount(line.number)} blocked`;
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

/** Which of the file's lines the crawl obeyed, in a sentence: the marks in the file say the same by their rule. */
export function obeyedWords(
  lines: readonly Pick<RobotsLine, 'number' | 'kind' | 'applies'>[],
  userAgent: string,
): string {
  const runs = obeyedLines(lines);
  if (runs.length === 0) {
    return lines.some((line) => line.kind === 'userAgent')
      ? `No group here is for ${userAgent} or for every crawler (*), so none of its rules applied.`
      : 'It has no group of rules, so nothing is refused.';
  }
  const words = runs.map(run);
  const listed = words.length > 1 ? `${words.slice(0, -1).join(', ')} and ${words.at(-1) ?? ''}` : (words[0] ?? '');
  return `As ${userAgent}, the crawl obeyed ${listed}.`;
}

/** What the blocked URLs are narrowed to: one line's, one reason's, or all of the origin's. */
export type Narrowing = { by: 'line'; line: number } | { by: 'reason'; reason: RobotsReasonSearch } | { by: 'none' };

export function narrowingOf(search: Pick<RobotsSearch, 'line' | 'reason'>): Narrowing {
  if (search.line !== undefined) return { by: 'line', line: search.line };
  if (search.reason !== undefined) return { by: 'reason', reason: search.reason };
  return { by: 'none' };
}

/** The read behind the list of blocked URLs, as the route narrows it. */
export function blockedQueryOf(origin: string, narrowing: Narrowing): Omit<RobotsBlockedQuery, 'offset' | 'limit'> {
  if (narrowing.by === 'line') return { origin, line: narrowing.line };
  if (narrowing.by === 'reason') return { origin, reason: narrowing.reason };
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

/** What the list is narrowed to, as its chip reads: "Rule is line 2: Disallow: /private/". Null for all of them. */
export function narrowingWords(
  narrowing: Narrowing,
  file: Pick<RobotsFile, 'lines'> | undefined,
): { field: string; value: string } | null {
  switch (narrowing.by) {
    case 'line': {
      const line = file ? lineAt(file, narrowing.line) : undefined;
      const at = `line ${formatCount(narrowing.line)}`;
      return { field: 'Rule', value: line ? `${at}: ${line.text.trim()}` : at };
    }
    case 'reason':
      return {
        field: 'Reason',
        value: narrowing.reason === 'unreachable' ? 'robots.txt couldn’t be read' : 'no rule kept',
      };
    default:
      return null;
  }
}

// ------------------------------------------------------------------------------------------- The origin in the route

/**
 * The origin the route's `:origin` names, as the engine spells it (the router encodes and decodes it whole), or null
 * for the list of origins. It's never tidied here: the engine knows its own origins, and says when it has none like
 * it (`origin-not-found`).
 */
export function originFromParam(param: string | undefined): string | null {
  return param === undefined || param.trim() === '' ? null : param;
}
