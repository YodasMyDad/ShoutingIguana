import { describe, expect, it } from 'vitest';
import { GIVE_WAY_STEPS, measureGiveWay } from './useGiveWay';

/**
 * A bar whose parts take the widths given, less what each step frees: jsdom lays nothing out, so the bar's
 * scrollWidth is worked out from the steps marked on it, as the browser would.
 */
function bar(initialWidth: number, full: number, freed: readonly number[]) {
  const element = document.createElement('header') as HTMLElement & { width: number };
  element.width = initialWidth;
  const step = () => Number(element.dataset.give ?? 0);
  Object.defineProperty(element, 'clientWidth', { get: () => element.width });
  Object.defineProperty(element, 'scrollWidth', {
    get: () => {
      const marked = GIVE_WAY_STEPS.filter((_, index) => element.hasAttribute(`data-give-${index + 1}`)).length;
      expect(marked).toBe(step());
      return full - freed.slice(0, marked).reduce((sum, value) => sum + value, 0);
    },
  });
  return element;
}

describe('the top bar’s order of giving way', () => {
  // What each step frees, in px: the keys, the date, search's words, Home, the count, the controls' words, the site
  // shortened, the site, Forward.
  const FREED = [40, 140, 190, 60, 70, 90, 100, 130, 32];
  const FULL = 1200;

  it('takes no step when everything fits', () => {
    expect(measureGiveWay(bar(1200, FULL, FREED))).toBe(0);
  });

  it('takes the fewest steps that let everything fit, in order', () => {
    expect(measureGiveWay(bar(1160, FULL, FREED))).toBe(1);
    expect(measureGiveWay(bar(1159, FULL, FREED))).toBe(2);
    expect(measureGiveWay(bar(830, FULL, FREED))).toBe(3);
    expect(measureGiveWay(bar(584, FULL, FREED))).toBe(7);
  });

  it('gives way again as the bar narrows, and gets its parts back as it widens', () => {
    const element = bar(1000, FULL, FREED);
    expect(measureGiveWay(element)).toBe(3);
    element.width = 700;
    expect(measureGiveWay(element)).toBe(5);
    element.width = 1300;
    expect(measureGiveWay(element)).toBe(0);
    expect(element.hasAttribute('data-give-1')).toBe(false);
  });

  it('stops at the last step, however narrow: what never gives way stays', () => {
    expect(measureGiveWay(bar(100, FULL, FREED))).toBe(GIVE_WAY_STEPS.length);
  });
});
