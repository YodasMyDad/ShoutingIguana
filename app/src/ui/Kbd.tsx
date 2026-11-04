import clsx from 'clsx';
import { isMac } from '../lib/platform';
import { chordKeys, parseChord } from '../keyboard/keys';

/**
 * A chord drawn as keycaps, in this platform's names: Ctrl+K, or ⌘K on macOS. `sm` is the grid footer's and the
 * menus' size. Inside a primary button the caps take the button's colours.
 */
export function Kbd({
  chord,
  className,
  size = 'md',
  'aria-hidden': hidden,
}: {
  chord: string;
  className?: string;
  size?: 'sm' | 'md';
  /** Shown, not read: keycaps inside a control whose `aria-keyshortcuts` gives the keys, out of its name. */
  'aria-hidden'?: boolean | undefined;
}) {
  const keys = chordKeys(parseChord(chord), isMac);
  return (
    <kbd className={clsx('si-kbd-group', className)} aria-hidden={hidden}>
      {keys.map((key, index) => (
        <kbd key={`${key}-${index}`} className={clsx('si-kbd', size === 'sm' && 'si-kbd--sm')}>
          {key}
        </kbd>
      ))}
    </kbd>
  );
}
