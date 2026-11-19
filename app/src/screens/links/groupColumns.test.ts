import { describe, expect, it } from 'vitest';
import type { LinkGroup } from '../../api/grids';
import { GROUP_LEAST, MEASURED_GROUPS, fittedGroupWidths, groupColumnNeeds } from './groupColumns';

const SHOWN = ['target', 'status', 'pages', 'links', 'text', 'where'];
const WIDTHS = { status: 128, pages: 280, links: 80, text: 216, where: 256 };
const FIXED = 128 + 80;
const total = (widths: Record<string, number>) => Object.values(widths).reduce((sum, width) => sum + width, 0);

describe('the grouped Links grid’s column widths', () => {
  it('give a target’s address and the page that links to it what they need while the text and the places can give room', () => {
    const widths = fittedGroupWidths(SHOWN, WIDTHS, 1316, { target: 380, pages: 300, text: 260, where: 380 });
    // Neither address is cut in the middle while the grid has room for it.
    expect(widths.target).toBe(380);
    expect(widths.pages).toBe(300);
    expect(widths.text).toBeGreaterThanOrEqual(GROUP_LEAST.text);
    expect(widths.where).toBeGreaterThanOrEqual(GROUP_LEAST.where);
    // The places want more past their least than the text does, so they get more of what's left.
    expect(widths.where).toBeGreaterThan(widths.text!);
    expect(total(widths)).toBeLessThanOrEqual(1316);
    expect(total(widths)).toBeGreaterThan(1316 - 3);
    // The count and the status keep their own widths.
    expect([widths.status, widths.links]).toEqual([128, 80]);
  });

  // The UX round's D-03 and rule 8. It used to be "the rest to the target": the page that holds the link then sat
  // 1,300 px from its target in a wide window, across blank space.
  it('give each what it needs with room for all, and the rest to the last column: each starts where the one before ends', () => {
    const widths = fittedGroupWidths(SHOWN, WIDTHS, 1800, { target: 300, pages: 250, text: 200, where: 260 });
    expect([widths.target, widths.pages, widths.text]).toEqual([300, 250, 200]);
    expect(widths.where).toBe(1800 - FIXED - 300 - 250 - 200);
  });

  it('cut the addresses only past the others’ least, each by what it wants, and give each its least with less room still', () => {
    const short = fittedGroupWidths(SHOWN, WIDTHS, FIXED + 800, { target: 500, pages: 400, text: 260, where: 380 });
    // 88 px past everyone's least, shared between the two addresses by what each wants past its own.
    expect(short).toMatchObject({ target: 271, pages: 236, text: 140, where: 152 });
    const narrow = fittedGroupWidths(SHOWN, WIDTHS, FIXED + 300, { target: 500, pages: 400, text: 260, where: 380 });
    expect(narrow).toMatchObject({ target: 220, pages: 200, text: 140, where: 152 });
  });

  it('give a column no more than its values take, short of room: the addresses have what it leaves', () => {
    // The garden crawl's broken links at 1280 px: every link sits in "Content", which doesn't take the 152 px it had.
    const widths = fittedGroupWidths(SHOWN, WIDTHS, FIXED + 764, { target: 600, pages: 393, text: 274, where: 79 });
    expect(widths.where).toBe(88);
    expect(widths.text).toBe(140);
    expect(widths.target! + widths.pages!).toBeGreaterThanOrEqual(764 - 140 - 88 - 2);
    expect(widths.target).toBeGreaterThan(GROUP_LEAST.target);
    expect(widths.pages).toBeGreaterThan(GROUP_LEAST.pages);
  });

  it('want the default widths for columns with nothing in view', () => {
    const widths = fittedGroupWidths(SHOWN, { ...WIDTHS, target: 420 }, 1516, {});
    expect([widths.target, widths.pages, widths.text]).toEqual([420, 280, 216]);
    expect(widths.where).toBe(1516 - FIXED - 420 - 280 - 216);
  });
});

describe('what the grouped grid’s columns need', () => {
  const group = (over: Partial<LinkGroup>): LinkGroup =>
    ({
      id: 1,
      url: 'https://example.com/a',
      pages: 1,
      links: 1,
      text: 'Home',
      texts: 1,
      positions: ['content'],
      likelyTemplate: false,
      target: {},
      ...over,
    }) as unknown as LinkGroup;
  // 10 px a character, whatever the face.
  const measure = (text: string) => text.length * 10;

  it('measure an address as it shows, with the toggle and the open button beside it, and the widest of each', () => {
    const needs = groupColumnNeeds(
      [group({ url: 'https://example.com/metal-patio-sets/' }), group({ url: 'https://other.example/x' })],
      false,
      'https://example.com',
      measure,
    );
    // "https://other.example/x" (23) is shown whole, off the crawl's own origin; "/metal-patio-sets/" (18) as its path.
    expect(needs.target).toBe(230 + 24 + 34 + 26);
    expect(needs.text).toBe(40 + 24);
    expect(needs.where).toBeGreaterThan(24);
    // The pages that link to them aren't read yet: that column wants its default.
    expect(needs.pages).toBeUndefined();
  });

  it('measure the page that links to a target as it shows, with “+3 more” and the open button beside it', () => {
    const pages = new Map([
      [1, 'https://example.com/blog/'],
      [2, 'https://example.com/a-longer-page/'],
    ]);
    const needs = groupColumnNeeds(
      [group({ id: 1, pages: 4 }), group({ id: 2 }), group({ id: 3 })],
      false,
      'https://example.com',
      measure,
      (target) => pages.get(target.id),
    );
    // "/a-longer-page/" (15) is the widest; "/blog/" with "+3 more" is 60 + 8 + 70.
    expect(needs.pages).toBe(150 + 24 + 26);
    expect(
      groupColumnNeeds([group({ id: 1, pages: 4 })], false, 'https://example.com', measure, (target) =>
        pages.get(target.id),
      ).pages,
    ).toBe(60 + 8 + 70 + 24 + 26);
  });

  it('measure the groups a window shows of the view’s first, not one long address further down', () => {
    const shortOnes = Array.from({ length: MEASURED_GROUPS }, (_, index) =>
      group({ id: index, url: `https://example.com/${index}` }),
    );
    const long = group({ id: 99, url: `https://example.com/${'x'.repeat(80)}` });
    expect(groupColumnNeeds([...shortOnes, long], false, 'https://example.com', measure).target).toBe(
      30 + 24 + 34 + 26,
    );
  });
});
