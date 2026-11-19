import { describe, expect, it } from 'vitest';
import type { CheckChange } from '../../api/fixLoop';
import { movedChecks } from './movedChecks';

const change = (patch: Partial<CheckChange> & Pick<CheckChange, 'checkId' | 'state'>): CheckChange => ({
  severity: 'notice',
  severityThen: 'notice',
  then: 0,
  now: 0,
  fixed: null,
  gone: null,
  notChecked: null,
  still: null,
  new: null,
  newUrl: null,
  notComparedThen: null,
  ...patch,
});

const CATALOG = [
  { id: 'not-in-sitemap', title: 'Indexable page not in a sitemap', severity: 'notice', subject: 'page' },
  { id: 'orphan-page', title: 'Orphan page in a sitemap', severity: 'notice', subject: 'page' },
  { id: 'internal-client-error', title: 'Internal URL returns 4xx', severity: 'error', subject: 'url' },
  { id: 'title-duplicate', title: 'Duplicate title', severity: 'warning', subject: 'page' },
] as const;

// The audit of 2026-11-19, H-06: "Checks: 2 fixed · 2 more" was all the card said of a check that went from 18 pages
// to 257.
describe('the checks that moved', () => {
  it('lists what got worse, the largest rise first, with both counts in what the check flags', () => {
    const { worse } = movedChecks(
      [
        change({ checkId: 'internal-client-error', state: 'more', severity: 'error', then: 6, now: 8 }),
        change({ checkId: 'not-in-sitemap', state: 'more', then: 18, now: 257 }),
        change({ checkId: 'title-duplicate', state: 'new', severity: 'warning', then: 0, now: 2 }),
      ],
      CATALOG,
    );
    expect(worse.map((check) => [check.title, check.counts])).toEqual([
      ['Indexable page not in a sitemap', '18 → 257 pages'],
      // A tie in size: the more severe first.
      ['Internal URL returns 4xx', '6 → 8 URLs'],
      ['Duplicate title', '0 → 2 pages'],
    ]);
    expect(worse[0]?.spoken).toBe('18 pages then, 257 now');
  });

  it('lists what got better apart, fixed and fewer alike, and leaves out what didn’t move or didn’t run', () => {
    const { worse, better } = movedChecks(
      [
        change({ checkId: 'orphan-page', state: 'fixed', then: 1, now: 0 }),
        change({ checkId: 'title-duplicate', state: 'fewer', severity: 'warning', then: 9, now: 2 }),
        change({ checkId: 'internal-client-error', state: 'same', severity: 'error', then: 8, now: 8 }),
        change({ checkId: 'not-in-sitemap', state: 'newCheck', then: null, now: 4 }),
      ],
      CATALOG,
    );
    expect(worse).toEqual([]);
    expect(better.map((check) => [check.title, check.counts, check.state])).toEqual([
      ['Duplicate title', '9 → 2 pages', 'fewer'],
      ['Orphan page in a sitemap', '1 → 0 pages', 'fixed'],
    ]);
  });

  it('names a check the catalog no longer has by its id, counted in URLs', () => {
    const { worse } = movedChecks([change({ checkId: 'gone-check', state: 'new', then: 0, now: 1 })], CATALOG);
    expect(worse[0]).toMatchObject({ title: 'gone-check', counts: '0 → 1 URL' });
  });
});
