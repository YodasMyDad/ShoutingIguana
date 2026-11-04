// A check's groups as the rows of one treegrid: every group's row, and under the open one its first pages and the
// link to all of them. One group is open at a time (the route's `group`), so where any row is comes from two numbers:
// which group is open and how many rows it has. Pure, so the index arithmetic is unit-tested.
import type { CheckGroupRef, GridFilter, IssueGroup } from '../../api/grids';

/** Pages shown under an open group before "Show all N pages in URLs". */
export const FIRST_GROUP_ROWS = 10;

export interface OpenGroup {
  /** The group's position among the check's groups (largest first). */
  index: number;
  /** Rows under it: its first pages, then the link to all of them. */
  children: number;
}

export type TreePlace =
  | { kind: 'group'; group: number }
  /** A row under the open group: `child` from 0; the last child is the link to all its pages. */
  | { kind: 'child'; group: number; child: number };

export interface TreeLayout {
  rowCount: number;
  place: (row: number) => TreePlace;
  /** The row a group's own row is on. */
  rowOfGroup: (group: number) => number;
}

export function treeLayout(groupCount: number, open: OpenGroup | null): TreeLayout {
  const openAt = open && open.index < groupCount ? open : null;
  const children = openAt?.children ?? 0;
  return {
    rowCount: groupCount + children,
    place: (row) => {
      if (!openAt || row <= openAt.index) return { kind: 'group', group: row };
      if (row <= openAt.index + children) return { kind: 'child', group: openAt.index, child: row - openAt.index - 1 };
      return { kind: 'group', group: row - children };
    },
    rowOfGroup: (group) => (openAt && group > openAt.index ? group + children : group),
  };
}

/** The rows under an open group: its first pages (as many as came, or as it has until they come), and the link. */
export function childRows(group: Pick<IssueGroup, 'pages'>, loaded: number | null): number {
  return (loaded ?? Math.min(group.pages, FIRST_GROUP_ROWS)) + 1;
}

/** A group as the route and `/rows` name it. A `relatedUrl` check's group is its related URL alone. */
export function groupRefOf(group: Pick<IssueGroup, 'detail' | 'relatedUrlId'>): CheckGroupRef {
  return { detail: group.detail, relatedUrlId: group.relatedUrlId };
}

export function sameGroup(a: CheckGroupRef | null | undefined, b: CheckGroupRef | null | undefined): boolean {
  return !!a && !!b && a.detail === b.detail && a.relatedUrlId === b.relatedUrlId;
}

/** The URL grid filtered to one group of a check: "Check is …" with its group, chips that read as sentences there. */
export function groupFilter(checkId: string, group: CheckGroupRef): GridFilter[] {
  return [{ column: 'check', op: 'in', values: [checkId], group }];
}
