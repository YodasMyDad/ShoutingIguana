import { describe, expect, it } from 'vitest';
import { issueGroups } from './mockTransport';

describe('issue groups', () => {
  it('gives a skipped check no groups and a null count, never 0', () => {
    expect(issueGroups('title-too-long', 'detail', null, 0, 100)).toMatchObject({
      total: null,
      groupCount: null,
      groups: [],
    });
  });

  it('gives a check that doesn’t group a count of 0', () => {
    expect(issueGroups('slow-response', 'none', [{ id: 1, detail: '1,250 ms' }], 0, 100)).toMatchObject({
      total: 1,
      groupCount: 0,
      groups: [],
    });
  });
});
