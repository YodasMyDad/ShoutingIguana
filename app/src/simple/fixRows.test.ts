import { describe, expect, it } from 'vitest';
import type { Check, CheckCount, ClearedCheck, Score } from '../api/types';
import type { CheckEntry } from '../screens/issues/checkList';
import { fixFirstJobs, fixRows } from './fixRows';

const entry = (id: string, count: number, clears: ClearedCheck[] = [], severity: Check['severity'] = 'error') =>
  ({
    check: { id, severity, title: id } as Check,
    outcome: severity,
    count,
    skipReason: null,
    hidden: false,
    causes: count,
    templateCauses: 0,
    clears,
  }) satisfies CheckEntry;

const ids = (rows: ReturnType<typeof fixRows>) =>
  rows.map((row) => [row.entry.check.id, row.nested.map((nested) => nested.check.id), row.points]);

// In the engine's order: the 404s, the links to them, a broken image, a server error.
const FOUR_O_FOUR = entry('internal-client-error', 9, [
  { checkId: 'broken-internal-links', pages: 5, all: true },
  { checkId: 'image-broken', pages: 1, all: false },
]);
const LINKS = entry('broken-internal-links', 5);
const IMAGES = entry('image-broken', 2);
const SERVER = entry('internal-server-error', 1, [{ checkId: 'broken-internal-links', pages: 5, all: true }]);
const PART: CheckEntry[] = [FOUR_O_FOUR, LINKS, IMAGES, SERVER];
const POINTS = new Map<string, number>([
  ['internal-client-error', 2],
  ['broken-internal-links', 4],
  ['image-broken', 3],
  ['internal-server-error', 1],
]);
const FIX_FIRST = PART.map((each) => each.check.id) satisfies CheckCount['checkId'][];

describe('What to fix’s rows', () => {
  it('fold a check a fix clears entirely under that fix, with the points of both, once', () => {
    expect(ids(fixRows(PART, { sort: 'fixFirst', fixFirst: FIX_FIRST, points: POINTS }))).toEqual([
      ['internal-client-error', ['broken-internal-links'], 6],
      // A part of the broken images isn't all of them: they stay a job of their own.
      ['image-broken', [], 3],
      // The links are folded under the first fix that clears them, not again.
      ['internal-server-error', [], 1],
    ]);
  });

  it('come in the order asked for, a row’s points with its folded checks’', () => {
    expect(ids(fixRows(PART, { sort: 'points', fixFirst: FIX_FIRST, points: POINTS })).map(([id]) => id)).toEqual([
      'internal-client-error',
      'image-broken',
      'internal-server-error',
    ]);
    expect(ids(fixRows(PART, { sort: 'count', fixFirst: FIX_FIRST, points: POINTS })).map(([id]) => id)).toEqual([
      'internal-client-error',
      'image-broken',
      'internal-server-error',
    ]);
  });

  it('fold only within the part, and give a notice no points', () => {
    const notices = [entry('redirect-chain', 3, [{ checkId: 'links-to-redirects', pages: 4, all: true }], 'notice')];
    // The links to redirects are in another part: nothing to fold.
    expect(ids(fixRows(notices, { sort: 'fixFirst', points: new Map() }))).toEqual([['redirect-chain', [], null]]);
  });

  it('never fold a check that has others folded under it', () => {
    const chain = [
      entry('redirect-broken', 2, [{ checkId: 'internal-client-error', pages: 2, all: true }]),
      FOUR_O_FOUR,
      LINKS,
    ];
    // The 404s are folded under the broken redirects; the links then stay, as the 404s are folded already.
    expect(
      ids(fixRows(chain, { sort: 'fixFirst', fixFirst: chain.map((each) => each.check.id), points: new Map() })).map(
        ([id, nested]) => [id, nested],
      ),
    ).toEqual([
      ['redirect-broken', ['internal-client-error']],
      ['broken-internal-links', []],
    ]);
  });
});

// The audit of 2026-11-24: Summary folded within its five, so a problem whose fix clears one outside them said +4
// there beside +8 on What to fix and on its own page.
describe('Summary’s five to fix first', () => {
  const lines = (points: Map<string, number>) =>
    [...points].map(([checkId, value]) => ({ checkId, points: value })) as Score['lines'];
  const filler = (id: string) => entry(id, 20);
  // The 404s lead; four more errors fill the five; the links the 404s' fix clears come sixth, outside the five.
  const errors = [FOUR_O_FOUR, filler('a'), filler('b'), filler('c'), filler('d'), LINKS];
  const results = { found: { error: errors, warning: [], notice: [] }, fixFirst: errors.slice(0, 5) };

  it('carry the points What to fix’s rows carry, whether or not what a fix clears is among the five', () => {
    const jobs = fixFirstJobs(results, { lines: lines(POINTS) });
    const whatToFix = fixRows(errors, {
      sort: 'fixFirst',
      fixFirst: errors.map((each) => each.check.id),
      points: POINTS,
    });
    expect(ids(jobs)).toEqual(ids(whatToFix));
    expect(ids(jobs)[0]).toEqual(['internal-client-error', ['broken-internal-links'], 6]);
    expect(jobs).toHaveLength(5);
  });

  it('leave out a job none of the five is part of', () => {
    const more = { found: { ...results.found, error: [...errors, filler('e')] }, fixFirst: results.fixFirst };
    expect(fixFirstJobs(more, null).map((row) => row.entry.check.id)).toEqual([
      'internal-client-error',
      'a',
      'b',
      'c',
      'd',
    ]);
  });
});
