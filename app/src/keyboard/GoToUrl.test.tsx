// The palette's "Go to URL…" in the app: the lookup, what it says when the crawl doesn't have the URL, and the
// inspector opening on what it finds.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { resetOverlaysForTests } from '../app/overlays';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { TEST_CRAWL_ID, fakeSidecar, standardRoutes, type FakeSidecar } from '../test/fakeSidecar';
import { inspectorRoutes, testUrlDetail } from '../test/inspectorSidecar';
import { setPaletteMode } from './paletteMode';
import { installKeyboard } from './registry';

const CRAWL = `/crawls/${TEST_CRAWL_ID}`;
const B = testUrlDetail({ id: 3, url: 'https://example.com/b' });

let uninstall: () => void = () => undefined;
let sidecar: FakeSidecar;

beforeEach(() => {
  uninstall = installKeyboard();
});

afterEach(() => {
  uninstall();
  setPaletteMode({ kind: 'commands' });
  resetOverlaysForTests();
  setTransport(fakeSidecar([]));
});

async function open(path: string) {
  sidecar = fakeSidecar([
    ...inspectorRoutes({
      urls: { 2: testUrlDetail(), 3: B },
      lookup: {
        '/b': { id: 3, url: B.url, requested: null },
        'http://example.com/b': { id: 3, url: B.url, requested: 'http://example.com/b' },
      },
    }),
    ...standardRoutes(),
  ]);
  setTransport(sidecar);
  const history = createMemoryHistory({ initialEntries: [path] });
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, history);
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  const user = userEvent.setup();
  return { router, history, user };
}

const inspected = (router: { state: { location: { search: unknown } } }) =>
  (router.state.location.search as { inspect?: number }).inspect;

/** Ctrl+K, then "Go to URL…" from the list: the palette stays open, its field now the URL's. */
async function goToUrlMode(user: ReturnType<typeof userEvent.setup>) {
  await user.keyboard('{Control>}k{/Control}');
  const palette = await screen.findByRole('dialog', { name: 'Command palette' });
  await user.type(within(palette).getByRole('combobox'), 'go to url');
  await user.keyboard('{Enter}');
  return { palette, field: await within(palette).findByRole('textbox', { name: 'Go to URL' }) };
}

describe('“Go to URL…” in the palette', () => {
  it('looks a path up in the open crawl and opens the inspector on it, a step back retraces', async () => {
    const { router, history, user } = await open(`${CRAWL}/urls?inspect=2`);
    await screen.findByRole('grid', { name: 'URLs' });
    const before = history.length;
    const { field } = await goToUrlMode(user);
    expect(field).toHaveFocus();
    await user.type(field, '/b{Enter}');
    await waitFor(() => expect(inspected(router)).toBe(3));
    expect(screen.queryByRole('dialog', { name: 'Command palette' })).toBeNull();
    expect(history.length).toBe(before + 1);
    expect(sidecar.requests.find((request) => request.path.endsWith('/urls/lookup'))?.body).toEqual({ url: '/b' });
    expect(await screen.findByRole('heading', { level: 2, name: B.url })).toBeVisible();
  });

  it('says under the field when the crawl doesn’t have the URL, or the text isn’t one, and stays open', async () => {
    const { router, user } = await open(`${CRAWL}/urls`);
    await screen.findByRole('grid', { name: 'URLs' });
    const { palette, field } = await goToUrlMode(user);
    await user.type(field, '/nowhere/{Enter}');
    expect(await within(palette).findByRole('alert')).toHaveTextContent('This crawl doesn’t have that URL.');
    expect(field).toHaveAttribute('aria-invalid', 'true');
    // Typing again withdraws what was said about the old text.
    await user.type(field, 'x');
    expect(within(palette).queryByRole('alert')).toBeNull();
    await user.clear(field);
    await user.type(field, 'not a url{Enter}');
    expect(await within(palette).findByRole('alert')).toHaveTextContent('That isn’t a URL.');
    expect(inspected(router)).toBeUndefined();
    expect(screen.getByRole('dialog', { name: 'Command palette' })).toBeVisible();
  });

  it('opens the crawl’s URLs with the inspector from a view that has none, and says which version it found', async () => {
    const { router, user } = await open(`${CRAWL}/overview`);
    const { field } = await goToUrlMode(user);
    await user.type(field, 'http://example.com/b{Enter}');
    await waitFor(() => expect(router.state.location.pathname).toBe(`${CRAWL}/urls`));
    expect(inspected(router)).toBe(3);
    expect(await screen.findByText('Opened another version of that URL')).toBeVisible();
    expect(screen.getByText(`The crawl doesn’t have http://example.com/b. It has ${B.url}.`)).toBeVisible();
  });

  it('takes a URL typed among the commands, and goes back to them with Backspace', async () => {
    const { router, user } = await open(`${CRAWL}/urls`);
    await screen.findByRole('grid', { name: 'URLs' });
    await user.keyboard('{Control>}k{/Control}');
    const palette = await screen.findByRole('dialog', { name: 'Command palette' });
    await user.type(within(palette).getByRole('combobox'), '/b');
    expect(within(palette).getAllByRole('option')[0]).toHaveTextContent('Go to URL /b');
    await user.keyboard('{Enter}');
    await waitFor(() => expect(inspected(router)).toBe(3));

    await user.keyboard('{Control>}k{/Control}');
    const again = await screen.findByRole('dialog', { name: 'Command palette' });
    await user.type(within(again).getByRole('combobox'), 'go to url{Enter}');
    const field = await within(again).findByRole('textbox', { name: 'Go to URL' });
    await user.keyboard('{Backspace}');
    expect(await within(again).findByRole('combobox')).toHaveFocus();
    expect(field).not.toBeInTheDocument();
  });

  it('isn’t offered where no crawl is open', async () => {
    const { user } = await open('/');
    await user.keyboard('{Control>}k{/Control}');
    const palette = await screen.findByRole('dialog', { name: 'Command palette' });
    await user.type(within(palette).getByRole('combobox'), 'go to url');
    expect(within(palette).queryByRole('option', { name: /Go to URL/ })).toBeNull();
  });
});
