import { Link } from '@tanstack/react-router';
import type { Checks } from '../../api/types';
import { affectedNoun } from '../../crawl/checkSubject';
import { formatCount, formatCountOf } from '../../lib/format';
import { Card, CardBody, CardHeader } from '../../ui/Card';
import { ListRow } from '../../ui/ListRow';
import { Num } from '../../ui/Num';
import { SeverityCounts } from '../../ui/SeverityCount';
import { SeverityMark } from '../../ui/SeverityMark';
import { Stat, StatLink, Stats } from '../../ui/Stat';
import { TextLink } from '../../ui/TextLink';
import { outcomeWord } from '../../ui/severity';
import type { AnalysisTotals, CheckEntry } from '../issues/checkList';
import { categoryFigures, fixFirst, severityFigures, skippedGroups } from './answer';
import { linkProps } from './overviewLinks';

const SEVERITY_LABELS = { error: 'Errors', warning: 'Warnings', notice: 'Notices' } as const;

/**
 * Issues by severity: the checks that found issues, and their issues, each opening Issues at that severity; then what
 * passed and what was skipped, which is never left to read as clean.
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
    <Card as="section" aria-label="Issues by severity">
      <Stats columns={columns} wrap={columns < 4}>
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
            context={
              figure.checks === 0 ? 'None found an issue' : `${formatCountOf(figure.issues, 'issue')} between them`
            }
          />
        ))}
        <Stat
          label={
            <>
              <SeverityMark severity="passed" decorative />
              Passed
            </>
          }
          value={<Num value={totals.passed} noun="check" />}
          context={
            totals.skipped > 0
              ? `${formatCount(totals.skipped)} ${totals.skipped === 1 ? 'was' : 'were'} skipped, with why below`
              : 'Every check ran'
          }
        />
      </Stats>
    </Card>
  );
}

/** "265 pages" for a check that flags only pages, "9 URLs" for any other (crawl/checkSubject.ts). */
const affected = (entry: CheckEntry) => <Num value={entry.count} noun={affectedNoun(entry.check)} />;

/**
 * Fix first (docs/v2-ui-ia.md, flow 3): the checks that found issues, the most severe first, then the most pages or
 * URLs affected, each opening its check in Issues, where it says what's wrong, why it matters and how to fix it.
 */
export function FixFirst({
  crawlId,
  entries,
  hiddenFound = 0,
}: {
  crawlId: string;
  entries: readonly CheckEntry[];
  /** Hidden checks that found issues: with nothing else to fix, the card says they're why it's empty. */
  hiddenFound?: number;
}) {
  const list = fixFirst(entries);
  return (
    <Card as="section" aria-labelledby="overview-fix-title">
      <CardHeader
        title="Fix first"
        titleId="overview-fix-title"
        meta="The most severe first, then the most pages or URLs affected"
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
            {list.checks.map(({ entry, link }) => (
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
                  meta={affected(entry)}
                  description={<span className="line-clamp-1">{entry.check.description}</span>}
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

/** "A, B and C". */
function listed(items: readonly React.ReactNode[]): React.ReactNode[] {
  return items.flatMap((item, index) => (index === 0 ? [item] : [index === items.length - 1 ? ' and ' : ', ', item]));
}

/**
 * The checks that didn't run, grouped by why, in the engine's plain words. Always shown when there are any: a skipped
 * check is not a clean one.
 */
export function SkippedChecks({ crawlId, entries }: { crawlId: string; entries: readonly CheckEntry[] }) {
  const groups = skippedGroups(entries);
  if (groups.length === 0) return null;
  const count = groups.reduce((sum, group) => sum + group.checks.length, 0);
  return (
    <Card as="section" aria-labelledby="overview-skipped-title">
      <CardHeader
        title="Not checked on this crawl"
        titleId="overview-skipped-title"
        meta={`${formatCountOf(count, 'check')} skipped`}
      />
      <CardBody>
        <ul className="flex flex-col gap-3">
          {groups.map((group) => (
            <li key={group.reason ?? ''} className="flex gap-2.5 text-13">
              <span className="pt-0.5">
                <SeverityMark severity="skipped" decorative />
              </span>
              <div className="flex min-w-0 flex-col gap-0.5">
                <span className="font-medium text-text">
                  <span className="sr-only">Skipped: </span>
                  {listed(
                    group.checks.map((check) => (
                      <TextLink key={check.id} {...linkProps(crawlId, check.link)} variant="subtle">
                        {check.title}
                      </TextLink>
                    )),
                  )}
                </span>
                <span className="text-text-secondary">
                  {group.reason ?? 'The analysis didn’t record why: its notes say what it couldn’t check.'}
                </span>
              </div>
            </li>
          ))}
        </ul>
      </CardBody>
    </Card>
  );
}
