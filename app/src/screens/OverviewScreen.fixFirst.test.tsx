import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { liveStore } from '../live/liveStore';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import { overviewRoutes } from '../test/overviewSidecar';
import { FIX_FIRST_LIMIT } from './overview/answer';

const OVERVIEW = `/crawls/${TEST_CRAWL_ID}/overview`;

async function open() {
  setTransport(fakeSidecar(overviewRoutes()));
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [OVERVIEW] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  await waitFor(() => expect(document.querySelector('[aria-label="Loading the overview"]')).toBeNull());
  return router;
}

const main = () => within(document.getElementById('main') ?? document.body);

afterEach(() => {
  liveStore.reset();
  setTransport(fakeSidecar([]));
});

// The audit of 2026-11-19, H-18: no Fix first row showed one affected URL, so the first fixable fact was always a click
// away; the catalogue's description, cut with "…", sat where it could be; and eight rows left the card a third blank.
describe('Overview’s Fix first', () => {
  it('shows the first URL each check flags under its name, with what’s wrong with it', async () => {
    await open();
    const fix = within(await main().findByRole('region', { name: 'Fix first' }));
    const first = (await fix.findAllByRole('listitem'))[0]!;
    // The check's first row, as its list in Issues has it: the URL as a link, the fault beside it.
    const example = await within(first).findByRole('button', { name: 'https://example.com/old/' });
    expect(example).toHaveTextContent('/old/');
    expect(first).toHaveTextContent('404 Not Found');
    // And its own open-in-browser, directly after it.
    expect(within(first).getByRole('button', { name: /^Open .*old/ })).toBeInTheDocument();
    // Where it could show the URL, the catalogue's description is left to the check's own page.
    expect(first).not.toHaveTextContent('This internal URL');
  });

  it('opens the check with that URL inspected when the URL is clicked: the fact, in one click', async () => {
    const router = await open();
    const fix = within(await main().findByRole('region', { name: 'Fix first' }));
    const first = (await fix.findAllByRole('listitem'))[0]!;
    fireEvent.click(await within(first).findByRole('button', { name: 'https://example.com/old/' }));
    await waitFor(() =>
      expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/issues/internal-client-error`),
    );
    expect(router.state.location.search).toMatchObject({ inspect: 10 });
  });

  it('still opens the check from the row itself', async () => {
    await open();
    const fix = within(await main().findByRole('region', { name: 'Fix first' }));
    const row = await fix.findByRole('link', { name: /Error:\s?Internal URL returns 4xx/ });
    expect(new URL((row as HTMLAnchorElement).href).pathname).toBe(
      `/crawls/${TEST_CRAWL_ID}/issues/internal-client-error`,
    );
  });

  it('lists enough checks to use the card’s height', () => {
    expect(FIX_FIRST_LIMIT).toBe(12);
  });
});
