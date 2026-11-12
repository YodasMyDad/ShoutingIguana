// What a URL the inspector names says where it has no status code (E3c's `outcome` on link rows and targets): the
// inspector's own words for the URL itself, for each outcome, never "No status" when the engine says what happened.
import { describe, expect, it } from 'vitest';
import type { UrlOutcome } from '../api/grids';
import { testResponse, testUrlDetail } from '../test/inspectorSidecar';
import { fetchState } from './fetchState';
import { hreflangProblem } from './hreflang';
import { linkStatusWords } from './links';
import { noStatusWords } from './words';

const NO_CODE: UrlOutcome[] = [
  'robotsDisallowed',
  'blocked',
  'notChecked',
  'timeout',
  'networkError',
  'processingError',
];

describe('a named URL with no status code', () => {
  it('says what became of it in the words the inspector says of the URL itself', () => {
    for (const outcome of NO_CODE) {
      const own = fetchState(testUrlDetail({ response: testResponse({ outcome, statusCode: null }) }), false);
      expect(noStatusWords(outcome, true)).toBe(own.title);
    }
  });

  it('says it wasn’t fetched, whatever the crawl is doing: queued or left alone, a named URL doesn’t say which', () => {
    expect(noStatusWords(null, false)).toBe('Not fetched');
    expect(noStatusWords(null)).toBe('Not fetched');
    // An outcome that comes with a code, stored without one: no answer to speak of.
    expect(noStatusWords('success', true)).toBe('No response');
  });

  it('says no more than an earlier engine did when the engine doesn’t say', () => {
    expect(noStatusWords(undefined, true)).toBe('No status');
    expect(noStatusWords(undefined, false)).toBe('Not fetched');
  });
});

describe('an inlink’s or outlink’s other end', () => {
  it('says its outcome where the row has no status', () => {
    expect(linkStatusWords({ statusCode: null, fetched: true, outcome: 'robotsDisallowed' })).toBe(
      'Blocked by robots.txt',
    );
    expect(linkStatusWords({ statusCode: null, fetched: false, outcome: 'notChecked' })).toBe('Not checked');
    // Never "yet": a URL left alone (not followed, past a limit) has no outcome either, and never will.
    expect(linkStatusWords({ statusCode: null, fetched: false, outcome: null })).toBe('Not fetched');
    expect(linkStatusWords({ statusCode: 404, fetched: true, outcome: 'clientError' })).toBe('404');
    // An earlier engine's row: whether fetched, and no more.
    expect(linkStatusWords({ statusCode: null, fetched: true })).toBe('No status');
  });
});

describe('an hreflang’s target', () => {
  const entry = (outcome: UrlOutcome | null | undefined) => ({
    language: 'de',
    href: 'https://example.com/de/',
    url: 'https://example.com/de/',
    inHead: true,
    fromHttpHeader: false,
    target: {
      id: 9,
      url: 'https://example.com/de/',
      statusCode: null,
      ...(outcome === undefined ? {} : { outcome }),
    },
  });

  it('says what became of it where it has no status', () => {
    expect(hreflangProblem(entry('robotsDisallowed'))).toBe('Blocked by robots.txt');
    expect(hreflangProblem(entry('timeout'))).toBe('Timed out');
    expect(hreflangProblem(entry(null))).toBe('Not fetched');
    expect(hreflangProblem(entry(undefined))).toBe('No status in this crawl');
  });
});
