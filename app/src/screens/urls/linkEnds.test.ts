import { describe, expect, it } from 'vitest';
import { parseLinkEnds, validateGridSearch } from '../../routing/search';
import { BROKEN_LINKS } from '../links/presets';
import { linkEndsOf, linkEndsWords, linksViewWords } from './linkEnds';

/** The Broken links preset's filters. */
const BROKEN = [...BROKEN_LINKS];

describe('the URL grid narrowed to a links view’s ends', () => {
  it('is read from the route, a links view’s filters and search kept as they are', () => {
    expect(parseLinkEnds({ end: 'to', filter: BROKEN, q: 'sofa' })).toEqual({ end: 'to', filter: BROKEN, q: 'sofa' });
    expect(parseLinkEnds({ end: 'from' })).toEqual({ end: 'from' });
    expect(parseLinkEnds({ end: 'sideways' })).toBeUndefined();
    expect(parseLinkEnds('to')).toBeUndefined();
    // The links view's sort and columns, for Back to the links (the review's finding 10): they narrow nothing.
    expect(
      parseLinkEnds({ end: 'to', sort: [{ column: 'to.url', desc: true }], cols: ['from.url', 'to.url', 'from.url'] }),
    ).toEqual({ end: 'to', sort: [{ column: 'to.url', desc: true }], cols: ['from.url', 'to.url'] });
    expect(linkEndsOf({ end: 'to', sort: [{ column: 'to.url', desc: true }] })).toEqual({ end: 'to' });
    expect(validateGridSearch({ links: { end: 'to', filter: BROKEN } }).links).toEqual({ end: 'to', filter: BROKEN });
  });

  it('is the engine’s linkEnds, none of either being every link', () => {
    expect(linkEndsOf({ end: 'to', filter: BROKEN, q: 'sofa' })).toEqual({
      end: 'to',
      filters: BROKEN,
      search: 'sofa',
    });
    expect(linkEndsOf({ end: 'from' })).toEqual({ end: 'from' });
    expect(linkEndsOf(undefined)).toBeUndefined();
  });

  it('says which end of which links, by the view’s name where it is one', () => {
    expect(linkEndsWords({ end: 'to', filter: BROKEN })).toBe(
      'Only the URLs that the broken links lead to, each once.',
    );
    expect(linkEndsWords({ end: 'from' })).toBe('Only the pages with a link on them, each once.');
    expect(linkEndsWords({ end: 'from', filter: BROKEN })).toBe(
      'Only the pages that the broken links are on, each once.',
    );
    expect(linksViewWords({ filter: [...BROKEN, { column: 'kind', op: 'in', values: ['image'] }], q: 'x' })).toBe(
      'the links of a view with 3 filters and the search “x”',
    );
  });
});
