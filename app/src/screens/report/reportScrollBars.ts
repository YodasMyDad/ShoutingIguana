// The report's frame scrolls with the app's own scroll bars (the gallery's G6): left to itself, the engine's document in
// its frame drew Windows' classic bar, arrows and all, in the middle of the app's thin quiet ones. The report is the
// engine's HTML, the same document a user saves, so the bars are added to the frame's copy only, never to the file.
// The frame is paper, light in both themes: the bars take the light theme's values of the app's tokens.

export const SCROLL_BARS_STYLE_ID = 'si-app-scroll-bars';

interface ScrollBarLook {
  size: string;
  inset: string;
  thumbMin: string;
  thumb: string;
  thumbHover: string;
}

/** The app's scroll bar tokens, read from the page, with the colours as the light theme has them. */
export function appScrollBarLook(root: HTMLElement = document.documentElement): ScrollBarLook | null {
  const style = getComputedStyle(root);
  const token = (name: string) => style.getPropertyValue(name).trim();
  const size = token('--scrollbar-size');
  const inset = token('--scrollbar-inset');
  const thumbMin = token('--scrollbar-thumb-min');
  // A colour token holds light-dark(): a probe in the light scheme gives its light value, whatever the app's theme.
  const probe = document.createElement('span');
  probe.style.colorScheme = 'light';
  probe.style.position = 'absolute';
  probe.style.visibility = 'hidden';
  root.append(probe);
  const colour = (name: string) => {
    probe.style.color = `var(${name})`;
    return getComputedStyle(probe).color;
  };
  const thumb = colour('--color-border-control');
  const thumbHover = colour('--color-text-muted');
  probe.remove();
  if (!size || !inset || !thumbMin || !thumb || !thumbHover) return null;
  return { size, inset, thumbMin, thumb, thumbHover };
}

/** The style sheet, as base.css draws the app's bars. Under forced colours the frame keeps the system's bars. */
export function scrollBarStyle(look: ScrollBarLook): string {
  return [
    '@media not (forced-colors: active) {',
    `::-webkit-scrollbar { width: ${look.size}; height: ${look.size}; }`,
    `::-webkit-scrollbar-thumb { min-height: ${look.thumbMin}; min-width: ${look.thumbMin}; border: ${look.inset} solid transparent; border-radius: 9999px; background: ${look.thumb} padding-box; }`,
    `::-webkit-scrollbar-thumb:hover { background: ${look.thumbHover} padding-box; }`,
    '::-webkit-scrollbar-track, ::-webkit-scrollbar-corner { background: transparent; }',
    '}',
  ].join('\n');
}

/** The report's document with the app's scroll bars in its head (or first, if it has none). */
export function withAppScrollBars(text: string, look: ScrollBarLook | null): string {
  if (!look) return text;
  const style = `<style id="${SCROLL_BARS_STYLE_ID}">\n${scrollBarStyle(look)}\n</style>`;
  const head = /<\/head\s*>/i.exec(text);
  return head ? `${text.slice(0, head.index)}${style}${text.slice(head.index)}` : `${style}${text}`;
}
