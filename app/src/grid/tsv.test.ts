import { describe, expect, it } from 'vitest';
import { toTsv, tsvField } from './tsv';

describe('tab-separated copy', () => {
  it('joins cells with tabs and rows with CRLF, with no trailing line ending', () => {
    expect(
      toTsv(
        ['URL', 'Status'],
        [
          ['https://a/', '200'],
          ['https://b/', '404'],
        ],
      ),
    ).toBe('URL\tStatus\r\nhttps://a/\t200\r\nhttps://b/\t404');
  });

  it('quotes a value with a tab, a line break or a quote, doubling its quotes, as Excel does', () => {
    expect(tsvField('plain')).toBe('plain');
    expect(tsvField('a\tb')).toBe('"a\tb"');
    expect(tsvField('two\nlines')).toBe('"two\nlines"');
    expect(tsvField('say "hi"')).toBe('"say ""hi"""');
  });

  it('copies without a header when given none', () => {
    expect(toTsv(null, [['only']])).toBe('only');
  });
});
