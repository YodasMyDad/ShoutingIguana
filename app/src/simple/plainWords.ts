// The words Simple's results build from the engine's numbers (docs/v2-ui-simple.md, "The Summary", "What to fix",
// "The score", "Plain words"). Every sentence here is the app's, made from counts only; a check's own words (its plain
// title, what's wrong, how to fix it, the good news) are the engine's, from `GET /api/checks`. Pure, so every rule is
// tested.
import type { Analysis, Check, CheckFixer, Score, ScoreBand, ScoreExtent, ScoreLine, Severity } from '../api/types';
import { formatCount, formatList } from '../lib/format';
import { plainSeverityCount } from '../ui/severity';
import { scoreWord } from '../ui/score';

/** A check's title in Simple: its plain title, or the technical one for a check newer than its plain words. */
export function plainTitle(check: Check): string {
  return check.plainTitle ?? check.title;
}

/**
 * The first sentence of what's wrong, which stands alone in lists: the plain one, or the technical description for a
 * check with no plain words.
 */
export function plainSummary(check: Check): string {
  return check.plainWhat?.[0] ?? check.description;
}

/** A passed check's good news, or its title for a check with no plain words. */
export function passedWords(check: Check): string {
  return check.plainPassed ?? check.title;
}

/** What a skipped check looks for, where its title is a sentence about the site that reads as a finding. */
export function looksForWords(check: Check): string {
  return check.plainLooksFor ?? plainTitle(check);
}

const FIXERS: Record<CheckFixer, string> = {
  you: 'Usually fixed by you, in your site’s editor',
  developer: 'Usually fixed by your developer or hosting company',
  either: 'Usually fixed by you or your developer',
};

/** Who usually makes the fix: "Usually fixed by you, in your site's editor". Null when the check doesn't say. */
export function fixerWords(fixer: CheckFixer | null | undefined): string | null {
  return fixer ? FIXERS[fixer] : null;
}

// --------------------------------------------------------------------------------------------- Counts and shares

/** What a check's count is out of: the pages read or the addresses checked, as the analysis counted them. */
export interface Wholes {
  pages: number | null;
  urls: number | null;
}

/** The analysis's own totals: the score's, else the analysis's (an analysis from before the score has no URL total). */
export function wholesOf(analysis: Pick<Analysis, 'pagesAnalyzed' | 'crawledUrlCount' | 'score'>): Wholes {
  return {
    pages: analysis.score?.covers.pages ?? analysis.pagesAnalyzed,
    urls: analysis.score?.covers.urls ?? analysis.crawledUrlCount ?? null,
  };
}

/** "5 of 265 pages" in parts, the count first so it can be bold, or a whole-site fault's words alone. */
export interface ShareWords {
  /** "5"; null for a fault of the whole site, which has no count to show. */
  count: string | null;
  /** " of 265 pages", or "The whole site". */
  rest: string;
  /** The two together: the accessible name, and what Copy writes. */
  text: string;
}

/**
 * A check's count and what it is a share of (docs/v2-ui-simple.md, "Simple's words"): pages for a check whose subject
 * is `page`, addresses for one that can flag any URL. A fault of the whole site is "The whole site"; a check reported
 * once for the site isn't a share of the pages.
 */
export function shareWords(check: Check, count: number, wholes: Wholes): ShareWords {
  if (check.siteWide) return { count: null, rest: 'The whole site', text: 'The whole site' };
  if (check.reportedOnce) return { count: null, rest: 'Once, for the whole site', text: 'Once, for the whole site' };
  const pages = (check.subject ?? 'page') === 'page';
  const whole = pages ? wholes.pages : wholes.urls;
  const noun = pages ? (whole === 1 || (whole === null && count === 1) ? 'page' : 'pages') : 'addresses';
  const single = !pages && (whole === 1 || (whole === null && count === 1)) ? 'address' : noun;
  const rest = whole === null ? ` ${single}` : ` of ${formatCount(whole)} ${single}`;
  return { count: formatCount(count), rest, text: `${formatCount(count)}${rest}` };
}

/**
 * A row's count in a list of problems, without what it's a share of: "9 addresses", "5 pages", "The whole site". The
 * rows of a list then compare (the UX audit, X6: "9 of 4,521 addresses" beside "5 of 265 pages" didn't); the share is
 * on the problem's own page.
 */
export function rowCountWords(check: Check, count: number): ShareWords {
  if (check.siteWide) return { count: null, rest: 'The whole site', text: 'The whole site' };
  if (check.reportedOnce) return { count: null, rest: 'Once, for the whole site', text: 'Once, for the whole site' };
  const pages = (check.subject ?? 'page') === 'page';
  const rest = pages ? (count === 1 ? ' page' : ' pages') : count === 1 ? ' address' : ' addresses';
  return { count: formatCount(count), rest, text: `${formatCount(count)}${rest}` };
}

/** The points fixing a problem gives back, on its row: "4 points back", or nothing when it gives none. */
export function rowPointsWords(points: number | null | undefined): string | null {
  if (!points) return null;
  return `${formatCount(points)} ${points === 1 ? 'point' : 'points'} back`;
}

// ---------------------------------------------------------------------------------------------------- The score

/** The score as a word: "Good", or "Good so far" for a crawl that didn't read all of the site. The ring's words. */
export function bandWord(band: ScoreBand, partial = false): string {
  return scoreWord(band, partial);
}

const EXTENTS: Record<ScoreExtent, string> = {
  few: 'a few',
  some: 'some',
  many: 'many',
  most: 'most',
  site: 'the whole site',
};

/** How much of the site a line is, as the breakdown says it: "5 of 265 pages: some", "The whole site". */
export function lineShareWords(line: ScoreLine): string {
  if (line.unit === 'site') return 'The whole site';
  const noun = line.unit === 'pages' ? (line.of === 1 ? 'page' : 'pages') : line.of === 1 ? 'address' : 'addresses';
  return `${formatCount(line.count)} of ${formatCount(line.of)} ${noun}: ${EXTENTS[line.extent]}`;
}

/** What a check does to the score: "Takes 4 points off the score", or "Doesn't change the score". */
export function pointsWords(line: ScoreLine | undefined, severity: Severity): { points: number | null; text: string } {
  if (severity === 'notice' || !line || line.points === 0) return { points: null, text: 'Doesn’t change the score' };
  return {
    points: line.points,
    text: `Takes ${formatCount(line.points)} ${line.points === 1 ? 'point' : 'points'} off the score`,
  };
}

/** Checks with problems, by severity, as the Summary counts them: hidden checks left out. */
export type ProblemCounts = Record<Severity, number>;

/** "There are 2 serious problems and 4 problems worth fixing.", from the two counts that need fixing. */
function problemsSentence(counts: ProblemCounts, lead: 'there' | 'soFar'): string {
  const parts = (['error', 'warning'] as const)
    .filter((severity) => counts[severity] > 0)
    .map((severity) => plainSeverityCount(severity, counts[severity]));
  const first = (['error', 'warning'] as const).find((severity) => counts[severity] > 0);
  const verb = first && counts[first] === 1 ? 'is' : 'are';
  const opening = lead === 'soFar' ? `On the pages read so far there ${verb}` : `There ${verb}`;
  return `${opening} ${formatList(parts)}.`;
}

const BAND_LEADS: Record<ScoreBand, string> = {
  veryGood: 'Your site is in very good shape.',
  good: 'Your site is in good shape.',
  needsWork: 'Your site needs some work.',
  poor: 'Your site has problems that will hold it back in search.',
};

/**
 * The Summary's sentence, built from counts only (docs/v2-ui-simple.md, "The Summary"): the band's lead, then the
 * problems; a serious fault of the whole site leads with its own title; a crawl that didn't read all of the site
 * speaks of the pages read so far; nothing to fix says so, with the things worth a look.
 */
export function summarySentence({
  band,
  partial,
  counts,
  siteWideSerious,
  hidden = 0,
}: {
  band: ScoreBand;
  partial: boolean;
  counts: ProblemCounts;
  /** The plain title of a serious problem of the whole site that stands, which leads the sentence. */
  siteWideSerious: string | null;
  /** The checks the user hid in Advanced: with any, nothing to fix is said of the checks shown only. */
  hidden?: number;
}): string {
  if (counts.error + counts.warning === 0) {
    const among = hidden > 0 ? ' among the checks you show' : '';
    const opening = partial
      ? `On the pages read so far, nothing needs fixing${among}.`
      : `Nothing needs fixing${among}.`;
    const things =
      counts.notice === 0
        ? ''
        : ` ${counts.notice === 1 ? '1 thing is' : `${formatCount(counts.notice)} things are`} worth a look when you have a moment.`;
    if (hidden > 0) {
      const nothing =
        counts.notice > 0
          ? opening
          : partial
            ? `Nothing to fix${among} on the pages read so far.`
            : `Nothing to fix${among}.`;
      return `${nothing}${things} ${hiddenWords(hidden)}`;
    }
    if (counts.notice > 0) return `${opening}${things}`;
    return partial
      ? 'Nothing to fix on the pages read so far. Every check that ran found nothing wrong.'
      : 'Nothing to fix. Every check that ran found nothing wrong.';
  }
  const problems = problemsSentence(counts, partial ? 'soFar' : 'there');
  if (siteWideSerious) return `${siteWideSerious}. That holds everything else back. ${problems}`;
  return partial ? problems : `${BAND_LEADS[band]} ${problems}`;
}

/** "1 check is hidden.", "6 checks are hidden.": the checks hidden in Advanced, as What to fix's last line says them. */
export function hiddenWords(hidden: number): string {
  return hidden === 1 ? '1 check is hidden.' : `${formatCount(hidden)} checks are hidden.`;
}

/** Why a partial score covers only part of the site, after "the N pages read". */
const PARTIAL_REASONS: Record<NonNullable<Score['covers']['reason']>, string> = {
  stopped: 'before the check was stopped',
  failed: 'before the check failed',
  urlLimit: 'before the check reached its limit of addresses',
  depthLimit: 'up to the check’s limit of clicks from the home page',
};

/** Fewer pages than this, and the coverage says the site may have more that need JavaScript to reach. */
export const TINY_SITE_PAGES = 10;

/**
 * What the score covers, as sentences (docs/v2-ui-simple.md, "The score": What it covers): the pages and checks it is
 * built from, the checks that couldn't run, what robots.txt kept out, and a tiny site's caution (only when the check
 * read all it found).
 */
export function coverageSentences(covers: Score['covers']): string[] {
  const total = covers.checksRun + covers.checksSkipped;
  const checks =
    covers.checksSkipped === 0
      ? `all ${formatCount(total)} checks`
      : `${formatCount(covers.checksRun)} of ${formatCount(total)} checks`;
  const pages = covers.pages === 1 ? 'page' : 'pages';
  const first =
    covers.complete || covers.reason === null
      ? `The score covers ${covers.pages === 1 ? 'the 1 page' : `all ${formatCount(covers.pages)} pages`} and ${checks}.`
      : `The score covers the ${formatCount(covers.pages)} ${pages} read ${PARTIAL_REASONS[covers.reason]}, and ${checks}.`;
  const sentences = [first];
  if (covers.checksSkipped > 0) {
    sentences.push(
      covers.checksSkipped === 1
        ? '1 check couldn’t run.'
        : `${formatCount(covers.checksSkipped)} checks couldn’t run.`,
    );
  }
  if (covers.blockedByRobots > 0) {
    sentences.push(
      `The site’s rules file for search engines (its “robots.txt”) keeps them out of ${formatCount(covers.blockedByRobots)} ${covers.blockedByRobots === 1 ? 'address, which wasn’t' : 'addresses, which weren’t'} read.`,
    );
  }
  // Only for a check that read all it could find: one that ended early read few pages because it ended.
  if (covers.complete && covers.pages < TINY_SITE_PAGES) {
    sentences.push(
      `Only ${covers.pages === 1 ? '1 page was' : `${formatCount(covers.pages)} pages were`} found. If your site has more, its links may need JavaScript, which this version doesn’t run.`,
    );
  }
  return sentences;
}

const SMALL_NUMBERS = ['', 'one', 'two', 'three', 'four', 'five'];

/** "Fixing these five takes the score from", for as many as there are: the numbers follow, in bold. */
export function ifFixedLead(count: number): string {
  return count === 1
    ? 'Fixing this one takes the score from'
    : `Fixing these ${SMALL_NUMBERS[count] ?? formatCount(count)} takes the score from`;
}

/** The categories with nothing found, in plain words: "visibility in search, languages and web addresses". */
export function categoriesWords(titles: readonly string[]): string {
  return formatList(titles.map((title) => title.charAt(0).toLowerCase() + title.slice(1)));
}

/** A count of checks: "1 check", "63 checks". */
export function checksWord(count: number): string {
  return count === 1 ? '1 check' : `${formatCount(count)} checks`;
}

/** The pages and addresses a check read, as the Summary's last line says them. */
export function factsWords({ pages, urls }: { pages: number; urls: number }): string {
  const read = pages === 1 ? '1 page' : `${formatCount(pages)} pages`;
  const all = urls === 1 ? '1 address' : `${formatCount(urls)} addresses`;
  return `Shouting Iguana read ${read} and checked ${all} in all`;
}

/** Names a list of check titles for one reason they couldn't run: "Pages nothing links to and Pages missing…". */
export function skippedTitles(titles: readonly string[]): string {
  return formatList(
    titles.map((title, index) => (index === 0 ? title : title.charAt(0).toLowerCase() + title.slice(1))),
  );
}
