import { describe, expect, it } from 'vitest';
import { languageName, tokenizeCode, type CodeToken } from './codeTokens';

const of = (tokens: readonly CodeToken[], kind: CodeToken['kind']) =>
  tokens.filter((token) => token.kind === kind).map((token) => token.text);
const joined = (tokens: readonly CodeToken[]) => tokens.map((token) => token.text).join('');

describe('tokenizeCode', () => {
  it('colours an HTML tag, its attribute names and their values', () => {
    const code = '<a href="/" aria-label="Example Furniture"><svg viewBox="0 0 120 32">…</svg></a>';
    const tokens = tokenizeCode(code, 'html');
    expect(of(tokens, 'tag')).toEqual(['<a', '><svg', '>', '</svg></a>']);
    expect(of(tokens, 'attribute')).toEqual(['href', 'aria-label', 'viewBox']);
    expect(of(tokens, 'string')).toEqual(['"/"', '"Example Furniture"', '"0 0 120 32"']);
    expect(joined(tokens)).toBe(code);
  });

  it('leaves the text between tags plain, and reads a comment whole', () => {
    const tokens = tokenizeCode('<h1>Oak dining tables</h1><!-- the page heading -->', 'html');
    expect(of(tokens, 'plain')).toEqual(['Oak dining tables']);
    expect(of(tokens, 'comment')).toEqual(['<!-- the page heading -->']);
  });

  it('reads a tag that runs over several lines, a self-closing tag and a value in single quotes', () => {
    const code = '<img src="/a.jpg"\n     srcset="/a-480.jpg 480w">\n<br/>\n<a href=\'/b\' hidden>';
    const tokens = tokenizeCode(code, 'html');
    expect(of(tokens, 'attribute')).toEqual(['src', 'srcset', 'href', 'hidden']);
    expect(of(tokens, 'string')).toEqual(['"/a.jpg"', '"/a-480.jpg 480w"', "'/b'"]);
    expect(of(tokens, 'tag')).toEqual(['<img', '>', '<br/>', '<a', '>']);
    expect(joined(tokens)).toBe(code);
  });

  it('reads a snippet that stops mid-tag or holds a stray bracket without losing a character', () => {
    for (const code of ['<meta name="description" content="Solid oak', 'a < b', '<', '', '<a href=/sale/>Sale</a>']) {
      expect(joined(tokenizeCode(code, 'html'))).toBe(code);
    }
    expect(of(tokenizeCode('<a href=/sale/>Sale</a>', 'html'), 'string')).toEqual(['/sale/']);
  });

  it('reads XML as markup, its declaration too', () => {
    const tokens = tokenizeCode('<?xml version="1.0"?>\n<url>\n  <loc>https://www.example.com/</loc>\n</url>', 'xml');
    expect(of(tokens, 'tag')).toEqual(['<?xml', '?>', '<url>', '<loc>', '</loc>', '</url>']);
    expect(of(tokens, 'attribute')).toEqual(['version']);
  });

  it('colours the name of each HTTP header, not its value', () => {
    const code = "X-Content-Type-Options: nosniff\nContent-Security-Policy: frame-ancestors 'self'";
    const tokens = tokenizeCode(code, 'httpHeader');
    expect(of(tokens, 'attribute')).toEqual(['X-Content-Type-Options', 'Content-Security-Policy']);
    expect(joined(tokens)).toBe(code);
  });

  it('colours the fields of a robots.txt file, and its comments', () => {
    const code = 'User-agent: *\nDisallow:\n\nSitemap: https://www.example.com/sitemap.xml # the index';
    const tokens = tokenizeCode(code, 'robotsTxt');
    expect(of(tokens, 'attribute')).toEqual(['User-agent', 'Disallow', 'Sitemap']);
    expect(of(tokens, 'comment')).toEqual(['# the index']);
    expect(joined(tokens)).toBe(code);
  });

  it('tells a JSON property’s name from a string, and colours numbers and the three words', () => {
    const code = '{\n  …\n  "name": "Oak dining table",\n  "price": 499.5, "inStock": true, "gtin": null\n}';
    const tokens = tokenizeCode(code, 'json');
    expect(of(tokens, 'attribute')).toEqual(['"name"', '"price"', '"inStock"', '"gtin"']);
    expect(of(tokens, 'string')).toEqual(['"Oak dining table"']);
    expect(of(tokens, 'number')).toEqual(['499.5']);
    expect(of(tokens, 'keyword')).toEqual(['true', 'null']);
    expect(joined(tokens)).toBe(code);
  });

  it('shows a language it does not know as it is', () => {
    expect(tokenizeCode('RewriteRule ^old$ /new [R=301]', 'apache')).toEqual([
      { text: 'RewriteRule ^old$ /new [R=301]', kind: 'plain' },
    ]);
    expect(tokenizeCode('', 'apache')).toEqual([]);
  });
});

describe('languageName', () => {
  it('names each language as a reader knows it', () => {
    expect(['html', 'xml', 'httpHeader', 'robotsTxt', 'json', 'apache'].map(languageName)).toEqual([
      'HTML',
      'XML',
      'HTTP header',
      'robots.txt',
      'JSON',
      null,
    ]);
  });
});
