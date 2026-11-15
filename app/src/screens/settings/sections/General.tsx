import { useRouter } from '@tanstack/react-router';
import { useShortcut } from '../../../keyboard/registry';
import { MODE_NAMES, MODES, useMode, type Mode } from '../../../mode/mode';
import { switchMode } from '../../../mode/switchMode';
import type { Density, ThemePreference } from '../../../settings/schema';
import { updateSettings, useSettings } from '../../../settings/settingsStore';
import { ChoiceCards } from '../../../ui/ChoiceCards';
import { SegmentedControl } from '../../../ui/SegmentedControl';
import { SectionPage, Setting } from '../SectionPage';

export const id = 'general';
export const title = 'General';
export const order = 10;

const MODE_LINES: Record<Mode, string> = {
  simple: 'A score, what to fix and a report, in plain words.',
  advanced: 'Every address, link, filter and setting, in technical terms.',
};

const DENSITY_HINTS: Record<Density, string> = {
  compact: 'Rows of 28 px',
  standard: 'Rows of 32 px',
  comfortable: 'Rows of 40 px',
};

/**
 * General: the mode (the switch in the top bar, the same value), the theme and the grids' density, the same in both
 * modes. Each applies at once and is remembered.
 */
export function Section() {
  const router = useRouter();
  const mode = useMode();
  const theme = useSettings((settings) => settings.theme);
  const density = useSettings((settings) => settings.density);
  const switchKeys = useShortcut('app.mode');
  return (
    <SectionPage
      title="General"
      purpose="How much the app shows, and how it looks. Changes apply at once and are remembered."
    >
      <ChoiceCards<Mode>
        legend="Mode"
        description={`The same site check either way, with the same checks and the same numbers. This is the switch at the top right of every screen${switchKeys ? ` (${switchKeys})` : ''}.`}
        columns={2}
        options={MODES.map((value) => ({ value, title: MODE_NAMES[value], text: MODE_LINES[value] }))}
        value={mode}
        onValueChange={(next) => switchMode(router, next)}
      />
      <Setting label="Theme" help="Light, dark, or whichever your computer is using.">
        <SegmentedControl<ThemePreference>
          label="Theme"
          value={theme}
          options={[
            { value: 'system', label: 'Follow the system' },
            { value: 'light', label: 'Light' },
            { value: 'dark', label: 'Dark' },
          ]}
          onValueChange={(value) => updateSettings({ theme: value })}
        />
      </Setting>
      <Setting label="Density" help={`How tall the rows of every grid are. ${DENSITY_HINTS[density]}.`}>
        <SegmentedControl<Density>
          label="Density"
          value={density}
          options={(Object.keys(DENSITY_HINTS) as Density[]).map((value) => ({
            value,
            label: value === 'compact' ? 'Compact' : value === 'standard' ? 'Standard' : 'Comfortable',
            description: DENSITY_HINTS[value],
          }))}
          onValueChange={(value) => updateSettings({ density: value })}
        />
      </Setting>
    </SectionPage>
  );
}
