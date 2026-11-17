# Title and description widths in pixels (step 3b)

`title-too-long`, `title-too-short`, `description-too-long` and `description-too-short` measure how wide the text is in
Google's desktop results, in pixels, not how many characters it has. They keep their ids. Built on `ui/3b-pixels`
(commits [below](#what-was-built-by-commit)) from the design in
[`handoff/2026-11-17/pixels-design.md`](handoff/2026-11-17/pixels-design.md), which has the research in full. This
page is the design as built: where the two differ, this page is right.

**The decision (owner, 2026-11-17).** "Should those checks measure pixel width, as Screaming Frog does, instead of
characters?" "Yes." Later the same day the owner set the title limit at 580 px, not Screaming Frog's 561
([below](#2-why-580-for-titles)).

- **Limits.** A title over 580 px or under 200 px; a description over 985 px or under 400 px. Each can be changed in
  Settings > Analysis, in px, with the characters it holds as a hint.
- **Advanced** says `612 px, over 580 (64 characters)`. **Simple** says where Google will likely cut the title, and
  never shows px: "Google will likely cut this title off after “…Phone, Email or Contract”", or "Only 10
  characters long".
- **Measured** with Liberation Sans 2.1.5's advance widths (metric-compatible with Arial, SIL OFL 1.1) at 20 px for
  titles and 14 px for descriptions, as Google shows them.
- **Every analysis is out of date** (`AnalysisFreshness.VERSION` 12) until analysed again.

The API shapes are in [the sidecar API](v2-sidecar-api.md#analysis-settings-e6); what each check shows is in
[the per-check table](v2-evidence.md#the-checks).

## 1. What Google does (research)

Google publishes no font, width or limit. It says titles and snippets are "truncated … as needed, typically to fit the
device width", and that it may rewrite title links. Everything below comes from tool vendors and third parties.
Sources are at the end; **(U)** marks what couldn't be confirmed.

| | Desktop | Mobile |
| --- | --- | --- |
| Title font | Arial 20 px, normal weight, line height 1.3 (Yoast's preview code; Firefox bug 1866588, Nov 2023). One sample from Sep 2026 (zh-TW interface) measured **22 px in a 652 px column** **(U)**: we may be one font change behind | Roboto or Google Sans, about 16 to 18 px, on up to 2 lines **(U)** |
| Title room | One line in a 600 px column (since Jul 2016, when it went from 512 px). Measured cut points: about 580 px (Sistrix, Jul 2025; Screaming Frog's learn page, 2024) | About 920 px over 2 lines (Sistrix); about 485 px (Screaming Frog's learn page) **(U)** |
| Description font | Arial 14 px (Yoast) **(U for 2026)** | Roboto 14 px **(U)** |
| Description room | About 2 lines. Tools use 920 px (Rank Math), 960 px (Mangools) and 985 to 1,005 px (Screaming Frog) | About 680 px (Mangools), up to 3 lines **(U)** |
| How it's cut | Google cuts the text itself and adds `...`, after the last whole word that fits (Screaming Frog, 2014 and 2024; the 2026 measurement). Titles were once cut mid-word by CSS (2014), and sometimes still are **(U)** | Same |

### Industry thresholds

| Tool | Title | Description | Unit |
| --- | --- | --- | --- |
| Screaming Frog (defaults) | Over 561 px, below 200 px; 60 and 30 characters | Over 985 px, below 400 px; 155 and 70 characters | both: separate filters |
| Yoast | 600 px max, 400 px min | 156 and 120 characters | px for titles |
| Rank Math | 60 characters, about 580 px | 160 characters and 920 px | both |
| Portent | 600 px | 320 characters | mixed |
| Sistrix | 580 px desktop, 920 px mobile | n/a | px |
| Sitebulb | over 60, under 40 characters | over 320, under 110 characters | characters |
| Moz, Semrush, Ahrefs | no published pixel limit (Moz: 50 to 60 characters, about 600 px) **(U)** | n/a | n/a |
| **Shouting Iguana** | **over 580 px, under 200 px** | **over 985 px, under 400 px** | px |

**Why Screaming Frog's 561 and 985 (inferred, (U)).** Screaming Frog publishes neither its font nor its reasons; it
calls its limits "an approximation" that "Google adjusts frequently". Two things make Arial at 20 px and 14 px the
likely basis:

- **Titles.** 600 px less Google's space and `...` (5.6 + 16.7 px at 20 px) is 578 px, close to the measured 580.
  561 leaves a further 17 px of margin.
- **The character defaults line up.** Measured in Arial 20/14, real titles average 9.1 to 9.4 px a character and
  descriptions 6.0 to 6.3 (the three audit crawls). So 561 px is about 60 characters and 985 px about 158: Screaming
  Frog's own 60 and 155.

## 2. Why 580 for titles

The owner's ruling: a title is flagged where Google's results are measured to cut it, about 580 px (Sistrix, Jul 2025;
Screaming Frog's own learn page, 2024; and 600 px less the 22 px of a space and `...`), not 17 px before it.
Screaming Frog's 561 flags titles Google shows whole: on the garden crawl it flags 97 titles, 580 flags 69 and 600
flags 39. The other three limits are Screaming Frog's: 200 px for titles, 985 px and 400 px for descriptions.

## 3. Measuring: `SearchResultWidth`

`Analysis/Rules/SearchResultWidth.cs` measures a title or description (`SearchResultText.Title` or `Description`):

```csharp
SearchResultMeasure Measure(string text, SearchResultText kind);  // (int Characters, int Pixels)
string? Cut(string text, SearchResultText kind, int limitPixels); // the part that shows; null when all of it fits
int Pixels(long units, int fontPixels);                          // font units at a size, in whole px
```

`TitleRules`, `DescriptionRules`, `UrlSummaryBuilder`, `UrlInspectorReader`, `UrlComputedColumns` and the report use
it. `DisplayWidth` keeps only `IsWide`, its UAX #11 test; its `Measure` (full-width characters as two) is gone.

- **The text.** As a browser renders it: runs of HTML white space (space, tab, CR, LF, form feed) become one space, and
  the ends are trimmed. A no-break space stays. Characters are user-perceived (`StringInfo` text elements).
- **The table.** `Analysis/Rules/ArialAdvanceWidths.cs` holds the advance width of each code point Liberation Sans
  Regular 2.1.5 maps, in font units (2,048 to the em, as Arial's): 2,327 code points in 125 runs of consecutive code
  points, binary-searched, about 5 KB. Private-use code points and those `DisplayWidth` takes as wide are left out.
  - **Why Liberation Sans.** It is metric-compatible with Arial: the same advance widths, by design. It is licensed SIL
    OFL 1.1 (since 2.0, July 2012), so its metrics can ship. Arial is Monotype's, and nothing from it ships.
  - **Generated, never edited.** From the repo root, with `LiberationSans-Regular.ttf` from
    `liberation-fonts-ttf-2.1.5.tar.gz` on the project's GitHub releases page:
    `node engine/tests/ShoutingIguana.Engine.Tests/Analysis/ArialWidths/generate.mjs <path>/LiberationSans-Regular.ttf`
    (`--check` writes nothing and fails when the table isn't what the font gives). Plain Node: it reads `cmap`
    (formats 4 and 12) and `hmtx`, and the file's header records the font's version and the TTF's SHA-256,
    `76d04c18ea243f426b7de1f3ad208e927008f961dc5945e5aad352d0dfde8ee8`.
  - **The licence.** The OFL text is `Analysis/Rules/LiberationSans-OFL.txt`, beside the table. About's licences list
    has "Liberation Sans (advance widths)" 2.1.5, OFL-1.1 (`app/scripts/licences.mjs`, `engineFontPackages`).
- **Each user-perceived character:**

  | The character | Width |
  | --- | --- |
  | Its first code point is wide (`DisplayWidth.IsWide`: UAX #11 W and F, the emoji blocks whole), or it holds U+FE0F. A ZWJ emoji sequence and a flag pair start with a wide code point | **1 em**, whole: 20 px in a title, 14 px in a description |
  | Its first code point is in the table | That advance, plus the advances of the others in the table. Combining marks (Mn, Me) and format characters (Cf: ZWJ, ZWNJ, soft hyphen, bidi marks) are 0 |
  | Anything else: Thai, Devanagari, Arabic and so on, which a browser draws from fallback fonts | **1,139/2,048 em once**, the width of `n`, marks and all. A rough average **(U)** |

- **Pixels.** `(units × font px + 1024) / 2048` in whole numbers: halves round up. The rule compares that same integer,
  so a detail never reads "580 px, over 580".
- **Constants, not settings.** `SearchResultWidth.TITLE_FONT_PX` 20 and `DESCRIPTION_FONT_PX` 14. If Google moves to
  22 px, that's an engine change with a new `AnalysisFreshness.VERSION`. The limits are what the user turns.
- **A typical character's width**, for the characters hint, the plain words and converting old limits:
  `AnalysisSettings.TYPICAL_TITLE_CHARACTER_PX` 9.4 and `TYPICAL_DESCRIPTION_CHARACTER_PX` 6.2. Measured: the averages
  over the three audit crawls' 293 titles and 275 descriptions (9.11 to 9.43 a site for titles, 5.99 to 6.27 for
  descriptions).
- **Tests** (`SearchResultWidthTests`) hold spot values of the table (`W` 1933, `i` 455, space 569, `n` 1139, `.` 569,
  `…` 2048, `|` 532, no-break space 569, the same as Arial's), a sum worked by hand, white space, marks and format
  characters, wide characters and emoji, the fallback, halves rounding up, and every case of the cut below.

## 4. Where Google will likely cut (`Cut`)

The cut is Simple's sentence, the report's, and the `titleInResults` and `descriptionInResults` columns.

1. Walk the user-perceived characters, adding widths. While the total stays within the limit, note each **break**:
   before a space, and between two characters where either is wide (Chinese, Japanese and Korean break between
   characters).
2. At the first character that takes the total over the limit, the part that shows ends at the last break; with none
   (one word or URL longer than the limit), mid-word, after the last character that fits.
3. Trailing white space goes, and one separator left dangling there (`|`, `-`, `–`, `—`, `·`, `:`, `,`, `&`, `/`): so
   "…Chair Set", not "…Chair Set &".
4. Text within the limit returns null: nothing is cut.

**The limit is the setting, not the 600 px column.** A title is flagged exactly when its width is over
`titleMaxPixels`, and the cut uses the same number in the same whole pixels, so every flagged title has a cut and no
other title has one. A user who raises the limit to 600 moves the cut with it.

## 5. The checks

- **`TitleRules` and `DescriptionRules`** compare `Pixels` with `TitleMaxPixels` and `TitleMinPixels`,
  `DescriptionMaxPixels` and `DescriptionMinPixels`. The ids, severities (Notice), categories, subject and grouping are
  unchanged, so the checks' fingerprint is too.
- **Details**, from `RuleText.Over` and `Under` with `RuleText.Pixels` ("612 px") and `RuleText.WithCharacters`:
  - `612 px, over 580 (64 characters)`
  - `98 px, under 200 (10 characters)`
  - `1,084 px, over 985 (183 characters)`

  `RuleText.Width` and its "counting full-width characters as two" went: a wide character takes its room in the width.
- **The catalog's text** says pixels. `title-too-long`'s description: "The page's title is wider than 580 pixels (the
  default, which can be changed in the analysis settings), about 60 characters of ordinary text, measured as Google's
  desktop results show titles: Arial at 20 px, with full-width characters such as Chinese, Japanese and Korean, and
  emoji, one em wide." Its why adds: "Width depends on the letters ("WWW" is more than four times as wide as "iii"), so
  pixels predict the cut better than characters: 580 px is about where Google's desktop results cut a title, after the
  last whole word that fits, and a rough guide, not a rule." The description checks name Arial at 14 px, and
  `description-too-long`'s why calls 985 px Screaming Frog's default.

## 6. Settings

**`AnalysisSettings`.** Four properties replace the character ones. They have new names because the unit changed, so
a limit saved as 60 characters is never read as 60 px:

| Property (JSON) | Default | Unit | Range in the sidecar | Plain words and the hint at the default |
| --- | --- | --- | --- | --- |
| `TitleMinPixels` (`titleMinPixels`) | 200 | pixels | 0 to 100,000 | about 20 characters |
| `TitleMaxPixels` (`titleMaxPixels`) | 580 | pixels | 1 to 100,000 | about 60 characters |
| `DescriptionMinPixels` (`descriptionMinPixels`) | 400 | pixels | 0 to 100,000 | about 65 characters |
| `DescriptionMaxPixels` (`descriptionMaxPixels`) | 985 | pixels | 1 to 100,000 | about 160 characters |

`Validate` keeps its pair rules, in widths: "Title widths must satisfy 0 <= minimum < maximum."

**Limits saved in characters** (`titleMinLength`, `titleMaxLength`, `descriptionMinLength`, `descriptionMaxLength`) are
converted by `LegacyAnalysisSettings` wherever saved settings are read: the sidecar's `analysis-settings.json`, and a
crawl file's stored analysis settings (the design left crawl files at the defaults; as built they're converted too).

- A limit at its old default (30, 60, 70 or 155) takes the new default (200, 580, 400 or 985): a limit never changed
  follows the engine.
- Any other keeps the user's intent: round(characters × 9.4) for titles, × 6.2 for descriptions, halves away from
  zero. 70 characters of title is 658 px; 160 of description is 992 px.
- When the old name and the new are both there, the new one wins.
- **The sidecar** logs one information line naming what it converted ("titleMaxLength 70 as titleMaxPixels 658"), and
  its next save writes only the new names. A converted value that breaks its range or its pair is passed over by the
  existing per-limit handling, which keeps the rest. **A broken pair now goes back together**: before, only the widest
  was dropped, and if the narrowest still broke the pair against the engine's widest, every saved limit was reset.
- **A crawl file** is never rewritten. Its old analysis is read with the limits converted, so one run at the old
  defaults doesn't read as `SettingsChanged`; it is out of date anyway (`EngineChanged`, [below](#9-freshness)).
- **The API refuses the old names**: `PUT`, `validate` and `reset` with one is `bad-request` (400), as any property it
  doesn't know is. The app and the sidecar ship together.

**`GET /api/analysis-settings`** ([the API](v2-sidecar-api.md#analysis-settings-e6)) serves the four with unit
`pixels`, the ranges above, and a new optional `pixelsPerCharacter` (9.4 or 6.2) on these four only. A widest's
description: "A title wider than this in search results is flagged as too long: Google's desktop results would likely
cut it off. Measured in Arial at 20 px, as Google shows titles." Errors read "This must be from 1 to 100,000 pixels."
and "The widest title allowed must be more than the narrowest (700)."

**`PlainLimits`.** A width is said as the ordinary characters it holds, never in pixels: `{limit:titleMaxPixels}` reads
"about 60 characters". The count is px ÷ the typical width, to the nearest 5 from 20 on and to the nearest whole below
(`PlainLimits.ApproximateCharacters`): 580 is about 60, 200 about 20, 985 about 160, 400 about 65. The plain words:

| Check, text | Before | Now |
| --- | --- | --- |
| `title-too-long` `plainWhat[0]` | "These pages have a title of more than {limit:titleMaxLength}." | "These pages have a title too long to show in full in search results, where there's room for {limit:titleMaxPixels}." |
| `title-too-short` `plainWhat[0]` | "…of fewer than {limit:titleMinLength}." | "These pages have a title shorter than {limit:titleMinPixels}." |
| `description-too-long` `plainWhat[0]` | "…of more than {limit:descriptionMaxLength}." | "…the text under their title in search results, too long to show in full." No limit |
| `description-too-long` `plainWhat[1]` | "Search results cut off the end of a long description (a "meta description")." | "Search results have room for {limit:descriptionMaxPixels}, and cut off the end of a longer description (a "meta description")." |
| `description-too-long` `plainFix[2]` | "Trim the rest to about {limit:descriptionMaxLength}, …" | "Trim the rest to {limit:descriptionMaxPixels}, …": the token says "about" |
| `description-too-short` `plainWhat[0]` | "…of fewer than {limit:descriptionMinLength}." | "…shorter than {limit:descriptionMinPixels}." |

**Settings > Analysis** (app, `analysisLimits.ts`): "Narrowest title", "Widest title", "Narrowest meta description" and
"Widest meta description", with `px` beside the field. The range line gives the default's characters: "From 1 to
100,000 px. The engine's is 580 px (about 60 characters)." Under the field, what's typed reads "About 60 characters of
ordinary text." (the value ÷ `pixelsPerCharacter`, rounded as `PlainLimits` rounds); an empty or invalid field says
nothing.

## 7. Columns, evidence and the inspector

**`url_summary`** (`CrawlSchema`, `UrlSummaryBuilder.COLUMNS`, `UrlSummaryReader`):

- `title_length` and `description_length` now hold **characters**, as a reader counts them: user-perceived, white
  space collapsed, nothing counted twice. That is Screaming Frog's "Title 1 Length".
- `title_width` and `description_width`, appended after `used_on`, hold **pixels**, as the checks measure them:
  Screaming Frog's "Pixel Width". `UrlSummary` has `TitleWidth` and `DescriptionWidth`.

**URL grid columns** ([the API](v2-sidecar-api.md#urls-e3a)):

| Column | Type | Sorts, filters | Is |
| --- | --- | --- | --- |
| `titleLength`, `descriptionLength` | number | yes | Characters, as above ("Title length", "Description length") |
| `titleWidth`, `descriptionWidth` | number, `px` | yes | The width the checks judge ("Title width", "Description width") |
| `titleInResults`, `descriptionInResults` | text, computed | no, shown only | The text up to the cut with "…", or the whole text when it fits ("Title in results", "Description in results") |

The two computed columns are worked out in `UrlComputedColumns.Fill` from `responses.title` and
`responses.meta_description`, against the analysis's stored `TitleMaxPixels` and `DescriptionMaxPixels` (the engine's
defaults before the first analysis), only when asked for and only for the rows of the page, as `redirectChain` is.

**Evidence** (`IssueCatalog`; [the per-check table](v2-evidence.md#titles), rows 44, 45, 49 and 50):

| Check | Evidence |
| --- | --- |
| `title-too-long` | `title`, `titleInResults`, `titleWidth`, `titleLength` |
| `title-too-short` | `title`, `titleWidth`, `titleLength` |
| `description-too-long` | `description`, `descriptionInResults`, `descriptionWidth`, `descriptionLength` |
| `description-too-short` | `description`, `descriptionWidth`, `descriptionLength` |

The report leaves out what the detail already says (`ReportRows.SAID_BY_THE_DETAIL`): the two lengths and the two
widths.

**The inspector** (`/urls/inspect`, [the API](v2-sidecar-api.md#the-inspector-e3a)):

- `content`'s titles, descriptions and H1s are `MeasuredText`, `{ text, length, width }`: characters, and the width in
  pixels for a title or description; `width` is null for an H1, which no result shows.
- `limits` has `titleMinPixels`, `titleMaxPixels`, `descriptionMinPixels` and `descriptionMaxPixels` in place of
  `titleMin`, `titleMax`, `descriptionMin` and `descriptionMax` (`ContentLimits`).
- Advanced's meter is "Title width": "612 px" on "200 to 580 px", "32 over the 580 limit", with "64 characters" under
  it; "Meta description width" alike. Its "counting full-width characters as two" words and `characterCount` went.
  Simple has no inspector, so no px meter reaches it.

## 8. The report and Simple's words

In plain words, these four checks' examples in the report say what Simple says, in place of the detail
(`ReportRows.PLAIN_DETAIL`, `ReportWords.CutOff` and `OnlyCharacters`); technical words keep the px detail.

- **Too long:** "Google will likely cut this title off after “…Garden Sofa”", and the same for "this description". The
  quote is the last whole words of `titleInResults` without its "…", up to 30 characters (`CUT_QUOTE_MAX`); it opens
  with "…" only when more comes before, never on a separator left from the words before (`587b89c`: "…Phone, Email or
  Contract", not "…| Phone, Email or Contract"), and a word longer than 30 characters is quoted by its end.
- **Too short:** "Only 10 characters long", from `titleLength` or `descriptionLength`.
- **Simple** (app `plainEvidence.ts`: `plainCutOff`, `plainDetail`) says the same in a page's line in "Where it is" and
  in Copy the list. The widths and the text in results are among the evidence a Simple line leaves out, so Simple never
  shows px; an e2e on the real sidecar holds that on the synthetic crawl.

## 9. Freshness

`AnalysisFreshness.VERSION` is 12: "12 measures titles and descriptions in pixels, as Google's desktop results show them
(Arial at 20 px and 14 px, `titleMaxPixels` and the rest), with details such as `612 px, over 580 (64 characters)`, and
stores `title_width` and `description_width` in the URL summary, with `title_length` and `description_length` in plain
characters." Every stored analysis is out of date (`EngineChanged`) until analysed again; nothing is rewritten in place.
These checks are Notices, so the score rarely moves.

## 10. Synthetic site

**Existing plants, measured again:** `title-long` 853 px and `title-short` ("Short page") 98 px stay flagged;
`title-wide` (21 Japanese characters, 420 px) passes and `title-wide-long` (48, 960 px) is flagged;
`description-long` (1,084 px) and `description-short` (63 px) stay flagged; `self-canonical/`'s "Self canonical"
(125 px) stays flagged.

**New plants, where characters and pixels disagree** (`SyntheticSite.Plants.cs`, `4268406`):

| Slug | Text | Characters | Pixels | Expected |
| --- | --- | ---: | ---: | --- |
| `title-caps` | "WHOLESALE PATIO HEATERS: MODERN LANTERNS AND OUTDOOR LIGHTS" | 59 | 736 | `title-too-long` |
| `title-narrow` | "little lilies, fritillaries and tulips: a field list of all the lilies in flower" | 80 | 561 | none |
| `title-narrow-short` | "lilies tilt; little jill fills it" | 33 | 184 | `title-too-short` |
| `title-caps-short` | "WOODEN GARDEN SHEDS" | 19 | 258 | none |
| `description-caps` | A description in capitals | 132 | 1,054 | `description-too-long` |
| `description-narrow` | A description of narrow letters | 172 | 826 | none |

## 11. Real sites: what moved

Copies of the step 3b audit's three crawls ([the check audit](v2-check-audit.md)), analysed again. Before is
characters, as stored; after is pixels.

| Site | `title-too-long` | `title-too-short` | `description-too-long` | `description-too-short` |
| --- | --- | --- | --- | --- |
| garden (www.gardenfurnituresets.co.uk) | 84 → 69 | 10 → 8 | 203 → 163 | 0 → 0 |
| aptitude (www.aptitude.co.uk) | 14 → 11 | 3 → 2 | 0 → 0 | 1 → 0 |
| jitsunami (jitsunami.com) | 0 → 0 | 6 → 3 | 2 → 2 | 1 → 1 |

- "Contact Us - GardenFurnitureSets | Phone, Email or Contract Form" is 64 characters and 595 px: flagged. Simple says
  "Google will likely cut this title off after “…Phone, Email or Contract”".
- "Tali 5 Seater Outdoor Corner Patio Sofa Set With Coffee Table" is 61 characters and 557 px: it passes now.
- Descriptions fall most: sentence case is narrow.
- Simple's fixture, the design's garden crawl of 31 October (another crawl), captured again: `title-too-long` 86 → 72,
  `title-too-short` 9 → 7, `description-too-long` 210 → 168, `description-too-short` 0 → 0. It still scores 87, Good.

## 12. Not modelled

Kerning (a few px on a title at most), bold query words in descriptions, mobile results, and Google's rewrites. The
font size may be one change behind (the 22 px sample in [the research](#1-what-google-does-research)).

No function was renamed, and `DisplayWidth` and the issue ids keep their names. Only the four settings and the
inspector's four limits took new names, because their unit changed.

## What was built, by commit

| Commit | What |
| --- | --- |
| `a6d57b7` | `SearchResultWidth`, the generated `ArialAdvanceWidths` table and its generator, the OFL text, tests |
| `7047d4b` | The four checks in px; the settings and their conversion; `PlainLimits` and the plain words; the sidecar's limits; `url_summary` and the grid's columns; evidence; the inspector; `VERSION` 12 |
| `96a14e1` | The report's plain examples: where Google will likely cut, or how many characters |
| `4268406` | The six new plants |
| `587b89c` | The quote never opens on a separator (the report and Simple) |
| `5affbb1` | About's licences list Liberation Sans's advance widths |
| `48d1010` | Settings > Analysis in px, with the characters hint |
| `2004b0d` | The URL grid's and Issues' width and in-results columns |
| `4c6a801`, `32f5461` | Simple's lines for the four checks, never px; the e2e on the real sidecar |
| `0ac98d5` | The inspector's px meter |
| `54d2e60`, `a6fea51` | Simple's and Issues' fixtures captured again from the pixel sidecar |

## Sources

Read for the design on 2026-11-17.

### Google

- [Title links](https://developers.google.com/search/docs/appearance/title-link)
- [Snippets](https://developers.google.com/search/docs/appearance/snippet)
- [The Aug 2021 title change](https://developers.google.com/search/blog/2021/08/update-to-generating-page-titles)

### Measurements of Google's results

- [Yoast's preview code](https://github.com/Yoast/wordpress-seo), `SnippetPreview.js` in search-metadata-previews
  (Arial 20/14, 600 px)
- [Firefox bug 1866588](https://bugzilla.mozilla.org/show_bug.cgi?id=1866588), Nov 2023 (20 px, line height 1.3)
- [The Sep 2026 measurement](https://github.com/adsandcode/serp-title-width) (22 px, 652 px, zh-TW)
- [Snippets lengthened, then shortened again](https://searchengineland.com/google-confirms-it-shortened-search-results-snippets-after-expanding-them-last-december-298196)
  (2018)
- [Sistrix](https://www.sistrix.com/?p=151231), Jan 2024, modified Jul 2025 (580 and 920 px)
- [Mangools](https://intercom.help/mangools), article 17450791 (960 and 680 px)

### Screaming Frog

- [Titles cut by pixel width](https://www.screamingfrog.co.uk/page-title-meta-description-lengths-by-pixel-width/), 2014
- [An update on pixel widths](https://www.screamingfrog.co.uk/an-update-on-pixel-width-in-google-serp-snippets/), May
  2014
- [SEO Spider 6.0](https://www.screamingfrog.co.uk/seo-spider-6-0/): the column went from 512 to 600 px
- [The issue pages](https://www.screamingfrog.co.uk/seo-spider/issues/): "Over 561 Pixels", "Below 200 Pixels", "Over
  985 Pixels", "Below 400 Pixels"
- [The tabs guide](https://www.screamingfrog.co.uk/seo-spider/user-guide/tabs/) (60/30 and 155/70 characters)
- [Configuration, Preferences](https://www.screamingfrog.co.uk/seo-spider/user-guide/configuration/)
- [The SERP snippet guide](https://www.screamingfrog.co.uk/guides/serp-snippet/) (desktop by default)
- Learn SEO, [page titles](https://www.screamingfrog.co.uk/learn-seo/page-title/) and
  [meta descriptions](https://www.screamingfrog.co.uk/learn-seo/meta-description/) (580 and 1,005 px, whole words, 2024)

### Fonts

- [Liberation fonts](https://github.com/liberationfonts/liberation-fonts) (OFL 1.1 from 2.0), with the 2.1.5 TTFs on
  [its releases page](https://github.com/liberationfonts/liberation-fonts/releases), and
  [on Wikipedia](https://en.wikipedia.org/wiki/Liberation_fonts)
- [Arimo, the Croscore fonts](https://en.wikipedia.org/wiki/Croscore_fonts)

### Our own

- The design's measurement: `audit3b/pixels-scratch/measure.py`, `thresholds.py` and `plants.py`, run over the three
  audit crawls (the session's scratchpad, not in the repo). The character counts matched the audit's exactly.
- The before and after counts above: copies of the three crawls analysed again by this branch's engine.
