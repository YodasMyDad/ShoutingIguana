import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { diagnostics } from './diagnostics';
import { StartupFailure } from './StartupFailure';

describe('the start-up failure', () => {
  it('copies diagnostics that carry the error and where it happened', async () => {
    const user = userEvent.setup();
    const writeText = vi.spyOn(navigator.clipboard, 'writeText');
    render(<StartupFailure error={new TypeError('invoke is not a function')} />);
    await user.click(screen.getByRole('button', { name: 'Copy diagnostics' }));
    expect(await screen.findByText('Copied')).toBeInTheDocument();
    const copied = writeText.mock.calls[0]?.[0] ?? '';
    expect(copied).toContain('Error: TypeError: invoke is not a function');
    expect(copied).toContain(`Page: ${window.location.href}`);
  });

  it('reads any thrown value', async () => {
    await expect(diagnostics('IPC refused')).resolves.toContain('Error: IPC refused');
    await expect(diagnostics(undefined)).resolves.toContain('Error: undefined');
  });
});
