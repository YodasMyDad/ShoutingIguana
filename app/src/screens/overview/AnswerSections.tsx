import { Link } from '@tanstack/react-router';
import { RotateCcw } from 'lucide-react';
import type { ReactNode } from 'react';
import type { CheckChange } from '../../api/fixLoop';
import type { Checks } from '../../api/types';
import { affectedNoun, affectedWords, checkReach, type CrawlCounts } from '../../crawl/checkSubject';
import { formatCount, formatCountOf } from '../../lib/format';
import { Button } from '../../ui/Button';
import { Card, CardBody, CardHeader } from '../../ui/Card';
import { ListRow } from '../../ui/ListRow';
import { Num } from '../../ui/Num';
import { SeverityCounts } from '../../ui/SeverityCount';
import { SeverityMark } from '../../ui/SeverityMark';
import { StatLink, Stats } from '../../ui/Stat';
import { TextLink } from '../../ui/TextLink';
import { outcomeWord } from '../../ui/severity';
import { causeWords, type AnalysisTotals, type CheckEntry } from '../issues/checkList';
import { CountWithReach } from '../issues/CountWithReach';
import {
  asksForACrawl,
  categoryFigures,
  clearedWords,
  fixFirst,
  severityFigures,
  skippedGroups,
  skippedReasonWords,
} from './answer';
import { linkProps } from './overviewLinks';

const SEVERITY_LABELS = { error: 'Errors', warning: 'Warnings', notice: 'Notices' } as const;

const capitalised = (text: string) => `${text.charAt(0).toUpperCase()}${text.slice(1)}`;

/** A count, and what it was: "9 URLs · was 12". */
function withWas(count: ReactNode, was: string | null): ReactNode {
  return was ? (
    <span className="inline-flex items-baseline gap-1.5">
      {count}
      <span className="text-12 text-text-muted">· {was}</span>
    </span>
  ) : (
    count
  );
}

/**
 * What Fix first adds after a check's count when the crawl is compared with another (the fix loop): "was 9" for a count
 * that moved, "new" for a check that found nothing then, "new check" for one that didn't run then; nothing otherwise.
 */
function wasWords(change: CheckChange | null | undefined): string | null {
  if (!change) return null;
  if ((change.state === 'fewer' || change.state === 'more') && change.then !== null) {
    return `was ${formatCount(change.then)}`;
  }
  if (change.state === 'new') return 'new';
  if (change.state === 'newCheck') return 'new check';
  return null;
}

/**
 * Issues by severity: the checks that found issues, and what they come to, each opening Issues at that severity; then
 * what passed, opening Issues with the passed checks listed, and what was skipped, which is never left to read as clean.
 */
export function SeverityFigures({
  crawlId,
  entries,
  totals,
  columns,
}: {
  crawlId: string;
  entries: readonly CheckEntry[];
  totals: AnalysisTotals;
  columns: number;
}) {
  return (
    <section aria-label="Issues by severity">
      <Stats columns={columns} cards>
        {severityFigures(entries).map((figure) => (
          <StatLink
            key={figure.severity}
            {...linkProps(crawlId, figure.link)}
            label={
              <>
                <SeverityMark severity={figure.severity} decorative />
                {SEVERITY_LABELS[figure.severity]}
              </>
            }
            value={<Num value={figure.checks} noun="check" />}
            // One figure and one line each (H-19): the things to change, the unit the headline counts in; how many
            // issues they come from is on hover.
            context={
              figure.checks === 0
                ? 'None found an issue'
                : figure.causes === null
                  ? `${formatCountOf(figure.issues, 'issue')} between them`
                  : capitalised(
                      causeWords(figure.causes, figure.templateCauses, figure.severity === 'notice' ? 'change' : 'fix'),
                    )
            }
            title={
              figure.checks > 0 && figure.causes !== null ? `From ${formatCountOf(figure.issues, 'issue')}` : undefined
            }
          />
        ))}
        {/* What passed opens Issues with the passed checks listed (`passed=1`); what was skipped is its own card. */}
        <StatLink
          to="/crawls/$crawl/issues/{-$check}"
          params={{ crawl: crawlId }}
          search={{ passed: 1 }}
          label={
            <>
              <SeverityMark severity="passed" decorative />
              Passed
            </>
          }
          value={<Num value={totals.passed} noun="check" />}
          context={
            totals.skipped > 0
              ? `${formatCount(totals.skipped)} ${totals.skipped === 1 ? 'was' : 'were'} skipped`
              : 'Every check ran'
          }
        />
      </Stats>
    </section>
  );
}

/**
 * "265 pages" for a check that flags only pages, "9 URLs" for any other (crawl/checkSubject.ts), with how much of the
 * site that is beside it ("% of Total"): of the pages analysed, of the URLs crawled, or the whole site.
 */
const affected = (entry: CheckEntry, totals: CrawlCounts | null) => {
  const words = affectedWords(entry.check);
  return totals ? (
    <CountWithReach
      count={entry.count ?? 0}
      noun={entry.count === 1 ? words.noun : words.plural}
      reach={checkReach(entry.check, entry.count, totals)}
      nounShown
    />
  ) : (
    <Num value={entry.count} noun={affectedNoun(entry.check)} />
  );
};

/**
 * Fix first (docs/v2-ui-ia.md, flow 3): the checks that found issues, the most severe first, then the most pages or
 * URLs affected, each opening its check in Issues, where it says what's wrong, why it matters and how to fix it.
 */
export function FixFirst({
  crawlId,
  entries,
  hiddenFound = 0,
  order,
  totals,
  changeOf,
}: {
  crawlId: string;
  entries: readonly CheckEntry[];
  /** Hidden checks that found issues: with nothing else to fix, the card says they're why it's empty. */
  hiddenFound?: number;
  /** The analysis's `fixFirst`: the engine's order. Absent from an earlier engine's analysis: then the app's. */
  order?: readonly string[] | undefined;
  /** The analysis's own totals, each count's share is of: the pages it ran on, the URLs crawled when it ran. */
  totals?: CrawlCounts | null | undefined;
  /** The fix loop: each check's change since the crawl compared with ("was 9"); left out, none is said. */
  changeOf?: ((checkId: string) => CheckChange | null) | undefined;
}) {
  const list = fixFirst(entries, undefined, order);
  return (
    <Card as="section" aria-labelledby="overview-fix-title">
      <CardHeader
        title="Fix first"
        titleId="overview-fix-title"
        meta={
          order
            ? 'Most severe first, site-wide faults ahead, then most URLs'
            : 'The most severe first, then the most pages or URLs affected'
        }
      />
      <CardBody tight>
        {list.checks.length === 0 ? (
          <p className="py-2 text-13 text-text-secondary">
            {hiddenFound > 0
              ? `Nothing to fix among the checks shown: the only ${hiddenFound === 1 ? 'check that found issues is' : 'checks that found issues are'} hidden.`
              : 'Nothing to fix: no check found an issue on this crawl.'}
          </p>
        ) : (
          <ol className="-mx-2.5 flex flex-col">
            {list.checks.map(({ entry, link, cleared }) => (
              <li key={entry.check.id}>
                <ListRow
                  asChild
                  icon={<SeverityMark severity={entry.check.severity} decorative />}
                  title={
                    <>
                      <span className="sr-only">{outcomeWord(entry.check.severity)}: </span>
                      {entry.check.title}
                    </>
                  }
                  meta={withWas(affected(entry, totals ?? null), wasWords(changeOf?.(entry.check.id)))}
                  description={
                    <>
                      <span className="line-clamp-1">{entry.check.description}</span>
                      {cleared.length > 0 ? (
                        // One job: what its fix also clears, named here rather than listed as another thing to do.
                        <span className="block text-text-secondary">{clearedWords(cleared)}</span>
                      ) : null}
                    </>
                  }
                >
                  <Link {...linkProps(crawlId, link)} />
                </ListRow>
              </li>
            ))}
          </ol>
        )}
        {list.more > 0 ? (
          <p className="pt-2 text-13 text-text-secondary">
            And {formatCountOf(list.more, 'more check')} with issues.{' '}
            <TextLink to="/crawls/$crawl/issues/{-$check}" params={{ crawl: crawlId }} variant="inline">
              See every check in Issues
            </TextLink>
          </p>
        ) : list.checks.length > 0 ? (
          <p className="pt-2 text-13 text-text-secondary">
            <TextLink to="/crawls/$crawl/issues/{-$check}" params={{ crawl: crawlId }}>
              See every check in Issues
            </TextLink>
          </p>
        ) : null}
      </CardBody>
    </Card>
  );
}

/** Issues by category: each category's checks by severity and its issues, opening its checks in Issues. */
export function Categories({
  crawlId,
  checks,
  entries,
}: {
  crawlId: string;
  checks: Checks;
  entries: readonly CheckEntry[];
}) {
  const figures = categoryFigures(checks, entries);
  if (figures.length === 0) return null;
  return (
    <Card as="section" aria-labelledby="overview-categories-title">
      <CardHeader
        title="Issues by category"
        titleId="overview-categories-title"
        meta="Checks with issues, and their issues"
      />
      <CardBody tight>
        <ul className="-mx-2.5 flex flex-col">
          {figures.map((figure) => (
            <li key={figure.id}>
              <ListRow
                asChild
                title={figure.title}
                meta={
                  <span className="flex items-center gap-4">
                    <SeverityCounts counts={figure.checks} compact hideZero />
                    <span className="figures w-(--overview-issues-width) text-right text-text-secondary">
                      {formatCountOf(figure.issues, 'issue')}
                    </span>
                  </span>
                }
              >
                <Link {...linkProps(crawlId, figure.link)} />
              </ListRow>
            </li>
          ))}
        </ul>
      </CardBody>
    </Card>
  );
}

/**
 * The checks that didn't run, grouped by why. Always shown when there are any: a skipped check is not a clean one.
 * Each group leads with how many checks and the reason (in the plural where it's several), then their names as a
 * list of links in columns; and where the reason's remedy is a fresh crawl, the card carries it as a control, with
 * this crawl's settings (the audit of 2026-11-19, H-04 and H-11: one sentence of 22 names, and "Crawl again" as plain
 * text three times).
 */
export function SkippedChecks({ crawlId, entries }: { crawlId: string; entries: readonly CheckEntry[] }) {
  const groups = skippedGroups(entries);
  const first = groups[0]?.checks[0];
  if (!first) return null;
  const count = groups.reduce((sum, group) => sum + group.checks.length, 0);
  const again = groups.some((group) => asksForACrawl(group.reason));
  return (
    <Card as="section" aria-labelledby="overview-skipped-title">
      <CardHeader
        title="Not checked on this crawl"
        titleId="overview-skipped-title"
        meta={
          <TextLink {...linkProps(crawlId, first.link)} variant="subtle">
            {formatCountOf(count, 'check')} skipped
          </TextLink>
        }
        actions={
          again ? (
            <Button asChild size="sm" icon={RotateCcw}>
              <Link to="/new" search={{ from: crawlId }}>
                Crawl again…
              </Link>
            </Button>
          ) : null
        }
      />
      <CardBody>
        <ul className="flex flex-col gap-4">
          {groups.map((group) => {
            const many = formatCountOf(group.checks.length, 'check');
            return (
              <li key={group.reason ?? ''} className="flex flex-col gap-2 text-13">
                <p className="text-text">
                  <span className="sr-only">Skipped: </span>
                  <span className="font-semibold">{many}</span>
                  <span className="text-text-muted"> · </span>
                  <span>
                    {group.reason
                      ? skippedReasonWords(group.reason, group.checks.length)
                      : 'The analysis didn’t record why: its notes say what it couldn’t check.'}
                  </span>
                </p>
                <ul aria-label={`The ${many}`} className="si-name-columns">
                  {group.checks.map((check) => (
                    <li key={check.id}>
                      <TextLink {...linkProps(crawlId, check.link)}>{check.title}</TextLink>
                    </li>
                  ))}
                </ul>
              </li>
            );
          })}
        </ul>
      </CardBody>
    </Card>
  );
}
