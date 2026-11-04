// "Indexable, and why" (docs/v2-ui-design.md): the engine's four tests (answered 2xx, allowed by robots.txt, no
// noindex, canonical is itself) as a checklist, with the one that failed marked, and the verdict in words. The verdict
// and each test's outcome are the engine's; this only words them, with the evidence each test carries.
import type { IndexabilityReason } from '../api/grids';
import type { IndexabilityTest, IndexabilityTestName, UrlDetail, UrlTarget } from '../api/inspector';
import { reasonPhrase } from './words';

export type TestState = IndexabilityTest['outcome'];

export interface IndexabilityRow {
  test: IndexabilityTestName;
  /** The test as a claim: "Answered 2xx". */
  label: string;
  state: TestState;
  /** What the crawl saw: "Answered 404 Not Found", "noindex in its robots meta tag". */
  detail: string;
  /** The other URL the test names: a canonical's target, an instant meta refresh's. */
  target?: { id: number | null; url: string; statusCode: number | null } | undefined;
}

export interface IndexabilityVerdict {
  state: 'indexable' | 'notIndexable' | 'notJudged';
  /** "Indexable", "Not indexable", "Not judged". */
  title: string;
  /** Why, in a sentence. */
  why: string;
  /** The tests that failed, in order: the first is the verdict's reason. */
  failed: IndexabilityTestName[];
  /** Set when the crawl judged it, not the analysis: the analysis hasn't seen this URL. */
  note: string | null;
  rows: IndexabilityRow[];
}

const LABELS: Record<IndexabilityTestName, string> = {
  answered: 'Answered 2xx',
  robotsTxt: 'Allowed by robots.txt',
  noindex: 'No noindex',
  canonical: 'Canonical is itself',
};

const ORDER: readonly IndexabilityTestName[] = ['answered', 'robotsTxt', 'noindex', 'canonical'];

const REASONS: Record<IndexabilityReason, string> = {
  none: 'Search engines can index it.',
  noindex: 'It asks search engines not to index it.',
  canonicalised: 'Its canonical names another URL, so search engines index that one instead.',
  redirected: 'It redirects, so search engines index where it goes instead.',
  clientError: 'It answers with a client error.',
  serverError: 'It answers with a server error.',
  blockedByRobots: 'robots.txt stops search engines reading it.',
  blocked: 'The server blocked the crawler.',
  unreachable: 'It didn’t answer.',
};

type Subject = Pick<UrlDetail, 'id' | 'url' | 'indexability' | 'response' | 'discovery'>;

const answeredWords = (code: number | null | undefined) => {
  if (code === null || code === undefined) return 'It didn’t answer';
  const phrase = reasonPhrase(code);
  return `Answered ${code}${phrase ? ` ${phrase}` : ''}`;
};

/** Why a test wasn't checked, from what came before it and how the URL was crawled. */
function notCheckedWords(subject: Subject, earlierFailed: boolean, test: IndexabilityTestName): string {
  if (subject.response === null) return 'Not checked: it wasn’t fetched';
  if (subject.response.outcome === 'robotsDisallowed' && test === 'answered') {
    return 'Not requested: robots.txt disallows it';
  }
  if (earlierFailed) return 'Not checked: an earlier test failed';
  if (subject.discovery.fetchMode === 'check') return 'Not checked: only its status was checked';
  if (test === 'robotsTxt') return 'Not checked: the crawl didn’t read robots.txt';
  return 'Not checked';
}

function canonicalTarget(test: IndexabilityTest): IndexabilityRow['target'] {
  if (test.canonical) return { id: test.canonical.id, url: test.canonical.url, statusCode: test.canonical.statusCode };
  return test.canonicalUrl ? { id: null, url: test.canonicalUrl, statusCode: null } : undefined;
}

function rowFor(subject: Subject, test: IndexabilityTest, earlierFailed: boolean): IndexabilityRow {
  const base = { test: test.test, label: LABELS[test.test], state: test.outcome };
  if (test.outcome === 'notChecked') return { ...base, detail: notCheckedWords(subject, earlierFailed, test.test) };
  switch (test.test) {
    case 'answered': {
      if (test.outcome === 'fail' && test.metaRefresh) {
        return { ...base, detail: 'It moves on at once with a meta refresh', target: refreshTarget(test.metaRefresh) };
      }
      return { ...base, detail: answeredWords(test.statusCode ?? subject.response?.statusCode) };
    }
    case 'robotsTxt':
      if (test.outcome === 'pass') return { ...base, detail: 'robots.txt allows it' };
      return {
        ...base,
        detail: test.unreachable ? 'robots.txt couldn’t be read, which blocks every URL' : 'robots.txt disallows it',
      };
    case 'noindex': {
      if (test.outcome === 'pass') return { ...base, detail: 'No noindex in a robots meta tag or the X-Robots-Tag header' };
      const where =
        test.meta && test.header
          ? 'its robots meta tag and its X-Robots-Tag header'
          : test.header
            ? 'its X-Robots-Tag header'
            : 'its robots meta tag';
      return { ...base, detail: `noindex in ${where}` };
    }
    case 'canonical': {
      const target = canonicalTarget(test);
      if (test.outcome === 'fail') return { ...base, detail: 'Its canonical is another URL', target };
      if (!target) return { ...base, detail: 'It has no canonical' };
      const itself = target.id === subject.id || target.url === subject.url;
      return itself
        ? { ...base, detail: 'Its canonical is itself' }
        : { ...base, detail: 'Its canonical leads back to it', target };
    }
  }
}

const refreshTarget = (target: UrlTarget) => ({ id: target.id, url: target.url, statusCode: target.statusCode });

/** The checklist and the verdict, for the summary. The four tests always come in the engine's order. */
export function indexabilityVerdict(subject: Subject): IndexabilityVerdict {
  const { indexability } = subject;
  const byName = new Map(indexability.tests.map((test) => [test.test, test]));
  const rows: IndexabilityRow[] = [];
  let earlierFailed = false;
  for (const name of ORDER) {
    const test = byName.get(name) ?? { test: name, outcome: 'notChecked' as const };
    rows.push(rowFor(subject, test, earlierFailed));
    if (test.outcome === 'fail') earlierFailed = true;
  }
  const failed = rows.filter((row) => row.state === 'fail').map((row) => row.test);
  const note =
    indexability.judgedBy === 'crawl' && indexability.indexable !== null
      ? 'The crawl’s own judgement: the analysis hasn’t judged this URL.'
      : null;

  if (indexability.indexable === true) {
    return { state: 'indexable', title: 'Indexable', why: REASONS.none, failed, note, rows };
  }
  if (indexability.indexable === false) {
    const reason = indexability.reason;
    let why = reason ? REASONS[reason] : 'Search engines can’t index it.';
    const blockReason = subject.response?.blockReason;
    if (reason === 'blocked' && blockReason) why = `The server blocked the crawler: ${blockReason}.`;
    return { state: 'notIndexable', title: 'Not indexable', why, failed, note, rows };
  }
  const why =
    subject.response === null
      ? 'It wasn’t fetched, so there’s nothing to judge.'
      : subject.discovery.fetchMode === 'check'
        ? 'Only its status was checked, so the crawl never read what decides it.'
        : 'The crawl couldn’t judge it.';
  return { state: 'notJudged', title: 'Not judged', why, failed, note, rows };
}
