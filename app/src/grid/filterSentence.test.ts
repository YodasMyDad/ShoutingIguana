import { describe, expect, it } from 'vitest';
import { NO_DETAIL, filterSentences } from './filterSentence';
import { urlColumnWords, urlEmptyWords, urlValueWords } from './urlFilterWords';

const words = {
  column: (id: string) => ({ statusCode: 'Status', check: 'Check', depth: 'Depth', title: 'Title' })[id] ?? id,
  value: (column: string, value: string | number) =>
    column === 'check' && value === 'empty-anchor-text' ? 'Internal links without anchor text' : String(value),
};

describe('a filter as a sentence', () => {
  it('reads each operator in words', () => {
    expect(filterSentences({ column: 'statusCode', op: 'eq', value: 404 }, words)).toEqual([
      { field: 'Status', op: 'is', value: '404' },
    ]);
    expect(filterSentences({ column: 'depth', op: 'gte', value: 4 }, words)).toEqual([
      { field: 'Depth', op: 'is at least', value: '4' },
    ]);
    expect(filterSentences({ column: 'depth', op: 'between', min: 1, max: 3 }, words)).toEqual([
      { field: 'Depth', op: 'is between', value: '1 and 3' },
    ]);
    expect(filterSentences({ column: 'title', op: 'isEmpty' }, words)).toEqual([
      { field: 'Title', op: 'is', value: 'empty' },
    ]);
    expect(filterSentences({ column: 'statusCode', op: 'in', values: [301, 302, 308] }, words)).toEqual([
      { field: 'Status', op: 'is', value: '301, 302 or 308' },
    ]);
  });

  it('says what a column being empty means, where the view has plainer words for it', () => {
    const urlWords = { column: (id: string) => urlColumnWords(id) ?? id, empty: urlEmptyWords };
    // Only a page whose HTML the crawl read has a word count: Overview's "Pages" filters on it.
    expect(filterSentences({ column: 'wordCount', op: 'isNotEmpty' }, urlWords)).toEqual([
      { field: 'HTML', op: 'was', value: 'read' },
    ]);
    expect(filterSentences({ column: 'wordCount', op: 'isEmpty' }, urlWords)).toEqual([
      { field: 'HTML', op: 'was', value: 'not read' },
    ]);
    // Any other column keeps the plain words, under its own name.
    expect(filterSentences({ column: 'statusClass', op: 'isEmpty' }, urlWords)).toEqual([
      { field: 'Status class', op: 'is', value: 'empty' },
    ]);
    expect(filterSentences({ column: 'wordCount', op: 'gte', value: 300 }, urlWords)).toEqual([
      { field: 'Word count', op: 'is at least', value: '300' },
    ]);
  });

  it('gives a time its unit, on a bound and on both ends of a range', () => {
    // Where Overview's response times lead: "Response is at least 1000" said neither the unit nor the thousands.
    const urlWords = {
      column: (id: string) => (id === 'responseMs' ? 'Response' : (urlColumnWords(id) ?? id)),
      value: urlValueWords,
    };
    expect(filterSentences({ column: 'responseMs', op: 'gte', value: 1000 }, urlWords)).toEqual([
      { field: 'Response', op: 'is at least', value: '1,000 ms' },
    ]);
    expect(filterSentences({ column: 'responseMs', op: 'between', min: 0, max: 249 }, urlWords)).toEqual([
      { field: 'Response', op: 'is between', value: '0 ms and 249 ms' },
    ]);
    expect(filterSentences({ column: 'responseMs', op: 'between', min: 2000 }, urlWords)).toEqual([
      { field: 'Response', op: 'is at least', value: '2,000 ms' },
    ]);
    expect(filterSentences({ column: 'ttfbMs', op: 'lt', value: 200 }, urlWords)).toEqual([
      { field: 'ttfbMs', op: 'is under', value: '200 ms' },
    ]);
    // A count keeps no unit, and has its thousands separators whether or not the view words its values.
    expect(filterSentences({ column: 'depth', op: 'gte', value: 10 }, urlWords)).toEqual([
      { field: 'depth', op: 'is at least', value: '10' },
    ]);
    expect(filterSentences({ column: 'wordCount', op: 'gte', value: 1500 }, urlWords)).toEqual([
      { field: 'Word count', op: 'is at least', value: '1,500' },
    ]);
    // An enum's words still come first.
    expect(filterSentences({ column: 'outcome', op: 'in', values: ['notChecked'] }, urlWords)).toEqual([
      { field: 'Outcome', op: 'is', value: 'not checked' },
    ]);
  });

  it('names a check by its title, and its group as a chip of its own', () => {
    expect(
      filterSentences(
        { column: 'check', op: 'in', values: ['empty-anchor-text'], group: { detail: null, relatedUrlId: 1 } },
        words,
        () => '/',
      ),
    ).toEqual([
      { field: 'Check', op: 'is', value: 'Internal links without anchor text' },
      { field: 'Related URL', op: 'is', value: '/' },
    ]);
    expect(
      filterSentences(
        {
          column: 'check',
          op: 'in',
          values: ['internal-client-error'],
          group: { detail: 'HTTP 404', relatedUrlId: null },
        },
        words,
      ),
    ).toEqual([
      { field: 'Check', op: 'is', value: 'internal-client-error' },
      { field: 'Detail', op: 'is', value: 'HTTP 404' },
    ]);
  });

  it('shows both parts of a group that has a detail and a related URL, and one wording for no detail', () => {
    const group = (detail: string | null, relatedUrlId: number | null) =>
      filterSentences(
        { column: 'check', op: 'in', values: ['redirect-temporary'], group: { detail, relatedUrlId } },
        words,
        () => '/new/',
      );
    expect(group('302', 7).slice(1)).toEqual([
      { field: 'Detail', op: 'is', value: '302' },
      { field: 'Related URL', op: 'is', value: '/new/' },
    ]);
    expect(group(null, null).slice(1)).toEqual([{ field: 'Detail', op: 'is', value: NO_DETAIL }]);
    // A related URL with no words to give reads as plain words, never its id.
    expect(
      filterSentences(
        { column: 'check', op: 'in', values: ['empty-anchor-text'], group: { detail: null, relatedUrlId: 1234 } },
        words,
      ).at(-1),
    ).toEqual({ field: 'Related URL', op: 'is', value: 'another URL' });
  });
});
