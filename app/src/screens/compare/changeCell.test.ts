import { describe, expect, it } from 'vitest';
import type { CheckChange } from '../../api/fixLoop';
import { checkChangeCell, groupChangeCell, rowChangeCell } from './changeCell';

const change = (patch: Partial<CheckChange>): CheckChange => ({
  checkId: 'broken-internal-links',
  severity: 'error',
  severityThen: 'error',
  state: 'same',
  then: 9,
  now: 9,
  fixed: 0,
  gone: 0,
  notChecked: 0,
  still: 9,
  new: 0,
  newUrl: 0,
  notComparedThen: 0,
  ...patch,
});

const SINCE = { iso: '2026-10-17T09:00:00Z', later: false };
const UNTIL = { iso: '2026-11-20T09:00:00Z', later: true };

describe('a check’s change in the checks list and the head (Advanced)', () => {
  it('shows the move as text, says it in words, and gives the rows’ split in its tooltip', () => {
    expect(
      checkChangeCell(change({ state: 'fewer', then: 9, now: 4, fixed: 3, notChecked: 2, still: 4 }), SINCE),
    ).toEqual({
      shown: '−5',
      spoken: '5 fewer since 17 Oct',
      title: 'Since 17 Oct: 9 then, 4 now. 3 fixed, 0 new, 2 not checked this time',
    });
    expect(checkChangeCell(change({ state: 'more', then: 2, now: 4, still: 2, new: 2 }), SINCE).shown).toBe('+2');
    expect(checkChangeCell(change({ state: 'new', then: 0, now: 3, new: 3 }), SINCE)).toMatchObject({
      shown: 'New',
      spoken: 'new since 17 Oct',
    });
    expect(checkChangeCell(change({ state: 'fixed', then: 3, now: 0, fixed: 3 }), SINCE)).toMatchObject({
      shown: 'Fixed',
      spoken: 'fixed since 17 Oct',
    });
    expect(checkChangeCell(change({ state: 'newCheck', then: null, now: 3 }), SINCE)).toMatchObject({
      shown: 'New check',
      spoken: 'not looked for on 17 Oct',
    });
    expect(checkChangeCell(change({ state: 'notCheckedNow', then: 3, now: null }), SINCE)).toMatchObject({
      shown: 'Not checked',
      spoken: 'not checked this time',
    });
    expect(checkChangeCell(change({ state: 'same' }), SINCE)).toMatchObject({
      shown: '±0',
      spoken: 'the same as on 17 Oct',
    });
  });

  it('reads "until" a later crawl', () => {
    expect(checkChangeCell(change({ state: 'fewer', then: 9, now: 4 }), UNTIL)).toMatchObject({
      spoken: '5 fewer by 20 Nov',
    });
    expect(checkChangeCell(change({ state: 'fixed', then: 9, now: 0 }), UNTIL).spoken).toBe('fixed by 20 Nov');
  });

  it('says counts only without the rows’ split', () => {
    expect(
      checkChangeCell(change({ state: 'fewer', then: 9, now: 4, fixed: null, new: null, still: null }), SINCE).title,
    ).toBe('Since 17 Oct: 9 then, 4 now');
  });
});

describe('a group’s and a row’s change (Advanced)', () => {
  it('gives a group its URLs then and its move', () => {
    expect(groupChangeCell({ state: 'fewer', then: 5 }, 3, SINCE)).toEqual({
      then: '5',
      shown: '−2',
      spoken: '2 fewer since 17 Oct',
    });
    expect(groupChangeCell({ state: 'new', then: 0 }, 3, SINCE)).toEqual({
      then: '–',
      shown: 'New',
      spoken: 'new since 17 Oct',
    });
    expect(groupChangeCell({ state: 'same', then: 3 }, 3, SINCE).shown).toBe('±0');
  });

  it('gives a row its state then', () => {
    expect(rowChangeCell({ state: 'still', detailThen: 'Links to 2 broken URLs' }, SINCE)).toEqual({
      shown: 'Still',
      title: 'Flagged on 17 Oct too: Links to 2 broken URLs',
    });
    expect(rowChangeCell({ state: 'new', detailThen: null }, SINCE).shown).toBe('New');
    expect(rowChangeCell({ state: 'newUrl', detailThen: null }, SINCE).shown).toBe('New URL');
    expect(rowChangeCell({ state: 'notComparedThen', detailThen: null }, SINCE).shown).toBe('Not compared');
    // Until a later crawl, read forward.
    expect(rowChangeCell({ state: 'new', detailThen: null }, UNTIL).shown).toBe('Fixed by then');
    expect(rowChangeCell({ state: 'newUrl', detailThen: null }, UNTIL).shown).toBe('Gone by then');
  });
});
