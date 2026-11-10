import { ChevronDown } from 'lucide-react';
import { formatCount } from '../lib/format';
import { Button } from '../ui/Button';
import { DropdownMenu, MenuItem, MenuLabel, MenuSeparator } from '../ui/Menu';

export interface FacetMenuValue {
  key: string;
  /** The value in words: "Hyperlink". */
  words: string;
  count: number;
  /** The view already has this value's filter: listed with its count, not to be chosen again. */
  applied?: boolean | undefined;
}

export interface FacetMenuProps<Value extends FacetMenuValue> {
  /** What's counted by: "Kind". */
  title: string;
  /** What the counts are of, for a count's name: "link". */
  noun: string;
  /** The values with rows, each with its count. Null while they're counted. */
  values: readonly Value[] | null;
  /** How many rows the counts are of, said over them ("of 21,914 links"); left out until it's known. */
  matching?: number | null | undefined;
  /** The counts couldn't be read: the menu says so, with Try again. */
  failed?: boolean | undefined;
  onRetry?: (() => void) | undefined;
  /** A value was chosen: the view narrows to it. */
  onChoose: (value: Value) => void;
  /** Whether the menu is open, when something other than its button says (a story). */
  open?: boolean | undefined;
}

/**
 * A facet as a quick filter: a menu of a column's values, each with how many of the rows in view have it. Choosing
 * one narrows the view to exactly that many. The counts are of the view as it is filtered now, so they add up to it.
 */
export function FacetMenu<Value extends FacetMenuValue>({
  title,
  noun,
  values,
  matching,
  failed = false,
  onRetry,
  onChoose,
  open,
}: FacetMenuProps<Value>) {
  return (
    <DropdownMenu
      {...(open === undefined ? {} : { open })}
      trigger={
        <Button variant="ghost" size="sm" iconEnd={ChevronDown} aria-label={`Filter by ${title.toLowerCase()}`}>
          {title}
        </Button>
      }
    >
      <MenuLabel>
        {matching === null || matching === undefined
          ? title
          : `${title}, of ${formatCount(matching)} ${matching === 1 ? noun : `${noun}s`}`}
      </MenuLabel>
      {failed ? (
        <>
          <MenuItem disabled>The counts couldn’t be read.</MenuItem>
          {onRetry ? (
            <>
              <MenuSeparator />
              <MenuItem onSelect={onRetry}>Try again</MenuItem>
            </>
          ) : null}
        </>
      ) : values === null ? (
        <MenuItem disabled>Counting…</MenuItem>
      ) : values.length === 0 ? (
        <MenuItem disabled>Nothing to count.</MenuItem>
      ) : (
        values.map((value) => (
          <MenuItem
            key={value.key}
            disabled={value.applied === true}
            onSelect={() => onChoose(value)}
            detail={
              <>
                <span aria-hidden="true">{formatCount(value.count)}</span>
                <span className="sr-only">
                  , {formatCount(value.count)} {value.count === 1 ? noun : `${noun}s`}
                  {value.applied ? ', the filter in force' : ''}
                </span>
              </>
            }
          >
            {value.words}
          </MenuItem>
        ))
      )}
    </DropdownMenu>
  );
}
