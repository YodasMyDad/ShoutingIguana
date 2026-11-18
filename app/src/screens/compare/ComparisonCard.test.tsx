import { QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import type { ChangesAnswer } from '../../api/fixLoop';
import { setTransport } from '../../api/transport';
import type { CrawlListItem } from '../../api/types';
import { liveStore } from '../../live/liveStore';
import { updateSettings } from '../../settings/settingsStore';
import { fakeSidecar, json, problem, type FakeSidecar } from '../../test/fakeSidecar';
import { changesRoute, comparedGardenItem, earlierGardenItem, gardenComparison } from '../../test/fixLoopSidecar';
import { createQueryClientWithoutBackoff } from '../../test/queryClient';
import { SIMPLE_CHECKS } from '../../test/simpleSidecar';
import { ComparisonCard } from './ComparisonCard';
import { useComparison } from './useComparison';

const GARDEN = comparedGardenItem();
const EARLIER = earlierGardenItem();

function Harness({ crawlId }: { crawlId: string }) {
  return <ComparisonCard crawlId={crawlId} comparison={useComparison(crawlId)} />;
}

function show({
  answer = gardenComparison(),
  item = GARDEN,
  library = [GARDEN, EARLIER],
}: { answer?: ChangesAnswer | Response; item?: CrawlListItem; library?: CrawlListItem[] } = {}): FakeSidecar {
  const sidecar = fakeSidecar([
    ['GET', /^\/api\/crawls$/, () => json(library)],
    ['GET', new RegExp(`^/api/crawls/${item.id}$`), () => json(item)],
    ['GET', /^\/api\/checks$/, () => json(SIMPLE_CHECKS)],
    ['POST', /\/analyze$/, () => new Response(null, { status: 202 })],
    answer instanceof Response ? ['GET', /\/changes/, () => answer] : changesRoute(answer),
  ]);
  setTransport(sidecar);
  render(
    <QueryClientProvider client={createQueryClientWithoutBackoff()}>
      <Harness crawlId={item.id} />
    </QueryClientProvider>,
  );
  return sidecar;
}

afterEach(() => {
  updateSettings({ compareWithByCrawl: {}, hiddenChecks: [] });
  liveStore.reset();
  setTransport(fakeSidecar([]));
});

describe('Overview’s comparison card (Advanced)', () => {
  it('heads with the crawl compared with, and gives the score, each severity, the checks and the issues as they moved', async () => {
    const comparison = gardenComparison();
    show({ answer: comparison });
    const card = await screen.findByRole('region', { name: /^Since 17 Oct, \d\d:\d\d$/ });
    expect(await within(card).findByText('(4,480 URLs, done)')).toBeVisible();
    expect(within(card).getByRole('button', { name: /^Compared with 17 Oct/ })).toBeVisible();
    const score = comparison.score!;
    expect(
      within(card).getByText(`Score: ${score.then.value} then, ${score.now.value} now, up ${score.change ?? 0}`),
    ).toBeInTheDocument();
    expect(within(card).getByText(`${score.then.value} → ${score.now.value}`)).toBeVisible();
    // The glyph is for the eye: a screen reader hears the words above.
    expect(
      within(card)
        .getByText(`▲${score.change ?? 0}`)
        .closest('[aria-hidden="true"]'),
    ).not.toBeNull();
    expect(within(card).getByText(/^Errors: \d+ then, \d+ now/)).toBeInTheDocument();
    expect(within(card).getByText(/^Checks:/).parentElement).toHaveTextContent(/^Checks: \d+ fixed · /);
    expect(within(card).getByText(/^Issues:/).parentElement).toHaveTextContent(/^Issues: \d+ fixed · /);
  });

  it('folds the differences, and opens them', async () => {
    show({
      answer: gardenComparison({
        comparable: 'partial',
        differences: [
          { code: 'stoppedEarly', side: 'now' },
          { code: 'limits', field: 'maxUrls', then: null, now: 5_000 },
        ],
      }),
    });
    const toggle = await screen.findByRole('button', { name: 'Differences (2)' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await userEvent.click(toggle);
    expect(screen.getByText('Stopped early: this crawl')).toBeVisible();
    expect(screen.getByText('Max URLs: none then, 5,000 now')).toBeVisible();
  });

  it('opens the differences of a counts-only comparison, and has no issues’ line', async () => {
    show({
      answer: gardenComparison({
        comparable: 'countsOnly',
        differences: [
          {
            code: 'startOrigin',
            then: 'http://www.gardenfurnituresets.co.uk/',
            now: 'https://www.gardenfurnituresets.co.uk/',
          },
        ],
      }),
    });
    expect(await screen.findByRole('button', { name: 'Differences (1)' })).toHaveAttribute('aria-expanded', 'true');
    expect(
      screen.getByText('Counts only: the start URLs’ scheme or host differ, so no URL can be matched.'),
    ).toBeVisible();
    expect(screen.queryByText(/^Issues:/)).toBeNull();
  });

  it('asks for the other crawl to be analysed again to compare like for like, counts only until then', async () => {
    const sidecar = show({
      answer: gardenComparison({ previousNeedsAnalysis: ['engineChanged'], comparable: 'countsOnly' }),
    });
    expect(
      await screen.findByText(
        'The 17 Oct crawl was analysed by an earlier version: analyse it again to compare like for like.',
      ),
    ).toBeVisible();
    await userEvent.click(screen.getByRole('button', { name: 'Analyse it again' }));
    expect(sidecar.requests.some((request) => request.path === `/api/crawls/${EARLIER.id}/analyze`)).toBe(true);
  });

  it('says a comparison that couldn’t be read, with the way back to the previous crawl', async () => {
    updateSettings({ compareWithByCrawl: { [GARDEN.id]: EARLIER.id } });
    show({
      answer: problem({
        type: 'crawl-not-comparable',
        title: 'That crawl can’t be compared with this one.',
        status: 409,
      }),
    });
    expect(
      await screen.findByText('That crawl can’t be compared with this one.', undefined, { timeout: 5_000 }),
    ).toBeVisible();
  });

  it('isn’t there for a site’s first crawl', async () => {
    show({ item: EARLIER, library: [EARLIER], answer: { crawlId: EARLIER.id, with: null } });
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(screen.queryByRole('region')).toBeNull();
  });
});
