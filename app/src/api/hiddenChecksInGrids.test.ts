// The grids' issue counts leave the hidden checks out, as every other count does (round 5 review, also noted): each
// read of a grid, its facets and its exports sends the checks hidden in its crawl, and a change to them reads the
// grids again.
import { QueryClient } from '@tanstack/react-query';
import { afterEach, describe, expect, it } from 'vitest';
import { replaceSettings } from '../settings/settingsStore';
import { fakeSidecar, json } from '../test/fakeSidecar';
import { api } from './client';
import { watchHiddenChecks } from './hiddenChecksRefresh';
import { setTransport } from './transport';

const CRAWL = '0123456789abcdef';
const PAGE = { rows: [], total: 0, matching: 0 };

afterEach(() => {
  replaceSettings({ hiddenChecks: [], hiddenChecksByCrawl: {} });
  setTransport(fakeSidecar([]));
});

function sidecar() {
  const fake = fakeSidecar([
    ['POST', /./, () => json(PAGE)],
    ['GET', /./, () => json(PAGE)],
  ]);
  setTransport(fake);
  return fake;
}

describe('the grids’ reads with hidden checks', () => {
  it('send the checks hidden in the crawl, in every crawl and its own, with every grid read', async () => {
    replaceSettings({ hiddenChecks: ['thin-content'], hiddenChecksByCrawl: { [CRAWL]: ['title-too-long'] } });
    const fake = sidecar();
    const query = { columns: ['url', 'issues'], sort: [], filters: [], offset: 0, limit: 10 };
    await api.urls(CRAWL, query);
    await api.urlFacets(CRAWL, { columns: ['severity'], filters: [] } as never);
    await api.issueRows(CRAWL, 'title-too-long', query);
    await api.links(CRAWL, query);
    await api.linkFacets(CRAWL, { columns: ['kind'], filters: [] } as never);
    await api.linkEnds(CRAWL, { end: 'target', filters: [], limit: 1 } as never);
    await api.sitemapUrls(CRAWL, 3, query);
    await api.startExport(CRAWL, { kind: 'urls', format: 'csv', path: 'C:/x.csv', ...query });
    for (const request of fake.requests) {
      expect(request.body, request.path).toMatchObject({ hiddenChecks: ['thin-content', 'title-too-long'] });
    }
    expect(fake.requests).toHaveLength(8);
  });

  it('send nothing new while no check is hidden, and none to an export that isn’t a grid’s', async () => {
    const fake = sidecar();
    await api.urls(CRAWL, { columns: ['url'], sort: [], filters: [], offset: 0, limit: 10 });
    replaceSettings({ hiddenChecks: ['thin-content'] });
    await api.startExport(CRAWL, { kind: 'check', format: 'csv', checkId: 'thin-content', path: 'C:/x.csv' });
    expect(fake.requests.map((request) => request.body)).toEqual([
      { columns: ['url'], sort: [], filters: [], offset: 0, limit: 10 },
      { kind: 'check', format: 'csv', checkId: 'thin-content', path: 'C:/x.csv' },
    ]);
  });

  it('read again once the hidden checks change, and not for any other setting', () => {
    const queryClient = new QueryClient();
    const stop = watchHiddenChecks(queryClient);
    queryClient.setQueryData(['grid', 'urls', CRAWL, {}], PAGE);
    queryClient.setQueryData(['crawl', CRAWL, 'facets', {}], PAGE);
    queryClient.setQueryData(['crawl', CRAWL], {});
    const stale = (key: readonly unknown[]) => queryClient.getQueryState(key)?.isInvalidated;
    replaceSettings({ sidebarCollapsed: true });
    expect(stale(['grid', 'urls', CRAWL, {}])).toBe(false);
    replaceSettings({ hiddenChecksByCrawl: { [CRAWL]: ['thin-content'] } });
    expect(stale(['grid', 'urls', CRAWL, {}])).toBe(true);
    expect(stale(['crawl', CRAWL, 'facets', {}])).toBe(true);
    expect(stale(['crawl', CRAWL])).toBe(false);
    stop();
  });
});
