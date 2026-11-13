// The Summary in every state the design lists, over a sidecar of the test's own (the garden crawl's analysis as
// today's engine answers it, and the states made from it). Each count is a link to exactly its part of What to fix.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { Analysis, CrawlListItem } from '../api/types';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { liveStore } from '../live/liveStore';
import { replaceSettings, updateSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import {
  GARDEN_ANALYSIS,
  NOTICES_ANALYSIS,
  PERFECT_ANALYSIS,
  SITE_WIDE_ANALYSIS,
  STOPPED_ANALYSIS,
  gardenItem,
  simpleRoutes,
} from '../test/simpleSidecar';

const CRAWL = `/crawls/${TEST_CRAWL_ID}`;
const FIX = `${CRAWL}/issues`;

async function open(
  analysis: Analysis | 'not-analyzed',
  item: CrawlListItem = gardenItem(),
  path = `${CRAWL}/overview`,
) {
  replaceSettings({ mode: 'simple', hiddenChecks: [] });
  setTransport(fakeSidecar(simpleRoutes(item, analysis)));
  const queryClient = createQueryClientWithoutBackoff();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return router;
}

const main = () => within(screen.getByRole('main'));
async function site() {
  const heading = await screen.findByRole(
    'heading',
    { level: 1, name: 'gardenfurnituresets.co.uk' },
    { timeout: 10_000 },
  );
  await waitFor(() => expect(screen.queryByLabelText('Loading the summary')).toBeNull());
  return heading;
}

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
  liveStore.reset();
});
afterEach(() => {
  uninstall();
  setTransport(fakeSidecar([]));
});

describe('the Summary of a finished check', () => {
  it('says the score, its word, its sentence and what it covers, as the design’s garden crawl', async () => {
    await open(GARDEN_ANALYSIS);
    await site();
    expect(main().getByRole('img', { name: 'Score 85 out of 100, Good' })).toBeVisible();
    expect(main().getByRole('heading', { level: 2, name: 'Good' })).toBeVisible();
    expect(
      main().getByText('Your site is in good shape. There are 2 serious problems and 4 problems worth fixing.'),
    ).toBeVisible();
    expect(
      main().getByText(/The score covers all 265 pages and 83 of 87 checks\. 4 checks couldn’t run\./),
    ).toBeVisible();
    expect(main().getByText(/Checked on 31 October 2026\. 265 pages read in 37 min/)).toBeVisible();
    expect(main().getByText(/Shouting Iguana read 265 pages and checked 4,521 addresses in all/)).toBeVisible();
  });

  it('lands each count on exactly its part of What to fix, and a count of 0 on nothing', async () => {
    await open(NOTICES_ANALYSIS);
    await site();
    const tally = within(main().getByRole('list', { name: 'What the checks found' }));
    expect(tally.getByRole('link', { name: '3 Things worth a look' })).toHaveAttribute('href', `${FIX}#worth-a-look`);
    expect(tally.getByRole('link', { name: /^80 Checks found nothing wrong$/ })).toHaveAttribute(
      'href',
      `${FIX}#nothing-wrong`,
    );
    expect(tally.getByRole('link', { name: '4 Checks couldn’t run' })).toHaveAttribute('href', `${FIX}#couldnt-check`);
    expect(tally.queryByRole('link', { name: /Serious problems/ })).toBeNull();
    expect(tally.getByText('Serious problems')).toBeVisible();
  });

  it('lists five to fix first in the engine’s order, each opening its problem, and what fixing them gives', async () => {
    await open(GARDEN_ANALYSIS);
    await site();
    const list = within(main().getByRole('region', { name: 'Fix these first' }));
    const rows = list.getAllByRole('link').filter((link) => link.getAttribute('href')?.includes('/issues/'));
    expect(rows.map((row) => row.getAttribute('href'))).toEqual(
      [
        'internal-client-error',
        'broken-internal-links',
        'image-too-large',
        'description-duplicate',
        'title-duplicate',
      ].map((id) => `${FIX}/${id}`),
    );
    expect(rows[0]).toHaveAccessibleName(/^1 Serious Pages or files that can't be found .* 9 of 4,521 addresses$/);
    expect(list.getByText(/Fixing these five takes the score from/)).toHaveTextContent(
      'Fixing these five takes the score from 85 to 98.',
    );
    expect(list.getByRole('link', { name: 'See all 6 problems and 13 things worth a look' })).toHaveAttribute(
      'href',
      FIX,
    );
  });

  it('says what’s fine and what couldn’t be checked, with the engine’s plain reasons', async () => {
    await open(GARDEN_ANALYSIS);
    await site();
    const fine = within(main().getByRole('region', { name: 'What’s fine' }));
    expect(fine.getByText('of 83 checks found nothing wrong')).toBeVisible();
    expect(fine.getByText(/Nothing wrong with .*visibility in search/)).toBeVisible();
    const skipped = within(main().getByRole('region', { name: 'Couldn’t be checked' }));
    expect(skipped.getByText(/No sitemap, the list of your pages for search engines, could be read/)).toBeVisible();
  });

  it('opens the score’s breakdown on score=1, and Esc closes it and takes it off the address', async () => {
    const router = await open(GARDEN_ANALYSIS);
    await site();
    fireEvent.click(main().getByRole('link', { name: 'How the score is worked out' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'How the score is worked out' }));
    expect(router.state.location.search).toEqual({ score: 1 });
    expect(
      dialog.getByRole('row', { name: /Pages or files that can't be found 9 of 4,521 addresses: a few −2/ }),
    ).toBeVisible();
    expect(dialog.getByRole('row', { name: '13 things worth a look They don’t change the score 0' })).toBeVisible();
    expect(dialog.getByRole('row', { name: 'Score 100 − 15 85' })).toBeVisible();
    expect(dialog.getByText('Score version 1')).toBeVisible();
    fireEvent.keyDown(document.activeElement ?? document.body, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(router.state.location.search).toEqual({});
  });

  it('leaves hidden checks out of the counts, and says nothing of five fixed that aren’t the engine’s five', async () => {
    replaceSettings({ mode: 'simple' });
    setTransport(fakeSidecar(simpleRoutes(gardenItem(), GARDEN_ANALYSIS)));
    updateSettings({ hiddenChecks: ['image-too-large'] });
    const queryClient = createQueryClientWithoutBackoff();
    const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [`${CRAWL}/overview`] }));
    render(<App router={router} queryClient={queryClient} />);
    await act(() => router.load());
    await site();
    expect(main().getByRole('link', { name: '3 Problems worth fixing' })).toBeVisible();
    expect(main().queryByText(/takes the score from/)).toBeNull();
  });
});

describe('the Summary in every other state', () => {
  it('a perfect site: 100, nothing to fix, and when to check again', async () => {
    await open(PERFECT_ANALYSIS);
    await site();
    expect(main().getByRole('img', { name: 'Score 100 out of 100, Very good' })).toBeVisible();
    expect(main().getByText('Nothing to fix. Every check that ran found nothing wrong.')).toBeVisible();
    expect(main().getByText(/Check the site again after you change it/)).toBeVisible();
  });

  it('nothing but things worth a look: nothing needs fixing, and they are listed', async () => {
    await open(NOTICES_ANALYSIS);
    await site();
    expect(main().getByText('Nothing needs fixing. 3 things are worth a look when you have a moment.')).toBeVisible();
  });

  it('stopped early: a banner with what it read and what was left, Continue checking, and a score so far', async () => {
    await open(STOPPED_ANALYSIS, gardenItem({ status: 'stopped' }));
    await site();
    expect(
      await main().findByText(
        'This check was stopped early. It read 265 pages and still had 1,380 addresses to check. Everything below covers only what it read.',
      ),
    ).toBeVisible();
    expect(main().getByRole('button', { name: /Continue checking/ })).toBeVisible();
    expect(main().getByRole('img', { name: 'Score 85 out of 100, Good so far' })).toBeVisible();
    expect(main().getByText(/On the pages read so far there are 2 serious problems/)).toBeVisible();
  });

  it('a serious fault of the whole site leads the sentence and the list', async () => {
    await open(SITE_WIDE_ANALYSIS);
    await site();
    expect(main().getByText(/That holds everything else back\. There are 2 serious problems\./)).toBeVisible();
    const first = within(main().getByRole('region', { name: 'Fix these first' })).getAllByRole('link')[0];
    expect(first).toHaveAttribute('href', `${FIX}/no-https`);
    expect(first).toHaveAccessibleName(/The whole site$/);
  });

  it('never analysed: no score yet, and Work out the results', async () => {
    await open('not-analyzed', gardenItem({ analyzedAt: null, issues: null, score: null }));
    await site();
    expect(await main().findByRole('heading', { name: 'No score yet' })).toBeVisible();
    expect(main().getByText('The results haven’t been worked out for this check.')).toBeVisible();
    expect(main().getByRole('button', { name: 'Work out the results' })).toBeVisible();
    expect(main().getByText(/Shouting Iguana read 265 pages/)).toBeVisible();
  });

  it('out of date: a banner with its reason and Update the results; the score stays', async () => {
    await open({ ...GARDEN_ANALYSIS, outOfDate: true, outOfDateReasons: ['engineChanged'] });
    await site();
    expect(main().getByText(/Shouting Iguana’s checks have been updated since these results\./)).toBeVisible();
    expect(main().getByRole('button', { name: 'Update the results' })).toBeVisible();
    expect(main().getByRole('img', { name: 'Score 85 out of 100, Good' })).toBeVisible();
  });

  it('nothing could be read: no score, and why', async () => {
    await open('not-analyzed', gardenItem({ pageCount: 0, analyzedAt: null, issues: null, score: null }));
    await site();
    expect(await main().findByRole('heading', { name: 'No pages could be read' })).toBeVisible();
    expect(main().queryByText(/Shouting Iguana read/)).toBeNull();
  });

  it('failed: says it couldn’t finish, with Try again, and shows what it read', async () => {
    await open(STOPPED_ANALYSIS, gardenItem({ status: 'failed' }));
    await site();
    expect(main().getByText(/The site check couldn’t finish\./)).toBeVisible();
    expect(main().getByRole('link', { name: 'Try again' })).toBeVisible();
    expect(main().getByRole('img', { name: /so far/ })).toBeVisible();
  });
});
