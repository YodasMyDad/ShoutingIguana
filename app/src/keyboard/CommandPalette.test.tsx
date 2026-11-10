import { act, fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { PaletteBody, type PaletteItem } from './CommandPalette';
import { looksLikeUrl, searchPalette } from './paletteSearch';

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
    expect(screen.getByText('Nothing matches that.', { selector: 'p' })).toBeVisible();
  });

  it('changes aria-activedescendant whenever typing changes the highlighted option', () => {
    const { input } = setup();
    const before = input.getAttribute('aria-activedescendant');
    fireEvent.change(input, { target: { value: 'theme' } });
    const after = input.getAttribute('aria-activedescendant');
    // Chromium tells screen readers only when the attribute's value changes.
    expect(after).not.toBe(before);
    expect(document.getElementById(after ?? '')).toHaveTextContent('Theme: dark');
  });

  it('says how many results there are once typing pauses, in a live region that’s always there', () => {
    vi.useFakeTimers();
    try {
      const { input } = setup();
      const status = screen.getByRole('status');
      act(() => {
        vi.advanceTimersByTime(500);
      });
      expect(status).toHaveTextContent('6 results');
      fireEvent.change(input, { target: { value: 'g' } });
      fireEvent.change(input, { target: { value: 'go' } });
      act(() => {
        vi.advanceTimersByTime(100);
      });
      expect(status).toHaveTextContent('6 results');
      act(() => {
        vi.advanceTimersByTime(500);
      });
      expect(status).toHaveTextContent('1 result');
      fireEvent.change(input, { target: { value: 'nothing at all' } });
      act(() => {
        vi.advanceTimersByTime(500);
      });
      expect(screen.getByRole('status')).toBe(status);
      expect(status).toHaveTextContent('Nothing matches that.');
    } finally {
      vi.useRealTimers();
    }
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

describe('a URL typed in the palette', () => {
  it('tells a URL or a path from words', () => {
    for (const text of [
      'https://example.com/a?b=1',
      'HTTP://example.com',
      '/contact/',
      '/',
      '//example.com/a',
      'www.example.com/a',
      'example.com',
      'example.co.uk:8080/x',
    ]) {
      expect(looksLikeUrl(text), text).toBe(true);
    }
    for (const text of ['', '  ', 'broken links', 'settings', 'theme: dark', 'https://', '/ a', 'title.']) {
      expect(looksLikeUrl(text), text).toBe(false);
    }
  });

  it('offers to go to it: first for a path or a scheme, after the commands for a bare host', () => {
    const onChoose = vi.fn();
    const onUrl = vi.fn();
    render(<PaletteBody entries={entries} onChoose={onChoose} onUrl={onUrl} />);
    const input = screen.getByRole('combobox');
    fireEvent.change(input, { target: { value: '/contact/' } });
    const options = screen.getAllByRole('option');
    expect(options).toHaveLength(1);
    expect(options[0]).toHaveTextContent('Go to URL /contact/');
    fireEvent.keyDown(input, { key: 'Enter' });
    const chosen = onChoose.mock.calls[0]?.[0] as PaletteItem;
    expect(chosen.stay).toBe(true);
    void chosen.run();
    expect(onUrl).toHaveBeenCalledWith('/contact/');
  });

  it('doesn’t offer it for words, or where there’s no crawl to look in', () => {
    const { rerender } = render(<PaletteBody entries={entries} onChoose={vi.fn()} onUrl={vi.fn()} />);
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'settings' } });
    expect(screen.getAllByRole('option').map((option) => option.textContent)).toEqual(['Open settings, Go to']);
    rerender(<PaletteBody entries={entries} onChoose={vi.fn()} />);
    fireEvent.change(screen.getByRole('combobox'), { target: { value: '/contact/' } });
    expect(screen.queryByRole('option')).toBeNull();
  });
});
