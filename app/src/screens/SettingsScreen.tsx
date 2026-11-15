import { useParams } from '@tanstack/react-router';
import { NavItem } from '../ui/NavItem';
import { SETTINGS_SECTIONS } from './settings/registry';

/**
 * App settings (docs/v2-ui-ia.md, Routes; docs/v2-ui-simple.md, Settings in each mode): every section, each one file
 * in `settings/sections/` (settings/registry.ts), the same in both modes, as the owner asked: "the Simple and Advanced
 * modes are just for the UI for the user of using the app, not for settings". An address naming a section there's no
 * file for opens General.
 */
export function SettingsScreen() {
  const { section = 'general' } = useParams({ from: '/settings/{-$section}' });
  const current =
    SETTINGS_SECTIONS.find((candidate) => candidate.id === section) ??
    SETTINGS_SECTIONS.find((candidate) => candidate.id === 'general') ??
    SETTINGS_SECTIONS[0];
  const Current = current?.Section;

  return (
    <div className="flex gap-8 px-(--page-gutter) py-6">
      <nav aria-label="Settings" className="flex w-(--settings-nav-width) shrink-0 flex-col gap-4">
        <ul className="flex flex-col gap-0.5">
          {SETTINGS_SECTIONS.map((entry) => (
            <li key={entry.id}>
              <NavItem
                to="/settings/{-$section}"
                params={{ section: entry.id === 'general' ? undefined : entry.id }}
                activeOptions={{ exact: true }}
                aria-current={entry.id === current?.id ? 'page' : undefined}
                data-settings-section={entry.id}
                label={entry.title}
              />
            </li>
          ))}
        </ul>
      </nav>
      <div className="min-w-0 flex-1">{Current ? <Current /> : null}</div>
    </div>
  );
}
