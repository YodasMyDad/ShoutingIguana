import { act, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { resetOverlaysForTests, setOverlay } from '../app/overlays';
import { ShortcutList, ShortcutSheet } from './ShortcutSheet';

afterEach(() => resetOverlaysForTests());

describe('the shortcut sheet', () => {
  it('lets a keyboard user scroll its list: a named region that takes focus', () => {
    act(() => setOverlay('shortcuts'));
    render(<ShortcutSheet />);
    const region = screen.getByRole('region', { name: 'Shortcuts' });
    expect(region).toHaveAttribute('tabindex', '0');
  });

  it('is a plain list in Settings, where the page scrolls', () => {
    render(<ShortcutList />);
    expect(screen.queryByRole('region', { name: 'Shortcuts' })).toBeNull();
  });
});
