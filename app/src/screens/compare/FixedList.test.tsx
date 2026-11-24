import { QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { FixedQuery } from '../../api/fixLoop';
import { setTransport } from '../../api/transport';
import { createQueryClient } from '../../app/queryClient';
import { TEST_CRAWL_ID, fakeSidecar, json } from '../../test/fakeSidecar';
import { FixedList } from './FixedList';

const EARLIER = '0123456789abcdef';
const COMPARED = { with: EARLIER, startedAt: '2026-11-24T07:03:59.158Z', comparable: 'full' };

function show(rows: unknown[]) {
  setTransport(
    fakeSidecar([
      [
        'POST',
        /\/issues\/internal-client-error\/fixed$/,
        (request) =>
          (request.body as FixedQuery).level === 'groups'
            ? json({
                checkId: 'internal-client-error',
                compared: COMPARED,
                level: 'groups',
                total: 0,
                offset: 0,
                groups: [],
              })
            : json({
                checkId: 'internal-client-error',
                compared: COMPARED,
                level: 'rows',
                total: rows.length,
                offset: 0,
                rows,
              }),
      ],
    ]),
  );
  render(
    <QueryClientProvider client={createQueryClient()}>
      <FixedList
        crawlId={TEST_CRAWL_ID}
        check={{
          id: 'internal-client-error',
          grouping: 'detail',
          references: 'sources',
          title: 'Internal URL returns 4xx',
        }}
        compareWith={EARLIER}
        period={{ iso: '2026-11-24T07:03:59.158Z', later: false }}
        siteOrigin="https://example.com"
        onInspect={() => undefined}
      />
    </QueryClientProvider>,
  );
}

describe('the Fixed tab of a check that groups', () => {
  // The audit of 2026-11-24: ten 404s were fixed while others still answered 404, so no cause was gone and the tab,
  // counting 10, said only "No cause it had since 24 nov is gone."
  it('lists the URLs it no longer flags when every cause it had is still there', async () => {
    show([
      {
        url: 'https://example.com/broken/29',
        urlId: 693,
        thenUrlId: 702,
        state: 'fixed',
        reason: null,
        detailThen: '404 Not Found',
        relatedUrlThen: null,
      },
    ]);
    expect(await screen.findByRole('button', { name: 'Inspect https://example.com/broken/29' })).toBeVisible();
    expect(screen.getByText('404 Not Found')).toBeVisible();
    expect(screen.queryByText(/No cause/)).toBeNull();
  });

  it('says so with the date as it is written when nothing is gone at all', async () => {
    show([]);
    expect(await screen.findByText('Nothing it flagged since 24 Nov is gone.')).toBeVisible();
  });
});
