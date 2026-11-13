import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import type { Analysis, Score } from '../api/types';
import { App } from '../app/App';
import { createAppRouter } from '../app/router';
import { replaceSettings } from '../settings/settingsStore';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import { ISSUES_ANALYSIS } from '../test/issuesSidecar';
import { overviewRoutes } from '../test/overviewSidecar';

const OVERVIEW = `/crawls/${TEST_CRAWL_ID}/overview`;

/** The garden crawl's score, as the engine stores it: 85, Good. */
const GARDEN_SCORE: Score = {
  version: 1,
  value: 85,
  band: 'good',
  lost: 15,
  beforeCeiling: 85,
  heldBySerious: false,
  partial: false,
  covers: {
    pages: 265,
    urls: 4_521,
    checksRun: 83,
    checksSkipped: 4,
    complete: true,
    reason: null,
    blockedByRobots: 0,
  },
  lines: [
    {
      checkId: 'internal-client-error',
      severity: 'error',
      count: 9,
      of: 4_521,
      unit: 'urls',
      extent: 'few',
      points: 2,
    },
    { checkId: 'title-too-long', severity: 'notice', count: 3, of: 265, unit: 'pages', extent: 'some', points: 0 },
  ],
  ifFixed: { checkIds: ['internal-client-error'], value: 98 },
};

async function open(analysis: Analysis, path = OVERVIEW) {
  setTransport(fakeSidecar(overviewRoutes({ analysis })));
  replaceSettings({ mode: 'advanced' });
  const queryClient = createQueryClientWithoutBackoff();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return router;
}

afterEach(() => setTransport(fakeSidecar([])));

describe('the score on Advanced’s Overview', () => {
  it('stands beside the answer with its word, and opens its breakdown; Esc closes it, back on the score', async () => {
    const router = await open({ ...ISSUES_ANALYSIS, score: GARDEN_SCORE, noScoreReason: null });
    const score = await screen.findByRole('link', { name: /Score 85 out of 100.*Score: Good/ });
    expect(score).toHaveAttribute('href', `${OVERVIEW}?score=1`);
    fireEvent.click(score);
    const dialog = await screen.findByRole('dialog', { name: 'How the score is worked out' });
    expect(router.state.location.search).toEqual({ score: 1 });
    expect(dialog).toHaveTextContent('100 − 15');
    fireEvent.keyDown(dialog, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(router.state.location.search).toEqual({});
    await waitFor(() => expect(score).toHaveFocus());
  });

  it('says "so far" for a crawl that didn’t read all of the site', async () => {
    await open({ ...ISSUES_ANALYSIS, score: { ...GARDEN_SCORE, partial: true }, noScoreReason: null });
    expect(await screen.findByRole('link', { name: /Score 85 out of 100 so far.*Score so far: Good/ })).toBeVisible();
  });

  it('says why there is no score, and opens nothing', async () => {
    await open({ ...ISSUES_ANALYSIS, score: null, noScoreReason: 'notScored' });
    expect(await screen.findByText('No score')).toBeVisible();
    expect(screen.getByText(/analyse it again to score it/)).toBeVisible();
    expect(screen.queryByRole('link', { name: /Score/ })).toBeNull();
  });

  it('opens over Overview from its address', async () => {
    await open({ ...ISSUES_ANALYSIS, score: GARDEN_SCORE, noScoreReason: null }, `${OVERVIEW}?score=1`);
    expect(await screen.findByRole('dialog', { name: 'How the score is worked out' })).toBeVisible();
  });
});
