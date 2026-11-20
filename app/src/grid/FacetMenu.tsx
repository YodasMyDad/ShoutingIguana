import { ChevronDown } from 'lucide-react';
import { formatCount } from '../lib/format';
import { Button } from '../ui/Button';
import { DropdownMenu, MenuCheckboxItem, MenuItem, MenuLabel, MenuSeparator } from '../ui/Menu';

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
  /**
   * Why these values can't be counted now, in words ("Counted by the analysis: this crawl hasn’t been analysed yet"):
   * the menu says it in place of the values. A facet the analysis counts, on a crawl without one.
   */
  unavailable?: string | undefined;
  /** A value was chosen: the view narrows to it. */
  onChoose: (value: Value) => void;
  /**
   * The facet takes several values at once: each is a tick-box, the menu stays open while they're ticked, and the
   * counts are of the view without this facet's own filter (the view reads them so), so a second value can be added
   * after a first. `ticked` are the keys in force; `onToggle` hears each change. `onChoose` isn't called.
   */
  multi?:
    | {
        ticked: readonly string[];
        onToggle: (value: Value, ticked: boolean) => void;
        /** Choices that stand for several values ("Not indexable, any reason"), listed first. */
        shortcuts?: readonly { key: string; words: string; count: number; ticked: boolean; onChoose: () => void }[];
      }
    | undefined;
  /** Whether the menu is open, when something other than its button says (a story). */
  open?: boolean | undefined;
  /**
   * The menu opened or closed: the view counts the values while it's open, and not before (a count is a pass over
   * the view's rows, which nobody sees with the menu closed).
   */
  onOpenChange?: ((open: boolean) => void) | undefined;
}

/**
 * A facet as a quick filter: a menu of a column's values, each with how many of the rows in view have it. Choosing
 * one narrows the view to exactly that many. The counts are of the view as it is filtered now, so they add up to it.
 * They are the view's to read, and it reads them when a menu opens (`onOpenChange`): until they come the menu says
 * "Counting…", and the button needs none of them to draw.
 */
export function FacetMenu<Value extends FacetMenuValue>({
  title,
  noun,
  values,
  matching,
  failed = false,
  onRetry,
  onChoose,
  multi,
  open,
  onOpenChange,
  unavailable,
}: FacetMenuProps<Value>) {
  const countOf = (count: number, applied: boolean) => (
    <>
      <span aria-hidden="true">{formatCount(count)}</span>
      <span className="sr-only">
        , {formatCount(count)} {count === 1 ? noun : `${noun}s`}
        {applied ? ', the filter in force' : ''}
      </span>
    </>
  );
  return (
    <DropdownMenu
      {...(open === undefined ? {} : { open })}
      {...(onOpenChange ? { onOpenChange } : {})}
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
      {unavailable ? (
        <MenuItem disabled>{unavailable}</MenuItem>
      ) : failed ? (
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
      ) : multi ? (
        <>
          {(multi.shortcuts ?? []).map((shortcut) => (
            <MenuCheckboxItem
              key={shortcut.key}
              checked={shortcut.ticked}
              onCheckedChange={shortcut.onChoose}
              detail={countOf(shortcut.count, false)}
            >
              {shortcut.words}
            </MenuCheckboxItem>
          ))}
          {(multi.shortcuts ?? []).length > 0 ? <MenuSeparator /> : null}
          {values.map((value) => (
            <MenuCheckboxItem
              key={value.key}
              checked={multi.ticked.includes(value.key)}
              onCheckedChange={(ticked) => multi.onToggle(value, ticked)}
              detail={countOf(value.count, false)}
            >
              {value.words}
            </MenuCheckboxItem>
          ))}
        </>
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
