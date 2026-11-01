// Which rows to render, two ways behind one interface, so their memory can be compared (?virt=own).
// - TanStack Virtual: allocates per-row storage for the whole count (a Float64Array of 2 x count, and an array of
//   count items that fills in as rows are visited) and needs two private members patched for the windowed space.
// - Our own fixed-height window: with every row 28 px, the range is one division and nothing is stored per row.
import { useCallback, useLayoutEffect, useMemo, useRef, useState, type RefObject } from 'react';
import { flushSync } from 'react-dom';
import { useVirtualizer, type Virtualizer } from '@tanstack/react-virtual';
import { HEADER_HEIGHT, ROW_HEIGHT } from '../perf/probe';
import type { ScrollWindow } from './scrollWindow';

export type RowItem = { index: number; key: number | string | bigint; start: number };
export type RowWindow = { items: RowItem[]; scrollToIndex: (index: number) => void };
export type RowWindowArgs = {
  count: number;
  overscan: number;
  scrollRef: RefObject<HTMLDivElement | null>;
  scrollWindow: ScrollWindow;
  /** Called with the virtual scroll offset on every scroll. */
  onOffset: (offset: number) => void;
};

export function useTanStackRows({ count, overscan, scrollRef, scrollWindow, onOffset }: RowWindowArgs): RowWindow {
  const virtualizer = useVirtualizer({
    count,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => ROW_HEIGHT,
    overscan,
    observeElementOffset: scrollWindow.observeElementOffset,
    scrollToFn: scrollWindow.scrollToFn,
    onChange: (instance) => onOffset(instance.scrollOffset ?? 0),
  });

  // TanStack Virtual reads the DOM scroll position and extent directly for its clamping and scroll reconciliation,
  // and has no option for a virtual scroll space, so two private readers are replaced (spike only: not a stable API).
  const patchedRef = useRef(false);
  if (!patchedRef.current) {
    const internals = virtualizer as unknown as {
      _elementMaxScrollOffset: () => number | null;
      _readElementOffset: () => number | null;
    };
    internals._elementMaxScrollOffset = () => {
      const element = scrollRef.current;
      return element ? Math.max(0, HEADER_HEIGHT + scrollWindow.total - element.clientHeight) : null;
    };
    internals._readElementOffset = () => {
      const element = scrollRef.current;
      return element ? scrollWindow.base + element.scrollTop : null;
    };
    patchedRef.current = true;
  }

  const scrollToIndex = useCallback((index: number) => virtualizer.scrollToIndex(index, { align: 'start' }), [virtualizer]);
  return { items: virtualizer.getVirtualItems(), scrollToIndex };
}

export function useFixedRows({ count, overscan, scrollRef, scrollWindow, onOffset }: RowWindowArgs): RowWindow {
  const [range, setRange] = useState({ first: 0, last: -1 });
  const countRef = useRef(count);
  countRef.current = count;
  const onOffsetRef = useRef(onOffset);
  onOffsetRef.current = onOffset;

  useLayoutEffect(() => {
    const element = scrollRef.current;
    if (!element) return;
    let offset = 0;
    let synchronous = false; // flushSync only from scroll events, never inside this effect
    const update = () => {
      const first = Math.max(0, Math.floor(offset / ROW_HEIGHT) - overscan);
      const last = Math.min(countRef.current - 1, Math.floor((offset + element.clientHeight) / ROW_HEIGHT) + overscan);
      const apply = () => setRange((current) => (current.first === first && current.last === last ? current : { first, last }));
      if (synchronous) flushSync(apply);
      else apply();
    };
    const instance = { scrollElement: element } as unknown as Virtualizer<HTMLDivElement, Element>;
    const stop = scrollWindow.observeElementOffset(instance, (next) => {
      offset = next;
      onOffsetRef.current(next);
      update();
    });
    synchronous = true;
    const observer = new ResizeObserver(() => update());
    observer.observe(element);
    return () => {
      stop();
      observer.disconnect();
    };
  }, [scrollRef, scrollWindow, overscan, count]);

  const items = useMemo(() => {
    const list: RowItem[] = [];
    for (let index = range.first; index <= Math.min(range.last, count - 1); index++) list.push({ index, key: index, start: index * ROW_HEIGHT });
    return list;
  }, [range, count]);

  const scrollToIndex = useCallback(
    (index: number) => {
      const element = scrollRef.current;
      if (!element) return;
      const max = Math.max(0, HEADER_HEIGHT + count * ROW_HEIGHT - element.clientHeight);
      const instance = { scrollElement: element } as unknown as Virtualizer<HTMLDivElement, Element>;
      scrollWindow.scrollToFn(Math.min(max, index * ROW_HEIGHT), {}, instance);
    },
    [count, scrollRef, scrollWindow],
  );
  return { items, scrollToIndex };
}
