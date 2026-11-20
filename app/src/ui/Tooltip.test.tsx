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

  // The audit of 2026-11-19 (H-01): a sidebar item's tooltip stayed up after the click that chose it, over the next
  // item, and took that item's click.
  it('closes on a click, and focus given back to the trigger after it opens nothing', async () => {
    render(
      <Tooltip content="URLs">
        <button type="button">Go</button>
      </Tooltip>,
    );
    const button = screen.getByRole('button', { name: 'Go' });
    act(() => button.focus());
    expect(await screen.findByRole('tooltip')).toBeInTheDocument();
    fireEvent.pointerDown(button);
    fireEvent.pointerUp(button);
    fireEvent.click(button);
    await waitFor(() => expect(screen.queryByRole('tooltip')).toBeNull());
    // The app gives focus back to what was clicked (a route change, a menu closing): the pointer is still on it.
    fireEvent.focus(button);
    await act(() => new Promise((resolve) => setTimeout(resolve, 20)));
    expect(screen.queryByRole('tooltip')).toBeNull();
    // Once the pointer has left, the keyboard opens it again.
    fireEvent.pointerLeave(button);
    fireEvent.blur(button);
    fireEvent.focus(button);
    expect(await screen.findByRole('tooltip')).toBeInTheDocument();
  });

  it('never takes the pointer: what it covers stays clickable, and it closes when the pointer leaves its trigger', async () => {
    render(
      <Tooltip content="URLs">
        <button type="button">Go</button>
      </Tooltip>,
    );
    act(() => screen.getByRole('button', { name: 'Go' }).focus());
    const tooltip = await screen.findByRole('tooltip');
    expect(tooltip.closest('.si-tooltip')).toHaveAttribute('data-hoverable', 'false');
  });
});
