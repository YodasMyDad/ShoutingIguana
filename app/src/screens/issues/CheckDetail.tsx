import { ArrowDown, Copy, Eye, EyeOff, ExternalLink } from 'lucide-react';
import { Link } from '@tanstack/react-router';
import { useId, useRef, type ReactNode, type Ref } from 'react';
import type { CheckGroupRef, GridSort } from '../../api/grids';
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
import { Num } from '../../ui/Num';
import { SeverityBadge } from '../../ui/SeverityBadge';
import { SeverityMark } from '../../ui/SeverityMark';
import { Stat, StatLink } from '../../ui/Stat';
import type { CheckEntry, RelatedCheck } from './checkList';
import { namesFileSize } from '../../crawl/evidence';
import { importanceWords } from './evidenceColumns';
import { IssueGroups } from './IssueGroups';
import { IssueRowsGrid } from './IssueRowsGrid';

export interface CheckDetailProps {
  crawlId: string;
  entry: CheckEntry;
  categoryTitle: string;
  analysis: Analysis;
  /** The check's groups, once the first page says (null before, and for a check that doesn't group). */
  groupCount: number | null;
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
}

/** "groups, by detail", "groups, by related URL", "by image" (the image checks): what the third figure counts. */
function groupedBy(check: CheckEntry['check']): string {
  if (check.grouping !== 'relatedUrl') return 'by detail';
  return check.id.startsWith('image-') ? 'by image' : 'by related URL';
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
}: CheckDetailProps) {
  const titleId = useId();
  const explainRef = useRef<HTMLDivElement>(null);
  const { check, outcome, count } = entry;
  const found = outcome !== 'passed' && outcome !== 'skipped';
  const words = affectedWords(check);
  // The share is of the analysis's own totals: the pages it ran on, or the URLs crawled when it ran (`checkShare`); a
  // fault of the whole site is the whole site, as Simple says, never "1 page, <0.1%" (`checkReach`).
  const share = found ? checkReach(check, count, analysis) : null;
  const groups = check.grouping !== 'none';
  // A check that doesn't group, on nearly every page analysed: probably the site's template (the engine's judgement).
  const likelyTemplate =
    !groups && analysis.counts.find((candidate) => candidate.checkId === check.id)?.likelyTemplate === true;

  return (
    <article aria-labelledby={titleId} className="flex flex-col">
      <header className="px-6 pt-6 pb-5">
        <div className="mb-3 flex flex-wrap items-center gap-3 text-13 text-text-muted">
          <SeverityBadge severity={check.severity} />
          <span>{categoryTitle}</span>
          {outcome === 'skipped' ? <Badge variant="dashed">Skipped</Badge> : null}
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
        <div className="flex flex-wrap items-start gap-x-4 gap-y-2">
          <h2
            ref={titleRef}
            id={titleId}
            tabIndex={-1}
            className="min-w-0 grow basis-2/3 text-21 font-semibold tracking-snug break-words outline-none"
          >
            {check.title}
          </h2>
          <div className="flex shrink-0 items-center gap-2">
            {found ? (
              <Button size="sm" icon={Copy} command="issues.copyUrls" loading={copying} onClick={onCopy}>
                Copy URLs
              </Button>
            ) : null}
            {onHideScopeChange && !entry.hidden ? (
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
            )}
          </div>
        </div>
        {found ? (
          <div className="mt-5 flex flex-wrap gap-x-10 gap-y-4">
            <StatLink
              compact
              size="md"
              label={words.affected}
              value={<Num value={count ?? 0} />}
              context="Open them in URLs"
              to="/crawls/$crawl/urls"
              params={{ crawl: crawlId }}
              search={{ filter: [{ column: 'check', op: 'in', values: [check.id] }] }}
            />
            {share ? <Stat compact size="md" label={share.label} value={share.value} context={share.context} /> : null}
            {groups ? (
              <Stat
                compact
                size="md"
                label="Groups"
                value={groupCount === null ? '–' : <Num value={groupCount} />}
                context={groupedBy(check)}
              />
            ) : null}
            {entry.causes !== null ? (
              <Stat
                compact
                size="md"
                label="Things to change"
                value={<Num value={entry.causes} />}
                context={
                  entry.templateCauses
                    ? `${formatCount(entry.templateCauses)} in the site’s template`
                    : entry.causes === count
                      ? 'one for each URL'
                      : 'causes shared by its URLs'
                }
              />
            ) : null}
          </div>
        ) : null}
        {found && related ? <RelatedLines crawlId={crawlId} related={related} /> : null}
        {check.description.trim() !== '' ? (
          <div className="mt-5">
            <h3 className="mb-1 text-13 font-semibold">What’s wrong</h3>
            <p className="max-w-(--measure-explain) text-14 leading-body text-text-secondary">{check.description}</p>
            {check.why.trim() !== '' || check.fix.trim() !== '' ? (
              <Button
                size="sm"
                variant="ghost"
                icon={ArrowDown}
                className="mt-1 -ml-2"
                onClick={() => {
                  explainRef.current?.scrollIntoView({ block: 'start' });
                  explainRef.current?.focus({ preventScroll: true });
                }}
              >
                Why it matters and how to fix it
              </Button>
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
        <section aria-labelledby={`${titleId}-pages`} className="px-6 pt-5 pb-6">
          <div className="mb-3 flex flex-wrap items-baseline gap-x-3 gap-y-1">
            <h3 id={`${titleId}-pages`} className="text-14 font-semibold">
              {words.heading}
            </h3>
            <span className="text-13 text-text-muted">
              {groups
                ? groupCount === null
                  ? `${formatCount(count)} ${count === 1 ? 'issue' : 'issues'}`
                  : `${formatCount(count)} ${count === 1 ? 'issue' : 'issues'} in ${formatCount(groupCount)} ${groupCount === 1 ? 'group' : 'groups'}, ${groupedBy(check)}${namesFileSize(check.targetFacts) ? ', largest file first' : ''}`
                : `${affectedCount(check, count)}${sort.length === 0 ? `, ${importanceWords(check)}` : ''}`}
            </span>
          </div>
          {likelyTemplate ? (
            <Callout tone="insight" title="Likely in the site’s template" className="mb-3">
              The same issue on {formatCount(count)} of the {formatCountOf(analysis.pagesAnalyzed, 'page')} analysed, so
              it’s likely in the site’s template: one fix clears them all.
            </Callout>
          ) : null}
          {groups ? (
            <IssueGroups
              crawlId={crawlId}
              check={check}
              total={count}
              siteOrigin={siteOrigin}
              open={group}
              onOpenChange={onGroupChange}
              onInspect={onInspect}
              onFollow={onFollow}
            />
          ) : (
            <IssueRowsGrid
              crawlId={crawlId}
              check={check}
              total={count}
              siteOrigin={siteOrigin}
              sort={sort}
              onSortChange={onSortChange}
              onInspect={onInspect}
              onFollow={onFollow}
            />
          )}
        </section>
      ) : null}

      <Explain
        ref={explainRef}
        why={check.why}
        fix={check.fix}
        learnMore={check.learnMore ? <LearnMore onClick={onLearnMore} /> : null}
      />
    </article>
  );
}

/** A check's pages as a phrase: "5 pages", "1 page". */
const pagesWords = (pages: number) => formatCountOf(pages, 'page');

/**
 * What a fix also clears, and which fix clears this check's pages: each other check a link to it in Issues, with its
 * pages and, when the fix clears all of it, the points it gives back.
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
          {other.points ? ` (${formatCountOf(other.points, 'point')} back)` : ''}.
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

/**
 * Why it matters and how to fix it: the catalog's own words, side by side, after the check's evidence. What's wrong is
 * in the head, above it. Focusable, for the head's button that brings it into view.
 */
function Explain({
  why,
  fix,
  learnMore,
  ref,
}: {
  why: string;
  fix: string;
  learnMore: ReactNode;
  ref?: Ref<HTMLDivElement> | undefined;
}) {
  const parts = [
    { title: 'Why it matters', text: why },
    { title: 'How to fix it', text: fix },
  ].filter((part) => part.text.trim() !== '');
  if (parts.length === 0 && !learnMore) return null;
  return (
    <div
      ref={ref}
      tabIndex={-1}
      className="scroll-mt-4 border-t border-border-subtle bg-surface-sunken px-6 py-5 outline-none"
    >
      <div className="grid grid-cols-(--explain-columns) gap-6">
        {parts.map((part) => (
          <div key={part.title}>
            <h3 className="mb-1.5 text-13 font-semibold">{part.title}</h3>
            <p className="max-w-(--measure-explain) text-14 leading-body text-text-secondary">{part.text}</p>
          </div>
        ))}
      </div>
      {learnMore ? <div className="mt-3">{learnMore}</div> : null}
    </div>
  );
}

function LearnMore({ onClick }: { onClick: () => void }) {
  return (
    <Button size="sm" variant="ghost" iconEnd={ExternalLink} command="issues.learnMore" onClick={onClick}>
      Learn more
    </Button>
  );
}
