// Writes engine/src/ShoutingIguana.Engine/Analysis/Rules/ArialAdvanceWidths.cs, the advance widths the title and
// description checks measure text by (docs/v2-pixel-widths.md), from Liberation Sans Regular: metric-compatible with
// Arial, the font Google's desktop results show titles and descriptions in, and licensed SIL OFL 1.1, so its metrics can
// ship (Arial is Monotype's, and nothing from it ships). The licence sits beside the table, in
// LiberationSans-OFL.txt, as the OFL asks.
//
// Usage, from the repo root, with the TTF from the 2.1.5 release (liberation-fonts-ttf-2.1.5.tar.gz at
// https://github.com/liberationfonts/liberation-fonts/releases):
//   node engine/tests/ShoutingIguana.Engine.Tests/Analysis/ArialWidths/generate.mjs <path>/LiberationSans-Regular.ttf
// With --check it writes nothing, and fails when the table isn't what the font gives.
//
// It reads the font's cmap (formats 4 and 12, Windows platform) and hmtx in plain Node. Private-use code points are left
// out, and so are those the measurer takes as wide (DisplayWidth's UAX #11 ranges, read from its source), which are one
// em whatever the font says. The file's header records the font's version and the TTF's SHA-256.
import { createHash } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { EOL } from 'node:os';

const here = dirname(fileURLToPath(import.meta.url));
const rules = join(here, '..', '..', '..', '..', 'src', 'ShoutingIguana.Engine', 'Analysis', 'Rules');
const fontPath = process.argv.slice(2).find(argument => !argument.startsWith('--'));
if (!fontPath) {
  console.error('usage: node generate.mjs <LiberationSans-Regular.ttf> [--check]');
  process.exit(2);
}

const font = readFileSync(fontPath);
const sha256 = createHash('sha256').update(font).digest('hex');

// --- The font's tables.

const tables = new Map();
for (let index = 0, count = font.readUInt16BE(4); index < count; index++) {
  const record = 12 + 16 * index;
  tables.set(font.toString('latin1', record, record + 4), font.readUInt32BE(record + 8));
}

const table = tag => {
  const offset = tables.get(tag);
  if (offset === undefined) throw new Error(`The font has no ${tag} table.`);
  return offset;
};

const unitsPerEm = font.readUInt16BE(table('head') + 18);
const metrics = font.readUInt16BE(table('hhea') + 34);
const advanceOf = glyph => font.readUInt16BE(table('hmtx') + 4 * Math.min(glyph, metrics - 1));

// The version string, name 5, in the Windows English (US) record.
const version = (() => {
  const name = table('name');
  const strings = name + font.readUInt16BE(name + 4);
  for (let index = 0, count = font.readUInt16BE(name + 2); index < count; index++) {
    const record = name + 6 + 12 * index;
    const [platform, encoding, language, id, length, offset] = [0, 2, 4, 6, 8, 10].map(at => font.readUInt16BE(record + at));
    if (platform === 3 && encoding === 1 && language === 0x409 && id === 5) {
      const bytes = font.subarray(strings + offset, strings + offset + length);
      return String.fromCharCode(...Array.from({ length: length / 2 }, (_, at) => bytes.readUInt16BE(2 * at)));
    }
  }

  throw new Error('The font names no version.');
})();

// Code point to glyph, from the Windows Unicode subtables: format 12 (full repertoire) where there is one, and format 4.
const glyphs = new Map();
const cmap = table('cmap');
for (let index = 0, count = font.readUInt16BE(cmap + 2); index < count; index++) {
  const record = cmap + 4 + 8 * index;
  const [platform, encoding] = [font.readUInt16BE(record), font.readUInt16BE(record + 2)];
  if (platform !== 3 || (encoding !== 1 && encoding !== 10)) continue;
  const subtable = cmap + font.readUInt32BE(record + 4);
  const format = font.readUInt16BE(subtable);
  if (format === 4) {
    const segments = font.readUInt16BE(subtable + 6) / 2;
    const ends = subtable + 14, starts = ends + 2 * segments + 2, deltas = starts + 2 * segments, ranges = deltas + 2 * segments;
    for (let segment = 0; segment < segments; segment++) {
      const [end, start] = [font.readUInt16BE(ends + 2 * segment), font.readUInt16BE(starts + 2 * segment)];
      const [delta, range] = [font.readInt16BE(deltas + 2 * segment), font.readUInt16BE(ranges + 2 * segment)];
      for (let codePoint = start; codePoint <= end && codePoint !== 0xFFFF; codePoint++) {
        let glyph;
        if (range === 0) glyph = (codePoint + delta) & 0xFFFF;
        else {
          glyph = font.readUInt16BE(ranges + 2 * segment + range + 2 * (codePoint - start));
          if (glyph !== 0) glyph = (glyph + delta) & 0xFFFF;
        }

        if (glyph !== 0 && !glyphs.has(codePoint)) glyphs.set(codePoint, glyph);
      }
    }
  } else if (format === 12) {
    for (let group = 0, groups = font.readUInt32BE(subtable + 12); group < groups; group++) {
      const at = subtable + 16 + 12 * group;
      const [start, end, first] = [font.readUInt32BE(at), font.readUInt32BE(at + 4), font.readUInt32BE(at + 8)];
      for (let codePoint = start; codePoint <= end; codePoint++) {
        if (first + codePoint - start !== 0) glyphs.set(codePoint, first + codePoint - start);
      }
    }
  }
}

// --- What the measurer takes as wide, read from the source it compiles.

const wideSource = readFileSync(join(rules, 'DisplayWidth.cs'), 'utf8').split('WIDE =')[1].split('];')[0];
const WIDE = [...wideSource.matchAll(/\(0x([0-9A-F]+), 0x([0-9A-F]+)\)/g)].map(match => [parseInt(match[1], 16), parseInt(match[2], 16)]);
if (WIDE.length < 40) throw new Error('DisplayWidth\'s wide ranges could not be read.');
const isWide = codePoint => WIDE.some(([first, last]) => codePoint >= first && codePoint <= last);
const isPrivateUse = codePoint => (codePoint >= 0xE000 && codePoint <= 0xF8FF) || codePoint >= 0xF0000;

const kept = [...glyphs.keys()].filter(codePoint => !isWide(codePoint) && !isPrivateUse(codePoint)).sort((a, b) => a - b);

// Runs of consecutive code points, each with its first advance's place in one list of advances.
const runs = [];
const advances = [];
for (const codePoint of kept) {
  const last = runs.at(-1);
  if (last && last.last === codePoint - 1) last.last = codePoint;
  else runs.push({ first: codePoint, last: codePoint, offset: advances.length });
  advances.push(advanceOf(glyphs.get(codePoint)));
}

// --- The file.

const hex = value => `0x${value.toString(16).toUpperCase().padStart(4, '0')}`;
const lines = (items, perLine) => Array.from({ length: Math.ceil(items.length / perLine) },
  (_, line) => `        ${items.slice(line * perLine, (line + 1) * perLine).join(', ')}`).join(',\n');

const content = `// Written by engine/tests/ShoutingIguana.Engine.Tests/Analysis/ArialWidths/generate.mjs from Liberation Sans Regular,
// ${version}, SHA-256 ${sha256}. Don't edit this file: run the generator on the font.
//
// The advance widths of Liberation Sans, which are Arial's by design (it is metric-compatible with Arial).
// Liberation Sans: Digitized data copyright (c) 2010 Google Corporation, with Reserved Font Arimo, Tinos and Cousine.
// Copyright (c) 2012 Red Hat, Inc., with Reserved Font Name Liberation. Licensed under the SIL Open Font License,
// Version 1.1, which is in LiberationSans-OFL.txt beside this file.
namespace ShoutingIguana.Engine.Analysis.Rules;

/// <summary>
/// How wide each character Arial draws is, in font units (<see cref="UNITS_PER_EM"/> to the em): the advance widths of
/// Liberation Sans Regular, metric-compatible with Arial. ${kept.length.toLocaleString('en-GB')} code points in ${runs.length} runs, with private-use code points
/// and those <see cref="DisplayWidth"/> takes as wide left out. See docs/v2-pixel-widths.md.
/// </summary>
internal static class ArialAdvanceWidths
{
    /// <summary>The font's units to the em.</summary>
    public const int UNITS_PER_EM = ${unitsPerEm};

    /// <summary>The font the widths come from.</summary>
    public const string FONT = "Liberation Sans Regular, ${version}";

    /// <summary>Runs of consecutive code points, sorted, each with where its first advance is in <see cref="ADVANCES"/>.</summary>
    private static readonly (int First, int Last, int Offset)[] RUNS =
    [
${lines(runs.map(run => `(${hex(run.first)}, ${hex(run.last)}, ${run.offset})`), 6)}
    ];

    private static readonly ushort[] ADVANCES =
    [
${lines(advances.map(String), 20)}
    ];

    /// <summary>The code point's advance in font units, or false when the font doesn't draw it.</summary>
    public static bool TryGet(int codePoint, out int advance)
    {
        int low = 0, high = RUNS.Length - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            var run = RUNS[middle];
            if (codePoint < run.First)
            {
                high = middle - 1;
            }
            else if (codePoint > run.Last)
            {
                low = middle + 1;
            }
            else
            {
                advance = ADVANCES[run.Offset + codePoint - run.First];
                return true;
            }
        }

        advance = 0;
        return false;
    }
}
`;

const path = join(rules, 'ArialAdvanceWidths.cs');
const existing = existsSync(path) ? readFileSync(path, 'utf8') : null;
const eol = existing === null ? EOL : existing.includes('\r\n') ? '\r\n' : '\n';
const written = content.replace(/\n/g, eol);
const checkOnly = process.argv.includes('--check');
if (existing === written) console.log(`${kept.length} code points in ${runs.length} runs from ${version}: the table is current`);
else if (checkOnly) {
  console.error(`ArialAdvanceWidths.cs is not what ${fontPath} gives: run the generator.`);
  process.exit(1);
} else {
  writeFileSync(path, written);
  console.log(`${kept.length} code points in ${runs.length} runs from ${version}: ArialAdvanceWidths.cs written`);
}
