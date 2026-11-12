// The probes of the site's other versions (E3c's `siteVersions`): the status breakdown is of the site's own URLs, live
// and done alike, with the probes said apart, every figure opening exactly its URLs by the contract's filters
// (docs/v2-sidecar-api.md, Progress).
import { describe, expect, it } from 'vitest';
import type { StatusClassCounts } from '../../api/types';
import {
  crawledCount,
  probeCountsOf,
  splitFilters,
  statusSplit,
  statusSplitFromFacets,
  type StatusFacets,
} from './statusSplit';

const sum = (values: readonly number[]) => values.reduce((total, value) => total + value, 0);

// A crawl of a site served over http only, a few seconds in: its https probe got no answer (a network error), and its
// www probe redirected to the site. Both are in every figure of the event, and `siteVersions` says so.
const counted: StatusClassCounts = { '2xx': 40, '3xx': 3, '4xx': 1, '5xx': 0, other: 0, noResponse: 1, blocked: 0 };
const codes = { '200': 40, '301': 3, '404': 1 };
const reasons = { robotsDisallowed: 0, timeout: 0, networkError: 1, notChecked: 0, processingError: 0 };
const probes = {
  fetched: 2,
  statusClasses: { '2xx': 0, '3xx': 1, '4xx': 0, '5xx': 0, other: 0, noResponse: 1, blocked: 0 },
  statusCodes: { '301': 1 },
  noResponse: { robotsDisallowed: 0, timeout: 0, networkError: 1, notChecked: 0, processingError: 0 },
};
const NOT_PROBE = { column: 'source', op: 'notIn', values: ['siteVersion'] };
const PROBES = [
  { column: 'source', op: 'in', values: ['siteVersion'] },
  { column: 'outcome', op: 'isNotEmpty' },
];

describe('the probes of the site’s other versions', () => {
  it('are left out of every class while the crawl runs, so an http-only site shows no network error', () => {
    const split = statusSplit(counted, codes, reasons, null, probes);
    const row = (key: string) => split.segments.find((segment) => segment.key === key);
    expect(row('noResponse')?.count).toBe(0);
    expect(row('noResponse')?.parts).toEqual([]);
    expect(row('3xx')?.count).toBe(2);
    expect(row('3xx')?.parts).toEqual([{ label: '301', count: 2 }]);
    expect(split.total).toBe(43);
    // Said apart, and opening exactly them: the probes with an outcome.
    expect(split.probes).toEqual({ count: 2, link: { to: 'urls', filters: PROBES } });
    // The site's own URLs crawled and the probes are every URL crawled.
    expect(split.total + (split.probes?.count ?? 0)).toBe(sum(Object.values(counted)));
  });

  it('are left out of each class’s link, by the contract’s filters', () => {
    const split = statusSplit(counted, codes, reasons, null, probes);
    for (const segment of split.segments) expect(segment.link.filters).toContainEqual(NOT_PROBE);
    expect(splitFilters('3xx', true)).toEqual([
      { column: 'statusClass', op: 'in', values: ['3xx'] },
      { column: 'outcome', op: 'notIn', values: ['blocked'] },
      NOT_PROBE,
    ]);
    expect(splitFilters('blocked', true)).toEqual([{ column: 'outcome', op: 'in', values: ['blocked'] }, NOT_PROBE]);
  });

  it('change nothing from an engine that doesn’t say which URLs they are, or before one has an outcome', () => {
    const none = {
      fetched: 0,
      statusClasses: { ...probes.statusClasses, '3xx': 0, noResponse: 0 },
      statusCodes: {},
      noResponse: { ...probes.noResponse, networkError: 0 },
    };
    for (const split of [statusSplit(counted, codes, reasons), statusSplit(counted, codes, reasons, null, none)]) {
      expect(split.probes).toBeNull();
      expect(split.total).toBe(45);
      for (const segment of split.segments) expect(segment.link.filters).not.toContainEqual(NOT_PROBE);
    }
  });

  it('that weren’t checked leave the URLs not checked, which are the site’s own', () => {
    const split = statusSplit({ ...counted, noResponse: 3 }, codes, { ...reasons, notChecked: 2 }, null, {
      ...probes,
      fetched: 3,
      statusClasses: { ...probes.statusClasses, noResponse: 2 },
      noResponse: { ...probes.noResponse, notChecked: 1 },
    });
    expect(split.notChecked).toEqual({
      count: 1,
      link: { to: 'urls', filters: [{ column: 'outcome', op: 'in', values: ['notChecked'] }, NOT_PROBE] },
    });
    expect(split.segments.find((segment) => segment.key === 'noResponse')?.count).toBe(0);
    expect(split.probes?.count).toBe(3);
  });

  it('are counted from their rows once the crawl is done, as the progress event counts them', () => {
    expect(
      probeCountsOf([
        { outcome: 'networkError', statusClass: 'noResponse', statusCode: null },
        { outcome: 'redirect', statusClass: '3xx', statusCode: 301 },
        { outcome: 'blocked', statusClass: '4xx', statusCode: 403 },
        { outcome: 'notChecked', statusClass: null, statusCode: null },
        { outcome: null, statusClass: null, statusCode: null },
      ]),
    ).toEqual({
      fetched: 4,
      statusClasses: { '2xx': 0, '3xx': 1, '4xx': 0, '5xx': 0, other: 0, noResponse: 2, blocked: 1 },
      statusCodes: { '301': 1 },
      noResponse: { robotsDisallowed: 0, timeout: 0, networkError: 1, notChecked: 1, processingError: 0 },
    });
  });

  it('are left out of a finished crawl’s breakdown too, so live and done agree', () => {
    // The same crawl done: 50 URLs, the 2 probes among them, 5 only found; the grid has nothing blocked.
    const facets: StatusFacets = {
      notChecked: 0,
      matching: 50,
      total: 50,
      summary: { available: true, reason: null },
      unavailable: [],
      facets: {
        statusClass: [
          { value: '2xx', count: 40, filter: { column: 'statusClass', op: 'in', values: ['2xx'] } },
          { value: '3xx', count: 3, filter: { column: 'statusClass', op: 'in', values: ['3xx'] } },
          { value: '4xx', count: 1, filter: { column: 'statusClass', op: 'in', values: ['4xx'] } },
          { value: 'noResponse', count: 1, filter: { column: 'statusClass', op: 'in', values: ['noResponse'] } },
          { value: null, count: 5, filter: { column: 'statusClass', op: 'isEmpty' } },
        ],
        statusCode: [
          { value: 200, count: 40, filter: { column: 'statusCode', op: 'eq', value: 200 } },
          { value: 301, count: 3, filter: { column: 'statusCode', op: 'eq', value: 301 } },
          { value: 404, count: 1, filter: { column: 'statusCode', op: 'eq', value: 404 } },
        ],
      },
      probes,
    };
    const stats = { outcomes: { success: 40, redirect: 3, clientError: 1, networkError: 1 }, notCheckedCount: 0 };
    const done = statusSplitFromFacets(facets, stats);
    const live = statusSplit(counted, codes, reasons, null, probes);
    const shape = (split: typeof done) =>
      split.segments.map((segment) => [segment.key, segment.count, segment.parts, segment.link]);
    expect(shape(done)).toEqual(shape(live));
    expect(done.probes).toEqual(live.probes);
    // "N URLs crawled" is every URL crawled, as the grid's `fetched` lists them: the probes among them.
    expect(crawledCount(facets)).toBe(45);
    expect(done.notFetched?.count).toBe(5);
  });
});
