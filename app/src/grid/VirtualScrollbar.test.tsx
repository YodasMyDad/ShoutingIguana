import { act, fireEvent, render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { RowWindowModel } from './rowWindow';
import { VirtualScrollbar } from './VirtualScrollbar';

const ROW = 32;
const HEADER = 32;
const VIEWPORT = 640;

/** A model over a scroll element as a browser would have it: a height, and a scrollTop that sticks. */
function windowedModel(count = 1_000_000) {
  const element = document.createElement('div');
  let top = 0;
  Object.defineProperty(element, 'clientHeight', { configurable: true, get: () => VIEWPORT + HEADER });
  Object.defineProperty(element, 'scrollTop', {
    configurable: true,
    get: () => top,
    set: (value: number) => (top = Math.max(0, value)),
  });
  const model = new RowWindowModel();
  model.configure({ count, rowHeight: ROW, headerHeight: HEADER });
  model.attach(element);
  return model;
}

/** The scrollbar, with its track this tall: it stops short of the rows' area by the horizontal scrollbar's corner. */
function renderScrollbar(model: RowWindowModel, trackHeight: number) {
  Object.defineProperty(HTMLElement.prototype, 'clientHeight', { configurable: true, get: () => trackHeight });
  try {
    const view = render(<VirtualScrollbar model={model} total={1_000_000 * ROW} viewport={VIEWPORT} top={HEADER} />);
    const track = view.container.firstElementChild as HTMLElement;
    const thumb = track.firstElementChild as HTMLElement;
    return { track, thumb };
  } finally {
    Reflect.deleteProperty(HTMLElement.prototype, 'clientHeight');
  }
}

const thumbTop = (thumb: HTMLElement) => Number(/translateY\(([-\d.]+)px\)/.exec(thumb.style.transform)?.[1] ?? 0);

describe('the windowed grid’s scrollbar', () => {
  it('scrolls the rows when the wheel turns over it', () => {
    const model = windowedModel();
    const { track } = renderScrollbar(model, VIEWPORT - 10);
    act(() => {
      fireEvent.wheel(track, { deltaY: 120, deltaMode: 0 });
    });
    expect(model.offset()).toBe(120);
    act(() => {
      fireEvent.wheel(track, { deltaY: 3, deltaMode: 1 }); // three lines
    });
    expect(model.offset()).toBe(240);
  });

  it('keeps the thumb inside its track at the very end', () => {
    const model = windowedModel();
    const trackHeight = VIEWPORT - 10;
    const { thumb } = renderScrollbar(model, trackHeight);
    act(() => model.scrollToOffset(Number.MAX_SAFE_INTEGER));
    expect(thumbTop(thumb) + thumb.offsetHeight).toBeLessThanOrEqual(trackHeight);
    expect(thumbTop(thumb) + Number.parseFloat(thumb.style.height)).toBeCloseTo(trackHeight, 5);
  });

  it('marks the rows as scrubbed while its thumb is held', () => {
    const model = windowedModel();
    const { thumb } = renderScrollbar(model, VIEWPORT - 10);
    Object.assign(thumb, {
      setPointerCapture: () => undefined,
      hasPointerCapture: () => true,
      releasePointerCapture: () => undefined,
    });
    fireEvent.pointerDown(thumb, { pointerId: 1, clientY: 10 });
    expect(model.isScrubbing()).toBe(true);
    fireEvent.pointerUp(thumb, { pointerId: 1, clientY: 20 });
    expect(model.isScrubbing()).toBe(false);
  });
});
