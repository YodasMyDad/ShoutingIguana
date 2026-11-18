import { describe, expect, it } from 'vitest';
import type { Analysis, Check, Checks } from '../../api/types';
import { thingsMoved, thingsMovedWords } from './thingsMoved';

const check = (id: string, severity: Check['severity']): Check => ({
  id,
  category: 'links',
  severity,
  title: id,
  description: '',
  why: '',
  fix: '',
  learnMore: null,
  grouping: 'detail',
});

const CHECKS: Checks = {
  categories: [{ id: 'links', title: 'Links' }],
  checks: [
    check('broken', 'error'),
    check('gone', 'error'),
    check('titles', 'warning'),
    check('anchors', 'notice'),
    check('fresh', 'warning'),
  ],
};

const analysis = (counts: Record<string, [count: number, causes: number | null]>): Analysis => ({
  analyzedAt: '2026-11-17T10:21:00Z',
  engineVersion: 'test',
  pagesAnalyzed: 265,
  durationMs: 7_012,
  isPartial: false,
  outOfDate: false,
  outOfDateReasons: [],
  notes: [],
  counts: Object.entries(counts).map(([checkId, [count, causes]]) => ({ checkId, count, causes })),
  skipped: [],
});

/** Then: 12 broken links (9 things), 4 gone (4), 6 titles (3), 800 anchors (40). */
const THEN = analysis({ broken: [12, 9], gone: [4, 4], titles: [6, 3], anchors: [800, 40], fresh: [0, null] });
/** Now: broken down to 3 things, gone fixed, titles up to 5, anchors 30, a new problem of 2 things. */
const NOW = analysis({ broken: [4, 3], gone: [0, null], titles: [10, 5], anchors: [600, 30], fresh: [2, 2] });

describe('what moved, in the headline’s unit: things (Advanced)', () => {
  it('counts the things to fix and worth a look then and now, as the headline does, and the things fixed and new', () => {
    expect(thingsMoved(CHECKS, NOW, THEN, new Set())).toEqual({
      fixThen: 16,
      fixNow: 10,
      lookThen: 40,
      lookNow: 30,
      // broken 9 → 3 and gone 4 → 0: 10 fixed; titles 3 → 5 and fresh 0 → 2: 4 new.
      fixed: 10,
      added: 4,
    });
  });

  it('leaves hidden checks out, as the headline does', () => {
    expect(thingsMoved(CHECKS, NOW, THEN, new Set(['gone']))).toMatchObject({ fixThen: 12, fixed: 6 });
  });

  it('has nothing to say when either analysis didn’t count its things (an earlier engine’s)', () => {
    const counted = analysis({ broken: [12, null], gone: [4, 4], titles: [6, 3], anchors: [800, 40] });
    expect(thingsMoved(CHECKS, NOW, counted, new Set())).toBeNull();
  });

  it('says it as the headline does: things fixed, new, and the notices’ move', () => {
    expect(thingsMovedWords({ fixThen: 16, fixNow: 10, lookThen: 40, lookNow: 30, fixed: 10, added: 4 })).toBe(
      'About 10 things fixed · about 4 new · about 10 fewer worth a look',
    );
    expect(thingsMovedWords({ fixThen: 1, fixNow: 0, lookThen: 0, lookNow: 1, fixed: 1, added: 0 })).toBe(
      '1 thing fixed · 1 more worth a look',
    );
    expect(thingsMovedWords({ fixThen: 0, fixNow: 3, lookThen: 0, lookNow: 0, fixed: 0, added: 3 })).toBe(
      'About 3 new things to fix',
    );
    expect(thingsMovedWords({ fixThen: 3, fixNow: 3, lookThen: 5, lookNow: 5, fixed: 0, added: 0 })).toBeNull();
  });
});
