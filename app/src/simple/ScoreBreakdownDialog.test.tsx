// The score's breakdown when there's no score to read: it says why, and never "Reading the score…" for ever.
import { QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { TEST_CRAWL_ID, fakeSidecar, json, problem, type FakeRoute } from '../test/fakeSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { SIMPLE_CHECKS } from '../test/simpleSidecar';
import { ScoreBreakdownDialog } from './ScoreBreakdownDialog';

function show(analysis: FakeRoute[2]) {
  setTransport(
    fakeSidecar([
      ['GET', /^\/api\/checks/, () => json(SIMPLE_CHECKS)],
      ['GET', /^\/api\/crawls\/[0-9a-f]{16}\/analysis$/, analysis],
    ]),
  );
  render(
    <QueryClientProvider client={createQueryClientWithoutBackoff()}>
      <ScoreBreakdownDialog crawlId={TEST_CRAWL_ID} open onOpenChange={() => undefined} />
    </QueryClientProvider>,
  );
  return within(screen.getByRole('dialog', { name: 'How the score is worked out' }));
}

afterEach(() => setTransport(fakeSidecar([])));

describe('the score’s breakdown with no score to read', () => {
  it('says the results haven’t been worked out, for a check never analysed', async () => {
    const dialog = show(() =>
      problem({ type: 'not-analyzed', title: 'This crawl hasn’t been analysed.', status: 404 }),
    );
    expect(
      await dialog.findByText(
        'There’s no score for this site check: its results haven’t been worked out yet. Work out the results to give it one.',
      ),
    ).toBeVisible();
    expect(dialog.queryByText('Reading the score…')).toBeNull();
  });

  it('says the score couldn’t be read, with Try again, when the read fails', async () => {
    const dialog = show(() => problem({ type: 'internal', title: 'Something went wrong.', status: 500 }));
    expect(await dialog.findByText('The score couldn’t be read.', undefined, { timeout: 5_000 })).toBeVisible();
    expect(dialog.getByRole('button', { name: /Try again/ })).toBeVisible();
    expect(dialog.queryByText('Reading the score…')).toBeNull();
  }, 15_000);
});
