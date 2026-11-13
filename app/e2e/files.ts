// Reading back what an export wrote, to compare it with the grid: a CSV as RFC 4180 has it (a byte-order mark, CRLF,
// quoted fields), and an Excel workbook's sheets, read from the zip with nothing but Node (its central directory, each
// entry inflated, the sheets' XML read for their cells' text).
import { readFileSync } from 'node:fs';
import { inflateRawSync } from 'node:zlib';

/** A CSV file's rows, each its fields. */
export function readCsv(path: string): string[][] {
  let text = readFileSync(path, 'utf8');
  if (text.charCodeAt(0) === 0xfeff) text = text.slice(1);
  const rows: string[][] = [];
  let row: string[] = [];
  let field = '';
  let quoted = false;
  for (let index = 0; index < text.length; index += 1) {
    const char = text.charAt(index);
    if (quoted) {
      if (char === '"' && text[index + 1] === '"') {
        field += '"';
        index += 1;
      } else if (char === '"') quoted = false;
      else field += char;
    } else if (char === '"') quoted = true;
    else if (char === ',') {
      row.push(field);
      field = '';
    } else if (char === '\r' && text[index + 1] === '\n') {
      row.push(field);
      rows.push(row);
      row = [];
      field = '';
      index += 1;
    } else field += char;
  }
  if (field !== '' || row.length > 0) {
    row.push(field);
    rows.push(row);
  }
  return rows;
}

/** A zip's entries by name. */
export function readZip(path: string): Map<string, Buffer> {
  const zip = readFileSync(path);
  let end = zip.length - 22;
  while (end >= 0 && zip.readUInt32LE(end) !== 0x06054b50) end -= 1;
  if (end < 0) throw new Error(`${path} isn't a zip`);
  const count = zip.readUInt16LE(end + 10);
  let at = zip.readUInt32LE(end + 16);
  const entries = new Map<string, Buffer>();
  for (let index = 0; index < count; index += 1) {
    const method = zip.readUInt16LE(at + 10);
    const size = zip.readUInt32LE(at + 20);
    const nameLength = zip.readUInt16LE(at + 28);
    const extraLength = zip.readUInt16LE(at + 30);
    const commentLength = zip.readUInt16LE(at + 32);
    const local = zip.readUInt32LE(at + 42);
    const name = zip.toString('utf8', at + 46, at + 46 + nameLength);
    const dataAt = local + 30 + zip.readUInt16LE(local + 26) + zip.readUInt16LE(local + 28);
    const data = zip.subarray(dataAt, dataAt + size);
    entries.set(name, method === 8 ? inflateRawSync(data) : Buffer.from(data));
    at += 46 + nameLength + extraLength + commentLength;
  }
  return entries;
}

const unescape = (text: string) =>
  text
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&#(\d+);/g, (_, code: string) => String.fromCodePoint(Number(code)))
    .replace(/&#x([0-9a-f]+);/gi, (_, code: string) => String.fromCodePoint(parseInt(code, 16)))
    .replace(/&amp;/g, '&');

/** The text of every `<t>` in an XML fragment, joined: a cell's inline or shared string. */
const texts = (xml: string) =>
  [...xml.matchAll(/<t(?: [^>]*)?>([\s\S]*?)<\/t>/g)].map((match) => unescape(match[1] ?? '')).join('');

/** Column letters to a 0-based index: A is 0, AA is 26. */
function columnIndex(reference: string): number {
  const letters = /^[A-Z]+/.exec(reference)?.[0] ?? 'A';
  let sum = 0;
  for (let index = 0; index < letters.length; index += 1) sum = sum * 26 + letters.charCodeAt(index) - 64;
  return sum - 1;
}

/** A workbook's sheets, by name in their order, each its rows of cells as text. */
export function readXlsx(path: string): { name: string; rows: string[][] }[] {
  const zip = readZip(path);
  const xml = (name: string) => zip.get(name)?.toString('utf8') ?? '';
  const shared = [...xml('xl/sharedStrings.xml').matchAll(/<si>([\s\S]*?)<\/si>/g)].map((match) =>
    texts(match[1] ?? ''),
  );
  const relationships = new Map(
    [...xml('xl/_rels/workbook.xml.rels').matchAll(/<Relationship [^>]*Id="([^"]+)"[^>]*Target="([^"]+)"/g)].map(
      (match) => [match[1] ?? '', (match[2] ?? '').replace(/^\/?xl\//, '')],
    ),
  );
  const sheets = [...xml('xl/workbook.xml').matchAll(/<sheet [^>]*name="([^"]+)"[^>]*r:id="([^"]+)"/g)];
  return sheets.map((sheet) => {
    const target = relationships.get(sheet[2] ?? '') ?? '';
    const rows = [...xml(`xl/${target}`).matchAll(/<row[^>]*>([\s\S]*?)<\/row>/g)].map((row) => {
      const cells: string[] = [];
      for (const cell of (row[1] ?? '').matchAll(/<c ([^>]*?)(?:\/>|>([\s\S]*?)<\/c>)/g)) {
        const attributes = cell[1] ?? '';
        const body = cell[2] ?? '';
        const reference = /r="([A-Z]+)\d+"/.exec(attributes)?.[1] ?? '';
        const type = /t="([^"]+)"/.exec(attributes)?.[1];
        const value = /<v>([\s\S]*?)<\/v>/.exec(body)?.[1];
        let text: string;
        if (type === 's') text = shared[Number(value)] ?? '';
        else if (type === 'inlineStr' || type === 'str') text = type === 'str' ? unescape(value ?? '') : texts(body);
        else if (type === 'b') text = value === '1' ? 'TRUE' : 'FALSE';
        else text = value === undefined ? '' : unescape(value);
        cells[reference ? columnIndex(reference) : cells.length] = text;
      }
      return Array.from(cells, (cell: string | undefined) => cell ?? '');
    });
    return { name: unescape(sheet[1] ?? ''), rows };
  });
}
