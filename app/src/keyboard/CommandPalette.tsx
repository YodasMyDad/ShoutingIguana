import * as Dialog from '@radix-ui/react-dialog';
import { useQuery } from '@tanstack/react-query';
import { useNavigate, useParams } from '@tanstack/react-router';
import { useEffect, useId, useMemo, useState, type KeyboardEvent, type ReactNode } from 'react';
import { checksQuery, libraryQuery } from '../api/queries';
import { restoreFocus, setOverlay, useOverlay } from '../app/overlays';
import { crawlName } from '../crawl/status';
import { formatCount, formatRelativeTime } from '../lib/format';
import { Kbd } from '../ui/Kbd';
import { searchPalette, type PaletteEntry } from './paletteSearch';
import { COMMAND_GROUPS, commands, useRegistryVersion } from './registry';

const RECENT_CRAWLS = 8;
const PAGE_STEP = 8;
/** The result count is announced once typing pauses, not on every key. */
const ANNOUNCE_DELAY_MS = 400;
const GROUP_ORDER = [...COMMAND_GROUPS.filter((group) => group !== 'In grids'), 'Checks', 'Recent crawls'];

export interface PaletteItem extends PaletteEntry {
  hint?: ReactNode;
  run: () => unknown;
}

/**
 * The command palette (Ctrl+K): every screen and action in the registry, the open crawl's checks and the recent
 * crawls. Searching any URL joins it with E6's search. An ARIA combobox over a listbox: the input keeps focus and
 * aria-activedescendant always names the highlighted option, so screen readers hear what Enter will run.
 */
export function CommandPalette() {
  const open = useOverlay() === 'palette';
  useRegistryVersion();
  const navigate = useNavigate();
  const { crawl: crawlId } = useParams({ strict: false });
  const library = useQuery({ ...libraryQuery(), enabled: open }).data;
  const checks = useQuery({ ...checksQuery(), enabled: open && crawlId !== undefined }).data;

  const entries = useMemo<PaletteItem[]>(() => {
    const list: PaletteItem[] = commands
      .list()
      .filter((command) => command.scope !== 'grid' && command.palette !== false && commands.isEnabled(command))
      .map((command) => ({
        id: `command:${command.id}`,
        group: command.group,
        title: command.title,
        keywords: command.keywords ?? [],
        ...(command.keys?.[0] ? { hint: <Kbd chord={command.keys[0]} size="sm" /> } : {}),
        run: () => commands.run(command.id),
      }));
    if (crawlId && checks) {
      for (const check of checks.checks) {
        list.push({
          id: `check:${check.id}`,
          group: 'Checks',
          title: check.title,
          keywords: [check.id, check.severity, check.category],
          run: () => navigate({ to: '/crawls/$crawl/issues/{-$check}', params: { crawl: crawlId, check: check.id } }),
        });
      }
    }
    for (const item of (library ?? []).slice(0, RECENT_CRAWLS)) {
      list.push({
        id: `crawl:${item.id}`,
        group: 'Recent crawls',
        title: crawlName(item),
        keywords: [item.startUrl ?? '', item.fileName],
        hint: <span className="text-12 text-text-muted">{formatRelativeTime(item.startedAt)}</span>,
        run: () => navigate({ to: '/crawls/$crawl', params: { crawl: item.id } }),
      });
    }
    return list;
  }, [crawlId, checks, library, navigate]);

  const close = () => setOverlay(null);
  /** Closes first, so focus is back where it was before the command moves it. */
  const choose = (entry: PaletteItem) => {
    close();
    requestAnimationFrame(() => void entry.run());
  };

  return (
    <Dialog.Root open={open} onOpenChange={(next) => setOverlay(next ? 'palette' : null)}>
      <Dialog.Portal>
        <Dialog.Overlay className="si-dialog-overlay" />
        <Dialog.Content
          aria-describedby={undefined}
          // Opened from the keyboard there's no trigger for Radix to give focus back to: put it where it was.
          onCloseAutoFocus={(event) => {
            event.preventDefault();
            restoreFocus();
          }}
          className="fixed top-(--palette-top) left-1/2 z-40 w-(--palette-width) max-w-full -translate-x-1/2 overflow-hidden rounded-xl border border-border bg-surface-overlay shadow-4"
        >
          <Dialog.Title className="sr-only">Command palette</Dialog.Title>
          <PaletteBody entries={entries} onChoose={choose} />
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

export function PaletteBody({
  entries,
  onChoose,
}: {
  entries: readonly PaletteItem[];
  onChoose: (entry: PaletteItem) => void;
}) {
  const id = useId();
  const [query, setQuery] = useState('');
  const [highlight, setHighlight] = useState(0);
  const results = useMemo(() => searchPalette(entries, query, GROUP_ORDER), [entries, query]);
  const active = results.length === 0 ? -1 : Math.min(highlight, results.length - 1);
  // By the entry, not its place: when typing changes what's highlighted, the attribute changes too, and screen
  // readers hear the new option.
  const optionId = (entry: PaletteItem) => `${id}-${entry.id}`;
  const activeEntry = active >= 0 ? results[active] : undefined;
  const activeId = activeEntry ? optionId(activeEntry) : undefined;
  const announcement = useDebouncedValue(resultCount(results.length), ANNOUNCE_DELAY_MS);

  useEffect(() => {
    if (activeId) document.getElementById(activeId)?.scrollIntoView({ block: 'nearest' });
  }, [activeId]);

  /** Where a key moves the highlight, or null for a key the palette leaves to the input. */
  const moveFor = (key: string, last: number): number | null => {
    switch (key) {
      case 'ArrowDown':
        return active >= last ? 0 : active + 1;
      case 'ArrowUp':
        return active <= 0 ? last : active - 1;
      case 'PageDown':
        return Math.min(last, active + PAGE_STEP);
      case 'PageUp':
        return Math.max(0, active - PAGE_STEP);
      default:
        return null;
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      const entry = results[active];
      if (entry) {
        event.preventDefault();
        onChoose(entry);
      }
      return;
    }
    const next = moveFor(event.key, results.length - 1);
    if (next === null) return;
    event.preventDefault();
    if (results.length > 0) setHighlight(next);
  };

  // A group's name shows above its first result.
  const startsGroup = results.map((entry, index) => index === 0 || results[index - 1]?.group !== entry.group);
  return (
    <div className="flex flex-col">
      {/* Radix focuses the first focusable element on open: this input. */}
      <input
        role="combobox"
        aria-expanded="true"
        aria-controls={`${id}-list`}
        aria-autocomplete="list"
        aria-activedescendant={activeId}
        aria-label="Search commands, checks and crawls"
        placeholder="Search or run a command…"
        autoComplete="off"
        spellCheck={false}
        value={query}
        onChange={(event) => {
          setQuery(event.target.value);
          setHighlight(0);
        }}
        onKeyDown={onKeyDown}
        className="h-(--control-height-lg) border-b border-border-subtle bg-transparent px-4 text-16 text-text outline-none placeholder:text-text-muted"
      />
      <div
        id={`${id}-list`}
        role="listbox"
        aria-label="Results"
        className="max-h-(--palette-list-max) overflow-auto p-2"
      >
        {results.map((entry, index) => (
          <div key={entry.id}>
            {startsGroup[index] ? (
              <div aria-hidden="true" className="si-palette-heading">
                {entry.group}
              </div>
            ) : null}
            {/* The combobox pattern: focus stays in the input, which takes the keys and names this option through
                  aria-activedescendant. jsx-a11y can't see that, so it asks for keys and focus on the option itself. */}
            {/* eslint-disable-next-line jsx-a11y-x/click-events-have-key-events, jsx-a11y-x/interactive-supports-focus */}
            <div
              id={optionId(entry)}
              role="option"
              aria-selected={index === active}
              onPointerMove={() => {
                if (index !== active) setHighlight(index);
              }}
              onClick={() => onChoose(entry)}
              className="si-palette-item"
            >
              <span className="truncate">{entry.title}</span>
              <span className="sr-only">, {entry.group}</span>
              {entry.hint ? <span className="ml-auto">{entry.hint}</span> : null}
            </div>
          </div>
        ))}
      </div>
      {results.length === 0 ? (
        <p aria-hidden="true" className="px-3 pb-6 text-center text-13 text-text-muted">
          Nothing matches that.
        </p>
      ) : null}
      {/* Always mounted, so what changes in it is read: how many results there are, once typing pauses. */}
      <div role="status" aria-live="polite" aria-atomic="true" className="sr-only">
        {announcement}
      </div>
    </div>
  );
}

function resultCount(count: number): string {
  if (count === 0) return 'Nothing matches that.';
  return count === 1 ? '1 result' : `${formatCount(count)} results`;
}

/** The value, once it has stayed the same for `delayMs`. */
function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [settled, setSettled] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);
  return settled;
}
