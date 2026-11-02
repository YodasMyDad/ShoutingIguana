import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { fakeSidecar, standardRoutes } from '../test/fakeSidecar';

// The top bar breaks: the rest of the shell, and the screen, must keep working.
vi.mock('./TopBar', () => ({
  TopBar: () => {
    throw new Error('The top bar broke');
  },
}));

describe('a part of the shell that fails', () => {
  it('says so in its own place, and the rest keeps working', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    setTransport(fakeSidecar(standardRoutes()));
    const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: ['/'] }));
    render(<App router={router} queryClient={createQueryClient()} />);
    await act(() => router.load());
    expect(await screen.findByText('The top bar couldn’t be shown.')).toBeVisible();
    expect(screen.getByRole('heading', { level: 1, name: 'Crawl a site' })).toBeVisible();
    expect(screen.getByRole('navigation', { name: 'Main' })).toBeVisible();
    expect(screen.queryByText('Shouting Iguana couldn’t show this window.')).toBeNull();
  });
});
