import type { LinkFlag } from '../../api/inspector';

/**
 * The case of a check's fix (a `FixExample`'s `case`) that what the crawl found points to, for `HowToFix` to show
 * first; undefined for a check with one fix, or when nothing found says which.
 *
 * `empty-anchor-text`: a link with nothing to read either wraps an image (the crawler flags it `imageLink`: the fix is
 * the image's alt text) or it doesn't, and then it's nearly always an icon or an inline SVG, as a logo, a basket or a
 * search icon is (the fix is an aria-label on the link). A link with nothing inside at all can't be told from an icon
 * by its flags, so that case is never the one chosen: it stays one click away.
 *
 * Given the flags of each link the check named (an issue's targets, a group's related URL), the case most of them are.
 */
export function preferredFixCase(
  checkId: string,
  links: readonly (readonly LinkFlag[] | undefined)[],
): string | undefined {
  if (checkId !== 'empty-anchor-text' || links.length === 0) return undefined;
  const images = links.filter((flags) => flags?.includes('imageLink')).length;
  return images * 2 > links.length ? 'image-link' : 'icon-link';
}
