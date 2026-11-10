// The response's headers as the inspector lists them (docs/v2-sidecar-api.md: stored with names lowercased, in the
// order they came, a name repeated as often as the server sent it). A repeated name becomes one row with every value,
// in the place of its first, so two `x-frame-options` read as one header said twice. Long values are flagged, so the
// view can fold them instead of pushing everything after them off the panel.

/** A value longer than this folds: a `content-security-policy`, a `set-cookie`. */
export const LONG_HEADER_VALUE = 160;

export interface HeaderRow {
  name: string;
  /** Every value sent under the name, in order. */
  values: string[];
  /** One of its values is long enough to fold. */
  long: boolean;
}

export function shapeHeaders(headers: readonly { name: string; value: string }[]): HeaderRow[] {
  const rows = new Map<string, HeaderRow>();
  for (const header of headers) {
    const name = header.name.toLowerCase();
    const row = rows.get(name);
    if (row) {
      row.values.push(header.value);
      row.long ||= header.value.length > LONG_HEADER_VALUE;
    } else {
      rows.set(name, { name, values: [header.value], long: header.value.length > LONG_HEADER_VALUE });
    }
  }
  return [...rows.values()];
}

/** The rows whose name or a value contains the text, ignoring case. Every row for no text. */
export function filterHeaders(rows: readonly HeaderRow[], text: string): HeaderRow[] {
  const needle = text.trim().toLowerCase();
  if (needle === '') return [...rows];
  return rows.filter(
    (row) => row.name.includes(needle) || row.values.some((value) => value.toLowerCase().includes(needle)),
  );
}

/** The headers as text, a line each, for copying: `name: value`. */
export function headersText(rows: readonly HeaderRow[]): string {
  return rows.flatMap((row) => row.values.map((value) => `${row.name}: ${value}`)).join('\n');
}
