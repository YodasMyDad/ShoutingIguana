// The pieces the inspector's sections share: a titled block, a URL that walks the inspector to it, an issue's row,
// the skeleton of a section, and what a section says when it couldn't be read.
import clsx from 'clsx';
import { useState, type ReactNode } from 'react';
import type { CheckGroupRef } from '../api/grids';
import type { UrlDetail, UrlIssue } from '../api/inspector';
import { ErrorState } from '../feedback/ErrorState';
import { Skeleton } from '../feedback/Skeleton';
import { SeverityMark } from '../ui/SeverityMark';
import { StatusCode } from '../ui/StatusCode';
import { TextLink } from '../ui/TextLink';
import { Url } from '../ui/Url';
import { checkTitle, useChecks, useInspectorContext } from './inspectorContext';
import { issueLink } from './issueLinks';
import { NO_STATUS } from './links';
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
          // A target comes with its status alone: why there's none (never fetched, blocked, unreachable) isn't said.
          reason={statusCode === null ? NO_STATUS : reasonPhrase(statusCode)}
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
export function IssueRow({ issue, detail }: { issue: UrlIssue; detail: UrlDetail }) {
  const { byId, ready } = useChecks();
  const check = byId.get(issue.checkId);
  const link = issueLink(issue, check?.grouping, detail);
  const severity = issue.severity ?? check?.severity ?? 'notice';
  return (
    <li className="grid min-h-(--row-height-issue) grid-cols-(--check-row-columns) gap-x-2 py-0.5 text-13">
      <span className="flex h-(--target-min) items-center">
        <SeverityMark severity={severity} />
      </span>
      <div className="min-w-0">
        {/* The detail sits beside the title while there's room; a long one (a parser's message) goes under it. */}
        <div className="flex flex-wrap items-center justify-between gap-x-3">
          <IssueCheckLink
            checkId={link.checkId}
            group={link.group}
            className="inline-flex min-h-(--target-min) items-center font-medium"
          >
            {checkTitle(check, ready)}
          </IssueCheckLink>
          {issue.detail ? (
            <span className="figures min-w-0 text-12 wrap-anywhere text-text-muted">{issue.detail}</span>
          ) : null}
        </div>
        {issue.related ? <WalkUrl id={issue.related.id} url={issue.related.url} /> : null}
      </div>
    </li>
  );
}

export function IssueList({
  issues,
  label,
  detail,
}: {
  issues: readonly UrlIssue[];
  label: string;
  /** The URL the issues are on: which group an issue is in can depend on it. */
  detail: UrlDetail;
}) {
  return (
    <ul aria-label={label} className="flex flex-col">
      {issues.map((issue, index) => (
        <IssueRow key={`${issue.checkId}-${index}`} issue={issue} detail={detail} />
      ))}
    </ul>
  );
}

/** Text longer than this (a stuffed description) folds to a few lines until it's asked for. */
const LONG_TEXT = 320;

/** A title, a description or an H1 as the page gives it. A very long one folds, so it never buries the rest. */
export function PageText({ text, what }: { text: string; what: string }) {
  const [open, setOpen] = useState(false);
  const long = text.length > LONG_TEXT;
  return (
    <div>
      <p className={long && !open ? 'line-clamp-4 text-14 wrap-anywhere text-text' : 'text-14 wrap-anywhere text-text'}>
        {text}
      </p>
      {long ? (
        <button
          type="button"
          className="si-link mt-1 min-h-(--target-min) text-12"
          aria-expanded={open}
          onClick={() => setOpen((value) => !value)}
        >
          {open ? 'Show less' : 'Show it all'} <span className="sr-only">of the {what}</span>
        </button>
      ) : null}
    </div>
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
