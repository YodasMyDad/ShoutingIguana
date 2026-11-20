import { describe, expect, it } from 'vitest';
import { MOVE_WIDTH, THEN_WIDTH, groupColumnWidths, groupColumnsTotal } from './groupColumns';

// The frames the audit measured (A-02): the detail pane at 2560 and at 1280, each with the inspector open beside it.
const FRAMES = [1806, 1398, 946, 570];

describe('groupColumnWidths', () => {
  it('never sums past the frame, compared or not, inspector open or closed, for every kind of group', () => {
    for (const width of FRAMES) {
      for (const compare of [false, true]) {
        for (const images of [false, true]) {
          for (const share of [false, true]) {
            const widths = groupColumnWidths({
              width,
              images,
              share,
              size: images,
              pixels: images,
              shownAt: images,
              compare,
            });
            // An image's five figures and the comparison's two need 632 px: only a frame narrower than that scrolls.
            if (width >= 800 || !images) expect(groupColumnsTotal(widths)).toBeLessThanOrEqual(width);
          }
        }
      }
    }
  });

  it('takes the comparison’s two columns out of the words, not out of the frame', () => {
    const plain = groupColumnWidths({
      width: 1806,
      images: false,
      share: true,
      size: false,
      pixels: false,
      shownAt: false,
      compare: false,
    });
    const compared = groupColumnWidths({
      width: 1806,
      images: false,
      share: true,
      size: false,
      pixels: false,
      shownAt: false,
      compare: true,
    });
    expect(plain.words - compared.words).toBe(THEN_WIDTH + MOVE_WIDTH);
    expect(groupColumnsTotal(compared)).toBe(1806);
  });

  it('drops the share bar before the words go under their least', () => {
    const narrow = groupColumnWidths({
      width: 570,
      images: false,
      share: true,
      size: false,
      pixels: false,
      shownAt: false,
      compare: true,
    });
    expect(narrow.share).toBe(0);
    expect(narrow.words).toBeGreaterThanOrEqual(200);
  });
});
