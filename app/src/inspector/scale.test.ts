import { describe, expect, it } from 'vitest';
import { TEST_LIMITS, testResponse, testUrlDetail } from '../test/inspectorSidecar';
import {
  characterCount,
  descriptionMeasure,
  htmlSizeMeasure,
  measuresOf,
  readOnScale,
  responseMeasure,
  scaleEnd,
  titleMeasure,
  wordsMeasure,
} from './scale';

describe('readOnScale', () => {
  it('reads a value on a limit as within the range, as the checks judge', () => {
    expect(readOnScale(60, { min: 30, max: 60 })).toEqual({ state: 'within', by: 0 });
    expect(readOnScale(30, { min: 30, max: 60 })).toEqual({ state: 'within', by: 0 });
  });

  it('says how far over or under', () => {
    expect(readOnScale(61, { min: 30, max: 60 })).toEqual({ state: 'over', by: 1 });
    expect(readOnScale(12, { min: 30, max: 60 })).toEqual({ state: 'under', by: 18 });
  });

  it('has no upper limit with only a minimum, and no lower with only a maximum', () => {
    expect(readOnScale(10_000, { min: 200 }).state).toBe('within');
    expect(readOnScale(0, { max: 1_000 }).state).toBe('within');
    expect(readOnScale(5, {}).state).toBe('within');
  });
});

describe('the measures', () => {
  it('reads a title one over the limit, naming the check it trips', () => {
    const measure = titleMeasure(61, TEST_LIMITS);
    expect(measure).toMatchObject({
      label: 'Title length',
      value: 61,
      min: 30,
      max: 60,
      state: 'over',
      by: 1,
      valueText: '61 characters',
      limitText: '30 to 60 characters',
      note: '1 over the 60 limit',
      checkId: 'title-too-long',
    });
  });

  it('reads a short title under the minimum', () => {
    const measure = titleMeasure(12, TEST_LIMITS);
    expect(measure.note).toBe('18 under the 30 minimum');
    expect(measure.checkId).toBe('title-too-short');
  });

  it('calls a length characters only when it is the text’s count of them: wide characters count twice', () => {
    // 48 Japanese characters: the engine's length is 96, the room they take in a search result.
    const wide = '日本語のタイトルです'.repeat(5).slice(0, 48);
    const measure = titleMeasure(96, TEST_LIMITS, wide);
    expect(measure.valueText).toBe('48 characters (96 counting full-width characters as two)');
    // The limit and how far past it are in the same measure as the value.
    expect(measure).toMatchObject({ state: 'over', note: '36 over the 60 limit', checkId: 'title-too-long' });
    expect(descriptionMeasure(40, TEST_LIMITS, '短い説明文です。'.repeat(3).slice(0, 20)).valueText).toBe(
      '20 characters (40 counting full-width characters as two)',
    );
    // An emoji is one character to a reader, and wide.
    expect(titleMeasure(8, TEST_LIMITS, 'Sale 🎉!').valueText).toBe(
      '7 characters (8 counting full-width characters as two)',
    );
    // Latin text, accents and all, is its count: "characters" as before. The ends don't count.
    expect(titleMeasure(4, TEST_LIMITS, ' Café ').valueText).toBe('4 characters');
    expect(titleMeasure(61, TEST_LIMITS, 'x'.repeat(61)).valueText).toBe('61 characters');
  });

  it('stays quiet within the range', () => {
    const measure = titleMeasure(45, TEST_LIMITS);
    expect(measure.state).toBe('within');
    expect(measure.note).toBeNull();
    expect(measure.checkId).toBeNull();
  });

  it('says one character in the singular', () => {
    expect(titleMeasure(1, TEST_LIMITS).valueText).toBe('1 character');
  });

  it('reads a description against its own range', () => {
    expect(descriptionMeasure(158, TEST_LIMITS)).toMatchObject({
      note: '3 over the 155 limit',
      checkId: 'description-too-long',
      limitText: '70 to 155 characters',
    });
    expect(descriptionMeasure(20, TEST_LIMITS).checkId).toBe('description-too-short');
  });

  it('reads the word count against its minimum only', () => {
    expect(wordsMeasure(701, TEST_LIMITS)).toMatchObject({ state: 'within', limitText: 'Minimum 200', max: undefined });
    expect(wordsMeasure(120, TEST_LIMITS)).toMatchObject({
      state: 'under',
      note: '80 under the 200 minimum',
      checkId: 'low-word-count',
      valueText: '120 words',
    });
  });

  it('reads the response time against its limit, with units', () => {
    expect(responseMeasure(117, TEST_LIMITS)).toMatchObject({
      state: 'within',
      valueText: '117 ms',
      limitText: 'Limit 1 s',
    });
    expect(responseMeasure(1_412, TEST_LIMITS)).toMatchObject({
      state: 'over',
      note: '412 ms over the 1 s limit',
      checkId: 'slow-response',
    });
    expect(responseMeasure(1_000, TEST_LIMITS).state).toBe('within');
  });

  it('reads the HTML’s size against its limit', () => {
    expect(htmlSizeMeasure(220_480, TEST_LIMITS)).toMatchObject({
      state: 'within',
      valueText: '215.3 KB',
      limitText: 'Limit 1 MB',
    });
    expect(htmlSizeMeasure(1_572_864, TEST_LIMITS)).toMatchObject({
      state: 'over',
      note: '512 KB over the 1 MB limit',
      checkId: 'large-page',
    });
  });

  it('uses the limits it’s given, never its own', () => {
    const limits = { ...TEST_LIMITS, titleMin: 10, titleMax: 20, slowResponseMs: 250 };
    expect(titleMeasure(25, limits).note).toBe('5 over the 20 limit');
    expect(responseMeasure(300, limits).note).toBe('50 ms over the 250 ms limit');
  });
});

describe('characterCount', () => {
  it('counts what a reader counts: a letter with its accent, an emoji with its parts, once', () => {
    expect(characterCount('Garden')).toBe(6);
    expect(characterCount('  Garden  ')).toBe(6);
    expect(characterCount('Café')).toBe(4);
    expect(characterCount('👩‍👩‍👧 family')).toBe(8);
    expect(characterCount('日本語')).toBe(3);
  });
});

describe('measuresOf', () => {
  it('measures every title and description on the page', () => {
    const detail = testUrlDetail();
    const content = detail.content;
    if (!content) throw new Error('the test page has content');
    const measures = measuresOf({
      ...detail,
      content: {
        ...content,
        titles: [
          { text: 'One', length: 3 },
          { text: 'Two', length: 45 },
        ],
      },
    });
    expect(measures.titles.map((measure) => measure.state)).toEqual(['under', 'within']);
    expect(measures.descriptions).toHaveLength(1);
    expect(measures.words?.value).toBe(701);
    expect(measures.response?.value).toBe(117);
    expect(measures.htmlSize?.value).toBe(220_480);
  });

  it('has no measure for what the crawl doesn’t have, never one of 0', () => {
    const notFetched = measuresOf(testUrlDetail({ response: null, content: null }));
    expect(notFetched).toEqual({ titles: [], descriptions: [], words: null, response: null, htmlSize: null });

    const noTimes = measuresOf(testUrlDetail({ response: testResponse({ responseMs: null, bodyBytes: null }) }));
    expect(noTimes.response).toBeNull();
    expect(noTimes.htmlSize).toBeNull();
    expect(noTimes.words).not.toBeNull();
  });

  it('doesn’t read a file’s size as a page’s', () => {
    const image = measuresOf(
      testUrlDetail({ type: 'image', content: null, response: testResponse({ bodyBytes: 9_000_000 }) }),
    );
    expect(image.htmlSize).toBeNull();
    expect(image.response).not.toBeNull();
  });
});

describe('scaleEnd', () => {
  it('ends a little past the further of the value and its limit', () => {
    expect(scaleEnd({ value: 61, min: 30, max: 60 })).toBe(77);
    expect(scaleEnd({ value: 10, min: 30, max: 60 })).toBe(75);
    expect(scaleEnd({ value: 5_000, max: 1_000 })).toBe(6_250);
    expect(scaleEnd({ value: 120, min: 200 })).toBe(250);
    expect(scaleEnd({ value: 0 })).toBe(1);
  });
});
