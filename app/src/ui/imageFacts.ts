// What an image's thumbnail draws by what the crawl knows of the image: the broken-image icon only for an image known
// to be broken. One that couldn't be read here (offline, refused by a policy) or that the crawl never asked for is
// neither: it gets the neutral image icon (`ImageThumb`). Pure.
import type { UrlOutcome } from '../api/grids';

/** The outcomes of a fetch that got no usable answer: the image isn't there to show. */
const NO_ANSWER: ReadonlySet<UrlOutcome> = new Set<UrlOutcome>([
  'clientError',
  'serverError',
  'timeout',
  'networkError',
  'blocked',
]);

export interface ImageAnswer {
  statusCode?: number | null | undefined;
  outcome?: UrlOutcome | null | undefined;
  /** Where its redirects end, when it redirects: the image is that one. */
  final?: { statusCode?: number | null | undefined; outcome?: UrlOutcome | null | undefined } | null | undefined;
}

/** Whether an image is known to be broken: it answered 4xx or 5xx, or didn't answer, after any redirects. */
export function isBrokenImage(image: ImageAnswer): boolean {
  const end = image.final ?? image;
  if (typeof end.statusCode === 'number') return end.statusCode >= 400;
  return end.outcome !== null && end.outcome !== undefined && NO_ANSWER.has(end.outcome);
}
