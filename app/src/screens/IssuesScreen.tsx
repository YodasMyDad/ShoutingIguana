import { useQuery } from '@tanstack/react-query';
import { useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { useEffect, useMemo, useRef, useState } from 'react';
import { api } from '../api/client';
import { GRID_PAGE_SIZE, type CheckGroupRef, type GridPage, type GridSort, type IssueGroup } from '../api/grids';
import {
  analysisQuery,
  checksQuery,
  crawlQuery,
  groupsAsGridPage,
  issueGroupPlaceQuery,
  queryKeys,
} from '../api/queries';
import { GRID_PAGE_GC_MS } from '../app/queryClient';
import { isCrawling, type Severity } from '../api/types';
import { continueCrawl } from '../crawl/actions';
import { affectedCount } from '../crawl/checkSubject';
import { crawlControls, effectiveStatus } from '../crawl/status';
import { announce } from '../feedback/announce';
import { ErrorState } from '../feedback/ErrorState';
import { notifyUndo } from '../feedback/notify';
import { useDelayedLoading } from '../feedback/useDelayedLoading';
import { searchCommand } from '../grid/searchCommand';
import { issuesOffer } from '../export/offers';
import { ExportButton } from '../export/ExportButton';
import { CRAWL_FILE_COMMANDS, useExportCommand } from '../export/useExportCommand';
import { useCommands } from '../keyboard/registry';
import { useMode } from '../mode/mode';
import { formatCountOf } from '../lib/format';
import { useLive } from '../live/liveStore';
import { getSettings, useSettings } from '../settings/settingsStore';
import { useInspector } from '../routing/inspector';
import { openInBrowser } from '../shell/os';
import { Card } from '../ui/Card';
import { EmptyState } from '../ui/EmptyState';
import { PageHeader } from '../ui/PageHeader';
import { SeverityMark } from '../ui/SeverityMark';
import { siteOriginOf } from '../ui/urlText';
import { AnalysisNotes, AnalysisProgress, IssuesSkeleton, NotAnalysed } from './issues/AnalysisState';
import { CheckDetail } from './issues/CheckDetail';
import { showCheckWords } from './issues/showCheckWords';
import { ChecksList, type SelectedBy, type SeverityFilter } from './issues/ChecksList';
import {
  analysisTotals,
  answerSentence,
  buildCheckList,
  categoryTitle,
  checkEntries,
  countedEntries,
  firstCheckToFix,
  hiddenTotals,
  hiddenWords,
  isInCategory,
  type CheckEntry,
} from './issues/checkList';
import { checkUrlsReader, copyUrls } from './issues/copyUrls';
import { groupRefOf } from './issues/groupTree';
import { useAnalysisAnswer } from './issues/useAnalysisAnswer';
import { useAnalysisControl } from './issues/useAnalysisControl';
import { hiddenScopeOf, setCheckHidden, useHiddenChecks, type HiddenScope } from '../settings/hiddenChecks';

const ROUTE = '/crawls/$crawl/issues/{-$check}';

/**
 * A check as it's announced when J or K selects it: "Title too long, 3 pages", "Internal URL returns 4xx, 9 URLs".
 * Pages only for a check that flags nothing else (crawl/checkSubject.ts).
 */
function checkWords(entry: CheckEntry): string {
  if (entry.outcome === 'skipped') return `${entry.check.title}, skipped`;
  if (entry.outcome === 'passed') return `${entry.check.title}, passed`;
  return `${entry.check.title}, ${affectedCount(entry.check, entry.count ?? 0)}`;
}

/**
 * Issues (docs/v2-ui-ia.md, Issues; flow 3, Triage): the answer first, the checks by category on the left, and the
 * selected check on the right, with what's wrong, why it matters, how to fix it, and the pages or URLs it flags
 * grouped by their cause. The check is in the path; the severity, the text filter, the category a link narrowed the
 * list to, the group in view, a flat check's sort and the inspected URL are in the search, so back, forward and links
 * restore all of it.
 */
export function IssuesScreen() {
  const { crawl: crawlId, check: checkId } = useParams({ from: ROUTE });
  const search = useSearch({ from: ROUTE });
  const navigate = useNavigate({ from: ROUTE });

  const checks = useQuery(checksQuery());
  const analysis = useQuery(analysisQuery(crawlId));
  const item = useQuery(crawlQuery(crawlId)).data;
  // Only what the screen shows of the live crawl: its status and the analysis's progress. The whole crawl changes on
  // every progress tick (4 a second while it runs), which would draw the screen again each time.
  const liveStatus = useLive((state) => state.crawls[crawlId]?.status);
  const analysisEvent = useLive((state) => state.crawls[crawlId]?.analysis ?? null);
  const status = effectiveStatus(item, liveStatus === undefined ? undefined : { status: liveStatus });
  const crawling = status !== null && isCrawling(status);
  const analysing = status === 'analyzing';
  const canAnalyse = crawlControls(status, item).canAnalyse;
  // Never analysed, remembered while the read is asked again: the screen keeps saying so, rather than swap itself
  // for its loading frame until the answer comes.
  const { neverAnalysed, rereading, finishing } = useAnalysisAnswer(crawlId, analysis, { analysing });

  const hiddenIds = useHiddenChecks(crawlId);
  const hidden = useMemo(() => new Set(hiddenIds), [hiddenIds]);
  const [showPassed, setShowPassed] = useState(false);
  const [showHidden, setShowHidden] = useState(false);
  // The copies running, by what they copy (a check, or one of its groups): another check's isn't held up by one.
  const [copies, setCopies] = useState<ReadonlySet<string>>(() => new Set());
  const filterRef = useRef<HTMLInputElement>(null);
  const titleRef = useRef<HTMLHeadingElement>(null);

  // The filter answers each key at once; the route follows, replaced, so back doesn't step through every letter. The
  // route's value is taken up only when it's not one this field sent (back, forward, a link, clearing the filters).
  const routeQ = search.q ?? '';
  const [q, setQ] = useState(routeQ);
  const sentQ = useRef(new Set<string>());
  useEffect(() => {
    if (sentQ.current.delete(routeQ)) return;
    sentQ.current.clear();
    setQ(routeQ);
  }, [routeQ]);
  const severity: SeverityFilter = (search.severity?.length === 1 ? search.severity[0] : undefined) ?? 'all';
  // One category's checks, from a link (Overview's "Issues by category"): a filter of its own, with its own words in
  // the list, never text in the filter field, which would match other categories' checks by their titles.
  const category = search.category;
  const sort = useMemo<GridSort[]>(() => search.sort ?? [], [search.sort]);

  const entries = useMemo(
    () => (checks.data && analysis.data ? checkEntries(checks.data, analysis.data, hidden) : null),
    [checks.data, analysis.data, hidden],
  );
  const list = useMemo(
    () =>
      checks.data && entries
        ? buildCheckList(checks.data, entries, {
            severity: severity === 'all' ? [] : [severity],
            q,
            category,
            showPassed,
            showHidden,
            hidden,
          })
        : null,
    [checks.data, entries, severity, q, category, showPassed, showHidden, hidden],
  );
  // The answer counts the checks that aren't hidden, as the list, its severities and the Overview do, and says what
  // it left out.
  const totals = entries ? analysisTotals(countedEntries(entries)) : null;
  const hiddenChecks = entries ? hiddenTotals(entries) : null;
  // The first to fix among the checks the filters keep: landing on Errors picks an error.
  // In the engine's order to fix them (the analysis's `fixFirst`), the app's own for an earlier engine's analysis.
  const first = list ? firstCheckToFix(list.matching, analysis.data?.fixFirst) : null;
  const selected = entries?.find((entry) => entry.check.id === checkId) ?? null;
  const found = selected !== null && selected.outcome !== 'passed' && selected.outcome !== 'skipped';
  // Arriving on a check that's hidden (a link from the inspector, the palette) shows the hidden checks, so the
  // check the page is on is in the list, marked "Hidden". Hiding the one already open doesn't: the list moves on.
  const [arrivedAt, setArrivedAt] = useState<string | undefined>(undefined);
  if (entries && arrivedAt !== checkId) {
    setArrivedAt(checkId);
    if (selected?.hidden && !showHidden) setShowHidden(true);
  }
  // A severity that leaves the selected check out of the list (the sidebar's Warnings, the severity control, a link)
  // moves the selection to the first to fix among what it lists, and so does a category. The text filter doesn't: it
  // narrows while typing.
  const outOfSeverity =
    selected !== null &&
    ((severity !== 'all' && selected.check.severity !== severity) ||
      (category !== undefined && checks.data !== undefined && !isInCategory(checks.data, selected.check, category)));

  // No check in the path, or one the severity leaves out: the first to fix, replacing the address so back doesn't
  // bounce here again.
  useEffect(() => {
    if ((checkId && !outOfSeverity) || !first || first.check.id === checkId) return;
    void navigate({
      params: { crawl: crawlId, check: first.check.id },
      search: (previous) =>
        checkId ? { ...previous, group: undefined, sort: undefined, inspect: undefined } : previous,
      replace: true,
    });
  }, [checkId, outOfSeverity, first, crawlId, navigate]);

  // The groups' first page, shared with the groups grid (the same query): the header's group count comes from it.
  const groupsPage = useQuery({
    queryKey: [...queryKeys.issueGroupsGrid(crawlId, checkId ?? ''), GRID_PAGE_SIZE, 0],
    queryFn: async ({ signal }): Promise<GridPage<IssueGroup>> =>
      groupsAsGridPage(await api.issueGroups(crawlId, checkId ?? '', { offset: 0, limit: GRID_PAGE_SIZE }, signal)),
    enabled: found && selected.check.grouping !== 'none',
    staleTime: Infinity,
    gcTime: GRID_PAGE_GC_MS,
  });

  const choose = (next: string, by: SelectedBy) => {
    if (next === checkId) return;
    void navigate({
      params: { crawl: crawlId, check: next },
      // The group, sort and inspected URL belong to the check that was selected.
      search: (previous) => ({ ...previous, group: undefined, sort: undefined, inspect: undefined }),
      replace: by === 'key',
    });
  };

  const setSearch = (patch: {
    severity?: Severity[] | undefined;
    q?: string | undefined;
    category?: string | undefined;
  }) =>
    void navigate({
      params: { crawl: crawlId, check: checkId },
      search: (previous) => ({ ...previous, ...patch }),
      replace: true,
      // In place: an inspector walked to here keeps the steps back to where it opened.
      state: true,
    });

  const onQueryChange = (next: string) => {
    setQ(next);
    sentQ.current.add(next);
    setSearch({ q: next || undefined });
  };
  const onSeverityChange = (next: SeverityFilter) => setSearch({ severity: next === 'all' ? undefined : [next] });
  const clearFilters = () => {
    setQ('');
    sentQ.current.add('');
    setSearch({ q: undefined, severity: undefined, category: undefined });
  };
  const clearCategory = () => setSearch({ category: undefined });

  const onGroupChange = (group: CheckGroupRef | null) =>
    void navigate({
      params: { crawl: crawlId, check: checkId },
      search: (previous) => ({ ...previous, group: group ?? undefined }),
      replace: true,
      state: true,
    });
  const onSortChange = (next: GridSort[]) =>
    void navigate({
      params: { crawl: crawlId, check: checkId },
      search: (previous) => ({ ...previous, sort: next.length > 0 ? next : undefined }),
    });
  // From a grid, in place, and following its cursor while it's open (routing/inspector.ts).
  const inspector = useInspector();
  const onFollow = search.inspect !== undefined ? inspector.open : undefined;

  // A check is hidden in this crawl only or in every crawl (docs/v2-ui-discovery.md, decision 2).
  const hiddenScope = useSettings((settings) => (checkId ? hiddenScopeOf(settings, crawlId, checkId) : null));

  // Another check selected by a key (J, K, hiding this one): focus in the list moves to the new check's option, as
  // the arrow keys move it; focus in the check (its grid, its buttons), which goes with it, moves to the new check's
  // title; anywhere else it stays, and the new check is announced.
  const focusTitleOf = useRef<string | null>(null);
  useEffect(() => {
    if (!checkId || focusTitleOf.current !== checkId) return;
    focusTitleOf.current = null;
    titleRef.current?.focus();
  }, [checkId]);
  const selectTakingFocus = (next: CheckEntry, fromDetail = false) => {
    const focused = document.activeElement;
    const listbox = focused?.closest('[role="listbox"]');
    choose(next.check.id, 'key');
    // A menu of the check's own (Hide's) has the keyboard in its portal: the check's title takes it, as from its button.
    if (fromDetail) focusTitleOf.current = next.check.id;
    else if (listbox) {
      const options = listbox.querySelectorAll<HTMLElement>('[data-check]');
      [...options].find((option) => option.dataset.check === next.check.id)?.focus();
    } else if (focused?.closest('[data-check-detail]')) focusTitleOf.current = next.check.id;
    else announce(checkWords(next));
  };

  const onHideChange = (hide: boolean) => onHideScopeChange(hide ? 'crawl' : null);
  const onHideScopeChange = (scope: HiddenScope, fromDetail = false) => {
    if (!selected) return;
    const { id, title } = selected.check;
    const before = hiddenScopeOf(getSettings(), crawlId, id);
    setCheckHidden(crawlId, id, scope);
    if (scope === null) {
      // Shown again where it was hidden, said once with Undo: one hidden in every crawl is shown in every crawl
      // (round 5 review R1-12).
      if (before !== null) {
        notifyUndo(before === 'all' ? `Showed “${title}” in every crawl` : `Showed “${title}” in this crawl`, () =>
          setCheckHidden(crawlId, id, before),
        );
      }
      return;
    }
    // The list moves on to the next check it shows, as a list does when a row goes, and focus with it.
    const visible = list?.visible ?? [];
    const at = visible.findIndex((entry) => entry.check.id === id);
    const next = visible[at + 1] ?? visible[at - 1];
    if (next && !showHidden) selectTakingFocus(next, fromDetail);
    notifyUndo(scope === 'all' ? `Hid “${title}” in every crawl` : `Hid “${title}” in this crawl`, () =>
      setCheckHidden(crawlId, id, before),
    );
  };

  // What Copy URLs copies: the group the groups grid placed and shows open, else the check's URLs. A group the route
  // names that isn't placed yet can't be copied; one the check doesn't have is dropped from the route (IssueGroups).
  const routeGroup = found && selected.check.grouping !== 'none' ? (search.group ?? null) : null;
  const placement = useQuery({
    ...issueGroupPlaceQuery(crawlId, checkId ?? '', routeGroup ?? { detail: null, relatedUrlId: null }),
    // Read only: the groups grid looks for it, and this hears what it found.
    enabled: false,
  });
  const place = routeGroup ? placement.data : undefined;
  const placed = place && place.index !== null ? place.group : null;
  const copyTarget = placed ? groupRefOf(placed) : null;
  const copyReady = found && (routeGroup === null || placed !== null);
  const copyKey = JSON.stringify([checkId, copyTarget]);
  const copying = copies.has(copyKey);
  const onCopy = async () => {
    if (!selected || !copyReady || copying) return;
    const key = copyKey;
    setCopies((running) => new Set(running).add(key));
    try {
      await copyUrls({
        read: checkUrlsReader(crawlId, selected.check.id, copyTarget),
        expected: placed ? placed.pages : selected.count,
      });
    } finally {
      setCopies((running) => {
        const next = new Set(running);
        next.delete(key);
        return next;
      });
    }
  };

  const onLearnMore = () => {
    const url = selected?.check.learnMore;
    if (url) void openInBrowser(url);
  };

  const moveBy = (step: number) => {
    const visible = list?.visible ?? [];
    if (visible.length === 0) return;
    const at = visible.findIndex((entry) => entry.check.id === checkId);
    const next = visible[Math.max(0, Math.min(visible.length - 1, at < 0 ? 0 : at + step))];
    if (!next || next.check.id === checkId) return;
    selectTakingFocus(next);
  };

  // Analysing from here, with where focus goes as the progress banner comes and goes (useAnalysisControl).
  const {
    titleRef: pageTitleRef,
    cancelRef,
    analyse,
    cancelHere,
    cancel,
    cancelling,
  } = useAnalysisControl(crawlId, analysing, neverAnalysed);

  useCommands(
    () => [
      searchCommand({ id: 'issues.filter', title: 'Filter the checks', group: 'Issues', field: filterRef }),
      {
        id: 'issues.next',
        title: 'Next check',
        group: 'Issues',
        keys: ['j'],
        enabled: () => (list?.visible.length ?? 0) > 0,
        run: () => moveBy(1),
      },
      {
        id: 'issues.previous',
        title: 'Previous check',
        group: 'Issues',
        keys: ['k'],
        enabled: () => (list?.visible.length ?? 0) > 0,
        run: () => moveBy(-1),
      },
      {
        id: 'issues.copyUrls',
        title: copyTarget ? 'Copy the group’s URLs' : 'Copy the check’s URLs',
        group: 'Issues',
        keys: ['mod+shift+c'],
        keywords: ['copy', 'urls', 'clipboard'],
        enabled: () => copyReady && !copying,
        run: onCopy,
      },
      {
        id: 'issues.hideCheck',
        title: selected?.hidden ? showCheckWords(hiddenScope) : 'Hide this check in this crawl',
        group: 'Issues',
        keys: ['mod+shift+h'],
        enabled: () => selected !== null,
        run: () => onHideChange(!(selected?.hidden ?? false)),
      },
      {
        id: 'issues.hideCheckEverywhere',
        title: 'Hide this check in every crawl',
        group: 'Issues',
        keywords: ['hide', 'all crawls'],
        enabled: () => selected !== null && hiddenScope !== 'all',
        run: () => onHideScopeChange('all'),
      },
      {
        id: 'issues.showPassed',
        title: showPassed ? 'Leave out the checks that passed' : 'Show the checks that passed',
        group: 'Issues',
        enabled: () => (list?.passedCount ?? 0) > 0,
        run: () => setShowPassed((value) => !value),
      },
      {
        id: 'issues.showHidden',
        title: showHidden ? 'Leave out the hidden checks' : 'Show the hidden checks',
        group: 'Issues',
        enabled: () => hidden.size > 0,
        run: () => setShowHidden((value) => !value),
      },
      {
        id: 'issues.learnMore',
        title: 'Learn more about this check',
        group: 'Issues',
        enabled: () => Boolean(selected?.check.learnMore),
        run: onLearnMore,
      },
      {
        id: 'crawl.cancelAnalysis',
        title: 'Cancel the analysis',
        group: 'Crawl',
        enabled: () => analysing && !cancelling,
        run: cancelHere,
      },
    ],
    [
      list,
      checkId,
      selected,
      copyReady,
      copying,
      copyTarget,
      showPassed,
      showHidden,
      hidden,
      analysing,
      cancelling,
      crawlId,
    ],
  );

  // Export (Ctrl+E): this check, the open group's pages, the report and every check (the spreadsheet of everything).
  const simple = useMode() === 'simple';
  // The checks the list's filters keep, when it's filtered: the workbook of those is offered too (the workbook takes
  // the view's filters, as a grid's file does).
  const listedChecks = useMemo(() => {
    if (!list || (severity === 'all' && !q && !category)) return null;
    const words = [
      severity === 'all' ? null : `${severity}s`,
      category ? 'one category' : null,
      q ? `titles with “${q}”` : null,
    ].filter((part) => part !== null);
    return { ids: list.matching.map((entry) => entry.check.id), words: words.join(', ') };
  }, [list, severity, q, category]);
  useExportCommand(
    () =>
      item
        ? issuesOffer({
            item,
            crawling,
            simple,
            check:
              selected && found
                ? {
                    id: selected.check.id,
                    title: (simple ? selected.check.plainTitle : null) ?? selected.check.title,
                    urls: selected.count,
                    // A check that doesn't group is its sorted list: its export is that list, in that order.
                    flat: selected.check.grouping === 'none',
                    sort,
                  }
                : null,
            group: placed
              ? {
                  ref: groupRefOf(placed),
                  pages: placed.pages,
                  words: placed.detail ? `“${placed.detail}”` : 'the group’s pages',
                }
              : null,
            listed: listedChecks,
          })
        : null,
    [item, crawling, simple, selected, found, placed, sort, listedChecks],
    { group: 'Issues', title: 'Export…', choices: CRAWL_FILE_COMMANDS },
  );

  const loading = (checks.isPending || (analysis.isPending && !rereading)) && !checks.error && !analysis.error;
  const skeleton = useDelayedLoading(loading);

  const hiddenNote = hiddenChecks ? hiddenWords(hiddenChecks) : null;
  const subtitle = totals
    ? `${answerSentence(totals)}${hiddenNote ? ` ${hiddenNote.count}${hiddenNote.rest}` : ''}`
    : neverAnalysed
      ? 'No issues to show yet.'
      : undefined;
  let body;
  if (loading || skeleton) {
    body = <IssuesSkeleton visible={skeleton} />;
  } else if (checks.error) {
    body = <ErrorState error={checks.error} onRetry={() => void checks.refetch()} />;
  } else if (neverAnalysed) {
    body = (
      <NotAnalysed
        crawling={crawling}
        analysing={analysing || finishing}
        event={finishing ? { crawlId, phase: 'writing', done: 1, total: 1 } : analysisEvent}
        canAnalyse={canAnalyse}
        status={status}
        missing={item?.missing === true}
        onAnalyse={analyse}
        onContinue={() => void continueCrawl(crawlId)}
        onCancel={cancel}
        cancelling={cancelling}
        cancelRef={cancelRef}
      />
    );
  } else if (analysis.error || !analysis.data || !checks.data || !list) {
    body = (
      <ErrorState error={analysis.error} title="The issues couldn’t be read." onRetry={() => void analysis.refetch()} />
    );
  } else {
    const refreshing = (analysis.isFetching || checks.isFetching) && !loading;
    body = (
      <>
        {analysing ? (
          <AnalysisProgress event={analysisEvent} onCancel={cancel} cancelling={cancelling} cancelRef={cancelRef} />
        ) : null}
        <AnalysisNotes
          analysis={analysis.data}
          crawling={crawling}
          analysing={analysing}
          canAnalyse={canAnalyse}
          onAnalyse={analyse}
        />
        <div className="grid min-h-0 flex-1 grid-cols-(--issues-columns) gap-4 @max-split:flex-none @max-split:grid-cols-1">
          <Card
            as="section"
            aria-label="Checks"
            aria-busy={refreshing}
            className="relative flex min-h-0 flex-col overflow-hidden"
          >
            <ChecksList
              list={list}
              selectedId={checkId}
              onSelect={choose}
              onOpen={() => requestAnimationFrame(() => titleRef.current?.focus())}
              q={q}
              onQueryChange={onQueryChange}
              severity={severity}
              onSeverityChange={onSeverityChange}
              onCategoryClear={clearCategory}
              showPassed={showPassed}
              onShowPassedChange={setShowPassed}
              showHidden={showHidden}
              onShowHiddenChange={setShowHidden}
              hiddenTotal={entries?.filter((entry) => entry.hidden).length ?? 0}
              notesShown={analysis.data.notes.length > 0}
              totals={analysis.data}
              onClearFilters={clearFilters}
              refreshing={refreshing}
              filterRef={filterRef}
            />
          </Card>
          <Card data-check-detail className="min-h-0 overflow-auto @max-split:overflow-visible">
            {selected ? (
              <CheckDetail
                key={selected.check.id}
                crawlId={crawlId}
                entry={selected}
                categoryTitle={categoryTitle(checks.data, selected.check)}
                analysis={analysis.data}
                groupCount={groupsPage.data?.matching ?? null}
                siteOrigin={siteOriginOf(item?.startUrl)}
                group={search.group}
                onGroupChange={onGroupChange}
                sort={sort}
                onSortChange={onSortChange}
                onInspect={inspector.open}
                onFollow={onFollow}
                onCopy={() => void onCopy()}
                copying={copying || (found && !copyReady)}
                onHideChange={onHideChange}
                hiddenScope={hiddenScope}
                onHideScopeChange={(scope) => onHideScopeChange(scope, true)}
                onLearnMore={onLearnMore}
                titleRef={titleRef}
              />
            ) : checkId ? (
              <EmptyState title="This check isn’t in this version of Shouting Iguana" level={2} panel>
                The link names a check this version doesn’t have. Pick one from the list.
              </EmptyState>
            ) : totals && totals.found > 0 ? (
              // Checks found issues, but none the filters keep: there's nothing to pick for them.
              <EmptyState title="No check to show" level={2} panel>
                None of the checks these filters keep found an issue. Pick one from the list, or clear the filters.
              </EmptyState>
            ) : (
              <EmptyState
                title="No issues found"
                icon={<SeverityMark severity="passed" size="lg" decorative />}
                level={2}
                panel
              >
                {totals && totals.passed > 0
                  ? `Every check that ran passed: ${formatCountOf(totals.passed, 'check')}.`
                  : 'The analysis found nothing to report.'}
                {totals && totals.skipped > 0
                  ? ` ${formatCountOf(totals.skipped, 'check')} skipped, listed with why.`
                  : ''}
              </EmptyState>
            )}
          </Card>
        </div>
      </>
    );
  }

  return (
    <div className="@container h-full">
      <div className="flex h-full min-h-0 flex-col gap-5 px-(--page-gutter) pt-7 pb-6 @max-split:h-auto">
        <PageHeader
          title="Issues"
          subtitle={subtitle}
          titleRef={pageTitleRef}
          actions={item ? <ExportButton /> : null}
        />
        {body}
      </div>
    </div>
  );
}
