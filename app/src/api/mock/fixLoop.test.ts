import { describe, expect, it } from 'vitest';
import { GARDEN_ANALYSIS } from '../../test/simpleSidecar';
import { BROKEN_LINKS_CLEARED, gardenComparison, recheckRecord, recheckResult } from '../../test/fixLoopSidecar';
import {
  comparedGroups,
  comparedRows,
  itemChangesOf,
  recheckKindsOf,
  scenarioFixed,
  siteKeyOf,
  standInCleared,
  standInRecheckRows,
} from './fixLoop';

describe('the mock sidecar’s fix loop', () => {
  it('keys a site as the sidecar does: the host without www, and a port of its own', () => {
    expect(siteKeyOf('https://www.gardenfurnituresets.co.uk/')).toBe('gardenfurnituresets.co.uk');
    expect(siteKeyOf('http://gardenfurnituresets.co.uk:80/shop/')).toBe('gardenfurnituresets.co.uk');
    expect(siteKeyOf('http://127.0.0.1:5001/')).toBe('127.0.0.1:5001');
    expect(siteKeyOf(null)).toBeNull();
    expect(siteKeyOf('not a url')).toBeNull();
  });

  it('says what a recheck of each check reads again, as the engine’s catalog does', () => {
    expect(recheckKindsOf('broken-internal-links')).toEqual(['urls']);
    expect(recheckKindsOf('no-https')).toEqual([]);
    expect(recheckKindsOf('orphan-page')).toEqual([]);
    expect(recheckKindsOf('sitemap-missing')).toEqual(['sitemaps']);
    expect(recheckKindsOf('not-in-sitemap')).toEqual(['urls', 'sitemaps']);
  });

  it('compares the garden crawl with its earlier check: every state, the score, the totals', () => {
    const comparison = gardenComparison();
    const state = (id: string) => comparison.checks.find((check) => check.checkId === id)?.state;
    expect(state('internal-server-error')).toBe('fixed');
    expect(state('canonical-to-first-page')).toBe('new');
    expect(state('broken-internal-links')).toBe('fewer');
    expect(state('image-too-large')).toBe('more');
    expect(state('title-duplicate')).toBe('same');
    expect(state('slow-response')).toBe('newCheck');
    // Skipped in both: not listed.
    expect(state('orphan-page')).toBeUndefined();
    expect(comparison.score).toMatchObject({
      then: { value: 72 },
      now: { value: GARDEN_ANALYSIS.score!.value },
      change: 14,
    });
    expect(comparison.score!.lines).toContainEqual({ checkId: 'broken-internal-links', then: 7, now: 4 });
    expect(comparison.score!.lines).toContainEqual({ checkId: 'internal-server-error', then: 6, now: 0 });
    expect(comparison.totals.checks).toMatchObject({ fixed: 4, new: 1, fewer: 3, more: 1, newCheck: 1 });
    // The fix-first order first.
    expect(comparison.checks[0]?.checkId).toBe(GARDEN_ANALYSIS.fixFirst?.[0]);
    const broken = comparison.checks.find((check) => check.checkId === 'broken-internal-links');
    expect(broken).toMatchObject({ then: 9, now: 5, fixed: 3, gone: 2, still: 4, new: 1 });
  });

  it('lists the item’s changes by state, as cached summaries do, and none of the row split when counts only', () => {
    expect(itemChangesOf(gardenComparison())).toEqual({
      score: 14,
      partial: false,
      fixed: ['internal-server-error', 'redirect-chain', 'title-missing', 'h1-multiple'],
      new: ['canonical-to-first-page'],
      fewer: ['internal-client-error', 'broken-internal-links', 'title-too-long'],
      more: ['image-too-large'],
      newCheck: ['slow-response'],
      notCheckedNow: [],
    });
    const countsOnly = gardenComparison({ comparable: 'countsOnly' });
    expect(countsOnly.totals.issues).toBeNull();
    expect(countsOnly.checks.every((check) => check.fixed === null && check.still === null)).toBe(true);
  });

  it('pages what a check no longer flags, by URL or by cause, and nothing for a first crawl', () => {
    const comparison = gardenComparison();
    const rows = scenarioFixed('broken-internal-links', comparison, BROKEN_LINKS_CLEARED, {});
    expect(rows.total).toBe(5);
    expect(rows.compared?.with).toBe(comparison.with.id);
    expect(scenarioFixed('broken-internal-links', comparison, BROKEN_LINKS_CLEARED, { states: ['gone'] }).total).toBe(
      2,
    );
    const groups = scenarioFixed('broken-internal-links', comparison, BROKEN_LINKS_CLEARED, { level: 'groups' });
    expect(
      groups.groups?.map((group) => [group.relatedUrl?.replace(/^.*\.uk/, ''), group.then, group.fixed, group.gone]),
    ).toEqual([
      ['/old-range/', 3, 2, 1],
      ['/summer-sale/', 2, 1, 1],
    ]);
    expect(scenarioFixed('broken-internal-links', null, BROKEN_LINKS_CLEARED, {})).toEqual({
      checkId: 'broken-internal-links',
      compared: null,
      level: 'rows',
      total: null,
      offset: 0,
      rows: [],
    });
  });

  it('marks a page of groups and rows with their change, and nothing on rows when counts only', () => {
    const comparison = gardenComparison();
    const page = comparedGroups(
      {
        checkId: 'broken-internal-links',
        grouping: 'relatedUrl',
        total: 5,
        groupCount: 2,
        offset: 0,
        groups: [
          { detail: null, relatedUrlId: 1, relatedUrl: 'a', pages: 3, urls: 3, share: 0.6, likelyTemplate: false },
          { detail: null, relatedUrlId: 2, relatedUrl: 'b', pages: 2, urls: 2, share: 0.4, likelyTemplate: false },
        ],
      },
      comparison,
    );
    expect(page.groups.map((group) => group.change)).toEqual([
      { state: 'fewer', then: 5 },
      { state: 'same', then: 2 },
    ]);
    const rows = comparedRows(
      [
        { id: 1, url: 'a', detail: 'x' },
        { id: 2, url: 'b', detail: 'y' },
      ],
      comparison,
      'broken-internal-links',
    );
    expect(rows.map((row) => row.change?.state)).toEqual(['still', 'new']);
    expect(
      comparedRows([{ id: 1, url: 'a' }], gardenComparison({ comparable: 'countsOnly' }), 'broken-internal-links')[0]
        ?.change,
    ).toBeNull();
  });
});

describe('never a count over an empty list (round 4)', () => {
  it('gives every check fixed since rows of its own, as many as its count then, split as the comparison splits them', () => {
    const comparison = gardenComparison({ splits: { 'title-missing': { fixed: 2, gone: 0, notChecked: 1 } } });
    for (const change of comparison.checks.filter((each) => each.state === 'fixed' || each.state === 'fewer')) {
      const rows = standInCleared(change, 'https://www.example.com');
      expect(rows).toHaveLength((change.fixed ?? 0) + (change.gone ?? 0) + (change.notChecked ?? 0));
    }
    const titles = standInCleared(
      comparison.checks.find((each) => each.checkId === 'title-missing')!,
      'https://a.example',
    );
    expect(titles.map((row) => row.state)).toEqual(['fixed', 'fixed', 'notChecked']);
    expect(titles[2]?.reason).toEqual({ code: 'notRead', text: 'Not read this time' });
  });

  it('gives a settled recheck rows of its own, as many as each of its counts', () => {
    const recheck = recheckRecord({
      judged: 9,
      result: recheckResult({ fixed: 3, still: 4, notChecked: 1, notRechecked: 1, new: 2 }),
    });
    const rows = standInRecheckRows(recheck, 'https://www.example.com');
    const count = (outcome: string) => rows.filter((row) => row.outcome === outcome).length;
    expect([count('fixed'), count('still'), count('notChecked'), count('notRechecked'), count('new')]).toEqual([
      3, 4, 1, 1, 2,
    ]);
    // The URLs it judged (the flagged ones), and beside them those that have it now and didn't.
    expect(rows.filter((row) => row.outcome !== 'new')).toHaveLength(9);
  });
});
