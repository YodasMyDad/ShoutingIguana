import { describe, expect, it } from 'vitest';
import { TEST_LIMITS, testResponse, testUrlDetail } from '../test/inspectorSidecar';
import {
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
  it('reads a title’s width one over the limit, in px with its characters under it, naming the check it trips', () => {
    const measure = titleMeasure(581, TEST_LIMITS, 61);
    expect(measure).toMatchObject({
      label: 'Title width',
      value: 581,
      min: 200,
      max: 580,
      state: 'over',
      by: 1,
      valueText: '581 px',
      limitText: '200 to 580 px',
      note: '1 over the 580 limit',
      aside: '61 characters',
      checkId: 'title-too-long',
    });
  });

  it('reads a narrow title under the minimum', () => {
    const measure = titleMeasure(150, TEST_LIMITS, 16);
    expect(measure.note).toBe('50 under the 200 minimum');
    expect(measure.checkId).toBe('title-too-short');
  });

  it('says the characters under the width as a reader counts them, wide ones taking their room in the width', () => {
    expect(titleMeasure(612, TEST_LIMITS, 64)).toMatchObject({
      valueText: '612 px',
      note: '32 over the 580 limit',
      aside: '64 characters',
    });
    // 48 Japanese characters, each as wide as an em: the width is over, the characters a reader's count.
    expect(titleMeasure(960, TEST_LIMITS, 48)).toMatchObject({
      state: 'over',
      valueText: '960 px',
      aside: '48 characters',
      note: '380 over the 580 limit',
    });
    expect(titleMeasure(9, TEST_LIMITS, 1).aside).toBe('1 character');
    // Without a count, nothing under it.
    expect(titleMeasure(300, TEST_LIMITS).aside).toBeNull();
  });

  it('writes a value exactly when it rounds to its limit, so it never reads as on it while it’s over', () => {
    // 1,015 ms is "1 s" rounded, and so is the limit.
    expect(responseMeasure(1_015, TEST_LIMITS)).toMatchObject({
      state: 'over',
      valueText: '1,015 ms',
      limitText: 'Limit 1 s',
      note: '15 ms over the 1 s limit',
    });
    expect(responseMeasure(1_412, TEST_LIMITS).valueText).toBe('1,412 ms');
    // Past where it rounds to the limit, and within it, it's rounded as everywhere.
    expect(responseMeasure(2_600, TEST_LIMITS).valueText).toBe('3 s');
    expect(responseMeasure(1_000, TEST_LIMITS).valueText).toBe('1 s');
    expect(responseMeasure(117, TEST_LIMITS).valueText).toBe('117 ms');
    // A page 24 bytes over 1 MB.
    expect(htmlSizeMeasure(1_048_600, TEST_LIMITS)).toMatchObject({
      valueText: '1,048,600 bytes',
      note: '24 bytes over the 1 MB limit',
    });
  });

  it('stays quiet within the range', () => {
    const measure = titleMeasure(450, TEST_LIMITS, 45);
    expect(measure.state).toBe('within');
    expect(measure.note).toBeNull();
    expect(measure.checkId).toBeNull();
  });

  it('reads a description’s width against its own range', () => {
    expect(descriptionMeasure(990, TEST_LIMITS, 158)).toMatchObject({
      label: 'Meta description width',
      valueText: '990 px',
      note: '5 over the 985 limit',
      aside: '158 characters',
      checkId: 'description-too-long',
      limitText: '400 to 985 px',
    });
    expect(descriptionMeasure(200, TEST_LIMITS, 31).checkId).toBe('description-too-short');
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
    const limits = { ...TEST_LIMITS, titleMinPixels: 100, titleMaxPixels: 200, slowResponseMs: 250 };
    expect(titleMeasure(250, limits).note).toBe('50 over the 200 limit');
    expect(responseMeasure(300, limits).note).toBe('50 ms over the 250 ms limit');
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
          { text: 'One', length: 3, width: 33 },
          { text: 'Garden furniture sets for every patio', length: 37, width: 349 },
          // A width the engine didn't give has no meter.
          { text: 'Two', length: 3, width: null },
        ],
      },
    });
    expect(measures.titles.map((measure) => measure?.state ?? null)).toEqual(['under', 'within', null]);
    expect(measures.titles[1]).toMatchObject({ valueText: '349 px', aside: '37 characters' });
    expect(measures.descriptions).toHaveLength(1);
    expect(measures.words?.value).toBe(701);
    expect(measures.response?.value).toBe(117);
    expect(measures.htmlSize?.value).toBe(220_480);
  });

  it('has no measure for what the crawl doesn’t have, never one of 0', () => {
    const notFetched = measuresOf(testUrlDetail({ response: null, content: null }));
    expect(notFetched).toEqual({
      titles: [],
      descriptions: [],
      words: null,
      response: null,
      htmlSize: null,
      imageSize: null,
      url: null,
    });

    const noTimes = measuresOf(testUrlDetail({ response: testResponse({ responseMs: null, bodyBytes: null }) }));
    expect(noTimes.response).toBeNull();
    expect(noTimes.htmlSize).toBeNull();
    expect(noTimes.words).not.toBeNull();
  });

  it('draws a page’s URL length on its limit by the check’s own measure, and flags it past the limit', () => {
    const within = measuresOf(testUrlDetail({ urlLength: 115 })).url;
    expect(within).toMatchObject({ state: 'within', valueText: '115 characters', limitText: 'Limit 115 characters' });
    expect(within?.checkId).toBeNull();
    const over = measuresOf(testUrlDetail({ urlLength: 130 })).url;
    expect(over).toMatchObject({
      state: 'over',
      note: '15 over the 115 limit',
      checkId: 'url-too-long',
      label: 'URL length',
    });
    // Not for what isn't a page (the check judges pages), nor from an engine that doesn't send it.
    expect(measuresOf(testUrlDetail({ urlLength: 130, type: 'image', content: null })).url).toBeNull();
    expect(measuresOf(testUrlDetail({ urlLength: null })).url).toBeNull();
    expect(measuresOf(testUrlDetail()).url).toBeNull();
  });

  it('doesn’t read a file’s size as a page’s', () => {
    const image = measuresOf(
      testUrlDetail({ type: 'image', content: null, response: testResponse({ bodyBytes: 9_000_000 }) }),
    );
    expect(image.htmlSize).toBeNull();
    expect(image.response).not.toBeNull();
  });

  it('draws an image’s size against the limit for images, and nothing else’s', () => {
    const image = (overrides: Parameters<typeof testResponse>[0]) =>
      measuresOf(testUrlDetail({ type: 'image', content: null, response: testResponse(overrides) }));
    expect(image({ sizeBytes: 307_200, bodyBytes: null }).imageSize).toMatchObject({
      label: 'Image size',
      value: 307_200,
      max: 204_800,
      state: 'over',
      valueText: '300 KB',
      limitText: 'Limit 200 KB',
      note: '100 KB over the 200 KB limit',
      checkId: 'image-too-large',
    });
    expect(image({ sizeBytes: 18_000 }).imageSize).toMatchObject({ state: 'within', note: null, checkId: null });
    // One that didn't answer as an image has no size to judge, and a page's size is its HTML's.
    expect(image({ sizeBytes: 300_000, outcome: 'clientError', statusCode: 404 }).imageSize).toBeNull();
    expect(image({ sizeBytes: null }).imageSize).toBeNull();
    expect(measuresOf(testUrlDetail()).imageSize).toBeNull();
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
