// Key chords: written once as `mod+k`, matched against key events, and shown as "Ctrl+K" on Windows and Linux or
// "⌘K" on macOS. `mod` is Cmd on macOS and Ctrl elsewhere; `ctrl` is always the Control key.

export interface Chord {
  mod: boolean;
  ctrl: boolean;
  alt: boolean;
  shift: boolean;
  /** Lower-case `KeyboardEvent.key` (`k`, `arrowleft`, `f7`, `?`, `,`), with `space` for " ". */
  key: string;
}

const MODIFIERS = new Set(['mod', 'ctrl', 'alt', 'shift']);

/** Parses `mod+shift+p`. Throws on a chord that names no key: that's a bug in a command's definition. */
export function parseChord(text: string): Chord {
  const chord: Chord = { mod: false, ctrl: false, alt: false, shift: false, key: '' };
  let rest = text.toLowerCase();
  // The "+" key itself is written `+` alone or last, as in `mod++`.
  if (rest === '+' || rest.endsWith('++')) {
    chord.key = '+';
    rest = rest.slice(0, -2);
  }
  for (const part of rest ? rest.split('+') : []) {
    if (MODIFIERS.has(part)) chord[part as 'mod' | 'ctrl' | 'alt' | 'shift'] = true;
    else if (part) chord.key = part === 'esc' ? 'escape' : part;
  }
  if (!chord.key) throw new Error(`The chord "${text}" names no key`);
  return chord;
}

function eventKey(event: Pick<KeyboardEvent, 'key' | 'code'>): string {
  if (event.key === ' ') return 'space';
  // With Ctrl or Alt held, some layouts report a different character for digits: go by the physical key.
  if (/^Digit\d$/.test(event.code) && !/^\d$/.test(event.key)) return event.code.slice(5);
  return event.key.toLowerCase();
}

/** A key whose character already says whether Shift was held: `?`, `+`, `,`. Its Shift state isn't compared. */
function isSymbol(key: string): boolean {
  return key.length === 1 && !/[a-z0-9]/.test(key);
}

export function matchesChord(
  chord: Chord,
  event: Pick<KeyboardEvent, 'key' | 'code' | 'ctrlKey' | 'metaKey' | 'altKey' | 'shiftKey'>,
  mac: boolean,
): boolean {
  if (eventKey(event) !== chord.key) return false;
  const wantCtrl = chord.ctrl || (chord.mod && !mac);
  const wantMeta = chord.mod && mac;
  if (event.ctrlKey !== wantCtrl || event.metaKey !== wantMeta || event.altKey !== chord.alt) return false;
  return isSymbol(chord.key) && !chord.shift ? true : event.shiftKey === chord.shift;
}

const KEY_NAMES: Record<string, string> = {
  arrowleft: 'Left',
  arrowright: 'Right',
  arrowup: 'Up',
  arrowdown: 'Down',
  escape: 'Esc',
  enter: 'Enter',
  space: 'Space',
  tab: 'Tab',
  backspace: 'Backspace',
  delete: 'Delete',
  home: 'Home',
  end: 'End',
  pageup: 'Page Up',
  pagedown: 'Page Down',
  contextmenu: 'Menu',
};

const MAC_KEY_NAMES: Record<string, string> = {
  arrowleft: '←',
  arrowright: '→',
  arrowup: '↑',
  arrowdown: '↓',
  escape: 'Esc',
  enter: '↩',
  backspace: '⌫',
  delete: '⌦',
};

function keyName(key: string, mac: boolean): string {
  return (mac ? MAC_KEY_NAMES[key] : undefined) ?? KEY_NAMES[key] ?? key.toUpperCase();
}

/** The separate keys of a chord, for drawing as keycaps: ["Ctrl", "Shift", "P"], or ["⇧", "⌘", "P"] on macOS. */
export function chordKeys(chord: Chord, mac: boolean): string[] {
  if (mac) {
    // Apple's modifier order: Control, Option, Shift, Command.
    return [
      ...(chord.ctrl ? ['⌃'] : []),
      ...(chord.alt ? ['⌥'] : []),
      ...(chord.shift ? ['⇧'] : []),
      ...(chord.mod ? ['⌘'] : []),
      keyName(chord.key, true),
    ];
  }
  return [
    ...(chord.ctrl || chord.mod ? ['Ctrl'] : []),
    ...(chord.alt ? ['Alt'] : []),
    ...(chord.shift ? ['Shift'] : []),
    keyName(chord.key, false),
  ];
}

/** "Ctrl+Shift+P" on Windows and Linux; "⇧⌘P" on macOS. */
export function formatChord(chord: Chord, mac: boolean): string {
  return chordKeys(chord, mac).join(mac ? '' : '+');
}

const ARIA_KEYS: Record<string, string> = {
  arrowleft: 'ArrowLeft',
  arrowright: 'ArrowRight',
  arrowup: 'ArrowUp',
  arrowdown: 'ArrowDown',
  escape: 'Escape',
  enter: 'Enter',
  space: 'Space',
  pageup: 'PageUp',
  pagedown: 'PageDown',
  home: 'Home',
  end: 'End',
  contextmenu: 'ContextMenu',
};

/** The chord as `aria-keyshortcuts` wants it: modifier and key names from UI Events ("Control+K", "Meta+K"). */
export function ariaKeyShortcut(chord: Chord, mac: boolean): string {
  const key = ARIA_KEYS[chord.key] ?? (/^f\d+$/.test(chord.key) ? chord.key.toUpperCase() : chord.key.toUpperCase());
  return [
    ...(chord.ctrl || (chord.mod && !mac) ? ['Control'] : []),
    ...(chord.mod && mac ? ['Meta'] : []),
    ...(chord.alt ? ['Alt'] : []),
    ...(chord.shift ? ['Shift'] : []),
    key,
  ].join('+');
}

/** True for chords that type a character when pressed alone, which must never fire while typing in a field. */
export function typesCharacter(chord: Chord): boolean {
  return !chord.mod && !chord.ctrl && !chord.alt && (chord.key.length === 1 || chord.key === 'space');
}

export function isEditableTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  if (target.isContentEditable) return true;
  if (target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement) return true;
  if (target instanceof HTMLInputElement) {
    return !['checkbox', 'radio', 'button', 'submit', 'reset', 'range', 'color', 'file'].includes(target.type);
  }
  return target.getAttribute('role') === 'combobox' || target.getAttribute('role') === 'searchbox';
}
