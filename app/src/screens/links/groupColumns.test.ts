import { describe, expect, it } from 'vitest';
import type { LinkGroup } from '../../api/grids';
import { GROUP_LEAST, MEASURED_GROUPS, fittedGroupWidths, groupColumnNeeds } from './groupColumns';

const SHOWN = ['target', 'status', 'pages', 'links', 'text', 'where'];
const WIDTHS = { status: 128, pages: 112, links: 96, text: 216, where: 256 };
const FIXED = 128 + 112 + 96;
const total = (widths: Record<string, number>) => Object.values(widths).reduce((sum, width) => sum + width, 0);

describe('the grouped Links grid’s column widths', () => {
  it('give a target’s address what it needs while the anchor text and the places can give it room', () => {
    // As gallery 6.1 had it at 1440 px: the address wanted 380, the old target column got the 308 the rest left.
    const widths = fittedGroupWidths(SHOWN, WIDTHS, 1116, { target: 380, text: 260, where: 380 });
    expect(widths.target).toBe(380);
    expect(widths.text).toBeGreaterThanOrEqual(GROUP_LEAST.text);
    expect(widths.where).toBeGreaterThanOrEqual(GROUP_LEAST.where);
    // The places want more past their least than the text does, so they get more of what's left.
    expect(widths.where).toBeGreaterThan(widths.text!);
    expect(total(widths)).toBeLessThanOrEqual(1116);
    expect(total(widths)).toBeGreaterThan(1116 - 3);
    // The counts and the status keep their own widths.
    expect([widths.status, widths.pages, widths.links]).toEqual([128, 112, 96]);
  });

  it('give each what it needs with room for all, and the rest to the target', () => {
    const widths = fittedGroupWidths(SHOWN, WIDTHS, 1400, { target: 300, text: 200, where: 260 });
    expect([widths.text, widths.where]).toEqual([200, 260]);
    expect(widths.target).toBe(1400 - FIXED - 200 - 260);
  });

  it('cut the address only past the others’ least, and give each its least with less room still', () => {
    const short = fittedGroupWidths(SHOWN, WIDTHS, FIXED + 600, { target: 500, text: 260, where: 380 });
    expect(short).toMatchObject({ target: 600 - GROUP_LEAST.text - GROUP_LEAST.where, text: 140, where: 152 });
    const narrow = fittedGroupWidths(SHOWN, WIDTHS, FIXED + 300, { target: 500, text: 260, where: 380 });
    expect(narrow).toMatchObject({ target: 220, text: 140, where: 152 });
  });

  it('want the default widths for columns with nothing in view', () => {
    const widths = fittedGroupWidths(SHOWN, WIDTHS, 1116, {});
    expect([widths.text, widths.where]).toEqual([216, 256]);
    expect(widths.target).toBe(1116 - FIXED - 216 - 256);
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
