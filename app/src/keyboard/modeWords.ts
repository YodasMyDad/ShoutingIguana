// What the palette and the `?` sheet say in each mode (docs/v2-ui-simple.md, Simple's words): Simple's words for the
// groups, and in Simple, none of what only Advanced has (the grids' keys, the inspector's). Pure.
import type { Mode } from '../mode/mode';
import type { Command } from './registry';

/** Simple's word for a group, where Advanced's is technical. */
const SIMPLE_GROUPS: Readonly<Record<string, string>> = {
  Crawl: 'Site check',
  'Recent crawls': 'Your sites',
};

/** A group's name, as the mode says it. */
export function groupTitle(group: string, mode: Mode): string {
  return mode === 'simple' ? (SIMPLE_GROUPS[group] ?? group) : group;
}

/** Whether a command is listed in the mode: Simple has no grid and no inspector of its own. */
export function listedIn(command: Pick<Command, 'id' | 'group'>, mode: Mode): boolean {
  return (
    mode === 'advanced' ||
    !(command.group === 'In grids' || command.group === 'Inspector' || command.id.startsWith('inspector.'))
  );
}
