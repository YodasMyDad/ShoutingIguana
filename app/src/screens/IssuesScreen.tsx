import { useQuery } from '@tanstack/react-query';
import { useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { useEffect, useMemo, useRef, useState } from 'react';
import { api } from '../api/client';
import { isProblem } from '../api/errors';
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
import { analyseCrawl, cancelAnalysis, continueCrawl } from '../crawl/actions';
import { crawlControls, effectiveStatus } from '../crawl/status';
import { announce } from '../feedback/announce';
import { ErrorState } from '../feedback/ErrorState';
import { notifyUndo } from '../feedback/notify';
import { useDelayedLoading } from '../feedback/useDelayedLoading';
import { useCommands } from '../keyboard/registry';
import { formatCountOf } from '../lib/format';
import { useLiveCrawl } from '../live/liveStore';
import { updateSettings, useSettings } from '../settings/settingsStore';
import { openInBrowser } from '../shell/os';
import { Card } from '../ui/Card';
import { EmptyState } from '../ui/EmptyState';
import { PageHeader } from '../ui/PageHeader';
import { SeverityMark } from '../ui/SeverityMark';
import { siteOriginOf } from '../ui/urlText';
import { AnalysisNotes, AnalysisProgress, IssuesSkeleton, NotAnalysed } from './issues/AnalysisState';
import { CheckDetail } from './issues/CheckDetail';
import { ChecksList, type SelectedBy, type SeverityFilter } from './issues/ChecksList';
import {
  analysisTotals,
  answerSentence,
  buildCheckList,
  categoryTitle,
  checkEntries,
  firstCheckToFix,
  type CheckEntry,
} from './issues/checkList';
import { checkUrlsReader, copyUrls } from './issues/copyUrls';
import { groupRefOf } from './issues/groupTree';

const ROUTE = '/crawls/$crawl/issues/{-$check}';

/** A check as it's announced when J or K selects it: "Title too long, 3 URLs". */
function checkWords(entry: CheckEntry): string {
  if (entry.outcome === 'skipped') return `${entry.check.title}, skipped`;
  if (entry.outcome === 'passed') return `${entry.check.title}, passed`;
  return `${entry.check.title}, ${formatCountOf(entry.count ?? 0, 'URL')}`;
}

/**
 * Issues (docs/v2-ui-ia.md, Issues; flow 3, Triage): the answer first, the checks by category on the left, and the
 * selected check on the right, with what's wrong, why it matters, how to fix it, and its affected pages grouped by
 * their cause. The check is in the path; the severity, the text filter, the group in view, a flat check's sort and the
 * inspected URL are in the search, so back, forward and links restore all of it.
 */
export function IssuesScreen() {
  const { crawl: crawlId, check: checkId } = useParams({ from: ROUTE });
  const search = useSearch({ from: ROUTE });
  const navigate = useNavigate({ from: ROUTE });

  const checks = useQuery(checksQuery());
  const analysis = useQuery(analysisQuery(crawlId));
  const item = useQuery(crawlQuery(crawlId)).data;
  const live = useLiveCrawl(crawlId);
  const status = effectiveStatus(item, live);
  const crawling = status !== null && isCrawling(status);
  const analysing = status === 'analyzing';
  const canAnalyse = crawlControls(status, item).canAnalyse;
  const neverAnalysed = isProblem(analysis.error, 'not-analyzed');

  const hiddenIds = useSettings((settings) => settings.hiddenChecks);
  const hidden = useMemo(() => new Set(hiddenIds), [hiddenIds]);
  const [showPassed, setShowPassed] = useState(false);
  const [showHidden, setShowHidden] = useState(false);
  const [copying, setCopying] = useState(false);
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
            showPassed,
            showHidden,
            hidden,
          })
        : null,
    [checks.data, entries, severity, q, showPassed, showHidden, hidden],
  );
  const totals = entries ? analysisTotals(entries) : null;
  // The first to fix among the checks the filters keep: landing on Errors picks an error.
  const first = list ? firstCheckToFix(list.matching) : null;
  const selected = entries?.find((entry) => entry.check.id === checkId) ?? null;
  const found = selected !== null && selected.outcome !== 'passed' && selected.outcome !== 'skipped';
  // A severity that leaves the selected check out of the list (the sidebar's Warnings, the severity control, a link)
  // moves the selection to the first to fix among what it lists. The text filter doesn't: it narrows while typing.
  const outOfSeverity = selected !== null && severity !== 'all' && selected.check.severity !== severity;

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

  const setSearch = (patch: { severity?: Severity[] | undefined; q?: string | undefined }) =>
    void navigate({
      params: { crawl: crawlId, check: checkId },
      search: (previous) => ({ ...previous, ...patch }),
      replace: true,
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
    setSearch({ q: undefined, severity: undefined });
  };

  const onGroupChange = (group: CheckGroupRef | null) =>
    void navigate({
      params: { crawl: crawlId, check: checkId },
      search: (previous) => ({ ...previous, group: group ?? undefined }),
      replace: true,
    });
  const onSortChange = (next: GridSort[]) =>
    void navigate({
      params: { crawl: crawlId, check: checkId },
      search: (previous) => ({ ...previous, sort: next.length > 0 ? next : undefined }),
    });
  // In place, as the inspector opens everywhere: back and forward step between views.
  const onInspect = (urlId: number) =>
    void navigate({
      params: { crawl: crawlId, check: checkId },
      search: (previous) => ({ ...previous, inspect: urlId }),
      replace: true,
    });

  const setHidden = (id: string, hide: boolean) =>
    updateSettings((settings) => ({
      hiddenChecks: hide
        ? [...settings.hiddenChecks.filter((other) => other !== id), id]
        : settings.hiddenChecks.filter((other) => other !== id),
    }));

  const onHideChange = (hide: boolean) => {
    if (!selected) return;
    const { id, title } = selected.check;
    setHidden(id, hide);
    if (!hide) return;
    // The list moves on to the next check it shows, as a list does when a row goes.
    const visible = list?.visible ?? [];
    const at = visible.findIndex((entry) => entry.check.id === id);
    const next = visible[at + 1] ?? visible[at - 1];
    if (next && !showHidden) choose(next.check.id, 'key');
    notifyUndo(`Hid “${title}” from the list`, () => setHidden(id, false));
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
  const onCopy = async () => {
    if (!selected || !copyReady || copying) return;
    setCopying(true);
    try {
      await copyUrls({
        read: checkUrlsReader(crawlId, selected.check.id, copyTarget),
        expected: placed ? placed.pages : selected.count,
      });
    } finally {
      setCopying(false);
    }
  };

  const onLearnMore = () => {
    const url = selected?.check.learnMore;
    if (url) void openInBrowser(url);
  };

  // J and K: focus in the list moves to the new check's option, as the arrow keys move it; focus in the check (its
  // grid, its buttons), which goes with it, moves to the new check's title; anywhere else it stays, and the new check
  // is announced.
  const focusTitleOf = useRef<string | null>(null);
  useEffect(() => {
    if (!checkId || focusTitleOf.current !== checkId) return;
    focusTitleOf.current = null;
    titleRef.current?.focus();
  }, [checkId]);
  const moveBy = (step: number) => {
    const visible = list?.visible ?? [];
    if (visible.length === 0) return;
    const at = visible.findIndex((entry) => entry.check.id === checkId);
    const next = visible[Math.max(0, Math.min(visible.length - 1, at < 0 ? 0 : at + step))];
    if (!next || next.check.id === checkId) return;
    const focused = document.activeElement;
    const listbox = focused?.closest('[role="listbox"]');
    choose(next.check.id, 'key');
    if (listbox) {
      const options = listbox.querySelectorAll<HTMLElement>('[data-check]');
      [...options].find((option) => option.dataset.check === next.check.id)?.focus();
    } else if (focused?.closest('[data-check-detail]')) focusTitleOf.current = next.check.id;
    else announce(checkWords(next));
  };

  // Analysing from here: the button pressed goes when the progress banner comes, so focus goes to its Cancel; when
  // the banner goes, focus that was in it goes to the page's title. Starting and finishing are announced with the
  // crawl's status (live/liveBridge.ts), and a cancel is announced here, as the status alone reads "finished".
  const pageTitleRef = useRef<HTMLHeadingElement>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);
  const startedHere = useRef(false);
  const cancelRequested = useRef(false);
  const [cancelling, setCancelling] = useState(false);
  if (cancelling && !analysing) setCancelling(false);
  const analyse = () => {
    startedHere.current = true;
    void analyseCrawl(crawlId);
  };
  const cancelHere = () => {
    cancelRequested.current = true;
    setCancelling(true);
    void cancelAnalysis(crawlId);
  };
  const cancel = analysing ? cancelHere : null;
  const wasAnalysing = useRef(analysing);
  useEffect(() => {
    if (analysing === wasAnalysing.current) return;
    wasAnalysing.current = analysing;
    const focusLost = document.activeElement === null || document.activeElement === document.body;
    if (analysing) {
      if (startedHere.current && focusLost) cancelRef.current?.focus();
      startedHere.current = false;
      return;
    }
    if (cancelRequested.current) {
      announce(
        neverAnalysed
          ? 'Analysis cancelled. This crawl still hasn’t been analysed.'
          : 'Analysis cancelled. These are the issues from the last analysis.',
      );
    }
    cancelRequested.current = false;
    if (focusLost) pageTitleRef.current?.focus();
  }, [analysing, neverAnalysed]);

  useCommands(
    () => [
      {
        id: 'issues.filter',
        title: 'Filter the checks',
        group: 'Issues',
        keys: ['mod+f'],
        inInputs: true,
        run: () => {
          filterRef.current?.focus();
          filterRef.current?.select();
        },
      },
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
        title: selected?.hidden ? 'Show this check in the list' : 'Hide this check',
        group: 'Issues',
        keys: ['mod+shift+h'],
        enabled: () => selected !== null,
        run: () => onHideChange(!(selected?.hidden ?? false)),
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

  const loading = (checks.isPending || analysis.isPending) && !checks.error && !analysis.error;
  const skeleton = useDelayedLoading(loading);

  const subtitle = totals ? answerSentence(totals) : neverAnalysed ? 'No issues to show yet.' : undefined;
  let body;
  if (loading || skeleton) {
    body = <IssuesSkeleton visible={skeleton} />;
  } else if (checks.error) {
    body = <ErrorState error={checks.error} onRetry={() => void checks.refetch()} />;
  } else if (neverAnalysed) {
    body = (
      <NotAnalysed
        crawling={crawling}
        analysing={analysing}
        event={live?.analysis ?? null}
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
          <AnalysisProgress
            event={live?.analysis ?? null}
            onCancel={cancel}
            cancelling={cancelling}
            cancelRef={cancelRef}
          />
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
              showPassed={showPassed}
              onShowPassedChange={setShowPassed}
              showHidden={showHidden}
              onShowHiddenChange={setShowHidden}
              hiddenTotal={entries?.filter((entry) => entry.hidden).length ?? 0}
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
                onInspect={onInspect}
                onCopy={() => void onCopy()}
                copying={copying || (found && !copyReady)}
                onHideChange={onHideChange}
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
        <PageHeader title="Issues" subtitle={subtitle} titleRef={pageTitleRef} />
        {body}
      </div>
    </div>
  );
}
