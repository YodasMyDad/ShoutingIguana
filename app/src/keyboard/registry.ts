// The one command registry. Every action is a command: its button, the palette, the `?` sheet, tooltips and menus
// all read it, and one keydown listener runs it, so they can't disagree. Screens register the commands that make
// sense while they're shown.
//
// Precedence is explicit: a screen's registration of an id replaces the app's while the screen is shown, whatever
// order they were registered in (the app re-registers its commands whenever a crawl's status changes); within the
// same precedence, the latest registration wins.
import { useEffect, useSyncExternalStore } from 'react';
import { isModalOpen } from '../app/overlays';
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

export type CommandGroup = 'Go to' | 'Crawl' | 'Issues' | 'View' | 'App' | 'In grids';

export const COMMAND_GROUPS: readonly CommandGroup[] = ['Go to', 'Crawl', 'Issues', 'View', 'App', 'In grids'];

/** Who registered a command: the app's shell, or the screen on show, whose commands win. */
export type Precedence = 'app' | 'screen';

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
  /** Also fires while an overlay (the palette, a dialog) is open: only for the key that toggles that overlay. */
  inOverlays?: boolean;
  /** Whether it can run now. A disabled command is greyed in the palette, and its keys do nothing. */
  enabled?: () => boolean;
  run?: () => unknown;
}

interface Entry {
  command: Command;
  chords: Chord[];
  precedence: number;
  order: number;
}

const PRECEDENCE: Record<Precedence, number> = { app: 0, screen: 1 };

/**
 * Keys the app owns everywhere (docs/v2-ui-ia.md, Keyboard) whose commands arrive with their screens (Export this
 * view, Filter this grid), and Ctrl+P, which would print. Pressing one where its command isn't there does nothing,
 * rather than open the browser's find bar or print dialog. The shell turns WebView2's browser keys off too; a plain
 * browser needs this.
 */
const RESERVED_KEYS = ['mod+e', 'mod+f', 'mod+p'];

export interface RegistryOptions {
  mac?: boolean;
  /** Whether a modal overlay is open: global keys then belong to it. */
  isModal?: () => boolean;
}

export class CommandRegistry {
  private entries: Entry[] = [];
  private nextOrder = 0;
  private version = 0;
  private readonly listeners = new Set<() => void>();
  private readonly mac: boolean;
  private readonly isModal: () => boolean;
  private readonly reserved: Chord[] = RESERVED_KEYS.map(parseChord);
  private onError: (error: unknown, command: Command) => void = (error) => console.error(error);

  constructor(options: RegistryOptions | boolean = {}) {
    const resolved = typeof options === 'boolean' ? { mac: options } : options;
    this.mac = resolved.mac ?? isMac;
    this.isModal = resolved.isModal ?? (() => false);
  }

  /** Adds commands; the returned function takes them away again. */
  register(commands: readonly Command[], precedence: Precedence = 'screen'): () => void {
    const added = commands.map((command) => ({
      command,
      chords: (command.keys ?? []).map(parseChord),
      precedence: PRECEDENCE[precedence],
      order: this.nextOrder++,
    }));
    this.entries = [...this.entries, ...added];
    this.changed();
    return () => {
      this.entries = this.entries.filter((entry) => !added.includes(entry));
      this.changed();
    };
  }

  /** Every command in effect: the winning registration of each id, in the order ids first appeared. */
  list(): Command[] {
    const winners = new Map<string, Entry>();
    for (const entry of this.entries) {
      const current = winners.get(entry.command.id);
      if (!current || outranks(entry, current)) winners.set(entry.command.id, entry);
    }
    return [...winners.values()].map((entry) => entry.command);
  }

  get(id: string): Command | undefined {
    return this.ranked().find((entry) => entry.command.id === id)?.command;
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

  /**
   * Whether a command would run for this key now. The focused grid asks before taking Esc for itself: closing the
   * inspector comes before clearing the grid's selection.
   */
  claims(event: KeyboardEvent): boolean {
    const command = this.match(event);
    return command !== null && this.canRunNow(command);
  }

  /**
   * The app's one keydown handler. Runs at most one command, and only when nothing else took the key. A key that
   * belongs to a command is taken even while the command can't run, so the browser never acts on it instead (F7
   * would start caret browsing); so are the reserved keys. While an overlay is open it owns the keyboard: the app's
   * keys do nothing behind it, except the one that toggles it.
   */
  handleKeyDown(event: KeyboardEvent): boolean {
    if (event.defaultPrevented || event.isComposing) return false;
    const command = this.match(event);
    if (!command) {
      if (this.isReserved(event)) event.preventDefault();
      return false;
    }
    event.preventDefault();
    // Held down, a key repeats: a command runs once per press.
    if (event.repeat || !this.canRunNow(command)) return false;
    this.invoke(command);
    return true;
  }

  setErrorHandler(handler: (error: unknown, command: Command) => void): void {
    this.onError = handler;
  }

  readonly subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  readonly getVersion = (): number => this.version;

  /** The command this key belongs to here, enabled or not; null when it's no command's, or a text field's to keep. */
  private match(event: KeyboardEvent): Command | null {
    const editable = isEditableTarget(event.target);
    const shadowed = new Set<string>();
    for (const { command, chords } of this.ranked()) {
      if (shadowed.has(command.id)) continue;
      shadowed.add(command.id);
      if (command.scope === 'grid') continue;
      const chord = chords.find((candidate) => matchesChord(candidate, event, this.mac));
      if (!chord) continue;
      // A text field keeps the keys that type, and the rest unless the command says otherwise.
      if (editable && (typesCharacter(chord) || !command.inInputs)) continue;
      return command;
    }
    return null;
  }

  /** Enabled, and not behind an open overlay (the app behind it is inert), unless it's the overlay's own toggle. */
  private canRunNow(command: Command): boolean {
    return this.isEnabled(command) && (command.inOverlays === true || !this.isModal());
  }

  private isReserved(event: KeyboardEvent): boolean {
    return this.reserved.some((chord) => matchesChord(chord, event, this.mac));
  }

  /** Entries from the winner down: screen before app, then the latest first. */
  private ranked(): Entry[] {
    return [...this.entries].sort((a, b) => (outranks(a, b) ? -1 : outranks(b, a) ? 1 : 0));
  }

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

function outranks(a: Entry, b: Entry): boolean {
  return a.precedence !== b.precedence ? a.precedence > b.precedence : a.order > b.order;
}

export const commands = new CommandRegistry({ isModal: isModalOpen });

/** Listens for the registry's keys on the window. Components that handle a key themselves call preventDefault. */
export function installKeyboard(registry: CommandRegistry = commands): () => void {
  const listener = (event: KeyboardEvent) => registry.handleKeyDown(event);
  window.addEventListener('keydown', listener);
  return () => window.removeEventListener('keydown', listener);
}

/**
 * Registers commands while the component is mounted. `create` runs again whenever `deps` change. A screen's commands
 * win over the app's (`precedence`).
 */
export function useCommands(
  create: () => readonly Command[],
  deps: readonly unknown[],
  precedence: Precedence = 'screen',
  registry = commands,
): void {
  // eslint-disable-next-line react-hooks/exhaustive-deps -- the caller's deps decide when to re-register
  useEffect(() => registry.register(create(), precedence), deps);
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

/** A command's first chord (`mod+k`), for keycaps in tooltips and menus, or null. */
export function useChord(id: string, registry = commands): string | null {
  useRegistryVersion(registry);
  return registry.get(id)?.keys?.[0] ?? null;
}

/** A command's chords for `aria-keyshortcuts`, or null. */
export function useAriaShortcut(id: string, registry = commands): string | null {
  useRegistryVersion(registry);
  return registry.ariaShortcut(id);
}
