// Where an issue of the inspected URL leads on the Issues screen: its check, with the group the issue is in open
// (docs/v2-sidecar-api.md, Issue groups). The engine says which group that is (`group` on each of the inspector's
// issues). An engine from before it said so leaves it out; then the group is worked out as the contract describes
// them, except for the checks that group by the fault a detail is an instance of, whose groups can't be told from
// an issue's own detail: those lead to the check alone.
import type { CheckGroupRef, CheckGrouping } from '../api/grids';
import type { UrlIssue } from '../api/inspector';

/** The `detail` checks whose groups are the fault each page's detail is an instance of, not the detail itself. */
export const FAULT_GROUPED_CHECKS: readonly string[] = [
  'canonical-relative',
  'canonical-invalid',
  'structured-data-invalid',
];

export interface IssueLink {
  checkId: string;
  /** The group to open on the check; undefined for the check alone. */
  group: CheckGroupRef | undefined;
}

/**
 * The Issues route for an issue. `grouping` is the catalog's word for how the check groups (`GET /api/checks`):
 * unknown, the link is to the check alone.
 */
export function issueLink(issue: UrlIssue, grouping: CheckGrouping | undefined): IssueLink {
  const { checkId } = issue;
  if (issue.group !== undefined) return { checkId, group: issue.group ?? undefined };
  if (grouping === 'relatedUrl') return { checkId, group: { detail: null, relatedUrlId: issue.related?.id ?? null } };
  if (grouping === 'detail' && !FAULT_GROUPED_CHECKS.includes(checkId)) {
    return { checkId, group: { detail: issue.detail, relatedUrlId: issue.related?.id ?? null } };
  }
  return { checkId, group: undefined };
}
