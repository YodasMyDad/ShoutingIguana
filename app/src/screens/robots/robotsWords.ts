// The robots.txt screen's words: what an origin's answer meant to the crawl, why a URL was blocked, what a line is.
// Each state has its own true sentence, by the engine's values (docs/v2-sidecar-api.md, robots.txt): a file that
// didn't answer is `unreachable` (the origin's URLs were refused) or `unanswered` (the crawl went ahead), and the two
// never share words. Pure, so every wording is unit-tested.
import type {
  GridFilter,
  RobotsBlockedUrl,
  RobotsFile,
  RobotsLine,
  RobotsLineKind,
  RobotsLineProblem,
  RobotsOrigin,
  RobotsOriginPage,
} from '../../api/grids';
import { formatCount, formatCountOf } from '../../lib/format';
import { sentenceText, type SentencePart } from '../countSentence';

export type RobotsTone = 'read' | 'nothing' | 'refused' | 'unknown';

export interface StateWords {
  /** A few words, for a cell. */
  short: string;
  /** The whole of it, as a sentence. */
  sentence: string;
  tone: RobotsTone;
}

type Answer = Pick<RobotsOrigin, 'state' | 'statusCode' | 'error' | 'bytes'>;

/** How the file answered, in the engine's own terms: its status, else how it failed. Null when neither is stored. */
function answerOf({ statusCode, error }: Pick<RobotsOrigin, 'statusCode' | 'error'>): string | null {
  if (statusCode !== null) return `HTTP ${statusCode}`;
  return error && error.trim() !== '' ? error : null;
}

/**
 * Whether the crawl obeyed robots.txt: true unless it was told to ignore it (`respected` of the list of origins), and
 * null when that couldn't be read, where the words say no more than holds either way.
 */
export type Obeyed = boolean | null;

/**
 * What an origin's robots.txt meant to the crawl. Four states, four sentences: read (its rules applied), missing
 * (nothing is refused), unreachable (the crawl refused the origin's URLs) and unanswered (the crawl went ahead). A
 * crawl told to ignore robots.txt obeyed no rule and refused nothing, whatever the file answered: its words say so.
 */
export function stateWords(origin: Answer, userAgent: string, obeyed: Obeyed = true): StateWords {
  const answer = answerOf(origin);
  const how = answer ? ` (${answer})` : '';
  switch (origin.state) {
    case 'read':
      if (origin.bytes === 0) {
        return {
          tone: 'nothing',
          short: 'Read: empty',
          sentence: 'The crawl read this file, and it’s empty, so nothing on this origin is refused.',
        };
      }
      if (obeyed === false) {
        return {
          tone: 'nothing',
          short: 'Read: rules ignored',
          sentence: 'The crawl read this file, and was told to ignore its rules.',
        };
      }
      return {
        tone: 'read',
        short: 'Read',
        sentence:
          obeyed === null
            ? 'The crawl read this file.'
            : `The crawl read this file and obeyed its rules for ${userAgent}.`,
      };
    case 'missing':
      return {
        tone: 'nothing',
        short: 'Missing: nothing refused',
        sentence: `There’s no robots.txt here${how}, so nothing on this origin is refused.`,
      };
    case 'unreachable':
      if (obeyed === false) {
        return {
          tone: 'nothing',
          short: 'Couldn’t be read: ignored',
          sentence: `robots.txt couldn’t be read${how}. The crawl was told to ignore robots.txt, so it refused nothing.`,
        };
      }
      return obeyed === null
        ? { tone: 'unknown', short: 'Couldn’t be read', sentence: `robots.txt couldn’t be read${how}.` }
        : {
            tone: 'refused',
            short: 'Couldn’t be read: URLs refused',
            sentence: `robots.txt couldn’t be read${how}, so the crawl refused this origin’s URLs until it could.`,
          };
    case 'unanswered':
      return obeyed === false
        ? {
            tone: 'unknown',
            short: 'No answer: ignored',
            sentence: `robots.txt didn’t answer${how}. The crawl was told to ignore robots.txt, so it went ahead.`,
          }
        : {
            tone: 'unknown',
            short: 'No answer: crawled anyway',
            sentence: `robots.txt didn’t answer${how}. The crawl gave no verdict and went ahead: this origin’s URLs show their own errors.`,
          };
    default:
      return {
        tone: 'unknown',
        short: 'None kept',
        sentence: 'The crawl kept no robots.txt for this origin.',
      };
  }
}

/** The answer first, over the list of origins. */
export function answerParts(
  page: Pick<RobotsOriginPage, 'total' | 'blocked' | 'respected' | 'userAgent'>,
): SentencePart[] {
  if (page.total === 0) return ['The crawl asked no origin for its robots.txt.'];
  const asked = `The crawl asked ${formatCountOf(page.total, 'origin')} for robots.txt, as ${page.userAgent}.`;
  if (!page.respected) return [asked];
  if (page.blocked === 0) return [`${asked} None of them kept it from a URL.`];
  return [
    `${asked} ${page.total === 1 ? 'It' : 'They'} kept it from `,
    // Every URL the crawl stored as refused by robots.txt, on whatever origin: what the origins' counts add up to.
    { text: formatCountOf(page.blocked, 'URL'), filters: REFUSED_BY_ROBOTS },
    '.',
  ];
}

/** The answer as it reads. */
export function answerSentence(page: Pick<RobotsOriginPage, 'total' | 'blocked' | 'respected' | 'userAgent'>): string {
  return sentenceText(answerParts(page));
}

/** The URL grid's filter for the URLs robots.txt kept the crawl from: the engine's own word for them. */
export const REFUSED_BY_ROBOTS: readonly GridFilter[] = [{ column: 'outcome', op: 'in', values: ['robotsDisallowed'] }];

/** Why an origin's list of blocked URLs is empty, by what its robots.txt meant to the crawl. */
export function nothingBlockedWords(file: Pick<RobotsFile, 'state' | 'lines'>, obeyed: Obeyed = true): string {
  // Told to ignore robots.txt, the crawl refused nothing, whatever the file says: no rule was "obeyed".
  if (obeyed === false) return 'This crawl was told to ignore robots.txt, so nothing was refused.';
  const none = 'The crawl stored no URL on this origin as refused.';
  switch (file.state) {
    case 'read':
      if (file.lines.length === 0) return 'The file is empty, so it refuses nothing.';
      return obeyed === null
        ? none
        : 'None of the URLs the crawl came to on this origin is refused by the rules it obeyed.';
    case 'missing':
      return 'There’s no robots.txt here, so nothing is refused.';
    case 'unanswered':
      return 'robots.txt didn’t answer, and the crawl went ahead: nothing was refused.';
    case 'unreachable':
      return obeyed === null ? none : 'The crawl came to no URL on this origin while its robots.txt couldn’t be read.';
    default:
      return none;
  }
}

/** A crawl told to ignore robots.txt: said at the top, so an empty list of blocked URLs isn't read as "allowed". */
export const IGNORED =
  'This crawl was told to ignore robots.txt, so nothing was blocked. It still read the site’s own, for the sitemaps it declares.';

export interface BlockedWhy {
  /** For the list's cell: "Line 2: Disallow: /private/". */
  short: string;
  /** In full, for the tooltip and the note over the list. */
  sentence: string;
}

/** Why a URL was blocked: the line that decided it, or which of the two cases no line accounts for. */
export function blockedWhy(row: Pick<RobotsBlockedUrl, 'reason' | 'line' | 'rule' | 'error'>): BlockedWhy {
  switch (row.reason) {
    case 'rule': {
      const line = row.line === null ? 'A rule' : `Line ${formatCount(row.line)}`;
      return {
        short: row.rule === null ? line : `${line}: ${row.rule}`,
        sentence: row.rule === null ? `${line} of this file refused it.` : `${line} refused it: ${row.rule}`,
      };
    }
    case 'unreachable':
      return {
        short: 'robots.txt couldn’t be read',
        sentence: `robots.txt couldn’t be read when the crawl came to this URL${row.error ? ` (${row.error})` : ''}, which refuses everything.`,
      };
    default:
      return { short: 'No rule kept', sentence: UNKNOWN_REASON };
  }
}

/** A URL stored as refused that nothing kept accounts for. No more is said than is known. */
export const UNKNOWN_REASON =
  'The crawl stored this URL as refused by robots.txt, but the robots.txt it kept for this origin doesn’t refuse it. Only the last answer is kept, so what refused it is gone.';

/** The two cases no line accounts for, as the words of the link that lists their URLs. */
export const UNMATCHED_WORDS = {
  unreachable: (count: number) =>
    `${formatCountOf(count, 'URL')} ${count === 1 ? 'was' : 'were'} refused while robots.txt couldn’t be read`,
  unknown: (count: number) =>
    `${formatCountOf(count, 'URL')} ${count === 1 ? 'is' : 'are'} stored as refused, though nothing kept here refuses ${count === 1 ? 'it' : 'them'}`,
} as const;

/** Why the crawler made nothing of a line, whatever the line is. `lineProblemWords` says it by the line's kind. */
export const LINE_PROBLEM_WORDS: Record<RobotsLineProblem, string> = {
  noUserAgent: 'Ignored: it comes before any User-agent line, so it belongs to no group',
  invalidValue: 'Ignored: its value can’t be used',
  unknownDirective: 'Not used by the crawl',
  notADirective: 'Ignored: not a “name: value” line',
};

/**
 * What's wrong with a value depends on what the line is for: the crawler refuses a `Crawl-delay` that isn't a number
 * or is below 0, and a `Sitemap:` line with nothing after it (RobotsTxtParser).
 */
const INVALID_VALUE_WORDS: Partial<Record<RobotsLineKind, string>> = {
  crawlDelay: 'Ignored: its value isn’t a number of seconds, 0 or more',
  sitemap: 'Ignored: it names no sitemap',
};

/** Why the crawler made nothing of a line, by the line's kind. */
export function lineProblemWords(line: Pick<RobotsLine, 'kind' | 'problem'>): string | null {
  if (line.problem === null) return null;
  if (line.problem === 'invalidValue') return INVALID_VALUE_WORDS[line.kind] ?? LINE_PROBLEM_WORDS.invalidValue;
  // A problem a newer engine names has no words here: none are guessed at.
  const words: Partial<Record<string, string>> = LINE_PROBLEM_WORDS;
  return words[line.problem] ?? null;
}
