import { Languages } from 'lucide-react';
import type { UrlDetail } from '../api/inspector';
import { formatCountOf } from '../lib/format';
import { Badge } from '../ui/Badge';
import { EmptyState } from '../ui/EmptyState';
import { fetchState } from './fetchState';
import { hreflangProblem } from './hreflang';
import { useInspectorContext, useIssuesIn } from './inspectorContext';
import { Block, IssueList, SectionBody, WalkUrl } from './parts';

const HREFLANG_CATEGORIES = ['hreflang'] as const;

/**
 * The page's hreflang annotations: each language with where it points and how that answered, the errors the analysis
 * found first.
 */
export function HreflangSection({ detail }: { detail: UrlDetail }) {
  const { crawling } = useInspectorContext();
  const entries = detail.hreflang;
  const issues = useIssuesIn(detail.issues, HREFLANG_CATEGORIES);

  if (entries.length === 0 && issues.length === 0) {
    const state = fetchState(detail, crawling);
    return (
      <SectionBody>
        <EmptyState title="No hreflang" icon={Languages} layout="row" level={3}>
          {state.hasContent
            ? 'The page names no other language or region version of itself.'
            : (state.why ?? 'The crawl didn’t read this URL’s content, so it has none to show.')}
        </EmptyState>
      </SectionBody>
    );
  }

  return (
    <SectionBody>
      {issues.length > 0 ? (
        <Block title="What the analysis found">
          <IssueList issues={issues} label="This URL’s hreflang issues" detail={detail} />
        </Block>
      ) : null}
      <Block title={formatCountOf(entries.length, 'annotation')}>
        <ul aria-label="Hreflang annotations" className="flex flex-col">
          {entries.map((entry, index) => {
            const problem = hreflangProblem(entry, crawling);
            return (
              <li
                key={`${entry.language}-${index}`}
                className="grid grid-cols-(--hreflang-columns) items-baseline gap-x-3 border-t border-border-subtle py-2 first:border-t-0 first:pt-0"
              >
                <span className="font-mono text-12 font-medium wrap-anywhere text-text">{entry.language || '–'}</span>
                <div className="flex min-w-0 flex-col gap-1">
                  {entry.target ? (
                    <WalkUrl
                      id={entry.target.id}
                      url={entry.target.url}
                      statusCode={entry.target.statusCode}
                      outcome={entry.target.outcome}
                      showStatus
                    />
                  ) : (
                    <span className="font-mono text-12 wrap-anywhere text-text">{entry.url ?? entry.href}</span>
                  )}
                  <span className="flex flex-wrap items-center gap-2 text-12 text-text-muted">
                    {entry.fromHttpHeader ? 'In the HTTP header' : entry.inHead ? 'In the head' : 'In the body'}
                    {problem ? <Badge variant="dashed">{problem}</Badge> : null}
                  </span>
                </div>
              </li>
            );
          })}
        </ul>
      </Block>
    </SectionBody>
  );
}
