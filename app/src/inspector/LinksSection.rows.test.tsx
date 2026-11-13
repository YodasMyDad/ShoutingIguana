// The inspector's link lists keep the cursor on a link, not at a row number: a running crawl that finds another link
// to the URL, listed above the cursor's, leaves the cursor where it was.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { LinkRow } from '../api/inspector';
import { gridPagesOf } from '../api/queries';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { DEFAULT_SETTINGS } from '../settings/schema';
import { updateSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import { inspectorRoutes, testLinkRow, testUrlDetail } from '../test/inspectorSidecar';
import { issuesRoutes } from '../test/issuesSidecar';
import { resetLinkKinds, resetLinkRows } from './links';
import { resetRememberedSection } from './sections';

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
  resetRememberedSection();
  resetLinkKinds();
  resetLinkRows();
  updateSettings({ ...DEFAULT_SETTINGS });
});
afterEach(() => {
  uninstall();
  setTransport(fakeSidecar([]));
});

const cursorRowText = (grid: HTMLElement) =>
  document.getElementById(grid.getAttribute('aria-activedescendant') ?? '')?.closest('[role="row"]')?.textContent ??
  null;

describe('a URL’s inlinks under a running crawl', () => {
  it('keep the cursor on its link when another arrives above it', async () => {
    const inlinks: Record<number, LinkRow[]> = {
      2: [
        testLinkRow({ id: 1, url: 'https://example.com/', text: 'Patio set' }),
        testLinkRow({ id: 3, url: 'https://example.com/b', text: 'Garden sets' }),
      ],
    };
    setTransport(
      fakeSidecar([
        ...inspectorRoutes({
          urls: { 1: testUrlDetail({ id: 1, url: 'https://example.com/' }), 2: testUrlDetail() },
          inlinks,
        }),
        ...issuesRoutes(),
      ]),
    );
    const queryClient = createQueryClient();
    const router = createAppRouter(
      queryClient,
      createMemoryHistory({ initialEntries: [`/crawls/${TEST_CRAWL_ID}/urls?inspect=2`] }),
    );
    render(<App router={router} queryClient={queryClient} />);
    await act(() => router.load());
    const user = userEvent.setup();
    const inspector = await screen.findByRole('complementary', { name: 'Inspector' });
    await user.click(await within(inspector).findByRole('tab', { name: /^Inlinks/ }));
    const grid = await within(inspector).findByRole('grid', { name: 'Inlinks' });
    await within(grid).findByText('Garden sets');
    act(() => grid.focus());
    await user.keyboard('{ArrowDown}');
    expect(cursorRowText(grid)).toMatch(/Garden sets/);
    inlinks[2] = [testLinkRow({ id: 4, url: 'https://example.com/c', text: 'New sofa' }), ...inlinks[2]!];
    await act(() => queryClient.invalidateQueries(gridPagesOf(TEST_CRAWL_ID)));
    await within(grid).findByText('New sofa');
    await waitFor(() => expect(cursorRowText(grid)).toMatch(/Garden sets/));
  });
});
