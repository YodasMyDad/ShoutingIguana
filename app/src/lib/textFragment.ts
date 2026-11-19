// A page's URL that scrolls to some of its words and highlights them when a browser opens it: the URL's text
// fragment (`#:~:text=`), which Chrome, Edge, Safari and Firefox understand, and which any other browser ignores.
// It's how "open the page that holds the broken link" lands on the link.

/** The longest text worth matching: a browser matches it whole, and a long run is more likely to span elements. */
const LONGEST = 80;

/** The characters the text directive's own syntax uses, which `encodeURIComponent` leaves alone. */
const encodeDirective = (text: string): string => encodeURIComponent(text).replace(/-/g, '%2D');

/** The words as a browser can find them: one line, trimmed, cut at a word where they run long. */
function matchable(text: string): string {
  const oneLine = text.replace(/\s+/g, ' ').trim();
  if (oneLine.length <= LONGEST) return oneLine;
  const cut = oneLine.slice(0, LONGEST);
  const lastSpace = cut.lastIndexOf(' ');
  return (lastSpace > LONGEST / 2 ? cut.slice(0, lastSpace) : cut).trim();
}

/**
 * The URL, opening at the words: `https://example.com/page/` and `Buy now` give
 * `https://example.com/page/#:~:text=Buy%20now`. A fragment the URL already has is kept ahead of the directive. With
 * no words to find (an image link with no alt text), or a URL that isn't http(s), it's the URL as it was.
 */
export function urlAtText(url: string, text: string | null | undefined): string {
  if (!/^https?:\/\//i.test(url)) return url;
  const words = matchable(text ?? '');
  if (words === '') return url;
  const directive = `:~:text=${encodeDirective(words)}`;
  // A URL that already carries a directive keeps its own.
  if (url.includes(':~:')) return url;
  return url.includes('#') ? `${url}${directive}` : `${url}#${directive}`;
}
