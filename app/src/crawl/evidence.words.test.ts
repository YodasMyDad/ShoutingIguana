// Advanced's words for what a check found (fix-it pass, links lane, item 4): a URL that never answered says how it
// failed, in words, with no "0 ms"; one fact one way ("No response"); a URL a row "forwards to" is never the row's own;
// a redirect hop is a redirect, not a place on a page.
import { describe, expect, it } from 'vitest';
import {
  evidenceLineParts,
  flagWords,
  fetchErrorWords,
  linkPositionWords,
  noStatusWords,
  statusWords,
  targetFactParts,
} from './evidence';

const SITE = 'https://example.com';

describe('how a request that got no answer failed', () => {
  it('says the engine’s text in words: a connection that failed, a timeout, a name not found, a refusal', () => {
    expect(fetchErrorWords('An error occurred while sending the request.')).toBe('The connection failed');
    expect(fetchErrorWords('No response within 30 seconds')).toBe('It timed out');
    expect(fetchErrorWords('DNS lookup failed')).toBe('The DNS name wasn’t found');
    expect(
      fetchErrorWords('Could not connect: No connection could be made because the target machine actively refused it.'),
    ).toBe('The connection was refused');
    expect(fetchErrorWords('TLS handshake failed: Cannot determine the frame size.')).toBe(
      'A secure connection couldn’t be made',
    );
    // Words it doesn't know are the engine's, as they are: Advanced hides nothing.
    expect(fetchErrorWords('Something else entirely')).toBe('Something else entirely');
  });
});

describe('a row’s evidence in one line', () => {
  it('words a fetch error and leaves out the time of an answer that never came', () => {
    const row = {
      url: `${SITE}/down/`,
      type: 'page',
      fetchError: 'An error occurred while sending the request.',
      responseMs: 0,
    };
    expect(evidenceLineParts(row, ['type', 'fetchError', 'responseMs'], SITE)).toEqual([
      'Page',
      'The connection failed',
    ]);
  });

  it('keeps the time of an answer that came', () => {
    const row = { url: `${SITE}/slow/`, type: 'page', fetchError: null, responseMs: 850 };
    expect(evidenceLineParts(row, ['type', 'fetchError', 'responseMs'], SITE)).toEqual(['Page', '850 ms']);
  });

  it('never says a URL forwards to itself, or names itself as its canonical', () => {
    const row = { url: `${SITE}/p/81`, finalUrl: `${SITE}/p/81`, canonicalUrl: `${SITE}/p/81` };
    expect(evidenceLineParts(row, ['finalUrl', 'canonicalUrl'], SITE)).toEqual([]);
    const elsewhere = { url: `${SITE}/p/81`, finalUrl: `${SITE}/p/82`, canonicalUrl: `${SITE}/p/1` };
    expect(evidenceLineParts(elsewhere, ['finalUrl', 'canonicalUrl'], SITE)).toEqual(['→ /p/82', 'canonical → /p/1']);
  });
});

describe('a URL without a status, in one wording', () => {
  it('says “No response” for a URL that didn’t answer, in a pill and in a line', () => {
    expect(noStatusWords('networkError')).toBe('No response');
    expect(noStatusWords('timeout')).toBe('Timed out');
    expect(noStatusWords('robotsDisallowed')).toBe('Blocked by robots.txt');
    expect(statusWords(null, 'networkError')).toBe('no response');
  });
});

describe('a redirect as where a URL is linked from', () => {
  it('says it redirects here, not where on a page it is', () => {
    expect(linkPositionWords({ kind: 'redirect', position: 'head' })).toBe('redirects here');
    expect(
      targetFactParts(
        {
          id: 1,
          url: `${SITE}/a`,
          statusCode: 301,
          fetched: true,
          outcome: 'redirect',
          kind: 'redirect',
          text: null,
          position: 'head',
        },
        ['position'],
        SITE,
      ),
    ).toEqual(['redirects here']);
  });
});

describe('a link’s flags', () => {
  it('says a protocol-relative link so', () => {
    expect(flagWords(['protocolRelative'])).toBe('protocol-relative');
    expect(flagWords(['noFollow', 'protocolRelative'])).toBe('nofollow, protocol-relative');
  });
});
