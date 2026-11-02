// Copying rows as tab-separated text that pastes into Excel, Google Sheets and Numbers as cells. A value with a tab,
// a line break or a quote is quoted, with its quotes doubled, as Excel itself writes them.
//
// Crawled text is the site's, not ours: a title such as "=HYPERLINK(…)" or "+44 20 …" would paste as a formula. So a
// value that starts with =, +, -, @, a tab or a carriage return gets a ' in front, as OWASP advises against CSV
// injection: spreadsheets show it as plain text, without the quote. Only copies of rows are for spreadsheets; copying
// one cell gives its exact value (GridCore).

/** Copying more than this many rows asks for an export instead: the clipboard isn't for a million rows. */
export const MAX_COPY_ROWS = 10_000;

/** The first characters that make a spreadsheet read a cell as a formula (OWASP, CSV injection). */
const FORMULA_START = /^[=+\-@\t\r]/;

/** A value as a spreadsheet must read it: plain text, never a formula. */
export function spreadsheetText(value: string): string {
  return FORMULA_START.test(value) ? `'${value}` : value;
}

export function tsvField(value: string): string {
  const text = spreadsheetText(value);
  return /[\t\n\r"]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

/** Lines end with CRLF, which every spreadsheet reads; there's no line ending after the last row. */
export function toTsv(header: readonly string[] | null, rows: readonly (readonly string[])[]): string {
  const lines = header ? [header, ...rows] : rows;
  return lines.map((cells) => cells.map(tsvField).join('\t')).join('\r\n');
}
