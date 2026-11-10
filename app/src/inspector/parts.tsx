// The pieces the inspector's sections share: a titled block, a URL that walks the inspector to it, an issue's row,
// the skeleton of a section, and what a section says when it couldn't be read.
import clsx from 'clsx';
import type { ReactNode } from 'react';
import type { CheckGroupRef } from '../api/grids';
import type { UrlIssue } from '../api/inspector';
import { ErrorState } from '../feedback/ErrorState';
import { Skeleton } from '../feedback/Skeleton';
import { SeverityMark } from '../ui/SeverityMark';
import { StatusCode } from '../ui/StatusCode';
import { TextLink } from '../ui/TextLink';
import { Url } from '../ui/Url';
import { checkTitle, useChecks, useInspectorContext } from './inspectorContext';
import { issueLink } from './issueLinks';
import { reasonPhrase } from './words';

/** A block of a section, under a hairline, with its title. */
export function Block({
  title,
  meta,
  actions,
  children,
  className,
}: {
  title?: ReactNode;
  /** Quiet words after the title: "1 warning, 4 notices". */
  meta?: ReactNode;
  actions?: ReactNode;
  children: ReactNode;
  className?: string | undefined;
}) {
  return (
    <section className={clsx('border-t border-border-subtle py-4 first:border-t-0 first:pt-0', className)}>
      {title ? (
        <div className="mb-3 flex flex-wrap items-baseline gap-x-2 gap-y-1">
          <h3 className="text-13 font-semibold text-text">{title}</h3>
          {meta ? <span className="text-13 text-text-muted">{meta}</span> : null}
          {actions ? <span className="ml-auto text-12">{actions}</span> : null}
        </div>
      ) : null}
      {children}
    </section>
  );
}

/** A section's scrolling body, padded as the sheet is. */
export function SectionBody({ children, className }: { children: ReactNode; className?: string | undefined }) {
  return <div className={clsx('min-h-0 flex-1 overflow-auto px-5 pt-4 pb-6', className)}>{children}</div>;
}

/**
 * A URL of the crawl as a link that walks the inspector to it. The URL the inspector already shows, and one the crawl
 * doesn't have, are plain text.
 */
export function WalkUrl({
  id,
  url,
  statusCode,
  showStatus = false,
  truncate = 'middle',
  className,
}: {
  /** Null for a URL the crawl doesn't have (a canonical to somewhere it never went). */
  id: number | null;
  url: string;
  statusCode?: number | null | undefined;
  /** Its status after it, when that's the point: where a canonical or a redirect leads. */
  showStatus?: boolean | undefined;
  truncate?: 'middle' | 'wrap' | undefined;
  className?: string | undefined;
}) {
  const { urlId, siteOrigin, walk } = useInspectorContext();
  const walkable = id !== null && id !== urlId;
  return (
    <span className={clsx('flex min-w-0 items-center gap-2', className)}>
      <Url
        url={url}
        siteOrigin={siteOrigin}
        copyable={!walkable}
        openable={!walkable}
        truncate={truncate}
        onGo={walkable ? () => walk(id) : undefined}
        className="min-w-0 flex-1 text-12"
      />
      {showStatus && statusCode !== undefined ? (
        <StatusCode
          code={statusCode}
          reason={statusCode === null ? 'Not fetched' : reasonPhrase(statusCode)}
          className="shrink-0 text-12"
        />
      ) : null}
    </span>
  );
}

/** The Issues route for one of the URL's issues: its check, with the group it's in open. */
export function IssueCheckLink({
  checkId,
  group,
  children,
  className,
}: {
  checkId: string;
  group: CheckGroupRef | undefined;
  children: ReactNode;
  className?: string | undefined;
}) {
  const { crawlId } = useInspectorContext();
  return (
    <TextLink
      to="/crawls/$crawl/issues/{-$check}"
      params={{ crawl: crawlId, check: checkId }}
      search={group ? { group } : {}}
      variant="subtle"
      className={className}
    >
      {children}
    </TextLink>
  );
}

/**
 * One check that flagged the URL: its severity, its title (a link to the check on Issues, its group open), the
 * detail, and the other URL the issue is about.
 */
export function IssueRow({ issue }: { issue: UrlIssue }) {
  const { byId, ready } = useChecks();
  const check = byId.get(issue.checkId);
  const link = issueLink(issue, check?.grouping);
  const severity = issue.severity ?? check?.severity ?? 'notice';
  return (
    <li className="grid min-h-(--row-height-issue) grid-cols-(--issue-row-columns) items-baseline gap-x-2.5 gap-y-0.5 py-1 text-13">
      <span className="self-center">
        <SeverityMark severity={severity} />
      </span>
      <IssueCheckLink checkId={link.checkId} group={link.group} className="min-w-0 font-medium">
        {checkTitle(check, ready)}
      </IssueCheckLink>
      <span className="figures text-12 text-text-muted">{issue.detail ?? ''}</span>
      {issue.related ? (
        <span className="col-span-2 col-start-2 min-w-0">
          <WalkUrl id={issue.related.id} url={issue.related.url} />
        </span>
      ) : null}
    </li>
  );
}

export function IssueList({ issues, label }: { issues: readonly UrlIssue[]; label: string }) {
  return (
    <ul aria-label={label} className="flex flex-col">
      {issues.map((issue, index) => (
        <IssueRow key={`${issue.checkId}-${index}`} issue={issue} />
      ))}
    </ul>
  );
}

/** A section's skeleton: the shape of its content, announced as loading. */
export function SectionSkeleton({ label, children }: { label: string; children?: ReactNode }) {
  return (
    <div role="status" aria-busy="true" aria-label={label} className="flex flex-col gap-4 px-5 pt-4 pb-6">
      {children ?? (
        <>
          <Skeleton className="h-(--skeleton-text) w-1/2" />
          <Skeleton className="h-(--skeleton-text) w-2/3" />
          <Skeleton className="h-(--skeleton-text) w-1/3" />
          <Skeleton className="h-(--skeleton-text) w-3/5" />
        </>
      )}
    </div>
  );
}

/** A section that couldn't be read: its own error, with a retry where that could work. The others carry on. */
export function SectionError({
  error,
  onRetry,
  what,
}: {
  error: unknown;
  onRetry?: (() => void) | undefined;
  what: string;
}) {
  return (
    <div className="px-1">
      <ErrorState compact error={error} title={`${what} couldn’t be read.`} {...(onRetry ? { onRetry } : {})} />
    </div>
  );
}
