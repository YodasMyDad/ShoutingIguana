import { ArrowDownWideNarrow, Check as Tick, EyeOff } from 'lucide-react';
import { useEffect, useId, useRef, type KeyboardEvent, type Ref } from 'react';
import type { CheckChange } from '../../api/fixLoop';
import type { Severity } from '../../api/types';
import type { ChangeFilterSearch } from '../../routing/search';
import { Icon } from '../../ui/Icon';
import { hasMoved } from '../../changes/changes';
import { changeDate } from '../../changes/changeWords';
import { checkChangeCell, periodLabel, type ComparedPeriod } from '../compare/changeCell';
import { affectedWords, checkReach, type CrawlCounts } from '../../crawl/checkSubject';
import { useFocusRescue } from '../../feedback/useFocusRescue';
import { formatCount, formatCountOf } from '../../lib/format';
import { Badge } from '../../ui/Badge';
import { Button } from '../../ui/Button';
import { EmptyState, FilteredEmptyState } from '../../ui/EmptyState';
import { FilterChip } from '../../ui/FilterChip';
import { ListHeading, ListRow } from '../../ui/ListRow';
import { RefreshLine } from '../../ui/ProgressBar';
import { SearchField } from '../../ui/SearchField';
import { SegmentedControl } from '../../ui/SegmentedControl';
import { Select } from '../../ui/Select';
import { SeverityMark } from '../../ui/SeverityMark';
import { Switch } from '../../ui/Switch';
import { CHECK_SORTS, type CheckEntry, type CheckList, type CheckSort } from './checkList';
import { CountWithReach } from './CountWithReach';

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
  /** The list's order (`list` is built in it); left out, the list is by category and offers no choice. */
  sort?: CheckSort | undefined;
  onSortChange?: ((sort: CheckSort) => void) | undefined;
  /** Points off the score by check id: shown on each row while the list is sorted by them. */
  points?: ReadonlyMap<string, number> | undefined;
  /** Removes the category the list is narrowed to (`list.category`): its chip's ✕. */
  onCategoryClear?: (() => void) | undefined;
  showPassed: boolean;
  onShowPassedChange: (show: boolean) => void;
  showHidden: boolean;
  onShowHiddenChange: (show: boolean) => void;
  /** Every check the user hid, whatever the filters. */
  hiddenTotal: number;
  /** The analysis left notes, shown above the list: a skipped check without a reason of its own points to them. */
  notesShown?: boolean;
  /**
   * The analysis's own totals, which each check's count is a share of beside it ("% of Total"): the pages it ran on
   * for a check that flags only pages, the URLs crawled when it ran for any other (crawl/checkSubject.ts).
   */
  totals?: CrawlCounts | null | undefined;
  onClearFilters: () => void;
  /** The analysis is being read again behind what's shown. */
  refreshing?: boolean;
  filterRef?: Ref<HTMLInputElement>;
  /**
   * The fix loop: each check's change since the crawl compared with (`period`), shown on its row; the checks that moved
   * one way (`change`) a filter of its own, its counts in `changeCounts`. Left out, nothing is compared.
   */
  changes?: ReadonlyMap<string, CheckChange> | null | undefined;
  period?: ComparedPeriod | null | undefined;
  change?: ChangeFilterSearch | undefined;
  onChangeFilter?: ((change: ChangeFilterSearch | undefined) => void) | undefined;
  changeCounts?: Readonly<Record<ChangeFilterSearch, number>> | undefined;
}

/** The change filters' chips, in words a first-time reader understands (A-12). */
const CHANGE_LABELS: Record<ChangeFilterSearch, string> = {
  new: 'New',
  fixed: 'Fixed',
  fewer: 'Fewer URLs',
  more: 'More URLs',
  changed: 'Any change',
};

/** A change filter as its chip among the filters that left nothing says it: "Since 17 Nov is fewer". */
const CHANGE_WORDS: Record<ChangeFilterSearch, string> = {
  new: 'new',
  fixed: 'fixed',
  fewer: 'fewer',
  more: 'more',
  changed: 'changed',
};

/** What a change filter keeps, for its chip's tooltip: "Checks that flag more URLs than on 17 Nov". */
function changeHint(change: ChangeFilterSearch, period: ComparedPeriod): string {
  const date = changeDate(period.iso) || 'the crawl compared with';
  switch (change) {
    case 'new':
      return `Checks that flag URLs now and flagged none on ${date}`;
    case 'fixed':
      return `Checks that flagged URLs on ${date} and flag none now`;
    case 'fewer':
      return `Checks that flag fewer URLs than on ${date}`;
    case 'more':
      return `Checks that flag more URLs than on ${date}`;
    case 'changed':
      return `Every check that moved since ${date}: new, fixed, fewer or more`;
  }
}

const SEVERITY_LABELS: Record<Severity, string> = { error: 'Errors', warning: 'Warnings', notice: 'Notices' };

/**
 * The checks list (docs/v2-ui-ia.md, Issues): the filter, the severity choice with its counts, the category a link
 * narrowed it to as a chip that removes it, then the checks by category in catalog order. One ARIA listbox with a group per category: it's one stop for Tab, the arrow keys move
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
  sort,
  onSortChange,
  points,
  onCategoryClear,
  showPassed,
  onShowPassedChange,
  showHidden,
  onShowHiddenChange,
  hiddenTotal,
  notesShown = false,
  totals = null,
  onClearFilters,
  refreshing = false,
  filterRef,
  changes = null,
  period = null,
  change,
  onChangeFilter,
  changeCounts,
}: ChecksListProps) {
  const compared = changes !== null && period !== null;
  const id = useId();
  const options = useRef(new Map<string, HTMLDivElement>());
  const listRef = useRef<HTMLDivElement>(null);
  // A filter's chip goes with its filter, the chips' list with the last of them, "Clear the filters" with them all:
  // focus that was on one goes to the filter field, which is always there, never to the page.
  const rootRef = useRef<HTMLDivElement>(null);
  useFocusRescue(rootRef, () => rootRef.current?.querySelector<HTMLElement>('input[type="search"]'));

  // The selected check is in view in the list, whoever picked it (a link, back, the first to fix), scrolling the list
  // alone: never the page around it.
  useEffect(() => {
    const list = listRef.current;
    const option = selectedId ? options.current.get(selectedId) : undefined;
    if (!list || !option) return;
    const top = option.getBoundingClientRect().top - list.getBoundingClientRect().top + list.scrollTop;
    const bottom = top + option.offsetHeight;
    if (top < list.scrollTop) list.scrollTop = top - option.offsetHeight;
    else if (bottom > list.scrollTop + list.clientHeight)
      list.scrollTop = bottom - list.clientHeight + option.offsetHeight;
  }, [selectedId]);
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

  // A category has no control of its own (it comes from a link), so its chip is the one place it shows and goes:
  // above the list, or among the filters that left nothing to list.
  const categoryChip = list.category ? (
    <FilterChip key="category" field="Category" value={list.category.title} size="sm" onRemove={onCategoryClear} />
  ) : null;
  // The checks that moved one way since the crawl compared with: a chip that removes itself, as the category's does.
  const changeChip =
    compared && change ? (
      <FilterChip
        key="change"
        field={periodLabel(period)}
        value={CHANGE_WORDS[change]}
        size="sm"
        onRemove={() => onChangeFilter?.(undefined)}
      />
    ) : null;
  const filters = [
    categoryChip,
    changeChip,
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
    // No box of its own: its parts are the card's own rows.
    <div ref={rootRef} className="contents">
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
          {sort && onSortChange ? (
            <Select<CheckSort>
              label="Sort"
              size="sm"
              icon={ArrowDownWideNarrow}
              options={compared ? CHECK_SORTS : CHECK_SORTS.filter((option) => option.value !== 'change')}
              value={sort}
              onValueChange={onSortChange}
            />
          ) : null}
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
            // A toggle: its words stay, and being pressed says the hidden checks are shown.
            <Button
              size="sm"
              variant="ghost"
              icon={EyeOff}
              pressed={showHidden}
              onClick={() => onShowHiddenChange(!showHidden)}
            >
              {`Show ${formatCountOf(hiddenTotal, 'hidden check')}`}
            </Button>
          ) : null}
        </div>
        {compared && onChangeFilter && changeCounts ? (
          // The one in use stays pressed with the others beside it, as the severities above do: another is one press
          // away, and pressing it again lets go (A-12).
          <div role="group" aria-label={periodLabel(period)} className="flex flex-wrap items-center gap-1">
            <span className="mr-1 text-12 text-text-secondary">{periodLabel(period)}</span>
            {(['new', 'fixed', 'fewer', 'more', 'changed'] as const).map((value) => (
              <Button
                key={value}
                size="sm"
                variant="ghost"
                pressed={change === value}
                title={changeHint(value, period)}
                disabled={changeCounts[value] === 0 && change !== value}
                onClick={() => onChangeFilter(change === value ? undefined : value)}
              >
                {CHANGE_LABELS[value]}
                <span className="text-text-muted tabular-nums">{formatCount(changeCounts[value])}</span>
              </Button>
            ))}
          </div>
        ) : null}
        {categoryChip && !list.filteredToNothing ? (
          <div role="group" aria-label="Filters" className="flex flex-wrap gap-2">
            {categoryChip}
          </div>
        ) : null}
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
          ref={listRef}
          role="listbox"
          aria-label="Checks"
          // Focus is on the options (one of them is in the tab order), never on the list itself.
          tabIndex={-1}
          onKeyDown={onKeyDown}
          className="min-h-0 flex-1 overflow-auto px-2 pb-4 @max-split:max-h-(--checks-height-stacked) @max-split:flex-none"
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
                    notesShown={notesShown}
                    totals={totals}
                    points={sort === 'points' ? (points?.get(entry.check.id) ?? 0) : null}
                    change={compared ? (changes.get(entry.check.id) ?? null) : null}
                    period={period}
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
    </div>
  );
}

function CheckOption({
  entry,
  selected,
  tabbable,
  onSelect,
  onOpen,
  notesShown,
  totals,
  points,
  change,
  period,
  ref,
}: {
  entry: CheckEntry;
  /** The fix loop: its change since the crawl compared with, and when that was. */
  change: CheckChange | null;
  period: ComparedPeriod | null;
  selected: boolean;
  tabbable: boolean;
  onSelect: () => void;
  onOpen: () => void;
  notesShown: boolean;
  totals: CrawlCounts | null;
  /** Its points off the score, shown while the list is sorted by them; null otherwise. */
  points: number | null;
  ref: Ref<HTMLDivElement>;
}) {
  const { check, outcome, count, skipReason } = entry;
  const skipped = outcome === 'skipped';
  // What the count is of, for the row's name: pages only for a check that flags nothing else.
  const words = affectedWords(check);
  // A check whose count and rows didn't move says nothing: only what changed is marked, so the list scans.
  const cell = change && period && hasMoved(change) ? checkChangeCell(change, period) : null;
  // The change as text (a tick for a fixed check), never colour alone; its words for a screen reader, its rows' split
  // in its tooltip.
  const moved = cell ? (
    <span className="inline-flex items-center gap-1 text-12 text-text-secondary tabular-nums" title={cell.title}>
      {change?.state === 'fixed' ? <Icon icon={Tick} size="sm" /> : null}
      <span aria-hidden="true">{cell.shown}</span>
      <span className="sr-only">, {cell.spoken}</span>
    </span>
  ) : null;
  const meta = skipped ? (
    <Badge variant="dashed">Skipped</Badge>
  ) : outcome === 'passed' ? (
    (moved ?? <span className="text-12 text-text-muted">Passed</span>)
  ) : (
    <span className="inline-flex items-baseline gap-2">
      {points !== null ? (
        <span className="text-12 text-text-muted tabular-nums">
          {points === 0 ? 'No points' : `−${formatCountOf(points, 'point')}`}
        </span>
      ) : null}
      <CountWithReach
        count={count ?? 0}
        noun={count === 1 ? words.noun : words.plural}
        reach={checkReach(check, count, totals)}
      />
      {moved}
    </span>
  );
  return (
    <ListRow
      asChild
      muted={skipped || outcome === 'passed' || entry.hidden}
      // The mark's word is part of the row's name ("Error Internal URL returns 4xx 9 URLs", "Notice Title too long 3
      // pages"); a skipped or passed check's own word is on the right already.
      icon={<SeverityMark severity={outcome} decorative={skipped || outcome === 'passed'} />}
      title={check.title}
      meta={
        entry.hidden || entry.platform ? (
          <span className="inline-flex items-center gap-2">
            {entry.hidden ? <Badge variant="outline">Hidden</Badge> : null}
            {/* Who owns the fix, quietly: the reason, as the engine gives it, on hover and to a screen reader. */}
            {entry.platform ? (
              <span className="text-12 text-text-muted" title={entry.platform.reason}>
                Set by {entry.platform.name}
                <span className="sr-only">: {entry.platform.reason}</span>
              </span>
            ) : null}
            {meta}
          </span>
        ) : (
          meta
        )
      }
      description={
        skipped
          ? (skipReason ?? (notesShown ? 'Skipped: the analysis notes above say why.' : 'The analysis didn’t say why.'))
          : undefined
      }
    >
      <div
        ref={ref}
        role="option"
        aria-selected={selected}
        data-check={check.id}
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
