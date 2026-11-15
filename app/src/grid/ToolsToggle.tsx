import { SlidersHorizontal } from 'lucide-react';
import { formatCountOf } from '../lib/format';
import { IconButton } from '../ui/IconButton';

export interface ToolsToggleProps {
  /** The toolbars are shown. */
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Filters in force, said in its name while the toolbars are folded, so what narrows the view is never hidden. */
  filters: number;
  /** Notes folded with them (why columns are empty, a narrowing to a links view's ends): said in its name too. */
  notes?: number | undefined;
  /** What the toolbars hold, in its name: "columns and filters". */
  what?: string | undefined;
}

/**
 * The button a short grid screen folds its toolbars behind (presets, facets, the filter bar): pressed while they show.
 * Its name says how many filters are in force and notes are folded with them, and a dot on it shows there are some.
 */
export function ToolsToggle({
  open,
  onOpenChange,
  filters,
  notes = 0,
  what = 'columns and filters',
}: ToolsToggleProps) {
  const held = [
    filters > 0 ? `${formatCountOf(filters, 'filter')} in force` : null,
    notes > 0 ? formatCountOf(notes, 'note') : null,
  ]
    .filter((part) => part !== null)
    .join(', ');
  const label = open ? `Hide the ${what}` : `Show the ${what}${held ? `: ${held}` : ''}`;
  return (
    <span className="relative inline-flex">
      <IconButton
        label={label}
        icon={SlidersHorizontal}
        size="sm"
        aria-expanded={open}
        onClick={() => onOpenChange(!open)}
      />
      {filters + notes > 0 && !open ? <span aria-hidden="true" className="si-tools-dot" /> : null}
    </span>
  );
}
