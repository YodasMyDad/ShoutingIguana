// Every value on its scale (docs/v2-ui-design.md): a title's width, a description's, the word count, the response
// time, the HTML's size and an image's, each read against the limit the analysis used (`limits` of `/urls/inspect`). The limits
// are the engine's, and so are the comparisons: over is past the maximum, under is short of the minimum, and a value
// on a limit is within it, as the checks judge (`width > titleMaxPixels`, `wordCount < minWords`).
import type { ContentLimits, UrlDetail } from '../api/inspector';
import { formatBytes, formatCount, formatCountOf, formatMilliseconds } from '../lib/format';

export type ScaleState = 'under' | 'within' | 'over';

export interface ScaleRange {
  min?: number | undefined;
  max?: number | undefined;
}

export interface ScaleReading {
  state: ScaleState;
  /** How far outside the range, in the value's own unit; 0 within it. */
  by: number;
}

/** Where a value sits against its range. A value on a limit is within it. */
export function readOnScale(value: number, range: ScaleRange): ScaleReading {
  if (range.max !== undefined && value > range.max) return { state: 'over', by: value - range.max };
  if (range.min !== undefined && value < range.min) return { state: 'under', by: range.min - value };
  return { state: 'within', by: 0 };
}

export type MeasureId = 'title' | 'description' | 'words' | 'response' | 'htmlSize' | 'imageSize' | 'url';

/** A value drawn on its scale, with its words. */
export interface Measure extends ScaleRange, ScaleReading {
  id: MeasureId;
  /** What's measured, for the meter's name: "Title width". */
  label: string;
  value: number;
  /** "612 px", "117 ms". */
  valueText: string;
  /** The limit in a few words: "200 to 580 px", "Limit 1 s", "Minimum 200". */
  limitText: string;
  /** How far out, in words: "32 over the 580 limit", "12 under the 200 minimum". Null within the range. */
  note: string | null;
  /** What else is known of the value, under the meter: a title's characters under its width, "64 characters". */
  aside: string | null;
  /** The check a value out of range trips, to find its severity and title. Null within the range. */
  checkId: string | null;
  /** How the limits read under the scale. */
  formatTick: (value: number) => string;
}

interface MeasureSpec {
  id: MeasureId;
  label: string;
  min?: number | undefined;
  max?: number | undefined;
  /** The value with its unit. */
  format: (value: number) => string;
  /** A bare number in the value's unit, for "32 over the 580 limit". */
  formatAmount: (value: number) => string;
  /** The value to its last unit, where `format` rounds: "1,015 ms". */
  formatExact?: (value: number) => string;
  limitText: string;
  over?: string;
  under?: string;
  aside?: string | null;
}

/**
 * The value in words. One past its limit that rounds to the limit's own words (1,015 ms reads "1 s", as the 1 s limit
 * does) would look as if it sat on the limit while a check says it's over: it's written to its last unit instead.
 */
function valueWords(spec: MeasureSpec, value: number, reading: ScaleReading): string {
  const limit = reading.state === 'over' ? spec.max : reading.state === 'under' ? spec.min : undefined;
  const rounded = spec.format(value);
  return limit !== undefined && spec.formatExact && rounded === spec.format(limit) ? spec.formatExact(value) : rounded;
}

function measure(spec: MeasureSpec, value: number): Measure {
  const reading = readOnScale(value, spec);
  const note =
    reading.state === 'over' && spec.max !== undefined
      ? `${spec.formatAmount(reading.by)} over the ${spec.formatAmount(spec.max)} limit`
      : reading.state === 'under' && spec.min !== undefined
        ? `${spec.formatAmount(reading.by)} under the ${spec.formatAmount(spec.min)} minimum`
        : null;
  return {
    id: spec.id,
    label: spec.label,
    value,
    min: spec.min,
    max: spec.max,
    ...reading,
    valueText: valueWords(spec, value, reading),
    limitText: spec.limitText,
    note,
    aside: spec.aside ?? null,
    checkId: reading.state === 'over' ? (spec.over ?? null) : reading.state === 'under' ? (spec.under ?? null) : null,
    formatTick: spec.formatAmount,
  };
}

const characters = (value: number) => formatCountOf(value, 'character');
const words = (value: number) => formatCountOf(value, 'word');

const pixels = (value: number) => `${formatCount(value)} px`;

/**
 * A title's width in Google's desktop results, in pixels, against the range the analysis used: what the checks judge.
 * `length`: its characters, said under the meter ("64 characters"), as a width alone says little to a reader.
 */
export function titleMeasure(width: number, limits: ContentLimits, length?: number): Measure {
  return measure(
    {
      id: 'title',
      label: 'Title width',
      min: limits.titleMinPixels,
      max: limits.titleMaxPixels,
      format: pixels,
      formatAmount: formatCount,
      limitText: `${formatCount(limits.titleMinPixels)} to ${formatCount(limits.titleMaxPixels)} px`,
      over: 'title-too-long',
      under: 'title-too-short',
      aside: length === undefined ? null : characters(length),
    },
    width,
  );
}

/** A meta description's width against the range the analysis used. `length`: as for a title. */
export function descriptionMeasure(width: number, limits: ContentLimits, length?: number): Measure {
  return measure(
    {
      id: 'description',
      label: 'Meta description width',
      min: limits.descriptionMinPixels,
      max: limits.descriptionMaxPixels,
      format: pixels,
      formatAmount: formatCount,
      limitText: `${formatCount(limits.descriptionMinPixels)} to ${formatCount(limits.descriptionMaxPixels)} px`,
      over: 'description-too-long',
      under: 'description-too-short',
      aside: length === undefined ? null : characters(length),
    },
    width,
  );
}

/** The word count against the analysis's minimum. */
export function wordsMeasure(wordCount: number, limits: ContentLimits): Measure {
  return measure(
    {
      id: 'words',
      label: 'Word count',
      min: limits.minWords,
      format: words,
      formatAmount: formatCount,
      limitText: `Minimum ${formatCount(limits.minWords)}`,
      under: 'low-word-count',
    },
    wordCount,
  );
}

/** The response time against the analysis's limit. */
export function responseMeasure(responseMs: number, limits: ContentLimits): Measure {
  return measure(
    {
      id: 'response',
      label: 'Response time',
      max: limits.slowResponseMs,
      format: formatMilliseconds,
      formatAmount: formatMilliseconds,
      formatExact: (value) => `${formatCount(Math.round(value))} ms`,
      limitText: `Limit ${formatMilliseconds(limits.slowResponseMs)}`,
      over: 'slow-response',
    },
    responseMs,
  );
}

/** The HTML's size, unpacked, against the analysis's limit. */
export function htmlSizeMeasure(bodyBytes: number, limits: ContentLimits): Measure {
  return measure(
    {
      id: 'htmlSize',
      label: 'HTML size',
      max: limits.largePageBytes,
      format: formatBytes,
      formatAmount: formatBytes,
      formatExact: (value) => formatCountOf(value, 'byte'),
      limitText: `Limit ${formatBytes(limits.largePageBytes)}`,
      over: 'large-page',
    },
    bodyBytes,
  );
}

/**
 * An image's size as it was sent, against the analysis's limit: what `image-too-large` goes by. The check's issues
 * are on the pages that use the image, so it's those that name it (`namedBy`), not the image's own issues.
 */
export function imageSizeMeasure(sizeBytes: number, limits: ContentLimits): Measure {
  return measure(
    {
      id: 'imageSize',
      label: 'Image size',
      max: limits.largeImageBytes,
      format: formatBytes,
      formatAmount: formatBytes,
      formatExact: (value) => formatCountOf(value, 'byte'),
      limitText: `Limit ${formatBytes(limits.largeImageBytes)}`,
      over: 'image-too-large',
    },
    sizeBytes,
  );
}

/**
 * A page's URL length against the analysis's limit, by the check's own measure (E3c's `urlLength`: the URL as people
 * read it, escapes decoded and the host in its own script, in characters as a reader counts them).
 */
export function urlLengthMeasure(length: number, limits: ContentLimits): Measure {
  return measure(
    {
      id: 'url',
      label: 'URL length',
      max: limits.maxUrlLength,
      format: characters,
      formatAmount: formatCount,
      limitText: `Limit ${characters(limits.maxUrlLength)}`,
      over: 'url-too-long',
    },
    length,
  );
}

export interface UrlMeasures {
  /** One per title on the page, in page order: null for one the engine gave no width. */
  titles: (Measure | null)[];
  descriptions: (Measure | null)[];
  words: Measure | null;
  response: Measure | null;
  htmlSize: Measure | null;
  /** For an image that answered: its size against the limit for images. */
  imageSize: Measure | null;
  /** A page's URL length, as the check measures it; null from an engine that doesn't send it (before E3c). */
  url: Measure | null;
}

/** Every value of a URL that has a limit. A value the crawl doesn't have has no measure, never one of 0. */
export function measuresOf(
  detail: Pick<UrlDetail, 'type' | 'content' | 'response' | 'limits'> & Partial<Pick<UrlDetail, 'urlLength'>>,
): UrlMeasures {
  const { content, response, limits } = detail;
  const number = (value: number | null | undefined) =>
    value !== null && value !== undefined && Number.isFinite(value) ? value : null;
  const responseMs = number(response?.responseMs);
  const bodyBytes = number(response?.bodyBytes);
  const sizeBytes = number(response?.sizeBytes);
  const image = detail.type === 'image' && response?.outcome === 'success';
  return {
    // A width the engine didn't give has no meter, never one of 0.
    titles: (content?.titles ?? []).map((title) =>
      typeof title.width === 'number' ? titleMeasure(title.width, limits, title.length) : null,
    ),
    descriptions: (content?.descriptions ?? []).map((description) =>
      typeof description.width === 'number' ? descriptionMeasure(description.width, limits, description.length) : null,
    ),
    words: content ? wordsMeasure(content.wordCount, limits) : null,
    response: responseMs === null ? null : responseMeasure(responseMs, limits),
    htmlSize: bodyBytes === null || content === null ? null : htmlSizeMeasure(bodyBytes, limits),
    imageSize: image && sizeBytes !== null ? imageSizeMeasure(sizeBytes, limits) : null,
    // The check judges pages: a URL's length is drawn on its limit where it's a page's.
    url: content && number(detail.urlLength) !== null ? urlLengthMeasure(number(detail.urlLength) ?? 0, limits) : null,
  };
}

/** Where a scale ends: a little past the further of the value and its limit, so neither sits on the end. */
export function scaleEnd(measured: Pick<Measure, 'value' | 'min' | 'max'>): number {
  const furthest = Math.max(measured.value, measured.max ?? measured.min ?? 0);
  return Math.max(1, Math.ceil(furthest * 1.25));
}
