// Where an issue of the inspected URL leads on the Issues screen: its check, with the group the issue is in open
// (docs/v2-sidecar-api.md, Issue groups). The engine says which group that is (`group` on each of the inspector's
// issues). An engine from before it said so leaves it out; then the group is worked out as the contract describes
// them, and a link is given a group only when that group is sure to be there with the URL in it:
// - the checks that group by the fault a detail is an instance of, whose groups can't be told from an issue's own
//   detail, lead to the check alone;
// - the duplicate checks group by cluster, each named after its first page, which is in it too. That page's own issue
//   names another page of the cluster, so its group is the page itself: it's the first when the other pages' issues
//   name it (`namedBy`). An analysis by another version of the engine may have grouped them the older way (by each
//   issue's own other URL), so its duplicates lead to the check alone.
import type { CheckGroupRef, CheckGrouping } from '../api/grids';
import type { UrlDetail, UrlIssue } from '../api/inspector';

/** The `detail` checks whose groups are the fault each page's detail is an instance of, not the detail itself. */
export const FAULT_GROUPED_CHECKS: readonly string[] = [
  'canonical-relative',
  'canonical-invalid',
  'structured-data-invalid',
  'pagination-sequence-error',
  'head-tags-in-body',
];

/** The checks whose groups are clusters of pages that share something, each named after its first page. */
export const CLUSTER_GROUPED_CHECKS: readonly string[] = [
  'title-duplicate',
  'description-duplicate',
  'h1-duplicate',
  'duplicate-content',
  'near-duplicate-content',
];

/** What of the URL an issue is on says which group the issue is in. */
export type IssueSubject = Pick<UrlDetail, 'id' | 'namedBy' | 'summary'>;

export interface IssueLink {
  checkId: string;
  /** The group to open on the check; undefined for the check alone. */
  group: CheckGroupRef | undefined;
}

/** The cluster a duplicate check's issue is in: the first page's id, or undefined when it can't be told. */
function clusterOf(issue: UrlIssue, subject: IssueSubject | undefined): number | undefined {
  if (!subject || subject.summary.reason === 'engineChanged') return undefined;
  const first = subject.namedBy?.checks.some((entry) => entry.checkId === issue.checkId) ?? false;
  return first ? subject.id : issue.related?.id;
}

/**
 * The Issues route for an issue. `grouping` is the catalog's word for how the check groups (`GET /api/checks`):
 * unknown, the link is to the check alone. `subject` is the URL the issue is on.
 */
export function issueLink(issue: UrlIssue, grouping: CheckGrouping | undefined, subject?: IssueSubject): IssueLink {
  const { checkId } = issue;
  if (issue.group !== undefined) return { checkId, group: issue.group ?? undefined };
  if (grouping === 'relatedUrl' && CLUSTER_GROUPED_CHECKS.includes(checkId)) {
    const first = clusterOf(issue, subject);
    return { checkId, group: first === undefined ? undefined : { detail: null, relatedUrlId: first } };
  }
  if (grouping === 'relatedUrl') return { checkId, group: { detail: null, relatedUrlId: issue.related?.id ?? null } };
  if (grouping === 'detail' && !FAULT_GROUPED_CHECKS.includes(checkId)) {
    return { checkId, group: { detail: issue.detail, relatedUrlId: issue.related?.id ?? null } };
  }
  return { checkId, group: undefined };
}
