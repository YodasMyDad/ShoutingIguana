import { useQuery } from '@tanstack/react-query';
import { useParams } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import { healthQuery } from '../api/queries';
import { ShortcutList } from '../keyboard/ShortcutSheet';
import { isTauri } from '../lib/platform';
import type { Density, ThemePreference } from '../settings/schema';
import { updateSettings, useSettings } from '../settings/settingsStore';
import { ipc, type ShellInfo } from '../shell/ipc';
import { Fact, Facts } from '../ui/Facts';
import { NavItem } from '../ui/NavItem';
import { SegmentedControl } from '../ui/SegmentedControl';
import { ScreenStub } from './ScreenStub';

const SECTIONS = [
  { id: 'general', title: 'General' },
  { id: 'crawl-defaults', title: 'Crawl defaults' },
  { id: 'analysis', title: 'Analysis' },
  { id: 'data', title: 'Data folder' },
  { id: 'shortcuts', title: 'Shortcuts' },
  { id: 'diagnostics', title: 'Diagnostics' },
  { id: 'updates', title: 'Updates' },
  { id: 'about', title: 'About' },
] as const;

/** App settings (docs/v2-ui-ia.md: general, crawl defaults, analysis, data, shortcuts, diagnostics, updates, about). */
export function SettingsScreen() {
  const { section = 'general' } = useParams({ from: '/settings/{-$section}' });
  const current = SECTIONS.find((candidate) => candidate.id === section) ?? SECTIONS[0];
  return (
    <div className="flex gap-8 px-(--page-gutter) py-6">
      <nav aria-label="Settings" className="w-(--settings-nav-width) shrink-0">
        <ul className="flex flex-col gap-0.5">
          {SECTIONS.map((entry) => (
            <li key={entry.id}>
              <NavItem
                to="/settings/{-$section}"
                params={{ section: entry.id === 'general' ? undefined : entry.id }}
                activeOptions={{ exact: true }}
                aria-current={entry.id === current.id ? 'page' : undefined}
                label={entry.title}
              />
            </li>
          ))}
        </ul>
      </nav>
      <div className="min-w-0 flex-1">
        {current.id === 'general' ? <General /> : null}
        {current.id === 'shortcuts' ? (
          <ScreenStub title="Shortcuts" purpose="Every shortcut, from the same list the ? sheet and the menus use.">
            <ShortcutList />
          </ScreenStub>
        ) : null}
        {current.id === 'about' ? <About /> : null}
        {current.id !== 'general' && current.id !== 'shortcuts' && current.id !== 'about' ? (
          <ScreenStub title={current.title} purpose="This section arrives with its slice." />
        ) : null}
      </div>
    </div>
  );
}

function General() {
  const theme = useSettings((settings) => settings.theme);
  const density = useSettings((settings) => settings.density);
  return (
    <ScreenStub title="General" purpose="How the app looks. Changes apply at once and are remembered.">
      <Choice<ThemePreference>
        legend="Theme"
        value={theme}
        options={[
          { value: 'system', label: 'Follow the system' },
          { value: 'light', label: 'Light' },
          { value: 'dark', label: 'Dark' },
        ]}
        onChange={(value) => updateSettings({ theme: value })}
      />
      <Choice<Density>
        legend="Density"
        value={density}
        options={[
          { value: 'compact', label: 'Compact', hint: 'Rows of 28 px' },
          { value: 'standard', label: 'Standard', hint: 'Rows of 32 px' },
          { value: 'comfortable', label: 'Comfortable', hint: 'Rows of 40 px' },
        ]}
        onChange={(value) => updateSettings({ density: value })}
      />
    </ScreenStub>
  );
}

/** One setting chosen from a few, all in view: the segmented control, its legend, and what the choice means. */
function Choice<T extends string>({
  legend,
  value,
  options,
  onChange,
}: {
  legend: string;
  value: T;
  options: { value: T; label: string; hint?: string }[];
  onChange: (value: T) => void;
}) {
  const hint = options.find((option) => option.value === value)?.hint;
  return (
    <div className="flex flex-col items-start gap-2">
      {/* The control's own name is the legend: this is its visible copy. */}
      <span aria-hidden="true" className="text-14 font-medium">
        {legend}
      </span>
      <SegmentedControl<T> label={legend} value={value} options={options} onValueChange={onChange} />
      {hint ? <span className="text-12 text-text-muted">{hint}</span> : null}
    </div>
  );
}

function About() {
  const health = useQuery(healthQuery());
  const [shell, setShell] = useState<ShellInfo | null>(null);
  useEffect(() => {
    if (isTauri)
      void ipc
        .shellInfo()
        .then(setShell)
        .catch(() => undefined);
  }, []);
  return (
    <ScreenStub title="About" purpose="Shouting Iguana: an SEO crawler and site auditor.">
      <Facts auto className="max-w-(--content-max)">
        <Fact label="App" className="font-mono text-12">
          {shell ? `${shell.appVersion}${shell.debug ? ' (debug build)' : ''}` : 'Running in a browser'}
        </Fact>
        <Fact label="Engine" className="font-mono text-12">
          {health.data ? health.data.engineVersion : health.error ? 'Not connected' : '…'}
        </Fact>
        <Fact label="Data folder" className="font-mono text-12">
          {health.data?.dataDir ?? '–'}
        </Fact>
        <Fact label="Logs" className="font-mono text-12">
          {shell?.logDir ?? health.data?.stateDir ?? '–'}
        </Fact>
      </Facts>
    </ScreenStub>
  );
}
