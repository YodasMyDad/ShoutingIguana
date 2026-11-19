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
  responsiveImageMinWidth: 'Widest image without smaller versions',
  legacyImageMinBytes: 'Largest image in an older format',
  inlineImageMaxBytes: 'Largest image built into a page',
  headingMaxLength: 'Longest H1 or H2',
  h2MinWords: 'Fewest words that need an H2',
  readabilityMinScore: 'Lowest reading ease',
  largePageBytes: 'Largest page',
  slowResponseMs: 'Slowest response',
  maxUrlLength: 'Longest URL',
  minInternalOutlinks: 'Fewest links to other pages',
  maxClickDepth: 'Deepest page',
  maxInternalLinks: 'Most internal links',
  maxExternalLinks: 'Most links to other sites',
  slowExternalLinkMs: 'Slowest page linked elsewhere',
};

/**
 * The limits in the groups the page lists them in (the audit of 2026-11-19, H-15: 23 limits in one column, 2,926 px
 * of scroll). A limit no group names is listed last, under "Other", so a new one from the engine is never lost.
 */
export const LIMIT_GROUPS: readonly { title: string; ids: readonly string[] }[] = [
  {
    title: 'Titles and descriptions',
    ids: ['titleMinPixels', 'titleMaxPixels', 'descriptionMinPixels', 'descriptionMaxPixels'],
  },
  {
    title: 'Content',
    ids: [
      'minWordCount',
      'duplicateMinWordCount',
      'nearDuplicateMaxBits',
      'headingMaxLength',
      'h2MinWords',
      'readabilityMinScore',
    ],
  },
  {
    title: 'Images',
    ids: [
      'largeImageBytes',
      'legacyImageMinBytes',
      'inlineImageMaxBytes',
      'responsiveImageMinWidth',
      'imageAltMaxLength',
    ],
  },
  { title: 'Pages and speed', ids: ['largePageBytes', 'slowResponseMs', 'maxUrlLength', 'maxClickDepth'] },
  { title: 'Links', ids: ['minInternalOutlinks', 'maxInternalLinks', 'maxExternalLinks', 'slowExternalLinkMs'] },
];

/** The limits, group by group, in each group's order; then any the groups don't name. Empty groups are left out. */
export function groupedLimits<T extends { id: string }>(limits: readonly T[]): { title: string; limits: T[] }[] {
  const grouped = LIMIT_GROUPS.map((group) => ({
    title: group.title,
    limits: group.ids.flatMap((id) => limits.filter((limit) => limit.id === id)),
  }));
  const named = new Set(LIMIT_GROUPS.flatMap((group) => group.ids));
  const other = limits.filter((limit) => !named.has(limit.id));
  return [...grouped, { title: 'Other', limits: other }].filter((group) => group.limits.length > 0);
}

// ------------------------------------------------------------------------------------------- Units people type in
//
// The engine keeps a size in bytes and the near-duplicate distance in bits of a 256-bit fingerprint. Nobody thinks in
// either (H-15: "204800 for 200 KB", "bits of 256"), so those two units are typed and shown in KB (or with MB or GB
// written after the number) and in "% the same"; the engine's own unit is what's sent and saved.

const BYTES_PER_KB = 1024;
/** The fingerprint's size: two pages `d` bits apart are about cos(πd/256) alike (engine, AnalysisSettings). */
const FINGERPRINT_BITS = 256;

/** A number as someone would type it: no trailing zeros, at most `decimals` places, no grouping. */
function plainNumber(value: number, decimals: number): string {
  return String(Number(value.toFixed(decimals)));
}

/** How alike two pages `bits` apart are, as a percentage. */
function percentAlike(bits: number): number {
  return Math.cos((Math.PI * bits) / FINGERPRINT_BITS) * 100;
}

/** The distance in bits a percentage alike stands for, to the nearest bit. Null for one that's no percentage. */
function bitsOf(percent: number): number | null {
  if (!Number.isFinite(percent) || percent < -100 || percent > 100) return null;
  return Math.round((Math.acos(percent / 100) * FINGERPRINT_BITS) / Math.PI);
}

/** The percentage that reads back as exactly these bits, in as few decimals as do it: 24 bits is "95.7". */
function percentText(bits: number): string {
  for (const decimals of [0, 1, 2, 3]) {
    const text = plainNumber(percentAlike(bits), decimals);
    if (bitsOf(Number(text)) === bits) return text;
  }
  return plainNumber(percentAlike(bits), 4);
}

/** Whether a limit is typed in another unit than the engine keeps it in. */
export function isConverted(unit: string): boolean {
  return unit === 'bytes' || unit === 'bits';
}

/** A limit's value as its field holds it: KB for a size, % alike for the near-duplicate distance, else the number. */
export function shownValue(value: number, unit: string): string {
  if (unit === 'bytes') return plainNumber(value / BYTES_PER_KB, 2);
  if (unit === 'bits') return percentText(value);
  return String(value);
}

/**
 * What's typed in a converted field as the engine's number: "200", "200 KB", "1.5 MB" and "1GB" are bytes; "95.7" and
 * "95.7%" are bits. Null when it isn't a size or a percentage, so it's sent as typed and the field says what's wrong.
 */
export function engineValue(text: string, unit: string): number | null {
  const typed = text.trim().replace(/,/g, '');
  if (unit === 'bytes') {
    const match = /^(\d+(?:\.\d+)?)\s*(kb|mb|gb)?$/i.exec(typed);
    if (!match?.[1]) return null;
    const scale = { kb: 1, mb: BYTES_PER_KB, gb: BYTES_PER_KB * BYTES_PER_KB }[(match[2] ?? 'kb').toLowerCase()] ?? 1;
    return Math.round(Number(match[1]) * BYTES_PER_KB * scale);
  }
  if (unit === 'bits') {
    const match = /^(\d+(?:\.\d+)?)\s*%?$/.exec(typed);
    return match?.[1] ? bitsOf(Number(match[1])) : null;
  }
  return null;
}

/** A converted limit's own words, where the engine's sentence speaks in its unit (bits of 256). */
const DESCRIPTIONS: Readonly<Record<string, string>> = {
  nearDuplicateMaxBits:
    'Two pages whose main content is at least this alike count as near duplicates. Higher is stricter: 100% is only exact copies.',
};

/** What a limit does, in a sentence: the engine's, or the app's where the engine's speaks in a unit nobody types. */
export function limitDescription(limit: Pick<AnalysisLimit, 'id' | 'description'>): string {
  return DESCRIPTIONS[limit.id] ?? limit.description;
}

/**
 * What a converted field says when the engine refuses its value: the engine's message is in bytes or bits, so the
 * field gives its range in the unit typed. Null for any other limit, whose engine message stands.
 */
export function convertedError(limit: AnalysisLimit): string | null {
  if (limit.unit === 'bytes') {
    return `Type a size up to ${formatBytes(limit.max)}, in KB: 200, or 1.5 MB.`;
  }
  if (limit.unit === 'bits') {
    return `Type a percentage from ${percentText(limit.max)} to ${percentText(limit.min)}.`;
  }
  return null;
}

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
  px: ['px', 'px'],
  points: ['point', 'points'],
  words: ['word', 'words'],
  bits: ['bit', 'bits'],
  bytes: ['byte', 'bytes'],
  ms: ['ms', 'ms'],
  links: ['link', 'links'],
  clicks: ['click', 'clicks'],
};

/** "60 characters", "580 px", "1 word", "1,000 ms", "200 KB" for bytes, "95.7% the same" for bits. */
export function valueWords(value: number, unit: string): string {
  if (unit === 'bytes') return formatBytes(value);
  if (unit === 'bits') return `${percentText(value)}% the same`;
  const [one, many] = UNIT_WORDS[unit] ?? [unit, unit];
  return `${formatCount(value)} ${value === 1 ? one : many}`;
}

/** The unit beside the field: "characters", "px", "ms"; "KB" for a size and "% the same" for near duplicates. */
export function unitWords(unit: string): string {
  if (unit === 'bytes') return 'KB';
  if (unit === 'bits') return '% the same';
  return UNIT_WORDS[unit]?.[1] ?? unit;
}

/**
 * "From 1 to 10,000 characters. The engine’s is 115 characters."; a width with the characters it holds, "From 1 to
 * 100,000 px. The engine’s is 580 px (about 60 characters)."
 */
export function rangeWords(limit: AnalysisLimit): string {
  // A size and a likeness in the units typed, never the engine's bytes and bits.
  if (limit.unit === 'bytes') {
    const upTo =
      limit.min < BYTES_PER_KB
        ? `Up to ${formatBytes(limit.max)}`
        : `From ${formatBytes(limit.min)} to ${formatBytes(limit.max)}`;
    return `${upTo}. The engine’s is ${formatBytes(limit.default)}.`;
  }
  if (limit.unit === 'bits') {
    return `From ${percentText(limit.max)}% to ${percentText(limit.min)}%. The engine’s is ${percentText(limit.default)}%.`;
  }
  const range = `From ${formatCount(limit.min)} to ${formatCount(limit.max)} ${unitWords(limit.unit)}`;
  const about = limit.pixelsPerCharacter
    ? ` (about ${formatCountOf(approximateCharacters(limit.default, limit.pixelsPerCharacter), 'character')})`
    : '';
  return `${range}. The engine’s is ${valueWords(limit.default, limit.unit)}${about}.`;
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
  return Object.fromEntries(limits.map((limit) => [limit.id, shownValue(limit.value, limit.unit)]));
}

/**
 * The draft as the body a save or a check takes: a whole number as a number, anything else as typed (the engine says
 * what's wrong with text or a fraction, field by field), an empty field as null (the engine's value). A size typed in
 * KB and a likeness typed in % go as the engine's bytes and bits (`limits` says which fields those are).
 */
export function changeOf(draft: LimitDraft, limits: readonly AnalysisLimit[] = []): AnalysisSettingsChange {
  const units = new Map(limits.map((limit) => [limit.id, limit.unit]));
  const change: Record<string, number | string | null> = {};
  for (const [id, text] of Object.entries(draft)) {
    const trimmed = text.trim().replace(/[,\s]/g, '');
    const unit = units.get(id);
    if (trimmed === '') change[id] = null;
    else if (unit !== undefined && isConverted(unit)) change[id] = engineValue(text, unit) ?? text;
    else change[id] = /^-?\d+$/.test(trimmed) ? Number(trimmed) : text;
  }
  return change as AnalysisSettingsChange;
}

/** Whether the draft holds what's saved: nothing to save. */
export function sameAsSaved(draft: LimitDraft, limits: readonly AnalysisLimit[]): boolean {
  const change = changeOf(draft, limits);
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
