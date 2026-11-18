// The Links screen's pure rules: its words, presets, facets, which reads are slow, what each state says, its columns
// and where other screens land in it.
import { describe, expect, it } from 'vitest';
import type { GridFilter, LinkRow } from '../../api/grids';
import { LINK_COLUMNS, answerLinkFacets, answerLinkQuery } from '../../api/mock/linkGrid';
import { filterSentences } from '../../grid/filterSentence';
import { LINKS_COUNTS, testLinkRows } from '../../test/linksSidecar';
import { urlStatusWords } from '../urls/urlColumns';
import { FACET_TITLES, buildFacets, facetValueWords, withFacetValue } from './facets';
import {
  DEFAULT_LINK_COLUMNS,
  endStatusWords,
  endUrls,
  isLinkColumn,
  linkChooserGroups,
  linkFilterColumns,
  linkFollowWords,
  linkGridColumns,
  linkQueryColumns,
  sharedUrlWidth,
  shownLinkColumns,
} from './linkColumns';
import { allInLinksWords, linksOfUrl } from './linkPlaces';
import {
  FOLLOW_COLUMN,
  linkColumnGroup,
  linkColumnName,
  linkColumnShortName,
  linkFilterWords,
  namesCheck,
  linkValueName,
  urlIdsNamed,
} from './linkWords';
import { ALL_LINKS, LINK_PRESETS, activePreset, sameFilters, sortShown } from './presets';
import { passReason, slowReadWords } from './readCost';
import { analysisNote, noLinksWords, noMatchWords } from './states';

const READY = { available: true, reason: null } as const;
const rows = testLinkRows();
const count = (filters: readonly GridFilter[]) => {
  const answer = answerLinkQuery(rows, { filters: [...filters], sort: [], limit: 1 }, READY);
  if (!answer.ok) throw new Error(JSON.stringify(answer.problem));
  return answer.page.matching;
};

describe('the links’ words', () => {
  it('names every column the engine lists, never by its id', () => {
    for (const column of LINK_COLUMNS) {
      const name = linkColumnName(column.id);
      // "robots.txt" is a name, not an id's dot.
      expect(name.replace('robots.txt', 'robots'), column.id).not.toContain('.');
      expect(name, column.id).not.toMatch(/[a-z][A-Z]/);
      expect(name.charAt(0), column.id).toBe(name.charAt(0).toUpperCase());
    }
    expect(linkColumnName('to.statusCode')).toBe('Target status');
    expect(linkColumnName('from.h1')).toBe('Source H1');
    expect(linkColumnName('to.url')).toBe('Target URL');
    expect(linkColumnName('to.id')).toBe('Target URL');
    expect(linkColumnName('from.isInternal')).toBe('Source on this site');
    expect(linkColumnName('text')).toBe('Anchor or alt text');
    expect(linkColumnShortName('to.statusCode')).toBe('Status');
    expect(linkColumnShortName('from.url')).toBe('URL');
  });

  it('words a column a newer engine adds from its id', () => {
    expect(linkColumnName('to.renderedWordCount')).toBe('Target rendered word count');
    expect(linkColumnName('openerPolicy')).toBe('Opener policy');
  });

  it('puts each column in the link, its source page or its target', () => {
    expect(linkColumnGroup('kind')).toBe('link');
    expect(linkColumnGroup('from.depth')).toBe('from');
    expect(linkColumnGroup('to.depth')).toBe('to');
  });

  it('names every enum value the engine lists', () => {
    for (const column of LINK_COLUMNS) {
      for (const value of column.values) {
        const name = linkValueName(column.id, value);
        expect(name, `${column.id} ${value}`).not.toBeNull();
        expect(name, `${column.id} ${value}`).not.toMatch(/[a-z][A-Z]/);
      }
    }
    expect(linkValueName('kind', 'metaRefresh')).toBe('Meta refresh');
    expect(linkValueName('to.outcome', 'robotsDisallowed')).toBe('Blocked by robots.txt');
    expect(linkValueName('to.indexability', 'none')).toBe('Indexable');
    expect(linkValueName('to.statusClass', 'noResponse')).toBe('No response');
  });

  it('reads a filter by an end’s check as the check’s title, never its id', () => {
    const titles: Record<string, string> = {
      'internal-client-error': 'Internal URL returns 4xx',
      'title-too-long': 'Title too long',
    };
    const words = linkFilterWords(
      () => '/gone',
      (id) => titles[String(id)] ?? 'a check this version doesn’t have',
    );
    const sentence = (filter: GridFilter) =>
      filterSentences(filter, words)
        .map((part) => `${part.field} ${part.op} ${part.value}`)
        .join(' + ');
    expect(sentence({ column: 'to.check', op: 'in', values: ['internal-client-error'] })).toBe(
      'Target check is Internal URL returns 4xx',
    );
    expect(sentence({ column: 'from.check', op: 'notIn', values: ['title-too-long', 'internal-client-error'] })).toBe(
      'Source check is not Title too long or Internal URL returns 4xx',
    );
    expect(sentence({ column: 'to.check', op: 'in', values: ['made-up'] })).toBe(
      'Target check is a check this version doesn’t have',
    );
    // Without the catalog's words, plain words: still never the id.
    const bare = linkFilterWords(() => '/gone');
    expect(
      filterSentences({ column: 'to.check', op: 'in', values: ['internal-client-error'] }, bare).map(
        (part) => part.value,
      ),
    ).toEqual(['a check']);
    // Which filters need the catalog read.
    expect(namesCheck([{ column: 'to.check', op: 'in', values: ['x'] }])).toBe(true);
    expect(namesCheck([{ column: 'from.check', op: 'notIn', values: ['x'] }])).toBe(true);
    expect(namesCheck([{ column: 'to.statusClass', op: 'in', values: ['4xx'] }])).toBe(false);
    expect(namesCheck([])).toBe(false);
  });

  it('reads filters as sentences', () => {
    const words = linkFilterWords((id) => (id === 4 ? '/gone' : 'a URL that isn’t in this crawl'));
    const sentence = (filter: GridFilter) =>
      filterSentences(filter, words)
        .map((part) => `${part.field} ${part.op} ${part.value}`)
        .join(' + ');
    expect(sentence({ column: 'to.statusClass', op: 'in', values: ['4xx', '5xx'] })).toBe(
      'Target status class is 4xx or 5xx',
    );
    expect(sentence({ column: 'kind', op: 'in', values: ['hyperlink'] })).toBe('Kind is Hyperlink');
    expect(sentence({ column: 'text', op: 'isEmpty' })).toBe('Anchor or alt text is empty');
    expect(sentence({ column: 'to.isInternal', op: 'is', value: false })).toBe('Target is on another site');
    expect(sentence({ column: 'from.isInternal', op: 'is', value: true })).toBe('Source is on this site');
    expect(sentence({ column: 'noFollow', op: 'is', value: true })).toBe('Nofollow is yes');
    expect(sentence({ column: 'to.id', op: 'eq', value: 4 })).toBe('Target URL is /gone');
    expect(sentence({ column: 'selfLink', op: 'is', value: false })).toBe('Link doesn’t lead to its own page');
    expect(sentence({ column: 'to.responseMs', op: 'gte', value: 1000 })).toBe(
      'Target response time is at least 1,000 ms',
    );
    expect(sentence({ column: 'to.outcome', op: 'in', values: ['notChecked'] })).toBe('Target outcome is not checked');
    expect(sentence({ column: 'to.wordCount', op: 'isNotEmpty' })).toBe('Target HTML was read');
  });

  it('finds the URLs a filter set names by id', () => {
    expect(
      urlIdsNamed([
        { column: 'to.id', op: 'eq', value: 9 },
        { column: 'from.id', op: 'in', values: [3, 9] },
        { column: 'to.depth', op: 'eq', value: 2 },
      ]),
    ).toEqual([3, 9]);
  });
});

describe('the links’ presets', () => {
  it('each lands on the links its name says', () => {
    const counts = Object.fromEntries(LINK_PRESETS.map((preset) => [preset.id, count(preset.filters)]));
    expect(counts).toEqual({
      all: LINKS_COUNTS.total,
      broken: LINKS_COUNTS.broken,
      redirects: LINKS_COUNTS.redirects,
      nofollow: LINKS_COUNTS.nofollow,
      'no-text': LINKS_COUNTS.noText,
      external: LINKS_COUNTS.external,
      images: LINKS_COUNTS.images,
    });
  });

  it('is the view’s only while the view’s filters are exactly its own, in any order, with no search', () => {
    expect(activePreset([], undefined)).toBe(ALL_LINKS);
    const noText = LINK_PRESETS.find((preset) => preset.id === 'no-text')!;
    expect(activePreset([...noText.filters].reverse(), undefined)).toBe(noText);
    expect(activePreset([{ column: 'to.statusClass', op: 'in', values: ['5xx', '4xx'] }], undefined)?.id).toBe(
      'broken',
    );
    expect(activePreset([{ column: 'to.statusClass', op: 'in', values: ['4xx'] }], undefined)).toBeNull();
    expect(
      activePreset([...noText.filters, { column: 'position', op: 'in', values: ['footer'] }], undefined),
    ).toBeNull();
    expect(activePreset([], 'rattan')).toBeNull();
  });

  it('keeps a sort only while its column is shown', () => {
    const byStatus = [{ column: 'to.statusCode', desc: true }];
    expect(sortShown(byStatus, ['from.url', 'to.url', 'to.statusCode'])).toEqual(byStatus);
    // Hidden, the sort goes: the route's "no sort" is undefined, never an empty list.
    expect(sortShown(byStatus, ['from.url', 'to.url'])).toBeUndefined();
    expect(sortShown([{ column: 'id', desc: true }], ['from.url', 'to.url'])).toBeUndefined();
    expect(sortShown([], ['from.url'])).toBeUndefined();
    expect(sortShown(undefined, ['from.url'])).toBeUndefined();
  });

  it('tells the same filters whatever order their fields came in', () => {
    const a: GridFilter = { column: 'to.isInternal', op: 'is', value: false };
    const b = { value: false, op: 'is', column: 'to.isInternal' } as GridFilter;
    expect(sameFilters([a], [b])).toBe(true);
    expect(sameFilters([a], [{ ...a, value: true }])).toBe(false);
    expect(sameFilters([a], [a, a])).toBe(false);
  });
});

describe('the links’ facets', () => {
  const facetsOf = (filters: GridFilter[]) => {
    const answer = answerLinkFacets(rows, { filters }, READY);
    if (!answer.ok) throw new Error('Refused');
    return buildFacets(answer.page, filters);
  };

  it('words every value, the unknown ones too', () => {
    expect(FACET_TITLES['to.statusClass']).toBe('Target status');
    expect(facetValueWords('kind', 'hyperlink')).toBe('Hyperlink');
    expect(facetValueWords('follow', 'nofollow')).toBe('Not followed');
    expect(facetValueWords('follow', 'notHyperlink')).toBe('Not a hyperlink');
    expect(facetValueWords('to.statusClass', null)).toBe('Not fetched');
    expect(facetValueWords('to.statusClass', 'noResponse')).toBe('No response');
    expect(facetValueWords('to.isInternal', false)).toBe('Another site');
    expect(facetValueWords('kind', null)).toBe('A kind this version doesn’t know');
  });

  it('lists each facet’s values with their counts, in the engine’s order', () => {
    const facets = facetsOf([]);
    expect(facets.map((facet) => facet.title)).toEqual(['Kind', 'Position', 'Follow', 'Target status', 'Target site']);
    const kind = facets[0]!;
    expect(kind.values.map((value) => [value.words, value.count])).toEqual([
      ['Hyperlink', 9],
      ['Image', 2],
      ['Canonical', 1],
      ['Redirect', 1],
    ]);
    expect(kind.values[0]?.name).toBe('Hyperlink, 9 links');
    expect(facets[3]?.values.at(-1)).toMatchObject({ words: 'Not fetched', count: 1, name: 'Not fetched, 1 link' });
  });

  it('lands every value on its count, whatever the view already filters by', () => {
    const views: GridFilter[][] = [
      [],
      [{ column: 'kind', op: 'in', values: ['hyperlink', 'image'] }],
      [{ column: 'to.statusClass', op: 'in', values: ['4xx', '5xx'] }],
      [
        { column: 'followed', op: 'is', value: true },
        { column: 'position', op: 'notIn', values: ['head'] },
      ],
      [
        { column: 'to.isInternal', op: 'is', value: true },
        { column: 'kind', op: 'in', values: ['hyperlink'] },
      ],
    ];
    let checked = 0;
    for (const filters of views) {
      for (const facet of facetsOf(filters)) {
        for (const value of facet.values) {
          expect(count(withFacetValue(filters, value.filter)), `${facet.title} ${value.words}`).toBe(value.count);
          checked++;
        }
      }
    }
    expect(checked).toBeGreaterThan(40);
  });

  it('takes the place of a filter on the same column, and marks a value the view already has', () => {
    const filters: GridFilter[] = [
      { column: 'kind', op: 'in', values: ['hyperlink', 'image'] },
      { column: 'position', op: 'in', values: ['content'] },
    ];
    expect(withFacetValue(filters, { column: 'kind', op: 'in', values: ['image'] })).toEqual([
      { column: 'position', op: 'in', values: ['content'] },
      { column: 'kind', op: 'in', values: ['image'] },
    ]);
    const position = facetsOf(filters).find((facet) => facet.id === 'position');
    expect(position?.values).toHaveLength(1);
    expect(position?.values[0]).toMatchObject({ words: 'Content', applied: true });
    expect(
      facetsOf(filters)
        .find((facet) => facet.id === 'kind')
        ?.values.every((value) => !value.applied),
    ).toBe(true);
  });
});

describe('which reads of the links are slow', () => {
  const quick = { crawling: false, summaryReason: null };
  const view = (filters: GridFilter[] = [], search?: string, sort: { column: string; desc: boolean }[] = []) => ({
    filters,
    search,
    sort,
  });

  it('are quick with no filter, and under filters the analysis counts by', () => {
    expect(passReason(view(), quick)).toBeNull();
    expect(passReason(view([{ column: 'kind', op: 'in', values: ['hyperlink'] }]), quick)).toBeNull();
    expect(passReason(view([{ column: 'to.statusClass', op: 'in', values: ['4xx'] }]), quick)).toBeNull();
    // Whether a link has text is counted; what it says isn't.
    expect(passReason(view([{ column: 'text', op: 'isEmpty' }]), quick)).toBeNull();
    expect(passReason(view([], undefined, [{ column: 'to.statusCode', desc: true }]), quick)).toBeNull();
    // One source page named outright reads only its own links.
    expect(passReason(view([{ column: 'from.id', op: 'eq', value: 4 }]), quick)).toBeNull();
    // An unfiltered grid is found by the links' ids, analysed or not, crawling or not.
    expect(passReason(view(), { crawling: true, summaryReason: 'crawling' })).toBeNull();
    expect(passReason(view(), { crawling: false, summaryReason: 'notAnalyzed' })).toBeNull();
  });

  it('pass over every link for a search, a filter on the anchor text, or one on its source page', () => {
    expect(passReason(view([], 'rattan'), quick)).toBe('search');
    expect(passReason(view([{ column: 'text', op: 'contains', value: 'buy' }]), quick)).toBe('text');
    expect(passReason(view([{ column: 'fragment', op: 'isNotEmpty' }]), quick)).toBe('text');
    expect(passReason(view([{ column: 'from.depth', op: 'gte', value: 3 }]), quick)).toBe('source');
  });

  it('pass over every link under any filter or sort while the crawl runs, or with no current analysis', () => {
    const filtered = view([{ column: 'kind', op: 'in', values: ['hyperlink'] }]);
    expect(passReason(filtered, { crawling: true, summaryReason: 'crawling' })).toBe('crawling');
    expect(passReason(filtered, { crawling: false, summaryReason: 'notAnalyzed' })).toBe('noSummary');
    expect(passReason(filtered, { crawling: false, summaryReason: 'crawlChanged' })).toBe('noSummary');
    expect(
      passReason(view([], undefined, [{ column: 'to.url', desc: false }]), {
        crawling: true,
        summaryReason: 'crawling',
      }),
    ).toBe('crawling');
    // Another version's analysis may have left a link summary: nothing is claimed either way.
    expect(passReason(filtered, { crawling: false, summaryReason: 'engineChanged' })).toBeNull();
  });

  it('say what they are doing, with the number of links when it is known', () => {
    expect(slowReadWords('search', 5_053_909)).toBe(
      'Reading all 5,053,909 links: a search looks at each link’s anchor text and both its URLs.',
    );
    expect(slowReadWords('text', null)).toBe(
      'Reading every link: a filter on what the anchor text says looks at each link.',
    );
    expect(slowReadWords(null, 5_053_909)).toBe('Reading the links…');
  });
});

describe('what Links says of a crawl with no current analysis', () => {
  it('says nothing of a crawl that has its analysis, or of one running with no column waiting', () => {
    expect(analysisNote(READY, [])).toBeNull();
    expect(analysisNote(undefined, [])).toBeNull();
    expect(analysisNote({ available: false, reason: 'crawling' }, [])).toBeNull();
  });

  it('says what still works and what doesn’t, with the action that mends it', () => {
    const never = analysisNote({ available: false, reason: 'notAnalyzed' }, []);
    expect(never?.text).toBe(
      'This crawl hasn’t been analysed yet. Every link is here to search, filter and sort, but each filter reads every link, so it’s slower on a big crawl. The columns an analysis fills (inlinks, issues, where redirects end) are empty until it’s analysed.',
    );
    expect(never?.action).toBe('Analyse it');
    const continued = analysisNote({ available: false, reason: 'crawlChanged' }, ['Target inlinks']);
    expect(continued?.text).toContain('This crawl was continued after its last analysis.');
    expect(continued?.text).toContain(', and Target inlinks is empty until it’s analysed again.');
    expect(continued?.action).toBe('Analyse again');
    const older = analysisNote({ available: false, reason: 'engineChanged' }, ['Target inlinks', 'Target issues']);
    expect(older?.text).toBe(
      'Another version of Shouting Iguana analysed this crawl. Every link is here to search, filter and sort by what the crawl itself recorded, and Target inlinks and Target issues are empty until it’s analysed again.',
    );
    expect(older?.action).toBe('Analyse again');
  });

  it('says only that the columns will fill in, with nothing to press, while the crawl runs', () => {
    expect(analysisNote({ available: false, reason: 'crawling' }, ['Target inlinks'])).toEqual({
      text: 'Target inlinks fill in once the crawl has finished and been analysed.',
      action: null,
    });
  });

  it('says a crawl is being analysed while it is, with nothing to press: never that it hasn’t been', () => {
    // The summary's reason is still the one from before the analysis began.
    for (const reason of ['notAnalyzed', 'crawlChanged', 'engineChanged'] as const) {
      const note = analysisNote({ available: false, reason }, [], true);
      expect(note?.text, reason).toBe(
        'This crawl is being analysed. Every link is here to search, filter and sort meanwhile, though each filter reads every link until it’s done. The columns an analysis fills (inlinks, issues, where redirects end) fill in when the analysis that’s running is done.',
      );
      expect(note?.text).not.toMatch(/hasn’t been analysed|continued after|Another version/);
      expect(note?.action).toBeNull();
    }
    expect(analysisNote({ available: false, reason: 'notAnalyzed' }, ['Target inlinks'], true)?.text).toContain(
      'Target inlinks fill in when the analysis that’s running is done.',
    );
    // With its analysis read, there's nothing to say; and a crawl still running is said as before.
    expect(analysisNote(READY, [], true)).toBeNull();
    expect(analysisNote({ available: false, reason: 'crawling' }, ['Target inlinks'], true)?.text).toBe(
      'Target inlinks fill in once the crawl has finished and been analysed.',
    );
  });

  it('words an empty grid by why it is empty', () => {
    expect(noLinksWords(true).title).toBe('No links yet');
    expect(noLinksWords(false).why).toBe('The crawl stored none: it read no page with a link on it.');
    expect(noMatchWords(undefined, true)).toBe('No links match these filters.');
    expect(noMatchWords('rattan', false)).toBe('No links match “rattan”.');
    expect(noMatchWords('rattan', true)).toBe('No links match “rattan” with these filters.');
  });
});

describe('the links’ columns', () => {
  it('shows the default set, the route’s own, and never one this engine doesn’t have', () => {
    expect(shownLinkColumns(undefined, LINK_COLUMNS)).toBe(DEFAULT_LINK_COLUMNS);
    expect(shownLinkColumns(['to.url', 'to.depth'], LINK_COLUMNS)).toEqual(['to.url', 'to.depth']);
    expect(shownLinkColumns(['to.url', 'to.nope', 'noFollow', 'from.id'], LINK_COLUMNS)).toEqual(['to.url']);
    expect(shownLinkColumns(['to.nope'], LINK_COLUMNS)).toBe(DEFAULT_LINK_COLUMNS);
    // Before the engine's list is read, the route's are taken as they are.
    expect(shownLinkColumns(['to.url', 'to.nope'], undefined)).toEqual(['to.url', 'to.nope']);
    expect(isLinkColumn(FOLLOW_COLUMN, LINK_COLUMNS)).toBe(true);
    expect(DEFAULT_LINK_COLUMNS.every((id) => isLinkColumn(id, LINK_COLUMNS))).toBe(true);
  });

  it('asks for what it draws with: each end’s id, the follow column’s parts, a status’s outcome', () => {
    expect(linkQueryColumns(DEFAULT_LINK_COLUMNS)).toEqual([
      'from.id',
      'to.id',
      'from.url',
      'to.url',
      'text',
      'kind',
      'position',
      'followed',
      'noFollow',
      'sponsored',
      'ugc',
      'to.statusCode',
      'to.outcome',
      'to.fetched',
      'to.statusClass',
    ]);
    // Every column asked for is one the engine has.
    const answer = answerLinkQuery(
      rows,
      { columns: linkQueryColumns(DEFAULT_LINK_COLUMNS), filters: [], sort: [], limit: 5 },
      READY,
    );
    expect(answer.ok).toBe(true);
  });

  it('says how an end answered, and what happened when it has no status', () => {
    const row = (to: number) => rows.find((candidate) => candidate['to.id'] === to)!;
    expect(endStatusWords(row(4), 'to')).toBe('404');
    expect(endStatusWords(row(6), 'to')).toBe('Blocked by robots.txt');
    // In the URL grid's words for the same URL: one helper for both.
    expect(endStatusWords(row(7), 'to')).toBe('Not crawled');
    expect(endStatusWords(row(7), 'to', true)).toBe('Not crawled so far');
    expect(endStatusWords({ id: 1, 'to.statusCode': null, 'to.outcome': null, 'to.fetched': true }, 'to')).toBe(
      urlStatusWords({ id: 1, statusCode: null, outcome: null, fetched: true }, false),
    );
    expect(endStatusWords({ id: 1, 'to.statusCode': null, 'to.statusClass': 'noResponse' }, 'to')).toBe('No response');
    expect(
      endStatusWords({ id: 1, 'to.statusCode': null, 'to.outcome': 'notChecked', 'to.fetched': false }, 'to'),
    ).toBe('Not checked');
    expect(endStatusWords({ id: 1, 'to.statusCode': null, 'to.outcome': 'timeout', 'to.fetched': true }, 'to')).toBe(
      'Timed out',
    );
  });

  it('says whether a link is followed, then its other flags', () => {
    const link = (facts: Partial<LinkRow>): LinkRow => ({
      id: 1,
      followed: true,
      noFollow: false,
      sponsored: false,
      ugc: false,
      ...facts,
    });
    expect(linkFollowWords(link({}))).toBe('Followed');
    expect(linkFollowWords(link({ followed: false, noFollow: true, sponsored: true }))).toBe('nofollow, sponsored');
    // A nofollow on its page stops it, with none on the link itself.
    expect(linkFollowWords(link({ followed: false }))).toBe('Not followed');
    expect(linkFollowWords(link({ followed: null }))).toBe('');
    expect(linkFollowWords(link({ followed: null, noFollow: true, ugc: true }))).toBe('nofollow, ugc');
  });

  it('draws each column by its type, and copies a number as the number', () => {
    const columns = linkGridColumns(
      ['to.responseMs', 'to.sizeBytes', 'selfLink', 'kind', 'text', 'to.statusCode', 'follow'],
      LINK_COLUMNS,
      {
        siteOrigin: 'https://example.com',
        onOpen: () => undefined,
        sort: [{ column: 'to.statusCode', desc: true }],
      },
    );
    const [ms, size, self, kind, text, status, follow] = columns;
    const row: LinkRow = { ...rows[5]!, 'to.responseMs': 456, 'to.sizeBytes': 2048 };
    expect(ms?.cell(row)).toBe('456 ms');
    expect(ms?.text(row)).toBe('456');
    expect(ms?.numeric).toBe(true);
    expect(size?.text(row)).toBe('2048');
    expect(self?.text(row)).toBe('No');
    expect(kind?.text(row)).toBe('Hyperlink');
    expect(text?.text(row)).toBe('Partner');
    expect(status?.text(row)).toBe('Blocked by robots.txt');
    expect(follow?.text(row)).toBe('nofollow, sponsored');
    // The target's columns sort; a link's own say why they don't.
    expect(status).toMatchObject({ sortable: true, sort: 'descending' });
    expect(text?.sortable).toBeUndefined();
    expect(text?.sortHint).toBe(
      'Links can’t be sorted by anchor or alt text: they sort by crawl order, or by a column of the target.',
    );
    expect(columns.map((column) => column.header)).toEqual([
      'Target response time',
      'Target size',
      'Links to its own page',
      'Kind',
      'Anchor or alt text',
      'Target status',
      'Follow',
    ]);
  });

  it('shares the grid’s room between the URL columns, never under their least', () => {
    expect(sharedUrlWidth(DEFAULT_LINK_COLUMNS, LINK_COLUMNS, null)).toBeUndefined();
    expect(sharedUrlWidth(['text', 'kind'], LINK_COLUMNS, 1_000)).toBeUndefined();
    const wide = sharedUrlWidth(DEFAULT_LINK_COLUMNS, LINK_COLUMNS, 1_600)!;
    const columns = linkGridColumns(DEFAULT_LINK_COLUMNS, LINK_COLUMNS, {
      siteOrigin: null,
      onOpen: () => undefined,
      sort: [],
      urlWidth: wide,
    });
    const used = columns.reduce((sum, column) => sum + column.width, 0);
    expect(used).toBeLessThanOrEqual(1_600);
    expect(used).toBeGreaterThan(1_598);
    expect(sharedUrlWidth(DEFAULT_LINK_COLUMNS, LINK_COLUMNS, 600)).toBe(160);
    // The default view fits a 1280 px window beside the sidebar.
    expect(sharedUrlWidth(DEFAULT_LINK_COLUMNS, LINK_COLUMNS, 980)).toBeGreaterThanOrEqual(160);
  });

  it('offers every column to show, grouped, the follow column in place of the flags it shows', () => {
    const groups = linkChooserGroups(LINK_COLUMNS, false);
    expect(groups.map((group) => group.title)).toEqual(['The link', 'Its source page', 'Its target']);
    const own = groups[0]!.columns.map((column) => column.name);
    expect(own).toEqual([
      'Follow',
      'Anchor or alt text',
      'Kind',
      'Position',
      'Image link',
      'Links to its own page',
      'Fragment',
      'Crawl order',
    ]);
    const target = groups[2]!.columns;
    expect(target[0]).toMatchObject({ id: 'to.url', name: 'URL', keywords: 'Target URL' });
    expect(target.find((column) => column.id === 'to.inlinks')?.note).toBe('It needs an analysis.');
    expect(
      linkChooserGroups(LINK_COLUMNS, true)[2]!.columns.find((column) => column.id === 'to.inlinks')?.note,
    ).toBeUndefined();
    expect(groups.flatMap((group) => group.columns).some((column) => column.id.endsWith('.id'))).toBe(false);
  });

  it('offers every column the engine filters by, with its values in words, and marks what needs an analysis', () => {
    const columns = linkFilterColumns(LINK_COLUMNS, false);
    expect(columns.some((column) => column.id === 'to.metaRobots' || column.id === 'id' || column.id === 'to.id')).toBe(
      false,
    );
    expect(columns.find((column) => column.id === 'kind')?.values[0]).toEqual({
      value: 'hyperlink',
      label: 'Hyperlink',
    });
    expect(columns.find((column) => column.id === 'to.inlinks')?.unavailable).toBe('needs an analysis');
    expect(columns.find((column) => column.id === 'to.statusCode')).toMatchObject({
      name: 'Target status',
      shortName: 'Status',
      group: 'to',
      type: 'number',
    });
    expect(linkFilterColumns(LINK_COLUMNS, true).every((column) => column.unavailable === undefined)).toBe(true);
  });

  it('copies one end’s URLs, each once, in the links’ order', () => {
    expect(endUrls(rows, 'to')).toEqual({
      count: 8,
      text: [
        'https://example.com/',
        'https://example.com/about',
        'https://example.com/old',
        'https://example.com/gone',
        'https://example.com/img/logo.png',
        'https://other.example/partner',
        'https://example.com/hidden',
        'https://example.com/error',
      ].join('\n'),
    });
    expect(endUrls(rows, 'from').count).toBe(3);
  });
});

describe('where other screens land in Links', () => {
  it('opens a URL’s inlinks or outlinks without a page’s links to itself, as the inspector lists them', () => {
    expect(linksOfUrl('inlinks', 2, 'all')).toEqual([
      { column: 'to.id', op: 'eq', value: 2 },
      { column: 'selfLink', op: 'is', value: false },
    ]);
    expect(linksOfUrl('outlinks', 2, 'image')).toEqual([
      { column: 'from.id', op: 'eq', value: 2 },
      { column: 'selfLink', op: 'is', value: false },
      { column: 'kind', op: 'in', values: ['image'] },
    ]);
    // /about has three links to it, one its own: the inspector counts two.
    expect(count(linksOfUrl('inlinks', 2, 'all'))).toBe(2);
    expect(count(linksOfUrl('outlinks', 2, 'all'))).toBe(4);
    expect(count(linksOfUrl('outlinks', 2, 'image'))).toBe(1);
  });

  it('words the inspector’s link to them by how many there are: one is “it”, never “All 1”', () => {
    expect(allInLinksWords(1)).toBe('Show it in Links');
    expect(allInLinksWords(2)).toBe('All 2 in Links');
    expect(allInLinksWords(792)).toBe('All 792 in Links');
    expect(allInLinksWords(5_053_909)).toBe('All 5,053,909 in Links');
  });
});
