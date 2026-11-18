// Step 3b, round 2: an image a problem is about is named by its file in Simple's "Where it is", its folder after it.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, cleanup, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { liveStore } from '../live/liveStore';
import { replaceSettings } from '../settings/settingsStore';
import { evidenceRoutes } from '../test/evidenceSidecar';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { TABLE_STUBBED } from './tableStubbed';

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
  liveStore.reset();
});
afterEach(() => {
  cleanup();
  uninstall();
  setTransport(fakeSidecar([]));
});

describe.skipIf(TABLE_STUBBED)('an image a problem is about, in Simple', () => {
  it('is named by its file, its folder after it, and read in full', async () => {
    replaceSettings({ mode: 'simple', hiddenChecks: [] });
    setTransport(fakeSidecar(evidenceRoutes()));
    const queryClient = createQueryClientWithoutBackoff();
    const router = createAppRouter(
      queryClient,
      createMemoryHistory({ initialEntries: [`/crawls/${TEST_CRAWL_ID}/issues/image-too-large`] }),
    );
    render(<App router={router} queryClient={queryClient} />);
    await act(() => router.load());
    const where = within(await screen.findByRole('region', { name: 'Where it is' }, { timeout: 10_000 }));
    const name = (await where.findAllByText('hero.jpg'))[0];
    expect(name).toHaveClass('si-file-name');
    expect(name?.parentElement).toHaveTextContent('hero.jpg/img/');
    expect(where.getAllByText(/\/img\/hero\.jpg$/).length).toBeGreaterThan(0);
  });
});
