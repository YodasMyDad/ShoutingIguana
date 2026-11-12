import { describe, expect, it } from 'vitest';
import type { FacetsPage, UrlRow } from '../../api/grids';
import { URL_COLUMNS } from '../../api/mock/urlColumns';
import { nextMultiSort } from '../../grid/sort';
import {
  DEFAULT_URL_COLUMNS,
  URL_MIN_WIDTH,
  URL_PRESETS,
  activeUrlPreset,
  colsParam,
  columnsInView,
  shownUrlColumns,
  urlChooserGroups,
  urlColumnWidth,
  urlFilterColumns,
  urlGridColumns,
  urlQueryColumns,
  urlStatusWords,
} from './urlColumns';
import { buildUrlFacets, urlFacetValueWords } from './urlFacets';
import { urlIdsOf } from './useUrlFilterWords';

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
    expect(urlQueryColumns(['url', 'statusCode'])).toEqual(['url', 'statusCode', 'statusClass', 'outcome', 'fetched']);
  });

  it('give the URL the room the others leave, never less than its least, so a narrow grid scrolls sideways', () => {
    const shown = ['url', 'statusCode', 'title'];
    expect(urlColumnWidth(shown, URL_COLUMNS, null)).toBeUndefined();
    expect(urlColumnWidth(shown, URL_COLUMNS, 1000)).toBe(1000 - 104 - 240);
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
