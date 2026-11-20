// How a grouped check's rows are shown when it's opened, and what its grouped view is called. Pure.
import type { CheckEntry } from './checkList';
import type { CheckView } from './issuesPlace';

/** The view a grouped check opens in (A-06): its groups where grouping saves work, else the list. */
export function defaultView(entry: Pick<CheckEntry, 'check' | 'templateCauses'>): CheckView {
  const { check } = entry;
  if (check.grouping === 'relatedUrl' && check.id.startsWith('image-')) return 'groups';
  return (entry.templateCauses ?? 0) > 0 ? 'groups' : 'list';
}

/** What the grouped view groups by, on its button: "By image", "By shared text", "By linked URL", "By cause". */
export function groupedLabel(check: CheckEntry['check']): string {
  if (check.grouping !== 'relatedUrl') return 'Grouped by cause';
  if (check.id.startsWith('image-')) return 'Grouped by image';
  if (check.id.includes('duplicate')) return 'Grouped by what they share';
  return 'Grouped by linked URL';
}
