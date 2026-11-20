// The widths of a check's groups' columns (A-02): the figures keep theirs, and the words (the group's detail, or the
// URL it's of) get what they leave, every column counted: the comparison's two among them, which were left out, so the
// tree drew 170 px wider than its frame on a compared crawl. Pure, so "never wider than the frame" is a unit test.

export const PAGES_WIDTH = 88;
export const SHARE_WIDTH = 176;
/** A group's file size, for a check of oversized files: "1.8 MB". */
export const SIZE_WIDTH = 88;
/** An image's own pixels, "4000×3000", and the size its pages show it at, "400×300 (10×)": figures, never cut. */
export const PIXELS_WIDTH = 112;
export const SHOWN_AT_WIDTH = 124;
/** Beside an image's figures, the count and the share bar take less, so the image's address keeps some room. */
export const IMAGE_PAGES_WIDTH = 68;
export const IMAGE_SHARE_WIDTH = 120;
/** The fix loop: the group's URLs then, and how it moved. */
export const THEN_WIDTH = 88;
export const MOVE_WIDTH = 104;
/**
 * A grid narrower than this leaves out the share bar: the count beside it says as much, and the words and the figures
 * need the room (a 1280 px window at 150%, or any window with the inspector open beside a compared crawl).
 */
export const SHARE_FROM = 760;
export const MIN_TEXT_WIDTH = 200;
/** The frame's edges and the room a scrollbar may take. */
export const FRAME_SLACK = 24;

export interface GroupColumnsAsked {
  /** The frame's width, px. */
  width: number;
  /** The groups are images: a thumbnail, and narrower figures. */
  images: boolean;
  /** The share bar says something (more than one group, of more than one page). */
  share: boolean;
  size: boolean;
  pixels: boolean;
  shownAt: boolean;
  /** The crawl is compared with another: "17 Nov" and "Change". */
  compare: boolean;
}

export interface GroupColumnWidths {
  words: number;
  pages: number;
  share: number;
  size: number;
  pixels: number;
  shownAt: number;
  then: number;
  move: number;
}

/** Each column's width; 0 for one that isn't shown. The share bar gives way first where the frame is narrow. */
export function groupColumnWidths(asked: GroupColumnsAsked): GroupColumnWidths {
  const pages = asked.images ? IMAGE_PAGES_WIDTH : PAGES_WIDTH;
  const size = asked.size ? SIZE_WIDTH : 0;
  const pixels = asked.pixels ? PIXELS_WIDTH : 0;
  const shownAt = asked.shownAt ? SHOWN_AT_WIDTH : 0;
  const then = asked.compare ? THEN_WIDTH : 0;
  const move = asked.compare ? MOVE_WIDTH : 0;
  const least = asked.images ? MIN_TEXT_WIDTH - 60 : MIN_TEXT_WIDTH;
  const figures = pages + size + pixels + shownAt + then + move + FRAME_SLACK;
  const wanted = !asked.share ? 0 : asked.images ? IMAGE_SHARE_WIDTH : SHARE_WIDTH;
  // The bar only where the words keep their least beside it, and never in a narrow frame.
  const share = wanted > 0 && asked.width >= SHARE_FROM && asked.width - figures - wanted >= least ? wanted : 0;
  const words = Math.max(least, asked.width - figures - share);
  return { words, pages, share, size, pixels, shownAt, then, move };
}

/** What the columns take in all, with the frame's slack: never more than the frame while the words have their least. */
export function groupColumnsTotal(widths: GroupColumnWidths): number {
  return (
    widths.words +
    widths.pages +
    widths.share +
    widths.size +
    widths.pixels +
    widths.shownAt +
    widths.then +
    widths.move +
    FRAME_SLACK
  );
}
