import { describe, expect, it } from 'vitest';
import { spreadsheetText, toTsv, tsvField } from './tsv';

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

  it('quotes a bare carriage return, which Excel reads as a line break', () => {
    expect(tsvField('a\rb')).toBe('"a\rb"');
  });

  it('copies without a header when given none', () => {
    expect(toTsv(null, [['only']])).toBe('only');
  });

  it('never lets crawled text paste as a formula: a quote goes before =, +, -, @, a tab or a carriage return', () => {
    expect(tsvField('=HYPERLINK("https://evil.example/","Click")')).toBe(
      '"\'=HYPERLINK(""https://evil.example/"",""Click"")"',
    );
    expect(tsvField('+44 20 7946 0958')).toBe("'+44 20 7946 0958");
    expect(tsvField('- 50% off')).toBe("'- 50% off");
    expect(tsvField('@SUM(A1)')).toBe("'@SUM(A1)");
    expect(tsvField('\tindented')).toBe('"\'\tindented"');
    expect(tsvField('\rstart')).toBe('"\'\rstart"');
    // Anywhere but the start is harmless.
    expect(tsvField('a=b+c-d@e')).toBe('a=b+c-d@e');
    expect(toTsv(['Title'], [['=1+1']])).toBe("Title\r\n'=1+1");
  });

  it('leaves the text alone otherwise', () => {
    expect(spreadsheetText('https://example.com/')).toBe('https://example.com/');
    expect(spreadsheetText('')).toBe('');
  });
});
