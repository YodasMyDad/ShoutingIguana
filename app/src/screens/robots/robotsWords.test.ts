import { describe, expect, it } from 'vitest';
import type { RobotsState } from '../../api/grids';
import { TEST_ORIGINS, testOrigin } from '../../test/sitemapsRobotsSidecar';
import {
  LINE_PROBLEM_WORDS,
  UNKNOWN_REASON,
  UNMATCHED_WORDS,
  answerParts,
  answerSentence,
  blockedWhy,
  lineProblemWords,
  nothingBlockedWords,
  refusedEarlierWords,
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

  it('never says a crawl told to ignore robots.txt obeyed a file or refused a URL', () => {
    const sentences = TEST_ORIGINS.map((origin) => stateWords({ ...origin, blocked: 0 }, UA, false));
    expect(sentences.map((words) => words.sentence)).toEqual([
      'The crawl read this file, and was told to ignore its rules.',
      'robots.txt couldn’t be read (HTTP 503). The crawl was told to ignore robots.txt, so it refused nothing.',
      'There’s no robots.txt here (HTTP 404), so nothing on this origin is refused.',
      'robots.txt didn’t answer (Timed out). The crawl was told to ignore robots.txt, so it went ahead.',
    ]);
    expect(sentences.map((words) => words.short)).toEqual([
      'Read: rules ignored',
      'Couldn’t be read: ignored',
      'Missing: nothing refused',
      'No answer: ignored',
    ]);
    for (const words of sentences) {
      expect(words.sentence).not.toMatch(/obeyed|refused this origin/);
      expect(words.tone).not.toBe('refused');
    }
  });

  it('says no more than holds either way when it isn’t known whether the crawl obeyed robots.txt', () => {
    const [read, unreachable] = TEST_ORIGINS.map((origin) => stateWords(origin, UA, null));
    expect(read).toMatchObject({ short: 'Read', sentence: 'The crawl read this file.' });
    expect(unreachable).toMatchObject({
      short: 'Couldn’t be read',
      sentence: 'robots.txt couldn’t be read (HTTP 503).',
    });
  });

  it('says why nothing is blocked by what the file meant, and of an ignored robots.txt that it was ignored', () => {
    const file = (state: RobotsState, lines = 0) => ({
      state,
      lines: Array.from({ length: lines }, (_, index) => ({
        number: index + 1,
        text: 'Disallow: /',
        kind: 'disallow' as const,
        applies: true,
        blocked: 0,
        problem: null,
      })),
    });
    expect(nothingBlockedWords(file('read', 2))).toBe(
      'None of the URLs the crawl came to on this origin is refused by the rules it obeyed.',
    );
    expect(nothingBlockedWords(file('unreachable'))).toMatch(/while its robots\.txt couldn’t be read/);
    for (const state of ['read', 'missing', 'unreachable', 'unanswered'] as const) {
      expect(nothingBlockedWords(file(state, 2), false)).toBe(
        'This crawl was told to ignore robots.txt, so nothing was refused.',
      );
      expect(nothingBlockedWords(file(state, 2), null)).not.toMatch(/obeyed/);
    }
  });

  it('says an answer that refuses nothing is the last one only, when the origin has URLs stored as refused', () => {
    // The garden crawl: no answer (DNS), and 1 URL stored as refused, by an engine that took no answer for a refusal.
    const garden = stateWords(
      testOrigin({ origin: 'https://a.example', state: 'unanswered', statusCode: null, error: null, blocked: 1 }),
      UA,
    );
    expect(garden.short).toBe('No answer when last asked');
    expect(garden.sentence).toBe(
      'When the crawl last asked, robots.txt didn’t answer, so it gave no verdict and went ahead. 1 URL on this origin is stored as refused all the same: it was refused earlier in the crawl, or by an earlier version of Shouting Iguana. Only the last answer is kept.',
    );
    const missing = stateWords(
      testOrigin({ origin: 'https://a.example', state: 'missing', statusCode: 404, bytes: null, blocked: 1_204 }),
      UA,
    );
    expect(missing.short).toBe('Missing when last asked');
    expect(missing.sentence).toBe(
      'When the crawl last asked, there was no robots.txt here (HTTP 404), which refuses nothing. 1,204 URLs on this origin are stored as refused all the same: they were refused earlier in the crawl, or by an earlier version of Shouting Iguana. Only the last answer is kept.',
    );
    const empty = stateWords(testOrigin({ origin: 'https://a.example', bytes: 0, blocked: 2 }), UA);
    expect(empty.sentence).toMatch(/^When the crawl last read this file it was empty, which refuses nothing. 2 URLs/);
    // A crawl told to ignore robots.txt this time, with URLs an earlier run refused.
    const ignored = stateWords(testOrigin({ origin: 'https://a.example', blocked: 3 }), UA, false);
    expect(ignored.sentence).toMatch(
      /told to ignore its rules. 3 URLs on this origin are stored as refused all the same/,
    );
    // None of them says nothing "is refused" on an origin that has refused URLs.
    for (const words of [garden, missing, empty, ignored]) {
      expect(words.sentence).not.toMatch(/nothing on this origin is refused|show their own errors/);
      expect(words.short).not.toMatch(/nothing refused|crawled anyway/);
    }
    expect(refusedEarlierWords(1)).toMatch(/^1 URL on this origin is stored as refused all the same: it was refused/);
    // With nothing stored as refused the words are as they were.
    expect(stateWords(testOrigin({ origin: 'https://a.example', state: 'missing', statusCode: 404 }), UA).short).toBe(
      'Missing: nothing refused',
    );
    // An answer that does refuse things isn't qualified: its lines, or the unmatched links, account for the URLs.
    expect(stateWords(testOrigin({ origin: 'https://a.example', blocked: 9 }), UA).sentence).toBe(
      'The crawl read this file and obeyed its rules for ShoutingIguana.',
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

  it('makes the URLs the crawl was kept from a link to exactly them, by the engine’s own word for them', () => {
    const parts = answerParts({ total: 17, blocked: 1_204, respected: true, userAgent: UA });
    expect(parts.filter((part) => typeof part !== 'string')).toEqual([
      { text: '1,204 URLs', filters: [{ column: 'outcome', op: 'in', values: ['robotsDisallowed'] }] },
    ]);
    // Nothing to open when nothing was blocked, or when the crawl ignored robots.txt.
    expect(
      answerParts({ total: 3, blocked: 0, respected: true, userAgent: UA }).every((part) => typeof part === 'string'),
    ).toBe(true);
    expect(
      answerParts({ total: 3, blocked: 0, respected: false, userAgent: UA }).every((part) => typeof part === 'string'),
    ).toBe(true);
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

  it('writes a line far down a long file with its thousands separator', () => {
    expect(blockedWhy({ reason: 'rule', line: 2_483, rule: 'Disallow: /x', error: null }).short).toBe(
      'Line 2,483: Disallow: /x',
    );
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

describe('a line the crawler made nothing of', () => {
  it('has words for every problem the engine names', () => {
    expect(Object.keys(LINE_PROBLEM_WORDS).sort()).toEqual(
      ['invalidValue', 'noUserAgent', 'notADirective', 'unknownDirective'].sort(),
    );
  });

  it('says what’s wrong with a value by what the line is for: only a crawl delay is “not a number”', () => {
    // RobotsTxtParser: a `Sitemap:` line with nothing after it, and a `Crawl-delay` that isn't a number or is
    // below 0, are both `invalidValue`.
    expect(lineProblemWords({ kind: 'sitemap', problem: 'invalidValue' })).toBe('Ignored: it names no sitemap');
    expect(lineProblemWords({ kind: 'crawlDelay', problem: 'invalidValue' })).toBe(
      'Ignored: its value isn’t a number of seconds, 0 or more',
    );
    // A kind this app doesn't expect it on says no more than is known.
    expect(lineProblemWords({ kind: 'allow', problem: 'invalidValue' })).toBe('Ignored: its value can’t be used');
    expect(lineProblemWords({ kind: 'disallow', problem: 'noUserAgent' })).toBe(LINE_PROBLEM_WORDS.noUserAgent);
    expect(lineProblemWords({ kind: 'disallow', problem: null })).toBeNull();
  });
});
