// "One fix" on a check's row (What to fix, the Summary): only when one shared cause covers every page the check
// flagged. The fixit audit found it on image-missing-dimensions, whose shared image covers 195 of its 225 pages.
import { QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import type { IssueGroup } from '../api/grids';
import { setTransport } from '../api/transport';
import { TEST_CRAWL_ID, fakeSidecar, json } from '../test/fakeSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { SIMPLE_CHECKS, groupPage } from '../test/simpleSidecar';
import type { CheckEntry } from '../screens/issues/checkList';
import { firstGroupQuery, useOneFix, useOneFixCovers } from './useOneFix';

const SITE = 'https://www.gardenfurnituresets.co.uk';

const template = (pages: number, share: number): IssueGroup => ({
  detail: null,
  relatedUrlId: 1,
  relatedUrl: `${SITE}/img/logo.png`,
  pages,
  urls: pages,
  share,
  likelyTemplate: true,
});

const entry = (checkId: string, count: number): CheckEntry => ({
  check: SIMPLE_CHECKS.checks.find((check) => check.id === checkId)!,
  outcome: 'warning',
  count,
  skipReason: null,
  hidden: false,
  causes: null,
  templateCauses: null,
  clears: [],
});

afterEach(() => setTransport(fakeSidecar([])));

describe('useOneFix', () => {
  it('marks a check whose shared cause covers every page, and not one it covers only most of', async () => {
    setTransport(
      fakeSidecar([
        [
          'POST',
          /\/issues\/image-missing-alt\/groups$/,
          () => json(groupPage('image-missing-alt', [template(40, 1)], 40)),
        ],
        [
          'POST',
          /\/issues\/image-missing-dimensions\/groups$/,
          () => json(groupPage('image-missing-dimensions', [template(195, 195 / 225)], 225)),
        ],
      ]),
    );
    const client = createQueryClientWithoutBackoff();
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    );
    const entries = [entry('image-missing-alt', 40), entry('image-missing-dimensions', 225)];
    const { result } = renderHook(() => useOneFix(TEST_CRAWL_ID, entries), { wrapper });
    // Both checks' largest groups read: the one covering 195 of 225 pages stays unmarked.
    await waitFor(() => {
      for (const { check } of entries) {
        expect(client.getQueryData(firstGroupQuery(TEST_CRAWL_ID, check.id).queryKey)).toBeDefined();
      }
    });
    await waitFor(() => expect([...result.current]).toEqual(['image-missing-alt']));
  });

  // The fixit page's item: the problem page says "One fix covers 195 of 225 pages"; a row can say it too.
  it('says how many pages a shared cause covers where it covers most of them but not all', async () => {
    setTransport(
      fakeSidecar([
        [
          'POST',
          /\/issues\/image-missing-alt\/groups$/,
          () => json(groupPage('image-missing-alt', [template(40, 1)], 40)),
        ],
        [
          'POST',
          /\/issues\/image-missing-dimensions\/groups$/,
          () => json(groupPage('image-missing-dimensions', [template(195, 195 / 225)], 225)),
        ],
        [
          'POST',
          /\/issues\/image-too-large\/groups$/,
          () => json(groupPage('image-too-large', [{ ...template(3, 3 / 60), likelyTemplate: false }], 60)),
        ],
      ]),
    );
    const client = createQueryClientWithoutBackoff();
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    );
    const entries = [
      entry('image-missing-alt', 40),
      entry('image-missing-dimensions', 225),
      entry('image-too-large', 60),
    ];
    const { result } = renderHook(() => useOneFixCovers(TEST_CRAWL_ID, entries), { wrapper });
    await waitFor(() => expect([...result.current]).toEqual([['image-missing-dimensions', 195]]));
  });
});
