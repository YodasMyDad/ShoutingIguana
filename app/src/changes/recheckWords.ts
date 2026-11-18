// The fix loop's words for a recheck, in both modes (docs/v2-fix-loop.md, section 4, the problem page's card and
// "Every result"; Advanced's head and banner): what it will do and how long it takes, its progress, and what it found
// ("4 of 5 fixed. 1 still has it"). "Couldn't be rechecked" and "not rechecked" are never fixed. Pure.
import type { RecheckAnswers, RecheckPlan, RecheckRefusal, RecheckResult, RecheckStatus } from '../api/fixLoop';
import type { CheckReferences, CheckSubject } from '../api/types';
import { formatCount, formatCountOf } from '../lib/format';
import type { ChangeMode } from './changes';
import { changeDate, clockWords, onWords, thenNow, whenWords } from './changeWords';

/** What a check flags, in a count: pages for a check of pages, addresses for one that can flag any URL. */
export function subjectNoun(subject: CheckSubject | undefined, count: number, mode: ChangeMode = 'simple'): string {
  if (mode === 'advanced') return count === 1 ? 'URL' : 'URLs';
  if ((subject ?? 'page') === 'page') return count === 1 ? 'page' : 'pages';
  return count === 1 ? 'address' : 'addresses';
}

// ---------------------------------------------------------------------------------------------------------- Time

/**
 * A time ahead, rounded as the card says it: "a few seconds", "about 20 seconds", "about a minute", "about 9 minutes",
 * "about 1 hour 20 minutes".
 */
export function aboutWords(ms: number): string {
  const seconds = Math.max(0, ms) / 1_000;
  if (seconds < 15) return 'a few seconds';
  if (seconds < 55) return `about ${Math.round(seconds / 5) * 5} seconds`;
  if (seconds < 90) return 'about a minute';
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `about ${minutes} minutes`;
  const tens = Math.round(minutes / 10) * 10;
  const hours = Math.floor(tens / 60);
  const rest = tens % 60;
  const hourWords = `${hours} ${hours === 1 ? 'hour' : 'hours'}`;
  return rest === 0 ? `about ${hourWords}` : `about ${hourWords} ${rest} minutes`;
}

// ---------------------------------------------------------------------------------------------------------- The ask

/** What a recheck is asked for: the whole check, one of its causes, or a list of URLs (Advanced's selection). */
export type RecheckScope = 'check' | 'group' | 'urls';

/**
 * The button: Simple's "Recheck these 5 pages", "Recheck this page", "Recheck this one (264 pages)" for a cause, "Recheck
 * the 1,000 most important pages" past the cap; Advanced's "Recheck 5 URLs", "Recheck this group (264 URLs)", "Recheck
 * 1,000 of 4,000 URLs".
 */
export function recheckButtonWords(
  plan: Pick<RecheckPlan, 'judged' | 'capped' | 'limit'>,
  subject: CheckSubject | undefined,
  scope: RecheckScope,
  mode: ChangeMode,
): string {
  const judged = plan.judged;
  const taken = plan.capped ? plan.limit : judged;
  if (mode === 'advanced') {
    if (plan.capped) return `Recheck ${formatCount(taken)} of ${formatCountOf(judged, 'URL')}`;
    if (scope === 'group') return `Recheck this group (${formatCountOf(judged, 'URL')})`;
    if (scope === 'urls') return `Recheck ${formatCount(judged)} selected ${judged === 1 ? 'URL' : 'URLs'}`;
    return `Recheck ${formatCountOf(judged, 'URL')}`;
  }
  const noun = subjectNoun(subject, taken);
  if (plan.capped) return `Recheck the ${formatCount(taken)} most important ${noun}`;
  if (scope === 'group') return `Recheck this one (${formatCount(judged)} ${noun})`;
  if (judged === 1) return `Recheck this ${noun}`;
  return `Recheck these ${formatCount(judged)} ${noun}`;
}

/**
 * Simple's card, before: "Recheck these 5 pages to see whether it's gone. It reads only them and the addresses they
 * link to: about 20 seconds. Other pages stay as the check found them on 3 Nov." What else it reads follows the check's
 * references: the pages that link to them (a 404), the addresses they name (a broken link), or nothing more.
 */
export function recheckIntroWords(args: {
  plan: Pick<RecheckPlan, 'judged' | 'capped' | 'limit' | 'notRechecked' | 'estimateMs' | 'suggestFullCheck'>;
  subject: CheckSubject | undefined;
  references: CheckReferences | undefined;
  /** When the crawl was: "Other pages stay as the check found them on 3 Nov". */
  crawledAt: string | null;
  scope: RecheckScope;
}): string[] {
  const { plan, subject, references, crawledAt, scope } = args;
  const taken = plan.capped ? plan.limit : plan.judged;
  const noun = subjectNoun(subject, taken);
  const these =
    scope === 'group'
      ? `Recheck the ${formatCount(plan.judged)} ${subjectNoun(subject, plan.judged)} it's on`
      : plan.capped
        ? `Recheck the ${formatCount(taken)} most important ${noun}`
        : plan.judged === 1
          ? `Recheck this ${noun}`
          : `Recheck these ${formatCount(plan.judged)} ${noun}`;
  const them = taken === 1 ? 'it' : 'them';
  const also =
    references === 'sources'
      ? `${them} and the pages that link to ${them}`
      : references === 'targets'
        ? `${them} and the addresses ${taken === 1 ? 'it links' : 'they link'} to`
        : them;
  const lines = [
    `${these} to see whether it’s gone. It reads only ${also}: ${aboutWords(plan.estimateMs)}. Other pages stay as the check found them ${onWords(crawledAt)}.`,
  ];
  if (plan.capped) {
    lines.push(
      `It rechecks the ${formatCount(taken)} most important of ${formatCount(plan.judged)}. The other ${formatCount(plan.notRechecked)} stay as the check found them.`,
    );
  }
  if (plan.suggestFullCheck) {
    lines.push('That’s most of the site: checking the whole site again takes about as long, and checks everything.');
  }
  return lines;
}

/** Advanced's line under the head's Recheck: "Fetches 8 URLs again (about 20 seconds). Other URLs stay as crawled." */
export function recheckPlanLine(
  plan: Pick<RecheckPlan, 'phase1' | 'phase2Max' | 'estimateMs' | 'capped' | 'notRechecked'>,
): string {
  const most = plan.phase1 + plan.phase2Max;
  const fetches =
    plan.phase2Max > 0
      ? `Fetches ${formatCount(plan.phase1)} URLs again, and up to ${formatCount(most)} with the pages linking to them`
      : `Fetches ${formatCountOf(plan.phase1, 'URL')} again`;
  const capped = plan.capped ? ` ${formatCount(plan.notRechecked)} past the cap stay as crawled.` : '';
  return `${fetches} (${aboutWords(plan.estimateMs)}). Other URLs stay as crawled.${capped}`;
}

/**
 * Why a recheck can't run now, in a mode's words: Simple's plain sentence for each refusal; Advanced's, the sidecar's
 * own title.
 */
export function refusalWords(refused: RecheckRefusal, mode: ChangeMode): string {
  if (mode === 'advanced') return refused.title;
  switch (refused.type) {
    case 'site-busy':
      return 'A check of this site is running. Its results will show what’s fixed.';
    case 'recheck-not-latest':
      return 'This is an earlier check. Open the latest check of this site to recheck it.';
    case 'recheck-unsupported':
      return 'This one needs the whole site read again to see a fix: check the site again.';
    case 'not-analyzed':
      return 'Work out this check’s results first, then recheck it.';
    case 'not-analysable':
      return 'This check was cut short. Continue it first, then recheck it.';
    case 'crawl-busy':
      return 'This check is busy right now. Try again when it’s done.';
    default:
      return refused.title;
  }
}

/** A check a recheck can't settle (`recheck` empty): Simple's card in place of the button. */
export const NEEDS_FULL_CHECK_WORDS = 'This one needs the whole site read again to see a fix: check the site again.';

// ---------------------------------------------------------------------------------------------------------- Running

/**
 * The card's progress line: Simple's "Rechecking… 3 of 8 addresses read", Advanced's "Rechecking: 3 of 8 URLs fetched";
 * "Rechecking…" before its first count; with `analysing`, "Working out the results…" (Advanced: "Analysing…").
 */
export function recheckProgressWords(
  progress: { done: number; total: number; phase: number | null } | null,
  mode: ChangeMode,
  analysing = false,
): string {
  if (analysing) return mode === 'simple' ? 'Working out the results…' : 'Analysing…';
  if (!progress || progress.total === 0) return 'Rechecking…';
  if (mode === 'simple') {
    return `Rechecking… ${formatCount(progress.done)} of ${formatCount(progress.total)} ${progress.total === 1 ? 'address' : 'addresses'} read`;
  }
  const phase = progress.phase === 2 ? ' (the pages linking to them)' : '';
  return `Rechecking: ${formatCount(progress.done)} of ${formatCount(progress.total)} URLs fetched${phase}`;
}

/** The sidebar's "Checking now" entry, and the status announced: "Rechecking gardenfurnituresets.co.uk". */
export function recheckingSiteWords(site: string): string {
  return `Rechecking ${site}`;
}

// ---------------------------------------------------------------------------------------------------------- The result

/** What a settled recheck found, as a kind: each its own sentence. */
export type RecheckResultKind =
  /** Every URL it judged is fixed. */
  | 'allFixed'
  /** Some fixed, some still have it. */
  | 'someFixed'
  /** None fixed: maybe a cache. */
  | 'noneFixed'
  /** None could be judged (no answer, blocked...): never "fixed". */
  | 'noneChecked';

export function recheckResultKind(result: RecheckResult, judged: number): RecheckResultKind {
  const rechecked = judged - result.notRechecked;
  if (rechecked > 0 && result.notChecked >= rechecked && result.fixed === 0) return 'noneChecked';
  if (result.fixed === 0) return 'noneFixed';
  if (result.fixed === judged) return 'allFixed';
  return 'someFixed';
}

/** "1 still has it", "2 still have it". */
function stillWords(still: number): string {
  return `${formatCount(still)} still ${still === 1 ? 'has' : 'have'} it`;
}

/**
 * Simple's result, a sentence or two (docs/v2-fix-loop.md, "Every result"):
 * - "All 5 fixed. This problem is gone from the list." (`cleared`: the check found nothing after it; else "All 5 fixed.")
 * - "4 of 5 fixed. 1 still has it." (`listed`, the card lists the ones that still have it: ending in a colon);
 * - "None of the 5 is fixed yet. If you changed it in the last few minutes, …a saved copy (a cache)…";
 * - "3 of 5 fixed. 2 couldn't be rechecked: the site didn't answer." (`notCheckedWhy` says why, when known);
 * - stopped: "Stopped: 3 of 5 rechecked. 2 fixed, 1 still has it."; the app closed: "Cut short: …";
 * - and, for URLs that have it now and didn't: "And 1 page has it now that didn't before."
 */
export function simpleResultWords(args: {
  status: RecheckStatus;
  judged: number;
  result: RecheckResult;
  subject: CheckSubject | undefined;
  cleared?: boolean;
  listed?: boolean;
  notCheckedWhy?: string | null;
}): string[] {
  const { status, judged, result, subject, cleared = false, listed = false, notCheckedWhy = null } = args;
  const lines: string[] = [];
  const rechecked = judged - result.notRechecked;
  const couldnt =
    result.notChecked > 0
      ? `${formatCount(result.notChecked)} couldn’t be rechecked${notCheckedWhy ? `: ${notCheckedWhy}` : ''}.`
      : '';
  if (status === 'stopped' || status === 'interrupted' || (status === 'failed' && result.notRechecked > 0)) {
    const lead = status === 'stopped' ? 'Stopped' : status === 'failed' ? 'It couldn’t finish' : 'Cut short';
    const parts = [`${formatCount(result.fixed)} fixed`];
    if (result.still > 0) parts.push(stillWords(result.still));
    lines.push(
      `${lead}: ${formatCount(rechecked)} of ${formatCount(judged)} rechecked. ${parts.join(', ')}${listed && result.still > 0 ? ':' : '.'}`,
    );
    if (couldnt) lines.push(couldnt);
  } else {
    switch (recheckResultKind(result, judged)) {
      case 'allFixed':
        lines.push(
          judged === 1
            ? `Fixed.${cleared ? ' This problem is gone from the list.' : ''}`
            : `All ${formatCount(judged)} fixed.${cleared ? ' This problem is gone from the list.' : ''}`,
        );
        break;
      case 'noneFixed':
        lines.push(
          result.notChecked > 0 && result.still === 0
            ? couldnt
            : `${judged === 1 ? 'It isn’t' : `None of the ${formatCount(judged)} is`} fixed yet. If you changed it in the last few minutes, your site may be showing a saved copy (a cache): try again in a few minutes.`,
        );
        if (result.notChecked > 0 && result.still > 0) lines.push(couldnt);
        break;
      case 'noneChecked':
        lines.push(
          `${judged === 1 ? 'It couldn’t' : `None of the ${formatCount(judged)} could`} be rechecked${notCheckedWhy ? `: ${notCheckedWhy}` : ''}.`,
        );
        break;
      case 'someFixed': {
        const head = `${formatCount(result.fixed)} of ${formatCount(judged)} fixed.`;
        if (result.still > 0) lines.push(`${head} ${stillWords(result.still)}${listed ? ':' : '.'}`);
        else lines.push(head);
        if (couldnt) lines.push(couldnt);
        if (result.notRechecked > 0) {
          lines.push(`${formatCount(result.notRechecked)} weren’t rechecked: they stay as the check found them.`);
        }
        break;
      }
    }
  }
  if (result.new > 0) {
    const noun = subjectNoun(subject, result.new);
    lines.push(
      `And ${formatCount(result.new)} ${noun} ${result.new === 1 ? 'has' : 'have'} it now that didn’t before.`,
    );
  }
  return lines;
}

/**
 * Why the site's pages couldn't be rechecked, when it turned them away again, in Simple's words: "the site turned them
 * away again: it asked us to slow down"; "…: its security stopped us"; "…: its robots.txt doesn't allow it"; "the site
 * didn't answer"; or, for more than one reason, "the site turned them away again, or didn't answer". Null when none were
 * (or the recheck is from before answers were kept).
 */
export function refusedAgainWords(answers: RecheckAnswers | null): string | null {
  if (!answers || answers.refused === 0) return null;
  const them = answers.refused === 1 ? 'it' : 'them';
  const reasons = [
    answers.rateLimited > 0 ? 'it asked us to slow down' : null,
    answers.blocked > 0 ? 'its security stopped us' : null,
    answers.robotsTxt > 0 ? 'its robots.txt doesn’t allow it' : null,
  ].filter((reason): reason is string => reason !== null);
  if (answers.noAnswer === answers.refused) return 'the site didn’t answer';
  if (reasons.length === 1 && answers.noAnswer === 0) return `the site turned ${them} away again: ${reasons[0]}`;
  return answers.noAnswer > 0
    ? `the site turned ${them} away again, or didn’t answer`
    : `the site turned ${them} away again`;
}

/** Advanced's why for refusals: "429", "blocked", "robots.txt", "no answer", joined. */
function refusedAgainCodes(answers: RecheckAnswers): string {
  return [
    answers.rateLimited > 0 ? '429' : null,
    answers.blocked > 0 ? 'blocked' : null,
    answers.robotsTxt > 0 ? 'robots.txt' : null,
    answers.noAnswer > 0 ? 'no answer' : null,
  ]
    .filter(Boolean)
    .join(', ');
}

/** The score before and after a recheck: "Score 85 → 88." Null when either is missing. */
export function recheckScoreWords(result: Pick<RecheckResult, 'scoreBefore' | 'scoreAfter'>): string | null {
  if (result.scoreBefore === null || result.scoreAfter === null) return null;
  return `Score ${thenNow(result.scoreBefore, result.scoreAfter)}.`;
}

/** "Rechecked today at 14:02." */
export function recheckedWhenWords(endedAt: string | null, now: number = Date.now()): string | null {
  const when = whenWords(endedAt, now);
  return when ? `Rechecked ${when}.` : null;
}

/**
 * Advanced's result line: "Rechecked 17 Nov 14:02: 4 of 5 fixed, 1 still (/sofas/), 0 new. Score 85 → 88". `stillPaths`
 * names the URLs still flagged when there are few; `answers`, the ones the site refused again: "2 not checked (2 refused
 * again: 429)".
 */
export function advancedResultWords(args: {
  endedAt: string | null;
  status: RecheckStatus;
  judged: number;
  result: RecheckResult;
  stillPaths?: readonly string[];
  answers?: RecheckAnswers | null;
}): string {
  const { endedAt, status, judged, result, stillPaths = [], answers = null } = args;
  const when = endedAt ? `${changeDate(endedAt)} ${clockWords(endedAt)}`.trim() : '';
  const lead = status === 'stopped' ? 'Stopped' : status === 'interrupted' ? 'Cut short' : 'Rechecked';
  const paths = result.still > 0 && stillPaths.length > 0 ? ` (${stillPaths.join(', ')})` : '';
  const parts = [
    `${formatCount(result.fixed)} of ${formatCount(judged)} fixed`,
    `${formatCount(result.still)} still${paths}`,
  ];
  if (result.notChecked > 0) {
    const refused =
      answers && answers.refused > 0
        ? ` (${formatCount(answers.refused)} refused again: ${refusedAgainCodes(answers)})`
        : '';
    parts.push(`${formatCount(result.notChecked)} not checked${refused}`);
  }
  if (result.notRechecked > 0) parts.push(`${formatCount(result.notRechecked)} not rechecked`);
  parts.push(`${formatCount(result.new)} new`);
  const score = recheckScoreWords(result);
  return `${lead}${when ? ` ${when}` : ''}: ${parts.join(', ')}.${score ? ` ${score.slice(0, -1)}` : ''}`;
}

/**
 * Advanced's banner for a recheck that ended without a result: one that didn't run ("The recheck didn’t run: Recheck works
 * on the site’s latest check."), or one that failed, which kept nothing ("The recheck failed, so nothing it fetched was
 * kept: the disk is full").
 */
export function advancedEndedWords(status: RecheckStatus | null, error: string | null): string {
  if (status === 'failed') {
    const reason = error?.replace(/^The recheck failed: /, '') ?? null;
    return `The recheck failed, so nothing it fetched was kept${reason ? `: ${reason}` : '.'}`;
  }
  return `The recheck didn’t run${error ? `: ${error}` : '.'}`;
}

/** The toast when a recheck ends: "Recheck done: 4 of 5 fixed"; "Recheck stopped: 2 of 5 fixed". */
export function recheckToastTitle(status: RecheckStatus, judged: number, result: RecheckResult | null): string {
  const lead =
    status === 'stopped' ? 'Recheck stopped' : status === 'failed' ? 'Recheck couldn’t finish' : 'Recheck done';
  if (!result) return lead;
  if (result.fixed === judged && judged > 0)
    return `${lead}: ${judged === 1 ? 'fixed' : `all ${formatCount(judged)} fixed`}`;
  return `${lead}: ${formatCount(result.fixed)} of ${formatCount(judged)} fixed`;
}

/** Home's row meta: "rechecked 5 on 17 Nov" (the URLs the latest recheck judged). */
export function rechecksMetaWords(last: { judged: number; endedAt: string | null }): string {
  return `rechecked ${formatCount(last.judged)} ${onWords(last.endedAt)}`;
}

/** Advanced's tooltip and "crawl in a line": "Rechecked 3 times, last on 17 Nov"; "Rechecked once, on 17 Nov". */
export function rechecksCountWords(count: number, lastEndedAt: string | null): string {
  if (count === 1) return `Rechecked once, ${onWords(lastEndedAt)}`;
  return `Rechecked ${formatCount(count)} times, last ${onWords(lastEndedAt)}`;
}
