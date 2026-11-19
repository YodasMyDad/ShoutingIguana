// A row's detail with its addresses told from its words (A-18 of the 2026-11-19 UX round): the engine's sentence for a
// check of the site's files names them in full ("https://example.com/robots.txt is empty, so it has no Sitemap: line;
// the sitemap was found at https://example.com/sitemap.xml"). An address is a link like any other; the file the row is
// already about, named in its own column, is said by its name alone.

export type DetailPart =
  | { kind: 'text'; text: string }
  /** An address the sentence names: a link. */
  | { kind: 'url'; url: string }
  /** The file the row is about, as its name: "robots.txt". */
  | { kind: 'file'; url: string; name: string };

/** An absolute address in a sentence, up to its closing punctuation. */
const ADDRESS = /https?:\/\/[^\s"“”<>]+/g;

/** What ends a sentence or a clause, not an address. */
const TRAILING = /[.,;:!?)\]’'”]+$/;

/** A file's name: the last part of its path, or its host for one without. */
function fileName(url: string): string {
  try {
    const parsed = new URL(url);
    return parsed.pathname.split('/').filter(Boolean).pop() ?? parsed.host;
  } catch {
    return url;
  }
}

/** The detail as words and addresses, in order. `files` are the addresses its row already names. */
export function detailParts(detail: string, files: readonly string[] = []): DetailPart[] {
  const parts: DetailPart[] = [];
  let from = 0;
  for (const match of detail.matchAll(ADDRESS)) {
    const url = match[0].replace(TRAILING, '');
    // What isn't an address a browser could open ("http://[not", an invalid canonical) stays words.
    if (!URL.canParse(url)) continue;
    if (match.index > from) parts.push({ kind: 'text', text: detail.slice(from, match.index) });
    parts.push(files.includes(url) ? { kind: 'file', url, name: fileName(url) } : { kind: 'url', url });
    from = match.index + url.length;
  }
  if (from < detail.length) parts.push({ kind: 'text', text: detail.slice(from) });
  return parts;
}
