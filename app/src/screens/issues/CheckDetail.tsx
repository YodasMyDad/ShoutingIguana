import clsx from 'clsx';
import { Check as Tick, ChevronDown, ChevronRight, Copy, Eye, EyeOff, ExternalLink } from 'lucide-react';
import { Link } from '@tanstack/react-router';
import { useId, useState, type Ref } from 'react';
import type { CheckGroupRef, GridFilter, GridSort, IssueGroupSort } from '../../api/grids';
import type { Analysis } from '../../api/types';
import { affectedCount, affectedWords, checkReach } from '../../crawl/checkSubject';
import { formatCount, formatCountOf } from '../../lib/format';
import { Badge } from '../../ui/Badge';
import { Button } from '../../ui/Button';
import { Callout } from '../../ui/Callout';
import { EmptyState, SkippedState } from '../../ui/EmptyState';
import { IconButton } from '../../ui/IconButton';
import { DropdownMenu, MenuItem } from '../../ui/Menu';
import type { HiddenScope } from '../../settings/hiddenChecks';
import { showCheckWords } from './showCheckWords';
import { SeverityBadge } from '../../ui/SeverityBadge';
import { SeverityMark } from '../../ui/SeverityMark';
import { SegmentedControl } from '../../ui/SegmentedControl';
import { TextLink } from '../../ui/TextLink';
import { urlDisplay } from '../../ui/urlText';
import { linksOfUrl } from '../links/linkPlaces';
import { BROKEN_LINKS, LINKS_TO_REDIRECTS } from '../links/presets';
import type { CheckEntry, RelatedCheck } from './checkList';
import { importanceWords } from './evidenceColumns';
import { HowToFix } from './HowToFix';
import { IssueGroups } from './IssueGroups';
import { IssueRowsGrid } from './IssueRowsGrid';
import { IssueUrl } from './IssueUrl';
import { defaultView, groupedLabel } from './checkView';
import { checkPlace, keepCheckPlace, type CheckView } from './issuesPlace';
import { usePreferredFixCase } from './usePreferredFixCase';
import { useShortWindow } from './useShortWindow';
import type { CheckChange } from '../../api/fixLoop';
import { Tabs } from '../../ui/Tabs';
import { checkChangeCell, periodLabel, type ComparedPeriod } from '../compare/changeCell';
import { FixedList } from '../compare/FixedList';
import { RecheckBanner } from '../rechecks/RecheckBanner';
import { RecheckControl, type OpenGroup } from '../rechecks/RecheckControl';
import { useSharedSource } from '../../simple/useSharedSource';
import { sharedSourceCallout } from './whereFound';

/** The fix loop: the check compared with another crawl, for its head, its Fixed tab and its grids. */
export interface CheckCompare {
  /** Its change; null when the comparison doesn't list it. */
  change: CheckChange | null;
  period: ComparedPeriod;
  /** `compareWith` for its grids and its Fixed tab. */
  compareWith: string;
  /** The crawl compared with, by its id: where the check's change leads, to how it stood then. */
  withCrawlId: string;
  /** Rows can be marked: not with counts only. */
  marksRows: boolean;
}

export interface CheckDetailProps {
  crawlId: string;
  entry: CheckEntry;
  categoryTitle: string;
  analysis: Analysis;
  /** The check's groups, once the first page says (null before, and for a check that doesn't group). */
  groupCount: number | null;
  /** The order its groups came in, as the first page says: by pages, or the largest file first. Null before. */
  groupOrder?: IssueGroupSort | null | undefined;
  siteOrigin: string | null;
  group: CheckGroupRef | undefined;
  onGroupChange: (group: CheckGroupRef | null) => void;
  sort: readonly GridSort[];
  onSortChange: (sort: GridSort[]) => void;
  onInspect: (urlId: number) => void;
  /** While the inspector is open: the URL under a grid's cursor, which it follows. */
  onFollow?: ((urlId: number) => void) | undefined;
  onCopy: () => void;
  copying: boolean;
  onHideChange: (hidden: boolean) => void;
  /**
   * Where the check is hidden (this crawl only, or every crawl), and the way to choose: with these, Hide asks which
   * (docs/v2-ui-discovery.md, decision 2). Left out, Hide hides it in every crawl, as before.
   */
  hiddenScope?: HiddenScope | undefined;
  onHideScopeChange?: ((scope: HiddenScope) => void) | undefined;
  onLearnMore: () => void;
  titleRef?: Ref<HTMLHeadingElement>;
  /**
   * The checks related to it by a fix on this crawl (`relatedChecks`): those a fix of it also clears, and those whose fix
   * clears its pages. Left out, none is said.
   */
  related?: { clears: readonly RelatedCheck[]; clearedBy: readonly RelatedCheck[] } | undefined;
  /** The fix loop: the comparison of this check with another crawl; left out, nothing is compared. */
  compare?: CheckCompare | null | undefined;
  /** The fix loop: the open group, as a recheck asks for it, with its URLs. */
  recheckGroup?: OpenGroup | null | undefined;
  /** Names a check by its id: a recheck of another check of the crawl, over this one. */
  titleOf?: ((checkId: string) => string | null) | undefined;
}

/**
 * The selected check (docs/v2-ui-ia.md, Issues): its severity and category, title and actions, its figures, the things
 * to change and what a fix also clears, what's wrong, then what it flags, the evidence, ahead of why it matters and how
 * to fix it, which follow it (the UX audit, top 12 item 6). That is worded by the check's `subject`
 * (crawl/checkSubject.ts): pages, as a share of the pages analysed, only for a check that flags nothing else; URLs, as
 * a share of the URLs crawled, for one that can flag any URL or doesn't say. A skipped check shows its reason, never
 * 0s; a check that passed says so plainly.
 */
export function CheckDetail({
  crawlId,
  entry,
  categoryTitle,
  analysis,
  groupCount,
  groupOrder = null,
  siteOrigin,
  group,
  onGroupChange,
  sort,
  onSortChange,
  onInspect,
  onFollow,
  onCopy,
  copying,
  onHideChange,
  hiddenScope,
  onHideScopeChange,
  onLearnMore,
  titleRef,
  related,
  compare = null,
  recheckGroup = null,
  titleOf = () => null,
}: CheckDetailProps) {
  const titleId = useId();
  const { check, outcome, count } = entry;
  const found = outcome !== 'passed' && outcome !== 'skipped';
  const words = affectedWords(check);
  // The share is of the analysis's own totals: the pages it ran on, or the URLs crawled when it ran (`checkShare`); a
  // fault of the whole site is the whole site, as Simple says, never "1 page, <0.1%" (`checkReach`).
  const share = found ? checkReach(check, count, analysis) : null;
  // A grouped check lists its groups, unless its groups come back empty though it flagged URLs (a file its reader can't
  // group): then its URLs list flat, so a count is never over an empty list (round 4).
  const groups = check.grouping !== 'none' && !(groupCount === 0 && (count ?? 0) > 0);
  // A check that doesn't group, on nearly every page analysed: probably the site's template (the engine's judgement).
  const counted = analysis.counts.find((candidate) => candidate.checkId === check.id);
  const likelyTemplate = !groups && counted?.likelyTemplate === true;
  // A check of the site's own files (its robots.txt, its sitemaps): the files it found at fault, named in place of the
  // home page its issue is reported on. Null for any other check, and an analysis from before files were stored.
  const files = check.about && counted?.files?.length ? counted.files : null;
  // The one page most of its first URLs are linked from, for a check whose URLs other pages refer to: one place to go.
  const shared = useSharedSource(crawlId, check);
  const sharedWords = found && shared && count !== null ? sharedSourceCallout(shared, check, count, siteOrigin) : null;
  // The fix loop: its change since the crawl compared with, and what it no longer flags.
  const change = compare?.change ?? null;
  const changeCell = change && compare ? checkChangeCell(change, compare.period) : null;
  const fixedCheck = outcome === 'passed' && change?.state === 'fixed';
  const cleared = change ? (change.fixed ?? 0) + (change.gone ?? 0) : 0;
  const notChecked = change?.notChecked ?? 0;
  const fixedTab = compare !== null && compare.marksRows && (cleared > 0 || notChecked > 0);
  const hideControl =
    onHideScopeChange && !entry.hidden ? (
      // Hidden in this crawl only, or in every crawl: the check is the same, the reason to hide it may not be.
      <DropdownMenu align="end" trigger={<IconButton size="sm" icon={EyeOff} label="Hide this check" />}>
        <MenuItem shortcut="mod+shift+h" onSelect={() => onHideScopeChange('crawl')}>
          Hide in this crawl
        </MenuItem>
        <MenuItem onSelect={() => onHideScopeChange('all')}>Hide in every crawl</MenuItem>
      </DropdownMenu>
    ) : (
      <IconButton
        size="sm"
        icon={entry.hidden ? Eye : EyeOff}
        label={entry.hidden ? showCheckWords(hiddenScope ?? null) : 'Hide this check'}
        command="issues.hideCheck"
        onClick={() => onHideChange(!entry.hidden)}
      />
    );

  // How its rows are shown (A-06): a list, one row a URL, unless grouping saves work (an image is fixed once for
  // every page that shows it; a group in the site's template is one fix). The route's group, from a link or the tree
  // itself, shows the groups; a choice made here is kept with the check for the session.
  const [chosenView, setChosenView] = useState<CheckView | undefined>(() => checkPlace(crawlId, check.id).view);
  const view: CheckView = !groups ? 'list' : group !== undefined ? 'groups' : (chosenView ?? defaultView(entry));
  const chooseView = (next: CheckView) => {
    setChosenView(next);
    keepCheckPlace(crawlId, check.id, { view: next });
    if (next === 'list' && group !== undefined) onGroupChange(null);
  };
  const [explained, setExplained] = useState(false);
  const short = useShortWindow();
  const hasWhy = check.why.trim() !== '';
  const hasCode = (check.fixExamples?.length ?? 0) > 0;
  const hasFix = check.fix.trim() !== '' || hasCode;
  const explains = hasWhy || hasFix;
  // A short window (1280 × 720) keeps the rows in view: where the pane is narrow too, what's wrong folds away with
  // why and how, behind the one button that names all it opens.
  const foldsWrong = short && explains && check.description.trim() !== '';
  // Opened there, the explanation has the pane and the rows wait behind "Show the rows": a fix with its code is taller
  // than a 1280 × 720 pane has beside its rows, and two half-height scrollers would show neither.
  const reading = short && explained;
  const foldWords = foldedWords([
    foldsWrong ? 'what’s wrong' : null,
    hasWhy ? 'why it matters' : null,
    hasFix ? 'how to fix it' : null,
  ]);
  // The case of its fix the crawl points to, from the links its rows have read: "On this site".
  const preferredCase = usePreferredFixCase(crawlId, check);
  const marked = compare?.marksRows ? { compareWith: compare.compareWith, period: compare.period } : null;

  return (
    <article aria-labelledby={titleId} className="flex min-h-0 flex-1 flex-col @max-split:flex-none">
      {/* A recheck of the crawl, as it runs and once it's done: over the detail, until dismissed. */}
      <div className="shrink-0 px-6 pt-4 empty:hidden">
        <RecheckBanner crawlId={crawlId} checkId={check.id} titleOf={titleOf} />
      </div>
      {/* The head gives way before the rows do (rule 7): in a pane too short for both it scrolls by itself, beside the
          list and never around it, so the card has no scroller of its own. */}
      <header
        className={clsx(
          'min-h-0 shrink overflow-y-auto px-6 pt-5 pb-4 @max-split:shrink-0 @max-split:overflow-visible',
          reading && READING_HEAD,
        )}
      >
        <div className="flex flex-wrap items-start gap-x-4 gap-y-2">
          <div className="min-w-0 grow basis-1/2">
            <div className="mb-1.5 flex flex-wrap items-center gap-3 text-13 text-text-muted">
              <SeverityBadge severity={check.severity} />
              <span>{categoryTitle}</span>
              {outcome === 'skipped' ? <Badge variant="dashed">Skipped</Badge> : null}
              {entry.platform ? <Badge variant="outline">Set by {entry.platform.name}</Badge> : null}
              {entry.hidden ? (
                <Badge variant="outline">
                  {hiddenScope === 'crawl'
                    ? 'Hidden in this crawl'
                    : hiddenScope === 'all'
                      ? 'Hidden in every crawl'
                      : 'Hidden from the list'}
                </Badge>
              ) : null}
            </div>
            <h2
              ref={titleRef}
              id={titleId}
              tabIndex={-1}
              className="text-21 font-semibold tracking-snug break-words outline-none"
            >
              {check.title}
            </h2>
            {found ? (
              // The counts in one line (A-09): how many, of how much, and how that moved; the count is the way to them.
              <p
                className="mt-1.5 flex flex-wrap items-baseline gap-x-2 gap-y-1 text-14 text-text-secondary"
                data-counts
              >
                <TextLink
                  to="/crawls/$crawl/urls"
                  params={{ crawl: crawlId }}
                  search={{ filter: [{ column: 'check', op: 'in', values: [check.id] }] }}
                  className="font-semibold tabular-nums"
                  title="Open them in URLs"
                >
                  {affectedCount(check, count ?? 0)}
                </TextLink>
                {share ? (
                  <>
                    <span aria-hidden="true">·</span>
                    <span title={share.context}>{share.words}</span>
                  </>
                ) : null}
                {changeCell && change && compare ? (
                  <>
                    <span aria-hidden="true">·</span>
                    {/* How it moved is the way to how it stood then: the same check, on the crawl compared with. */}
                    <TextLink
                      to="/crawls/$crawl/issues/{-$check}"
                      params={{ crawl: compare.withCrawlId, check: check.id }}
                      variant="subtle"
                      title={`${changeCell.title}. Open this check on that crawl`}
                      data-change-then
                    >
                      {changeCell.spoken}
                    </TextLink>
                  </>
                ) : null}
                {entry.causes !== null && count !== null && entry.causes < count && entry.causes > 0 ? (
                  // Only when its URLs share causes: a cause for each URL says nothing the count doesn't.
                  <>
                    <span aria-hidden="true">·</span>
                    <span title="How many different things there are to change: URLs that share a cause are fixed together">
                      {formatCountOf(entry.causes, 'cause')} to fix
                      {entry.templateCauses ? `, ${formatCount(entry.templateCauses)} in the site’s template` : ''}
                    </span>
                  </>
                ) : null}
              </p>
            ) : null}
          </div>
          {/* One row of actions, Recheck's first, at the head's end; Recheck's line goes under them all. */}
          <div className="ml-auto max-w-full min-w-0">
            {found ? (
              <RecheckControl
                crawlId={crawlId}
                check={check}
                group={recheckGroup}
                actions={
                  <>
                    <Button size="sm" icon={Copy} command="issues.copyUrls" loading={copying} onClick={onCopy}>
                      Copy URLs
                    </Button>
                    {hideControl}
                  </>
                }
              />
            ) : (
              <div className="flex flex-wrap items-center justify-end gap-2">{hideControl}</div>
            )}
          </div>
        </div>
        {entry.platform ? (
          // Why the owner can't make this fix, exactly as the engine says it.
          <p className="mt-2 max-w-(--measure-text) text-13 text-text-secondary">{entry.platform.reason}</p>
        ) : null}
        {found && related ? <RelatedLines crawlId={crawlId} related={related} /> : null}
        {check.description.trim() !== '' || explains ? (
          // What's wrong in a readable measure; why it matters and how to fix it beside it where the pane is wide, and
          // folded behind their own button where it isn't (rule 7): never a jump link to a block further down. A fix
          // with code takes the wider column, what's wrong and why it matters one over the other beside it: the code
          // wants up to 100 characters, the words about 46.
          <div
            className={clsx(
              'mt-3 grid grid-cols-1 gap-x-10 gap-y-2',
              hasCode
                ? '@min-checks-beside:grid-cols-(--how-to-fix-columns)'
                : '@min-checks-beside:grid-cols-(--explain-columns)',
            )}
            data-explain
          >
            {/* Stacked (a narrow pane), its parts are the grid's own, so the fix comes before why it matters. */}
            <div
              className={
                hasCode
                  ? 'contents @min-checks-beside:flex @min-checks-beside:min-w-0 @min-checks-beside:flex-col @min-checks-beside:gap-y-2'
                  : 'contents'
              }
            >
              {check.description.trim() !== '' ? (
                <div
                  className={clsx(
                    'max-w-(--measure-text)',
                    foldsWrong && '@max-checks-beside:order-2',
                    foldsWrong && !explained && '@max-checks-beside:hidden',
                  )}
                  data-explain-wrong
                >
                  <h3 className="mb-0.5 text-13 font-semibold">What’s wrong</h3>
                  <p className="text-13 leading-body text-text-secondary">{check.description}</p>
                </div>
              ) : null}
              {explains ? (
                <div className={clsx('@min-checks-beside:hidden', foldsWrong && '@max-checks-beside:order-first')}>
                  <Button
                    size="sm"
                    variant="ghost"
                    icon={explained ? ChevronDown : ChevronRight}
                    className="-ml-2"
                    aria-expanded={explained}
                    onClick={() => setExplained((open) => !open)}
                  >
                    {foldWords}
                  </Button>
                </div>
              ) : null}
              {hasWhy ? (
                <div
                  className={clsx(
                    'max-w-(--measure-text) @max-checks-beside:order-last',
                    !explained && '@max-checks-beside:hidden',
                  )}
                  data-explain-part
                >
                  <h3 className="mb-0.5 text-13 font-semibold">Why it matters</h3>
                  <p className="text-13 leading-body text-text-secondary">{check.why}</p>
                  {!hasFix && check.learnMore ? (
                    <div className="mt-1">
                      <LearnMore onClick={onLearnMore} />
                    </div>
                  ) : null}
                </div>
              ) : null}
            </div>
            {hasFix ? (
              <div
                className={clsx(
                  'min-w-0',
                  foldsWrong && '@max-checks-beside:order-1',
                  !explained && '@max-checks-beside:hidden',
                )}
                data-explain-part
              >
                <HowToFix check={check} preferredCase={preferredCase} />
                {check.learnMore ? (
                  <div className="mt-1">
                    <LearnMore onClick={onLearnMore} />
                  </div>
                ) : null}
              </div>
            ) : null}
          </div>
        ) : null}
      </header>

      {outcome === 'skipped' ? (
        <div className="px-6 pb-5">
          <SkippedState
            title="This check was skipped on this crawl"
            reason={
              entry.skipReason ?? (
                <>
                  The analysis didn’t say why for this check.
                  {analysis.notes.length > 0 ? <> Its notes: {analysis.notes.join(' ')}</> : null}
                </>
              )
            }
          />
        </div>
      ) : fixedCheck && compare ? (
        // Fixed since the crawl compared with: it flagged URLs then and none now. What it flagged, and how each went.
        <section aria-labelledby={`${titleId}-fixed`} className="flex flex-col gap-3 overflow-auto px-6 pb-6">
          <EmptyState
            title={`Fixed ${periodLabel(compare.period)
              .replace(/^Since/, 'since')
              .replace(/^Until/, 'by')}`}
            icon={<Tick aria-hidden className="size-(--icon-lg)" />}
            layout="row"
            level="none"
          >
            {`It flagged ${formatCount(change.then ?? 0)} ${change.then === 1 ? words.noun : words.plural} then, and flags none now.`}
          </EmptyState>
          <h3 id={`${titleId}-fixed`} className="text-14 font-semibold">
            What it no longer flags
          </h3>
          <FixedList
            crawlId={crawlId}
            check={check}
            compareWith={compare.compareWith}
            period={compare.period}
            siteOrigin={siteOrigin}
            onInspect={onInspect}
          />
        </section>
      ) : outcome === 'passed' ? (
        <div className="px-6 pb-5">
          <EmptyState
            title="Passed"
            icon={<SeverityMark severity="passed" size="lg" decorative />}
            layout="row"
            level="none"
          >
            No URL on this crawl has this issue.
          </EmptyState>
        </div>
      ) : null}

      {found && count !== null ? (
        // The list the screen exists for gets the rest of the pane's height, with one scroller: its own (rule 7).
        <section
          aria-labelledby={`${titleId}-pages`}
          className={clsx(
            'grid flex-1 grid-cols-1 grid-rows-(--check-rows) border-t border-border-subtle px-6 pt-3 pb-5 has-data-template-callout:grid-rows-(--check-rows-callout) @max-split:block @max-split:flex-none',
            reading && READING_ROWS,
          )}
          data-check-rows
        >
          <div className="min-w-0">
            <div
              className={clsx('flex shrink-0 flex-wrap items-center gap-x-3 gap-y-1', reading ? READING_LINE : 'mb-2')}
            >
              <h3 id={`${titleId}-pages`} className="text-14 font-semibold">
                {words.heading}
              </h3>
              <span className="text-13 text-text-muted">
                {view === 'groups'
                  ? groupCount === null
                    ? affectedCount(check, count)
                    : `${affectedCount(check, count)} in ${formatCountOf(groupCount, 'group')}${groupOrder === 'size' ? ', largest file first' : ''}`
                  : `${affectedCount(check, count)}${sort.length === 0 && !files ? `, ${importanceWords(check)}` : ''}`}
              </span>
              {reading ? (
                <div className={READING_ONLY}>
                  <Button size="sm" icon={ChevronRight} onClick={() => setExplained(false)}>
                    Show the rows
                  </Button>
                </div>
              ) : null}
              {groups ? (
                <SegmentedControl<CheckView>
                  label="How the rows are shown"
                  className={clsx('ml-auto', reading && READING_HIDDEN)}
                  value={view}
                  onValueChange={chooseView}
                  options={[
                    { value: 'list', label: 'List' },
                    { value: 'groups', label: groupedLabel(check) },
                  ]}
                />
              ) : null}
            </div>
            {shared && sharedWords && !reading ? (
              // One line where there's room (A-09): the page, a link like any other, what it links to, and the way to those links.
              <Callout tone="insight" className="mb-2 shrink-0">
                <span className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-0.5" data-shared-source>
                  <SharedSourceTitle
                    shared={shared}
                    words={sharedWords.title}
                    siteOrigin={siteOrigin}
                    onInspect={onInspect}
                  />
                  {/* The advice where there's room for it on the line; the fact and the way on always. */}
                  <span className="@max-checks-beside:hidden">{sharedWords.body}</span>
                  <TextLink
                    to="/crawls/$crawl/links"
                    params={{ crawl: crawlId }}
                    search={{ filter: sharedLinksFilter(check, shared.id) }}
                    variant="inline"
                  >
                    {shared.urls === 1
                      ? 'Show its link in Links'
                      : `Show its ${formatCount(shared.urls)} links in Links`}
                  </TextLink>
                </span>
              </Callout>
            ) : null}
            {likelyTemplate ? (
              <Callout tone="insight" title="Likely in the site’s template" className="mb-2 shrink-0">
                On {formatCount(count)} of the {formatCountOf(analysis.pagesAnalyzed, 'page')} analysed, so it’s likely
                1 cause in the site’s template: one fix clears them all.
              </Callout>
            ) : null}
          </div>
          <div
            className={clsx(
              'flex min-h-0 min-w-0 flex-col contain-size @max-split:block @max-split:contain-none',
              reading && READING_HIDDEN,
            )}
            data-check-table
          >
            {(() => {
              const now =
                view === 'groups' ? (
                  <IssueGroups
                    crawlId={crawlId}
                    check={check}
                    total={count}
                    siteOrigin={siteOrigin}
                    open={group}
                    onOpenChange={onGroupChange}
                    onInspect={onInspect}
                    onFollow={onFollow}
                    compare={marked}
                    legend={!short}
                  />
                ) : (
                  <IssueRowsGrid
                    crawlId={crawlId}
                    check={check}
                    files={files}
                    total={count}
                    siteOrigin={siteOrigin}
                    sort={sort}
                    onSortChange={onSortChange}
                    onInspect={onInspect}
                    onFollow={onFollow}
                    compare={marked}
                    legend={!short}
                  />
                );
              if (!fixedTab) return now;
              // Now, and what it no longer flags since the crawl compared with: fixed or gone (counted), and not
              // checked this time (listed, never counted as fixed).
              return (
                <Tabs
                  label="What it flags now, and what it no longer flags"
                  flush
                  className="flex min-h-0 flex-1 flex-col"
                  panelClassName="flex min-h-0 flex-1 flex-col"
                  tabs={[
                    { value: 'now', label: 'Now', count, content: now },
                    {
                      value: 'fixed',
                      label: `Fixed ${periodLabel(compare.period)
                        .replace(/^Since/, 'since')
                        .replace(/^Until/, 'by')}`,
                      count: cleared,
                      content: (
                        <div className="flex min-h-0 flex-col gap-2 overflow-auto pt-3">
                          {notChecked > 0 ? (
                            <p className="text-13 text-text-secondary">
                              {`${formatCount(notChecked)} more ${notChecked === 1 ? 'was' : 'were'} not checked this time: listed with why, never counted as fixed.`}
                            </p>
                          ) : null}
                          <FixedList
                            crawlId={crawlId}
                            check={check}
                            compareWith={compare.compareWith}
                            period={compare.period}
                            siteOrigin={siteOrigin}
                            onInspect={onInspect}
                          />
                        </div>
                      ),
                    },
                  ]}
                />
              );
            })()}
          </div>
        </section>
      ) : null}
    </article>
  );
}

/**
 * The links from the one page most of a check's URLs are linked from, in Links: its links the link checks count as
 * broken (or as links to redirects) where the check is one a link's fix clears, else all its links.
 */
function sharedLinksFilter(check: Pick<CheckEntry['check'], 'category'>, pageId: number): GridFilter[] {
  const own = linksOfUrl('outlinks', pageId, 'all');
  if (check.category === 'responseCodes' || check.category === 'links') return [...own, ...BROKEN_LINKS];
  if (check.category === 'redirects') return [...own, ...LINKS_TO_REDIRECTS];
  return own;
}

/** The shared page's callout title, its address a link like any other (rule 3): "[/blog/x/] links to 5 of these 8 URLs". */
function SharedSourceTitle({
  shared,
  words,
  siteOrigin,
  onInspect,
}: {
  shared: { id: number; url: string };
  words: string;
  siteOrigin: string | null;
  onInspect: (urlId: number) => void;
}) {
  const shownUrl = urlDisplay(shared.url, siteOrigin).shown;
  const rest = words.startsWith(shownUrl) ? words.slice(shownUrl.length) : ` ${words}`;
  return (
    <span className="flex min-w-0 items-center gap-x-1.5">
      <IssueUrl url={shared.url} siteOrigin={siteOrigin} onInspect={() => onInspect(shared.id)} />
      <span className="font-semibold text-text">{rest.trim()}</span>
    </span>
  );
}

/** A check's pages as a phrase: "5 pages", "1 page". */
const pagesWords = (pages: number) => formatCountOf(pages, 'page');

/**
 * What a fix also clears, and which fix clears this check's pages: each other check a link to it in Issues, with its
 * pages and, when the fix clears all of it, what that adds to the score.
 */
function RelatedLines({
  crawlId,
  related,
}: {
  crawlId: string;
  related: { clears: readonly RelatedCheck[]; clearedBy: readonly RelatedCheck[] };
}) {
  if (related.clears.length === 0 && related.clearedBy.length === 0) return null;
  const checkLink = (other: RelatedCheck) => (
    <Link
      to="/crawls/$crawl/issues/{-$check}"
      params={{ crawl: crawlId, check: other.entry.check.id }}
      className="si-link si-link--inline font-semibold"
    >
      {other.entry.check.title}
    </Link>
  );
  return (
    <ul aria-label="Related checks" className="mt-4 flex flex-col gap-1 text-14 text-text-secondary">
      {related.clears.map((other) => (
        <li key={`clears-${other.entry.check.id}`}>
          Fixing this also clears {checkLink(other)}
          {other.all
            ? `: ${other.pages === 1 ? 'the 1 page' : `all ${pagesWords(other.pages)}`} it flags`
            : ` on ${formatCount(other.pages)} of the ${pagesWords(other.entry.count ?? other.pages)} it flags`}
          {other.points ? ` (+${formatCount(other.points)} to the score)` : ''}.
        </li>
      ))}
      {related.clearedBy.map((other) => (
        <li key={`by-${other.entry.check.id}`}>
          Fixing {checkLink(other)} clears{' '}
          {other.all ? 'every page here' : `${formatCount(other.pages)} of the pages here`}.
        </li>
      ))}
    </ul>
  );
}

// A short pane, narrower than the checks list fits beside and not stacked, with the fold open: the head has the pane
// and scrolls, the rows' table waits behind "Show the rows".
const READING_HEAD = '@max-checks-beside:@min-split:flex-1';
const READING_ROWS =
  '@max-checks-beside:@min-split:block @max-checks-beside:@min-split:flex-none @max-checks-beside:@min-split:pb-3';
const READING_LINE = 'mb-2 @max-checks-beside:@min-split:mb-0';
const READING_HIDDEN = '@max-checks-beside:@min-split:hidden';
const READING_ONLY = 'ml-auto hidden @max-checks-beside:@min-split:block';

/** The fold's button, naming what it opens: "Why it matters and how to fix it", "How to fix it". */
function foldedWords(parts: readonly (string | null)[]): string {
  const named = parts.filter((part) => part !== null);
  const words =
    named.length > 2 ? `${named.slice(0, -1).join(', ')} and ${named[named.length - 1] ?? ''}` : named.join(' and ');
  return words.charAt(0).toUpperCase() + words.slice(1);
}

function LearnMore({ onClick }: { onClick: () => void }) {
  return (
    <Button size="sm" variant="ghost" iconEnd={ExternalLink} command="issues.learnMore" onClick={onClick}>
      Learn more
    </Button>
  );
}
