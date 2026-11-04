import { useMatches, useNavigate } from '@tanstack/react-router';
import clsx from 'clsx';
import { PanelBottom, PanelRight, X } from 'lucide-react';
import { useLayoutEffect, useRef, useState, type KeyboardEvent, type PointerEvent } from 'react';
import { useCommands } from '../keyboard/registry';
import { inspectedUrl } from '../routing/search';
import { INSPECTOR_HEIGHT_RANGE, INSPECTOR_WIDTH_RANGE } from '../settings/schema';
import { updateSettings, useSettings } from '../settings/settingsStore';
import { IconButton } from '../ui/IconButton';

const KEY_STEP = 16;

/**
 * Where the URL inspector opens (docs/v2-ui-ia.md, The shell): on the right or docked at the bottom, resizable by
 * pointer or keyboard, closed with Esc. It opens for `?inspect=<url id>` on any view, so back and forward retrace
 * it. The inspector's content is its own slice (it reads `POST /urls/inspect`).
 */
export function InspectorSlot() {
  const navigate = useNavigate();
  // The current view's own checked search: a view without `inspect` (Home, Overview, Settings) never opens it.
  const inspect = useMatches({ select: (matches) => inspectedUrl(matches[matches.length - 1]?.search) });
  const open = inspect !== undefined;

  // The inspector opens and closes in place (a replace), as it follows the grid's cursor: back and forward step
  // between views, so Alt+Left after Esc goes back to where the user was, not to the URL that was open.
  const close = () =>
    void navigate({
      to: '.',
      search: (previous: Record<string, unknown>) => ({ ...previous, inspect: undefined }),
      replace: true,
    });

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
    [open],
    'app',
  );

  return open ? <InspectorPanel urlId={inspect} onClose={close} /> : null;
}

/**
 * The panel itself. Until the user resizes it, it takes the layout's size (tokens.css `--inspector-width` and
 * `--inspector-height`, which narrow with the window); once resized, the size they chose, remembered.
 */
function InspectorPanel({ urlId, onClose }: { urlId: number; onClose: () => void }) {
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
        'relative flex shrink-0 flex-col rounded-xl border border-border bg-surface-raised shadow-2',
        dock === 'right' ? 'my-2 mr-2' : 'mx-2 mb-2',
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
      <div className="flex h-(--tab-height) items-center gap-2 border-b border-border-subtle pr-2 pl-5">
        <h2 className="text-13 font-semibold">Inspector</h2>
        <span className="figures text-12 text-text-muted">URL {urlId}</span>
        <div className="ml-auto flex gap-0.5">
          <IconButton
            label={dock === 'right' ? 'Dock at the bottom' : 'Dock on the right'}
            icon={dock === 'right' ? PanelBottom : PanelRight}
            size="sm"
            onClick={() => updateSettings({ inspectorDock: dock === 'right' ? 'bottom' : 'right' })}
          />
          <IconButton
            label="Close the inspector"
            tooltip="Close"
            icon={X}
            size="sm"
            command="inspector.close"
            onClick={onClose}
          />
        </div>
      </div>
      <p className="px-5 py-4 text-13 text-text-secondary">
        Everything about this URL shows here: its summary, issues, inlinks and outlinks, redirects, headers, structured
        data, hreflang and source. It arrives with the inspector’s slice.
      </p>
    </aside>
  );
}
