import { useNavigate, useSearch } from '@tanstack/react-router';
import clsx from 'clsx';
import { useRef, type KeyboardEvent, type PointerEvent } from 'react';
import { useCommands } from '../keyboard/registry';
import { INSPECTOR_HEIGHT_RANGE, INSPECTOR_WIDTH_RANGE } from '../settings/schema';
import { updateSettings, useSettings } from '../settings/settingsStore';
import { Button } from '../ui/Button';

const KEY_STEP = 16;

/**
 * Where the URL inspector opens (docs/v2-ui-ia.md, The shell): on the right or docked at the bottom, resizable by
 * pointer or keyboard, closed with Esc. It opens for `?inspect=<url id>` on any view, so back and forward retrace
 * it. The inspector's content is its own slice (it needs E3's `/urls/{urlId}`).
 */
export function InspectorSlot() {
  const navigate = useNavigate();
  const inspect = useSearch({ strict: false, select: (search) => (search as { inspect?: number }).inspect });
  const dock = useSettings((settings) => settings.inspectorDock);
  const size = useSettings((settings) => (dock === 'right' ? settings.inspectorWidth : settings.inspectorHeight));
  const drag = useRef<{ start: number; size: number } | null>(null);
  const open = inspect !== undefined;

  const close = () =>
    void navigate({ to: '.', search: (previous: Record<string, unknown>) => ({ ...previous, inspect: undefined }) });

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
  );

  if (!open) return null;

  const range = dock === 'right' ? INSPECTOR_WIDTH_RANGE : INSPECTOR_HEIGHT_RANGE;
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
      aria-label="Inspector"
      className={clsx(
        'relative flex shrink-0 flex-col border-border bg-surface-raised shadow-2',
        dock === 'right' ? 'border-l' : 'border-t',
      )}
      style={dock === 'right' ? { width: size } : { height: size }}
    >
      {/* A focusable separator is the APG window-splitter widget; jsx-a11y only knows the static separator. */}
      {/* eslint-disable-next-line jsx-a11y-x/no-noninteractive-element-interactions */}
      <div
        role="separator"
        // eslint-disable-next-line jsx-a11y-x/no-noninteractive-tabindex
        tabIndex={0}
        aria-label="Resize the inspector"
        aria-orientation={dock === 'right' ? 'vertical' : 'horizontal'}
        aria-valuenow={size}
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
      <div className="flex h-(--tab-height) items-center gap-2 border-b border-border-subtle px-3">
        <h2 className="text-13 font-semibold">Inspector</h2>
        <span className="figures text-12 text-text-muted">URL {inspect}</span>
        <Button
          variant="ghost"
          size="sm"
          className="ml-auto"
          onClick={() => updateSettings({ inspectorDock: dock === 'right' ? 'bottom' : 'right' })}
        >
          {dock === 'right' ? 'Dock at the bottom' : 'Dock on the right'}
        </Button>
        <Button
          variant="ghost"
          size="sm"
          command="inspector.close"
          aria-label="Close the inspector"
          title="Close"
          onClick={close}
        >
          ✕
        </Button>
      </div>
      <p className="p-4 text-13 text-text-secondary">
        Everything about this URL shows here: its summary, issues, inlinks and outlinks, redirects, headers, structured
        data, hreflang and source. It arrives with the inspector’s slice.
      </p>
    </aside>
  );
}
