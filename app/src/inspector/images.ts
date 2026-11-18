// The inspector's Images (step 3b): the images a page shows, each file once, the largest first, from `/urls/images`.
// Which are large (by the analysis setting the large-image check uses), what the section says of them, and where each
// leads in the Links grid. Pure, so each is unit-tested.
import type { GridFilter } from '../api/grids';
import type { PageImage, PageImages } from '../api/inspector';
import { pixelsWords } from '../crawl/evidence';
import { formatBytes, formatCount, formatCountOf } from '../lib/format';
import { POSITION_WORDS } from './words';

/** The most images the section lists: the largest, which are the ones to act on. Past it, "All N in Links". */
export const IMAGES_LISTED = 500;

/** An image file over the analysis's limit for one, after any redirects: what `image-too-large` flags. */
export function isLargeImage(image: Pick<PageImage, 'target'>, largeImageBytes: number): boolean {
  return typeof image.target.sizeBytes === 'number' && image.target.sizeBytes > largeImageBytes;
}

/** The section's line: "14 images, 2 over 200 KB", "1 image", or "No images". */
export function imagesSummaryWords(page: Pick<PageImages, 'total' | 'rows'>, largeImageBytes: number): string {
  if (page.total === 0) return 'No images';
  const large = page.rows.filter((image) => isLargeImage(image, largeImageBytes)).length;
  const all = formatCountOf(page.total, 'image');
  return large > 0 ? `${all}, ${formatCount(large)} over ${formatBytes(largeImageBytes)}` : all;
}

/** How the page shows an image: "Content", "2 times · Content, Footer". */
export function shownWords(image: Pick<PageImage, 'links' | 'positions'>): string {
  const where = image.positions.map((position) => POSITION_WORDS[position]).join(', ');
  const times = image.links > 1 ? `${formatCount(image.links)} times` : '';
  return [times, where].filter((part) => part).join(' · ');
}

/** An image file as one line: "1.8 MB · 4000×3000 px", what of it is known. */
export function imageFileWords(image: Pick<PageImage, 'target'>): string {
  return [
    typeof image.target.sizeBytes === 'number' ? formatBytes(image.target.sizeBytes) : null,
    pixelsWords(image.target.pixelWidth, image.target.pixelHeight),
  ]
    .filter((part): part is string => part !== null)
    .join(' · ');
}

/** Every page that shows an image, in the Links grid's Images view: its image links, from any page but its own. */
export function imageUsesFilters(imageId: number): GridFilter[] {
  return [
    { column: 'to.id', op: 'eq', value: imageId },
    { column: 'selfLink', op: 'is', value: false },
    { column: 'kind', op: 'in', values: ['image'] },
  ];
}
