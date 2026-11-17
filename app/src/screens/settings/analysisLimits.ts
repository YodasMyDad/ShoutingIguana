// Settings > Analysis's words and its draft (docs/v2-sidecar-api.md, Analysis settings): each limit's name, its range
// in words, what's typed held as typed, and the engine's errors sorted into those of a field and those of the whole.
// Pure, so every word is tested.
import type { AnalysisLimit, AnalysisSettingsChange } from '../../api/types';
import { formatBytes, formatCount, formatCountOf } from '../../lib/format';

/** The limits' read: one for the app, as the engine keeps one set for every crawl. */
export const ANALYSIS_SETTINGS_KEY = ['analysis-settings'] as const;

/** Each limit's name in the list: the engine gives what it does, in a sentence; the name is the app's. */
const NAMES: Readonly<Record<string, string>> = {
  titleMinPixels: 'Narrowest title',
  titleMaxPixels: 'Widest title',
  descriptionMinPixels: 'Narrowest meta description',
  descriptionMaxPixels: 'Widest meta description',
  minWordCount: 'Fewest words on a page',
  duplicateMinWordCount: 'Fewest words to compare for duplicates',
  nearDuplicateMaxBits: 'Near-duplicate distance',
  imageAltMaxLength: 'Longest alt text',
  largeImageBytes: 'Largest image',
  largePageBytes: 'Largest page',
  slowResponseMs: 'Slowest response',
  maxUrlLength: 'Longest URL',
  minInternalOutlinks: 'Fewest links to other pages',
  maxClickDepth: 'Deepest page',
  maxInternalLinks: 'Most internal links',
  maxExternalLinks: 'Most links to other sites',
  slowExternalLinkMs: 'Slowest page linked elsewhere',
};

/** A limit's name: the app's for the limits it knows, else its id in words ("someNewLimit" is "Some new limit"). */
export function limitName(id: string): string {
  const known = NAMES[id];
  if (known) return known;
  const words = id.replace(/([a-z\d])([A-Z])/g, '$1 $2').toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

const UNIT_WORDS: Record<string, [string, string]> = {
  characters: ['character', 'characters'],
  pixels: ['px', 'px'],
  words: ['word', 'words'],
  bits: ['bit', 'bits'],
  bytes: ['byte', 'bytes'],
  ms: ['ms', 'ms'],
  links: ['link', 'links'],
  clicks: ['click', 'clicks'],
};

/** "60 characters", "580 px", "1 word", "1,000 ms", "200 KB" for bytes. */
export function valueWords(value: number, unit: string): string {
  if (unit === 'bytes') return formatBytes(value);
  const [one, many] = UNIT_WORDS[unit] ?? [unit, unit];
  return `${formatCount(value)} ${value === 1 ? one : many}`;
}

/** The unit beside the field: "characters", "px", "ms". */
export function unitWords(unit: string): string {
  return UNIT_WORDS[unit]?.[1] ?? unit;
}

/**
 * "From 1 to 10,000 characters. The engine’s is 115 characters."; a width with the characters it holds, "From 1 to
 * 100,000 px. The engine’s is 580 px (about 60 characters)."
 */
export function rangeWords(limit: AnalysisLimit): string {
  const range = `From ${formatCount(limit.min)} to ${formatCount(limit.max)} ${unitWords(limit.unit)}`;
  const bytes = limit.unit === 'bytes' ? ` (${formatBytes(limit.max)})` : '';
  const about = limit.pixelsPerCharacter
    ? ` (about ${formatCountOf(approximateCharacters(limit.default, limit.pixelsPerCharacter), 'character')})`
    : '';
  return `${range}${bytes}. The engine’s is ${valueWords(limit.default, limit.unit)}${about}.`;
}

/**
 * About how many ordinary characters fit in a width: 580 px of title is about 60, 200 about 20, 985 px of description
 * about 160 and 400 about 65. Rounded as the engine's plain words round it (`PlainLimits.ApproximateCharacters`): to the
 * nearest 5 from 20 on, to the nearest whole below.
 */
export function approximateCharacters(pixels: number, pixelsPerCharacter: number): number {
  const characters = pixels / pixelsPerCharacter;
  return characters >= 20 ? Math.round(characters / 5) * 5 : Math.round(characters);
}

/**
 * Under a width's field, what's typed as the characters it holds: "About 60 characters of ordinary text." Null for a
 * limit that isn't a width, and for a field that's empty or holds no width the engine would take.
 */
export function charactersHint(limit: AnalysisLimit, typed: string): string | null {
  if (!limit.pixelsPerCharacter) return null;
  const value = changeOf({ [limit.id]: typed })[limit.id];
  if (typeof value !== 'number' || value < limit.min || value > limit.max) return null;
  return `About ${formatCountOf(approximateCharacters(value, limit.pixelsPerCharacter), 'character')} of ordinary text.`;
}

/** What's typed in each field, as typed: a half-typed value stays, and reaches the engine as typed for its message. */
export type LimitDraft = Readonly<Record<string, string>>;

export function draftOf(limits: readonly AnalysisLimit[]): LimitDraft {
  return Object.fromEntries(limits.map((limit) => [limit.id, String(limit.value)]));
}

/**
 * The draft as the body a save or a check takes: a whole number as a number, anything else as typed (the engine says
 * what's wrong with text or a fraction, field by field), an empty field as null (the engine's value).
 */
export function changeOf(draft: LimitDraft): AnalysisSettingsChange {
  const change: Record<string, number | string | null> = {};
  for (const [id, text] of Object.entries(draft)) {
    const trimmed = text.trim().replace(/[,\s]/g, '');
    change[id] = trimmed === '' ? null : /^-?\d+$/.test(trimmed) ? Number(trimmed) : text;
  }
  return change as AnalysisSettingsChange;
}

/** Whether the draft holds what's saved: nothing to save. */
export function sameAsSaved(draft: LimitDraft, limits: readonly AnalysisLimit[]): boolean {
  const change = changeOf(draft);
  return limits.every((limit) => change[limit.id] === limit.value);
}

/**
 * The engine's errors split into each field's and the rest: a key that's a limit's goes beside its field; `settings`
 * (the limits together) and `limits[n]` (a reset of a name that's no limit's) go above the form.
 */
export function splitErrors(
  errors: Readonly<Record<string, readonly string[]>>,
  ids: readonly string[],
): { byLimit: Record<string, string>; whole: string[] } {
  const byLimit: Record<string, string> = {};
  const whole: string[] = [];
  for (const [key, messages] of Object.entries(errors)) {
    if (messages.length === 0) continue;
    if (ids.includes(key)) byLimit[key] = messages.join(' ');
    else whole.push(...messages);
  }
  return { byLimit, whole };
}
