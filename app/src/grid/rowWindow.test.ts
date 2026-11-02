import { describe, expect, it } from 'vitest';
import { RowWindowModel } from './rowWindow';
import { WINDOW_PX } from './scrollWindow';

const ROW = 32;
const HEADER = 32;
const VIEWPORT = 640;

/** A scroll element as a browser would have it: a height, and a scrollTop that sticks (jsdom does no layout). */
function scroller(height = VIEWPORT + HEADER) {
  const element = document.createElement('div');
  let top = 0;
  Object.defineProperty(element, 'clientHeight', { configurable: true, get: () => height });
  Object.defineProperty(element, 'scrollTop', {
    configurable: true,
    get: () => top,
    set: (value: number) => {
      top = Math.max(0, value);
    },
  });
  const scrollTo = (value: number) => {
    top = value;
    element.dispatchEvent(new Event('scroll'));
  };
  return { element, scrollTo };
}

describe('RowWindowModel', () => {
  it('renders the first screenful with overscan below', () => {
    const model = new RowWindowModel();
    const { element } = scroller();
    model.configure({ count: 1_000_000, rowHeight: ROW, headerHeight: HEADER, overscan: 6 });
    model.attach(element);
    expect(model.getSnapshot()).toMatchObject({
      first: 0,
      firstVisible: 0,
      lastVisible: 19,
      last: 25,
      windowed: true,
      base: 0,
    });
    expect(model.getSnapshot().bodyHeight).toBe(WINDOW_PX);
  });

  it('re-bases near the bottom of the window without moving what is on screen', () => {
    const model = new RowWindowModel();
    const { element, scrollTo } = scroller();
    model.configure({ count: 1_000_000, rowHeight: ROW, headerHeight: HEADER });
    model.attach(element);
    const target = WINDOW_PX - 100_000;
    scrollTo(target);
    const view = model.getSnapshot();
    expect(view.base).toBeGreaterThan(0);
    expect(view.base + element.scrollTop).toBe(target);
    expect(view.firstVisible).toBe(Math.floor(target / ROW));
  });

  it('jumps to the last row of a million, inside the window', () => {
    const model = new RowWindowModel();
    const { element } = scroller();
    model.configure({ count: 1_000_000, rowHeight: ROW, headerHeight: HEADER });
    model.attach(element);
    model.scrollToIndex(999_999);
    const view = model.getSnapshot();
    expect(view.lastVisible).toBe(999_999);
    expect(element.scrollTop).toBeLessThanOrEqual(WINDOW_PX - VIEWPORT);
    expect(view.base % ROW).toBe(0);
  });

  it('only notifies React when the rows to render change', () => {
    const model = new RowWindowModel();
    const { element, scrollTo } = scroller();
    model.configure({ count: 1_000, rowHeight: ROW, headerHeight: HEADER, overscan: 6 });
    model.attach(element);
    let renders = 0;
    model.subscribe(() => renders++);
    scrollTo(10); // row 20 comes partly into view
    expect(renders).toBe(1);
    scrollTo(20); // the same rows: nothing to render
    expect(renders).toBe(1);
    scrollTo(ROW * 3);
    expect(renders).toBe(2);
  });

  it('reports every scroll offset, for prefetching', () => {
    const model = new RowWindowModel();
    const { element, scrollTo } = scroller();
    model.configure({ count: 1_000, rowHeight: ROW, headerHeight: HEADER });
    model.attach(element);
    const offsets: number[] = [];
    model.onOffset((offset) => offsets.push(offset));
    scrollTo(100);
    scrollTo(200);
    expect(offsets).toEqual([100, 200]);
  });

  it('keeps the top row when the row height changes with the density', () => {
    const model = new RowWindowModel();
    const { element, scrollTo } = scroller();
    model.configure({ count: 10_000, rowHeight: 32, headerHeight: HEADER });
    model.attach(element);
    scrollTo(500 * 32);
    model.configure({ count: 10_000, rowHeight: 40, headerHeight: HEADER });
    expect(model.getSnapshot().firstVisible).toBe(500);
  });

  it('tells offset listeners about a jump once, not again when its scroll event arrives', () => {
    const model = new RowWindowModel();
    const { element } = scroller();
    model.configure({ count: 1_000_000, rowHeight: ROW, headerHeight: HEADER });
    model.attach(element);
    const offsets: number[] = [];
    model.onOffset((offset) => offsets.push(offset));
    model.scrollToOffset(64_000);
    // The browser's scroll event for the same move follows a frame later.
    element.dispatchEvent(new Event('scroll'));
    expect(offsets).toEqual([64_000]);
  });

  it('reaches the last row exactly at compact density, in one jump', () => {
    const model = new RowWindowModel();
    const { element } = scroller();
    model.configure({ count: 1_000_000, rowHeight: 28, headerHeight: HEADER });
    model.attach(element);
    model.scrollToIndex(999_999);
    expect(model.offset()).toBe(1_000_000 * 28 - VIEWPORT);
    expect(element.scrollTop).toBeLessThanOrEqual(model.getSnapshot().bodyHeight - VIEWPORT);
    expect(model.getSnapshot().lastVisible).toBe(999_999);
  });

  it('says when the scrollbar’s thumb is held and let go', () => {
    const model = new RowWindowModel();
    const heard: boolean[] = [];
    model.onScrubbing((scrubbing) => heard.push(scrubbing));
    model.setScrubbing(true);
    model.setScrubbing(true);
    expect(model.isScrubbing()).toBe(true);
    model.setScrubbing(false);
    expect(heard).toEqual([true, false]);
  });
});
