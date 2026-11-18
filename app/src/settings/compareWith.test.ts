import { afterEach, describe, expect, it } from 'vitest';
import { compareChoiceOf, setCompareChoice, withCompareChoice } from './compareWith';
import { DEFAULT_SETTINGS, readSettings, type Settings } from './schema';
import { getSettings, updateSettings } from './settingsStore';

const crawl = '0123456789abcdef';
const other = 'fedcba9876543210';
const third = 'aaaaaaaaaaaaaaaa';
const settings = (patch: Partial<Settings>): Settings => ({ ...DEFAULT_SETTINGS, ...patch });

afterEach(() => updateSettings({ compareWithByCrawl: {} }));

describe('the crawl a crawl is compared with, kept per crawl (Compare with…)', () => {
  it('is its previous crawl until another is picked, or none', () => {
    expect(compareChoiceOf(settings({}), crawl)).toBe('previous');
    expect(compareChoiceOf(settings({ compareWithByCrawl: { [crawl]: other } }), crawl)).toBe(other);
    expect(compareChoiceOf(settings({ compareWithByCrawl: { [crawl]: 'none' } }), crawl)).toBe('none');
    expect(compareChoiceOf(settings({ compareWithByCrawl: { [crawl]: other } }), third)).toBe('previous');
  });

  it('keeps a pick or none, drops the entry for the previous crawl, the crawl changed last going last', () => {
    let state = settings({ compareWithByCrawl: { [crawl]: other, [third]: 'none' } });
    state = { ...state, ...withCompareChoice(state, crawl, 'none') };
    expect(Object.entries(state.compareWithByCrawl)).toEqual([
      [third, 'none'],
      [crawl, 'none'],
    ]);
    state = { ...state, ...withCompareChoice(state, crawl, 'previous') };
    expect(state.compareWithByCrawl).toEqual({ [third]: 'none' });
  });

  it('never keeps a crawl compared with itself', () => {
    const state = settings({});
    expect(withCompareChoice(state, crawl, crawl).compareWithByCrawl).toEqual({});
  });

  it('is saved in the app’s preferences', () => {
    setCompareChoice(crawl, other);
    expect(getSettings().compareWithByCrawl).toEqual({ [crawl]: other });
    setCompareChoice(crawl, 'previous');
    expect(getSettings().compareWithByCrawl).toEqual({});
  });

  it('reads back only crawls’ ids, each with a crawl’s id or none', () => {
    expect(
      readSettings({
        compareWithByCrawl: {
          [crawl]: other,
          [other]: 'none',
          [third]: 'Bad',
          nope: crawl,
          ['bbbbbbbbbbbbbbbb']: 'bbbbbbbbbbbbbbbb',
        },
      }).compareWithByCrawl,
    ).toEqual({ [crawl]: other, [other]: 'none' });
    expect(readSettings({ compareWithByCrawl: [] }).compareWithByCrawl).toEqual({});
    expect(readSettings({}).compareWithByCrawl).toEqual({});
  });

  it('keeps the 100 crawls changed last', () => {
    const many = Object.fromEntries(
      Array.from({ length: 120 }, (_, index) => [index.toString(16).padStart(16, '0'), 'none']),
    );
    const kept = Object.keys(readSettings({ compareWithByCrawl: many }).compareWithByCrawl);
    expect(kept).toHaveLength(100);
    expect(kept[0]).toBe((20).toString(16).padStart(16, '0'));
  });
});
