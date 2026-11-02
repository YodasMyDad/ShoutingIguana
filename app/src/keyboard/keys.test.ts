import { describe, expect, it } from 'vitest';
import {
  ariaKeyShortcut,
  chordKeys,
  formatChord,
  isEditableTarget,
  matchesChord,
  parseChord,
  typesCharacter,
} from './keys';

const event = (
  key: string,
  modifiers: Partial<Record<'ctrlKey' | 'metaKey' | 'altKey' | 'shiftKey', boolean>> = {},
  code = '',
) => ({
  key,
  code,
  ctrlKey: false,
  metaKey: false,
  altKey: false,
  shiftKey: false,
  ...modifiers,
});

describe('chords', () => {
  it('match the key marked on a non-Latin layout, by its physical place', () => {
    // A Russian layout: the K key types "л", the comma key "б", the 1 key "1".
    expect(matchesChord(parseChord('mod+k'), event('л', { ctrlKey: true }, 'KeyK'), false)).toBe(true);
    expect(matchesChord(parseChord('mod+c'), event('с', { ctrlKey: true }, 'KeyC'), false)).toBe(true);
    expect(matchesChord(parseChord('mod+,'), event('б', { ctrlKey: true }, 'Comma'), false)).toBe(true);
    expect(matchesChord(parseChord('mod+k'), event('л', { metaKey: true }, 'KeyK'), true)).toBe(true);
  });

  it('match the letter marked on a Latin layout, wherever it is', () => {
    // AZERTY: the A key sits where QWERTY has Q.
    expect(matchesChord(parseChord('mod+a'), event('a', { ctrlKey: true }, 'KeyQ'), false)).toBe(true);
    expect(matchesChord(parseChord('mod+q'), event('a', { ctrlKey: true }, 'KeyQ'), false)).toBe(false);
    // AZERTY's 1 key types "&": Ctrl+1 is still Ctrl+1.
    expect(matchesChord(parseChord('mod+1'), event('&', { ctrlKey: true }, 'Digit1'), false)).toBe(true);
    // A German layout's comma is ASCII where it is: kept.
    expect(matchesChord(parseChord('mod+,'), event(',', { ctrlKey: true }, 'Comma'), false)).toBe(true);
  });

  it('parses modifiers and a key', () => {
    expect(parseChord('mod+shift+P')).toEqual({ mod: true, ctrl: false, alt: false, shift: true, key: 'p' });
    expect(parseChord('esc').key).toBe('escape');
    expect(parseChord('mod++')).toEqual({ mod: true, ctrl: false, alt: false, shift: false, key: '+' });
    expect(parseChord('+').key).toBe('+');
    expect(() => parseChord('mod+')).toThrow();
  });

  it('matches Ctrl on Windows and Cmd on macOS for mod', () => {
    const chord = parseChord('mod+k');
    expect(matchesChord(chord, event('k', { ctrlKey: true }), false)).toBe(true);
    expect(matchesChord(chord, event('k', { metaKey: true }), false)).toBe(false);
    expect(matchesChord(chord, event('k', { metaKey: true }), true)).toBe(true);
    expect(matchesChord(chord, event('k', { ctrlKey: true }), true)).toBe(false);
  });

  it('needs the exact modifiers', () => {
    expect(matchesChord(parseChord('mod+k'), event('k', { ctrlKey: true, shiftKey: true }), false)).toBe(false);
    expect(matchesChord(parseChord('alt+arrowleft'), event('ArrowLeft', { altKey: true }), false)).toBe(true);
    expect(matchesChord(parseChord('f7'), event('F7'), false)).toBe(true);
  });

  it('ignores Shift for a symbol that already says it (? is Shift+/ on most layouts)', () => {
    expect(matchesChord(parseChord('?'), event('?', { shiftKey: true }), false)).toBe(true);
    expect(matchesChord(parseChord('mod++'), event('+', { ctrlKey: true, shiftKey: true }), false)).toBe(true);
  });

  it('reads digits by their key even where the layout types something else', () => {
    expect(matchesChord(parseChord('mod+1'), event('&', { ctrlKey: true }, 'Digit1'), false)).toBe(true);
  });

  it('shows Ctrl+Shift+P on Windows and ⇧⌘P on macOS', () => {
    expect(formatChord(parseChord('mod+shift+p'), false)).toBe('Ctrl+Shift+P');
    expect(formatChord(parseChord('mod+shift+p'), true)).toBe('⇧⌘P');
    expect(formatChord(parseChord('alt+arrowleft'), false)).toBe('Alt+Left');
    expect(formatChord(parseChord('alt+arrowleft'), true)).toBe('⌥←');
    expect(chordKeys(parseChord('mod++'), false)).toEqual(['Ctrl', '+']);
    expect(formatChord(parseChord('escape'), false)).toBe('Esc');
  });

  it('writes aria-keyshortcuts in UI Events names', () => {
    expect(ariaKeyShortcut(parseChord('mod+k'), false)).toBe('Control+K');
    expect(ariaKeyShortcut(parseChord('mod+k'), true)).toBe('Meta+K');
    expect(ariaKeyShortcut(parseChord('alt+arrowleft'), false)).toBe('Alt+ArrowLeft');
  });

  it('knows which chords would type a character', () => {
    expect(typesCharacter(parseChord('?'))).toBe(true);
    expect(typesCharacter(parseChord('space'))).toBe(true);
    expect(typesCharacter(parseChord('mod+k'))).toBe(false);
    expect(typesCharacter(parseChord('f7'))).toBe(false);
  });

  it('knows a text field from a button', () => {
    const input = document.createElement('input');
    const checkbox = document.createElement('input');
    checkbox.type = 'checkbox';
    expect(isEditableTarget(input)).toBe(true);
    expect(isEditableTarget(checkbox)).toBe(false);
    expect(isEditableTarget(document.createElement('textarea'))).toBe(true);
    expect(isEditableTarget(document.createElement('button'))).toBe(false);
  });
});
