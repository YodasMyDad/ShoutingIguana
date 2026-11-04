import { screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type * as Connection from './shell/connection';

vi.mock('./shell/connection', async (importOriginal) => ({
  ...(await importOriginal<typeof Connection>()),
  initConnection: () => Promise.reject(new Error('The shell didn’t answer (IPC)')),
}));

afterEach(() => {
  document.body.innerHTML = '';
});

describe('start-up', () => {
  /** The front-end review's case (L15): if the shell's IPC rejects, start() rejected and the window stayed blank. */
  it('shows a plain failure with diagnostics to copy when it can’t start, never a blank window', async () => {
    document.body.innerHTML = '<div id="root"></div>';
    await import('./main');
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Shouting Iguana couldn’t start');
    expect(alert).toHaveTextContent('The shell didn’t answer (IPC)');
    expect(screen.getByRole('button', { name: 'Copy diagnostics' })).toBeInTheDocument();
  });
});
