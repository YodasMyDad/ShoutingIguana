import { useRouter } from '@tanstack/react-router';
import { MODE_NAMES, MODES, useMode, type Mode } from '../mode/mode';
import { switchMode } from '../mode/switchMode';
import { SegmentedControl } from '../ui/SegmentedControl';
import { Tooltip } from '../ui/Tooltip';

/** What each mode is, read with its segment. */
const MODE_HINTS: Record<Mode, string> = {
  simple: 'A score, what to fix and a report, in plain words',
  advanced: 'Every URL, link, filter and setting, in technical terms',
};

/**
 * The switch (docs/v2-ui-simple.md, The switch): Simple | Advanced, in words, at the right-hand end of the top bar on
 * every screen. It never moves, shrinks or hides: everything else in the bar gives way first. Choosing a segment
 * switches at once and keeps focus where it is, on the segment; the change is announced once (mode/mode.ts).
 */
export function ModeSwitch() {
  const router = useRouter();
  const mode = useMode();
  return (
    <Tooltip content="Switch between Simple and Advanced" command="app.mode" triggerName="Mode">
      <div className="flex shrink-0" data-mode-switch="">
        <SegmentedControl<Mode>
          label="Mode"
          value={mode}
          options={MODES.map((value) => ({ value, label: MODE_NAMES[value], description: MODE_HINTS[value] }))}
          onValueChange={(next) => switchMode(router, next)}
        />
      </div>
    </Tooltip>
  );
}
