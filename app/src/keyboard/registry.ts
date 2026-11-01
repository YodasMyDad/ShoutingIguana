// The one command registry. Every action is a command: its button, the palette, the `?` sheet, tooltips and menus
// all read it, and one keydown listener runs it, so they can't disagree. Screens register the commands that make
// sense while they're shown; a later registration of the same id replaces the earlier one until it goes away.
import { useEffect, useSyncExternalStore } from 'react';
import { isMac } from '../lib/platform';
import {
  ariaKeyShortcut,
  formatChord,
  isEditableTarget,
  matchesChord,
  parseChord,
  typesCharacter,
  type Chord,
} from './keys';

export type CommandGroup = 'Go to' | 'Crawl' | 'View' | 'App' | 'In grids';

export const COMMAND_GROUPS: readonly CommandGroup[] = ['Go to', 'Crawl', 'View', 'App', 'In grids'];

export interface Command {
  id: string;
  /** What the palette and the `?` sheet say: an action, in sentence case ("Pause the crawl"). */
  title: string;
  group: CommandGroup;
  /** Chords such as `mod+k`; the first is the one shown. */
  keys?: readonly string[];
  /** More words the palette matches. */
  keywords?: readonly string[];
  /**
   * `grid` keys are handled by the focused grid itself: the registry lists them, for the `?` sheet and tooltips, and
   * never dispatches them.
   */
  scope?: 'global' | 'grid';
  /** Listed in the palette. Defaults to true for a global command that can run. */
  palette?: boolean;
  /** Also fires while typing in a field. */
  inInputs?: boolean;
  /** Whether it can run now. A disabled command is greyed in the palette, and its keys do nothing. */
  enabled?: () => boolean;
  run?: () => unknown;
}

interface Entry {
  command: Command;
  chords: Chord[];
  order: number;
}

export class CommandRegistry {
  private entries: Entry[] = [];
  private nextOrder = 0;
  private version = 0;
  private readonly listeners = new Set<() => void>();
  private onError: (error: unknown, command: Command) => void = (error) => console.error(error);

  constructor(private readonly mac: boolean = isMac) {}

  /** Adds commands; the returned function takes them away again. */
  register(commands: readonly Command[]): () => void {
    const added = commands.map((command) => ({
      command,
      chords: (command.keys ?? []).map(parseChord),
      order: this.nextOrder++,
    }));
    this.entries = [...this.entries, ...added];
    this.changed();
    return () => {
      this.entries = this.entries.filter((entry) => !added.includes(entry));
      this.changed();
    };
  }

  /** Every command in effect: the latest registration of each id, in the order ids first appeared. */
  list(): Command[] {
    const latest = new Map<string, Command>();
    for (const entry of this.entries) latest.set(entry.command.id, entry.command);
    return [...latest.values()];
  }

  get(id: string): Command | undefined {
    for (let i = this.entries.length - 1; i >= 0; i--) {
      const entry = this.entries[i];
      if (entry?.command.id === id) return entry.command;
    }
    return undefined;
  }

  isEnabled(command: Command): boolean {
    return command.run !== undefined && (command.enabled?.() ?? true);
  }

  /** The command's first chord as this platform writes it ("Ctrl+K", "⌘K"), or null when it has none. */
  shortcut(id: string): string | null {
    const key = this.get(id)?.keys?.[0];
    return key ? formatChord(parseChord(key), this.mac) : null;
  }

  /** Every chord of the command, for `aria-keyshortcuts`. */
  ariaShortcut(id: string): string | null {
    const keys = this.get(id)?.keys ?? [];
    return keys.length > 0 ? keys.map((key) => ariaKeyShortcut(parseChord(key), this.mac)).join(' ') : null;
  }

  /** Runs a command by id. False when there's no such command or it can't run now. */
  run(id: string): boolean {
    const command = this.get(id);
    if (!command || !this.isEnabled(command)) return false;
    this.invoke(command);
    return true;
  }

  /** The app's one keydown handler. Runs at most one command, and only when nothing else took the key. */
  handleKeyDown(event: KeyboardEvent): boolean {
    if (event.defaultPrevented || event.isComposing || event.repeat) return false;
    const editable = isEditableTarget(event.target);
    // The latest registrations first, so a screen's command wins over the app's.
    const shadowed = new Set<string>();
    for (let i = this.entries.length - 1; i >= 0; i--) {
      const entry = this.entries[i];
      if (!entry) continue;
      const { command, chords } = entry;
      if (shadowed.has(command.id)) continue;
      shadowed.add(command.id);
      if (command.scope === 'grid') continue;
      const chord = chords.find((candidate) => matchesChord(candidate, event, this.mac));
      if (!chord) continue;
      if (editable && (typesCharacter(chord) || !command.inInputs)) continue;
      if (!this.isEnabled(command)) continue;
      event.preventDefault();
      this.invoke(command);
      return true;
    }
    return false;
  }

  setErrorHandler(handler: (error: unknown, command: Command) => void): void {
    this.onError = handler;
  }

  readonly subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  readonly getVersion = (): number => this.version;

  private invoke(command: Command): void {
    try {
      const result = command.run?.();
      if (result instanceof Promise) result.catch((error: unknown) => this.onError(error, command));
    } catch (error) {
      this.onError(error, command);
    }
  }

  private changed(): void {
    this.version++;
    this.listeners.forEach((listener) => listener());
  }
}

export const commands = new CommandRegistry();

/** Listens for the registry's keys on the window. Components that handle a key themselves call preventDefault. */
export function installKeyboard(registry: CommandRegistry = commands): () => void {
  const listener = (event: KeyboardEvent) => registry.handleKeyDown(event);
  window.addEventListener('keydown', listener);
  return () => window.removeEventListener('keydown', listener);
}

/** Registers commands while the component is mounted. `create` runs again whenever `deps` change. */
export function useCommands(create: () => readonly Command[], deps: readonly unknown[], registry = commands): void {
  // eslint-disable-next-line react-hooks/exhaustive-deps -- the caller's deps decide when to re-register
  useEffect(() => registry.register(create()), deps);
}

/** Re-renders when the registry changes, for lists such as the palette and the `?` sheet. */
export function useRegistryVersion(registry = commands): number {
  return useSyncExternalStore(registry.subscribe, registry.getVersion, registry.getVersion);
}

/** "Ctrl+K" for a command's tooltip or menu item, or null. */
export function useShortcut(id: string, registry = commands): string | null {
  useRegistryVersion(registry);
  return registry.shortcut(id);
}

/** A command's chords for `aria-keyshortcuts`, or null. */
export function useAriaShortcut(id: string, registry = commands): string | null {
  useRegistryVersion(registry);
  return registry.ariaShortcut(id);
}
