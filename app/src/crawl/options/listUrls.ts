// A list of addresses, as the engine's ListUrlFile reads one: a text list is one address a line, and a CSV file has a
// Url column and an optional Priority column. A higher number is crawled first. The same address is kept once.

const DEFAULT_PRIORITY = 1000;

export interface ReadList {
  urls: string[];
  /** Why the text can't be used, or null when it can. An empty text is an empty list, not an error. */
  error: string | null;
}

/** The addresses in this text, highest priority first. */
export function readList(text: string): ReadList {
  if (text.trim() === '') return { urls: [], error: null };
  const lines = text
    .replace(/^\uFEFF/, '')
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line !== '');
  if (lines.length === 0) return { urls: [], error: null };

  const header = cells(lines[0] ?? '');
  const urlColumn = header.findIndex((cell) => cell.toLowerCase() === 'url');
  const priorityColumn = urlColumn >= 0 ? header.findIndex((cell) => cell.toLowerCase() === 'priority') : -1;
  const data = urlColumn >= 0 ? lines.slice(1) : lines;
  const rows: { url: string; priority: number; index: number }[] = [];
  for (const [index, line] of data.entries()) {
    const row = urlColumn >= 0 ? cells(line) : [line];
    const url = (urlColumn >= 0 ? row[urlColumn] : row[0])?.trim() ?? '';
    if (url === '') continue;
    const priorityText = priorityColumn >= 0 ? (row[priorityColumn]?.trim() ?? '') : '';
    if (priorityText !== '' && !/^-?\d+$/.test(priorityText)) {
      return { urls: [], error: `Priority '${priorityText}' in the list isn't a whole number.` };
    }
    rows.push({
      url,
      priority: priorityText === '' ? DEFAULT_PRIORITY : Number(priorityText),
      index,
    });
  }
  if (rows.length === 0) return { urls: [], error: 'The list has no URLs.' };

  rows.sort((left, right) => right.priority - left.priority || left.index - right.index);
  const seen = new Set<string>();
  const urls: string[] = [];
  for (const row of rows) {
    const key = identity(row.url);
    if (seen.has(key)) continue;
    seen.add(key);
    urls.push(row.url);
  }
  return { urls, error: null };
}

/** The address as the crawl stores it, so two spellings collapse. One with a user name stays as written. */
function identity(url: string): string {
  try {
    const parsed = new URL(url);
    if (parsed.username !== '' || parsed.password !== '') return url;
    parsed.hash = '';
    if (
      (parsed.protocol === 'http:' && parsed.port === '80') ||
      (parsed.protocol === 'https:' && parsed.port === '443')
    ) {
      parsed.port = '';
    }
    return parsed.toString();
  } catch {
    return url;
  }
}

/** CSV cells, with quotes. A text line is never split: a comma there belongs to the address. */
function cells(line: string): string[] {
  const out: string[] = [];
  let current = '';
  let quoted = false;
  for (let index = 0; index < line.length; index++) {
    const character = line.charAt(index);
    if (quoted) {
      if (character === '"') {
        if (line.charAt(index + 1) === '"') {
          current += '"';
          index++;
        } else {
          quoted = false;
        }
      } else {
        current += character;
      }
    } else if (character === '"') {
      quoted = true;
    } else if (character === ',') {
      out.push(current.trim());
      current = '';
    } else {
      current += character;
    }
  }
  out.push(current.trim());
  return out;
}
