// Copying rows as tab-separated text that pastes into Excel, Google Sheets and Numbers as cells. A value with a tab,
// a line break or a quote is quoted, with its quotes doubled, as Excel itself writes them.

/** Copying more than this many rows asks for an export instead: the clipboard isn't for a million rows. */
export const MAX_COPY_ROWS = 10_000;

export function tsvField(value: string): string {
  return /[\t\n\r"]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value;
}

/** Lines end with CRLF, which every spreadsheet reads; there's no line ending after the last row. */
export function toTsv(header: readonly string[] | null, rows: readonly (readonly string[])[]): string {
  const lines = header ? [header, ...rows] : rows;
  return lines.map((cells) => cells.map(tsvField).join('\t')).join('\r\n');
}
