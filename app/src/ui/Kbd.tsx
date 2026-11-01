import { isMac } from '../lib/platform';
import { chordKeys, parseChord } from '../keyboard/keys';

/** A chord drawn as keycaps, in this platform's names: Ctrl+K, or ⌘K on macOS. */
export function Kbd({ chord, className }: { chord: string; className?: string }) {
  const keys = chordKeys(parseChord(chord), isMac);
  return (
    <kbd className={className ?? 'inline-flex items-center gap-0.5 font-sans'}>
      {keys.map((key, index) => (
        <kbd
          key={`${key}-${index}`}
          className="inline-flex h-(--kbd-size-sm) min-w-(--kbd-size-sm) items-center justify-center rounded-xs border border-border bg-surface-sunken px-1 text-11 text-text-muted"
        >
          {key}
        </kbd>
      ))}
    </kbd>
  );
}
