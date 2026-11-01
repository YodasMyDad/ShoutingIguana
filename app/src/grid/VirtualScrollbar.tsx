import { useLayoutEffect, useRef, type PointerEvent } from 'react';
import type { RowWindowModel } from './rowWindow';

/** tokens.css `--scrollbar-thumb-min`: the thumb never gets smaller than this, however long the list. */
const THUMB_MIN_PX = 24;

/**
 * The vertical scrollbar of a windowed grid. The native thumb only shows the position inside the current window, so
 * this one shows the position in the whole list. Dragging the thumb scrubs, clicking the track pages. Hidden from
 * assistive technology: keyboard users move with the grid's own keys, and the row count is on the grid.
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

  const trackHeight = Math.max(0, viewport);
  const thumbHeight =
    total > 0 ? Math.max(THUMB_MIN_PX, Math.min(trackHeight, (viewport / total) * trackHeight)) : trackHeight;
  const travel = Math.max(1, trackHeight - thumbHeight);

  // The thumb follows every scroll without re-rendering React.
  useLayoutEffect(() => {
    const place = (offset: number) => {
      const max = model.maxOffset();
      const position = max > 0 ? (offset / max) * travel : 0;
      if (thumbRef.current) thumbRef.current.style.transform = `translateY(${position}px)`;
    };
    place(model.offset());
    return model.onOffset(place);
  }, [model, travel]);

  const onThumbDown = (event: PointerEvent<HTMLDivElement>) => {
    event.preventDefault();
    event.stopPropagation();
    event.currentTarget.setPointerCapture(event.pointerId);
    drag.current = { startY: event.clientY, startOffset: model.offset() };
  };

  const onThumbMove = (event: PointerEvent<HTMLDivElement>) => {
    const state = drag.current;
    if (!state) return;
    const moved = event.clientY - state.startY;
    model.scrollToOffset(state.startOffset + (moved / travel) * model.maxOffset());
  };

  const onThumbUp = (event: PointerEvent<HTMLDivElement>) => {
    drag.current = null;
    event.currentTarget.releasePointerCapture(event.pointerId);
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
        className="si-scroll-thumb absolute inset-x-0.5 rounded-full bg-border-strong hover:bg-text-muted"
        style={{ height: thumbHeight }}
      />
    </div>
  );
}
