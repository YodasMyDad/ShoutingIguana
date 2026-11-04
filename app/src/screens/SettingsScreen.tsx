import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from '@tanstack/react-router';
import clsx from 'clsx';
import { useEffect, useState, type ReactNode } from 'react';
import { healthQuery } from '../api/queries';
import { ShortcutList } from '../keyboard/ShortcutSheet';
import { isTauri } from '../lib/platform';
import type { Density, ThemePreference } from '../settings/schema';
import { updateSettings, useSettings } from '../settings/settingsStore';
import { ipc, type ShellInfo } from '../shell/ipc';
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
              <Link
                to="/settings/{-$section}"
                params={{ section: entry.id === 'general' ? undefined : entry.id }}
                aria-current={entry.id === current.id ? 'page' : undefined}
                className={clsx(
                  'flex h-(--nav-item-height) items-center rounded-md px-2.5 text-13',
                  entry.id === current.id ? 'si-current font-medium' : 'text-text-secondary hover:bg-surface-hover',
                )}
              >
                {entry.title}
              </Link>
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
  return (
    <fieldset className="flex flex-col gap-2">
      <legend className="pb-1 text-14 font-medium">{legend}</legend>
      {options.map((option) => (
        <label key={option.value} className="flex items-center gap-2 text-13">
          <input
            type="radio"
            name={legend}
            value={option.value}
            checked={value === option.value}
            onChange={() => onChange(option.value)}
          />
          {option.label}
          {option.hint ? <span className="text-text-muted">{option.hint}</span> : null}
        </label>
      ))}
    </fieldset>
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
      <dl className="grid max-w-(--content-max) grid-cols-(--facts-columns) gap-x-6 gap-y-1 text-13">
        <Fact label="App">
          {shell ? `${shell.appVersion}${shell.debug ? ' (debug build)' : ''}` : 'Running in a browser'}
        </Fact>
        <Fact label="Engine">{health.data ? health.data.engineVersion : health.error ? 'Not connected' : '…'}</Fact>
        <Fact label="Data folder">{health.data?.dataDir ?? '–'}</Fact>
        <Fact label="Logs">{shell?.logDir ?? health.data?.stateDir ?? '–'}</Fact>
      </dl>
    </ScreenStub>
  );
}

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <>
      <dt className="text-text-muted">{label}</dt>
      <dd className="font-mono text-12 break-all">{children}</dd>
    </>
  );
}
