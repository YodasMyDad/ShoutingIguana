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

  it('never takes how a URL answered for a template fault, however the analysis wrote the status', () => {
    const twelve = (detail: string) => Array.from({ length: 12 }, (_, index) => ({ id: index + 1, detail }));
    const likely = (detail: string) =>
      issueGroups('internal-client-error', 'detail', twelve(detail), 0, 100).groups.map(
        (group) => group.likelyTemplate,
      );

    // As the engine writes a status since the score, as it wrote one before, and a code with no standard name.
    expect(likely('404 Not Found')).toEqual([false]);
    expect(likely('HTTP 404')).toEqual([false]);
    expect(likely('HTTP 599')).toEqual([false]);
    // A fault in the page's own HTML, shared by most of the check's pages, is one.
    expect(likely('Missing og:image')).toEqual([true]);
  });
});
