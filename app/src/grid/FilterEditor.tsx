import { useId, useMemo, useState, type ReactNode, type SyntheticEvent } from 'react';
import type { GridFilter } from '../api/grids';
import { Button } from '../ui/Button';
import { Checkbox } from '../ui/Checkbox';
import { Select, type SelectOption } from '../ui/Select';
import { TextField } from '../ui/TextField';
import {
  draftOf,
  filterOf,
  inputFor,
  newDraft,
  opWords,
  opsFor,
  withColumn,
  type DraftField,
  type DraftOp,
  type FilterColumn,
  type FilterDraft,
  type FilterGroup,
} from './filterEditing';

export interface FilterEditorProps {
  /** Every column a filter can be on, in the order to list them. */
  columns: readonly FilterColumn[];
  /** The columns' groups, in order ("The link", "Its source page", "Its target"). One group needs no choosing. */
  groups: readonly FilterGroup[];
  /** The filter being changed; none for a new one. */
  filter?: GridFilter | undefined;
  /** The column a new filter starts on; the first that can be filtered on otherwise. */
  startColumn?: string | undefined;
  /**
   * What the engine said of the filter being changed when it wouldn't run it (a pattern it refuses), in its own
   * words: shown under the value it's about until the filter is changed.
   */
  refusal?: string | undefined;
  onApply: (filter: GridFilter) => void;
}

/** A field of the editor with its name beside it. */
function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-3">
      <span className="text-13 text-text-secondary">{label}</span>
      {children}
    </div>
  );
}

/**
 * A filter's editor: what it's on, how it compares, and with what, by the column's type (a number, a range, text, a
 * pattern, some of an enum's values, yes or no). It goes in a popover from the filter's chip or from "Add filter".
 * Nothing is applied until Apply (or Enter), and what's wrong is said under the field it's wrong in. What the editor
 * can tell is wrong (no number, an empty range), it says before the engine is asked; what only the engine can judge
 * (a pattern) is applied, and comes back as `refusal` if the engine won't run it.
 */
export function FilterEditor({ columns, groups, filter, startColumn, refusal, onApply }: FilterEditorProps) {
  const formId = useId();
  const byId = useMemo(() => new Map(columns.map((column) => [column.id, column])), [columns]);
  const [draft, setDraft] = useState<FilterDraft | null>(() => {
    const editing = filter ? draftOf(filter, byId.get(filter.column)) : null;
    if (editing) return editing;
    const first = byId.get(startColumn ?? '') ?? columns.find((column) => !column.unavailable) ?? columns[0];
    return first ? newDraft(first) : null;
  });
  const [problem, setProblem] = useState<{ field: DraftField; error: string } | null>(null);
  /** The engine's sentence stands until the filter it's about is changed. */
  const [changed, setChanged] = useState(false);
  const column = draft ? byId.get(draft.column) : undefined;
  if (!draft || !column) return <p className="text-13 text-text-secondary">This view has no columns to filter on.</p>;

  const change = (next: FilterDraft) => {
    setDraft(next);
    setProblem(null);
    setChanged(true);
  };
  const groupOptions: SelectOption<string>[] = groups
    .filter((group) => columns.some((candidate) => candidate.group === group.id))
    .map((group) => ({ value: group.id, label: group.title }));
  const columnOptions: SelectOption<string>[] = columns
    .filter((candidate) => candidate.group === column.group)
    .map((candidate) => ({
      value: candidate.id,
      label: candidate.unavailable
        ? `${candidate.shortName ?? candidate.name} (${candidate.unavailable})`
        : (candidate.shortName ?? candidate.name),
      disabled: candidate.unavailable !== undefined,
    }));
  const opOptions: SelectOption<DraftOp>[] = opsFor(column.type).map((op) => ({
    value: op,
    label: opWords(op, column.type),
  }));
  const input = inputFor(draft.op, column.type);
  const refused = refusal !== undefined && !changed ? refusal : undefined;
  /** The field the engine's sentence goes under: the one value, where the filter has one. */
  const refusedField: DraftField | null = input === 'text' || input === 'number' ? 'text' : null;
  const errorOf = (field: DraftField) =>
    problem?.field === field ? problem.error : field === refusedField ? refused : undefined;

  const chooseGroup = (group: string) => {
    const inGroup = columns.filter((candidate) => candidate.group === group);
    const first = inGroup.find((candidate) => !candidate.unavailable) ?? inGroup[0];
    if (first) change(withColumn(draft, first));
  };
  const chooseColumn = (id: string) => {
    const next = byId.get(id);
    if (next) change(withColumn(draft, next));
  };
  const submit = (event: SyntheticEvent<HTMLFormElement>) => {
    event.preventDefault();
    const result = filterOf(draft, column);
    if ('filter' in result) onApply(result.filter);
    else setProblem(result);
  };
  const toggleValue = (value: string, on: boolean) =>
    change({ ...draft, values: on ? [...draft.values, value] : draft.values.filter((other) => other !== value) });

  return (
    <form id={formId} onSubmit={submit} noValidate className="flex flex-col gap-3">
      {groupOptions.length > 1 ? (
        <Row label="About">
          <Select
            label="About"
            options={groupOptions}
            value={column.group}
            onValueChange={chooseGroup}
            variant="secondary"
            size="sm"
            align="end"
          />
        </Row>
      ) : null}
      <Row label="Column">
        <Select
          label="Column"
          options={columnOptions}
          value={column.id}
          onValueChange={chooseColumn}
          variant="secondary"
          size="sm"
          align="end"
        />
      </Row>
      <Row label="Condition">
        <Select
          label="Condition"
          options={opOptions}
          value={draft.op}
          onValueChange={(op) => change({ ...draft, op })}
          variant="secondary"
          size="sm"
          align="end"
        />
      </Row>
      {input === 'text' || input === 'number' ? (
        <TextField
          label={input === 'number' ? 'Number' : draft.op === 'regex' || draft.op === 'notRegex' ? 'Pattern' : 'Text'}
          size="sm"
          inputMode={input === 'number' ? 'decimal' : undefined}
          mono={draft.op === 'regex' || draft.op === 'notRegex'}
          value={draft.text}
          onChange={(event) => change({ ...draft, text: event.target.value })}
          error={errorOf('text')}
          // Changing a filter starts in its value, which is what's changed most (and where the engine's sentence is,
          // read with the field, when it refused the filter). A new filter starts at its first field.
          {...(filter ? { 'data-autofocus': '' } : {})}
          description={
            draft.op === 'regex' || draft.op === 'notRegex'
              ? 'A regular expression. It tells capitals from small letters unless it starts (?i).'
              : input === 'text' && draft.op !== 'eq' && draft.op !== 'ne'
                ? 'Capitals and small letters match each other.'
                : undefined
          }
        />
      ) : null}
      {input === 'range' ? (
        <div className="grid grid-cols-2 gap-2">
          <TextField
            label="From"
            size="sm"
            inputMode="decimal"
            value={draft.min}
            onChange={(event) => change({ ...draft, min: event.target.value })}
            error={errorOf('min')}
            {...(filter ? { 'data-autofocus': '' } : {})}
          />
          <TextField
            label="To"
            size="sm"
            inputMode="decimal"
            value={draft.max}
            onChange={(event) => change({ ...draft, max: event.target.value })}
            error={errorOf('max')}
          />
        </div>
      ) : null}
      {input === 'values' ? (
        <fieldset
          className="flex max-h-(--filter-values-height) flex-col gap-2 overflow-y-auto"
          aria-describedby={errorOf('values') ? `${formId}-values-error` : undefined}
        >
          <legend className="sr-only">{column.name}</legend>
          {column.values.map((value) => (
            <Checkbox
              key={value.value}
              label={value.label}
              checked={draft.values.includes(value.value)}
              onCheckedChange={(on) => toggleValue(value.value, on)}
            />
          ))}
          {errorOf('values') ? (
            <p id={`${formId}-values-error`} className="si-field-error" role="alert">
              {errorOf('values')}
            </p>
          ) : null}
        </fieldset>
      ) : null}
      {refused !== undefined && refusedField === null ? (
        <p className="si-field-error" role="alert">
          {refused}
        </p>
      ) : null}
      <div className="flex justify-end">
        <Button type="submit" variant="primary" size="sm">
          {filter ? 'Apply' : 'Add filter'}
        </Button>
      </div>
    </form>
  );
}
