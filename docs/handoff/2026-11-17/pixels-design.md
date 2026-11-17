# Title and description widths in pixels (step 3b, C8)

Draft for `docs/v2-pixel-widths.md` on branch `ui/3b-pixels`, written 2026-11-17 from `ui/step-3` at c6a9b40. Stage A only: research and
design, nothing built.

**The decision (owner, 2026-11-17).** "Should those checks measure pixel width, as Screaming Frog does, instead of characters?" "Yes."
`title-too-long`, `title-too-short`, `description-too-long` and `description-too-short` measure the width the text takes in Google's
desktop results, in pixels, and keep their ids. Advanced says `612 px, over 561 (64 characters)`. Simple says where Google will likely
cut it, with no pixels. Settings > Analysis takes pixels, with a hint in characters.

## 1. What Google does (research)

Google publishes no font, width or limit. It says titles and snippets are "truncated … as needed, typically to fit the device width",
and that it may rewrite title links. Everything below comes from tool vendors and third parties. Sources are at the end; **(U)** marks
what couldn't be confirmed.

| | Desktop | Mobile |
| --- | --- | --- |
| Title font | Arial 20 px, normal weight, line height 1.3 (Yoast's preview code; Firefox bug 1866588, Nov 2023). One sample from Sep 2026 (zh-TW interface) measured **22 px in a 652 px column** **(U)**: we may be one font change behind | Roboto or Google Sans, about 16 to 18 px, on up to 2 lines **(U)** |
| Title room | One line in a 600 px column (since Jul 2016, when it went from 512 px). Measured cut points: about 580 px (Sistrix, Jul 2025; Screaming Frog's learn page, 2024) | About 920 px over 2 lines (Sistrix); about 485 px (Screaming Frog's learn page) **(U)** |
| Description font | Arial 14 px (Yoast) **(U for 2026)** | Roboto 14 px **(U)** |
| Description room | About 2 lines. Tools use 920 px (Rank Math), 960 px (Mangools) and 985 to 1,005 px (Screaming Frog) | About 680 px (Mangools), up to 3 lines **(U)** |
| How it's cut | Google cuts the text itself and adds `...`, after the last whole word that fits (Screaming Frog, 2014 and 2024; the 2026 measurement). Titles were once cut mid-word by CSS (2014), and sometimes still are **(U)** | Same |

**Industry thresholds**

| Tool | Title | Description | Unit |
| --- | --- | --- | --- |
| Screaming Frog (defaults) | Over 561 px, below 200 px; 60 and 30 characters | Over 985 px, below 400 px; 155 and 70 characters | both: separate filters |
| Yoast | 600 px max, 400 px min | 156 and 120 characters | px for titles |
| Rank Math | 60 characters, about 580 px | 160 characters and 920 px | both |
| Portent | 600 px | 320 characters | mixed |
| Sistrix | 580 px desktop, 920 px mobile | n/a | px |
| Sitebulb | over 60, under 40 characters | over 320, under 110 characters | characters |
| Moz, Semrush, Ahrefs | no published pixel limit (Moz: 50 to 60 characters, about 600 px) **(U)** | n/a | n/a |

**Why 561 and 985 (inferred, (U)).** Screaming Frog publishes neither its font nor its reasons; it calls its limits "an approximation"
that "Google adjusts frequently". Two things make Arial at 20 px and 14 px the likely basis:
- **Titles.** 600 px less Google's ` ...` (5.6 + 16.7 px at 20 px) is 578 px, close to the measured 580. 561 leaves a further 17 px
  of margin.
- **The character defaults line up.** Measured in Arial 20/14, real titles average 9.1 to 9.4 px a character and descriptions 6.0 to
  6.3 (our three audit crawls, §9). So 561 px is about 60 characters and 985 px about 158: Screaming Frog's own 60 and 155.

## 2. Measuring: `SearchResultWidth`

A new `Analysis/Rules/SearchResultWidth.cs` replaces `DisplayWidth.Measure`. `DisplayWidth` keeps only its UAX #11 wide-range test, which
the new code reuses. The engine's three callers move to the new class: `TitleRules`, `DescriptionRules`, `UrlSummaryBuilder` and
`UrlInspectorReader`.

```csharp
internal enum SearchResultText { Title, Description }           // 20 px and 14 px Arial
internal static SearchResultMeasure Measure(string text, SearchResultText kind); // (int Characters, int Pixels)
internal static string? Cut(string text, SearchResultText kind, int limitPixels); // the part that fits, or null when all of it does
```

- **The text.** Runs of white space become one space and the ends are trimmed, as a browser renders it. This is new: today the text is
  only trimmed. A no-break space stays. Characters are user-perceived characters (`StringInfo` text elements), as today.
- **The table.** `ArialAdvanceWidths.cs`, generated, holds the advance widths of every code point Liberation Sans 2.1.5 Regular maps. Its
  units are 2048 to the em, as Arial's are. Private-use code points and those UAX #11 calls wide are left out.
  - **Why Liberation Sans.** It is metric-compatible with Arial: the same advance widths by design. It is licensed SIL OFL 1.1 (from
    2.0, July 2012), so its metrics can ship. Arial is Monotype's, so its file isn't used.
  - **Storage.** Runs of consecutive code points, binary-searched as `DisplayWidth.WIDE` is: about 5 KB of `ushort`s.
  - **Generator.** `engine/tests/ShoutingIguana.Engine.Tests/Analysis/ArialWidths/generate.mjs`, in the precedent of the plain-words
    generator. It is plain Node: it reads `cmap` (formats 4 and 12) and `hmtx`. It takes the TTF's path as an argument, and the file
    header records the font's version and SHA-256.
  - **Licence.** The OFL text sits beside the table. The licences script (`app/scripts/licences.mjs`) gains one `font` entry, "Liberation
    Sans 2.1.5 (advance widths)", so About lists it.
- **Each user-perceived character:**

  | The character | Width |
  | --- | --- |
  | Its first code point is wide (UAX #11 W/F, the existing ranges), or it holds U+FE0F, or it is a ZWJ emoji sequence or a flag pair | **1 em**, whole: 20 px in a title, 14 px in a description |
  | Its first code point is in the table | That advance, plus the advances of the others in the table. Combining marks (Mn, Me) and format characters (Cf: ZWJ, ZWNJ, soft hyphen, bidi marks) are 0 |
  | Anything else: Thai, Devanagari, Arabic and so on, which Chrome draws from fallback fonts | **1139/2048 em once**, the width of `n`. A rough average **(U)** |

- **Pixels.** `round(Σ advances × font px ÷ 2048)`, rounding halves up, as a whole number. The rule compares that same integer, so a
  detail never reads "561 px, over 561".
- **Not modelled.** Kerning (a few px on a title at most), bold query words in descriptions, mobile, and Google's rewrites.
- **The font sizes are constants, not settings.** `TITLE_FONT_PX = 20` and `DESCRIPTION_FONT_PX = 14` sit in one place. If Google moves
  to 22 px, that's an engine change with an `AnalysisFreshness` bump. The limits are what the user turns.
- **A typical character's width**, for the characters hint, the migration and the plain words: **9.4 px in a title, 6.2 px in a
  description**. These are measured: the averages over the three audit crawls' 293 titles and 275 descriptions (9.11 to 9.43 and 5.99 to
  6.27). They are constants beside the font sizes (`TYPICAL_TITLE_CHARACTER_PX`, `TYPICAL_DESCRIPTION_CHARACTER_PX`).
- **Spot values a test holds**, from Arial and the same in Liberation Sans by its design: `W` 1933, `i` 455, space 569, `n` 1139, `.`
  569, `…` 2048. Measured from `C:\Windows\Fonts\arial.ttf` for calibration only; nothing from it ships.

## 3. Where Google will likely cut (`Cut`)

The cut is used by Simple's sentence and by the `titleInResults` and `descriptionInResults` columns.

1. Walk the user-perceived characters, adding widths. While the running total stays within the limit, note each **break**:
   - before a space;
   - between two characters where either is wide, since CJK text breaks between characters (UAX #14's ID class, near enough).
2. At the first character that takes the total over the limit:
   - if there was a break, the visible part ends at the last one;
   - if not (a single word or URL longer than the limit), it ends mid-word, at the last character that fits.
3. Trim the visible part's trailing white space and one dangling separator (`|`, `-`, `–`, `—`, `·`, `:`, `,`, `&`, `/`). So the
   reader sees "…Chair Set", not "…Chair Set &".
4. Text within the limit returns null: nothing is cut.

**The limit is the setting, not the 600 px column.** A title is flagged exactly when its width is over `titleMaxPixels`, and `Cut`
uses the same number, so every flagged title has a cut and no unflagged one does. The 39 px between 561 and 600 covers Google's
` ...` (22 px) with margin. A user who raises the limit to 600 moves the cut with it.

## 4. The checks

- **`TitleRules` and `DescriptionRules`.** The same structure, with `measured.Pixels` compared against `TitleMaxPixels`,
  `TitleMinPixels`, `DescriptionMaxPixels` and `DescriptionMinPixels`. The ids, severities (Notice), categories, subject and grouping
  are unchanged, so `ChecksFingerprint` is unchanged too.
- **Details (Advanced, stored per issue):**
  - `612 px, over 561 (64 characters)`
  - `150 px, under 200 (31 characters)`

  These come from `RuleText.Over` and `RuleText.Under` with a new `RuleText.Pixels(int)` ("612 px"), and `Characters(n)` in brackets.
  `RuleText.Width` and its "counting full-width characters as two" wording go: pixels make them unnecessary, and the audit called
  them noise (accuracy-onpage, description-too-long 2). The longest detail is about 45 characters, inside `DETAIL_MAX`.
- **Catalog text.** `Description` and `Why` are rewritten (they aren't fingerprinted), for example `title-too-long`:
  - **Description:** "The page's title is wider than 561 pixels (the default, which can be changed in the analysis settings), measured as Google's
    desktop results show titles: Arial at 20 px, with full-width characters such as Chinese, Japanese and Korean, and emoji, one
    em wide."
  - **Why:** keeps the "no limit, truncated to fit the device width, rewrites" core, and adds: "Width depends on the letters ('WWW'
    is more than four times as wide as 'iii'), so pixels predict the cut better than characters. 561 px is Screaming Frog's default
    and leaves room for Google's '...' in a 600 px column."

  The descriptions' text names Arial at 14 px; `too-short` is "narrower than 200 pixels".

## 5. Settings

**`AnalysisSettings`.** Four new properties replace the character ones. They get new names because the unit changed, as
`NearDuplicateMaxBits` did: an old crawl file's or settings file's `titleMaxLength: 60` must never be read as 60 px.

| Property (JSON) | Default | Unit | Range | Characters hint at the default |
| --- | --- | --- | --- | --- |
| `TitleMinPixels` (`titleMinPixels`) | 200 | pixels | 0 to 100,000 | about 20 |
| `TitleMaxPixels` (`titleMaxPixels`) | 561 | pixels | 1 to 100,000 | about 60 |
| `DescriptionMinPixels` (`descriptionMinPixels`) | 400 | pixels | 0 to 100,000 | about 65 |
| `DescriptionMaxPixels` (`descriptionMaxPixels`) | 985 | pixels | 1 to 100,000 | about 160 |

`Validate` keeps its pair rules, with "widths" for "lengths". The class summary and `README`'s "Thresholds" line now say pixels.

**Migrating the old character settings**

- **The sidecar's `analysis-settings.json`.** It holds only limits the user changed. `AnalysisSettingsStore.Load` maps the four legacy
  keys before it drops unknown ones, when the new key isn't there too:
  - titles: pixels = round(characters × 9.4);
  - descriptions: pixels = round(characters × 6.2).

  So a user's "70 characters" becomes 658 px, and the hint reads "about 70 characters": their intent survives. A converted value that
  breaks a range or a pair is passed over by the existing per-limit handling, which logs and keeps the rest. The next save writes only
  the new keys. One `LogInformation` line names the conversion.
- **Crawl files.** Nothing to rewrite.
  - An old analysis's stored settings name the old keys, which deserialise as unknown (skipped), so the pixel fields take their defaults.
  - That analysis is out of date anyway by `AnalysisFreshness.VERSION` (§8), so no `SettingsChanged` is raised by mistake.
  - Until it's analysed again, the inspector's limits for it are the pixel defaults, behind the out-of-date notice.
- **The API.** `PUT`, `validate` and `reset` with an old name is a 400, as any unknown property is. The app and the sidecar ship
  together.

**The sidecar: `AnalysisLimits` and `/api/analysis-settings`**

- The four entries become `titleMinPixels`, `titleMaxPixels`, `descriptionMinPixels` and `descriptionMaxPixels`, with unit `pixels`
  and `MAX_PIXELS = 100_000`. `PAIRS` is updated.
- `AnalysisLimit` gains an optional `PixelsPerCharacter` (9.4 or 6.2), served as `limits[i].pixelsPerCharacter` on the pixel limits
  only. It is a new, optional field.
- Descriptions:
  - "A title wider than this in search results is flagged as too long: Google's desktop results would likely cut it off. Measured in
    Arial at 20 px, as Google shows titles."
  - "A title narrower than this is flagged as too short."
  - And the same for descriptions, at 14 px.
- Errors read "This must be from 1 to 100,000 pixels." and "The widest title allowed must be more than the narrowest (200)."

**`PlainLimits`.** A new `LimitUnit.Pixels`, which `Describe` renders as an approximate character count, never as pixels:
- `{limit:titleMaxPixels}` reads "about 60 characters".
- The count is px ÷ the typical width, rounded to the nearest 5 at 20 or more, to the nearest whole below that.
- So 561 is "about 60 characters", 200 "about 20", 985 "about 160" and 400 "about 65".

The test that every numeric threshold has a name keeps holding.

**The app: Settings > Analysis**

| | Now | New |
| --- | --- | --- |
| `NAMES` | "Shortest title", "Longest title" | "Narrowest title", "Widest title", "Narrowest meta description", "Widest meta description" |
| Unit beside the field | characters | `px` |
| Description line | as served | as served, then a live hint from the typed value: "About 60 characters of ordinary text." (value ÷ `pixelsPerCharacter`, rounded as `PlainLimits` does). An empty or invalid field shows no hint |
| `UNIT_WORDS` | | gains `pixels: ['px', 'px']` |
| `rangeWords` | | "From 1 to 100,000 px. The engine's is 561 px (about 60 characters)." |

## 6. Columns, evidence, inspector, report

**`url_summary`** (rebuilt by every analysis; schema in `CrawlSchema`, `UrlSummaryBuilder.COLUMNS`, `UrlSummaryReader`):
- `title_length` and `description_length` now hold **characters**: user-perceived, after the white space collapses, with no double
  counting. That is Screaming Frog's "Title 1 Length".
- New `title_width` and `description_width` columns hold **pixels**: Screaming Frog's "Pixel Width".
- `UrlSummary` gains `TitleWidth` and `DescriptionWidth`.

**URL grid columns** (`UrlColumns`, `ColumnWords`):
- `titleLength` and `descriptionLength` keep their ids, now in characters. Titles "Title length", "Description length".
- New `titleWidth` and `descriptionWidth`: Number, `SummarySql` `s.title_width`, sortable and filterable, unit `px` in `UNITS`, titled
  "Title width" and "Description width".
- New `titleInResults` and `descriptionInResults`: Text, `ColumnSource.Computed`, neither sortable nor filterable, titled "Title in
  results" and "Description in results".
  - The value is the text up to `Cut` with "…" added, or the whole text when it fits.
  - They are worked out in `UrlComputedColumns.Fill` from `responses.title` and `responses.meta_description`, against the analysis's
    stored `TitleMaxPixels` and `DescriptionMaxPixels` (the engine's defaults before the first analysis). That's the same read cost
    as `redirectChain`: only when asked, only for the rows of the page.

**Evidence** (`IssueCatalog`, docs/v2-evidence.md rows 44, 45, 49 and 50):

| Check | Evidence |
| --- | --- |
| `title-too-long` | `title`, `titleInResults`, `titleWidth`, `titleLength` |
| `title-too-short` | `title`, `titleWidth`, `titleLength` |
| `description-too-long` | `description`, `descriptionInResults`, `descriptionWidth`, `descriptionLength` |
| `description-too-short` | `description`, `descriptionWidth`, `descriptionLength` |

`ReportRows.SAID_BY_THE_DETAIL` gains the two widths.

**Inspector** (`/urls/inspect`, `PageContent`, `ContentLimits`, app `scale.ts`):
- `MeasuredText` is `(Text, Length, Width)`: characters and pixels.
- `ContentLimits`' `TitleMin`, `TitleMax`, `DescriptionMin` and `DescriptionMax` become `TitleMinPixels` and so on. The same unit
  reason applies, and only the app reads them.
- Advanced's title meter is in px: "612 px", "200 to 561 px", "51 over the 561 limit", with "64 characters" under it.
- Wherever Simple shows a page's title size, it's the cut sentence or characters, never px. Stage B checks whether Simple renders the
  inspector meter.

**Report** (`ReportDocument`, `ReportRows`):
- When the problem's words are plain (`Words.IsPlain`), these four checks' examples carry the Simple sentence (§7) in place of the
  detail. The engine's `ReportWords` gets the same two sentences as the app.
- Technical words keep the px detail.

## 7. Simple's words

**A page's line in "Where it is"** (app: `plainEvidence.ts`, `WhereItIs.tsx`, flat rows). For these four checks Simple shows a plain line
instead of the engine's detail. A small map keyed by check id does it, in the precedent of `OTHER_NOUNS`:
- **title-too-long:** `Google will likely cut this title off after “…Garden Sofa”`
  - The quote is the last whole words of `titleInResults`, without our "…", up to 30 characters.
  - The opening "…" shows only when there's more before it.
  - A title cut mid-word (one long word, or CJK) quotes what fits.
- **description-too-long:** `Google will likely cut this description off after “…comes flat-packed for”`
- **title-too-short:** `Only 5 characters long` (from `titleLength`).
- **description-too-short:** `Only 17 characters long`.
- `SAID_ELSEWHERE` gains `titleWidth`, `descriptionWidth`, `titleInResults` and `descriptionInResults`, so no pixel value ever reaches a
  Simple line.

**The plain words** (`plain-words.json`, regenerated into `IssuePlainTexts.cs`; the tokens follow §5):

| Check | Now | New |
| --- | --- | --- |
| `title-too-long` What[0] | "These pages have a title of more than {limit:titleMaxLength}." | "These pages have a title too long to show in full in search results, where there's room for {limit:titleMaxPixels}." This reads "…room for about 60 characters." |
| `title-too-short` What[0] | "…fewer than {limit:titleMinLength}." | "These pages have a title shorter than {limit:titleMinPixels}." |
| `description-too-long` What[0] | | "…too long to show in full, where there's room for {limit:descriptionMaxPixels}." |
| `description-too-long` Fix[2] | | "Trim the rest to {limit:descriptionMaxPixels}, or leave it if the opening already works." The token supplies the "about" |
| `description-too-short` What[0] | | "…shorter than {limit:descriptionMinPixels}." |

Titles and "passed" lines are unchanged: "Titles that may be cut off in search results" already fits.

## 8. Freshness, schema, merging

- **`AnalysisFreshness.VERSION`** takes the next free number at merge time: 12 on today's `ui/step-3`.
  - `ui/3b-newchecks` will raise it too. Version 9 was used twice before; the merge takes one number for both, as 10 did.
  - The comment: "12 measures titles and descriptions in pixels (Arial 20 px and 14 px, `titleMaxPixels` and the rest), details
    `612 px, over 561 (64 characters)`, and stores `title_width` and `description_width` in the URL summary, with `title_length` in
    plain characters."
- **What goes out of date.** Every stored analysis (`EngineChanged`). Counts, details, the summary's length columns and the score
  (these are Notices, so rarely points) change on the next analysis. Nothing is rewritten in place.
- **Merge conflicts to expect with `ui/3b-newchecks`.** That branch's own thresholds are to become settings, so expect conflicts in
  `AnalysisSettings`, `AnalysisLimits`, `PlainLimits`, the `analysis-settings` doc table, `UrlSummaryBuilder.COLUMNS` and the catalog.
  This branch should land first or second, never interleaved.

## 9. Real sites: what moves

Measured independently (Python over the stored crawls, Arial metrics, the rules above; `audit3b/pixels-scratch/measure.py`). The
character counts match the audit's exactly, which cross-checks the script.

| Site (indexable pages) | title-too-long, chars > 60 → px > 561 | title-too-short, < 30 → < 200 | description-too-long, > 155 → > 985 | description-too-short, < 70 → < 400 |
| --- | --- | --- | --- | --- |
| garden (257) | 84 → **97** (75 in both) | 10 → 8 | 203 → **163** (162 in both) | 0 → 0 |
| aptitude (26) | 14 → 12 | 3 → 2 | 0 → 0 | 1 → 0 |
| jitsunami (10) | 0 → 0 | 6 → 3 | 2 → 2 | 1 → 1 |

- **garden's titles go up, not down.** Its titles are Title Case product names, about 9.4 px a character against Screaming Frog's
  implied 9.35. The audit hoped otherwise: 51 of its 60 titles of 61 to 65 characters are still over 561 px.
  - Examples: "Tali 5 Seater Outdoor Corner Patio Sofa Set With Coffee Table" is 61 characters and 557 px, so it passes now. "Brisbane Wooden
    Garden Storage Tool Shed With Sloped Roof" is 57 characters and 565 px, so it's flagged now.
- **Descriptions fall by 40** (203 → 163): sentence case is narrow.
- **Other title limits, for comparison:** garden has 69 titles over 580 px and 39 over 600.

## 10. Synthetic site and tests

**Existing plants, re-measured** (Arial):
- `title-long` is 853 px and stays flagged.
- `title-short` ("Short page") is 98 px and stays flagged.
- `title-wide` is 21 CJK characters, 420 px: it passes.
- `title-wide-long` is 48 CJK characters, 960 px: flagged.
- `description-long` is 1,084 px and `description-short` 63 px: both stay.
- `self-canonical/`'s "Self canonical" is 125 px and stays flagged.

**Default page titles and descriptions:**
- The widest indexable default plant title is `fragment-elements` at 554 px, 7 px under the limit. Stage B asserts it.
- `noindex-canonical` (591 px) and `robots-spaces` (577 px) are wider, but they're noindex, so they're never judged.
- Default plant descriptions are 644 to 815 px.
- Products ("Product N: w w | Synthetic Store") come to about 330 to 480 px.
- Product descriptions (14 words) come to about 450 to 870 px.
- Home, category and hub pages are 343 to 700 px.

So the 100k bench should flag nothing new. That is to be proven by the bench, not this estimate.

**New plants: where characters and pixels disagree.** Exact texts are picked in stage B by measuring.

| Slug | Title or description | Characters | Pixels | Expected |
| --- | --- | --- | --- | --- |
| `title-caps` | A title in wide capitals | ≤ 60 | > 561 | `title-too-long` |
| `title-narrow` | A title of narrow letters (`il1ft.`) | > 60 | ≤ 561 and ≥ 200 | none |
| `title-narrow-short` | A title of narrow letters | ≥ 30 | < 200 | `title-too-short` |
| `title-caps-short` | A title in capitals | < 30 | ≥ 200 | none |
| `description-caps` | A description in capitals | ≤ 155 | > 985 | `description-too-long` |
| `description-narrow` | A description of narrow letters | > 155 | ≤ 985 and ≥ 400 | none |

**Engine unit tests**
- `SearchResultWidthTests`:
  - the table's spot values and an independent sum;
  - white space collapsing; combining marks and ZWJ as 0;
  - a family emoji and a flag pair as 1 em; CJK as 1 em; the fallback width; rounding at .5;
  - `Cut` at a word, between CJK characters, mid-word with no break, dropping a dangling `|`, and null within the limit.
- `PageRuleTests`: the details.
- `PlainLimitsTests`: "about 60 characters".
- Sidecar `AnalysisSettingsTests`: legacy keys converted, an invalid conversion passed over, `pixelsPerCharacter` served.
- `AnalysisFreshnessTests`: the version.
- Grid tests: `titleWidth` sorts and filters, `titleInResults` follows a changed limit.
- `ReportDocumentTests`: the plain sentence.
- Stored fixtures and expected counts in `CliTests`, `GridEndpointsTests`, `ExportEndpointsTests` and others that quote "characters,
  over 60" are updated.

**App tests.** `analysisLimits` (names, hint, range words), `plainEvidence` (the cut sentence and its tail), `WhereItIs` (Simple shows
the sentence, never px), `scale` (px meter). Simple's and Advanced's fixtures are captured again from the sidecar, as c6a9b40 did.

**Docs.** `v2-sidecar-api.md` (analysis settings table and JSON, URL columns, inspector limits), `engine/README.md` (thresholds,
`url_summary`), `v2-evidence.md`, `v2-new-checks.md` C8 (built), `v2-comparison.md` "In part (7)" (the four pixel filters now
matched), and `v2-rebuild.md`'s status.

## 11. Stage B order

1. **Engine.**
   - The generator and table, then `SearchResultWidth` with its tests.
   - The rules, details and catalog text.
   - `AnalysisSettings`, `PlainLimits` and the plain words.
   - `url_summary` and the columns; the computed columns; the inspector; the report.
   - `VERSION`.
   - The plants, then a full `dotnet test`.
2. **Sidecar.** `AnalysisLimits`, the store's migration, the API doc.
3. **App.** Settings > Analysis, Simple's lines, the inspector meter, fixtures, then vitest and e2e.
4. **Gates.** The local checks: the verify scripts, `smoke:shell`, the Linux Docker check, and `si bench --pages 100000`.

## Sources

**Google**
- Title links: https://developers.google.com/search/docs/appearance/title-link
- Snippets: https://developers.google.com/search/docs/appearance/snippet
- The Aug 2021 title change: https://developers.google.com/search/blog/2021/08/update-to-generating-page-titles

**Measurements of Google's results**
- Yoast's preview code, `SnippetPreview.js` (Arial 20/14, 600 px): https://github.com/Yoast/wordpress-seo (search-metadata-previews)
- Firefox bug 1866588, Nov 2023 (20 px, line height 1.3): https://bugzilla.mozilla.org/show_bug.cgi?id=1866588
- The Sep 2026 measurement (22 px, 652 px, zh-TW): https://github.com/adsandcode/serp-title-width
- Snippets lengthened, then shortened again (2018): https://searchengineland.com/google-confirms-it-shortened-search-results-snippets-after-expanding-them-last-december-298196
- Sistrix, Jan 2024, modified Jul 2025 (580 and 920 px): https://www.sistrix.com/?p=151231
- Mangools (960 and 680 px): https://intercom.help/mangools (article 17450791)

**Screaming Frog**
- 2014, titles cut by pixel width: https://www.screamingfrog.co.uk/page-title-meta-description-lengths-by-pixel-width/
- An update on pixel widths, May 2014: https://www.screamingfrog.co.uk/an-update-on-pixel-width-in-google-serp-snippets/
- SEO Spider 6.0, the column went from 512 to 600 px: https://www.screamingfrog.co.uk/seo-spider-6-0/
- The issue pages ("Over 561 Pixels", "Below 200 Pixels", "Over 985 Pixels", "Below 400 Pixels"): https://www.screamingfrog.co.uk/seo-spider/issues/
- The tabs guide (60/30 and 155/70 characters): https://www.screamingfrog.co.uk/seo-spider/user-guide/tabs/
- Configuration, Preferences: https://www.screamingfrog.co.uk/seo-spider/user-guide/configuration/
- The SERP snippet guide (desktop by default): https://www.screamingfrog.co.uk/guides/serp-snippet/
- Learn SEO, page titles and meta descriptions (580 and 1,005 px, whole words, 2024): https://www.screamingfrog.co.uk/learn-seo/page-title/ and https://www.screamingfrog.co.uk/learn-seo/meta-description/

**Fonts**
- Liberation fonts (OFL 1.1 from 2.0; the 2.1.5 TTFs): https://github.com/liberationfonts/liberation-fonts and https://en.wikipedia.org/wiki/Liberation_fonts
- Arimo, the Croscore fonts: https://en.wikipedia.org/wiki/Croscore_fonts
- Roboto, now OFL: https://github.com/google/fonts/tree/main/ofl/roboto

**Our own**
- Our own measurement: `audit3b/pixels-scratch/measure.py`, `thresholds.py` and `plants.py`, run over the three audit crawls.
