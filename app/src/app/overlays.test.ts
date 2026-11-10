import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  FOCUS_RETURN_TIMEOUT_MS,
  afterFocusReturns,
  isModalOpen,
  resetOverlaysForTests,
  restoreFocus,
  setOverlay,
} from './overlays';

/** A view as the shell lays it out: a main area, with a grid in it or not, and a button elsewhere. */
function layout({ withGrid }: { withGrid: boolean }) {
  document.body.innerHTML = `
    <button id="trigger">Search</button>
    <main id="main" tabindex="-1">${withGrid ? '<div role="grid" tabindex="0" aria-label="URLs"></div>' : ''}</main>`;
  const byId = (id: string) => document.getElementById(id)!;
  return { trigger: byId('trigger'), main: byId('main'), grid: document.querySelector<HTMLElement>('[role="grid"]') };
}

afterEach(() => {
  vi.useRealTimers();
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

  it('is given back once: an overlay’s close said twice leaves focus where it has gone since', () => {
    const { trigger, main } = layout({ withGrid: true });
    trigger.focus();
    setOverlay('palette');
    setOverlay(null);
    restoreFocus();
    main.focus();
    restoreFocus();
    expect(document.activeElement).toBe(main);
  });

  it('waits for the last overlay: one that closes under another leaves focus alone, and its place is kept', () => {
    const { trigger, main } = layout({ withGrid: true });
    trigger.focus();
    setOverlay('shortcuts');
    // The palette opens over the sheet, which closes: Radix says so while the palette is open.
    setOverlay('palette');
    main.focus();
    restoreFocus();
    expect(document.activeElement).toBe(main);
    setOverlay(null);
    restoreFocus();
    expect(document.activeElement).toBe(trigger);
  });
});

describe('what an overlay closes to do', () => {
  /** A command that takes focus, as "Search the HTML source" does: it says where focus was when it ran. */
  function command(target: HTMLElement) {
    const ran = vi.fn(() => {
      const before = document.activeElement;
      target.focus();
      return before;
    });
    return ran;
  }

  it('runs once focus has been given back, in the same turn, and keeps the focus it takes', () => {
    const { trigger, main } = layout({ withGrid: true });
    trigger.focus();
    setOverlay('palette');
    trigger.blur();
    setOverlay(null);
    const ran = command(main);
    afterFocusReturns(ran);
    // The palette hasn't given focus back yet: Radix says when, a moment after the close.
    expect(ran).not.toHaveBeenCalled();
    restoreFocus();
    expect(ran).toHaveBeenCalledTimes(1);
    expect(ran).toHaveReturnedWith(trigger);
    expect(document.activeElement).toBe(main);
  });

  it('runs at once when no overlay is closing', () => {
    const { main } = layout({ withGrid: true });
    const ran = command(main);
    afterFocusReturns(ran);
    expect(ran).toHaveBeenCalledTimes(1);

    // Focus already given back: nothing to wait for.
    setOverlay('palette');
    setOverlay(null);
    restoreFocus();
    afterFocusReturns(ran);
    expect(ran).toHaveBeenCalledTimes(2);
  });

  it('never waits longer than the bound: focus is given back then, and a close said late doesn’t take it', () => {
    vi.useFakeTimers();
    const { trigger, main } = layout({ withGrid: true });
    trigger.focus();
    setOverlay('palette');
    trigger.blur();
    setOverlay(null);
    const ran = command(main);
    afterFocusReturns(ran);
    vi.advanceTimersByTime(FOCUS_RETURN_TIMEOUT_MS - 1);
    expect(ran).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    // Given back to where it was first, then the command's.
    expect(ran).toHaveBeenCalledTimes(1);
    expect(ran).toHaveReturnedWith(trigger);
    expect(document.activeElement).toBe(main);
    // Radix says the overlay closed after all: the command keeps the focus, and runs no second time.
    restoreFocus();
    expect(document.activeElement).toBe(main);
    expect(ran).toHaveBeenCalledTimes(1);
  });

  it('runs once, and the wait’s timer goes with it', () => {
    vi.useFakeTimers();
    const { trigger, main } = layout({ withGrid: true });
    trigger.focus();
    setOverlay('palette');
    setOverlay(null);
    const ran = command(main);
    afterFocusReturns(ran);
    restoreFocus();
    trigger.focus();
    vi.advanceTimersByTime(FOCUS_RETURN_TIMEOUT_MS * 2);
    expect(ran).toHaveBeenCalledTimes(1);
    expect(document.activeElement).toBe(trigger);
    expect(vi.getTimerCount()).toBe(0);
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
