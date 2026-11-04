import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { isKeyTaken } from '../keyboard/layers';
import { Tooltip } from './Tooltip';

/** Whether the window, where the registry listens, sees Esc as taken. */
function escapeAtTheWindow(target: Element): boolean {
  const seen: boolean[] = [];
  const listener = (event: KeyboardEvent) => seen.push(isKeyTaken(event));
  window.addEventListener('keydown', listener);
  fireEvent.keyDown(target, { key: 'Escape' });
  window.removeEventListener('keydown', listener);
  const [taken] = seen;
  if (taken === undefined) throw new Error('Esc never reached the window');
  return taken;
}

afterEach(() => {
  document.getElementById('another-layer')?.remove();
});

describe('Tooltip', () => {
  it('closes on Esc and gives the key back to the app under it', async () => {
    render(
      <Tooltip content="Dock at the bottom">
        <button type="button">Dock</button>
      </Tooltip>,
    );
    const button = screen.getByRole('button', { name: 'Dock' });
    act(() => button.focus());
    expect(await screen.findByRole('tooltip')).toBeInTheDocument();
    expect(escapeAtTheWindow(button)).toBe(false);
    await waitFor(() => expect(screen.queryByRole('tooltip')).toBeNull());
  });

  it('keeps Esc over a menu, a popover or a dialog, which close one Esc at a time', async () => {
    const layer = document.createElement('div');
    layer.id = 'another-layer';
    layer.setAttribute('role', 'dialog');
    document.body.append(layer);
    render(
      <Tooltip content="Dock at the bottom">
        <button type="button">Dock</button>
      </Tooltip>,
    );
    const button = screen.getByRole('button', { name: 'Dock' });
    act(() => button.focus());
    expect(await screen.findByRole('tooltip')).toBeInTheDocument();
    expect(escapeAtTheWindow(button)).toBe(true);
    await waitFor(() => expect(screen.queryByRole('tooltip')).toBeNull());
  });
});
