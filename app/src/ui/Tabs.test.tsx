import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
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

/**
 * jsdom lays nothing out: the strip's bar `bar` px wide, each tab `tab` px wide, the "N more" button `more` px wide,
 * no gaps. Read when the strip measures, so set before it renders or before a resize is heard.
 */
const room = { bar: 250, tab: 100, more: 60 };
function layOut() {
  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockImplementation(function (this: HTMLElement) {
    return this.classList.contains('si-tabs-bar') ? room.bar : 0;
  });
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    const width =
      this.getAttribute('role') === 'tab'
        ? room.tab
        : this.classList.contains('si-tabs-bar__more')
          ? room.more
          : this.classList.contains('si-tabs-bar')
            ? room.bar
            : 0;
    return DOMRect.fromRect({ width, height: 32 });
  });
}

/** The tabs the strip shows, and the ones it leaves to its menu. */
const shownTabs = () =>
  screen
    .getAllByRole('tab')
    .filter((tab) => !tab.hasAttribute('data-overflow'))
    .map((tab) => tab.textContent);
const tabsInMenu = () =>
  screen
    .getAllByRole('tab')
    .filter((tab) => tab.hasAttribute('data-overflow'))
    .map((tab) => tab.textContent);

/** A ResizeObserver whose callbacks the test calls: a resize heard. */
function hearResizes() {
  const observers: (() => void)[] = [];
  vi.stubGlobal(
    'ResizeObserver',
    class {
      // No entries: the strip measures for itself, and the menu's positioning reads none.
      constructor(private readonly callback: (entries: unknown[]) => void) {
        observers.push(() => this.callback([]));
      }
      observe = vi.fn();
      unobserve = vi.fn();
      disconnect = vi.fn();
    },
  );
  return () =>
    act(() => {
      for (const notify of observers) notify();
    });
}

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  Object.assign(room, { bar: 250, tab: 100, more: 60 });
});

// Polish 2 (gallery 27): the inspector's Inlinks tab was cut in half beside "7 more", whose menu listed it too.
describe('a strip of tabs wider than its room', () => {
  it('shows each tab whole or lists it in the menu, never both, never cut', async () => {
    layOut();
    render(<Tabs label="Sections" tabs={TABS} value="summary" />);
    // 250 px: 190 beside the 60 px button, so one tab of 100 px, and none half in view.
    expect(shownTabs()).toEqual(['summary']);
    expect(tabsInMenu()).toEqual(['inlinks', 'outlinks', 'hreflang']);
    const more = screen.getByRole('button', { name: '3 more: inlinks, outlinks, hreflang' });
    expect(more).toHaveTextContent('3 more');
    await userEvent.setup().click(more);
    expect(
      within(await screen.findByRole('menu'))
        .getAllByRole('menuitem')
        .map((item) => item.textContent),
    ).toEqual(tabsInMenu());
  });

  it('shows every tab and no button when they fit', () => {
    room.bar = 400;
    layOut();
    render(<Tabs label="Sections" tabs={TABS} value="summary" />);
    expect(shownTabs()).toEqual(['summary', 'inlinks', 'outlinks', 'hreflang']);
    expect(screen.queryByRole('button', { name: /more:/ })).toBeNull();
  });

  it('keeps the chosen tab in the strip, whichever it is: one chosen from the menu is shown and focused', async () => {
    const user = userEvent.setup();
    const chosen = vi.fn();
    room.bar = 360;
    layOut();
    const { rerender } = render(<Tabs label="Sections" tabs={TABS} value="summary" onValueChange={chosen} />);
    // 300 px beside the button: three tabs.
    expect(shownTabs()).toEqual(['summary', 'inlinks', 'outlinks']);
    await user.click(screen.getByRole('button', { name: '1 more: hreflang' }));
    await user.click(within(await screen.findByRole('menu')).getByRole('menuitem', { name: 'hreflang' }));
    expect(chosen).toHaveBeenCalledWith('hreflang');
    rerender(<Tabs label="Sections" tabs={TABS} value="hreflang" onValueChange={chosen} />);
    // In the place of the last that fitted, and the one it took the place of is in the menu.
    expect(shownTabs()).toEqual(['summary', 'inlinks', 'hreflang']);
    expect(tabsInMenu()).toEqual(['outlinks']);
    await waitFor(() => expect(screen.getByRole('tab', { name: 'hreflang' })).toHaveFocus());
    expect(screen.getByRole('button', { name: '1 more: outlinks' })).toBeInTheDocument();
  });

  it('measures again when the strip or a tab changes size, as counts arrive or the panel narrows', () => {
    const resize = hearResizes();
    room.bar = 400;
    layOut();
    render(<Tabs label="Sections" tabs={TABS} value="outlinks" />);
    expect(tabsInMenu()).toEqual([]);
    // The tabs grew (their counts came): two fit beside the button, the chosen one among them.
    room.tab = 150;
    resize();
    expect(shownTabs()).toEqual(['summary', 'outlinks']);
    expect(tabsInMenu()).toEqual(['inlinks', 'hreflang']);
  });

  it('chooses from the list on its own when nothing controls it, and keeps the list as it was opened', async () => {
    const user = userEvent.setup();
    const resize = hearResizes();
    layOut();
    render(<Tabs label="Sections" tabs={TABS} defaultValue="summary" />);
    await user.click(screen.getByRole('button', { name: /^3 more:/ }));
    // Tabs moving in or out of the strip while it's open (counts arriving) leave its items as they were opened.
    room.bar = 360;
    resize();
    expect(screen.getAllByRole('menuitem').map((item) => item.textContent)).toEqual([
      'inlinks',
      'outlinks',
      'hreflang',
    ]);
    await user.click(screen.getByRole('menuitem', { name: 'outlinks' }));
    expect(screen.getByRole('tab', { name: 'outlinks' })).toHaveAttribute('aria-selected', 'true');
    expect(shownTabs()).toContain('outlinks');
  });

  it('hides a tab in the menu from sight and from the keyboard, after the ones shown, in a strip that never scrolls', () => {
    const css = readFileSync(path.join(__dirname, '..', 'styles', 'components.css'), 'utf8');
    expect(css).toMatch(/\.si-tab\[data-overflow\] \{\s*order: 1;\s*visibility: hidden;/);
    const tabs = /\.si-tabs \{[^}]*\}/.exec(css)?.[0] ?? '';
    expect(tabs).toContain('overflow-x: clip;');
    expect(tabs).not.toContain('overflow-x: auto');
  });

  it('gives every other part that scrolls a thin bar without arrows', () => {
    const styles = (file: string) => readFileSync(path.join(__dirname, '..', 'styles', file), 'utf8');
    const base = styles('base.css');
    // Chromium (WebView2) by its scroll bar parts, which have no arrow buttons, outside forced colours only.
    expect(base).toMatch(
      /@media not \(forced-colors: active\) \{\s*::-webkit-scrollbar \{\s*width: var\(--scrollbar-size\);/,
    );
    expect(base).toMatch(/::-webkit-scrollbar-thumb \{[^}]*background: var\(--color-border-control\) padding-box;/);
    // A browser without them by the standard properties; never both, as either property makes Chromium ignore the parts.
    expect(base).toMatch(/@supports not selector\(::-webkit-scrollbar\) \{\s*\* \{\s*scrollbar-width: thin;/);
    expect(base).not.toMatch(/^\s{2}\* \{\s*scrollbar-width: thin;/m);
    // The windowed grid's own thumb too: inside the clear edge every thumb has, never the bar's full height.
    expect(base).toMatch(
      /\.si-grid--windowed::-webkit-scrollbar-thumb \{\s*background: var\(--color-border-control\) padding-box;/,
    );
  });
});
