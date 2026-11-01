import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { PaletteBody, type PaletteItem } from './CommandPalette';
import { searchPalette } from './paletteSearch';

const item = (id: string, group: string, title: string, keywords: string[] = []): PaletteItem => ({
  id,
  group,
  title,
  keywords,
  run: vi.fn(),
});

const entries = [
  item('home', 'Go to', 'Go to Home', ['library']),
  item('settings', 'Go to', 'Open settings', ['preferences']),
  item('new', 'Crawl', 'New crawl'),
  item('compact', 'View', 'Density: compact', ['rows']),
  item('dark', 'View', 'Theme: dark', ['appearance']),
  item('broken', 'Checks', 'Links to broken internal URLs', ['broken-internal-links']),
];
const ORDER = ['Go to', 'Crawl', 'View', 'App', 'Checks'];

describe('searchPalette', () => {
  it('lists everything in group order with no query', () => {
    expect(searchPalette(entries, '', ORDER).map((entry) => entry.id)).toEqual([
      'home',
      'settings',
      'new',
      'compact',
      'dark',
      'broken',
    ]);
  });

  it('needs every word, from the title or the keywords', () => {
    expect(searchPalette(entries, 'broken links', ORDER).map((entry) => entry.id)).toEqual(['broken']);
    expect(searchPalette(entries, 'preferences', ORDER).map((entry) => entry.id)).toEqual(['settings']);
    expect(searchPalette(entries, 'zzz', ORDER)).toEqual([]);
  });

  it('puts the best match first within a group', () => {
    const results = searchPalette(
      [item('a', 'View', 'Show the theme'), item('b', 'View', 'Theme: dark')],
      'theme',
      ORDER,
    );
    expect(results.map((entry) => entry.id)).toEqual(['b', 'a']);
  });
});

describe('the palette', () => {
  function setup() {
    const onChoose = vi.fn();
    render(<PaletteBody entries={entries} onChoose={onChoose} />);
    const input = screen.getByRole('combobox', { name: 'Search commands, checks and crawls' });
    const highlighted = () => document.getElementById(input.getAttribute('aria-activedescendant') ?? '');
    return { input, highlighted, onChoose };
  }

  it('always names the highlighted option, so a screen reader hears what Enter runs', () => {
    const { input, highlighted } = setup();
    expect(highlighted()).toHaveTextContent('Go to Home');
    expect(highlighted()).toHaveAttribute('aria-selected', 'true');
    fireEvent.change(input, { target: { value: 'dark' } });
    expect(highlighted()).toHaveTextContent('Theme: dark');
    fireEvent.change(input, { target: { value: 'nothing at all' } });
    expect(input).not.toHaveAttribute('aria-activedescendant');
    expect(screen.getByRole('status')).toHaveTextContent('Nothing matches that.');
  });

  it('moves with the arrows, wrapping at the ends', () => {
    const { input, highlighted } = setup();
    fireEvent.keyDown(input, { key: 'ArrowUp' });
    expect(highlighted()).toHaveTextContent('Links to broken internal URLs');
    fireEvent.keyDown(input, { key: 'ArrowDown' });
    fireEvent.keyDown(input, { key: 'ArrowDown' });
    expect(highlighted()).toHaveTextContent('Open settings');
  });

  it('runs the highlighted option on Enter, and an option on click', () => {
    const { input, onChoose } = setup();
    fireEvent.change(input, { target: { value: 'new' } });
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(onChoose).toHaveBeenCalledWith(expect.objectContaining({ id: 'new' }));
    fireEvent.change(input, { target: { value: '' } });
    fireEvent.click(screen.getByRole('option', { name: /Theme: dark/ }));
    expect(onChoose).toHaveBeenLastCalledWith(expect.objectContaining({ id: 'dark' }));
  });
});
