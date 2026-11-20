import { ShieldCheck, SearchX } from 'lucide-react';
import type { UrlDetail } from '../api/inspector';
import { analyseCrawl } from '../crawl/actions';
import { formatCountOf } from '../lib/format';
import { Button } from '../ui/Button';
import { EmptyState } from '../ui/EmptyState';
import { severityCount, SEVERITIES } from '../ui/severity';
import { useInspectorContext, useNamedBy } from './inspectorContext';
import { Block, IssueList, NamedByList, SectionBody, StaleIssues } from './parts';
import { splitHidden } from './sections';
import { namedByWords } from './words';
import { useHiddenChecks } from '../settings/hiddenChecks';

/**
 * Its issues: each check that flagged the URL in the last analysis, with its severity, title and detail, linking to
 * the check on Issues with the issue's group open; then the issues on other URLs that name this one.
 */
export function IssuesSection({ detail }: { detail: UrlDetail }) {
  const { crawlId, crawling } = useInspectorContext();
  const hiddenChecks = useHiddenChecks(crawlId);
  const named = useNamedBy(detail);
  const { issues } = detail;

  if (issues === null) {
    return (
      <SectionBody>
        <EmptyState
          title="Not analysed yet"
          icon={SearchX}
          level={3}
          actions={
            crawling ? null : (
              <Button size="sm" command="crawl.analyse" onClick={() => void analyseCrawl(crawlId)}>
                Analyse it
              </Button>
            )
          }
        >
          {crawling
            ? 'The checks run when the crawl finishes: this URL’s issues show here then.'
            : 'No check has looked at this URL: its issues show here once the crawl is analysed.'}
        </EmptyState>
      </SectionBody>
    );
  }

  // Checks hidden from the Issues list stay listed, last and marked, and out of the counts.
  const { counted, hidden } = splitHidden(issues, hiddenChecks);
  const bySeverity = SEVERITIES.flatMap((severity) => {
    const count = counted.filter((issue) => issue.severity === severity).length;
    return count > 0 ? [severityCount(severity, count)] : [];
  });
  const isNamed = named.counted.length + named.hidden.length > 0;
  const hiddenWords = hidden.length > 0 ? `${formatCountOf(hidden.length, 'hidden check')} not counted` : null;

  return (
    <SectionBody>
      <StaleIssues detail={detail} className="pb-4" />
      {issues.length === 0 ? (
        // Its only issues are on other URLs (an image's, on the pages that show it): said below, never "No issues".
        named.counted.length > 0 ? null : (
          <Block>
            <EmptyState title="No issues" icon={ShieldCheck} layout="row" level={3}>
              No check flagged this URL in the last analysis.
            </EmptyState>
          </Block>
        )
      ) : (
        <Block
          title={counted.length === 0 ? 'No issues' : formatCountOf(counted.length, 'issue')}
          meta={[...bySeverity, ...(hiddenWords ? [hiddenWords] : [])].join(', ')}
        >
          <IssueList issues={issues} label="This URL’s issues" detail={detail} />
        </Block>
      )}
      {isNamed ? (
        <Block
          title={namedByWords(detail.type, named.total)}
          meta={detail.type === 'image' ? undefined : formatCountOf(named.total, 'issue')}
        >
          <NamedByList detail={detail} />
        </Block>
      ) : null}
    </SectionBody>
  );
}
