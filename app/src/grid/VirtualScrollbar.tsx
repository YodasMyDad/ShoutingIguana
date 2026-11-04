import { useEffect, useLayoutEffect, useRef, useState, type PointerEvent } from 'react';
import type { RowWindowModel } from './rowWindow';

/** tokens.css `--scrollbar-thumb-min`: the thumb never gets smaller than this, however long the list. */
const THUMB_MIN_PX = 24;
/** A wheel's line and page steps, in px, for wheels that don't scroll by pixels. */
const WHEEL_LINE_PX = 40;

/**
 * The vertical scrollbar of a windowed grid. The native thumb only shows the position inside the current window, so
 * this one shows the position in the whole list. Dragging the thumb scrubs (the data layer waits for it to settle
 * before fetching), clicking the track pages, and the wheel scrolls over it as over the rows. Hidden from assistive
 * technology: keyboard users move with the grid's own keys, and the row count is on the grid.
 */
export function VirtualScrollbar({
  model,
  total,
  viewport,
  top,
}: {
  model: RowWindowModel;
  /** Virtual height of all rows. */
  total: number;
  /** Visible height of the rows' area. */
  viewport: number;
  /** px from the frame's top: the header's height. */
  top: number;
}) {
  const trackRef = useRef<HTMLDivElement>(null);
  const thumbRef = useRef<HTMLDivElement>(null);
  const drag = useRef<{ startY: number; startOffset: number } | null>(null);
  // The track's own height: it stops above the horizontal scrollbar's corner, which the rows' area doesn't.
  const [trackHeight, setTrackHeight] = useState(Math.max(0, viewport));

  useLayoutEffect(() => {
    const track = trackRef.current;
    if (!track) return undefined;
    const measure = () => setTrackHeight(track.clientHeight);
    measure();
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(measure);
    observer?.observe(track);
    return () => observer?.disconnect();
  }, []);

  const thumbHeight =
    total > 0 ? Math.max(THUMB_MIN_PX, Math.min(trackHeight, (viewport / total) * trackHeight)) : trackHeight;
  const travel = Math.max(1, trackHeight - thumbHeight);

  // The thumb follows every scroll without re-rendering React.
  useLayoutEffect(() => {
    const place = (offset: number) => {
      const max = model.maxOffset();
      const position = max > 0 ? (Math.min(offset, max) / max) * travel : 0;
      if (thumbRef.current) thumbRef.current.style.transform = `translateY(${position}px)`;
    };
    place(model.offset());
    return model.onOffset(place);
  }, [model, travel]);

  // The wheel over the scrollbar scrolls the rows, as it does over them. Not passive, so the page behind never
  // scrolls instead.
  useEffect(() => {
    const track = trackRef.current;
    if (!track) return undefined;
    const onWheel = (event: WheelEvent) => {
      event.preventDefault();
      const step =
        event.deltaMode === WheelEvent.DOM_DELTA_LINE
          ? WHEEL_LINE_PX
          : event.deltaMode === WheelEvent.DOM_DELTA_PAGE
            ? viewport
            : 1;
      model.scrollToOffset(model.offset() + event.deltaY * step);
    };
    track.addEventListener('wheel', onWheel, { passive: false });
    return () => track.removeEventListener('wheel', onWheel);
  }, [model, viewport]);

  // Unmounted mid-drag (the rows changed meaning): the thumb is no longer held.
  useEffect(() => () => model.setScrubbing(false), [model]);

  const onThumbDown = (event: PointerEvent<HTMLDivElement>) => {
    event.preventDefault();
    event.stopPropagation();
    event.currentTarget.setPointerCapture(event.pointerId);
    drag.current = { startY: event.clientY, startOffset: model.offset() };
    model.setScrubbing(true);
  };

  const onThumbMove = (event: PointerEvent<HTMLDivElement>) => {
    const state = drag.current;
    if (!state) return;
    const moved = event.clientY - state.startY;
    model.scrollToOffset(state.startOffset + (moved / travel) * model.maxOffset());
  };

  const onThumbUp = (event: PointerEvent<HTMLDivElement>) => {
    drag.current = null;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
    model.setScrubbing(false);
  };

  const onTrackDown = (event: PointerEvent<HTMLDivElement>) => {
    const thumb = thumbRef.current?.getBoundingClientRect();
    if (!thumb) return;
    event.preventDefault();
    const direction = event.clientY < thumb.top ? -1 : 1;
    model.scrollToOffset(model.offset() + direction * viewport);
  };

  return (
    <div
      ref={trackRef}
      aria-hidden="true"
      onPointerDown={onTrackDown}
      className="absolute right-0 bottom-(--scrollbar-size) z-2 w-(--scrollbar-size)"
      style={{ top }}
    >
      <div
        ref={thumbRef}
        onPointerDown={onThumbDown}
        onPointerMove={onThumbMove}
        onPointerUp={onThumbUp}
        onPointerCancel={onThumbUp}
        onLostPointerCapture={() => {
          drag.current = null;
          model.setScrubbing(false);
        }}
        className="si-scroll-thumb absolute inset-x-0.5 rounded-full"
        style={{ height: thumbHeight }}
      />
    </div>
  );
}
