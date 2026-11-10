import { describe, expect, it } from 'vitest';
import type { StatusClassCounts } from '../../api/types';
import { formatCount } from '../../lib/format';
import {
  MIN_REST_PERCENT,
  MIN_SEGMENT_PERCENT,
  crawledCount,
  partsText,
  sharePercents,
  splitFilters,
  statusSplit,
  statusSplitFromFacets,
  type StatusFacets,
} from './statusSplit';

const sum = (values: readonly number[]) => values.reduce((total, value) => total + value, 0);

// The garden crawl at 60%: 2,713 URLs, 20 of them not 2xx.
const GARDEN: StatusClassCounts = { '2xx': 2693, '3xx': 8, '4xx': 8, '5xx': 0, other: 0, noResponse: 2, blocked: 2 };

describe('the bars’ widths', () => {
  it('are in proportion and add up to 100', () => {
    expect(sharePercents([50, 30, 20], 2)).toEqual([50, 30, 20]);
    expect(sum(sharePercents([7, 11, 13], 2))).toBeCloseTo(100);
  });

  it('never let a part with anything in it fall under the least, which the larger parts give up', () => {
    const widths = sharePercents([9_990, 9, 1], 2.5);
    expect(widths[1]).toBe(2.5);
    expect(widths[2]).toBe(2.5);
    expect(widths[0]).toBeCloseTo(95);
    expect(sum(widths)).toBeCloseTo(100);
  });

  it('raise a part that only falls short once another has been raised', () => {
    // 3% of the bar before the 1 is raised to 10%, but under 10% of what's left after.
    const widths = sharePercents([890, 105, 1], 10);
    expect(widths[2]).toBe(10);
    expect(widths[1]).toBe(10);
    expect(widths[0]).toBeCloseTo(80);
  });

  it('give nothing to a part of nothing, and share equally when there isn’t room for the least', () => {
    expect(sharePercents([4, 0, 6], 2)).toEqual([40, 0, 60]);
    expect(sharePercents([0, 0], 2)).toEqual([0, 0]);
    expect(sharePercents([1, 100, 5], 40)).toEqual([100 / 3, 100 / 3, 100 / 3]);
    expect(sharePercents([Number.NaN, -3, 2], 2)).toEqual([0, 0, 100]);
  });
});

describe('the status breakdown', () => {
  const split = statusSplit(
    GARDEN,
    { '200': 2693, '301': 8, '404': 8 },
    { robotsDisallowed: 2, timeout: 0, networkError: 0, notChecked: 0, processingError: 0 },
  );

  it('adds up: every URL fetched is in one class', () => {
    expect(split.total).toBe(2713);
    expect(split.ok).toBe(2693);
    expect(split.rest).toBe(20);
    expect(sum(split.segments.map((segment) => segment.count))).toBe(split.total);
  });

  it('lists the classes in order, even at 0, and codes outside 200 to 599 only once there are some', () => {
    expect(split.segments.map((segment) => [segment.label, segment.count])).toEqual([
      ['2xx', 2693],
      ['3xx', 8],
      ['4xx', 8],
      ['5xx', 0],
      ['No response', 2],
      ['Blocked', 2],
    ]);
    const plain = statusSplit({ ...GARDEN, blocked: 0, other: 3 }, null, null);
    expect(plain.segments.map((segment) => segment.label)).toEqual([
      '2xx',
      '3xx',
      '4xx',
      '5xx',
      'Other codes',
      'No response',
      'Blocked',
    ]);
  });

  it('keeps the part that didn’t answer 2xx visible in the full bar, and magnifies it beneath', () => {
    // 20 of 2,713 is 0.7%: too thin to see, so it gets the least.
    expect(split.whole.rest).toBe(MIN_REST_PERCENT);
    expect(split.whole.ok + split.whole.rest).toBeCloseTo(100);
    expect(split.magnified.map((part) => part.key)).toEqual(['3xx', '4xx', 'noResponse', 'blocked']);
    expect(sum(split.magnified.map((part) => part.percent))).toBeCloseTo(100);
    // 8, 8, 2 and 2 of 20.
    expect(split.magnified.map((part) => Math.round(part.percent))).toEqual([40, 40, 10, 10]);
    for (const part of split.magnified) expect(part.percent).toBeGreaterThanOrEqual(MIN_SEGMENT_PERCENT);
  });

  it('has nothing to magnify when everything answered 2xx, and draws nothing for a crawl that fetched nothing', () => {
    const clean = statusSplit({ ...GARDEN, '3xx': 0, '4xx': 0, noResponse: 0, blocked: 0 }, null, null);
    expect(clean.whole).toEqual({ ok: 100, rest: 0 });
    expect(clean.magnified).toEqual([]);
    const nothing = statusSplit(
      { '2xx': 0, '3xx': 0, '4xx': 0, '5xx': 0, other: 0, noResponse: 0, blocked: 0 },
      null,
      null,
    );
    expect(nothing.whole).toEqual({ ok: 0, rest: 0 });
    expect(nothing.total).toBe(0);
  });

  it('says what each class is made of, the most first', () => {
    const mixed = statusSplit(
      { ...GARDEN, '3xx': 16, '4xx': 11, other: 1 },
      { '200': 2693, '301': 12, '302': 4, '404': 9, '410': 2, '999': 1 },
      { robotsDisallowed: 1, timeout: 3, networkError: 0, notChecked: 0, processingError: 0 },
    );
    const parts = (label: string) => mixed.segments.find((segment) => segment.label === label)?.parts;
    expect(parts('3xx')).toEqual([
      { label: '301', count: 12 },
      { label: '302', count: 4 },
    ]);
    expect(parts('4xx')).toEqual([
      { label: '404', count: 9 },
      { label: '410', count: 2 },
    ]);
    expect(parts('Other codes')).toEqual([{ label: '999', count: 1 }]);
    expect(parts('No response')).toEqual([
      { label: 'Refused by robots.txt', count: 1 },
      { label: 'Timed out', count: 3 },
    ]);
    expect(parts('Blocked')).toEqual([]);
  });

  it('links each class to exactly its URLs: by class without the blocked, and the blocked by their outcome', () => {
    expect(splitFilters('4xx')).toEqual([
      { column: 'statusClass', op: 'in', values: ['4xx'] },
      { column: 'outcome', op: 'notIn', values: ['blocked'] },
    ]);
    expect(splitFilters('blocked')).toEqual([{ column: 'outcome', op: 'in', values: ['blocked'] }]);
    expect(split.segments.find((segment) => segment.key === 'noResponse')?.link).toEqual({
      to: 'urls',
      filters: [
        { column: 'statusClass', op: 'in', values: ['noResponse'] },
        { column: 'outcome', op: 'notIn', values: ['blocked'] },
      ],
    });
  });

  // A crawl of the engine's test site with a dead twin host, as its last progress event had it: 6,914 outcomes
  // stored, 5 of them URLs never asked because their host had stopped answering. The URL grid has those 5 as not
  // fetched, with no status class: 6,909 fetched, 13 with no response.
  const DEAD_TWIN: StatusClassCounts = {
    '2xx': 6326,
    '3xx': 264,
    '4xx': 277,
    '5xx': 28,
    other: 0,
    noResponse: 18,
    blocked: 1,
  };
  const DEAD_TWIN_REASONS = { robotsDisallowed: 10, timeout: 0, networkError: 3, notChecked: 5, processingError: 0 };

  it('leaves the URLs that weren’t checked out of No response and of the total, and says them apart', () => {
    const live = statusSplit(
      DEAD_TWIN,
      { '200': 6326, '301': 203, '302': 61, '404': 277, '500': 28 },
      DEAD_TWIN_REASONS,
    );
    // What the grid counts under "fetched is true", and under the No response class's link.
    expect(live.total).toBe(6909);
    const noResponse = live.segments.find((segment) => segment.key === 'noResponse');
    expect(noResponse?.count).toBe(13);
    expect(noResponse?.link.filters).toEqual(splitFilters('noResponse'));
    // Its parts add up to it: not checked isn't a reason a URL asked for got no response.
    expect(noResponse?.parts).toEqual([
      { label: 'Refused by robots.txt', count: 10 },
      { label: 'Network error', count: 3 },
    ]);
    expect(sum(live.segments.map((segment) => segment.count))).toBe(live.total);
    expect(live.rest).toBe(583);
    expect(live.notChecked).toEqual({
      count: 5,
      link: { to: 'urls', filters: [{ column: 'outcome', op: 'in', values: ['notChecked'] }] },
    });
  });

  it('has none apart when none weren’t checked, or when the stream didn’t say why URLs got no response', () => {
    expect(statusSplit(GARDEN, null, { ...DEAD_TWIN_REASONS, notChecked: 0 }).notChecked).toBeNull();
    const unsaid = statusSplit(DEAD_TWIN, null, null);
    expect(unsaid.notChecked).toBeNull();
    expect(unsaid.total).toBe(6914);
    // Never more than the class that holds them.
    expect(statusSplit(GARDEN, null, { ...DEAD_TWIN_REASONS, notChecked: 9 }).notChecked?.count).toBe(2);
  });

  it('writes the parts as a short list', () => {
    const parts = [
      { label: '404', count: 4575 },
      { label: '403', count: 2 },
      { label: '410', count: 1 },
      { label: '429', count: 1 },
    ];
    expect(partsText(parts, formatCount)).toBe('404 × 4,575, 403 × 2, 410 × 1 and 1 more');
    expect(partsText(parts.slice(0, 2), formatCount)).toBe('404 × 4,575, 403 × 2');
    expect(partsText([], formatCount)).toBe('');
  });
});

describe('a finished crawl’s breakdown, from the URL facets', () => {
  // The garden crawl as the sidecar answers it under "outcome is not blocked": 6,284 of 6,286 URLs match.
  const facets: StatusFacets = {
    notChecked: 0,
    matching: 6284,
    total: 6286,
    summary: { available: true, reason: null },
    unavailable: [],
    facets: {
      statusClass: [
        { value: '2xx', count: 4492, filter: { column: 'statusClass', op: 'in', values: ['2xx'] } },
        { value: '3xx', count: 16, filter: { column: 'statusClass', op: 'in', values: ['3xx'] } },
        { value: '4xx', count: 9, filter: { column: 'statusClass', op: 'in', values: ['4xx'] } },
        { value: 'noResponse', count: 2, filter: { column: 'statusClass', op: 'in', values: ['noResponse'] } },
        { value: null, count: 1765, filter: { column: 'statusClass', op: 'isEmpty' } },
      ],
      statusCode: [
        { value: 200, count: 4492, filter: { column: 'statusCode', op: 'eq', value: 200 } },
        { value: 301, count: 12, filter: { column: 'statusCode', op: 'eq', value: 301 } },
        { value: 302, count: 4, filter: { column: 'statusCode', op: 'eq', value: 302 } },
        { value: 404, count: 9, filter: { column: 'statusCode', op: 'eq', value: 404 } },
        { value: null, count: 1767, filter: { column: 'statusCode', op: 'isEmpty' } },
      ],
    },
  };

  it('counts the blocked as what the filter left out, so the classes add up to the URLs fetched', () => {
    const split = statusSplitFromFacets(facets);
    expect(split.segments.map((segment) => [segment.label, segment.count])).toEqual([
      ['2xx', 4492],
      ['3xx', 16],
      ['4xx', 9],
      ['5xx', 0],
      ['No response', 2],
      ['Blocked', 2],
    ]);
    // The library's "URLs" for this crawl.
    expect(split.total).toBe(4521);
    expect(split.notFetched).toEqual({
      count: 1765,
      link: { to: 'urls', filters: [{ column: 'statusClass', op: 'isEmpty' }] },
    });
  });

  it('says why there was no response once the crawl’s stats arrive, and not before', () => {
    const noResponse = (split: ReturnType<typeof statusSplitFromFacets>) =>
      split.segments.find((segment) => segment.key === 'noResponse')?.parts;
    expect(noResponse(statusSplitFromFacets(facets))).toEqual([]);
    expect(
      noResponse(
        statusSplitFromFacets(facets, {
          outcomes: { success: 4492, blocked: 2, robotsDisallowed: 2 },
          notCheckedCount: 0,
        }),
      ),
    ).toEqual([{ label: 'Refused by robots.txt', count: 2 }]);
  });

  // The dead-twin crawl once it's done, as the sidecar answers the breakdown's two reads and `/stats`: the 5 URLs
  // that weren't checked are among the 145 with no status class, and among `/stats`'s 8 network errors.
  const deadTwin: StatusFacets = {
    notChecked: 5,
    matching: 7053,
    total: 7054,
    summary: { available: true, reason: null },
    unavailable: [],
    facets: {
      statusClass: [
        { value: '2xx', count: 6326, filter: { column: 'statusClass', op: 'in', values: ['2xx'] } },
        { value: '3xx', count: 264, filter: { column: 'statusClass', op: 'in', values: ['3xx'] } },
        { value: '4xx', count: 277, filter: { column: 'statusClass', op: 'in', values: ['4xx'] } },
        { value: '5xx', count: 28, filter: { column: 'statusClass', op: 'in', values: ['5xx'] } },
        { value: 'noResponse', count: 13, filter: { column: 'statusClass', op: 'in', values: ['noResponse'] } },
        { value: null, count: 145, filter: { column: 'statusClass', op: 'isEmpty' } },
      ],
    },
  };
  const deadTwinStats = {
    notCheckedCount: 5,
    outcomes: {
      success: 6326,
      redirect: 264,
      clientError: 277,
      serverError: 28,
      blocked: 1,
      robotsDisallowed: 10,
      networkError: 8,
    },
  };

  it('says the URLs that weren’t checked apart from the ones only found, each opening exactly its URLs', () => {
    const split = statusSplitFromFacets(deadTwin, deadTwinStats);
    expect(split.total).toBe(6909);
    expect(crawledCount(deadTwin)).toBe(6909);
    expect(split.notChecked).toEqual({
      count: 5,
      link: { to: 'urls', filters: [{ column: 'outcome', op: 'in', values: ['notChecked'] }] },
    });
    // The 140 not followed: no status class, and not one of the 5.
    expect(split.notFetched).toEqual({
      count: 140,
      link: {
        to: 'urls',
        filters: [
          { column: 'statusClass', op: 'isEmpty' },
          { column: 'outcome', op: 'isEmpty' },
        ],
      },
    });
    // The row's reasons add up to its count: `/stats` has the 5 among its network errors.
    const noResponse = split.segments.find((segment) => segment.key === 'noResponse');
    expect(noResponse?.count).toBe(13);
    expect(noResponse?.parts).toEqual([
      { label: 'Refused by robots.txt', count: 10 },
      { label: 'Network error', count: 3 },
    ]);
    expect(sum(noResponse?.parts.map((part) => part.count) ?? [])).toBe(noResponse?.count);
    // Before the stats arrive the figures are the same: only the reasons are missing.
    const early = statusSplitFromFacets(deadTwin);
    expect([early.total, early.notChecked?.count, early.notFetched?.count]).toEqual([6909, 5, 140]);
  });
});
