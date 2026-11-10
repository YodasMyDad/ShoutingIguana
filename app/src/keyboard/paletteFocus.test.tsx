// A command chosen in the palette runs once the palette has given focus back, however long its close takes, so the
// focus a command takes stays where the command put it. The palette gives focus back from a timer Radix starts once
// its content has gone; a command run before that (on the next frame, as it once was) had its focus taken back.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { FOCUS_RETURN_TIMEOUT_MS, resetOverlaysForTests } from '../app/overlays';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { resetRememberedSection } from '../inspector/sections';
import { TEST_CRAWL_ID, fakeSidecar, standardRoutes } from '../test/fakeSidecar';
import { inspectorRoutes, testUrlDetail } from '../test/inspectorSidecar';
import { lateZeroTimers } from '../test/lateTimers';
import { setPaletteMode } from './paletteMode';
import { installKeyboard } from './registry';

// CodeMirror needs a browser's layout: here the viewer is a stand-in, as in the inspector's own tests.
vi.mock('../inspector/SourceViewer', () => ({
  default: (props: { html: string }) => <div data-testid="source-viewer">{props.html}</div>,
}));

const CRAWL = `/crawls/${TEST_CRAWL_ID}`;
const PAGE = testUrlDetail();
/** Longer than the close takes by far, and than the frame a command once waited for. */
const SLOW_CLOSE_MS = 100;
const SEARCH = { name: 'Search the HTML source' };

let uninstall: () => void = () => undefined;

beforeEach(() => {
  uninstall = installKeyboard();
  resetRememberedSection();
});

afterEach(() => {
  uninstall();
  setPaletteMode({ kind: 'commands' });
  resetOverlaysForTests();
  setTransport(fakeSidecar([]));
});

/** The crawl's URLs with the inspector open, its source read once already, and focus on the inspector's Summary tab. */
async function openWithSourceRead() {
  setTransport(fakeSidecar([...inspectorRoutes({ urls: { 2: PAGE } }), ...standardRoutes()]));
  const history = createMemoryHistory({ initialEntries: [`${CRAWL}/urls?inspect=2`] });
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, history);
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  const user = userEvent.setup();
  const inspector = await screen.findByRole('complementary', { name: 'Inspector' });
  await within(inspector).findByRole('heading', { level: 2, name: PAGE.url });
  // The viewer's code and the page's HTML are there, so the section shows its search field the moment it's chosen.
  await user.click(within(inspector).getByRole('tab', { name: /^Source/ }));
  await within(inspector).findByRole('searchbox', SEARCH);
  const summary = within(inspector).getByRole('tab', { name: /^Summary/ });
  await user.click(summary);
  expect(summary).toHaveFocus();
  return { inspector, user };
}

/** Ctrl+K, and the text that leaves "Search the HTML source" highlighted. */
async function paletteOnSearchSource(user: ReturnType<typeof userEvent.setup>) {
  await user.keyboard('{Control>}k{/Control}');
  const palette = await screen.findByRole('dialog', { name: 'Command palette' });
  const input = within(palette).getByRole('combobox');
  await user.type(input, 'search the html');
  expect(within(palette).getByRole('option', { selected: true })).toHaveTextContent('Search the HTML source');
  return input;
}

const paletteGone = () => waitFor(() => expect(screen.queryByRole('dialog', { name: 'Command palette' })).toBeNull());

describe('a command chosen in the palette', () => {
  it('keeps the focus it takes, however long the palette takes to give focus back', async () => {
    const { inspector, user } = await openWithSourceRead();
    await paletteOnSearchSource(user);
    const late = lateZeroTimers(SLOW_CLOSE_MS);
    await user.keyboard('{Enter}');
    await paletteGone();
    // The palette has given focus back, late: the command ran after it, and its field has the keyboard.
    await late.over();
    await waitFor(() => expect(within(inspector).getByRole('searchbox', SEARCH)).toHaveFocus());
  });

  it('is never lost: it runs within the bound when the palette doesn’t say it closed, and keeps its focus', async () => {
    const { inspector, user } = await openWithSourceRead();
    const input = await paletteOnSearchSource(user);
    // The close is said long after the bound has passed.
    const late = lateZeroTimers(FOCUS_RETURN_TIMEOUT_MS * 2);
    fireEvent.keyDown(input, { key: 'Enter' });
    late.stop();
    expect(screen.queryByRole('dialog', { name: 'Command palette' })).toBeNull();
    expect(late.pending()).toBeGreaterThan(0);
    await waitFor(() => expect(within(inspector).getByRole('searchbox', SEARCH)).toHaveFocus());
    // The bound ran it, not the close, which is still to come…
    expect(late.pending()).toBeGreaterThan(0);
    await late.over();
    // …and which takes nothing back when it does.
    expect(within(inspector).getByRole('searchbox', SEARCH)).toHaveFocus();
  });
});
