import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { fakeSidecar, standardRoutes } from '../test/fakeSidecar';

afterEach(() => setTransport(fakeSidecar([])));

describe('Settings, General', () => {
  it('reads what each density means on its segment, not only the chosen one’s', async () => {
    setTransport(fakeSidecar(standardRoutes()));
    const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: ['/settings'] }));
    render(<App router={router} queryClient={createQueryClient()} />);
    await act(() => router.load());
    const density = await screen.findByRole('radiogroup', { name: 'Density' });
    expect(within(density).getByRole('radio', { name: 'Compact' })).toHaveAccessibleDescription('Rows of 28 px');
    expect(within(density).getByRole('radio', { name: 'Standard' })).toHaveAccessibleDescription('Rows of 32 px');
    expect(within(density).getByRole('radio', { name: 'Comfortable' })).toHaveAccessibleDescription('Rows of 40 px');
  });
});
