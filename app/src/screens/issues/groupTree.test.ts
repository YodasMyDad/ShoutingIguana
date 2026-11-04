import { describe, expect, it } from 'vitest';
import { FIRST_GROUP_ROWS, childRows, groupFilter, groupRefOf, sameGroup, treeLayout } from './groupTree';

describe('the groups as one treegrid', () => {
  it('is every group’s row while none is open', () => {
    const layout = treeLayout(3, null);
    expect(layout.rowCount).toBe(3);
    expect([0, 1, 2].map(layout.place)).toEqual([
      { kind: 'group', group: 0 },
      { kind: 'group', group: 1 },
      { kind: 'group', group: 2 },
    ]);
  });

  it('puts the open group’s rows under it, and moves the groups after it down', () => {
    const layout = treeLayout(4, { index: 1, children: 3 });
    expect(layout.rowCount).toBe(7);
    expect([0, 1, 2, 3, 4, 5, 6].map(layout.place)).toEqual([
      { kind: 'group', group: 0 },
      { kind: 'group', group: 1 },
      { kind: 'child', group: 1, child: 0 },
      { kind: 'child', group: 1, child: 1 },
      { kind: 'child', group: 1, child: 2 },
      { kind: 'group', group: 2 },
      { kind: 'group', group: 3 },
    ]);
    expect([0, 1, 2, 3].map(layout.rowOfGroup)).toEqual([0, 1, 5, 6]);
  });

  it('ignores an open group past the end (the groups changed under the route)', () => {
    expect(treeLayout(2, { index: 5, children: 4 }).rowCount).toBe(2);
  });

  it('shows a group’s first pages, as many as it has up to the limit, then the link to all of them', () => {
    expect(childRows({ pages: 264 }, null)).toBe(FIRST_GROUP_ROWS + 1);
    expect(childRows({ pages: 3 }, null)).toBe(4);
    // Once they've come, as many as came.
    expect(childRows({ pages: 264 }, 7)).toBe(8);
  });
});

describe('a group as the route names it', () => {
  it('is its detail and related URL, null matching null', () => {
    const group = { detail: null, relatedUrlId: 1 };
    expect(groupRefOf({ ...group, pages: 264 } as never)).toEqual(group);
    expect(sameGroup(group, { detail: null, relatedUrlId: 1 })).toBe(true);
    expect(sameGroup(group, { detail: null, relatedUrlId: 2 })).toBe(false);
    expect(sameGroup(group, undefined)).toBe(false);
  });

  it('opens in the URL grid as the check filter with the group', () => {
    expect(groupFilter('empty-anchor-text', { detail: null, relatedUrlId: 1 })).toEqual([
      { column: 'check', op: 'in', values: ['empty-anchor-text'], group: { detail: null, relatedUrlId: 1 } },
    ]);
  });
});
