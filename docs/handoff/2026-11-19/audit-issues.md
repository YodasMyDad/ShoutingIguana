# UX audit, 2026-11-19: Advanced > Issues

Zone: `/crawls/<id>/issues` and `/issues/<check-id>`, Advanced mode. Judged at 2560 × 1380 first, then 1280 × 720 for
layout. Driven with `drive.mjs` (headless Chromium); nothing in `app/` or `engine/` was changed, no crawl or recheck
was run.

- Shots: `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\da833503-3c78-46eb-9e8a-d618294b5af4\scratchpad\audit\shots\issues-*.png`
- Flows and logs: `...\scratchpad\audit\scripts\issues\` (`i03`–`i14`, `walk.mjs`; logs `i05.short.log` garden walk,
  `i08.log` every synthetic check's table shape, `i09.log` synthetic walk).
- Headless Chromium hides scrollbars, so every "horizontal scroll" below is measured (`scrollWidth` against
  `clientWidth`), not seen as a bar. The owner's WebView draws the bar.
- Requests to anything but localhost were blocked in my flows, so "Open in the browser" was caught at `window.open`
  and image thumbnails never loaded.

## 1. Tasks tried

### T1. The owner's complaint: the 404s and where the broken links are (garden crawl)

| # | Click | What happened | Shot |
| - | ----- | ------------- | ---- |
| 1 | Issues in the side nav | Lands on Issues with "Internal URL returns 4xx" already picked and its one group open. Table starts at y ≈ 800 of 1380. | `issues-02-404-arrive` |
| 2 | Single click on the grey `Found on 1 page · /blog/best-rattan-patio-sets/ (Content · "Buy Now")` | Nothing navigates. The row takes a faint blue tint (`aria-selected`). Cursor is the arrow. The whole line is one `<span>` with a `title`. | `issues-03-404-single-click` |
| 3 | Single click on the 404's own address | Same: selects the row, nothing else. The address is black monospace text, not a link. | `issues-04-404-click-address` |
| 4 | Click the 14 px chevron left of the address | Row expands: `Linked from  /blog/best-rattan-patio-sets/ … Conte` and a second sub-row `Show the link in Links`. The anchor text and "Followed" are pushed to the right edge and cut ("Conte"). | `issues-05-404-expanded-clip` |
| 5 | Single click on the "Linked from" address | Nothing. It is plain text too. | `issues-06b-404-subrow-clicked` |
| 6 | Hover the sub-row | An open-in-browser icon appears at x = 2422, about 1,670 px to the right of the address it belongs to, hard against the clipped "Conte". | `issues-06-404-subrow-hover` |
| 7 | Click that icon | `window.open(https://www.gardenfurnituresets.co.uk/blog/best-rattan-patio-sets/)`. | |
| 8 | "Show the link in Links" | Links screen filtered to that one link (source, target, anchor, 404). Here both URLs are blue and clickable. | `issues-08-404-in-links` |
| 9 | Back | Back on the check, but the row I had expanded is collapsed again. | `issues-09-404-back` |

Counts: the linking page is *named* at zero clicks but can't be used. Opening it live = 2 clicks plus a hover hunt
(expand, find the far-right icon). Inspecting it = expand, then double-click the sub-row (works, nothing says so,
`issues-06c-404-subrow-dblclick`) or: double-click the 404 row, Inlinks tab, click the inlink = 3. Seeing the link as a
row in Links = 2, on another screen. Reproduced exactly as the owner described, plus two things they didn't say: the
table is 170 px wider than its frame (A-02), and Back forgets the expansion (A-07).

### T2. "Which pages link to broken URLs?" from the mirror check (Links to broken internal URLs)

Opens with 8 collapsed groups for 4 pages ("4 issues in 8 groups, by related URL"), each group a broken target. The
page that needs editing is inside each group: 8 chevron clicks to read 8 lines (`issues-g-broken-internal-links`).
Single click on a group does nothing; double-click expands it (on a page row double-click inspects: same gesture, two
meanings). The group's right-click menu has "Copy the group's URL / Copy the row / Show the page in URLs": no Inspect,
no Open, no Links (`issues-24-group-rowmenu`).

### T3. Fix oversized images (Large images, 60 pages, 129 groups)

Grouped by image file; file size is in the row (good). The page that embeds it is one expansion down. "Pixels" is "–"
on every row of the garden crawl. Two scroll areas: the detail card (1,086 px showing 1,598) and the table inside it
(881 px showing 4,160). `issues-g-image-too-large`.

### T4. Titles too long (flat table)

Evidence is good: `595 px, over 580 (64 characters)`, the title, and the title as results would cut it. But the URL
column is 325 px with addresses cut in the middle (`/groby-outdo…abric-hanging-egg-chair/`) beside ~400 px of blank
before "Inlinks", and nothing on the row is visibly clickable (`issues-g-title-too-long`). Double-click inspects.

### T5. Find a check, compare with the last crawl, hide one

Filter by name works ("title" → 3). "404" finds nothing (A-11). The change chips work but are unexplained and vanish
when one is used (A-12). Hide → menu "Hide in this crawl / Hide in every crawl" → toast with Undo, "Show 1 hidden
check", header sentence updated: this one is done well (`issues-39-hidden`).

### T6. Inspector from a row and back

Double-click / Enter / the far-right `>` opens the inspector as a 400 px side panel; Escape closes it and keeps the
selection. Browser/app Back does not close it: it leaves the check for the previously viewed check (A-07). Hopping to
the linking page inside the inspector works (blue link in Inlinks), but the panel has no back control of its own.

## 2. Findings, ranked

### A-01 · Check detail, every table · **Blocks** · "Found on …" names the page to fix and is dead text

Did: clicked the grey "Found on 1 page · /blog/… (Content · "Buy Now")" in a 4xx row, the same line in the flat
tables' "Found on" column (Linked only from non-indexable pages, Indexable URL with parameters, redirect loop, URL
checks), and the address in the banner "/blog/best-rattan-patio-sets/ links to 5 of these 8 URLs". None responds.
Shots `issues-03-404-single-click`, `issues-22-flat-foundon`, `issues-02-404-arrive`.
Code: `app/src/screens/issues/WhereFoundLine.tsx:41` (one truncated `<span>`), `IssueRowsGrid.tsx:278-292` (the
"Found on" column), `CheckDetail.tsx:368-371` + `whereFound.ts:303` (banner is a string).
Fix: render the line as parts. The page is a link (click = inspect that page with its Outlinks narrowed to this
target; an always-visible open-in-browser icon beside it), the anchor text a quoted chip, "+15" a link that expands the
row. In the banner make the address the same link and add "Show its 5 links" → Links filtered to source = that page,
target status 4xx. In the flat tables split "Found on" into real columns: Linking page (link) · Anchor · Position.

### A-02 · Grouped tables on a compared crawl · **Blocks** · table is 170 px wider than its frame; Inspect control and the compare columns are off-screen

Did: measured every grouped (tree) table on the garden crawl: `scrollWidth 1976` in a 1,806 px frame at 2560; 1,116 in
946 at 1280; 1,568 in 1,398 with the inspector open. Columns: `Detail@687 w1696 | URLs@2383 w88 | 17 Nov@2471 w88 |
Change@2559 w104`. So "17 Nov" and "Change" (or "Share of issues") and each row's Inspect button (x = 2637 in a 2,560
viewport) are cut, and sub-row evidence is clipped ("Conte"). On the synthetic crawl (no earlier crawl) the same
tables fit exactly. Shots `issues-05-404-expanded-clip`, `issues-07-404-scrolled-right`, `issues-48-empty-anchor`.
Code: `IssueGroups.tsx:659-668` works out `room` for the words column from the width minus pages/share/size/pixels
columns, but never subtracts `THEN_WIDTH` (88) and `MOVE_WIDTH` (104) (`:128-129`) that `:864-896` add when `compare`
is set. 88 + 104 − `FRAME_SLACK` = 170.
Fix: subtract both when `compare` is set. Add a test that the columns' sum never exceeds the frame.

### A-03 · Every table · **Slows** · a single click shows nothing; inspecting is double-click, Enter, right-click, or a `>` that looks like "expand"

Did: single-clicked rows in tree and flat tables: selection tint only. Double-click and Enter inspect; Shift+Enter
opens in the browser; ArrowRight expands. None of it is said on the screen (Links has a key legend strip; Issues has
none: `issues-08-404-in-links`). The tree's only visible inspect control is a bare `>` at the far right, ~1,700 px
from the address and the same glyph as the expand chevron on the left (`issues-42-synth-links-to-redirects`); on the
garden crawl it is off-screen (A-02). Flat tables have no inspect control at all (`issues-20-flat-row-click`).
Code: `IssueGroups.tsx:1016-1026` (the `>` button), `grid/GridCore.tsx:980,1186` (double-click), `:709` (Shift+Enter).
Fix: single click on a row opens the inspector (it's a side panel; nothing is lost). Replace the `>` with a labelled
"Details" control next to the address, and give Issues the same key legend as Links.

### A-04 · Every table · **Slows** · URLs in Issues neither look nor act like the URLs in Links and the inspector

Did: compared. Links screen and inspector Inlinks: blue, click = inspect, open icon beside (`issues-08`,
`issues-13-inspector-inlinks`). Issues tables: black monospace, cursor arrow, click = select row; the open icon is
hidden until the row is hovered or selected, and in flat tables sits at the right end of the URL column.
Code: `ui/Url.tsx:134-257` (address is text; only the icon button acts), `styles/components.css:2234-2253` (icon shown
on hover/selection only), `IssueGroups.tsx:984-990` (`copyable={false}`).
Fix: one `Url` behaviour everywhere: the address is a link (click = inspect), the open icon always visible directly
after it, Ctrl+click = open in the browser.

### A-05 · Expanded sub-rows · **Slows** · the row that holds the answer has no usable control, clipped evidence and coded words

Did: expanded rows on both crawls. "Linked from [address]": address is text; anchor text and "Followed" are
right-aligned at the far edge (and clipped on garden); open icon hover-only at the far right. "Names [address] 200 …":
"Names" needed decoding (it means "this page links to / embeds"); evidence again right-aligned ~900 px from the
address (`issues-42`). "All 5 in the inspector" / "All 17 in the inspector" is grey text with no control: single click
nothing, double-click opens the page's inspector on Summary, not on the list it promises (`issues-43-all-in-inspector`).
"+2 other URLs", "+5" on the parent row are dead text too.
Code: `IssueGroups.tsx:1193-1211` (source), `:1215-1236` (target, "Names"), `:1258` + `whereFound.ts:205` ("All N in
the inspector").
Fix: sub-row as columns left-aligned under the parent: `Linked from` [page link + open icon] · anchor · position ·
follow. Rename "Names" to "Links to" / "Embeds" / "Canonical to" by evidence kind. Make "All 17 in the inspector" a
link that opens the inspector on Outlinks/Images filtered to this check.

### A-06 · Checks grouped "by related URL" · **Slows** · one-row-per-fault checks are drawn as trees of collapsed groups

Did: walked them. Links to broken internal URLs: "4 issues in 8 groups". Paginated page canonicalised to page 1: 3
groups of one page each, 3 expansions to read 3 lines. Links to internal redirects (synthetic): "1 issue in 6 groups",
the same page inside each. Image in an older format: 1,420 groups; Images without width and height: 3,661 groups. With
more than one group everything starts collapsed, so the faulty page is never visible on arrival. A group that is a URL
can't be inspected (double-click expands; menu has no Inspect/Open/Links); its status badge floats at a ragged x after
the address (`issues-g-broken-internal-links`).
Code: `IssueGroups.tsx` (group rows `:669-760`, activation `:1110-1114` handles only page/where/more),
`IssuePageMenu.tsx:122` ("Copy the group's URL").
Fix: default to a flat table for these: Page · Broken link (target, status) · Anchor · Position, with a "Group by
target" toggle. If the tree stays: open all groups when there are ≤ 20, make the group's address the standard link,
and give group rows Inspect / Open / "Show its links in Links".

### A-07 · Navigation · **Slows** · Back doesn't close the inspector or keep your place

Did: Overview → Issues → Large images → Title too long → double-click a row (inspector opens, `?inspect=11`) → Back:
lands on Large images, the check I was working on is gone. From 4xx: expand a row → "Show the link in Links" → Back:
the row is collapsed again. Hopping inside the inspector (Inlinks → click the page) does push history, so Back there
steps back one URL, but the panel itself has no back arrow. On the linking page's Outlinks tab the broken link is not
surfaced: 71 outlinks starting with a stylesheet and the favicon (`issues-41b-inspector-outlinks`).
Code: `routing/inspector.ts:49-53` (opening replaces history; comment at `:3` says it's deliberate),
`IssuesScreen.tsx:125-129`; expansion is component state in `IssueGroups.tsx`, not in the URL.
Fix: opening the inspector pushes one history entry (hops inside replace it), so Back closes it. Keep expanded rows in
the search params (or session state keyed by check). Arriving in the inspector from a "Linked from" row should open
Outlinks filtered to the target, highlighted.

### A-08 · Check header · **Slows** (real bug) · "Recheck 1,000 of 240 URLs"

Did: opened Image in an older format: button reads "Recheck 1,000 of 240 URLs", line under it "Fetches 1,000 URLs
again (about 8 minutes) … 90 past the cap stay as crawled." Images without width and height: "Recheck 1,000 of 225
URLs … Fetches 999 URLs again … 166 past the cap". Indexable URL with parameters: "Fetches 1 URLs again, and up to 2
with the pages linking to them". Shot `issues-46-recheck-1000-of-240`. Not pressed.
Code: `changes/recheckWords.ts:56` (`Recheck ${taken} of ${judged}`; my guess, not traced: `judged` counts pages and
`taken` counts pages plus their images), `:120` (no singular).
Fix: say what will happen in one unit: "Recheck 240 pages" and under it "Fetches the pages and 760 of their 850
images (about 8 minutes)". Singular for 1.

### A-09 · Check detail layout · **Slows** · the table is pushed below a tall header; text set in a 46-character column in a 1,800 px pane

Did: measured. At 2560 × 1380 the 4xx table starts at y = 799; above it: page title + two sentences + notes strip
(200 px), severity/title/buttons, four figure tiles, the "Fixing this also clears" line, "What's wrong", the banner.
"What's wrong", "Why it matters" and "How to fix it" wrap at ~310 px with 1,400 px blank beside them
(`issues-02-404-arrive`). At 1280 × 720 the detail card is 998 × 428 px under 260 px of fixed page header; on Large
images, Title too long, Linked only from non-indexable pages and Links to broken internal URLs no table row is visible
on arrival (tables start at y = 774–910 of 720) (`issues-1280-*`).
Code: `CheckDetail.tsx:228-279` (figures), `:282-283` + `styles/tokens.css:554` (`--measure-explain: 46ch`),
`:503-532` (why/how).
Fix: one compact header row: severity · title · `8 URLs · 0.2% · same as 17 Nov` · buttons. Put "What's wrong" and
"Why / How to fix" side by side in a collapsible strip (or a right-hand column at ≥ 1600 px), collapsed after first
view. Let the page header sentences scroll away with the content at short heights.

### A-10 · Check detail · **Slows** · two vertical scroll areas, one inside the other

Did: on Large images, Image in an older format, Images without width and height, Indexable page not in a sitemap,
Meta description too long: the card scrolls (1,086 showing 1,598–1,712) and the table inside scrolls (881 showing
4,160–117,184). The wheel moves whichever is under the pointer. Title too long instead renders all 69 rows at full
height and only the card scrolls. Shot `issues-g-image-too-large`.
Code: `CheckDetail.tsx` card (`si-card min-h-0 overflow-auto`), `IssueGroups.tsx:1100-1124` / `GridCore` own scroller.
Fix: one scroller. Make the header part sticky/collapsible and give the table the rest of the card's height, or let
the table grow and only the card scroll, the same way for both table kinds.

### A-11 · Check list filter · **Slows** · "404" finds nothing

Did: typed "404" in "Filter checks": empty list (`issues-31-filter-404`). "broken", "title", "canonical" work.
Code: `screens/issues/checkList.ts:461` (`matchesText` matches title/category words).
Fix: also match the check id, its description and the group details already loaded for it ("404 Not Found"), so
"404", "noindex", "hreflang en-UK" find their checks.

### A-12 · Check list, "Since 17 Nov" chips · **Slows** · unexplained, and using one removes the rest

Did: hovered New / Fixed / Fewer / More / Changed: no tooltip, no title. Clicked "Fixed 2": the whole chip row is
replaced by one token "Since 17 Nov is fixed ×", the selected check jumps to the first fixed one, and to try "More" I
must first clear. With that token on, "Show the 116 checks that passed" can be switched on and shows nothing.
"Fewer 0" vs "More 2" vs "Changed 4" needed decoding (fewer/more URLs flagged; changed = any of them).
Shots `issues-34-chip-fixed`, `issues-37-fixed-detail`.
Code: `ChecksList.tsx:77-81` (labels), `:176-186` (chip swapped for a `FilterChip`).
Fix: keep the five as a pressed/unpressed segmented row (like the severity tabs right above), with titles: "Checks
that flag more URLs than on 17 Nov". Drop "Changed" or label it "Any change".

### A-13 · Bottom strip of a group · **Polish** · "Check is … / Detail is …" chips are not controls and sometimes contradict the group

Did: looked for controls in the strip: only "Show all 8 in URLs" is a link; the two chips beside it look like filter
buttons and do nothing. Missing H1: group label "No H1", chip "Detail is none". Links to missing page sections: "Related
URL is No other URL". With one group "Show all 8 in URLs" duplicates the header's "Open them in URLs".
Code: `IssueGroups.tsx:1051-1058` (`FilterChip variant="preview"`).
Fix: drop the chips; keep one link "Open these 8 in URLs". Hide the strip when the group already shows all its rows.

### A-14 · Flat tables · **Polish** · column widths waste the pane

Did: Title too long: URL 325 px (275 visible of 399 needed, middle-ellipsis) with ~400 px blank before Inlinks.
Linked only from non-indexable pages: Detail 518 px repeating "Linked from 1 page, which isn't indexable" on all nine
rows, "Inlinks 1" and "Linking pages 1" side by side, and the useful "Found on" squeezed last. Missing meta
description / Missing canonical / X card missing: URL at the far left, "Inlinks" 1,500 px away at the far right.
Shots `issues-g-title-too-long`, `issues-22-flat-foundon`, `issues-39-hidden`.
Code: `IssueRowsGrid.tsx` column list (`:200-292`), `evidenceColumns.ts`.
Fix: size URL to its longest shown path first (cap ~40%), never middle-truncate while there is free width; drop a
Detail column whose value is the same on every row (say it once above the table); keep numeric columns next to the
URL, not at the far edge.

### A-15 · Words and figures · **Polish** · things that needed decoding

Seen on screen: "Groups 1 · by detail" tile (`issues-02`); "8 issues in 1 group, by detail"; "Things to change 37 ·
1 in the site's template" and "Things to change 1 · its URLs share causes" (`issues-47-things-to-change`); "(4 points
back)"; "Related URL" as a column name for what is the broken link / the image / the canonical target; "Names";
"+2 other URLs"; the lone word "Page" between the address and "Found on"; "Show the page in URLs" on a 404 that isn't
a page; "Copy the group's URL"; export option "This group's pages · 8 pages: "404 Not Found"" (`issues-18-export`,
identical to "This check" when there's one group); "1 of 54" in the narrow layout's "All checks" bar when 29 checks
fired (`issues-1280-404-inspector`). "On 17 Nov 8" is not a link to that crawl's view of the check.
Code: `CheckDetail.tsx:85-89,254-276,485-490`, `IssueGroups.tsx:743-790` (headers).
Fix: delete the Groups tile and the "in N groups, by X" tail; name the column by what it holds ("Broken link",
"Image", "Canonical target"); "Things to change" → "Distinct causes: 37 (1 is in the template)" or drop; "(+4 to the
score)"; make "On 17 Nov: 8" open the compare.

### A-16 · Image checks · **Polish** · a column of dashes, no thumbnail seen

Did: Large images / older format / missing dimensions on garden: "Pixels" is "–" on all rows; the thumbnail cell
showed a placeholder (my flows block external requests, and the synthetic site wasn't serving), so the thumbnail
itself is unjudged. `issues-g-image-too-large`, `issues-44-synth-image-no-srcset`.
Code: `IssueGroups.tsx:795-840`.
Fix: hide a column that is empty for every loaded row; say once "pixel sizes weren't recorded on this crawl".

### A-17 · Row menu · **Polish** · acts only on the row's own URL, and covers the row

Did: right-clicked a 4xx row: Inspect, Open in the browser, Copy the URL, Copy the row, Show the page in URLs, Show
where it's found in Links, Recheck this URL (`issues-10-rowmenu-4xx`). Nothing for the linking page. A page row under a
broken-link group has no Links item at all (`issues-25-child-rowmenu`).
Code: `IssuePageMenu.tsx:63-125`.
Fix: add "Inspect the linking page" / "Open the linking page" when there is exactly one, and "Show this link in
Links" on page rows under a related-URL group.

### A-18 · Site-wide rows · **Polish** · addresses inside the Detail sentence are text

Did: robots.txt doesn't name the sitemap: Detail reads "https://…/robots.txt is empty, so it has no Sitemap: line; the
sitemap was found at https://…/sitemap.xml"; only the separate "Open robots.txt" link works (it does land on the
robots screen: good). Sitemap can't be read has "Open in Sitemaps" → `/sitemaps/5` (good). `issues-45-robots-row`.
Fix: link the sitemap address to Sitemaps; drop the repeated robots.txt address from the sentence.

### Worked well (keep)

- Hide: menu, toast with Undo, "Show 1 hidden check", header sentence updates (`issues-39-hidden`).
- Copy URLs: one click, toast "Copied 8 URLs", absolute addresses (`issues-17-copy-urls`).
- "URLs affected 8 · Open them in URLs" and "Show all 8 in URLs" land on URLs with the right filter; "Fixing this also
  clears [check]" is a real link.
- Flat-table evidence for titles, descriptions, headings, word count, security headers is specific and in the row.
- Keyboard in the tables works (ArrowRight expands, ArrowDown moves, Enter inspects, Shift+Enter opens in the browser:
  all tried; Ctrl+C is listed in the menu, not tried); it just isn't advertised.
- Recheck menu ("Recheck this group (8 URLs)", "Recheck selected URLs" disabled) and the export dialog read clearly.

## 3. Check by check

Legend for the verdict: **a** open the faulty page live · **b** see the faulty element/value · **c** reach the other
page involved · **d** this URL's other facts. A number is clicks; "h" = needs a hover to reveal the control; "dbl" =
only by double-click/Enter (nothing visible). "tree" = grouped table, "flat" = plain table.

### Garden crawl: all 29 checks that fired

| Check id | Shape; what the row shows | Clickable in the row | a · b · c · d | Verdict |
| --- | --- | --- | --- | --- |
| internal-client-error | tree, 1 group "404 Not Found" open; address · "Page" · grey "Found on 1 page · /blog/… (Content · "Buy Now")" | chevron; open icon (hover); `>` off-screen | a 1h · b in row · c 2h (expand, far-right icon) · d dbl | Slows: T1 |
| broken-internal-links | tree by related URL, 8 collapsed groups (target + 404) for 4 pages; child: page · "in the content · "anchor"" | group: chevron, open icon (hover); child: open icon (hover), `>` off-screen | a 2h · b after expand · c target 1h, no inspect · d 1 + dbl | Slows: A-06 |
| internal-server-error | tree, 1 group; "/product-videos/ · Page · 491 ms · Not linked from any page crawled" | as above | a 1h · b in row · c none exists (says so) · d dbl | OK |
| image-too-large | tree by image, 129 groups: file · type · Pixels "–" · Shown at · size · pages; child: page · "shown at 1456×816 · lazy · in the content · +2 other URLs" | group open icon (hover); child as above | a image 1h, page 2h · b size in row · c 2h · d 1 + dbl | Slows: nested scroll, dashes |
| canonical-to-first-page | tree, 3 groups of 1 (canonical target); child "/garden-sheds/?p=2 · Canonical /garden-sheds/ (200)" | as above | a 2h · b after expand · c target 1h · d 1 + dbl | Slows: should be 3 flat rows |
| title-duplicate | tree, 1 group "Login" open; rows: page · "Login" · "1 other page" | open icon (hover); `>` off-screen | a 1h · b in row · c twin is the next row · d dbl | OK |
| broken-external-links | tree, 1 group open (external URL · "No response"); child: page · "in the content · "Oxford University"" | open icons (hover) | a 1h · b in row · c 1h · d dbl | OK |
| empty-anchor-text | tree, 5 groups; "/" Likely template 260; child: page · "in the header"; sub-rows "Names /garden-sheds/ 200" (evidence clipped) | as above | a 2h · b only "in the header", no element · c 1h · d 1 + dbl | Slows: which link in the header? |
| not-in-sitemap | flat: URL · Title · Depth · Inlinks · Since 17 Nov | open icon (hover) only | a 1h · b n/a · c n/a · d dbl | OK; nested scroll |
| image-legacy-format | tree by image, 1,420 groups; child "Shows /media/… (200) +1" | as tree | a 2h · b size in group · c 1h · d 1 + dbl | Slows: 1,420 groups; A-08 label |
| image-missing-dimensions | tree by image, 3,661 groups; child "no width or height · in the content · +17 other URLs"; "All 17 in the inspector" dead | as tree | a 2h · b in row · c 1h · d 1 + dbl | Slows: A-05, A-08 |
| description-too-long | flat: URL · "1,083 px, over 985 (171 characters)" · description · as shown in results · Inlinks · Since | open icon (hover) | a 1h · b in row, with limit · c n/a · d dbl | Good |
| heading-levels-skipped | tree by detail, 4 collapsed groups ("H1 → H3"); child: page · "H1 → H3: "Is delivery free?"" | as tree | a 2h · b after expand · c n/a · d 1 + dbl | OK once open |
| title-too-long | flat: URL (cut) · "595 px, over 580 (64 characters)" · title · title in results · Inlinks · Since | open icon (hover) | a 1h · b in row · c n/a · d dbl | Good; A-14 |
| heading-too-long | flat: URL · "H1 of 87 characters, over 70: "…"" · Inlinks · Since | open icon (hover) | a 1h · b in row · c n/a · d dbl | Good |
| description-missing | flat: URL · Inlinks · Since | open icon (hover) | a 1h · b n/a · d dbl | OK |
| open-graph-missing | tree by detail, 3 collapsed groups ("Empty og:title, og:description"); child: address only | as tree | a 2h · b in group label · d 1 + dbl | OK |
| non-indexable-inlinks-only | flat: URL · same Detail on every row · Inlinks 1 · Linking pages 1 · Since · grey "Found on 1 page · /garden-sheds/?p=2 (…)" | open icon (hover) only | a 1h · b in row · c **no control** (dbl → Inlinks → click = 3) · d dbl | Slows/Blocks: the linking page is the fix (A-01) |
| low-word-count | flat: URL · "121 words, under 200" · Inlinks · Since | open icon (hover) | a 1h · b in row · d dbl | Good |
| canonical-missing | flat: URL · Inlinks · Since | open icon (hover) | a 1h · d dbl | OK |
| title-too-short | flat: URL · "152 px, under 200 (16 characters)" · title · Inlinks · Since | open icon (hover) | a 1h · b in row · d dbl | Good |
| description-duplicate | tree, 2 collapsed groups (shared text, cut at ~120 chars); child: page · same quote · "1 other page" | as tree | a 2h · b in group · c twin = sibling · d 1 + dbl | OK |
| cookies-insecure | tree by detail, 2 collapsed groups ("ASP.NET_SessionId: no Secure"); child: address only | as tree | a 2h · b in group · d 1 + dbl | OK |
| x-card-missing | flat: URL · Inlinks · Since | open icon (hover) | a 1h · d dbl | OK |
| robots-txt-no-sitemap | flat, site-wide: /robots.txt · sentence with two addresses as text · "Open robots.txt" link · Since | open icon (hover); **text link** to robots screen | a 1h · b in row · c 1 (robots), sitemap none · d 1 | Good; A-18 |
| h1-missing | tree, 1 group "No H1" open; child: /blog/; strip says "Detail is none" | as tree | a 1h · b in group · d dbl | OK; A-13 |
| security-headers-missing | flat, site-wide: / · "Missing Content-Security-Policy, Referrer-Policy" · Inlinks 777 | open icon (hover) | a 1h · b in row · d dbl | Good |
| security-headers-weak | flat, site-wide: / · "HSTS max-age 10886400 (126 days), under a year" | open icon (hover) | a 1h · b in row · d dbl | Good |
| url-parameters | flat: URL · "register" · Since · grey "Found on 1 page · /login/ (Content · "Create an account")" | open icon (hover) | a 1h · b in row · c no control · d dbl | Slows: A-01; "Fetches 1 URLs" |

On this crawl every "tree" row also has the 170 px overflow (A-02), so its `>` Inspect control is never on screen.

### Synthetic crawl: one or more per evidence kind (42 walked, `i09.log`; all 150 shapes listed in `i08.log`)

| Kind | Check id | Shape; what the row shows | a · b · c · d | Verdict |
| --- | --- | --- | --- | --- |
| Links | broken-internal-links | tree by target, "3 issues in 5 groups"; group "/plants/redirect-broken 301 → /plants/gone-redirect 404"; child page · "in the content · "anchor" · +3 other URLs"; sub-rows "Names [url] No response …" | a 2h · b yes (chain in group) · c 1h · d `>` visible here | Slows: A-06 |
| Links | links-to-redirects | tree, "1 issue in 6 groups": the same page under each | a 2h · b yes · c 1h | Slows: one page, six groups |
| Links | broken-fragment-links | tree; first group is labelled "No other URL" | a 2h · b "#nowhere, …#missing" in row | Polish: "No other URL" |
| Links | generic-anchor-text | flat: URL · "2 links: "Click here", "Read more…"" · Inlinks · "Names: Links to /plants/thin (200)" | a 1h · b in row · c text only | OK; "Names" column |
| Links | internal-nofollow-links, uncrawlable-links | tree by target / flat with "1 link: "Thin page" (onclick)" | b in row | OK |
| Images | image-broken | tree by image; "missing.jpg 404", "image-moved.jpg 301 → /img/missing.jpg 404"; child page · alt text | a 2h · b yes · c 1h | OK |
| Images | image-no-srcset | tree; Pixels 1200×800 · Shown at 600×400 (2×) · 4 KB; child "shown at 600×400 · alt "…"" | b yes | Good evidence |
| Images | image-missing-alt, image-alt-too-long, image-inline-large | tree by image / flat ("1 inline image: 12 KB, alt "…"") | b yes | OK |
| Redirects | redirect-loop | flat: URL · "2 hops: a (301) → b (301) → back to a" · grey "Found on 2 pages · /plants/ (…) +1" | a 1h · b in row · c no control | Slows: A-01 |
| Redirects | redirect-chain | tree by final target; child "chain-a → chain-b 301 → chain-c 301 → thin 200 · moves elsewhere · Found on 1 page …"; sub-row "Linked from /plants/" + "Show the link in Links" | a 2h · b full chain in row · c 2h | OK evidence; hops are text |
| Redirects | redirect-broken, redirect-temporary | tree by detail with a "Related URL" column; same sub-rows | as above | OK |
| Redirects | canonical-on-redirect | flat: URL · sentence · "→ /plants/thin 200" · Found on | c no control | Slows: A-01 |
| Canonicals | canonical-broken | tree; group "/plants/gone 404"; child "Canonical /plants/gone (404) · The canonical URL returns 404 Not Found" | a 1h (1 group) · b in row · c 1h | OK |
| Canonicals | canonical-loop | tree by detail "A loop of 2: a → b → back to a"; sub-row "Names b 200 canonicalised · canonical → a 200" | b in row | OK |
| Canonicals | canonical-multiple | flat: "2 URLs: … (head) · … (head)" | b in row | Good |
| Canonicals | canonicalised-page | tree, "9 issues in 9 groups": one page per group | a 2h each | Slows: should be flat Page · Canonical |
| Hreflang | hreflang-missing-return | tree by the alternate; child "page · "en" · 1 alternate doesn't link back" | b in row · c 1h | OK |
| Hreflang | hreflang-invalid-code | tree by detail "en-UK"; child: address only | b in group | OK |
| Hreflang | hreflang-unlinked | flat: … · grey "Found on 2 pages · /plants/hreflang-only-a (Hreflang in the head · "es") +1" | c no control | Slows: A-01 |
| Sitemaps | sitemap-error | flat, site-wide: sitemap · "(404 Not Found)" · "Open in Sitemaps" link | c 1 | Good |
| Sitemaps | sitemap-url-repeated, orphan-page | flat: "In 2 sitemaps: a.xml and b.xml" (text) · grey Found on / "In a sitemap · /sitemaps/extra.xml" | c no control | Slows: sitemap names aren't links |
| Sitemaps | sitemap-url-other-host | tree by detail; sub-rows "Linked from …", "Listed in the sitemap /sitemaps/extra.xml" (text + hover icon) | c 2h | A-05 |
| robots.txt | blocked-by-robots | tree by detail "Disallow: /private/, line 2"; child repeats it; sub-row "Listed in the sitemap …" | b rule and line in row · c no link to the robots screen at that line | Polish: link the rule to robots.txt |
| Robots meta | robots-directives-conflict, noindex-page | tree with both values quoted / flat: URL · Meta robots · X-Robots-Tag · Inlinks | b in row | Good |
| Titles/descriptions | title-too-long, description-too-short | flat with px, limit, characters and the text | b in row | Good |
| Headings | h1-duplicate, h1-multiple | tree by shared H1 (4 groups, collapsed) / flat "2 H1s: "…" · "…"" | b in row or group | OK |
| Structured data | structured-data-missing-required | tree by detail "Product: needs offers or review or aggregateRating"; child: address only | b in group | OK |
| Structured data | structured-data-invalid | tree; child "Block 1 of 1, line 1, position 55: …" | b in row | Good |
| Security | no-https | flat, site-wide: "/" · Inlinks 512 | b title only | OK |
| Duplicates | duplicate-content, near-duplicate-content | tree, 1 group named by its first page; rows "page · indexable · 1 inlink · 608 words · 1 similar page" | c twin = sibling row | OK; no similarity figure, no side-by-side |
| Files | broken-resources, resource-past-crawl-limit | tree (file 404; child "Script · in the head") / flat with size, type, Found on | b in row | OK |
| Social | social-image-too-small | tree; "100×100"; child "og:image is 100 × 100 px, under 200 × 200" | b in row with limit | Good |

Sub-row kinds seen: "Linked from [page] · position · "anchor" · Followed", "Names [url] [status] …", "Listed in the
sitemap [url]", "No page crawled links to it, and no sitemap lists it", "Show the link in Links" / "Show all 2 links in
Links", "All N in the inspector".

## 4. Sizes

| | 2560 × 1380 | 1280 × 720 |
| --- | --- | --- |
| Layout | list 370 px beside a 1,858 px detail card | list folded behind "All checks · 1 of 54 · ‹ ›"; card 998 × 428 px under 260 px of fixed header |
| Tree table, compared crawl | 1,976 in 1,806: 170 px horizontal scroll | 1,116 in 946: same 170 px |
| First table row on arrival | y ≈ 700–830: 9–21 rows visible | 4xx: 7 rows (the card arrives scrolled to the table); other checks: 0 rows, table starts below the fold |
| Nested vertical scroll | card + table on the five big checks; the check list is a third scroller | card + table |
| Clipped cells | tree address fits; "Found on" fits; sub-row evidence clipped at the edge; flat URL column 275 of 399 px | address 444 of 631 px, "Found on" 477 of 591 px, flat URL 110 of 399 px: titles are the only way to read them |
| With inspector | card 1,450 px, table 1,568 in 1,398 | page itself scrolls (1,636 in 650); table 740 in 570 |

## 5. Not tested, and why

- **Recheck, "Recheck this group", "Check the site again"**: opened and read only; pressing them fetches from the
  site, which needs the owner's word.
- **Export**: dialog opened and read; not exported (the dev build shows a path field where the app has a save dialog).
- **Image thumbnails**: never loaded (external requests blocked in my flows; the synthetic site wasn't serving).
- **Visible scrollbars**: headless Chromium hides them; overflow is measured, not seen.
- **"Hide in every crawl"**: it changes app settings; only "Hide in this crawl" was tried on the synthetic crawl and
  undone (count back to 150).
- **jitsunami.com and aptitude.co.uk crawls, Simple mode, dark theme**: outside this zone's brief or not reached.
- **The 108 synthetic checks not walked row by row**: their table shape and columns are in `i08.log`; each shares a
  shape with one in the table above.
- **Passed and skipped checks**: opened one of each (`issues-36-passed-detail`, `issues-38-skipped-detail`); the
  skipped one reads clearly, the passed one wasn't examined beyond loading.
- Late in the session the side nav showed "1 crawling · 127.0.0.1" (`issues-1280-404-inspector`). My flows never
  pressed a crawl or recheck control, so I take it to be another auditor's crawl of the test site; I didn't confirm it.
