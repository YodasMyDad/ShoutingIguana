import { afterEach, describe, expect, it } from 'vitest';
import { isModalOpen, resetOverlaysForTests, restoreFocus, setOverlay } from './overlays';

/** A view as the shell lays it out: a main area, with a grid in it or not, and a button elsewhere. */
function layout({ withGrid }: { withGrid: boolean }) {
  document.body.innerHTML = `
    <button id="trigger">Search</button>
    <main id="main" tabindex="-1">${withGrid ? '<div role="grid" tabindex="0" aria-label="URLs"></div>' : ''}</main>`;
  const byId = (id: string) => document.getElementById(id)!;
  return { trigger: byId('trigger'), main: byId('main'), grid: document.querySelector<HTMLElement>('[role="grid"]') };
}

afterEach(() => {
  resetOverlaysForTests();
  document.body.innerHTML = '';
});

describe('focus after an overlay', () => {
  it('goes back where it was when the overlay opened', () => {
    const { trigger } = layout({ withGrid: true });
    trigger.focus();
    setOverlay('palette');
    trigger.blur();
    setOverlay(null);
    restoreFocus();
    expect(document.activeElement).toBe(trigger);
  });

  it('goes to the view’s grid when what had it is gone', () => {
    const { trigger, grid } = layout({ withGrid: true });
    trigger.focus();
    setOverlay('shortcuts');
    trigger.remove();
    setOverlay(null);
    restoreFocus();
    expect(document.activeElement).toBe(grid);
  });

  it('goes to the main area when there’s no grid either', () => {
    const { main } = layout({ withGrid: false });
    // Opened with nothing focused: a keyboard shortcut on the page itself.
    setOverlay('palette');
    setOverlay(null);
    restoreFocus();
    expect(document.activeElement).toBe(main);
  });

  it('remembers where focus was when the first overlay opened, not when one replaced another', () => {
    const { trigger } = layout({ withGrid: true });
    trigger.focus();
    setOverlay('shortcuts');
    document.getElementById('main')!.focus();
    setOverlay('palette');
    setOverlay(null);
    restoreFocus();
    expect(document.activeElement).toBe(trigger);
  });
});

describe('modality', () => {
  it('lasts while an overlay is open', () => {
    expect(isModalOpen()).toBe(false);
    setOverlay('palette');
    expect(isModalOpen()).toBe(true);
    setOverlay(null);
    expect(isModalOpen()).toBe(false);
  });
});
