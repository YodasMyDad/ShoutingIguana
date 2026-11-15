// Settings' sections (docs/v2-ui-ia.md, Routes: `/settings/:section?`; docs/v2-ui-simple.md, Settings in each mode).
// Each section is one file in `./sections/`, found here by its name: adding a section is adding its file, and nothing
// else. A section file exports four things:
//
//   export const id = 'analysis';     // its place in the address: /settings/analysis
//   export const title = 'Analysis';  // its name in the list
//   export const order = 30;          // where it's listed: General 10, Crawl defaults 20, Analysis 30, Data folder 40,
//                                     // Shortcuts 50, Diagnostics 60, Updates 70, About 80
//   export function Section() { … }   // the section itself
//
// Both modes list every section, the same: the owner's word, "the Simple and Advanced modes are just for the UI for
// the user of using the app, not for settings. I still need the settings whatever version they're using." A section
// says the same in both modes too. Two things read the mode, and aren't settings: General's Mode choice, which is the
// mode, and Shortcuts' list, which is the keys that work in the mode shown (the `?` sheet's list).
import type { ComponentType } from 'react';

export interface SettingsSection {
  id: string;
  title: string;
  order: number;
  Section: ComponentType;
}

const modules = import.meta.glob<Partial<SettingsSection>>('./sections/*.tsx', { eager: true });

function isSection(module: Partial<SettingsSection>): module is SettingsSection {
  return (
    typeof module.id === 'string' &&
    typeof module.title === 'string' &&
    typeof module.order === 'number' &&
    typeof module.Section === 'function'
  );
}

/** Every section, in order: the list in both modes. */
export const SETTINGS_SECTIONS: readonly SettingsSection[] = Object.values(modules)
  .filter(isSection)
  .sort((a, b) => a.order - b.order);
