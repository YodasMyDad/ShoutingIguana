import { useMatches, useParams } from '@tanstack/react-router';
import clsx from 'clsx';
import { useLayoutEffect, useRef, useState, type KeyboardEvent, type PointerEvent } from 'react';
import { Inspector } from '../inspector/Inspector';
import { useCommands } from '../keyboard/registry';
import { useInspector } from '../routing/inspector';
import { inspectedUrl } from '../routing/search';
import { INSPECTOR_HEIGHT_RANGE, INSPECTOR_WIDTH_RANGE } from '../settings/schema';
import { updateSettings, useSettings } from '../settings/settingsStore';

const KEY_STEP = 16;

/**
 * Where the URL inspector opens (docs/v2-ui-ia.md, The shell): on the right or docked at the bottom, resizable by
 * pointer or keyboard, closed with Esc. It opens for `?inspect=<url id>` on any view, so back and forward retrace
 * it (routing/inspector.ts has its place in history). What it shows is `inspector/Inspector.tsx`.
 */
export function InspectorSlot() {
  const inspector = useInspector();
  // The current view's own checked search: a view without `inspect` (Home, Overview, Settings) never opens it.
  const inspect = useMatches({ select: (matches) => inspectedUrl(matches[matches.length - 1]?.search) });
  const { crawl: crawlId } = useParams({ strict: false });
  const open = inspect !== undefined && crawlId !== undefined;

  // Esc and Close go back over the steps walked inside it, then close it in place: Alt+Left after Esc goes back to
  // where the user was before it opened, not to a URL that was open.
  const close = inspector.close;

  useCommands(
    () => [
      {
        id: 'inspector.close',
        title: 'Close the inspector',
        group: 'View',
        keys: ['escape'],
        inInputs: true,
        enabled: () => open,
        run: close,
      },
    ],
    [open, close],
    'app',
  );

  return open ? <InspectorPanel crawlId={crawlId} urlId={inspect} onClose={close} /> : null;
}

/**
 * The panel itself. Until the user resizes it, it takes the layout's size (tokens.css `--inspector-width` and
 * `--inspector-height`, which narrow with the window); once resized, the size they chose, remembered.
 */
function InspectorPanel({ crawlId, urlId, onClose }: { crawlId: string; urlId: number; onClose: () => void }) {
  const dock = useSettings((settings) => settings.inspectorDock);
  const chosen = useSettings((settings) => (dock === 'right' ? settings.inspectorWidth : settings.inspectorHeight));
  const range = dock === 'right' ? INSPECTOR_WIDTH_RANGE : INSPECTOR_HEIGHT_RANGE;
  const panelRef = useRef<HTMLElement>(null);
  const drag = useRef<{ start: number; size: number } | null>(null);
  const [measured, setMeasured] = useState<number>(range.min);

  useLayoutEffect(() => {
    const panel = panelRef.current;
    if (!panel) return undefined;
    const measure = () => setMeasured(dock === 'right' ? panel.offsetWidth : panel.offsetHeight);
    measure();
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(measure);
    observer?.observe(panel);
    return () => observer?.disconnect();
  }, [dock]);

  const size = Math.min(range.max, Math.max(range.min, chosen ?? measured));
  const resize = (next: number) =>
    updateSettings(dock === 'right' ? { inspectorWidth: next } : { inspectorHeight: next });

  const onPointerDown = (event: PointerEvent<HTMLDivElement>) => {
    event.currentTarget.setPointerCapture(event.pointerId);
    drag.current = { start: dock === 'right' ? event.clientX : event.clientY, size };
  };
  const onPointerMove = (event: PointerEvent<HTMLDivElement>) => {
    if (!drag.current) return;
    const moved = (dock === 'right' ? event.clientX : event.clientY) - drag.current.start;
    resize(drag.current.size - moved);
  };
  const onPointerUp = () => {
    drag.current = null;
  };
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const grow = dock === 'right' ? 'ArrowLeft' : 'ArrowUp';
    const shrink = dock === 'right' ? 'ArrowRight' : 'ArrowDown';
    if (event.key === grow) resize(size + KEY_STEP);
    else if (event.key === shrink) resize(size - KEY_STEP);
    else if (event.key === 'Home') resize(range.min);
    else if (event.key === 'End') resize(range.max);
    else return;
    event.preventDefault();
  };

  return (
    <aside
      ref={panelRef}
      aria-label="Inspector"
      className={clsx(
        // A sheet above the grid: the raised surface, the large radius and the second elevation (clarity's inspector).
        'relative flex min-h-0 min-w-0 shrink-0 flex-col rounded-xl border border-border bg-surface-raised shadow-2',
        // Docked at the bottom it never takes the whole height: the view above keeps some rows (a short window, 200%).
        dock === 'right' ? 'my-2 mr-2' : 'mx-2 mb-2 max-h-(--inspector-height-max)',
        chosen === null && (dock === 'right' ? 'w-(--inspector-width)' : 'h-(--inspector-height)'),
      )}
      style={chosen === null ? undefined : dock === 'right' ? { width: size } : { height: size }}
    >
      {/* A focusable separator is the APG window-splitter widget; jsx-a11y only knows the static separator. */}
      {/* eslint-disable-next-line jsx-a11y-x/no-noninteractive-element-interactions */}
      <div
        role="separator"
        // eslint-disable-next-line jsx-a11y-x/no-noninteractive-tabindex
        tabIndex={0}
        aria-label="Resize the inspector"
        aria-orientation={dock === 'right' ? 'vertical' : 'horizontal'}
        aria-valuenow={Math.round(size)}
        aria-valuemin={range.min}
        aria-valuemax={range.max}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onKeyDown={onKeyDown}
        className={clsx(
          'absolute z-1 hover:bg-accent-subtle',
          dock === 'right' ? 'inset-y-0 -left-1 w-2 cursor-col-resize' : 'inset-x-0 -top-1 h-2 cursor-row-resize',
        )}
      />
      <Inspector crawlId={crawlId} urlId={urlId} onClose={onClose} />
    </aside>
  );
}
