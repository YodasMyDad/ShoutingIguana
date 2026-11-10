// The inspector's smaller pure pieces: headers, structured data, what the crawl did with the URL, where its issues
// lead, and its sections.
import { beforeEach, describe, expect, it } from 'vitest';
import type { StructuredDataBlock, UrlIssue } from '../api/inspector';
import { testResponse, testUrlDetail } from '../test/inspectorSidecar';
import { fetchState } from './fetchState';
import { hreflangProblem } from './hreflang';
import { LONG_HEADER_VALUE, filterHeaders, headersText, shapeHeaders } from './headers';
import { issueLink } from './issueLinks';
import { chooseLinkKind, chosenLinkKind, followWords, linkStatusWords, resetLinkKinds } from './links';
import {
  SECTION_IDS,
  isSectionId,
  rememberSection,
  rememberedSection,
  resetRememberedSection,
  sectionCounts,
  splitHidden,
} from './sections';
import {
  CHILD_PAGE,
  MAX_DEPTH,
  parseBlock,
  parseStructuredData,
  structuredDataSummary,
  visibleChildren,
} from './structuredData';
import { depthInBrief, depthWords } from './words';

describe('shapeHeaders', () => {
  it('merges a repeated name into one row, in the place of its first', () => {
    const rows = shapeHeaders([
      { name: 'date', value: 'Sat, 31 Oct 2026 14:14:57 GMT' },
      { name: 'x-frame-options', value: 'SAMEORIGIN' },
      { name: 'vary', value: 'Accept-Encoding' },
      { name: 'x-frame-options', value: 'sameorigin' },
    ]);
    expect(rows.map((row) => row.name)).toEqual(['date', 'x-frame-options', 'vary']);
    expect(rows[1]?.values).toEqual(['SAMEORIGIN', 'sameorigin']);
  });

  it('treats names as the same whatever their case', () => {
    const rows = shapeHeaders([
      { name: 'Set-Cookie', value: 'a=1' },
      { name: 'set-cookie', value: 'b=2' },
    ]);
    expect(rows).toEqual([{ name: 'set-cookie', values: ['a=1', 'b=2'], long: false }]);
  });

  it('flags a row with a long value', () => {
    const long = 'x'.repeat(LONG_HEADER_VALUE + 1);
    const rows = shapeHeaders([
      { name: 'content-security-policy', value: long },
      { name: 'etag', value: 'x'.repeat(LONG_HEADER_VALUE) },
      { name: 'set-cookie', value: 'a=1' },
      { name: 'set-cookie', value: long },
    ]);
    expect(rows.map((row) => row.long)).toEqual([true, false, true]);
  });

  it('is empty for no headers', () => {
    expect(shapeHeaders([])).toEqual([]);
  });

  it('filters by name or value, ignoring case, and copies as lines', () => {
    const rows = shapeHeaders([
      { name: 'content-type', value: 'text/html; charset=utf-8' },
      { name: 'x-frame-options', value: 'SAMEORIGIN' },
      { name: 'x-frame-options', value: 'sameorigin' },
    ]);
    expect(filterHeaders(rows, 'FRAME').map((row) => row.name)).toEqual(['x-frame-options']);
    expect(filterHeaders(rows, 'utf-8').map((row) => row.name)).toEqual(['content-type']);
    expect(filterHeaders(rows, '  ')).toHaveLength(2);
    expect(filterHeaders(rows, 'nothing')).toEqual([]);
    expect(headersText(rows)).toBe(
      'content-type: text/html; charset=utf-8\nx-frame-options: SAMEORIGIN\nx-frame-options: sameorigin',
    );
  });
});

const block = (value: unknown, overrides: Partial<StructuredDataBlock> = {}): StructuredDataBlock => ({
  valid: true,
  types: [],
  json: typeof value === 'string' ? value : JSON.stringify(value),
  error: null,
  truncated: false,
  ...overrides,
});

describe('structured data', () => {
  it('turns a block into a tree: objects by their type, lists by their size, values as text', () => {
    const parsed = parseBlock(
      block(
        {
          '@context': 'https://schema.org',
          '@type': 'Product',
          name: 'Patio set',
          offers: { '@type': 'Offer', price: 499.99, inStock: true, gtin: null },
          image: ['https://example.com/a.jpg', 'https://example.com/b.jpg'],
        },
        { types: ['Product'] },
      ),
      1,
    );
    expect(parsed.title).toBe('Product');
    const root = parsed.root;
    if (!root) throw new Error('the block parses');
    expect(root).toMatchObject({ key: null, kind: 'object', label: 'Product' });
    expect(root.children.map((child) => [child.key, child.kind, child.label])).toEqual([
      ['@context', 'string', 'https://schema.org'],
      ['@type', 'string', 'Product'],
      ['name', 'string', 'Patio set'],
      ['offers', 'object', 'Offer'],
      ['image', 'array', '2 items'],
    ]);
    expect(root.children[0]?.isUrl).toBe(true);
    expect(root.children[2]?.isUrl).toBe(false);
    const offers = root.children[3];
    expect(offers?.children.map((child) => [child.key, child.kind, child.label])).toEqual([
      ['@type', 'string', 'Offer'],
      ['price', 'number', '499.99'],
      ['inStock', 'boolean', 'true'],
      ['gtin', 'null', 'null'],
    ]);
    expect(root.children[4]?.children.map((child) => child.key)).toEqual(['1', '2']);
  });

  it('names an object without a type by its size, and joins several types', () => {
    expect(parseBlock(block({ a: 1 }), 1).root?.label).toBe('1 property');
    expect(parseBlock(block({ a: 1, b: 2 }), 1).root?.label).toBe('2 properties');
    expect(parseBlock(block({ '@type': ['Product', 'Thing'] }), 1).root?.label).toBe('Product, Thing');
    expect(parseBlock(block([1]), 1).root?.label).toBe('1 item');
    expect(parseBlock(block({}), 1).title).toBe('No type');
  });

  it('keeps a block that doesn’t parse as its text, with the parser’s message', () => {
    const parsed = parseBlock(
      block('{"@type": "Product",}', { valid: false, error: "'}' is invalid after a value.", types: [] }),
      2,
    );
    expect(parsed).toMatchObject({
      index: 2,
      valid: false,
      root: null,
      error: "'}' is invalid after a value.",
      raw: '{"@type": "Product",}',
    });
    expect(parseBlock(block('{', { valid: false, error: null }), 1).error).toBe('It isn’t valid JSON.');
  });

  it('shows the text of a block the engine read and this parser can’t', () => {
    const parsed = parseBlock(block('{"@type": "Product", /* a comment */ }', { types: ['Product'] }), 1);
    expect(parsed).toMatchObject({ valid: true, root: null, error: null });
  });

  it('caps the children shown, and says how many more', () => {
    const root = parseBlock(block(Array.from({ length: 120 }, (_, index) => index)), 1).root;
    if (!root) throw new Error('the block parses');
    expect(visibleChildren(root, CHILD_PAGE)).toMatchObject({ more: 70 });
    expect(visibleChildren(root, CHILD_PAGE).children).toHaveLength(50);
    expect(visibleChildren(root, 100).more).toBe(20);
    expect(visibleChildren(root, 500)).toMatchObject({ more: 0 });
    expect(visibleChildren(root, 500).children).toHaveLength(120);
  });

  it('cuts a tree nested deeper than it will draw', () => {
    let nested: unknown = 'leaf';
    for (let depth = 0; depth < MAX_DEPTH + 10; depth++) nested = { child: nested };
    let node = parseBlock(block(nested), 1).root;
    let depth = 0;
    while (node && node.children.length > 0) {
      node = node.children[0] ?? null;
      depth++;
    }
    expect(depth).toBe(MAX_DEPTH);
  });

  it('marks where a tree nested past its depth is cut: what’s there has properties, it isn’t empty', () => {
    let json = '{"leaf":1}';
    for (let depth = 0; depth < 40; depth++) json = `{"a":${json}}`;
    let node = parseBlock({ valid: true, types: [], json, error: null, truncated: false }, 1).root;
    const above: boolean[] = [];
    while (node && node.children.length > 0) {
      above.push(node.cut === true);
      node = node.children[0] ?? null;
    }
    // The deepest node drawn says "1 property" and has none to show: the cut is said, on that node alone.
    expect(node).toMatchObject({ kind: 'object', label: '1 property', children: [], cut: true });
    expect(above).not.toContain(true);
    // The same for a list; and an object or list that really is empty there isn't cut.
    const nest = (inner: string) => {
      let text = inner;
      for (let depth = 0; depth < MAX_DEPTH; depth++) text = `{"a":${text}}`;
      let at = parseBlock({ valid: true, types: [], json: text, error: null, truncated: false }, 1).root;
      while (at && at.children.length > 0) at = at.children[0] ?? null;
      return at;
    };
    expect(nest('[1,2,3]')).toMatchObject({ kind: 'array', label: '3 items', cut: true });
    expect(nest('{}')?.cut).toBeUndefined();
    expect(nest('[]')?.cut).toBeUndefined();
  });

  it('answers the section in a sentence', () => {
    expect(structuredDataSummary([])).toBe('No structured data');
    const blocks = parseStructuredData([
      block({ '@type': 'Product' }, { types: ['Product'] }),
      block({ '@graph': [] }, { types: ['BreadcrumbList', 'Product'] }),
      block('{', { valid: false, error: 'Unexpected end.' }),
    ]);
    expect(blocks.map((entry) => entry.index)).toEqual([1, 2, 3]);
    expect(structuredDataSummary(blocks)).toBe('3 blocks: Product, BreadcrumbList and 1 that doesn’t parse');
    expect(structuredDataSummary(blocks.slice(0, 1))).toBe('1 block: Product');
    expect(structuredDataSummary(parseStructuredData([block({})]))).toBe('1 block');
    const broken = block('{', { valid: false, error: 'Unexpected end.' });
    expect(structuredDataSummary(parseStructuredData([broken]))).toBe('1 block that doesn’t parse');
    expect(structuredDataSummary(parseStructuredData([broken, broken]))).toBe('2 blocks that don’t parse');
  });
});

describe('fetchState', () => {
  const base = testUrlDetail();
  const found = (overrides: Partial<typeof base.discovery>) => ({ ...base.discovery, ...overrides });

  /** As a crawl that's done says it: only a URL the crawl hadn't reached reads differently while it runs. */
  const after = (subject: Parameters<typeof fetchState>[0]) => fetchState(subject, false);

  it('is quiet for a page fetched and read', () => {
    expect(fetchState(base, false)).toEqual({
      kind: 'ok',
      answered: true,
      hasContent: true,
      title: null,
      why: null,
      pending: false,
    });
  });

  it('says a queued URL hasn’t been reached, and one being fetched is, while the crawl runs', () => {
    const queued = fetchState({ ...base, response: null, content: null, discovery: found({ state: 'queued' }) }, true);
    expect(queued).toMatchObject({ kind: 'queued', title: 'Not fetched yet', pending: true, answered: false });
    const fetching = fetchState(
      { ...base, response: null, content: null, discovery: found({ state: 'fetching' }) },
      true,
    );
    expect(fetching).toMatchObject({ kind: 'fetching', title: 'Being fetched', pending: true });
  });

  it('says the crawl stopped before it reached a URL still queued, with nothing on its way', () => {
    for (const waiting of ['queued', 'fetching'] as const) {
      const state = fetchState({ ...base, response: null, content: null, discovery: found({ state: waiting }) }, false);
      expect(state).toMatchObject({
        kind: waiting,
        title: 'Not fetched',
        why: 'The crawl stopped before it reached this URL.',
        pending: false,
        answered: false,
      });
    }
  });

  it('says why a skipped URL wasn’t fetched', () => {
    const skipped = (reason: (typeof base.discovery)['notFetchedBecause']) =>
      after({
        ...base,
        response: null,
        content: null,
        discovery: found({ state: 'skipped', notFetchedBecause: reason }),
      });
    expect(skipped('noFollow')).toMatchObject({
      kind: 'skipped',
      title: 'Not fetched',
      why: 'Every link to it is nofollow, and the crawl doesn’t follow nofollow links.',
      pending: false,
    });
    expect(skipped('depthLimit').why).toBe('It’s deeper than the crawl’s depth limit.');
    expect(skipped('urlLimit').why).toBe('The crawl reached its URL limit before it.');
    expect(skipped('externalsDisabled').why).toBe('The crawl’s settings leave out URLs on other sites.');
    // No narrower reason stored, or one this version doesn't know.
    expect(skipped('none').why).toBe('The crawl found it but didn’t fetch it.');
    expect(skipped(null).why).toBe('The crawl found it but didn’t fetch it.');
  });

  it('goes by the outcome for a URL robots.txt disallows, whatever `fetched` says', () => {
    const state = after({
      ...base,
      content: null,
      response: testResponse({ fetched: true, outcome: 'robotsDisallowed', statusCode: null }),
    });
    expect(state).toMatchObject({
      kind: 'robots',
      title: 'Blocked by robots.txt',
      why: 'robots.txt disallows it, so the crawl didn’t request it.',
      answered: false,
      hasContent: false,
    });
  });

  it('gives the server’s reason when it blocked the crawler', () => {
    const state = after({
      ...base,
      content: null,
      response: testResponse({ outcome: 'blocked', statusCode: 403, blockReason: 'a challenge page' }),
    });
    expect(state).toMatchObject({
      kind: 'blocked',
      why: 'The server refused the crawler: a challenge page.',
      answered: true,
    });
    const bare = after({
      ...base,
      content: null,
      response: testResponse({ outcome: 'blocked', statusCode: null }),
    });
    expect(bare).toMatchObject({ why: 'The server refused the crawler.', answered: false });
  });

  it('says what went wrong when there was no answer', () => {
    const failed = (outcome: 'timeout' | 'networkError' | 'processingError' | 'notChecked') =>
      after({ ...base, content: null, response: testResponse({ outcome, statusCode: null, fetched: false }) });
    expect(failed('timeout')).toMatchObject({ kind: 'failed', title: 'Timed out' });
    expect(failed('networkError')).toMatchObject({ kind: 'failed', title: 'Couldn’t be reached' });
    expect(failed('processingError')).toMatchObject({ kind: 'failed', title: 'Couldn’t be read' });
    expect(failed('notChecked')).toMatchObject({ kind: 'notChecked', title: 'Not checked' });
  });

  it('tells a redirect, an error, a status check and a file from a page with no body', () => {
    const answered = (overrides: Parameters<typeof testResponse>[0], rest: Partial<typeof base> = {}) =>
      after({ ...base, content: null, response: testResponse(overrides), ...rest });
    expect(answered({ outcome: 'redirect', statusCode: 301 })).toMatchObject({ kind: 'redirected', answered: true });
    expect(answered({ outcome: 'clientError', statusCode: 404 })).toMatchObject({
      kind: 'errorStatus',
      title: 'Answered 404 Not Found',
    });
    expect(answered({ outcome: 'serverError', statusCode: 599 }).title).toBe('Answered 599');
    expect(answered({}, { discovery: found({ fetchMode: 'check' }) })).toMatchObject({
      kind: 'statusOnly',
      title: 'Status checked only',
    });
    expect(answered({}, { type: 'image' })).toMatchObject({
      kind: 'notPage',
      why: 'It’s an image: the crawl reads the content of pages only.',
    });
    expect(answered({}, { type: 'pdf' }).why).toBe('It’s a PDF: the crawl reads the content of pages only.');
    expect(answered({ bodyBytes: 0, sizeBytes: 0 })).toMatchObject({
      kind: 'noBody',
      title: 'Answered with no body',
      why: 'It answered 200 with nothing to read.',
      answered: true,
      hasContent: false,
    });
  });

  it('says there’s no answer stored when an outcome comes without a status', () => {
    expect(
      after({ ...base, content: null, response: testResponse({ outcome: null, statusCode: null }) }),
    ).toMatchObject({
      kind: 'failed',
      title: 'No response',
    });
  });
});

describe('hreflangProblem', () => {
  const entry = { language: 'fr', href: '/fr/', url: 'https://example.com/fr/', inHead: true, fromHttpHeader: false };
  const target = (statusCode: number | null) => ({ id: 9, url: 'https://example.com/fr/', statusCode });

  it('finds nothing wrong with an annotation in the head whose target answers', () => {
    expect(hreflangProblem({ ...entry, target: target(200) })).toBeNull();
    expect(hreflangProblem({ ...entry, inHead: false, fromHttpHeader: true, target: target(200) })).toBeNull();
  });

  it('says what’s wrong, the address first', () => {
    expect(hreflangProblem({ ...entry, url: null, target: null })).toBe('Its address isn’t a URL');
    expect(hreflangProblem({ ...entry, inHead: false, target: target(200) })).toBe(
      'Outside the head, where search engines ignore it',
    );
    expect(hreflangProblem({ ...entry, target: null })).toBe('Not in this crawl');
    // Its status is all that comes with a target: no code doesn't say it was never fetched.
    expect(hreflangProblem({ ...entry, target: target(null) })).toBe('No status in this crawl');
    expect(hreflangProblem({ ...entry, target: target(404) })).toBe('Answers 404');
    expect(hreflangProblem({ ...entry, target: target(301) })).toBe('Answers 301');
  });
});

describe('the link grids’ words', () => {
  it('says whether a hyperlink is followed, then its other flags', () => {
    expect(followWords({ followed: true, flags: [] })).toBe('Followed');
    expect(followWords({ followed: true, flags: ['imageLink'] })).toBe('Followed, image link');
    expect(followWords({ followed: false, flags: ['noFollow', 'sponsored'] })).toBe('nofollow, sponsored');
    // The page's own nofollow stops a link that carries no flag.
    expect(followWords({ followed: false, flags: [] })).toBe('Not followed');
    expect(followWords({ followed: false, flags: ['ugc'] })).toBe('Not followed, ugc');
  });

  it('gives any other kind of link its flags alone', () => {
    expect(followWords({ followed: null, flags: [] })).toBe('');
    expect(followWords({ followed: null, flags: ['outsideHead', 'fromHttpHeader'] })).toBe(
      'outside the head, from the HTTP header',
    );
  });

  it('says why the other end has no status', () => {
    expect(linkStatusWords({ statusCode: 404, fetched: true })).toBe('404');
    // An outcome with no code may be robots.txt's refusal, where no request was made: never "No response".
    expect(linkStatusWords({ statusCode: null, fetched: true })).toBe('No status');
    expect(linkStatusWords({ statusCode: null, fetched: false })).toBe('Not fetched');
  });

  it('remembers the kind each grid showed', () => {
    resetLinkKinds();
    expect(chosenLinkKind('inlinks')).toBe('all');
    chooseLinkKind('inlinks', 'hyperlink');
    expect(chosenLinkKind('inlinks')).toBe('hyperlink');
    expect(chosenLinkKind('outlinks')).toBe('all');
    resetLinkKinds();
    expect(chosenLinkKind('inlinks')).toBe('all');
  });
});

describe('hidden checks', () => {
  const issues = [
    { checkId: 'title-too-long', severity: 'notice' as const, detail: '61 characters', related: null },
    { checkId: 'internal-client-error', severity: 'error' as const, detail: 'HTTP 404', related: null },
    { checkId: 'low-word-count', severity: 'notice' as const, detail: '85 words', related: null },
  ];

  it('keeps a URL’s issues of hidden checks apart, in their order, to list last and leave uncounted', () => {
    expect(splitHidden(issues, ['title-too-long', 'not-this-url’s'])).toEqual({
      counted: [issues[1], issues[2]],
      hidden: [issues[0]],
    });
    expect(splitHidden(issues, [])).toEqual({ counted: issues, hidden: [] });
  });

  it('leaves them out of the Issues tab’s count, and has no count when only hidden checks flagged the URL', () => {
    const detail = testUrlDetail({ issues });
    expect(sectionCounts(detail).issues).toBe(3);
    expect(sectionCounts(detail, ['title-too-long']).issues).toBe(2);
    expect(
      sectionCounts(
        detail,
        issues.map((issue) => issue.checkId),
      ).issues,
    ).toBeUndefined();
  });
});

describe('depth', () => {
  it('calls depth 0 the start URL only for the URL the crawl started at', () => {
    expect(depthWords({ depth: 0, source: 'seed' })).toBe('The start URL');
    // A probe of the site's other versions is asked for by the crawl itself, at depth 0 too.
    expect(depthWords({ depth: 0, source: 'siteVersion' })).toBe('Not reached by links: the crawl asked for it itself');
    expect(depthWords({ depth: 1, source: 'link' })).toBe('1 click from the start URL');
    expect(depthWords({ depth: 3, source: 'sitemap' })).toBe('3 clicks from the start URL');
    expect(depthWords({ depth: null, source: 'sitemap' })).toBe('Not reached by links from the start URL');
    // A source this engine doesn't know: its depth is said as it is.
    expect(depthWords({ depth: 0, source: null })).toBe('0 clicks from the start URL');
  });

  it('says it in brief for the head, without a depth for a probe', () => {
    expect(depthInBrief({ depth: 2, source: 'link' })).toBe(', depth 2');
    expect(depthInBrief({ depth: 0, source: 'seed' })).toBe(', depth 0');
    expect(depthInBrief({ depth: 0, source: 'siteVersion' })).toBe(', another version of the site');
    expect(depthInBrief({ depth: null, source: 'sitemap' })).toBe('');
  });
});

describe('issueLink', () => {
  const issue = (overrides: Partial<UrlIssue>): UrlIssue => ({
    checkId: 'empty-anchor-text',
    severity: 'notice',
    detail: 'Links to 1 URL',
    related: { id: 1, url: 'https://example.com/' },
    ...overrides,
  });

  it('uses the group the engine gives', () => {
    const group = { detail: 'Root-relative: starts with /', relatedUrlId: null };
    expect(issueLink(issue({ checkId: 'canonical-relative', detail: '/a', related: null, group }), 'detail')).toEqual({
      checkId: 'canonical-relative',
      group,
    });
  });

  it('links to the check alone when the engine says the check doesn’t group', () => {
    expect(issueLink(issue({ checkId: 'title-too-long', group: null }), 'none')).toEqual({
      checkId: 'title-too-long',
      group: undefined,
    });
    // The engine's word wins over the catalog's.
    expect(issueLink(issue({ group: null }), 'relatedUrl').group).toBeUndefined();
  });

  describe('with an engine that doesn’t give the group', () => {
    it('groups a related-URL check by the other URL', () => {
      expect(issueLink(issue({}), 'relatedUrl').group).toEqual({ detail: null, relatedUrlId: 1 });
      expect(issueLink(issue({ related: null }), 'relatedUrl').group).toEqual({ detail: null, relatedUrlId: null });
    });

    it('groups a detail check by its detail and other URL', () => {
      expect(
        issueLink(issue({ checkId: 'internal-client-error', detail: 'HTTP 404', related: null }), 'detail'),
      ).toEqual({
        checkId: 'internal-client-error',
        group: { detail: 'HTTP 404', relatedUrlId: null },
      });
      expect(issueLink(issue({ checkId: 'open-graph-missing', detail: 'Missing og:image' }), 'detail').group).toEqual({
        detail: 'Missing og:image',
        relatedUrlId: 1,
      });
    });

    describe('a duplicate check, grouped by cluster under its first page', () => {
      const duplicate = issue({ checkId: 'title-duplicate', detail: '1 other page', related: { id: 99, url: '' } });
      const analysed = { available: true, reason: null } as const;

      it('opens the first page’s own group from the first page, whose issue names another', () => {
        // The other pages' issues name the first page: that's what says it is the first.
        const first = { id: 6, namedBy: { total: 1, checks: [{ checkId: 'title-duplicate', count: 1 }] } };
        expect(issueLink(duplicate, 'relatedUrl', { ...first, summary: analysed }).group).toEqual({
          detail: null,
          relatedUrlId: 6,
        });
      });

      it('opens the first page’s group from the cluster’s other pages, whose issues name it', () => {
        const other = { id: 99, namedBy: { total: 0, checks: [] }, summary: analysed };
        const names = issue({ checkId: 'h1-duplicate', related: { id: 6, url: '' } });
        expect(issueLink(names, 'relatedUrl', other).group).toEqual({ detail: null, relatedUrlId: 6 });
        // Named by another check's issues only, it's no cluster's first page.
        const canonical = { ...other, namedBy: { total: 2, checks: [{ checkId: 'canonicalised-page', count: 2 }] } };
        expect(issueLink(names, 'relatedUrl', canonical).group).toEqual({ detail: null, relatedUrlId: 6 });
      });

      it('leads to the check alone when another version analysed the crawl, or the URL isn’t known', () => {
        const first = { id: 6, namedBy: { total: 1, checks: [{ checkId: 'title-duplicate', count: 1 }] } };
        const older = { available: false, reason: 'engineChanged' } as const;
        expect(issueLink(duplicate, 'relatedUrl', { ...first, summary: older }).group).toBeUndefined();
        expect(issueLink(duplicate, 'relatedUrl').group).toBeUndefined();
      });
    });

    it('links the checks that group by fault to the check alone', () => {
      for (const checkId of ['canonical-relative', 'canonical-invalid', 'structured-data-invalid']) {
        expect(issueLink(issue({ checkId, detail: 'its own detail', related: null }), 'detail').group).toBeUndefined();
      }
    });

    it('links a check that doesn’t group, or isn’t in the catalog, to the check alone', () => {
      expect(issueLink(issue({ checkId: 'title-too-long' }), 'none').group).toBeUndefined();
      expect(issueLink(issue({ checkId: 'a-newer-check' }), undefined).group).toBeUndefined();
    });
  });
});

describe('sections', () => {
  beforeEach(() => resetRememberedSection());

  it('lists the IA’s tabs in order', () => {
    expect(SECTION_IDS).toEqual([
      'summary',
      'issues',
      'inlinks',
      'outlinks',
      'redirects',
      'headers',
      'structuredData',
      'hreflang',
      'source',
    ]);
    expect(isSectionId('headers')).toBe(true);
    expect(isSectionId('nope')).toBe(false);
    expect(isSectionId(null)).toBe(false);
  });

  it('counts what each section lists', () => {
    expect(sectionCounts(testUrlDetail())).toEqual({
      issues: 1,
      inlinks: 3,
      outlinks: 4,
      headers: 4,
      structuredData: 1,
    });
  });

  it('shows no count for nothing, nor for what isn’t known', () => {
    const counts = sectionCounts(
      testUrlDetail({ issues: null, links: null, headers: [], structuredData: [], hreflang: [] }),
    );
    expect(counts).toEqual({});
  });

  it('remembers the section chosen for the session, starting on Summary', () => {
    expect(rememberedSection()).toBe('summary');
    rememberSection('inlinks');
    expect(rememberedSection()).toBe('inlinks');
    expect(sessionStorage.getItem('si.inspector.section')).toBe('inlinks');
  });

  it('reads the session’s section back, and ignores one it doesn’t know', () => {
    sessionStorage.setItem('si.inspector.section', 'headers');
    expect(rememberedSection()).toBe('headers');
    resetRememberedSection();
    sessionStorage.setItem('si.inspector.section', 'gone');
    expect(rememberedSection()).toBe('summary');
  });
});
