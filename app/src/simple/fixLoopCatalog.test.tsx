// The fix loop over the whole catalog (all 170 checks, the new ones included): every check's change has its words in
// Simple, every check fixed since has its line in the Fixed group, and every check a recheck can't settle (its
// `recheck` empty: orphan-page, no-https, canonical-unlinked, non-indexable-inlinks-only, deep-page, hreflang-unlinked…)
// offers Check the site again, never Recheck, on its problem page and wherever Simple shows its card.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import type { CheckChange } from '../api/fixLoop';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createAppRouter } from '../app/router';
import { checkChangeWords, clearedWords, fixedThenWords } from '../changes/changeWords';
import { resetPendingRechecks } from '../crawl/recheck';
import { liveStore } from '../live/liveStore';
import { replaceSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import { gardenComparison, recheckPlan, recheckRecord, recheckRoutes } from '../test/fixLoopSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { GARDEN_ANALYSIS, SIMPLE_CHECKS, analysisWith, gardenItem, simpleRoutes } from '../test/simpleSidecar';
import { fixedSinceEntries } from './fixedSinceEntries';
import { recheckCardState } from './recheckCardState';
import { TABLE_STUBBED } from './tableStubbed';

const CHECKS = SIMPLE_CHECKS.checks;
const UNSUPPORTED = CHECKS.filter((check) => check.recheck?.length === 0);

afterEach(() => {
  replaceSettings({ mode: 'advanced', hiddenChecks: [], hiddenChecksByCrawl: {} });
  liveStore.reset();
  resetPendingRechecks();
});

describe('the fix loop over the whole catalog', () => {
  it('knows which checks a recheck can’t settle: every catalog check says, and these say none', () => {
    expect(CHECKS.length).toBeGreaterThanOrEqual(170);
    expect(CHECKS.filter((check) => check.recheck === undefined)).toEqual([]);
    expect(UNSUPPORTED.map((check) => check.id)).toEqual(
      expect.arrayContaining([
        'orphan-page',
        'no-https',
        'canonical-unlinked',
        'non-indexable-inlinks-only',
        'deep-page',
      ]),
    );
  });

  it('gives every check a recheck can’t settle Check the site again, whatever the plan, past rechecks or runs say', () => {
    for (const check of UNSUPPORTED) {
      for (const plan of [recheckPlan({ checkId: check.id }), undefined]) {
        expect(
          recheckCardState({
            check,
            plan,
            planFailed: false,
            rechecks: [recheckRecord({ checkId: check.id })],
            run: null,
            pending: undefined,
          }),
          check.id,
        ).toEqual({ kind: 'unsupported' });
      }
    }
  });

  it('says every check’s change in Simple’s words, in every state', () => {
    const since = '2026-10-17T14:13:30Z';
    const states: CheckChange['state'][] = ['new', 'fixed', 'fewer', 'more', 'same', 'newCheck', 'notCheckedNow'];
    for (const check of CHECKS) {
      for (const state of states) {
        const words = checkChangeWords({ state, then: 9, now: state === 'fixed' ? 0 : 4 }, since, 'simple');
        if (state === 'notCheckedNow') expect(words, check.id).toBeNull();
        else expect(words, `${check.id} ${state}`).toMatch(/\S/);
      }
      for (const state of ['fixed', 'gone', 'notChecked'] as const) {
        expect(clearedWords(state, check.references, 'simple'), check.id).toMatch(/\S/);
      }
      expect(fixedThenWords(check, 3, since), check.id).toMatch(/ on 17 Oct$/);
    }
  });

  it('lists every check fixed since in the Fixed group with its title and its count then, problems first', () => {
    const then = Object.fromEntries(CHECKS.map((check) => [check.id, 2]));
    const comparison = gardenComparison({
      now: { ...GARDEN_ANALYSIS, counts: CHECKS.map((check) => ({ checkId: check.id, count: 0 })), skipped: [] },
      then,
    });
    const entries = fixedSinceEntries(comparison, CHECKS, new Set());
    expect(entries).toHaveLength(CHECKS.length);
    expect(entries.every((entry) => entry.then.length > 0 && entry.check.plainTitle !== undefined)).toBe(true);
    const firstNotice = entries.findIndex((entry) => entry.severity === 'notice');
    expect(entries.slice(firstNotice).every((entry) => entry.severity === 'notice')).toBe(true);
  });
});

describe.skipIf(TABLE_STUBBED)('a check a recheck can’t settle, on its problem page', () => {
  for (const check of UNSUPPORTED) {
    it(`${check.id}: Check the site again, never Recheck`, async () => {
      replaceSettings({ mode: 'simple', hiddenChecks: [] });
      // The check found something (a check the garden crawl skipped, or never counted, is counted here).
      const found = analysisWith(
        { [check.id]: 3 },
        {},
        { skipped: GARDEN_ANALYSIS.skipped.filter((skip) => skip.checkId !== check.id) },
      );
      const analysis = found.counts.some((count) => count.checkId === check.id)
        ? found
        : { ...found, counts: [...found.counts, { checkId: check.id, count: 3 }] };
      setTransport(fakeSidecar([...recheckRoutes(), ...simpleRoutes(gardenItem(), analysis)]));
      const queryClient = createQueryClientWithoutBackoff();
      const router = createAppRouter(
        queryClient,
        createMemoryHistory({ initialEntries: [`/crawls/${TEST_CRAWL_ID}/issues/${check.id}`] }),
      );
      render(<App router={router} queryClient={queryClient} />);
      await act(() => router.load());
      const card = within(await screen.findByRole('region', { name: 'Fixed it?' }, { timeout: 10_000 }));
      expect(
        card.getByText('This one needs the whole site read again to see a fix: check the site again.'),
      ).toBeVisible();
      expect(card.getByRole('link', { name: 'Check the site again' })).toBeVisible();
      expect(card.queryByRole('button', { name: /Recheck/ })).toBeNull();
      // The first draws the whole app cold.
    }, 30_000);
  }
});
