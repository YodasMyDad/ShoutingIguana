import { useParams, useRouter } from '@tanstack/react-router';
import { useMode } from '../mode/mode';
import { switchMode } from '../mode/switchMode';
import { Button } from '../ui/Button';
import { listOf } from '../crawl/options/words';
import { NavItem } from '../ui/NavItem';
import { SETTINGS_SECTIONS, sectionsFor } from './settings/registry';

/** How Simple's note names each Advanced-only section, mid-sentence. */
const IN_A_SENTENCE: Record<string, string> = {
  'crawl-defaults': 'crawl defaults',
  analysis: 'the checks’ limits',
  shortcuts: 'shortcuts',
  diagnostics: 'diagnostics',
};

/** "Crawl defaults, shortcuts and diagnostics are Advanced settings.": the sections there are, and only those. */
function advancedOnlyNote(ids: readonly string[]): string {
  const words = listOf(ids.map((id) => IN_A_SENTENCE[id] ?? id));
  return `${words.charAt(0).toUpperCase()}${words.slice(1)} ${ids.length === 1 ? 'is an Advanced setting' : 'are Advanced settings'}.`;
}

/**
 * App settings (docs/v2-ui-ia.md, Routes; docs/v2-ui-simple.md, Settings in each mode): the sections the mode lists,
 * each one file in `settings/sections/` (settings/registry.ts). Simple lists four, with a note saying where the rest
 * are. An address naming a section there's no file for opens General.
 */
export function SettingsScreen() {
  const { section = 'general' } = useParams({ from: '/settings/{-$section}' });
  const router = useRouter();
  const mode = useMode();
  const listed = sectionsFor(mode);
  const current =
    SETTINGS_SECTIONS.find((candidate) => candidate.id === section) ??
    SETTINGS_SECTIONS.find((candidate) => candidate.id === 'general') ??
    SETTINGS_SECTIONS[0];
  const Current = current?.Section;

  // "Show them in Advanced" goes with Simple: focus goes to the first section it brought into the list.
  const showInAdvanced = () => {
    switchMode(router, 'advanced');
    requestAnimationFrame(() => {
      const added = SETTINGS_SECTIONS.find((entry) => !listed.includes(entry));
      if (added) document.querySelector<HTMLElement>(`[data-settings-section="${added.id}"]`)?.focus();
    });
  };

  return (
    <div className="flex gap-8 px-(--page-gutter) py-6">
      <nav aria-label="Settings" className="flex w-(--settings-nav-width) shrink-0 flex-col gap-4">
        <ul className="flex flex-col gap-0.5">
          {listed.map((entry) => (
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
        {mode === 'simple' ? (
          <p className="border-t border-border-subtle px-2.5 pt-3 text-12 text-text-secondary">
            {advancedOnlyNote(SETTINGS_SECTIONS.filter((entry) => !listed.includes(entry)).map((entry) => entry.id))}{' '}
            <Button variant="ghost" size="sm" className="mt-1 -ml-2.5" onClick={showInAdvanced}>
              Show them in Advanced
            </Button>
          </p>
        ) : null}
      </nav>
      <div className="min-w-0 flex-1">{Current ? <Current /> : null}</div>
    </div>
  );
}
