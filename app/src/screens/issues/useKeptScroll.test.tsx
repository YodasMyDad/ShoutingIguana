import { act, fireEvent, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { checkPlace, forgetCheckPlaces } from './issuesPlace';
import { useKeptScroll } from './useKeptScroll';

/** A grid's frame with its scroller, as tall as jsdom is told. */
function Frame({ grid, check = 'title-too-long' }: { grid: string; check?: string }) {
  const keep = useKeptScroll('crawl', check, grid);
  return (
    <div ref={keep}>
      <div className="si-grid" data-testid="scroller" key={grid} />
    </div>
  );
}

/** jsdom lays nothing out: the scroller is 300 px showing 2,000. */
function sized(scroller: HTMLElement): HTMLElement {
  Object.defineProperty(scroller, 'scrollHeight', { configurable: true, value: 2000 });
  Object.defineProperty(scroller, 'clientHeight', { configurable: true, value: 300 });
  return scroller;
}

describe('a check’s rows keep how far they were scrolled', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['requestAnimationFrame', 'cancelAnimationFrame', 'performance'] });
    forgetCheckPlaces();
  });
  afterEach(() => vi.useRealTimers());

  it('keeps the offset as the reader scrolls and puts it back when the check is opened again', () => {
    const first = render(<Frame grid="rows|url" />);
    const scroller = sized(first.getByTestId('scroller'));
    scroller.scrollTop = 640;
    fireEvent.scroll(scroller);
    expect(checkPlace('crawl', 'title-too-long').scroll).toEqual({ grid: 'rows|url', offset: 640 });
    first.unmount();

    // Back from Links: the check is drawn again, its rows not there yet, then there.
    const again = render(<Frame grid="rows|url" />);
    const back = again.getByTestId('scroller');
    expect(back.scrollTop).toBe(0);
    sized(back);
    act(() => {
      vi.advanceTimersToNextFrame();
    });
    expect(back.scrollTop).toBe(640);
    // The grid's own first scroll (to its cursor's row) doesn't take it: the offset wins while the rows settle.
    back.scrollTop = 96;
    fireEvent.scroll(back);
    act(() => {
      vi.advanceTimersToNextFrame();
    });
    expect(back.scrollTop).toBe(640);
    expect(checkPlace('crawl', 'title-too-long').scroll?.offset).toBe(640);
    // The reader's own scroll does, at once.
    fireEvent.wheel(back);
    back.scrollTop = 128;
    fireEvent.scroll(back);
    act(() => {
      vi.advanceTimersToNextFrame();
    });
    expect(back.scrollTop).toBe(128);
    expect(checkPlace('crawl', 'title-too-long').scroll?.offset).toBe(128);
  });

  it('starts another order of the rows, and another check, at the top', () => {
    const first = render(<Frame grid="rows|url" />);
    const scroller = sized(first.getByTestId('scroller'));
    scroller.scrollTop = 640;
    fireEvent.scroll(scroller);
    first.unmount();

    const sorted = render(<Frame grid="rows|title" />);
    const other = sized(sorted.getByTestId('scroller'));
    act(() => {
      vi.advanceTimersToNextFrame();
    });
    expect(other.scrollTop).toBe(0);
    sorted.unmount();

    const another = render(<Frame grid="rows|url" check="missing-h1" />);
    const third = sized(another.getByTestId('scroller'));
    act(() => {
      vi.advanceTimersToNextFrame();
    });
    expect(third.scrollTop).toBe(0);
  });
});
