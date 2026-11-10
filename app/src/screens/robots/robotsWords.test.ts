import { describe, expect, it } from 'vitest';
import type { RobotsState } from '../../api/grids';
import { TEST_ORIGINS, testOrigin } from '../../test/sitemapsRobotsSidecar';
import {
  LINE_KIND_WORDS,
  LINE_PROBLEM_WORDS,
  UNKNOWN_REASON,
  UNMATCHED_WORDS,
  answerSentence,
  blockedWhy,
  stateWords,
} from './robotsWords';

const UA = 'ShoutingIguana';

describe('what an origin’s robots.txt meant to the crawl', () => {
  it('has a sentence of its own for each of the four states', () => {
    const sentences = TEST_ORIGINS.map((origin) => stateWords(origin, UA).sentence);
    expect(sentences).toEqual([
      'The crawl read this file and obeyed its rules for ShoutingIguana.',
      'robots.txt couldn’t be read (HTTP 503), so the crawl refused this origin’s URLs until it could.',
      'There’s no robots.txt here (HTTP 404), so nothing on this origin is refused.',
      'robots.txt didn’t answer (Timed out). The crawl gave no verdict and went ahead: this origin’s URLs show their own errors.',
    ]);
    expect(new Set(sentences).size).toBe(4);
  });

  it('never says of an origin that didn’t answer what’s true of the other kind', () => {
    const unreachable = stateWords(
      testOrigin({ origin: 'https://a.example', state: 'unreachable', statusCode: null, error: 'Timed out' }),
      UA,
    );
    const unanswered = stateWords(
      testOrigin({ origin: 'https://a.example', state: 'unanswered', statusCode: null, error: 'Timed out' }),
      UA,
    );
    expect(unreachable.sentence).toMatch(/refused this origin’s URLs/);
    expect(unreachable.sentence).not.toMatch(/went ahead/);
    expect(unanswered.sentence).toMatch(/went ahead/);
    expect(unanswered.sentence).not.toMatch(/refused/);
    expect(unreachable.short).toBe('Couldn’t be read: URLs refused');
    expect(unanswered.short).toBe('No answer: crawled anyway');
  });

  it('says an empty file refuses nothing', () => {
    const empty = stateWords(testOrigin({ origin: 'https://a.example', bytes: 0 }), UA);
    expect(empty).toMatchObject({ short: 'Read: empty', tone: 'nothing' });
    expect(empty.sentence).toMatch(/it’s empty, so nothing on this origin is refused/);
  });

  it('says how the answer failed only when the crawl stored it', () => {
    const silent = stateWords(
      testOrigin({ origin: 'https://a.example', state: 'unanswered', statusCode: null, error: null }),
      UA,
    );
    expect(silent.sentence).toBe(
      'robots.txt didn’t answer. The crawl gave no verdict and went ahead: this origin’s URLs show their own errors.',
    );
  });

  it('says so when the crawl kept no robots.txt for an origin (a state it doesn’t know reads the same)', () => {
    for (const state of [null, 'somethingNew' as RobotsState]) {
      const words = stateWords(testOrigin({ origin: 'https://a.example', state, statusCode: null, bytes: null }), UA);
      expect(words.short).toBe('None kept');
    }
  });
});

describe('the answer over the origins', () => {
  it('says how many origins were asked, as whom, and what they kept the crawl from', () => {
    expect(answerSentence({ total: 17, blocked: 2, respected: true, userAgent: UA })).toBe(
      'The crawl asked 17 origins for robots.txt, as ShoutingIguana. They kept it from 2 URLs.',
    );
    expect(answerSentence({ total: 1, blocked: 1, respected: true, userAgent: UA })).toBe(
      'The crawl asked 1 origin for robots.txt, as ShoutingIguana. It kept it from 1 URL.',
    );
    expect(answerSentence({ total: 4_120, blocked: 0, respected: true, userAgent: UA })).toBe(
      'The crawl asked 4,120 origins for robots.txt, as ShoutingIguana. None of them kept it from a URL.',
    );
  });

  it('doesn’t say “none blocked” of a crawl told to ignore robots.txt: the banner says why', () => {
    expect(answerSentence({ total: 1, blocked: 0, respected: false, userAgent: UA })).toBe(
      'The crawl asked 1 origin for robots.txt, as ShoutingIguana.',
    );
  });

  it('has a sentence for a crawl that asked none', () => {
    expect(answerSentence({ total: 0, blocked: 0, respected: true, userAgent: UA })).toBe(
      'The crawl asked no origin for its robots.txt.',
    );
  });
});

describe('why a URL was blocked', () => {
  it('names the line that decided it, with the line’s text', () => {
    expect(blockedWhy({ reason: 'rule', line: 6, rule: 'Disallow: /admin/', error: null })).toEqual({
      short: 'Line 6: Disallow: /admin/',
      sentence: 'Line 6 refused it: Disallow: /admin/',
    });
  });

  it('says in words that the file couldn’t be read, with how when that’s stored', () => {
    const why = blockedWhy({ reason: 'unreachable', line: null, rule: null, error: 'HTTP 503' });
    expect(why.short).toBe('robots.txt couldn’t be read');
    expect(why.sentence).toBe(
      'robots.txt couldn’t be read when the crawl came to this URL (HTTP 503), which refuses everything.',
    );
  });

  it('says in words that nothing kept refuses it, and no more than is known', () => {
    const why = blockedWhy({ reason: 'unknown', line: null, rule: null, error: null });
    expect(why).toEqual({ short: 'No rule kept', sentence: UNKNOWN_REASON });
    expect(UNKNOWN_REASON).toMatch(/doesn’t refuse it/);
  });

  it('counts the two cases no line accounts for in words that agree in number', () => {
    expect(UNMATCHED_WORDS.unreachable(1)).toBe('1 URL was refused while robots.txt couldn’t be read');
    expect(UNMATCHED_WORDS.unreachable(1_204)).toBe('1,204 URLs were refused while robots.txt couldn’t be read');
    expect(UNMATCHED_WORDS.unknown(1)).toBe('1 URL is stored as refused, though nothing kept here refuses it');
    expect(UNMATCHED_WORDS.unknown(2)).toBe('2 URLs are stored as refused, though nothing kept here refuses them');
  });
});

describe('a line’s kind', () => {
  it('has words for every kind and every problem the engine names', () => {
    expect(Object.keys(LINE_KIND_WORDS).sort()).toEqual(
      ['allow', 'blank', 'comment', 'crawlDelay', 'disallow', 'invalid', 'other', 'sitemap', 'userAgent'].sort(),
    );
    expect(Object.keys(LINE_PROBLEM_WORDS).sort()).toEqual(
      ['invalidValue', 'noUserAgent', 'notADirective', 'unknownDirective'].sort(),
    );
  });
});
