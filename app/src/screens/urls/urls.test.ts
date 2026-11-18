import { Fragment, createElement, type ReactNode } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { FacetsPage, GridFilter, UrlRow } from '../../api/grids';
import { answerFacets, answerGridQuery } from '../../api/mock/gridQuery';
import { URL_COLUMNS } from '../../api/mock/urlColumns';
import { nextMultiSort } from '../../grid/sort';
import { testUrlRows } from '../../test/urlsSidecar';
import { withFacetValue } from '../links/facets';
import {
  DEFAULT_URL_COLUMNS,
  URL_MIN_WIDTH,
  URL_GRID_AT_1440,
  URL_PRESETS,
  activeUrlPreset,
  colsParam,
  columnsInView,
  shownUrlColumns,
  urlChooserGroups,
  urlColumnWidth,
  urlFilterColumns,
  urlGridColumns,
  urlPresetFits,
  urlQueryColumns,
  urlStatusWords,
} from './urlColumns';
import { buildUrlFacets, urlFacetValueWords } from './urlFacets';
import { urlIdsOf } from './useUrlFilterWords';

/** A cell as the grid draws it, to read its words. */
function render(node: ReactNode): HTMLElement {
  const element = document.createElement('div');
  element.innerHTML = renderToStaticMarkup(createElement(Fragment, null, node));
  return element;
}

describe('the URL grid’s presets', () => {
  it('lead with the URL, show more than the nine columns of before, and name only columns the engine has', () => {
    const ids = new Set(URL_COLUMNS.map((column) => column.id));
    for (const preset of URL_PRESETS) {
      expect(preset.columns[0]).toBe('url');
      expect(preset.columns.filter((id) => !ids.has(id))).toEqual([]);
      expect(new Set(preset.columns).size).toBe(preset.columns.length);
    }
    expect(DEFAULT_URL_COLUMNS.length).toBeGreaterThan(9);
    expect(URL_PRESETS.map((preset) => preset.title)).toEqual([
      'Overview',
      'Indexability',
      'Content',
      'Performance',
      'Links',
    ]);
  });

  it('fit the URL grid of a 1,440-pixel window, the URL at its least width or more (the fix-it pass)', () => {
    // The Indexability preset's Redirects to was off the right edge at 1,440 pixels, where the grid is 1,120 wide.
    for (const preset of URL_PRESETS) {
      expect({ preset: preset.id, fits: urlPresetFits(preset.columns, URL_COLUMNS, URL_GRID_AT_1440) }).toEqual({
        preset: preset.id,
        fits: true,
      });
    }
    // A redirect's target and a page's canonical lead the Indexability preset's reasons.
    const indexability = URL_PRESETS.find((preset) => preset.id === 'indexability')!.columns;
    expect(indexability).toEqual(expect.arrayContaining(['canonicalUrl', 'metaRobots', 'inSitemap', 'redirectTarget']));
  });

  it('reads as chosen exactly while the columns are its own, in its order', () => {
    expect(activeUrlPreset(DEFAULT_URL_COLUMNS)?.id).toBe('overview');
    const content = URL_PRESETS.find((preset) => preset.id === 'content')!;
    expect(activeUrlPreset(content.columns)?.id).toBe('content');
    expect(activeUrlPreset([...content.columns].reverse())).toBeNull();
    expect(activeUrlPreset([...content.columns, 'depth'])).toBeNull();
  });

  it('keeps the default set out of the address', () => {
    expect(colsParam(DEFAULT_URL_COLUMNS)).toBeUndefined();
    expect(colsParam(['url', 'title'])).toEqual(['url', 'title']);
  });
});

describe('the columns shown', () => {
  it('are the route’s, each once, without a URL’s id or a check’s own column, or the default set', () => {
    expect(shownUrlColumns(undefined, URL_COLUMNS)).toBe(DEFAULT_URL_COLUMNS);
    expect(shownUrlColumns(['url', 'title', 'url', 'id', 'detail', 'noSuchColumn'], URL_COLUMNS)).toEqual([
      'url',
      'title',
    ]);
    // Before the engine's list is read, the route's are taken as they are (but an id).
    expect(shownUrlColumns(['url', 'noSuchColumn'], undefined)).toEqual(['url', 'noSuchColumn']);
    expect(shownUrlColumns(['noSuchColumn'], URL_COLUMNS)).toBe(DEFAULT_URL_COLUMNS);
  });

  it('ask for the URL always, and with the status how the request went', () => {
    expect(urlQueryColumns(['title'])).toEqual(['url', 'title']);
    // With indexability, the type: a file isn't a page to judge.
    expect(urlQueryColumns(['url', 'indexability'])).toEqual(['url', 'indexability', 'type']);
    expect(urlQueryColumns(['url', 'statusCode'])).toEqual(['url', 'statusCode', 'statusClass', 'outcome', 'fetched']);
  });

  it('give the URL the room the others leave, never less than its least, so a narrow grid scrolls sideways', () => {
    const shown = ['url', 'statusCode', 'title'];
    expect(urlColumnWidth(shown, URL_COLUMNS, null)).toBeUndefined();
    expect(urlColumnWidth(shown, URL_COLUMNS, 1000)).toBe(1000 - 88 - 208);
    expect(urlColumnWidth(shown, URL_COLUMNS, 400)).toBe(URL_MIN_WIDTH);
    expect(urlColumnWidth(['title'], URL_COLUMNS, 1000)).toBeUndefined();
  });

  it('count the columns wholly in view from the left', () => {
    expect(columnsInView([300, 100, 100], 450)).toBe(2);
    expect(columnsInView([300, 100, 100], 500)).toBe(3);
    expect(columnsInView([300], 100)).toBe(0);
  });
});

describe('the URL grid’s cells', () => {
  const context = { siteOrigin: 'https://example.com', fullUrls: false, sort: [], crawling: false };
  const column = (id: string) => urlGridColumns([id], URL_COLUMNS, context)[0]!;

  it('say what happened to a URL with no status, from its outcome, never a guess', () => {
    expect(urlStatusWords({ id: 1, statusCode: 404 }, false)).toBe('404');
    expect(urlStatusWords({ id: 1, statusCode: null, outcome: 'robotsDisallowed', fetched: true }, false)).toBe(
      'Blocked by robots.txt',
    );
    expect(urlStatusWords({ id: 1, statusCode: null, outcome: 'notChecked', fetched: false }, false)).toBe(
      'Not checked',
    );
    expect(urlStatusWords({ id: 1, statusCode: null, outcome: null, fetched: false }, true)).toBe('Not crawled so far');
    expect(urlStatusWords({ id: 1, statusCode: null, outcome: null, fetched: false }, false)).toBe('Not crawled');
    expect(urlStatusWords({ id: 1, statusCode: null, statusClass: 'noResponse' }, false)).toBe('No response');
  });

  it('copy numbers as numbers and the rest in words, never an engine id', () => {
    const row: UrlRow = {
      id: 1,
      url: 'https://example.com/a',
      responseMs: 1234,
      indexability: 'blockedByRobots',
      type: 'pdf',
      inSitemap: false,
      title: 'A',
    };
    expect(column('responseMs').text(row)).toBe('1234');
    expect(column('indexability').text(row)).toBe('Blocked by robots.txt');
    expect(column('type').text(row)).toBe('PDF');
    expect(column('inSitemap').text(row)).toBe('No');
    // A URL is copied whole, whatever the cell shows.
    expect(column('url').text(row)).toBe('https://example.com/a');
  });

  it('say a page’s canonical names itself, and a file isn’t a page to judge (the fix-it pass)', () => {
    const canonical = column('canonicalUrl');
    const self: UrlRow = { id: 1, url: 'https://example.com/a', canonicalUrl: 'https://example.com/a' };
    const other: UrlRow = { id: 2, url: 'https://example.com/a?p=2', canonicalUrl: 'https://example.com/a' };
    expect(render(canonical.cell(self)).textContent).toBe('Self');
    // Another URL is drawn as the address it is.
    expect(render(canonical.cell(other)).textContent).not.toContain('Self');
    expect(render(canonical.cell(other)).textContent).toContain('/a');
    // Copied whole either way.
    expect(canonical.text(self)).toBe('https://example.com/a');

    // A URL that didn't redirect is its own final URL: said as "Self" too, as the Canonical column says it, so a URL
    // that ends elsewhere stands out.
    const final = column('finalUrl');
    const stays: UrlRow = { id: 4, url: 'https://example.com/b', finalUrl: 'https://example.com/b' };
    const moves: UrlRow = { id: 5, url: 'https://example.com/old', finalUrl: 'https://example.com/b' };
    expect(render(final.cell(stays)).textContent).toBe('Self');
    expect(render(final.cell(moves)).textContent).not.toContain('Self');
    expect(render(final.cell(moves)).textContent).toContain('/b');
    expect(final.text(stays)).toBe('https://example.com/b');

    const indexability = column('indexability');
    expect(
      indexability.text({ id: 3, url: 'https://example.com/s.css', indexability: 'none', type: 'stylesheet' }),
    ).toBe('Not a page');
    expect(indexability.text({ id: 4, url: 'https://example.com/i.png', indexability: 'none', type: 'image' })).toBe(
      'Not a page',
    );
    expect(indexability.text({ id: 5, url: 'https://example.com/a', indexability: 'none', type: 'page' })).toBe(
      'Indexable',
    );
    expect(
      indexability.text({ id: 6, url: 'https://example.com/gone.png', indexability: 'clientError', type: 'image' }),
    ).toBe('Client error');
    expect(indexability.text({ id: 7, url: 'https://example.com/x', indexability: 'blocked', type: 'page' })).toBe(
      'Blocked by a firewall',
    );
  });

  it('name the host once in the URL column’s header, unless every URL is shown whole', () => {
    expect(column('url').headerDetail).toBe('on example.com');
    expect(urlGridColumns(['url'], URL_COLUMNS, { ...context, fullUrls: true })[0]!.headerDetail).toBeUndefined();
  });

  it('sort where the engine sorts, with their place in a sort of several keys, and say why where it doesn’t', () => {
    const sort = [
      { column: 'depth', desc: false },
      { column: 'inlinks', desc: true },
    ];
    const [depth, inlinks, title, outcome] = urlGridColumns(['depth', 'inlinks', 'title', 'outcome'], URL_COLUMNS, {
      ...context,
      sort,
    });
    expect(depth).toMatchObject({ sortable: true, sort: 'ascending', sortOrder: 1 });
    expect(inlinks).toMatchObject({ sortable: true, sort: 'descending', sortOrder: 2 });
    expect(title?.sortable).toBeUndefined();
    expect(title?.sortHint).toMatch(/^URLs can’t be sorted by title: a sort by text would read every page/);
    expect(outcome?.sortHint).toMatch(/read from each response/);
    // One key: no figure beside the arrow.
    expect(
      urlGridColumns(['depth'], URL_COLUMNS, { ...context, sort: [{ column: 'depth', desc: true }] })[0]!.sortOrder,
    ).toBeUndefined();
  });
});

describe('the URL grid’s chooser and filters', () => {
  it('list every column the grid can show once, grouped, a check’s own and the id left out', () => {
    const groups = urlChooserGroups(URL_COLUMNS, true);
    const ids = groups.flatMap((group) => group.columns.map((column) => column.id));
    expect(new Set(ids).size).toBe(ids.length);
    expect(ids).not.toContain('id');
    expect(ids).not.toContain('detail');
    expect(ids).toContain('inlinks');
    expect(groups.map((group) => group.title)).toEqual([
      'The URL',
      'Its response',
      'Indexability',
      'Content',
      'Links',
      'Issues',
    ]);
  });

  it('say which columns need an analysis, without one', () => {
    const issues = urlChooserGroups(URL_COLUMNS, false)
      .flatMap((group) => group.columns)
      .find((column) => column.id === 'issues');
    expect(issues?.note).toBe('It needs an analysis.');
    const filter = urlFilterColumns(URL_COLUMNS, false).find((column) => column.id === 'inlinks');
    expect(filter?.unavailable).toBe('needs an analysis');
    expect(urlFilterColumns(URL_COLUMNS, true).find((column) => column.id === 'inlinks')?.unavailable).toBeUndefined();
  });

  it('filter on every column the engine filters, an enum’s values in words', () => {
    const columns = urlFilterColumns(URL_COLUMNS, true);
    expect(columns.map((column) => column.id)).not.toContain('metaRobots');
    expect(columns.map((column) => column.id)).not.toContain('id');
    const type = columns.find((column) => column.id === 'type');
    expect(type?.values.find((value) => value.value === 'pdf')?.label).toBe('PDF');
  });

  it('read the URLs a view names by their ids, for their words', () => {
    expect(
      urlIdsOf([
        { column: 'id', op: 'eq', value: 9 },
        { column: 'check', op: 'in', values: ['x'], group: { detail: null, relatedUrlId: 3 } },
        { column: 'check', op: 'in', values: ['y'], group: { detail: 'd', relatedUrlId: null } },
        { column: 'depth', op: 'eq', value: 2 },
      ]),
    ).toEqual([3, 9]);
  });
});

describe('the URL grid’s facets', () => {
  const page: FacetsPage = {
    matching: 10,
    total: 12,
    summary: { available: true, reason: null },
    unavailable: [],
    facets: {
      statusClass: [
        { value: '2xx', count: 6, filter: { column: 'statusClass', op: 'in', values: ['2xx'] } },
        { value: null, count: 4, filter: { column: 'statusClass', op: 'isEmpty' } },
      ],
      responseTime: [
        {
          value: '250-499',
          min: 250,
          max: 499,
          count: 3,
          filter: { column: 'responseMs', op: 'between', min: 250, max: 499 },
        },
        { value: '2000+', min: 2000, count: 1, filter: { column: 'responseMs', op: 'gte', value: 2000 } },
      ],
      severity: [{ value: 'none', count: 2, filter: { column: 'issues', op: 'eq', value: 0 } }],
    },
  };

  it('word each value, the unknown by what it means, and mark the one the view already has', () => {
    const facets = buildUrlFacets(page, [{ column: 'statusClass', op: 'in', values: ['2xx'] }]);
    const status = facets.find((facet) => facet.id === 'statusClass')!;
    expect(status.values.map((value) => [value.words, value.count, value.applied])).toEqual([
      ['2xx', 6, true],
      ['Not crawled', 4, false],
    ]);
    expect(facets.find((facet) => facet.id === 'responseTime')!.values.map((value) => value.words)).toEqual([
      '250–499 ms',
      '2000 ms or more',
    ]);
    expect(facets.find((facet) => facet.id === 'severity')!.values[0]!.words).toBe('No issues');
  });

  it('tell a firewall’s block from robots.txt’s, and say why a URL wasn’t judged (the fix-it pass)', () => {
    const indexability = (value: string | null) =>
      urlFacetValueWords(
        'indexability',
        { value, count: 1, filter: { column: 'indexability', op: 'isEmpty' } },
        false,
        {
          blocked: 'Blocked',
          blockedByRobots: 'Blocked by robots.txt',
        },
      );
    expect(indexability('blocked')).toBe('Blocked by a firewall');
    expect(indexability('blockedByRobots')).toBe('Blocked by robots.txt');
    expect(indexability(null)).toBe('Not crawled, so not judged');
  });

  it('offer the pages the not-in-sitemap check found, in one click, on a view not narrowed yet', () => {
    const sitemaps: FacetsPage = {
      ...page,
      facets: { inSitemap: [{ value: false, count: 5_811, filter: { column: 'inSitemap', op: 'is', value: false } }] },
    };
    const check = { column: 'check', op: 'in', values: ['not-in-sitemap'] };
    const values = (filters: GridFilter[], count: number | null) =>
      buildUrlFacets(sitemaps, filters, false, undefined, count).find((facet) => facet.id === 'inSitemap')!.values;
    expect(values([], 18).map((value) => [value.words, value.count, value.filter])).toEqual([
      ['In no sitemap', 5_811, { column: 'inSitemap', op: 'is', value: false }],
      ['Indexable pages in no sitemap', 18, check],
    ]);
    // Not when the check found none, nor on a view already narrowed, whose count it wouldn't be.
    expect(values([], 0)).toHaveLength(1);
    expect(values([], null)).toHaveLength(1);
    expect(values([{ column: 'type', op: 'in', values: ['page'] }], 18)).toHaveLength(1);
  });

  it('say a URL not crawled yet is so far, while the crawl runs', () => {
    expect(
      urlFacetValueWords(
        'statusClass',
        { value: null, count: 1, filter: { column: 'statusClass', op: 'isEmpty' } },
        true,
      ),
    ).toBe('Not crawled so far');
  });
});

describe('a sort of up to three keys', () => {
  const a = { column: 'a', desc: false };
  it('sorts by a column alone on a plain press, then turns it about, then drops it', () => {
    expect(nextMultiSort([], 'a', false, 3).sort).toEqual([a]);
    expect(nextMultiSort([a], 'a', false, 3).sort).toEqual([{ column: 'a', desc: true }]);
    expect(nextMultiSort([{ column: 'a', desc: true }], 'a', false, 3).sort).toEqual([]);
    // A plain press on a sort of several keys starts again from that column alone.
    expect(nextMultiSort([a, { column: 'b', desc: true }], 'b', false, 3).sort).toEqual([{ column: 'b', desc: false }]);
  });

  it('adds a key with Shift, turns it about, then takes it out, the others kept in their order', () => {
    const two = nextMultiSort([a], 'b', true, 3).sort;
    expect(two).toEqual([a, { column: 'b', desc: false }]);
    expect(nextMultiSort(two, 'b', true, 3).sort).toEqual([a, { column: 'b', desc: true }]);
    expect(nextMultiSort([a, { column: 'b', desc: true }], 'b', true, 3).sort).toEqual([a]);
    expect(
      nextMultiSort(
        [
          { column: 'a', desc: true },
          { column: 'b', desc: false },
        ],
        'a',
        true,
        3,
      ).sort,
    ).toEqual([{ column: 'b', desc: false }]);
  });

  it('says it’s full rather than drop a key', () => {
    const three = [a, { column: 'b', desc: false }, { column: 'c', desc: false }];
    expect(nextMultiSort(three, 'd', true, 3)).toEqual({ sort: three, full: true });
  });
});

describe('a facet’s value chosen on a view already filtered on its column', () => {
  it('lands on the count the menu showed: a range the view has stays, beside the value', () => {
    const rows = testUrlRows(48);
    const summary = { available: true, reason: null };
    for (const filters of [
      [{ column: 'responseMs', op: 'gt', value: 300 }],
      [{ column: 'depth', op: 'gte', value: 2 }],
      [{ column: 'statusClass', op: 'in', values: ['2xx', '4xx'] }],
    ] as GridFilter[][]) {
      const facets = answerFacets(rows, { filters }, summary);
      if (!facets.ok) throw new Error('facets');
      for (const facet of buildUrlFacets(facets.page, filters)) {
        for (const value of facet.values.filter((candidate) => !candidate.applied)) {
          const landed = answerGridQuery(
            rows,
            { filters: withFacetValue(filters, value.filter), sort: [], limit: 1 },
            summary,
          );
          if (!landed.ok) throw new Error('rows');
          expect(landed.page.matching, `${JSON.stringify(filters)}: ${facet.title} ${value.words}`).toBe(value.count);
        }
      }
    }
  });

  it('takes the place only of a list the value is one of', () => {
    expect(
      withFacetValue([{ column: 'statusClass', op: 'in', values: ['2xx', '4xx'] }], {
        column: 'statusClass',
        op: 'in',
        values: ['4xx'],
      }),
    ).toEqual([{ column: 'statusClass', op: 'in', values: ['4xx'] }]);
    expect(
      withFacetValue([{ column: 'responseMs', op: 'gt', value: 300 }], {
        column: 'responseMs',
        op: 'between',
        min: 250,
        max: 499,
      }),
    ).toHaveLength(2);
  });
});

describe('the engine’s words for a value', () => {
  it('are the filter editor’s choices', () => {
    const columns = URL_COLUMNS.map((column) =>
      column.id === 'type' ? { ...column, valueTitles: { media: 'Audio or video' } } : column,
    );
    const type = urlFilterColumns(columns, true).find((column) => column.id === 'type');
    expect(type?.values.find((value) => value.value === 'media')?.label).toBe('Audio or video');
    // Where the engine gives none, this version's words, which are the engine's as it knew them.
    expect(type?.values.find((value) => value.value === 'other')?.label).toBe('File');
  });
});
