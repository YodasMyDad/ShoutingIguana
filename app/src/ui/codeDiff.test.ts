import { describe, expect, it } from 'vitest';
import { diffCode, type DiffPart } from './codeDiff';

const marked = (parts: readonly DiffPart[]) => parts.filter((part) => part.changed).map((part) => part.text);
const text = (parts: readonly DiffPart[]) => parts.map((part) => part.text).join('');

describe('diffCode', () => {
  it('marks an attribute added in the middle of a tag, without the space before it', () => {
    const before = '<a href="/"><svg viewBox="0 0 120 32">…</svg></a>';
    const after = '<a href="/" aria-label="Example Furniture"><svg viewBox="0 0 120 32">…</svg></a>';
    const diff = diffCode(before, after);
    expect(marked(diff.after)).toEqual(['aria-label="Example Furniture"']);
    expect(marked(diff.before)).toEqual([]);
    expect(text(diff.before)).toBe(before);
    expect(text(diff.after)).toBe(after);
  });

  it('marks an attribute added at the end of a tag', () => {
    const diff = diffCode(
      '<img src="/img/oak-table.jpg" alt="Oak dining table">',
      '<img src="/img/oak-table.jpg" alt="Oak dining table" width="1200" height="800">',
    );
    expect(marked(diff.after)).toEqual(['width="1200" height="800"']);
    expect(marked(diff.before)).toEqual([]);
  });

  it('marks a changed attribute value on both sides, as whole words', () => {
    const diff = diffCode(
      '<meta name="robots" content="noindex, follow">',
      '<meta name="robots" content="index, follow">',
    );
    expect(marked(diff.before)).toEqual(['noindex']);
    expect(marked(diff.after)).toEqual(['index']);
  });

  it('marks a value written into an empty attribute', () => {
    const diff = diffCode(
      '<a href="/"><img src="/img/logo.png" alt=""></a>',
      '<a href="/"><img src="/img/logo.png" alt="Example Furniture"></a>',
    );
    expect(marked(diff.after)).toEqual(['Example Furniture']);
    expect(marked(diff.before)).toEqual([]);
  });

  it('marks a new line whole, and nothing in the old code', () => {
    const before = 'User-agent: *\nDisallow: /cart/';
    const after = 'User-agent: *\nDisallow: /cart/\nSitemap: https://www.example.com/sitemap.xml';
    const diff = diffCode(before, after);
    expect(marked(diff.after)).toEqual(['Sitemap: https://www.example.com/sitemap.xml']);
    expect(marked(diff.before)).toEqual([]);
    expect(text(diff.after)).toBe(after);
  });

  it('marks a line taken out whole in the old code, and nothing in the new', () => {
    const before = 'User-agent: *\nDisallow: /blog/\nDisallow: /cart/';
    const after = 'User-agent: *\nDisallow: /cart/';
    const diff = diffCode(before, after);
    expect(marked(diff.before)).toEqual(['Disallow: /blog/']);
    expect(marked(diff.after)).toEqual([]);
    expect(text(diff.before)).toBe(before);
    expect(text(diff.after)).toBe(after);
  });

  it('takes the second of two alike lines out, not half of each', () => {
    const first = '<link rel="canonical" href="https://www.example.com/oak-dining-tables/">';
    const second = '<link rel="canonical" href="https://www.example.com/tables/">';
    expect(marked(diffCode(`${first}\n${second}`, first).before)).toEqual([second]);
    expect(marked(diffCode(`${first}\n${second}`, second).before)).toEqual([first]);
  });

  it('marks nothing when the code is the same', () => {
    const code = '<title>Oak dining tables</title>\n<meta charset="utf-8">';
    const diff = diffCode(code, code);
    expect(diff.before).toEqual([{ text: code, changed: false }]);
    expect(diff.after).toEqual([{ text: code, changed: false }]);
  });

  it('marks the changed line of several, and leaves the others', () => {
    const diff = diffCode('<h2>Delivery</h2>\n<h4>Returns</h4>', '<h2>Delivery</h2>\n<h3>Returns</h3>');
    expect(marked(diff.before)).toEqual(['h4', 'h4']);
    expect(marked(diff.after)).toEqual(['h3', 'h3']);
    expect(diff.after[0]).toEqual({ text: '<h2>Delivery</h2>\n<', changed: false });
  });

  it('marks attributes that run on to new lines without their indent or the line breaks', () => {
    const before = '<img src="/img/hero-1600.jpg" alt="Oak dining table">';
    const after =
      '<img src="/img/hero-1600.jpg" alt="Oak dining table"\n' +
      '     srcset="/img/hero-480.jpg 480w, /img/hero-1600.jpg 1600w"\n' +
      '     sizes="(max-width: 800px) 100vw, 800px">';
    const diff = diffCode(before, after);
    expect(marked(diff.after)).toEqual([
      'srcset="/img/hero-480.jpg 480w, /img/hero-1600.jpg 1600w"',
      'sizes="(max-width: 800px) 100vw, 800px"',
    ]);
    expect(marked(diff.before)).toEqual([]);
    expect(text(diff.after)).toBe(after);
  });

  it('marks one character taken out', () => {
    const diff = diffCode('{\n  …\n  "name": "Oak dining table",\n}', '{\n  …\n  "name": "Oak dining table"\n}');
    expect(marked(diff.before)).toEqual([',']);
    expect(marked(diff.after)).toEqual([]);
  });

  it('shows a rewritten attribute as one change, with its closing quote', () => {
    const diff = diffCode(`<a onclick="location.href='/sale/'">Sale</a>`, '<a href="/sale/">Sale</a>');
    expect(marked(diff.before)).toEqual([`onclick="location.href='/sale/'"`]);
    expect(marked(diff.after)).toEqual(['href="/sale/"']);
  });

  it('does not keep a short match between two rewritten halves', () => {
    const diff = diffCode(
      '<img src="data:image/png;base64,iVBORw0KGgo…" alt="Free delivery">',
      '<img src="/img/free-delivery.png" alt="Free delivery">',
    );
    expect(marked(diff.before)).toEqual(['data:image/png;base64,iVBORw0KGgo…']);
    expect(marked(diff.after)).toEqual(['/img/free-delivery.png']);
  });

  it('marks a renamed element at both its tags and keeps its words', () => {
    const diff = diffCode(
      '<div class="page-title">Oak dining tables</div>',
      '<h1 class="page-title">Oak dining tables</h1>',
    );
    expect(marked(diff.before)).toEqual(['div', 'div']);
    expect(marked(diff.after)).toEqual(['h1', 'h1']);
  });

  it('marks a header value, not its name', () => {
    const diff = diffCode('Content-Type: text/plain', 'Content-Type: text/javascript; charset=utf-8');
    expect(marked(diff.before)).toEqual(['plain']);
    expect(marked(diff.after)).toEqual(['javascript; charset=utf-8']);
  });

  it('keeps every character of both sides, whatever changed', () => {
    const pairs: [string, string][] = [
      ['', '<meta charset="utf-8">'],
      ['<html>', '<html lang="en-GB">'],
      ['a\n\nb', 'a\nb\n\nc'],
      ['<h1><img src="/img/logo.svg" alt="Example Furniture"></h1>', '<img src="/img/logo.svg">\n<h1>Tables</h1>'],
    ];
    for (const [before, after] of pairs) {
      const diff = diffCode(before, after);
      expect(text(diff.before)).toBe(before);
      expect(text(diff.after)).toBe(after);
    }
  });
});
