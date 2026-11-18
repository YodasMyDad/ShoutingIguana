import { QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import type { ShopifyAccess } from '../api/types';
import { TEST_CRAWL_ID, fakeSidecar, json } from '../test/fakeSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { useShopifyPromptOnProblem } from './shopifyPromptPlace';

const STORE: ShopifyAccess = {
  host: 'shop.example',
  signal: 'powered-by: Shopify',
  notCheckedUnsigned: 3,
  notCheckedSigned: 0,
  urlIds: [11, 12, 13],
  reasons: [],
};

function place(check: { id: string; subject: 'page' | 'url' }, access: ShopifyAccess | null, rowIds: number[] = []) {
  setTransport(
    fakeSidecar([
      ['GET', /^\/api\/crawls\/[0-9a-f]{16}\/shopify$/, () => json(access)],
      [
        'POST',
        /^\/api\/crawls\/[0-9a-f]{16}\/issues\/[a-z-]+\/rows$/,
        () =>
          json({
            rows: rowIds.map((id) => ({ id, url: `https://shop.example/p/${id}`, detail: null })),
            total: rowIds.length,
          }),
      ],
    ]),
  );
  const client = createQueryClientWithoutBackoff();
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return renderHook(() => useShopifyPromptOnProblem(TEST_CRAWL_ID, check), { wrapper });
}

describe('where the Shopify prompt belongs on a problem page', () => {
  afterEach(() => setTransport(fakeSidecar([])));

  it('isn’t on a problem whose pages Shopify didn’t refuse (cookies, robots.txt, images)', async () => {
    const { result } = place({ id: 'cookies-insecure', subject: 'page' }, STORE);
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(result.current).toBe(false);
    const robots = place({ id: 'blocked-by-robots', subject: 'url' }, STORE, [40, 41]);
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(robots.result.current).toBe(false);
  });

  it('is on a problem whose own addresses include pages Shopify refused', async () => {
    const { result } = place({ id: 'internal-client-error', subject: 'url' }, STORE, [5, 12]);
    await waitFor(() => expect(result.current).toBe(true));
  });

  it('is on a problem about pages that weren’t checked', async () => {
    const { result } = place({ id: 'internal-blocked', subject: 'url' }, STORE);
    await waitFor(() => expect(result.current).toBe(true));
  });

  it('is never there for a site that isn’t a refusing store', async () => {
    const { result } = place({ id: 'internal-blocked', subject: 'url' }, null);
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(result.current).toBe(false);
  });
});
