// What the command palette lists for a query: every word typed must appear in the entry's title or keywords; the
// best matches come first within each group, and the groups keep their order so the list never jumps around.

export interface PaletteEntry {
  id: string;
  group: string;
  title: string;
  keywords?: readonly string[];
}

function score(entry: PaletteEntry, words: readonly string[], phrase: string): number {
  const title = entry.title.toLowerCase();
  const haystack = [title, ...(entry.keywords ?? []).map((keyword) => keyword.toLowerCase())].join(' ');
  if (!words.every((word) => haystack.includes(word))) return 0;
  if (title.startsWith(phrase)) return 4;
  if (title.split(/\W+/).some((part) => part.startsWith(phrase))) return 3;
  if (title.includes(phrase)) return 2;
  return 1;
}

/** The entries matching `query`, best first within each group, groups in the order given. */
export function searchPalette<T extends PaletteEntry>(
  entries: readonly T[],
  query: string,
  groupOrder: readonly string[],
): T[] {
  const phrase = query.trim().toLowerCase();
  const rank = (group: string) => {
    const index = groupOrder.indexOf(group);
    return index < 0 ? groupOrder.length : index;
  };
  if (phrase === '') return [...entries].sort((a, b) => rank(a.group) - rank(b.group));
  const words = phrase.split(/\s+/);
  return entries
    .map((entry, index) => ({ entry, index, score: score(entry, words, phrase) }))
    .filter((candidate) => candidate.score > 0)
    .sort((a, b) => rank(a.entry.group) - rank(b.entry.group) || b.score - a.score || a.index - b.index)
    .map((candidate) => candidate.entry);
}

/**
 * Whether what's typed reads as a URL to go to: one with a scheme (`https://…`), a path on the site (`/contact/`), a
 * protocol-relative one (`//example.com/a`) or a host with a dot (`www.example.com/a`). Never text with a space.
 */
export function looksLikeUrl(query: string): boolean {
  const text = query.trim();
  if (text === '' || /\s/.test(text)) return false;
  if (text === '/' || /^https?:\/\/\S/i.test(text) || /^\/\/?\S/.test(text)) return true;
  return /^[a-z0-9-]+(\.[a-z0-9-]+)+(:\d+)?([/?#]\S*)?$/i.test(text);
}
