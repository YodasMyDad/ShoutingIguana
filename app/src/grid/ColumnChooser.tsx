import { Columns3 } from 'lucide-react';
import { useId, useRef, useState } from 'react';
import { Button } from '../ui/Button';
import { Checkbox } from '../ui/Checkbox';
import { Popover } from '../ui/Popover';
import { SearchField } from '../ui/SearchField';

export interface ChooserColumn {
  id: string;
  /** Its name under its group's heading: "Status" under "Its target". */
  name: string;
  /** What the search also matches: its full name ("Target status"). */
  keywords?: string | undefined;
  /** Said after its name when it has no values now: "needs an analysis". It can still be shown. */
  note?: string | undefined;
}

export interface ChooserGroup {
  id: string;
  title: string;
  columns: readonly ChooserColumn[];
}

export interface ColumnChooserProps {
  groups: readonly ChooserGroup[];
  /** The columns shown, in order. */
  shown: readonly string[];
  /** A column ticked goes last; one unticked goes. */
  onChange: (shown: string[]) => void;
  /** The view's own set, for "Reset to the default columns". */
  defaults: readonly string[];
  open?: boolean | undefined;
  onOpenChange?: ((open: boolean) => void) | undefined;
  /** The command that opens it, for the button's tooltip and keys. */
  command?: string | undefined;
}

const sameColumns = (a: readonly string[], b: readonly string[]) =>
  a.length === b.length && a.every((id, index) => id === b[index]);

/**
 * The grid's column chooser: every column the view can show, grouped, each with a tick, and a search for a grid with
 * a hundred of them. A tick applies at once, the grid behind following; the last column shown can't be unticked, as
 * a grid with none says nothing.
 */
export function ColumnChooser({ groups, shown, onChange, defaults, open, onOpenChange, command }: ColumnChooserProps) {
  const id = useId();
  const findRef = useRef<HTMLInputElement>(null);
  const [find, setFind] = useState('');
  const term = find.trim().toLowerCase();
  const matches = (column: ChooserColumn) =>
    term === '' || `${column.name} ${column.keywords ?? ''}`.toLowerCase().includes(term);
  const visible = groups
    .map((group) => ({ ...group, columns: group.columns.filter(matches) }))
    .filter((group) => group.columns.length > 0);
  const toggle = (column: string, on: boolean) =>
    onChange(on ? [...shown.filter((other) => other !== column), column] : shown.filter((other) => other !== column));
  return (
    <Popover
      trigger={
        <Button variant="ghost" size="sm" icon={Columns3} command={command}>
          Columns
        </Button>
      }
      title="Columns"
      align="end"
      {...(open === undefined ? {} : { open })}
      {...(onOpenChange ? { onOpenChange } : {})}
    >
      <div className="flex flex-col gap-3">
        <SearchField
          ref={findRef}
          label="Find a column"
          placeholder="Find a column"
          size="sm"
          value={find}
          onChange={(event) => setFind(event.target.value)}
          onClear={() => setFind('')}
        />
        <div className="flex max-h-(--column-list-height) flex-col gap-3 overflow-y-auto">
          {visible.length === 0 ? (
            <p className="text-13 text-text-secondary">No column is called that.</p>
          ) : (
            visible.map((group) => (
              <fieldset key={group.id} className="flex flex-col gap-1" aria-labelledby={`${id}-${group.id}`}>
                <p id={`${id}-${group.id}`} className="text-12 font-medium text-text-muted">
                  {group.title}
                </p>
                {group.columns.map((column) => {
                  const on = shown.includes(column.id);
                  return (
                    <Checkbox
                      key={column.id}
                      label={column.name}
                      description={column.note}
                      checked={on}
                      disabled={on && shown.length === 1}
                      onCheckedChange={(next) => toggle(column.id, next)}
                    />
                  );
                })}
              </fieldset>
            ))
          )}
        </div>
        <div className="flex justify-end">
          <Button
            size="sm"
            disabled={sameColumns(shown, defaults)}
            onClick={() => {
              // The button has nothing left to do once it's pressed: the keyboard goes on to the list's search.
              findRef.current?.focus();
              onChange([...defaults]);
            }}
          >
            Reset to the default columns
          </Button>
        </div>
      </div>
    </Popover>
  );
}
