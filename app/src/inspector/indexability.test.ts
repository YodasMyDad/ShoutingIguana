import { describe, expect, it } from 'vitest';
import type { Indexability, IndexabilityTest } from '../api/inspector';
import { testResponse, testUrlDetail } from '../test/inspectorSidecar';
import { indexabilityVerdict } from './indexability';

const PASSING: IndexabilityTest[] = [
  { test: 'answered', outcome: 'pass', statusCode: 200 },
  { test: 'robotsTxt', outcome: 'pass' },
  { test: 'noindex', outcome: 'pass', meta: false, header: false },
  { test: 'canonical', outcome: 'pass' },
];

const withTests = (
  changes: Partial<Record<IndexabilityTest['test'], Partial<IndexabilityTest>>>,
  verdict: Partial<Indexability> = {},
): Indexability => ({
  indexable: false,
  reason: 'noindex',
  judgedBy: 'analysis',
  tests: PASSING.map((test) => ({ ...test, ...changes[test.test] })),
  ...verdict,
});

const states = (verdict: ReturnType<typeof indexabilityVerdict>) => verdict.rows.map((row) => row.state);
const row = (verdict: ReturnType<typeof indexabilityVerdict>, test: IndexabilityTest['test']) => {
  const found = verdict.rows.find((candidate) => candidate.test === test);
  if (!found) throw new Error(`no ${test} row`);
  return found;
};

describe('indexabilityVerdict', () => {
  it('passes all four tests for an indexable page, in the engine’s order', () => {
    const verdict = indexabilityVerdict(testUrlDetail());
    expect(verdict.state).toBe('indexable');
    expect(verdict.title).toBe('Indexable');
    expect(verdict.why).toBe('Search engines can index it.');
    expect(verdict.rows.map((entry) => entry.label)).toEqual([
      'Answered 2xx',
      'Allowed by robots.txt',
      'No noindex',
      'Canonical is itself',
    ]);
    expect(states(verdict)).toEqual(['pass', 'pass', 'pass', 'pass']);
    expect(verdict.failed).toEqual([]);
    expect(verdict.note).toBeNull();
    expect(row(verdict, 'answered').detail).toBe('Answered 200 OK');
    expect(row(verdict, 'canonical').detail).toBe('Its canonical is itself');
  });

  it('says a page without a canonical has none', () => {
    const verdict = indexabilityVerdict(
      testUrlDetail({ indexability: withTests({}, { indexable: true, reason: 'none' }) }),
    );
    expect(row(verdict, 'canonical').detail).toBe('It has no canonical');
    expect(row(verdict, 'canonical').target).toBeUndefined();
  });

  it('marks the answer as the failed test of a 404, and the rest as not checked because of it', () => {
    const verdict = indexabilityVerdict(
      testUrlDetail({
        response: testResponse({ outcome: 'clientError', statusCode: 404 }),
        content: null,
        indexability: withTests(
          {
            answered: { outcome: 'fail', statusCode: 404 },
            noindex: { outcome: 'notChecked' },
            canonical: { outcome: 'notChecked' },
          },
          { reason: 'clientError' },
        ),
      }),
    );
    expect(verdict.state).toBe('notIndexable');
    expect(verdict.why).toBe('It answers with a client error.');
    expect(verdict.failed).toEqual(['answered']);
    expect(row(verdict, 'answered').detail).toBe('Answered 404 Not Found');
    expect(row(verdict, 'noindex').detail).toBe('Not checked: an earlier test failed');
    expect(row(verdict, 'canonical').detail).toBe('Not checked: an earlier test failed');
  });

  it('names an instant meta refresh’s target when that fails the answer', () => {
    const target = { id: 9, url: 'https://example.com/new/', statusCode: 200 };
    const verdict = indexabilityVerdict(
      testUrlDetail({
        indexability: withTests(
          { answered: { outcome: 'fail', statusCode: 200, metaRefresh: target } },
          { reason: 'redirected' },
        ),
      }),
    );
    expect(row(verdict, 'answered').detail).toBe('It moves on at once with a meta refresh');
    expect(row(verdict, 'answered').target).toEqual(target);
    expect(verdict.why).toBe('It redirects, so search engines index where it goes instead.');
  });

  it('says where robots.txt stops a URL, and when robots.txt couldn’t be read', () => {
    const blocked = indexabilityVerdict(
      testUrlDetail({
        response: testResponse({ outcome: 'robotsDisallowed', statusCode: null }),
        content: null,
        indexability: withTests(
          {
            answered: { outcome: 'notChecked' },
            robotsTxt: { outcome: 'fail' },
            noindex: { outcome: 'notChecked' },
            canonical: { outcome: 'notChecked' },
          },
          { reason: 'blockedByRobots' },
        ),
      }),
    );
    expect(blocked.failed).toEqual(['robotsTxt']);
    expect(row(blocked, 'answered').detail).toBe('Not requested: robots.txt disallows it');
    expect(row(blocked, 'robotsTxt').detail).toBe('robots.txt disallows it');
    expect(blocked.why).toBe('robots.txt stops search engines reading it.');

    const unreachable = indexabilityVerdict(
      testUrlDetail({ indexability: withTests({ robotsTxt: { outcome: 'fail', unreachable: true } }) }),
    );
    expect(row(unreachable, 'robotsTxt').detail).toBe('robots.txt couldn’t be read, which blocks every URL');
  });

  it('says where a noindex is: the meta tag, the header, or both', () => {
    const where = (meta: boolean, header: boolean) =>
      row(
        indexabilityVerdict(testUrlDetail({ indexability: withTests({ noindex: { outcome: 'fail', meta, header } }) })),
        'noindex',
      ).detail;
    expect(where(true, false)).toBe('noindex in its robots meta tag');
    expect(where(false, true)).toBe('noindex in its X-Robots-Tag header');
    expect(where(true, true)).toBe('noindex in its robots meta tag and its X-Robots-Tag header');
  });

  it('names the canonical’s target when it’s another URL, in the crawl or not', () => {
    const inCrawl = { id: 7, url: 'https://example.com/blog/', statusCode: 200 };
    const known = indexabilityVerdict(
      testUrlDetail({
        indexability: withTests(
          { canonical: { outcome: 'fail', canonical: inCrawl, canonicalUrl: inCrawl.url } },
          { reason: 'canonicalised' },
        ),
      }),
    );
    expect(known.failed).toEqual(['canonical']);
    expect(row(known, 'canonical').detail).toBe('Its canonical is another URL');
    expect(row(known, 'canonical').target).toEqual(inCrawl);
    expect(known.why).toBe('Its canonical names another URL, so search engines index that one instead.');

    const unknown = indexabilityVerdict(
      testUrlDetail({
        indexability: withTests({ canonical: { outcome: 'fail', canonicalUrl: 'https://other.example/x' } }),
      }),
    );
    expect(row(unknown, 'canonical').target).toEqual({ id: null, url: 'https://other.example/x', statusCode: null });
  });

  it('says a canonical that leads back to the URL passes, naming where it goes first', () => {
    const verdict = indexabilityVerdict(
      testUrlDetail({
        indexability: withTests(
          {
            canonical: {
              outcome: 'pass',
              canonical: { id: 31, url: 'http://example.com/garden/patio-set/', statusCode: 301 },
              canonicalUrl: 'http://example.com/garden/patio-set/',
            },
          },
          { indexable: true, reason: 'none' },
        ),
      }),
    );
    expect(row(verdict, 'canonical').detail).toBe('Its canonical leads back to it');
    expect(row(verdict, 'canonical').target?.id).toBe(31);
  });

  it('keeps every failed test, the first being the reason', () => {
    const verdict = indexabilityVerdict(
      testUrlDetail({
        indexability: withTests({
          noindex: { outcome: 'fail', meta: true, header: false },
          canonical: { outcome: 'fail', canonicalUrl: 'https://example.com/other/' },
        }),
      }),
    );
    expect(verdict.failed).toEqual(['noindex', 'canonical']);
    expect(verdict.why).toBe('It asks search engines not to index it.');
  });

  it('doesn’t judge a URL that was only status-checked', () => {
    const verdict = indexabilityVerdict(
      testUrlDetail({
        discovery: { ...testUrlDetail().discovery, fetchMode: 'check' },
        content: null,
        indexability: withTests(
          { noindex: { outcome: 'notChecked' }, canonical: { outcome: 'notChecked' } },
          { indexable: null, reason: null },
        ),
      }),
    );
    expect(verdict.state).toBe('notJudged');
    expect(verdict.title).toBe('Not judged');
    expect(verdict.why).toBe('Only its status was checked, so the crawl never read what decides it.');
    expect(row(verdict, 'noindex').detail).toBe('Not checked: only its status was checked');
    expect(verdict.note).toBeNull();
  });

  it('doesn’t judge a URL that wasn’t fetched', () => {
    const verdict = indexabilityVerdict(
      testUrlDetail({
        response: null,
        content: null,
        indexability: {
          indexable: null,
          reason: null,
          judgedBy: 'crawl',
          tests: PASSING.map((test) => ({ test: test.test, outcome: 'notChecked' })),
        },
      }),
    );
    expect(verdict.state).toBe('notJudged');
    expect(verdict.why).toBe('It wasn’t fetched, so there’s nothing to judge.');
    expect(states(verdict)).toEqual(['notChecked', 'notChecked', 'notChecked', 'notChecked']);
    expect(row(verdict, 'answered').detail).toBe('Not checked: it wasn’t fetched');
    // Nothing was judged, so there's no judgement to attribute to the crawl.
    expect(verdict.note).toBeNull();
  });

  it('notes when the verdict is the crawl’s own, not the analysis’s', () => {
    const verdict = indexabilityVerdict(
      testUrlDetail({ indexability: { ...testUrlDetail().indexability, judgedBy: 'crawl' } }),
    );
    expect(verdict.note).toBe('The crawl’s own judgement: the analysis hasn’t judged this URL.');
  });

  it('gives the server’s reason when it blocked the crawler', () => {
    const verdict = indexabilityVerdict(
      testUrlDetail({
        response: testResponse({ outcome: 'blocked', statusCode: 403, blockReason: 'a challenge page' }),
        content: null,
        indexability: withTests({ answered: { outcome: 'fail', statusCode: 403 } }, { reason: 'blocked' }),
      }),
    );
    expect(verdict.why).toBe('The server blocked the crawler: a challenge page.');
    expect(row(verdict, 'answered').detail).toBe('Answered 403 Forbidden');
  });

  it('falls back to plain words for a reason this version doesn’t know', () => {
    const verdict = indexabilityVerdict(testUrlDetail({ indexability: withTests({}, { reason: null }) }));
    expect(verdict.why).toBe('Search engines can’t index it.');
  });

  it('treats a test the engine left out as not checked', () => {
    const verdict = indexabilityVerdict(
      testUrlDetail({ indexability: { ...testUrlDetail().indexability, tests: PASSING.slice(0, 2) } }),
    );
    expect(states(verdict)).toEqual(['pass', 'pass', 'notChecked', 'notChecked']);
  });
});
