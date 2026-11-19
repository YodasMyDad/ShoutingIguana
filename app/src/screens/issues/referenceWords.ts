// The words of a reference of a check's row (ReferenceCells.tsx draws them): what its link says, where on the page it
// is, what else the check says of it, and the page's URL opened at the link. Pure.
import type { Check } from '../../api/types';
import { followWords } from '../../inspector/links';
import { urlAtText } from '../../lib/textFragment';
import { linkWhereWords, targetRestWords, type WhereRow } from './whereFound';

/** What opening the page at a link's words says it does. */
export const OPEN_AT_LINK = 'Open the page at this link';

/** The words a browser can look for on the page that holds a link: what the link says. */
function wordsOf(text: string | null | undefined): string | null {
  const words = text?.trim();
  return words === undefined || words === '' ? null : words;
}

/**
 * The page's URL opened at a link's words (`urlAtText`), with the tooltip that says so; nothing where the link has no
 * words to find, and the page then opens at its top as any URL does.
 */
export function openAtLink(pageUrl: string, text: string | null | undefined): { openUrl?: string; openHint?: string } {
  const words = wordsOf(text);
  if (!words) return {};
  const at = urlAtText(pageUrl, words);
  return at === pageUrl ? {} : { openUrl: at, openHint: OPEN_AT_LINK };
}

/** What a reference's link says, as text: its anchor text, an image's alt text, a hreflang link's language. */
export function referenceText(where: WhereRow): string | null {
  if (where.kind === 'source') return wordsOf(where.link.text);
  if (where.kind === 'target') return wordsOf(where.target.text);
  return null;
}

/** Where on its page a reference's link is, as text: "Footer", "Image in the content · nofollow". */
export function referencePlace(where: WhereRow): string {
  if (where.kind === 'source') {
    const follow = followWords(where.link);
    return [linkWhereWords(where.link), follow === 'Followed' ? '' : follow].filter((part) => part).join(' · ');
  }
  if (where.kind === 'target') return linkWhereWords(where.target);
  return '';
}

/** What the check says of the URL a page names beyond its status, its text and its place. */
export function referenceRest(where: WhereRow, view: { check: Check; siteOrigin: string | null }): string {
  return where.kind === 'target' ? targetRestWords(where.target, view.check, view.siteOrigin) : '';
}
