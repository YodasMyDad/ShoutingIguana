import { act, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CLOCK_TICK_MS } from '../lib/clock';
import { formatExactTime } from '../lib/format';
import { RelativeTime } from './RelativeTime';

const START = Date.parse('2026-11-04T10:00:00Z');
let windowHidden = false;

function setHidden(value: boolean) {
  windowHidden = value;
  document.dispatchEvent(new Event('visibilitychange'));
}

afterEach(() => {
  setHidden(false);
  vi.useRealTimers();
});

describe('RelativeTime', () => {
  it('reads the exact time after the relative one, which the keyboard can’t reach in the tooltip', () => {
    const iso = '2026-11-04T09:57:00Z';
    const { container } = render(<RelativeTime iso={iso} now={START} />);
    const time = container.querySelector('time');
    expect(time).toHaveAttribute('dateTime', iso);
    expect(time).toHaveTextContent(`3 minutes ago, ${formatExactTime(iso)}`);
    expect(screen.getByText(`, ${formatExactTime(iso)}`)).toHaveClass('sr-only');
  });

  it('moves on as time passes, a minute at a time, and not while the window is hidden', () => {
    vi.useFakeTimers({ now: START });
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => windowHidden });
    render(<RelativeTime iso="2026-11-04T09:59:50Z" />);
    const shown = () => document.querySelector('time')?.firstChild?.textContent;
    expect(shown()).toBe('just now');
    act(() => {
      vi.advanceTimersByTime(2 * CLOCK_TICK_MS);
    });
    expect(shown()).toBe('2 minutes ago');

    // In the tray: no ticks, so nothing renders; shown again, it's right at once.
    act(() => setHidden(true));
    act(() => {
      vi.advanceTimersByTime(10 * CLOCK_TICK_MS);
    });
    expect(shown()).toBe('2 minutes ago');
    expect(vi.getTimerCount()).toBe(0);
    act(() => setHidden(false));
    expect(shown()).toBe('12 minutes ago');
  });
});
