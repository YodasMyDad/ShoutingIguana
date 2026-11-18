import { describe, expect, it } from 'vitest';
import { recheckRecord, recheckResult } from '../../test/fixLoopSidecar';
import { recheckAskOf, recheckOutcomeWords, recheckResultLine, recheckWhatWords, recheckable } from './rechecks';

const titleOf = (id: string) => ({ 'broken-internal-links': 'Broken internal links' })[id] ?? null;

describe('a recheck in Advanced’s words', () => {
  it('says what it rechecked: the check, one of its groups, or URLs picked', () => {
    expect(recheckWhatWords(recheckRecord(), titleOf)).toBe('Broken internal links');
    expect(recheckWhatWords(recheckRecord({ group: { detail: null, relatedUrlId: 812 }, judged: 3 }), titleOf)).toBe(
      'Broken internal links, one group (3 URLs)',
    );
    expect(recheckWhatWords(recheckRecord({ urlList: true, judged: 6 }), titleOf)).toBe(
      '6 selected URLs, by Broken internal links',
    );
    expect(recheckWhatWords(recheckRecord({ urlList: true, checkId: null, judged: 1 }), titleOf)).toBe(
      '1 selected URL',
    );
    expect(recheckWhatWords(recheckRecord({ checkId: 'gone-check' }), titleOf)).toBe('gone-check');
  });

  it('says its result in a line, "not checked" never counted as fixed', () => {
    expect(recheckResultLine(recheckRecord())).toBe('4 of 5 fixed · 1 still · 0 new · Score 86 → 88');
    expect(
      recheckResultLine(
        recheckRecord({ result: recheckResult({ fixed: 2, still: 0, notChecked: 3, scoreAfter: 86 }) }),
      ),
    ).toBe('2 of 5 fixed · 0 still · 3 not checked · 0 new');
    expect(
      recheckResultLine(
        recheckRecord({ status: 'stopped', result: recheckResult({ fixed: 2, still: 1, notRechecked: 2 }) }),
      ),
    ).toBe('Stopped: 2 of 5 fixed · 1 still · 2 not rechecked · 0 new · Score 86 → 88');
    expect(recheckResultLine(recheckRecord({ status: 'running', result: null, settledAt: null }))).toBe('Running');
    expect(recheckResultLine(recheckRecord({ result: null, settledAt: null }))).toBe(
      'Not worked out yet: analyse the crawl to settle it',
    );
    // A list of URLs with no check gives no outcomes: only what it fetched.
    expect(recheckResultLine(recheckRecord({ urlList: true, checkId: null, fetched: 8, result: null }))).toBe(
      '8 URLs fetched again',
    );
  });

  it('words each outcome of a recheck’s URLs', () => {
    expect(recheckOutcomeWords('fixed')).toBe('Fixed');
    expect(recheckOutcomeWords('still')).toBe('Still');
    expect(recheckOutcomeWords('notChecked')).toBe('Not checked');
    expect(recheckOutcomeWords('notRechecked')).toBe('Not rechecked');
    expect(recheckOutcomeWords('new')).toBe('New');
    expect(recheckOutcomeWords(null)).toBe('');
  });

  it('asks for a check, one of its groups, or URLs picked (at most 1,000, judged by the check)', () => {
    expect(recheckAskOf('broken-internal-links', { scope: 'check' })).toEqual({ checkId: 'broken-internal-links' });
    expect(
      recheckAskOf('broken-internal-links', { scope: 'group', group: { detail: null, relatedUrlId: 812 } }),
    ).toEqual({ checkId: 'broken-internal-links', group: { detail: null, relatedUrlId: 812 } });
    expect(recheckAskOf('broken-internal-links', { scope: 'urls', urlIds: [3, 1, 3] })).toEqual({
      checkId: 'broken-internal-links',
      urlIds: [3, 1],
    });
    expect(
      recheckAskOf(null, { scope: 'urls', urlIds: Array.from({ length: 1_200 }, (_, index) => index + 1) }),
    ).toEqual({ urlIds: Array.from({ length: 1_000 }, (_, index) => index + 1) });
  });

  it('knows a check a recheck can settle from its catalog entry: one with nothing to read again needs a full crawl', () => {
    expect(recheckable({ recheck: ['urls'] })).toBe(true);
    expect(recheckable({ recheck: [] })).toBe(false);
    // A sidecar from before the fix loop offers no recheck.
    expect(recheckable({})).toBe(false);
  });
});
