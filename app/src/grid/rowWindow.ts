// The fixed-height virtualiser over the windowed scroll space: owns the scroll element's position and tells React
// which rows to render. React reads it through useSyncExternalStore, so a scroll that changes the range renders
// before the next paint and a scroll that doesn't change it renders nothing. Nothing is stored per row.
import { useLayoutEffect, useState, useSyncExternalStore, type RefObject } from 'react';
import { DEFAULT_OVERSCAN, computeRange, offsetToReveal, type RowRange } from './rowRange';
import {
  createScrollSpace,
  domHeight,
  isWindowed,
  placeOffset,
  rebaseForScroll,
  type ScrollSpace,
} from './scrollWindow';

export interface RowWindowLayout {
  count: number;
  rowHeight: number;
  /** The sticky header's height: it covers the top of the scroll viewport. */
  headerHeight: number;
  overscan?: number;
}

export interface RowWindowView extends RowRange {
  /** The virtual offset of the window's top: subtract it from a row's virtual top to place it. */
  base: number;
  /** Height of the rows' container. */
  bodyHeight: number;
  windowed: boolean;
  /** Visible height of the rows' area, below the header. */
  viewport: number;
}

export type ScrollAlign = 'auto' | 'start' | 'center' | 'end';

export class RowWindowModel {
  private element: HTMLElement | null = null;
  private space: ScrollSpace = createScrollSpace(0, 1);
  private layout: Required<RowWindowLayout> = { count: 0, rowHeight: 1, headerHeight: 0, overscan: DEFAULT_OVERSCAN };
  private view: RowWindowView = { ...computeRange(0, 0, 1, 0), base: 0, bodyHeight: 0, windowed: false, viewport: 0 };
  private readonly listeners = new Set<() => void>();
  private readonly offsetListeners = new Set<(offset: number) => void>();

  readonly subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  readonly getSnapshot = (): RowWindowView => this.view;

  /** Every scroll, with the virtual offset: for prefetching by speed and for the custom scrollbar. */
  onOffset(listener: (offset: number) => void): () => void {
    this.offsetListeners.add(listener);
    return () => this.offsetListeners.delete(listener);
  }

  configure(layout: RowWindowLayout): void {
    const previousRowHeight = this.layout.rowHeight;
    this.layout = { overscan: DEFAULT_OVERSCAN, ...layout };
    // A new density keeps the same row at the top, not the same pixel offset.
    const offset =
      previousRowHeight === layout.rowHeight ? this.offset() : (this.offset() / previousRowHeight) * layout.rowHeight;
    this.space = { ...this.space, total: layout.count * layout.rowHeight, rowHeight: layout.rowHeight };
    // The rows changed (a new sort, more rows): keep the same virtual offset, re-based if it has to be.
    if (this.element) {
      const placed = placeOffset(this.space, offset, this.viewportHeight());
      this.space.base = placed.base;
      if (Math.abs(this.element.scrollTop - placed.scrollTop) >= 1) this.element.scrollTop = placed.scrollTop;
    } else {
      this.space.base = 0;
    }
    this.update();
  }

  attach(element: HTMLElement): () => void {
    this.element = element;
    const onScroll = () => this.handleScroll();
    element.addEventListener('scroll', onScroll, { passive: true });
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(() => this.update());
    observer?.observe(element);
    this.update();
    return () => {
      element.removeEventListener('scroll', onScroll);
      observer?.disconnect();
      if (this.element === element) this.element = null;
    };
  }

  /** The virtual scroll offset now. */
  offset(): number {
    return this.space.base + (this.element?.scrollTop ?? 0);
  }

  maxOffset(): number {
    return Math.max(0, this.space.total - this.viewportHeight());
  }

  /** Scrolls so `index` is in view (`auto`: only if it isn't already). */
  scrollToIndex(index: number, align: ScrollAlign = 'auto'): void {
    const { rowHeight } = this.layout;
    this.scrollToOffset(offsetToReveal(index, this.offset(), this.viewportHeight(), rowHeight, align));
  }

  scrollToOffset(virtualOffset: number): void {
    const element = this.element;
    if (!element) return;
    const placed = placeOffset(this.space, virtualOffset, this.viewportHeight());
    const rebased = placed.base !== this.space.base;
    this.space.base = placed.base;
    element.scrollTop = placed.scrollTop;
    // A re-base can leave scrollTop where it was, and then no scroll event fires: update directly.
    if (rebased || Math.abs(element.scrollTop - placed.scrollTop) < 1) this.handleScroll();
  }

  private viewportHeight(): number {
    return Math.max(0, (this.element?.clientHeight ?? 0) - this.layout.headerHeight);
  }

  private handleScroll(): void {
    const element = this.element;
    if (!element) return;
    const nextBase = rebaseForScroll(this.space, element.scrollTop, this.viewportHeight());
    if (nextBase !== this.space.base) {
      const virtual = this.space.base + element.scrollTop;
      this.space.base = nextBase;
      element.scrollTop = virtual - nextBase;
    }
    this.update();
    const offset = this.offset();
    this.offsetListeners.forEach((listener) => listener(offset));
  }

  private update(): void {
    const viewport = this.viewportHeight();
    const { count, rowHeight, overscan } = this.layout;
    const range = computeRange(this.offset(), viewport, rowHeight, count, overscan);
    const next: RowWindowView = {
      ...range,
      base: this.space.base,
      bodyHeight: domHeight(this.space),
      windowed: isWindowed(this.space),
      viewport,
    };
    const current = this.view;
    if (
      current.first === next.first &&
      current.last === next.last &&
      current.firstVisible === next.firstVisible &&
      current.lastVisible === next.lastVisible &&
      current.base === next.base &&
      current.bodyHeight === next.bodyHeight &&
      current.windowed === next.windowed &&
      current.viewport === next.viewport
    ) {
      return;
    }
    this.view = next;
    this.listeners.forEach((listener) => listener());
  }
}

/** The virtualiser for a scroll element. Returns the rows to render and the model, for scrolling and the scrollbar. */
export function useRowWindow(
  scrollRef: RefObject<HTMLElement | null>,
  layout: RowWindowLayout,
): { view: RowWindowView; model: RowWindowModel } {
  const [model] = useState(() => new RowWindowModel());
  const { count, rowHeight, headerHeight, overscan = DEFAULT_OVERSCAN } = layout;

  useLayoutEffect(() => {
    const element = scrollRef.current;
    return element ? model.attach(element) : undefined;
  }, [model, scrollRef]);

  useLayoutEffect(() => {
    model.configure({ count, rowHeight, headerHeight, overscan });
  }, [model, count, rowHeight, headerHeight, overscan]);

  const view = useSyncExternalStore(model.subscribe, model.getSnapshot, model.getSnapshot);
  return { view, model };
}
