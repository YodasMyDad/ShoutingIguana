// Ctrl+F, "Filter this grid" (docs/v2-ui-ia.md, Keyboard, "Everywhere"): one behaviour on every screen whose grid has
// a field that narrows it (URLs and one sitemap's URLs: their search; Links: its search; Issues: the checks' filter).
// The key puts the keyboard in that field with its text selected, from anywhere on the screen, a field included, and
// the palette lists it under the screen's own words.
import type { RefObject } from 'react';
import type { Command, CommandGroup } from '../keyboard/registry';

export interface SearchCommandOptions {
  id: string;
  /** What the palette says: "Search URLs and titles", "Filter the checks". */
  title: string;
  group: CommandGroup;
  /** The field the key goes to. */
  field: RefObject<HTMLInputElement | null>;
  /** More words the palette matches, beyond the ones every grid's search has. */
  keywords?: readonly string[];
  enabled?: () => boolean;
}

/** The screen's Ctrl+F: its field focused, its text selected so typing replaces it. */
export function searchCommand({ id, title, group, field, keywords = [], enabled }: SearchCommandOptions): Command {
  return {
    id,
    title,
    group,
    keys: ['mod+f'],
    inInputs: true,
    keywords: ['find', 'filter this grid', 'search', ...keywords],
    ...(enabled ? { enabled } : {}),
    run: () => {
      field.current?.focus();
      field.current?.select();
    },
  };
}
