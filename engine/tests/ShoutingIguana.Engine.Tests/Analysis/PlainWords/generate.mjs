// Writes the engine's plain words from plain-words.json beside this file, the one place they are written:
//   engine/src/ShoutingIguana.Engine/Analysis/IssuePlainTexts.cs   the checks' words, by check id
//   engine/src/ShoutingIguana.Engine/Analysis/PlainWords.cs        the categories' titles and the skip reasons' words
// A text change is an edit to plain-words.json and a run of this. PlainWordsDataTests holds the compiled words to the
// data file, so a file edited by hand, or a data file changed without a run, fails the tests.
//
// Usage, from the repo root, whenever a text changes or the catalog gains a check, a category or a skip reason:
//   node engine/tests/ShoutingIguana.Engine.Tests/Analysis/PlainWords/generate.mjs
// With --check it writes nothing, and fails when the two files aren't what the data file gives.
//
// The data file's shapes are the API's (GET /api/checks): a check's plainTitle, plainWhat and plainFix (a sentence or a
// step a string), fixer (you, developer, either), plainPassed, and the optional plainOneFix and plainLooksFor; a
// category by its API id (responseCodes); a skip reason by its code, with plainText and plainAction. A text names an
// analysis setting's threshold as {limit:name} (PlainLimits fills it), never as a number.
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { EOL } from 'node:os';

const here = dirname(fileURLToPath(import.meta.url));
const analysis = join(here, '..', '..', '..', '..', 'src', 'ShoutingIguana.Engine', 'Analysis');
const source = name => readFileSync(join(analysis, name), 'utf8');
const data = JSON.parse(readFileSync(join(here, 'plain-words.json'), 'utf8'));
const DATA_FILE = 'engine/tests/ShoutingIguana.Engine.Tests/Analysis/PlainWords/plain-words.json';

const problems = [];
const problem = text => { problems.push(text); return false; };

// --- What the engine has, read from the source it compiles.

// The catalog's checks, in catalog order.
const catalogSource = source('IssueCatalog.cs');
const declared = new Map([...catalogSource.matchAll(/public static readonly IssueDefinition (\w+) = new\("([a-z0-9-]+)",/g)].map(match => [match[1], match[2]]));
const all = catalogSource.split('IReadOnlyList<IssueDefinition> All { get; } =')[1].split('];')[0];
const CHECKS = [...all.matchAll(/[A-Z][A-Z0-9_]+/g)].map(match => declared.get(match[0]));
if (CHECKS.length !== declared.size || CHECKS.some(id => !id)) throw new Error('The catalog could not be read.');

// The categories, in the enum's order: the API's id is the enum's name with a small first letter.
const CATEGORIES = [...source('Models/IssueCategory.cs').split('public enum IssueCategory')[1].matchAll(/^\s+([A-Z]\w+),?\s*$/gm)].map(match => match[1]);
const apiId = name => name[0].toLowerCase() + name.slice(1);
if (CATEGORIES.length === 0) throw new Error('The categories could not be read.');

// The skip reasons: each code and the constant that holds it, in the order they are declared.
const REASONS = [...source('SkippedCheckReasons.cs').matchAll(/public const string (\w+) = "([a-z0-9-]+)";/g)].map(match => ({ constant: match[1], code: match[2] }));
if (REASONS.length === 0) throw new Error('The skip reasons could not be read.');

// --- The data file, held to its shapes: a misspelt field or id is an error here, never a text quietly left out.

const FIXERS = { you: 'You', developer: 'Developer', either: 'Either' };
const CHECK_FIELDS = ['plainTitle', 'plainLooksFor', 'plainWhat', 'plainFix', 'fixer', 'plainPassed', 'plainOneFix'];

function isText(value, where) {
  if (typeof value !== 'string' || value.trim() === '' || value !== value.trim()) return problem(`${where}: not a text, or one with space around it`);
  if (/[\u0000-\u001f]/.test(value)) return problem(`${where}: holds a line break or another control character`);
  // Braces are for limits only: {limit:titleMaxLength}.
  if (/[{}]/.test(value.replace(/\{limit:[A-Za-z]+\}/g, ''))) return problem(`${where}: a brace that isn't part of {limit:name}`);
  return true;
}

function isTexts(value, where) {
  if (!Array.isArray(value) || value.length === 0) return problem(`${where}: not a list of texts`);
  return value.map((text, index) => isText(text, `${where}[${index}]`)).every(Boolean);
}

const keysOf = (object, where) => (object && typeof object === 'object' && !Array.isArray(object) ? Object.keys(object) : (problem(`${where}: missing`), []));

for (const key of Object.keys(data)) if (!['checks', 'categories', 'skipReasons'].includes(key)) problem(`"${key}" is not a part of the data file`);

for (const id of keysOf(data.checks, 'checks')) if (!CHECKS.includes(id)) problem(`checks.${id}: the catalog has no such check`);
for (const id of CHECKS) {
  const entry = data.checks?.[id];
  if (!entry) { problem(`checks.${id}: no plain words`); continue; }
  for (const field of Object.keys(entry)) if (!CHECK_FIELDS.includes(field)) problem(`checks.${id}.${field}: not a field of a check's plain words`);
  isText(entry.plainTitle, `checks.${id}.plainTitle`);
  isTexts(entry.plainWhat, `checks.${id}.plainWhat`);
  isTexts(entry.plainFix, `checks.${id}.plainFix`);
  isText(entry.plainPassed, `checks.${id}.plainPassed`);
  if (!Object.hasOwn(FIXERS, entry.fixer)) problem(`checks.${id}.fixer: "${entry.fixer}" is not one of ${Object.keys(FIXERS).join(', ')}`);
  for (const optional of ['plainOneFix', 'plainLooksFor']) if (Object.hasOwn(entry, optional)) isText(entry[optional], `checks.${id}.${optional}`);
}

for (const id of keysOf(data.categories, 'categories')) if (!CATEGORIES.map(apiId).includes(id)) problem(`categories.${id}: the engine has no such category`);
for (const name of CATEGORIES) {
  const entry = data.categories?.[apiId(name)];
  if (!entry) { problem(`categories.${apiId(name)}: no plain title`); continue; }
  for (const field of Object.keys(entry)) if (field !== 'plainTitle') problem(`categories.${apiId(name)}.${field}: not a field of a category's plain words`);
  isText(entry.plainTitle, `categories.${apiId(name)}.plainTitle`);
}

for (const code of keysOf(data.skipReasons, 'skipReasons')) if (!REASONS.some(reason => reason.code === code)) problem(`skipReasons.${code}: the engine has no such reason`);
for (const { code } of REASONS) {
  const entry = data.skipReasons?.[code];
  if (!entry) { problem(`skipReasons.${code}: no plain words`); continue; }
  for (const field of Object.keys(entry)) if (!['plainText', 'plainAction'].includes(field)) problem(`skipReasons.${code}.${field}: not a field of a skip reason's plain words`);
  isText(entry.plainText, `skipReasons.${code}.plainText`);
  isText(entry.plainAction, `skipReasons.${code}.plainAction`);
}

if (problems.length > 0) {
  console.error(`${DATA_FILE} can't be used as it is:\n  ${problems.join('\n  ')}`);
  process.exit(1);
}

// --- The two files.

// A text as a C# string: raw ("""…""") so the quotation marks in it read as written, escaped where a raw string
// can't hold it (a text that starts or ends with a quotation mark, or holds three in a row).
const literal = text => (text.startsWith('"') || text.endsWith('"') || text.includes('"""')
  ? `"${text.replace(/\\/g, '\\\\').replace(/"/g, '\\"')}"`
  : `"""${text}"""`);

const list = (texts, indent) => ['[', ...texts.map((text, index) => `    ${literal(text)}${index < texts.length - 1 ? ',' : ''}`), ']'].map(line => indent + line).join('\n');

const HEADER = `// Written by engine/tests/ShoutingIguana.Engine.Tests/Analysis/PlainWords/generate.mjs from plain-words.json beside it.
// Don't edit this file: change the words in the data file and run the generator again. A test holds the two together.`;

function check(id) {
  const entry = data.checks[id];
  const optional = [
    entry.plainOneFix === undefined ? null : `            OneFix = ${literal(entry.plainOneFix)}`,
    entry.plainLooksFor === undefined ? null : `            LooksFor = ${literal(entry.plainLooksFor)}`
  ].filter(Boolean);
  return [
    `        ["${id}"] = new(`,
    `            ${literal(entry.plainTitle)},`,
    `${list(entry.plainWhat, '            ')},`,
    `${list(entry.plainFix, '            ')},`,
    `            IssueFixer.${FIXERS[entry.fixer]},`,
    `            ${literal(entry.plainPassed)})${optional.length > 0 ? `\n        {\n${optional.join(',\n')}\n        }` : ''}`
  ].join('\n');
}

const issuePlainTexts = `${HEADER}
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// The checks in plain words, by check id (docs/v2-ui-simple.md, "Plain words"). They live here, apart from
/// <see cref="IssueCatalog"/> and the rules, so every check's words can be written and reviewed in one place. They are
/// words, not results: no part of <see cref="AnalysisFreshness.ChecksFingerprint"/>, so writing or changing one makes no
/// analysis out of date.
/// <para>
/// Every check of the catalog has its words: a test holds that, and holds every entry to the template's limits and the
/// words the design rules out. A check without an entry (one a newer catalog adds before its words are written) has no
/// plain words: hosts get null for it, never an empty text or the technical one in its place.
/// </para>
/// <para>
/// A text names a threshold as <c>{limit:name}</c>, never as a number: <c>{limit:titleMaxLength}</c> reads "60 characters"
/// once <see cref="PlainLimits.Fill(IssuePlainText, AnalysisSettings)"/> has filled it from the settings an analysis ran
/// with, so the words follow a changed threshold. The entries here are as written, tokens and all: hosts show them filled.
/// </para>
/// </summary>
public static class IssuePlainTexts
{
    private static readonly Dictionary<string, IssuePlainText> BY_CHECK = new(StringComparer.Ordinal)
    {
${CHECKS.map(check).join(',\n\n')}
    };

    /// <summary>Every check that has plain words, by its id.</summary>
    public static IReadOnlyDictionary<string, IssuePlainText> All => BY_CHECK;

    /// <summary>The check's plain words, or null when it has none yet.</summary>
    public static IssuePlainText? Find(string checkId) => BY_CHECK.GetValueOrDefault(checkId);
}
`;

const reason = ({ constant, code }) => [
  `        [SkippedCheckReasons.${constant}] = new(`,
  `            ${literal(data.skipReasons[code].plainText)},`,
  `            ${literal(data.skipReasons[code].plainAction)})`
].join('\n');

const plainWords = `${HEADER}
using ShoutingIguana.Engine.Analysis.Models;

namespace ShoutingIguana.Engine.Analysis;

/// <summary>
/// The categories and the reasons a check was skipped, in plain words (docs/v2-ui-simple.md, "Plain words"): the
/// companion of <see cref="IssuePlainTexts"/>, and like it no part of what an analysis stores. Every category and every
/// reason has its words; a reason with no entry (one a newer engine adds before its words are written) has none, and
/// hosts get null for it. A text may name a threshold as <c>{limit:name}</c>, which <see cref="PlainLimits"/> fills.
/// </summary>
public static class PlainWords
{
    /// <summary>By the analysis's reason code (<see cref="SkippedCheckReasons"/>).</summary>
    private static readonly Dictionary<string, SkipReasonPlainText> SKIP_REASONS = new(StringComparer.Ordinal)
    {
${REASONS.map(reason).join(',\n\n')}
    };

    /// <summary>Every skip reason that has plain words, by its code.</summary>
    public static IReadOnlyDictionary<string, SkipReasonPlainText> SkipReasons => SKIP_REASONS;

    /// <summary>The category's title in plain words, such as "Pages that load" for response codes, or null when it has none yet.</summary>
    public static string? CategoryTitle(IssueCategory category) => category switch
    {
${CATEGORIES.map(name => `        IssueCategory.${name} => ${literal(data.categories[apiId(name)].plainTitle)},`).join('\n')}
        _ => null
    };

    /// <summary>Why a check was skipped and what would let it run, in plain words, by the reason's code; null when it has none yet.</summary>
    public static SkipReasonPlainText? SkipReason(string code) => SKIP_REASONS.GetValueOrDefault(code);
}
`;

// --- Write them, or with --check say whether they are what the data gives. A file keeps the line endings it has.

const checkOnly = process.argv.includes('--check');
let stale = 0;
for (const [name, content] of [['IssuePlainTexts.cs', issuePlainTexts], ['PlainWords.cs', plainWords]]) {
  const path = join(analysis, name);
  const existing = existsSync(path) ? readFileSync(path, 'utf8') : null;
  const eol = existing === null ? EOL : existing.includes('\r\n') ? '\r\n' : '\n';
  const written = content.replace(/\n/g, eol);
  if (existing === written) continue;
  stale++;
  if (checkOnly) console.error(`${name} is not what ${DATA_FILE} gives: run the generator.`);
  else writeFileSync(path, written);
}

if (checkOnly && stale > 0) process.exit(1);
console.log(`${CHECKS.length} checks, ${CATEGORIES.length} categories, ${REASONS.length} skip reasons: ${checkOnly ? 'the files are current' : stale === 0 ? 'nothing to change' : `${stale} file${stale === 1 ? '' : 's'} written`}`);
