// Every value on its scale (docs/v2-ui-design.md): a title's length, a description's, the word count, the response
// time and the HTML's size, each read against the limit the analysis used (`limits` of `/urls/inspect`). The limits
// are the engine's, and so are the comparisons: over is past the maximum, under is short of the minimum, and a value
// on a limit is within it, as the checks judge (`length > titleMax`, `wordCount < minWords`).
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

export type MeasureId = 'title' | 'description' | 'words' | 'response' | 'htmlSize';

/** A value drawn on its scale, with its words. */
export interface Measure extends ScaleRange, ScaleReading {
  id: MeasureId;
  /** What's measured, for the meter's name: "Title length". */
  label: string;
  value: number;
  /** "61 characters", "117 ms". */
  valueText: string;
  /** The limit in a few words: "30 to 60 characters", "Limit 1 s", "Minimum 200". */
  limitText: string;
  /** How far out, in words: "1 over the 60 limit", "12 under the 30 minimum". Null within the range. */
  note: string | null;
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
  /** A bare number in the value's unit, for "1 over the 60 limit". */
  formatAmount: (value: number) => string;
  /** The value to its last unit, where `format` rounds: "1,015 ms". */
  formatExact?: (value: number) => string;
  limitText: string;
  over?: string;
  under?: string;
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
    checkId: reading.state === 'over' ? (spec.over ?? null) : reading.state === 'under' ? (spec.under ?? null) : null,
    formatTick: spec.formatAmount,
  };
}

const characters = (value: number) => formatCountOf(value, 'character');
const words = (value: number) => formatCountOf(value, 'word');

/**
 * The characters a reader counts in a text, as the engine counts them: an emoji or an accented letter once, the
 * ends trimmed.
 */
export function characterCount(text: string): number {
  const trimmed = text.trim();
  if (typeof Intl.Segmenter !== 'function') return Array.from(trimmed).length;
  let count = 0;
  for (const _ of new Intl.Segmenter(undefined, { granularity: 'grapheme' }).segment(trimmed)) count++;
  return count;
}

/**
 * A title's or description's length as it reads. The engine's `length` is the room the text takes in a search
 * result: a full-width character (Chinese, Japanese, Korean, an emoji) counts as two. So it's "characters" only when
 * it is the text's count of them; else it's said as the engine's own issue details say it: "48 characters (96
 * counting full-width characters as two)". The limits, and how far out the value is, are in that same measure.
 */
function lengthWords(length: number, text: string | undefined): string {
  const count = text === undefined ? length : characterCount(text);
  return length > count
    ? `${characters(count)} (${formatCount(length)} counting full-width characters as two)`
    : characters(length);
}

/** A title's length against the range the analysis used. `text`: the title itself, for what its length counts. */
export function titleMeasure(length: number, limits: ContentLimits, text?: string): Measure {
  return measure(
    {
      id: 'title',
      label: 'Title length',
      min: limits.titleMin,
      max: limits.titleMax,
      format: (value) => lengthWords(value, text),
      formatAmount: formatCount,
      limitText: `${formatCount(limits.titleMin)} to ${formatCount(limits.titleMax)} characters`,
      over: 'title-too-long',
      under: 'title-too-short',
    },
    length,
  );
}

/** A meta description's length against the range the analysis used. `text`: as for a title. */
export function descriptionMeasure(length: number, limits: ContentLimits, text?: string): Measure {
  return measure(
    {
      id: 'description',
      label: 'Meta description length',
      min: limits.descriptionMin,
      max: limits.descriptionMax,
      format: (value) => lengthWords(value, text),
      formatAmount: formatCount,
      limitText: `${formatCount(limits.descriptionMin)} to ${formatCount(limits.descriptionMax)} characters`,
      over: 'description-too-long',
      under: 'description-too-short',
    },
    length,
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

export interface UrlMeasures {
  /** One per title on the page, in page order. */
  titles: Measure[];
  descriptions: Measure[];
  words: Measure | null;
  response: Measure | null;
  htmlSize: Measure | null;
}

/** Every value of a URL that has a limit. A value the crawl doesn't have has no measure, never one of 0. */
export function measuresOf(detail: Pick<UrlDetail, 'content' | 'response' | 'limits'>): UrlMeasures {
  const { content, response, limits } = detail;
  const number = (value: number | null | undefined) =>
    value !== null && value !== undefined && Number.isFinite(value) ? value : null;
  const responseMs = number(response?.responseMs);
  const bodyBytes = number(response?.bodyBytes);
  return {
    titles: (content?.titles ?? []).map((title) => titleMeasure(title.length, limits, title.text)),
    descriptions: (content?.descriptions ?? []).map((description) =>
      descriptionMeasure(description.length, limits, description.text),
    ),
    words: content ? wordsMeasure(content.wordCount, limits) : null,
    response: responseMs === null ? null : responseMeasure(responseMs, limits),
    htmlSize: bodyBytes === null || content === null ? null : htmlSizeMeasure(bodyBytes, limits),
  };
}

/** Where a scale ends: a little past the further of the value and its limit, so neither sits on the end. */
export function scaleEnd(measured: Pick<Measure, 'value' | 'min' | 'max'>): number {
  const furthest = Math.max(measured.value, measured.max ?? measured.min ?? 0);
  return Math.max(1, Math.ceil(furthest * 1.25));
}
