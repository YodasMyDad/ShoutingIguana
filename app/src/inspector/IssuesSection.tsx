import { ShieldCheck, SearchX } from 'lucide-react';
import type { UrlDetail } from '../api/inspector';
import { analyseCrawl } from '../crawl/actions';
import { formatCountOf } from '../lib/format';
import { Button } from '../ui/Button';
import { EmptyState } from '../ui/EmptyState';
import { severityCount, SEVERITIES } from '../ui/severity';
import { checkTitle, useChecks, useInspectorContext } from './inspectorContext';
import { Block, IssueCheckLink, IssueList, SectionBody, StaleIssues } from './parts';

/**
 * Its issues: each check that flagged the URL in the last analysis, with its severity, title and detail, linking to
 * the check on Issues with the issue's group open; then the issues on other URLs that name this one.
 */
export function IssuesSection({ detail }: { detail: UrlDetail }) {
  const { crawlId, urlId, crawling } = useInspectorContext();
  const { byId, ready } = useChecks();
  const { issues, namedBy } = detail;

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

  const bySeverity = SEVERITIES.flatMap((severity) => {
    const count = issues.filter((issue) => issue.severity === severity).length;
    return count > 0 ? [severityCount(severity, count)] : [];
  });
  const named = namedBy?.checks ?? [];

  return (
    <SectionBody>
      <StaleIssues detail={detail} className="pb-4" />
      {issues.length === 0 ? (
        <Block>
          <EmptyState title="No issues" icon={ShieldCheck} layout="row" level={3}>
            No check flagged this URL in the last analysis.
          </EmptyState>
        </Block>
      ) : (
        <Block title={formatCountOf(issues.length, 'issue')} meta={bySeverity.join(', ')}>
          <IssueList issues={issues} label="This URL’s issues" detail={detail} />
        </Block>
      )}
      {named.length > 0 ? (
        <Block title="Named by issues on other URLs" meta={formatCountOf(namedBy?.total ?? 0, 'issue')}>
          <ul aria-label="Issues on other URLs that name this one" className="flex flex-col">
            {named.map((entry) => {
              const check = byId.get(entry.checkId);
              return (
                <li
                  key={entry.checkId}
                  className="flex min-h-(--row-height-issue) items-baseline justify-between gap-3 py-1 text-13"
                >
                  <IssueCheckLink
                    checkId={entry.checkId}
                    // A check grouped by the other URL has this URL's group: the pages whose issue names it.
                    group={check?.grouping === 'relatedUrl' ? { detail: null, relatedUrlId: urlId } : undefined}
                    className="inline-flex min-h-(--target-min) min-w-0 items-center font-medium"
                  >
                    {checkTitle(check, ready)}
                  </IssueCheckLink>
                  <span className="figures shrink-0 text-12 text-text-muted">{formatCountOf(entry.count, 'URL')}</span>
                </li>
              );
            })}
          </ul>
        </Block>
      ) : null}
    </SectionBody>
  );
}
