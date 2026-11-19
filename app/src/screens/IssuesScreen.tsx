import { useQuery } from '@tanstack/react-query';
import { useNavigate, useParams, useRouter, useSearch } from '@tanstack/react-router';
import clsx from 'clsx';
import { ChevronDown, ChevronUp, ListChecks } from 'lucide-react';
import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { flushSync } from 'react-dom';
import { api } from '../api/client';
import { GRID_PAGE_SIZE, type CheckGroupRef, type GridSort } from '../api/grids';
import { analysisQuery, checksQuery, crawlQuery, issueGroupPlaceQuery, queryKeys } from '../api/queries';
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
import { formatCount, formatCountOf } from '../lib/format';
import { useLive } from '../live/liveStore';
import { getSettings, updateSettings, useSettings } from '../settings/settingsStore';
import { useInspector } from '../routing/inspector';
import { openInBrowser } from '../shell/os';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { IconButton } from '../ui/IconButton';
import { EmptyState } from '../ui/EmptyState';
import { PageHeader } from '../ui/PageHeader';
import { SeverityMark } from '../ui/SeverityMark';
import { siteOriginOf } from '../ui/urlText';
import { AnalysisNotes, AnalysisProgress, IssuesSkeleton, NotAnalysed } from './issues/AnalysisState';
import { AnswerHeadline } from './issues/AnswerHeadline';
import { CheckDetail } from './issues/CheckDetail';
import { showCheckWords } from './issues/showCheckWords';
import { ChecksList, type SelectedBy, type SeverityFilter } from './issues/ChecksList';
import {
  analysisTotals,
  buildCheckList,
  categoryTitle,
  checkEntries,
  countedEntries,
  firstCheckToFix,
  hiddenTotals,
  hiddenWords,
  isInCategory,
  relatedChecks,
  type CheckEntry,
  type CheckSort,
} from './issues/checkList';
import { checkUrlsReader, copyUrls } from './issues/copyUrls';
import { groupRefOf, groupsGridPageOf, type GroupsGridPage } from './issues/groupTree';
import { useAnalysisAnswer } from './issues/useAnalysisAnswer';
import { useAnalysisControl } from './issues/useAnalysisControl';
import { useShortWindow } from './issues/useShortWindow';
import { hiddenScopeOf, setCheckHidden, useHiddenChecks, type HiddenScope } from '../settings/hiddenChecks';
import { comparisonCounts, countedChecks, marksRows } from '../changes/changes';
import type { ChangeFilterSearch } from '../routing/search';
import { periodLabel, type ComparedPeriod } from './compare/changeCell';
import { useComparison } from './compare/useComparison';
import type { CheckCompare } from './issues/CheckDetail';
import { useThingsMoved } from './compare/useThingsMoved';
import { thingsMovedWords } from './compare/thingsMoved';

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
  // When a check is open, the check is the work: in a main area too narrow for the list beside it (under a 1600 px
  // window, or with the inspector open), the list is behind "All checks" (tokens.css `--container-checks-beside`). The
  // bar with that button shows only then, so whether it's drawn says whether the list is behind it.
  const [checksOpen, setChecksOpen] = useState(false);
  const checksBarRef = useRef<HTMLDivElement>(null);
  const checksListId = useId();
  const checksBehindButton = () => (checksBarRef.current?.getClientRects().length ?? 0) > 0;
  // The inspector opening (a row inspected, "Go to URL…") folds the list back behind its button: the check is what
  // was being worked on, and it stays beside the inspector, never stacked under the list off-screen (3b final).
  const inspecting = search.inspect !== undefined;
  const [wasInspecting, setWasInspecting] = useState(inspecting);
  if (inspecting !== wasInspecting) {
    setWasInspecting(inspecting);
    if (inspecting) setChecksOpen(false);
  }

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
  // The list's order, remembered for Advanced: the order to fix them in until another is chosen.
  const checkSort = useSettings((settings) => settings.checkSort.advanced);
  const onCheckSortChange = (next: CheckSort) =>
    updateSettings((settings) => ({ checkSort: { ...settings.checkSort, advanced: next } }));
  const points = useMemo(
    () => new Map((analysis.data?.score?.lines ?? []).map((line) => [line.checkId, line.points])),
    [analysis.data?.score?.lines],
  );

  // The fix loop (docs/v2-fix-loop.md, Advanced, "Issues"): the checks compared with the crawl compared with, each with
  // its change; the checks fixed since then listed apart; the Change sort and the New, Fixed, Fewer, More filters.
  const comparison = useComparison(crawlId);
  const compared =
    comparison.stage.kind === 'shown' || comparison.stage.kind === 'needsAnalysis' ? comparison.stage.comparison : null;
  const period = useMemo<ComparedPeriod | null>(
    () =>
      comparison.comparing.kind === 'none'
        ? null
        : { iso: comparison.comparing.startedAt, later: comparison.comparing.later },
    [comparison.comparing],
  );
  const changes = useMemo(
    () => (compared && period ? new Map(compared.checks.map((change) => [change.checkId, change])) : null),
    [compared, period],
  );
  const changeFilter: ChangeFilterSearch | undefined = changes ? search.change : undefined;
  const changeCounts = useMemo(() => {
    if (!compared) return undefined;
    const tally = comparisonCounts(countedChecks(compared, hidden, 'advanced'));
    return { ...tally, changed: tally.new + tally.fixed + tally.fewer + tally.more };
  }, [compared, hidden]);
  const listSort = checkSort === 'change' && !changes ? 'fixFirst' : checkSort;
  const things = useThingsMoved(crawlId, comparison);

  const entries = useMemo(
    () => (checks.data && analysis.data ? checkEntries(checks.data, analysis.data, hidden) : null),
    [checks.data, analysis.data, hidden],
  );
  const list = useMemo(
    () =>
      checks.data && entries
        ? buildCheckList(
            checks.data,
            entries,
            {
              severity: severity === 'all' ? [] : [severity],
              q,
              category,
              showPassed,
              showHidden,
              hidden,
              changes,
              fixedTitle: period
                ? `Fixed ${periodLabel(period)
                    .replace(/^Since/, 'since')
                    .replace(/^Until/, 'by')}`
                : undefined,
              change: changeFilter,
            },
            { sort: listSort, fixFirst: analysis.data?.fixFirst, points, changes },
          )
        : null,
    [
      checks.data,
      entries,
      severity,
      q,
      category,
      showPassed,
      showHidden,
      hidden,
      listSort,
      analysis.data?.fixFirst,
      points,
      changes,
      period,
      changeFilter,
    ],
  );
  // The answer counts the checks that aren't hidden, as the list, its severities and the Overview do, and says what
  // it left out.
  const totals = entries ? analysisTotals(countedEntries(entries)) : null;
  const hiddenChecks = entries ? hiddenTotals(entries) : null;
  // The first to fix among the checks the filters keep: landing on Errors picks an error.
  // In the engine's order to fix them (the analysis's `fixFirst`), the app's own for an earlier engine's analysis.
  // With a change filter that keeps only checks fixed since then (none to fix), the first of them.
  const first = list
    ? (firstCheckToFix(list.matching, analysis.data?.fixFirst) ?? (changeFilter ? (list.visible[0] ?? null) : null))
    : null;
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
      (category !== undefined && checks.data !== undefined && !isInCategory(checks.data, selected.check, category)) ||
      (changeFilter !== undefined && list !== null && !list.visible.includes(selected)));

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

  // What the selected check is compared with, for its head, its Fixed tab and its grids.
  const checkCompare: CheckCompare | null =
    compared && period && comparison.compareWith && checkId
      ? {
          change: changes?.get(checkId) ?? null,
          period,
          compareWith: comparison.compareWith,
          withCrawlId: compared.with.id,
          marksRows: marksRows(compared.comparable),
        }
      : null;
  const groupsCompareWith = checkCompare?.marksRows ? checkCompare.compareWith : null;

  // The groups' first page, shared with the groups grid (the same query): the header's group count comes from it.
  const groupsPage = useQuery({
    // The groups grid's own key, the comparison in it, so the two read one page.
    queryKey: [...queryKeys.issueGroupsGrid(crawlId, checkId ?? ''), groupsCompareWith ?? 'none', GRID_PAGE_SIZE, 0],
    queryFn: async ({ signal }): Promise<GroupsGridPage> =>
      groupsGridPageOf(
        await api.issueGroups(
          crawlId,
          checkId ?? '',
          { offset: 0, limit: GRID_PAGE_SIZE, ...(groupsCompareWith ? { compareWith: groupsCompareWith } : {}) },
          signal,
        ),
      ),
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
    change?: ChangeFilterSearch | undefined;
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
    setSearch({ q: undefined, severity: undefined, category: undefined, change: undefined });
  };
  const clearCategory = () => setSearch({ category: undefined });

  const router = useRouter();
  const onGroupChange = (group: CheckGroupRef | null) => {
    // A check's only group opens by itself once its groups are read: by then another check may have been picked (a
    // hide, J), and the group isn't that one's.
    const path = router.latestLocation.pathname.replace(/\/$/, '');
    if (checkId && !path.endsWith(`/issues/${checkId}`)) return;
    void navigate({
      params: { crawl: crawlId, check: checkId },
      search: (previous) => ({ ...previous, group: group ?? undefined }),
      replace: true,
      state: true,
    });
  };
  const onSortChange = (next: GridSort[]) =>
    void navigate({
      params: { crawl: crawlId, check: checkId },
      search: (previous) => ({ ...previous, sort: next.length > 0 ? next : undefined }),
    });
  // From a grid, in place, and following its cursor while it's open (routing/inspector.ts).
  const inspector = useInspector();
  const onFollow = inspecting ? inspector.open : undefined;

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

  /** The check `step` places from the selected one in the list, or null where there's none to move to. */
  const entryBy = (step: number): CheckEntry | null => {
    const visible = list?.visible ?? [];
    if (visible.length === 0) return null;
    const at = visible.findIndex((entry) => entry.check.id === checkId);
    const next = visible[Math.max(0, Math.min(visible.length - 1, at < 0 ? 0 : at + step))];
    return !next || next.check.id === checkId ? null : next;
  };
  const moveBy = (step: number) => {
    const next = entryBy(step);
    if (next) selectTakingFocus(next);
  };
  // The bar's Previous and Next: focus stays on the button pressed, and the check it moved to is announced.
  const stepBy = (step: number) => {
    const next = entryBy(step);
    if (!next) return;
    choose(next.check.id, 'key');
    announce(checkWords(next));
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
      {
        ...searchCommand({ id: 'issues.filter', title: 'Filter the checks', group: 'Issues', field: filterRef }),
        // The list behind "All checks" comes out first, so its field can take the keys.
        run: () => {
          if (checksBehindButton()) flushSync(() => setChecksOpen(true));
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
                    references: selected.check.references,
                  }
                : null,
            // A check with one group: its pages are the check's, which "This check" already offers (A-15).
            group:
              placed && (groupsPage.data?.matching ?? 2) > 1
                ? {
                    ref: groupRefOf(placed),
                    pages: placed.pages,
                    words: placed.detail ? `“${placed.detail}”` : 'the group’s pages',
                  }
                : null,
            listed: listedChecks,
          })
        : null,
    [item, crawling, simple, selected, found, placed, sort, listedChecks, groupsPage.data?.matching],
    { group: 'Issues', title: 'Export…', choices: CRAWL_FILE_COMMANDS },
  );

  // A check picked from the list behind "All checks" (a click, Enter): the list goes back behind it and the check takes
  // the width, with focus on its title. The arrow keys only move through the list, which stays out.
  const onListSelect = (next: string, by: SelectedBy) => {
    choose(next, by);
    if (by !== 'click' || !checksBehindButton()) return;
    setChecksOpen(false);
    if (next === checkId) titleRef.current?.focus();
    else focusTitleOf.current = next;
  };
  const onListOpen = () => {
    if (checksBehindButton()) setChecksOpen(false);
    requestAnimationFrame(() => titleRef.current?.focus());
  };
  // The list is behind its button only while a check is open: with none, the list is what there is to pick from.
  const listBehind = selected !== null && !checksOpen;
  const short = useShortWindow();
  const visibleChecks = list?.visible ?? [];
  const checkAt = visibleChecks.findIndex((entry) => entry.check.id === checkId);
  // Its place among the checks that found something, which is what the sidebar counts (A-15: never "1 of 54" when
  // 31 checks fired); a passed or skipped check, listed after them, says no place.
  const foundChecks = visibleChecks.filter((entry) => entry.outcome !== 'passed' && entry.outcome !== 'skipped');
  const foundAt = foundChecks.findIndex((entry) => entry.check.id === checkId);

  const loading = (checks.isPending || (analysis.isPending && !rereading)) && !checks.error && !analysis.error;
  const skeleton = useDelayedLoading(loading);

  const hiddenNote = hiddenChecks ? hiddenWords(hiddenChecks) : null;
  const movedLine = things && period ? thingsMovedWords(things) : null;
  const subtitle = totals ? (
    // In a short window with the check alone in view, the answer is with the list, behind "All checks": the check's
    // rows get its room (A-09).
    <span className={clsx(short && listBehind && '@max-checks-beside:hidden')}>
      <AnswerHeadline totals={totals} />
      {hiddenNote ? ` ${hiddenNote.count}${hiddenNote.rest}` : ''}
      {/* The fix loop: what moved since the crawl compared with, in the headline's unit. */}
      {movedLine && period ? (
        <span className="mt-1 block">
          <span className="text-text-secondary">{periodLabel(period)}: </span>
          {movedLine.charAt(0).toLowerCase()}
          {movedLine.slice(1)}
        </span>
      ) : null}
    </span>
  ) : neverAnalysed ? (
    'No issues to show yet.'
  ) : undefined;
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
        {/* In a short window with the check alone in view, the notes are with the list, behind "All checks". */}
        <div className={clsx('contents', short && listBehind && '@max-checks-beside:hidden')}>
          <AnalysisNotes
            analysis={analysis.data}
            crawling={crawling}
            analysing={analysing}
            canAnalyse={canAnalyse}
            onAnalyse={analyse}
          />
        </div>
        <div
          className={clsx(
            'grid min-h-0 flex-1 grid-cols-(--issues-columns) gap-4 @max-split:flex-none @max-split:grid-cols-1',
            listBehind && '@max-checks-beside:grid-cols-1',
          )}
        >
          <Card
            as="section"
            id={checksListId}
            aria-label="Checks"
            aria-busy={refreshing}
            className={clsx(
              'relative flex min-h-0 flex-col overflow-hidden',
              listBehind && '@max-checks-beside:hidden',
            )}
          >
            <ChecksList
              list={list}
              selectedId={checkId}
              onSelect={onListSelect}
              onOpen={onListOpen}
              q={q}
              onQueryChange={onQueryChange}
              severity={severity}
              onSeverityChange={onSeverityChange}
              sort={listSort}
              onSortChange={onCheckSortChange}
              changes={changes}
              period={period}
              change={changeFilter}
              onChangeFilter={(next) => setSearch({ change: next })}
              changeCounts={changeCounts}
              points={points}
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
          <Card data-check-detail className="flex min-h-0 flex-col overflow-auto @max-split:overflow-visible">
            {selected ? (
              // The list's way back, while it's behind its button: the check's place among those listed, and the
              // checks on either side of it.
              <div
                ref={checksBarRef}
                className="flex items-center gap-2 border-b border-border-subtle px-4 py-2 @min-checks-beside:hidden"
              >
                <Button
                  size="sm"
                  variant="ghost"
                  icon={ListChecks}
                  aria-expanded={checksOpen}
                  aria-controls={checksListId}
                  onClick={() => setChecksOpen((open) => !open)}
                >
                  All checks
                </Button>
                {foundAt >= 0 ? (
                  <span className="text-13 text-text-muted tabular-nums">
                    {formatCount(foundAt + 1)} of {formatCountOf(foundChecks.length, 'check')} with issues
                  </span>
                ) : null}
                <span className="ml-auto flex items-center gap-1">
                  <IconButton
                    size="sm"
                    icon={ChevronUp}
                    label="Previous check"
                    command="issues.previous"
                    disabled={checkAt <= 0}
                    onClick={() => stepBy(-1)}
                  />
                  <IconButton
                    size="sm"
                    icon={ChevronDown}
                    label="Next check"
                    command="issues.next"
                    disabled={checkAt < 0 || checkAt >= visibleChecks.length - 1}
                    onClick={() => stepBy(1)}
                  />
                </span>
              </div>
            ) : null}
            {selected ? (
              <CheckDetail
                key={selected.check.id}
                crawlId={crawlId}
                entry={selected}
                categoryTitle={categoryTitle(checks.data, selected.check)}
                analysis={analysis.data}
                related={entries ? relatedChecks(entries, selected, points) : undefined}
                groupCount={groupsPage.data?.matching ?? null}
                groupOrder={groupsPage.data?.sort ?? null}
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
                compare={checkCompare}
                recheckGroup={
                  placed ? { detail: placed.detail, relatedUrlId: placed.relatedUrlId, urls: placed.pages } : null
                }
                titleOf={(id) => checks.data.checks.find((entry) => entry.id === id)?.title ?? null}
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
