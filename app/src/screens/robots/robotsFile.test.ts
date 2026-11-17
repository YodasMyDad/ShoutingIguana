import { describe, expect, it } from 'vitest';
import { TEST_BLOCKED, TEST_FILE, TEST_FILE_LINES, blockedPage } from '../../test/sitemapsRobotsSidecar';
import { blockedUrlsReader } from './copyBlocked';
import {
  blockedQueryOf,
  fileMarks,
  fileText,
  lineAt,
  lineOf,
  markedLine,
  narrowingOf,
  emptyLineWords,
  narrowingWords,
  obeyedLines,
  obeyedWords,
  originFromParam,
} from './robotsFile';

describe('the file as the viewer shows it', () => {
  it('is the text as the crawl kept it, line for line', () => {
    const text = fileText(TEST_FILE_LINES);
    expect(text.split('\n')).toHaveLength(TEST_FILE_LINES.length);
    expect(text.split('\n')[5]).toBe('Disallow: /admin/');
    expect(fileText([])).toBe('');
  });

  it('marks the group the crawl obeyed, and leaves the other groups quiet', () => {
    const tone = new Map(fileMarks(TEST_FILE_LINES).map((mark) => [mark.line, mark.tone]));
    // The group for every crawler, which this crawl passed over for its own.
    expect([tone.get(2), tone.get(3)]).toEqual(['quiet', 'quiet']);
    expect([tone.get(5), tone.get(6), tone.get(7), tone.get(8), tone.get(9)]).toEqual(Array(5).fill('strong'));
  });

  it('gives a sitemap line no group’s tone: it applies to everyone', () => {
    expect(fileMarks(TEST_FILE_LINES).find((mark) => mark.line === 12)?.tone).toBeUndefined();
  });

  it('sets a directive’s name apart, a comment as one, and leaves blank lines alone', () => {
    const marks = fileMarks(TEST_FILE_LINES);
    const at = (line: number) => marks.find((mark) => mark.line === line);
    expect(at(1)).toEqual({ line: 1, comment: true });
    expect(at(6)?.nameEnd).toBe('Disallow'.length);
    expect(at(5)?.nameEnd).toBe('User-agent'.length);
    expect(at(4)).toBeUndefined();
    // Not a "name: value" line: no name to set apart.
    expect(at(11)?.nameEnd).toBeUndefined();
  });

  it('puts how many URLs a rule blocked beside it, as a link, and nothing beside a rule that blocked none', () => {
    const marks = fileMarks(TEST_FILE_LINES);
    expect(marks.find((mark) => mark.line === 6)).toMatchObject({
      action: 'Blocked 3 URLs',
      actionTitle: 'List the 3 URLs line 6 blocked',
    });
    // A real file has hundreds of rules that blocked nothing: a note on each would bury the few that did.
    const none = marks.find((mark) => mark.line === 7);
    expect(none?.action).toBeUndefined();
    expect(none?.note).toBeUndefined();
    expect(marks.find((mark) => mark.line === 3)?.note).toBeUndefined();
  });

  it('says why the crawler made nothing of a line', () => {
    const note = new Map(fileMarks(TEST_FILE_LINES).map((mark) => [mark.line, mark.note]));
    expect(note.get(9)).toBe('Ignored: its value isn’t a number of seconds, 0 or more');
    expect(note.get(10)).toBe('Not used by the crawl');
    expect(note.get(11)).toBe('Ignored: not a “name: value” line');
  });

  it('never tells an empty Sitemap line that its value isn’t a number', () => {
    const [mark] = fileMarks([
      { number: 4, text: 'Sitemap:', kind: 'sitemap', applies: false, blocked: null, problem: 'invalidValue' },
    ]);
    expect(mark?.note).toBe('Ignored: it names no sitemap');
  });

  it('uses thousands separators in a rule’s count', () => {
    const [mark] = fileMarks([{ ...TEST_FILE_LINES[5]!, number: 2_483, blocked: 12_345 }]);
    expect(mark?.action).toBe('Blocked 12,345 URLs');
    expect(mark?.actionTitle).toBe('List the 12,345 URLs line 2,483 blocked');
  });

  it('gives a link to the lines that blocked URLs, and to no other', () => {
    expect(
      fileMarks(TEST_FILE_LINES)
        .filter((mark) => mark.action !== undefined)
        .map((mark) => mark.line),
    ).toEqual([6]);
  });
});

describe('which lines the crawl obeyed', () => {
  it('is the runs of lines that apply, a sitemap line apart', () => {
    expect(obeyedLines(TEST_FILE_LINES)).toEqual([[5, 9]]);
    expect(obeyedWords(TEST_FILE_LINES, 'ShoutingIguana')).toBe('As ShoutingIguana, the crawl obeyed lines 5 to 9.');
    const far = [
      { number: 2_482, kind: 'userAgent', applies: true },
      { number: 4_882, kind: 'disallow', applies: true },
    ] as const;
    expect(obeyedWords(far, 'ShoutingIguana')).toBe('As ShoutingIguana, the crawl obeyed lines 2,482 to 4,882.');
  });

  it('keeps a blank line or a comment inside a group, and names two groups apart', () => {
    const lines = [
      { number: 1, kind: 'userAgent', applies: true },
      { number: 2, kind: 'comment', applies: null },
      { number: 3, kind: 'disallow', applies: true },
      { number: 4, kind: 'userAgent', applies: false },
      { number: 5, kind: 'disallow', applies: false },
      { number: 6, kind: 'userAgent', applies: true },
    ] as const;
    expect(obeyedLines(lines)).toEqual([
      [1, 3],
      [6, 6],
    ]);
    expect(obeyedWords(lines, 'ShoutingIguana')).toBe('As ShoutingIguana, the crawl obeyed lines 1 to 3 and line 6.');
  });

  it('says so when no group is for this crawler, or the file has no rules', () => {
    const others = [
      { number: 1, kind: 'userAgent', applies: false },
      { number: 2, kind: 'disallow', applies: false },
    ] as const;
    expect(obeyedWords(others, 'ShoutingIguana')).toBe(
      'No group here is for ShoutingIguana or for every crawler (*), so none of its rules applied.',
    );
    expect(obeyedWords([{ number: 1, kind: 'sitemap', applies: true }], 'ShoutingIguana')).toBe(
      'It has no group of rules, so nothing is refused.',
    );
  });

  it('never says a crawl told to ignore robots.txt obeyed the lines: they’re the ones it would have', () => {
    expect(obeyedWords(TEST_FILE_LINES, 'ShoutingIguana', false)).toBe(
      'The crawl was told to ignore robots.txt. As ShoutingIguana, it would have obeyed lines 5 to 9.',
    );
    const others = [
      { number: 1, kind: 'userAgent', applies: false },
      { number: 2, kind: 'disallow', applies: false },
    ] as const;
    expect(obeyedWords(others, 'ShoutingIguana', false)).toBe(
      'No group here is for ShoutingIguana or for every crawler (*), so none of its rules applies.',
    );
    // Not known whether it obeyed any: whose rules they are, and nothing of what the crawl did.
    expect(obeyedWords(TEST_FILE_LINES, 'ShoutingIguana', null)).toBe('The rules for ShoutingIguana are lines 5 to 9.');
  });
});

describe('a URL and its line, both ways', () => {
  it('narrows the list to a line or a reason from the route, a line first', () => {
    expect(narrowingOf({})).toEqual({ by: 'none' });
    expect(narrowingOf({ line: 6 })).toEqual({ by: 'line', line: 6 });
    expect(narrowingOf({ reason: 'unknown' })).toEqual({ by: 'reason', reason: 'unknown' });
    expect(narrowingOf({ line: 6, reason: 'unknown' })).toEqual({ by: 'line', line: 6 });
    // The conflicts (in a sitemap or a menu, yet blocked): a line or a reason wins over them.
    expect(narrowingOf({ conflict: true })).toEqual({ by: 'conflict' });
    expect(narrowingOf({ line: 6, conflict: true })).toEqual({ by: 'line', line: 6 });
    expect(narrowingOf({ reason: 'unknown', conflict: true })).toEqual({ by: 'reason', reason: 'unknown' });
  });

  it('asks the engine for exactly what the route narrows to', () => {
    const origin = TEST_FILE.origin;
    expect(blockedQueryOf(origin, { by: 'none' })).toEqual({ origin });
    expect(blockedQueryOf(origin, { by: 'line', line: 6 })).toEqual({ origin, line: 6 });
    expect(blockedQueryOf(origin, { by: 'reason', reason: 'unreachable' })).toEqual({ origin, reason: 'unreachable' });
    expect(blockedQueryOf(origin, { by: 'conflict' })).toEqual({ origin, conflict: true });
  });

  it('choosing a line lists exactly the URLs the file says it blocked', () => {
    for (const line of TEST_FILE_LINES.filter((each) => (each.blocked ?? 0) > 0)) {
      const page = blockedPage({ ...blockedQueryOf(TEST_FILE.origin, { by: 'line', line: line.number }) });
      expect(page.matching).toBe(line.blocked);
      expect(page.rows.every((row) => lineOf(row) === line.number)).toBe(true);
    }
    const unknown = blockedPage({ ...blockedQueryOf(TEST_FILE.origin, { by: 'reason', reason: 'unknown' }) });
    expect(unknown.matching).toBe(TEST_FILE.unmatched.unknown);
  });

  it('choosing a URL shows the line that decided it, and none for a URL no line did', () => {
    expect(lineOf(TEST_BLOCKED[0])).toBe(6);
    expect(lineOf(TEST_BLOCKED[3])).toBeNull();
    expect(lineOf(undefined)).toBeNull();
    expect(lineOf({ reason: 'unreachable', line: null })).toBeNull();
  });

  it('marks the line the list is narrowed to, else the line of the URL under the cursor', () => {
    expect(markedLine({ by: 'none' }, TEST_BLOCKED[0])).toBe(6);
    expect(markedLine({ by: 'none' }, TEST_BLOCKED[3])).toBeNull();
    expect(markedLine({ by: 'none' }, undefined)).toBeNull();
    expect(markedLine({ by: 'line', line: 3 }, TEST_BLOCKED[0])).toBe(3);
    expect(markedLine({ by: 'reason', reason: 'unknown' }, TEST_BLOCKED[3])).toBeNull();
    expect(markedLine({ by: 'conflict' }, TEST_BLOCKED[0])).toBe(6);
  });

  it('words what the list is narrowed to, with the line’s own text when the file has it', () => {
    expect(narrowingWords({ by: 'none' }, TEST_FILE)).toBeNull();
    expect(narrowingWords({ by: 'line', line: 6 }, TEST_FILE)).toEqual({
      field: 'Line',
      value: '6: Disallow: /admin/',
    });
    // A line the file doesn't have (a link from before the site was crawled again), or a file not read yet.
    expect(narrowingWords({ by: 'line', line: 99 }, TEST_FILE)).toEqual({ field: 'Line', value: '99' });
    expect(narrowingWords({ by: 'line', line: 6 }, undefined)).toEqual({ field: 'Line', value: '6' });
    // A line that's no rule is never called one: a User-agent line, a blank one.
    expect(narrowingWords({ by: 'line', line: 2 }, TEST_FILE)).toEqual({ field: 'Line', value: '2: User-agent: *' });
    expect(narrowingWords({ by: 'line', line: 4 }, TEST_FILE)).toEqual({ field: 'Line', value: '4' });
    expect(narrowingWords({ by: 'line', line: 2_483 }, undefined)?.value).toBe('2,483');
    expect(narrowingWords({ by: 'reason', reason: 'unreachable' }, TEST_FILE)?.value).toBe(
      'robots.txt couldn’t be read',
    );
    expect(narrowingWords({ by: 'reason', reason: 'unknown' }, TEST_FILE)?.value).toBe('no matching rule');
    expect(narrowingWords({ by: 'conflict' }, TEST_FILE)).toEqual({
      field: 'Conflict',
      value: 'in your sitemap or menu but blocked',
    });
  });

  it('says why no URLs are listed for a line: no such line, no rule, or a rule that blocked nothing', () => {
    expect(emptyLineWords(TEST_FILE, 999)).toBe('This file has no line 999.');
    expect(emptyLineWords(TEST_FILE, 0)).toBe('This file has no line 0.');
    expect(emptyLineWords(TEST_FILE, 2)).toBe('Line 2 isn’t a rule, so it blocked no URLs.');
    expect(emptyLineWords(TEST_FILE, 1)).toBe('Line 1 isn’t a rule, so it blocked no URLs.');
    expect(emptyLineWords(TEST_FILE, 7)).toBe('Line 7 blocked no URLs.');
    expect(emptyLineWords(TEST_FILE, 8)).toBe('Line 8 blocked no URLs.');
    // A file with no text kept has no lines at all; one that couldn't be read says no more than is known.
    expect(emptyLineWords({ lines: [] }, 3)).toBe('This file has no line 3.');
    expect(emptyLineWords(undefined, 3)).toBe('No blocked URLs are listed for line 3.');
  });

  it('finds a line by its number, wherever it is in the list', () => {
    expect(lineAt(TEST_FILE, 6)?.text).toBe('Disallow: /admin/');
    expect(lineAt({ lines: [TEST_FILE_LINES[5]!] }, 6)?.text).toBe('Disallow: /admin/');
    expect(lineAt(TEST_FILE, 0)).toBeUndefined();
    expect(lineAt(TEST_FILE, 99)).toBeUndefined();
  });
});

describe('the origin in the route', () => {
  it('is the origin as the engine spells it, or none for the list', () => {
    expect(originFromParam(undefined)).toBeNull();
    expect(originFromParam('')).toBeNull();
    expect(originFromParam('https://www.example.com')).toBe('https://www.example.com');
    expect(originFromParam('http://127.0.0.1:50196')).toBe('http://127.0.0.1:50196');
    // What can't be an http(s) URL is passed on as it is: the engine says when it has no origin like it.
    expect(originFromParam('not an origin')).toBe('not an origin');
    expect(originFromParam('www.example.com')).toBe('www.example.com');
    expect(originFromParam('mailto:someone@example.com')).toBe('mailto:someone@example.com');
  });

  it('reads an origin spelled another way as the one spelling the engine keeps', () => {
    // Capitals, a slash or a path after it, the scheme's own port, spaces around it.
    expect(originFromParam('HTTPS://WWW.Example.COM')).toBe('https://www.example.com');
    expect(originFromParam('https://www.example.com/')).toBe('https://www.example.com');
    expect(originFromParam('https://www.example.com/robots.txt')).toBe('https://www.example.com');
    expect(originFromParam('https://www.example.com:443')).toBe('https://www.example.com');
    expect(originFromParam('http://www.example.com:80/')).toBe('http://www.example.com');
    expect(originFromParam('  https://www.example.com ')).toBe('https://www.example.com');
    // A port that isn't the scheme's own is part of the origin.
    expect(originFromParam('HTTP://127.0.0.1:50196/')).toBe('http://127.0.0.1:50196');
    // An international name, as it's written and as it's sent (punycode, which the engine stores): one origin.
    expect(originFromParam('https://bücher.example')).toBe('https://xn--bcher-kva.example');
    expect(originFromParam('https://BÜCHER.example/')).toBe('https://xn--bcher-kva.example');
    expect(originFromParam('https://xn--bcher-kva.example')).toBe('https://xn--bcher-kva.example');
  });
});

describe('copying the blocked URLs', () => {
  it('reads on by offset until every URL the list is narrowed to has been read', async () => {
    const all = Array.from({ length: 2_500 }, (_, index) => ({ ...TEST_BLOCKED[0]!, id: index + 1, url: `u${index}` }));
    const asked: unknown[] = [];
    const { setTransport } = await import('../../api/transport');
    const { fakeSidecar, json, TEST_CRAWL_ID } = await import('../../test/fakeSidecar');
    setTransport(
      fakeSidecar([
        [
          'POST',
          /\/robots\/blocked$/,
          (request) => {
            asked.push(request.body);
            return json(blockedPage(request.body as Parameters<typeof blockedPage>[0], all));
          },
        ],
      ]),
    );
    const read = blockedUrlsReader(TEST_CRAWL_ID, { origin: 'https://example.com', line: 6 });
    const signal = new AbortController().signal;
    const urls: string[] = [];
    let after: string | undefined;
    for (;;) {
      const page = await read(after, signal);
      urls.push(...page.rows.map((row) => row.url ?? ''));
      if (page.next === null) break;
      after = page.next;
    }
    expect(urls).toHaveLength(2_500);
    expect(new Set(urls).size).toBe(2_500);
    expect(asked).toEqual([
      { origin: 'https://example.com', line: 6, offset: 0, limit: 1_000 },
      { origin: 'https://example.com', line: 6, offset: 1_000, limit: 1_000 },
      { origin: 'https://example.com', line: 6, offset: 2_000, limit: 1_000 },
    ]);
  });
});
