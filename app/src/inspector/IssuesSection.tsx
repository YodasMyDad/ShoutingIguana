import { ShieldCheck, SearchX } from 'lucide-react';
import type { UrlDetail } from '../api/inspector';
import { analyseCrawl } from '../crawl/actions';
import { formatCountOf } from '../lib/format';
import { useSettings } from '../settings/settingsStore';
import { Badge } from '../ui/Badge';
import { Button } from '../ui/Button';
import { EmptyState } from '../ui/EmptyState';
import { severityCount, SEVERITIES } from '../ui/severity';
import { checkTitle, useChecks, useInspectorContext } from './inspectorContext';
import { Block, IssueCheckLink, IssueList, SectionBody, StaleIssues } from './parts';
import { splitHidden } from './sections';

/**
 * Its issues: each check that flagged the URL in the last analysis, with its severity, title and detail, linking to
 * the check on Issues with the issue's group open; then the issues on other URLs that name this one.
 */
export function IssuesSection({ detail }: { detail: UrlDetail }) {
  const { crawlId, urlId, crawling } = useInspectorContext();
  const { byId, ready } = useChecks();
  const hiddenChecks = useSettings((settings) => settings.hiddenChecks);
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

  // Checks hidden from the Issues list stay listed, last and marked, and out of the counts.
  const { counted, hidden } = splitHidden(issues, hiddenChecks);
  const bySeverity = SEVERITIES.flatMap((severity) => {
    const count = counted.filter((issue) => issue.severity === severity).length;
    return count > 0 ? [severityCount(severity, count)] : [];
  });
  const named = splitHidden(namedBy?.checks ?? [], hiddenChecks);
  const namedTotal = named.counted.reduce((total, entry) => total + entry.count, 0);
  const hiddenWords = hidden.length > 0 ? `${formatCountOf(hidden.length, 'hidden check')} not counted` : null;

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
        <Block
          title={counted.length === 0 ? 'No issues' : formatCountOf(counted.length, 'issue')}
          meta={[...bySeverity, ...(hiddenWords ? [hiddenWords] : [])].join(', ')}
        >
          <IssueList issues={issues} label="This URL’s issues" detail={detail} />
        </Block>
      )}
      {named.counted.length + named.hidden.length > 0 ? (
        <Block title="Named by issues on other URLs" meta={formatCountOf(namedTotal, 'issue')}>
          <ul aria-label="Issues on other URLs that name this one" className="flex flex-col">
            {[...named.counted, ...named.hidden].map((entry) => {
              const check = byId.get(entry.checkId);
              const isHidden = named.hidden.includes(entry);
              return (
                <li key={entry.checkId} className="flex min-h-(--row-height-issue) items-baseline gap-3 py-1 text-13">
                  <IssueCheckLink
                    checkId={entry.checkId}
                    // A check grouped by the other URL has this URL's group: the pages whose issue names it.
                    group={check?.grouping === 'relatedUrl' ? { detail: null, relatedUrlId: urlId } : undefined}
                    className="inline-flex min-h-(--target-min) min-w-0 items-center font-medium"
                  >
                    {checkTitle(check, ready)}
                  </IssueCheckLink>
                  {isHidden ? <Badge variant="outline">Hidden</Badge> : null}
                  <span className="figures ml-auto shrink-0 text-12 text-text-muted">
                    {formatCountOf(entry.count, 'URL')}
                  </span>
                </li>
              );
            })}
          </ul>
        </Block>
      ) : null}
    </SectionBody>
  );
}
