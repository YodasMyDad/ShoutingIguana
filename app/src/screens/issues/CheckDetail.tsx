import { Copy, Eye, EyeOff, ExternalLink } from 'lucide-react';
import { useId, type ReactNode, type Ref } from 'react';
import type { CheckGroupRef, GridSort } from '../../api/grids';
import type { Analysis } from '../../api/types';
import { formatCount, formatPercent } from '../../lib/format';
import { Badge } from '../../ui/Badge';
import { Button } from '../../ui/Button';
import { EmptyState, SkippedState } from '../../ui/EmptyState';
import { IconButton } from '../../ui/IconButton';
import { Num } from '../../ui/Num';
import { SeverityBadge } from '../../ui/SeverityBadge';
import { SeverityMark } from '../../ui/SeverityMark';
import { Stat, StatLink } from '../../ui/Stat';
import type { CheckEntry } from './checkList';
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
  onCopy: () => void;
  copying: boolean;
  onHideChange: (hidden: boolean) => void;
  onLearnMore: () => void;
  titleRef?: Ref<HTMLHeadingElement>;
}

/** "groups, by detail", "groups, by related URL": what the third figure counts. */
function groupedBy(grouping: CheckEntry['check']['grouping']): string {
  return grouping === 'relatedUrl' ? 'by related URL' : 'by detail';
}

/**
 * The selected check (docs/v2-ui-ia.md, Issues): its severity and category, title and actions, its figures, what's
 * wrong, why it matters and how to fix it, then its affected pages. A skipped check shows its reason, never 0s; a
 * check that passed says so plainly.
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
  onCopy,
  copying,
  onHideChange,
  onLearnMore,
  titleRef,
}: CheckDetailProps) {
  const titleId = useId();
  const { check, outcome, count } = entry;
  const found = outcome !== 'passed' && outcome !== 'skipped';
  const pages = analysis.pagesAnalyzed;
  const groups = check.grouping !== 'none';

  return (
    <article aria-labelledby={titleId} className="flex flex-col">
      <header className="px-6 pt-6 pb-5">
        <div className="mb-3 flex flex-wrap items-center gap-3 text-13 text-text-muted">
          <SeverityBadge severity={check.severity} />
          <span>{categoryTitle}</span>
          {outcome === 'skipped' ? <Badge variant="dashed">Skipped</Badge> : null}
          {entry.hidden ? <Badge variant="outline">Hidden from the list</Badge> : null}
        </div>
        <div className="flex items-start gap-4">
          <h2
            ref={titleRef}
            id={titleId}
            tabIndex={-1}
            className="min-w-0 flex-1 text-21 font-semibold tracking-snug break-words outline-none"
          >
            {check.title}
          </h2>
          <div className="flex shrink-0 items-center gap-2">
            {found ? (
              <Button size="sm" icon={Copy} command="issues.copyUrls" loading={copying} onClick={onCopy}>
                Copy URLs
              </Button>
            ) : null}
            <IconButton
              size="sm"
              icon={entry.hidden ? Eye : EyeOff}
              label={entry.hidden ? 'Show this check in the list' : 'Hide this check'}
              command="issues.hideCheck"
              onClick={() => onHideChange(!entry.hidden)}
            />
          </div>
        </div>
        {found ? (
          <div className="mt-5 flex flex-wrap gap-x-10 gap-y-4">
            <StatLink
              compact
              size="md"
              label="Pages affected"
              value={<Num value={count ?? 0} />}
              context="Open them in URLs"
              to="/crawls/$crawl/urls"
              params={{ crawl: crawlId }}
              search={{ filter: [{ column: 'check', op: 'in', values: [check.id] }] }}
            />
            {count !== null && pages > 0 && count <= pages ? (
              <Stat
                compact
                size="md"
                label="Share of pages"
                value={formatPercent(count, pages)}
                context={`of the ${formatCount(pages)} pages crawled`}
              />
            ) : null}
            {groups ? (
              <Stat
                compact
                size="md"
                label="Groups"
                value={groupCount === null ? '–' : <Num value={groupCount} />}
                context={groupedBy(check.grouping)}
              />
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

      <Explain
        what={check.description}
        why={check.why}
        fix={check.fix}
        learnMore={check.learnMore ? <LearnMore onClick={onLearnMore} /> : null}
      />

      {found && count !== null ? (
        <section aria-labelledby={`${titleId}-pages`} className="px-6 pt-5 pb-6">
          <div className="mb-3 flex flex-wrap items-baseline gap-x-3 gap-y-1">
            <h3 id={`${titleId}-pages`} className="text-14 font-semibold">
              Affected pages
            </h3>
            <span className="text-13 text-text-muted">
              {groups
                ? groupCount === null
                  ? `${formatCount(count)} ${count === 1 ? 'issue' : 'issues'}`
                  : `${formatCount(count)} ${count === 1 ? 'issue' : 'issues'} in ${formatCount(groupCount)} ${groupCount === 1 ? 'group' : 'groups'}, ${groupedBy(check.grouping)}`
                : `${formatCount(count)} ${count === 1 ? 'URL' : 'URLs'}`}
            </span>
          </div>
          {groups ? (
            <IssueGroups
              crawlId={crawlId}
              check={check}
              total={count}
              siteOrigin={siteOrigin}
              open={group}
              onOpenChange={onGroupChange}
              onInspect={onInspect}
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
            />
          )}
        </section>
      ) : null}
    </article>
  );
}

/** What's wrong, why it matters and how to fix it: the catalog's own words, side by side. */
function Explain({ what, why, fix, learnMore }: { what: string; why: string; fix: string; learnMore: ReactNode }) {
  const parts = [
    { title: 'What’s wrong', text: what },
    { title: 'Why it matters', text: why },
    { title: 'How to fix it', text: fix },
  ].filter((part) => part.text.trim() !== '');
  return (
    <div className="border-y border-border-subtle bg-surface-sunken px-6 py-5">
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
