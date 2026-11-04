import { EyeOff } from 'lucide-react';
import { useId, useRef, type KeyboardEvent, type Ref } from 'react';
import type { Severity } from '../../api/types';
import { formatCount, formatCountOf } from '../../lib/format';
import { Badge } from '../../ui/Badge';
import { Button } from '../../ui/Button';
import { EmptyState, FilteredEmptyState } from '../../ui/EmptyState';
import { FilterChip } from '../../ui/FilterChip';
import { ListHeading, ListRow } from '../../ui/ListRow';
import { Num } from '../../ui/Num';
import { RefreshLine } from '../../ui/ProgressBar';
import { SearchField } from '../../ui/SearchField';
import { SegmentedControl } from '../../ui/SegmentedControl';
import { SeverityMark } from '../../ui/SeverityMark';
import { Switch } from '../../ui/Switch';
import type { CheckEntry, CheckList } from './checkList';

export type SeverityFilter = Severity | 'all';

/** How a check was picked: a click (a new place, so back returns to the last), or the arrow keys (it replaces). */
export type SelectedBy = 'click' | 'key';

export interface ChecksListProps {
  list: CheckList;
  selectedId: string | undefined;
  onSelect: (checkId: string, by: SelectedBy) => void;
  /** Enter on a check: go to what it says (focus moves to the selected check). */
  onOpen: () => void;
  q: string;
  onQueryChange: (q: string) => void;
  severity: SeverityFilter;
  onSeverityChange: (severity: SeverityFilter) => void;
  showPassed: boolean;
  onShowPassedChange: (show: boolean) => void;
  showHidden: boolean;
  onShowHiddenChange: (show: boolean) => void;
  /** Every check the user hid, whatever the filters. */
  hiddenTotal: number;
  onClearFilters: () => void;
  /** The analysis is being read again behind what's shown. */
  refreshing?: boolean;
  filterRef?: Ref<HTMLInputElement>;
}

const SEVERITY_LABELS: Record<Severity, string> = { error: 'Errors', warning: 'Warnings', notice: 'Notices' };

/**
 * The checks list (docs/v2-ui-ia.md, Issues): the filter, the severity choice with its counts, then the checks by
 * category in catalog order. One ARIA listbox with a group per category: it's one stop for Tab, the arrow keys move
 * the selection (and the route), Home and End go to the ends, and Tab goes on to the selected check.
 */
export function ChecksList({
  list,
  selectedId,
  onSelect,
  onOpen,
  q,
  onQueryChange,
  severity,
  onSeverityChange,
  showPassed,
  onShowPassedChange,
  showHidden,
  onShowHiddenChange,
  hiddenTotal,
  onClearFilters,
  refreshing = false,
  filterRef,
}: ChecksListProps) {
  const id = useId();
  const options = useRef(new Map<string, HTMLDivElement>());
  const visible = list.visible;
  const selectedIndex = visible.findIndex((entry) => entry.check.id === selectedId);
  // The one row Tab lands on: the selected check, or the first when it isn't listed.
  const tabStop = visible[selectedIndex >= 0 ? selectedIndex : 0]?.check.id;

  const moveTo = (index: number) => {
    const entry = visible[Math.max(0, Math.min(visible.length - 1, index))];
    if (!entry) return;
    const element = options.current.get(entry.check.id);
    element?.focus();
    element?.scrollIntoView({ block: 'nearest' });
    if (entry.check.id !== selectedId) onSelect(entry.check.id, 'key');
  };

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.altKey || event.ctrlKey || event.metaKey) return;
    const from = selectedIndex >= 0 ? selectedIndex : -1;
    const step = { ArrowDown: 1, ArrowUp: -1 }[event.key];
    if (step !== undefined) moveTo(from < 0 ? 0 : from + step);
    else if (event.key === 'Home') moveTo(0);
    else if (event.key === 'End') moveTo(visible.length - 1);
    else if (event.key === 'PageDown') moveTo(from + 10);
    else if (event.key === 'PageUp') moveTo(from - 10);
    else return;
    event.preventDefault();
  };

  const filters = [
    q ? (
      <FilterChip key="q" field="Title" op="contains" value={q} size="sm" onRemove={() => onQueryChange('')} />
    ) : null,
    severity !== 'all' ? (
      <FilterChip
        key="severity"
        field="Severity"
        value={SEVERITY_LABELS[severity].toLowerCase()}
        size="sm"
        onRemove={() => onSeverityChange('all')}
      />
    ) : null,
  ];

  return (
    <>
      {refreshing ? <RefreshLine label="Reading the analysis again" /> : null}
      <div className="grid gap-3 border-b border-border p-4">
        <SearchField
          ref={filterRef}
          label="Filter checks"
          placeholder="Filter checks"
          shortcut="mod+f"
          value={q}
          onChange={(event) => onQueryChange(event.currentTarget.value)}
          onClear={() => onQueryChange('')}
          clearLabel="Clear the filter"
        />
        <SegmentedControl<SeverityFilter>
          label="Severity"
          fill
          value={severity}
          onValueChange={onSeverityChange}
          options={[
            { value: 'all', label: 'All', count: list.severityCounts.all },
            ...(['error', 'warning', 'notice'] as const).map((value) => ({
              value,
              label: SEVERITY_LABELS[value],
              icon: <SeverityMark severity={value} decorative />,
              iconOnly: true,
              count: list.severityCounts[value],
            })),
          ]}
        />
        <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
          <Switch
            label={
              list.passedCount === 1
                ? 'Show the check that passed'
                : `Show the ${formatCount(list.passedCount)} checks that passed`
            }
            checked={showPassed}
            onCheckedChange={onShowPassedChange}
            disabled={list.passedCount === 0}
          />
          {hiddenTotal > 0 ? (
            <Button
              size="sm"
              variant="ghost"
              icon={EyeOff}
              pressed={showHidden}
              onClick={() => onShowHiddenChange(!showHidden)}
            >
              {showHidden ? 'Hide the hidden checks' : `Show ${formatCountOf(hiddenTotal, 'hidden check')}`}
            </Button>
          ) : null}
        </div>
      </div>
      {list.filteredToNothing ? (
        <FilteredEmptyState
          title="No checks match these filters."
          filters={filters}
          onClear={onClearFilters}
          level="none"
          panel
        />
      ) : visible.length === 0 ? (
        <EmptyState title={list.passedCount > 0 ? 'Every check passed' : 'No checks to list'} level="none" panel>
          {list.hiddenCount > 0
            ? `${formatCountOf(list.hiddenCount, 'hidden check')} would show here.`
            : list.passedCount > 0
              ? 'Turn on “Show the checks that passed” to list them.'
              : 'This analysis ran no checks.'}
        </EmptyState>
      ) : (
        <div
          role="listbox"
          aria-label="Checks"
          // Focus is on the options (one of them is in the tab order), never on the list itself.
          tabIndex={-1}
          onKeyDown={onKeyDown}
          className="min-h-0 flex-1 overflow-auto px-2 pb-4"
        >
          {list.categories.map((category) => {
            const headingId = `${id}-${category.id}`;
            return (
              <div key={category.id} role="group" aria-labelledby={headingId}>
                <ListHeading
                  id={headingId}
                  level="none"
                  meta={category.passed > 0 ? `${formatCount(category.passed)} passed` : undefined}
                >
                  {category.title}
                </ListHeading>
                {category.entries.map((entry) => (
                  <CheckOption
                    key={entry.check.id}
                    entry={entry}
                    selected={entry.check.id === selectedId}
                    tabbable={entry.check.id === tabStop}
                    onSelect={() => onSelect(entry.check.id, 'click')}
                    onOpen={onOpen}
                    ref={(element) => {
                      if (element) options.current.set(entry.check.id, element);
                      else options.current.delete(entry.check.id);
                    }}
                  />
                ))}
              </div>
            );
          })}
        </div>
      )}
    </>
  );
}

function CheckOption({
  entry,
  selected,
  tabbable,
  onSelect,
  onOpen,
  ref,
}: {
  entry: CheckEntry;
  selected: boolean;
  tabbable: boolean;
  onSelect: () => void;
  onOpen: () => void;
  ref: Ref<HTMLDivElement>;
}) {
  const { check, outcome, count, skipReason } = entry;
  const skipped = outcome === 'skipped';
  const meta = skipped ? (
    <Badge variant="dashed">Skipped</Badge>
  ) : outcome === 'passed' ? (
    <span className="text-12 text-text-muted">Passed</span>
  ) : (
    <>
      <Num value={count ?? 0} />
      <span className="sr-only"> {count === 1 ? 'URL' : 'URLs'}</span>
    </>
  );
  return (
    <ListRow
      asChild
      muted={skipped || outcome === 'passed' || entry.hidden}
      // The mark's word is part of the row's name ("Error Internal URL returns 4xx 9 URLs"); a skipped or passed
      // check's own word is on the right already.
      icon={<SeverityMark severity={outcome} decorative={skipped || outcome === 'passed'} />}
      title={check.title}
      meta={
        entry.hidden ? (
          <span className="inline-flex items-center gap-2">
            <Badge variant="outline">Hidden</Badge>
            {meta}
          </span>
        ) : (
          meta
        )
      }
      description={skipped ? (skipReason ?? 'Skipped: the analysis notes above say why.') : undefined}
    >
      <div
        ref={ref}
        role="option"
        aria-selected={selected}
        tabIndex={tabbable ? 0 : -1}
        title={check.title}
        onClick={onSelect}
        onKeyDown={(event) => {
          if (event.key === ' ') {
            event.preventDefault();
            onSelect();
          } else if (event.key === 'Enter') {
            event.preventDefault();
            onSelect();
            onOpen();
          }
        }}
      />
    </ListRow>
  );
}
