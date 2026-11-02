import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { fakeSidecar, standardRoutes } from '../test/fakeSidecar';

// The frame itself breaks (the shell's own hooks), which no part's boundary can catch.
vi.mock('../keyboard/useAppCommands', () => ({
  useAppCommands: () => {
    throw new Error('The frame broke');
  },
  GRID_KEY_COMMANDS: [],
}));

describe('the frame around every view, when it fails', () => {
  it('offers to load the window again, rather than a view’s retry that can’t help', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    setTransport(fakeSidecar(standardRoutes()));
    const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: ['/'] }));
    render(<App router={router} queryClient={createQueryClient()} />);
    await act(() => router.load());
    expect(await screen.findByText('Shouting Iguana couldn’t show this window.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Reload the window' })).toBeVisible();
  });
});
