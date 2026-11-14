import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { fakeSidecar, json, standardRoutes, testCrawlItem } from '../test/fakeSidecar';

const ID = '0123456789abcdef';

afterEach(() => setTransport(fakeSidecar([])));

describe('the window’s title (round 5: D’s titles with A’s host-and-port names)', () => {
  it('names a site on a port of its own by its host and port, as Your sites does', async () => {
    const item = testCrawlItem({ id: ID, site: '127.0.0.1', startUrl: 'http://127.0.0.1:8080/' });
    setTransport(fakeSidecar([['GET', new RegExp(`^/api/crawls/${ID}$`), () => json(item)], ...standardRoutes()]));
    const router = createAppRouter(
      createQueryClient(),
      createMemoryHistory({ initialEntries: [`/crawls/${ID}/urls`] }),
    );
    render(<App router={router} queryClient={createQueryClient()} />);
    await act(() => router.load());
    await waitFor(() => expect(document.title).toBe('URLs · 127.0.0.1:8080 · Shouting Iguana'), { timeout: 5_000 });
  });
});
