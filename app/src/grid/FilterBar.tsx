import { useRef, useState, type ReactNode } from 'react';
import type { GridFilter } from '../api/grids';
import { Button } from '../ui/Button';
import { AddFilterChip, FilterChip } from '../ui/FilterChip';
import { Popover } from '../ui/Popover';
import { focusQuietly } from '../ui/quietFocus';
import { FilterEditor } from './FilterEditor';
import {
  canEditFilter,
  tooManyFiltersWords,
  type FilterColumn,
  type FilterGroup,
  type FilterRefusal,
} from './filterEditing';
import { filterSentences, type FilterWords } from './filterSentence';

/** The filters without one of them, or without one's group (a check's group is a chip of its own). */
function without(filters: readonly GridFilter[], index: number, part: number): GridFilter[] {
  if (part === 0) return filters.filter((_, other) => other !== index);
  return filters.map((other, at) => {
    if (at !== index || !('group' in other)) return other;
    const { group: _group, ...rest } = other;
    return rest;
  });
}

export interface FilterChipsProps {
  filters: readonly GridFilter[];
  onChange: (filters: GridFilter[]) => void;
  /** The view's words for its columns and values: nothing shows an engine id. */
  words: FilterWords;
  /** A check's group's related URL in words; "another URL" without it. */
  relatedUrl?: ((urlId: number) => string) | undefined;
  /**
   * What a filter can be on. With it, a chip the editor can hold opens it (a popover from the chip), and the filter
   * it gives takes the chip's place. Without it, chips are read and removed, nothing more.
   */
  editor?: { columns: readonly FilterColumn[]; groups: readonly FilterGroup[] } | undefined;
  /** Where focus goes when a chip's editor closes and the chip has gone. */
  focusFallback?: (() => HTMLElement | null | undefined) | undefined;
  /**
   * The filter whose editor is open, by its place, when the view opens it too ("Change the filter", beside what the
   * engine said of it); null for none. Left out, the chips keep it themselves.
   */
  editing?: number | null | undefined;
  onEditingChange?: ((index: number | null) => void) | undefined;
  /** The filter the engine wouldn't run, with its sentence: its editor says it under the value until it's changed. */
  refused?: FilterRefusal | null | undefined;
}

/**
 * A view's filters as chips that read as sentences ("Status class is 4xx", "Target is on another site"), each
 * removable, and editable where the view says what its columns are. The chips alone: `FilterBar` adds the rest.
 */
export function FilterChips({
  filters,
  onChange,
  words,
  relatedUrl,
  editor,
  focusFallback,
  editing: editingGiven,
  onEditingChange,
  refused,
}: FilterChipsProps) {
  // The editor that's open is of the filter it was opened on, not only of its place: when the view's filters change
  // under it (back, forward, a link), the place may be another filter's, whose editor nobody asked for.
  const [editingHere, setEditingHere] = useState<{ index: number; filter: string } | null>(null);
  const stillThere =
    editingHere !== null && JSON.stringify(filters[editingHere.index]) === editingHere.filter
      ? editingHere.index
      : null;
  const editing = editingGiven === undefined ? stillThere : editingGiven;
  const setEditing = (index: number | null) => {
    setEditingHere(index === null ? null : { index, filter: JSON.stringify(filters[index]) });
    onEditingChange?.(index);
  };
  return (
    <>
      {filters.flatMap((filter, index) =>
        filterSentences(filter, words, relatedUrl).map((sentence, part) => {
          // Only the filter itself is edited, never its group's chip, and only one the editor has fields for.
          const editable = editor !== undefined && part === 0 && canEditFilter(filter, editor.columns);
          const chip = (
            <FilterChip
              key={`${index}-${part}`}
              field={sentence.field}
              op={sentence.op}
              value={sentence.value}
              truncate
              onEdit={editable ? () => setEditing(index) : undefined}
              onRemove={() => onChange(without(filters, index, part))}
            />
          );
          if (!editable) return chip;
          return (
            <Popover
              key={`${index}-${part}`}
              anchor={chip}
              title="Change the filter"
              open={editing === index}
              onOpenChange={(open) => setEditing(open ? index : null)}
              focusOnClose={focusFallback}
            >
              <FilterEditor
                columns={editor.columns}
                groups={editor.groups}
                filter={filter}
                refusal={refused?.index === index ? refused.message : undefined}
                onApply={(next) => {
                  setEditing(null);
                  onChange(filters.map((other, at) => (at === index ? next : other)));
                }}
              />
            </Popover>
          );
        }),
      )}
    </>
  );
}

export interface FilterBarProps extends FilterChipsProps {
  /** Whether "Add filter" has its editor open, when the view opens it too (a command, a key). */
  adding?: boolean | undefined;
  onAddingChange?: ((adding: boolean) => void) | undefined;
  /** Removes every filter at once. */
  onClear?: (() => void) | undefined;
  /**
   * The most filters the view takes (the contract's 20: a route keeps no more). At the limit "Add filter" says so in
   * place of its editor, so a filter is never typed out only to be dropped.
   */
  maxFilters?: number | undefined;
  /** Quick filters that come before the chips (a view's facets). */
  children?: ReactNode;
  className?: string | undefined;
}

/**
 * The grid's filter bar: its quick filters, the filters in force as chips, "Add filter" where the view says what a
 * filter can be on, and "Clear filters". One group, named "Filters", that wraps onto more lines as it fills.
 */
export function FilterBar({
  filters,
  onChange,
  words,
  relatedUrl,
  editor,
  editing,
  onEditingChange,
  refused,
  adding,
  onAddingChange,
  onClear,
  maxFilters,
  children,
  className,
}: FilterBarProps) {
  const addRef = useRef<HTMLButtonElement>(null);
  const [addingHere, setAddingHere] = useState(false);
  const isAdding = adding ?? addingHere;
  const setAdding = (next: boolean) => {
    setAddingHere(next);
    onAddingChange?.(next);
  };
  return (
    <div role="group" aria-label="Filters" className={className ?? 'flex flex-wrap items-center gap-2'}>
      {children}
      <FilterChips
        filters={filters}
        onChange={onChange}
        words={words}
        relatedUrl={relatedUrl}
        editor={editor}
        focusFallback={() => addRef.current}
        editing={editing}
        onEditingChange={onEditingChange}
        refused={refused}
      />
      {editor ? (
        <Popover trigger={<AddFilterChip ref={addRef} />} title="Add a filter" open={isAdding} onOpenChange={setAdding}>
          {maxFilters !== undefined && filters.length >= maxFilters ? (
            <p className="text-13 text-text-secondary">{tooManyFiltersWords(maxFilters)}</p>
          ) : (
            <FilterEditor
              columns={editor.columns}
              groups={editor.groups}
              onApply={(next) => {
                setAdding(false);
                onChange([...filters, next]);
              }}
            />
          )}
        </Popover>
      ) : null}
      {onClear && filters.length > 0 ? (
        <Button
          variant="ghost"
          size="sm"
          onClick={() => {
            // The button goes with the last filter: the keyboard moves to what's left of the bar.
            focusQuietly(addRef.current);
            onClear();
          }}
        >
          Clear filters
        </Button>
      ) : null}
    </div>
  );
}
