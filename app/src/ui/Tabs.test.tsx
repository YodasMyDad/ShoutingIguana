import { render, screen } from '@testing-library/react';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Tabs, type TabItem } from './Tabs';

type Section = 'summary' | 'inlinks' | 'outlinks' | 'hreflang';

const TABS: TabItem<Section>[] = (['summary', 'inlinks', 'outlinks', 'hreflang'] as const).map((value) => ({
  value,
  label: value,
  content: value,
}));

/** jsdom lays nothing out: each tab 100 px wide side by side, in a strip 150 px wide. */
function layOut(list: HTMLElement) {
  Object.defineProperty(list, 'clientWidth', { configurable: true, value: 150 });
  vi.spyOn(list, 'getBoundingClientRect').mockImplementation(() => DOMRect.fromRect({ x: 0, width: 150 }));
  screen.getAllByRole('tab').forEach((tab, index) => {
    vi.spyOn(tab, 'getBoundingClientRect').mockImplementation(() =>
      DOMRect.fromRect({ x: index * 100 - list.scrollLeft, width: 100 }),
    );
  });
}

afterEach(() => vi.restoreAllMocks());

describe('a strip of tabs wider than its room', () => {
  it('scrolls the chosen tab into view when it changes, so it’s never chosen out of sight', () => {
    const { rerender } = render(<Tabs label="Sections" tabs={TABS} value="summary" />);
    const list = screen.getByRole('tablist', { name: 'Sections' });
    layOut(list);
    rerender(<Tabs label="Sections" tabs={TABS} value="hreflang" />);
    // The fourth tab, 300 to 400 px in: the strip scrolls just enough to show all of it.
    expect(list.scrollLeft).toBe(250);
    rerender(<Tabs label="Sections" tabs={TABS} value="summary" />);
    expect(list.scrollLeft).toBe(0);
  });

  it('scrolls without a bar: the tab strip asks for none, where every other part has a thin one without arrows', () => {
    const styles = (file: string) => readFileSync(path.join(__dirname, '..', 'styles', file), 'utf8');
    const tabs = /\.si-tabs \{[^}]*\}/.exec(styles('components.css'))?.[0] ?? '';
    expect(tabs).toContain('scrollbar-width: none;');
    const base = styles('base.css');
    // Chromium (WebView2) by its scroll bar parts, which have no arrow buttons, outside forced colours only.
    expect(base).toMatch(
      /@media not \(forced-colors: active\) \{\s*::-webkit-scrollbar \{\s*width: var\(--scrollbar-size\);/,
    );
    expect(base).toMatch(/::-webkit-scrollbar-thumb \{[^}]*background: var\(--color-border-control\) padding-box;/);
    // A browser without them by the standard properties; never both, as either property makes Chromium ignore the parts.
    expect(base).toMatch(/@supports not selector\(::-webkit-scrollbar\) \{\s*\* \{\s*scrollbar-width: thin;/);
    expect(base).not.toMatch(/^\s{2}\* \{\s*scrollbar-width: thin;/m);
  });
});
