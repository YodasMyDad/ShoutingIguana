import * as Dialog from '@radix-ui/react-dialog';
import { useQuery } from '@tanstack/react-query';
import { useNavigate, useParams } from '@tanstack/react-router';
import { useEffect, useId, useMemo, useState, type KeyboardEvent, type ReactNode } from 'react';
import { checksQuery, libraryQuery } from '../api/queries';
import { setOverlay, useOverlay } from '../app/overlays';
import { crawlName } from '../crawl/status';
import { formatRelativeTime } from '../lib/format';
import { Kbd } from '../ui/Kbd';
import { searchPalette, type PaletteEntry } from './paletteSearch';
import { COMMAND_GROUPS, commands, useRegistryVersion } from './registry';

const RECENT_CRAWLS = 8;
const PAGE_STEP = 8;
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
        ...(command.keys?.[0] ? { hint: <Kbd chord={command.keys[0]} className="inline-flex gap-0.5" /> } : {}),
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
        <Dialog.Overlay className="fixed inset-0 z-40 bg-scrim" />
        <Dialog.Content
          aria-describedby={undefined}
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
  const optionId = (index: number) => `${id}-option-${index}`;

  useEffect(() => {
    if (active >= 0) document.getElementById(optionId(active))?.scrollIntoView({ block: 'nearest' });
    // optionId only joins strings: active is what moves.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [active]);

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
        aria-activedescendant={active >= 0 ? optionId(active) : undefined}
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
              id={optionId(index)}
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
        <p role="status" className="px-3 pb-6 text-center text-13 text-text-muted">
          Nothing matches that.
        </p>
      ) : null}
    </div>
  );
}
