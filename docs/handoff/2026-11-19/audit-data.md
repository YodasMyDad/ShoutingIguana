# UX audit, 2026-11-19: Advanced mode's data screens

URLs, the inspector, Links, Sitemaps and robots.txt, used as an SEO would on the garden crawl
(`/crawls/33454603b74b57f1`), then the synthetic crawl (`/crawls/0b71328b5d42d1ed`) for what the garden crawl lacks, and
jitsunami (`/crawls/e6b4b1d9a0bb1541`) for Sitemaps. Judged at 2560 × 1380 first, then 1280 × 720. Nothing was
rechecked, crawled or exported; no app code was changed.

- Shots: `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\da833503-3c78-46eb-9e8a-d618294b5af4\scratchpad\audit\shots\`, every file
  prefixed `data-`. Names below are given without the folder or `.png`.
- Flows: `…\scratchpad\audit\scripts\data\` (`lib.mjs` has the helpers; `montage.mjs` joins shots).
- Crawl-file evidence was read from private copies in `…\scratchpad\audit\data-audit-copy\` with Python's `sqlite3`,
  opened read-only (`q.py`). The sidecar's own files were not touched.
- Paths under `app/src/` unless they start with `engine/`.

**What works, so it isn't lost in a redesign:** Links > Broken links (one click: source page, anchor text, target and
status in each row, both URLs blue links); one origin's robots.txt page (the file beside its blocked URLs, "Blocked 1
URL" on the rule's own line, "In a sitemap: Yes: extra.xml" as a link); the inspector's "Indexable, and why" checklist
and its Redirects tab; Ctrl+K finds a URL by its path; filters, sort, columns and search live in the address, so Back
restores them; a saved view shows up as a tab beside the presets, on every crawl.

## 1. The tasks tried

Clicks are counted from the screen named, the crawl already open. A double-click counts as one.

| # | An SEO wants | Path taken | Clicks | Where it broke |
| --- | --- | --- | --- | --- |
| 1a | Every 404 and who links to each, from URLs | Status code ▾ → 404 | 2 for the list | The 8 rows show `Inlinks 1` as a plain number. The linking page isn't in the row and nothing in the row leads to it: right-click → "Show its inlinks in Links" (2 more, hidden, leaves the screen), or double-click → Inlinks tab (2 more). 8 URLs × 2 = 16 more actions. D-02, D-09 |
| 1b | The same, from Links | Broken links tab | 1 | Works: 9 rows, each with source, target, anchor, 404. The grouped view of the same list hides the source page again (D-03) |
| 2 | Who links to this URL, with what anchor; what it links out to | Row → right-click → Show its inlinks in Links; or double-click → Inlinks / Outlinks | 2 each | Nothing on screen says double-click or right-click. No "outlinks" item in the menu. In the 400 px inspector the anchor is cut ("What Is Rattan and How Is It Different to W…") and the URL middle-cut (D-05) |
| 3 | All redirects, where each ends, chains, and who still links to them | URLs: Status ▾ → 3xx | 2 | 17 rows, no destination column. A third click (Indexability tab) shows "Redirects to" at 224 px. Chains: only in All columns (54 columns, 7,976 px of sideways scroll) or one URL at a time in the inspector, under "6 more" (3 clicks a URL). Links > Links to redirects is 1 click and fine. D-12 |
| 4 | Non-indexable pages and why; and one URL, exactly why | Indexability ▾ | 2 per reason | The facet takes one value, then offers only that value until its chip is removed: eight reasons, three clicks each, no "Not indexable". "Indexable" returns 4,048 images. The reverse is good: double-click → Summary answers at once. D-10 |
| 5 | Missing or duplicate title / description / H1, duplicates side by side | Add filter → About → Content → Column → Title → Condition → is empty → Add filter | 8 | 5,793 rows: images, redirects, other sites' pages. No "duplicate" condition, Title can't be sorted. Can't be done from URLs. D-11 |
| 6 | Biggest and slowest pages; biggest images and where each is used | Type ▾ → Page, Response header twice; Performance tab, Type ▾ → Image, Size header twice; Links > Images | 4; 5; 1 | Pages include other sites' until a third filter is built. "Used on pages 1" is a plain number. Links > Images answers it in one click (image, size, alt, "Shown on" as a link). Pixel size is "–" on every garden row I saw |
| 7 | Is this URL in the sitemap; what's in it that shouldn't be; what should be and isn't | Inspector Summary; Sitemaps list counts; the sentence under the list | 1; 1; 1 | On the garden crawl every answer is wrong (D-01). Elsewhere: the inspector says "Yes. Open Sitemaps" without naming the sitemap (D-13); the "isn't in any sitemap" link lands on URLs under eight filter chips (D-16) |
| 8 | Which rule blocks this URL; what each rule blocks | Inspector Summary; robots.txt → the origin | 1; 1 double-click | Both answers are good once there. Getting to an origin's page is double-click only, and its name isn't a link (D-07) |
| 9 | Open on the live site, copy the URL, copy 50 rows, export the filtered list | Hover → icon; right-click → Copy the URL; click, Shift+click, Ctrl+C; Export | 1; 2; 3; 2 | The open icon is invisible until hover and sits at the far end of a 1,358 px column. Shift+click then Ctrl+C copies the wrong thing (D-06). Export's dialog is right first time |
| 10 | Filter, sort, search, custom filter, columns, save a view, clear it all; state after leaving and Back | All of them | | "Clear filters" leaves the search and the sort. Back keeps filters, sort, search and columns, loses the selected row and the scroll place. D-14 |
| 11 | Every inspector tab and link; walking between related pages and back | From a 404: Inlinks → the source page → its Outlinks | | The grid still highlights the row I left, the panel has no way back, and nothing marks the link I came for among 78 outlinks. D-08 |

### What a click does, grid by grid

Nothing on any of these screens mentions double-click or right-click (checked: the page text has neither phrase). The
foot of each grid lists keys only: `Up Down Move · Enter Inspect · Space Select · Ctrl C Copy for Excel`.

| Grid | Click a row | Click the URL text | Double-click / Enter | Right-click |
| --- | --- | --- | --- | --- |
| URLs | Tints the row. Follows in the inspector only if it's already open | Nothing | Inspector | Menu: Inspect, Open, Copy the URL, Copy the row, Filter/Exclude this value, Show its inlinks in Links, Recheck |
| Links, flat | Tints the row | **Inspects that end** (blue link) | Inspects the target | Menu, with both ends named |
| Links, grouped: a target | Nothing visible | Inspects the target | Opens or closes the group | Menu: target only |
| Links, grouped: the "on /page/" line | Nothing | Nothing | Inspects the source page | Menu: target only, no source page |
| Sitemaps list | Nothing visible | Nothing | Opens the sitemap | Browser's own menu |
| One sitemap's URLs | Tints the row | Nothing | Inspector | Menu, plus "Show the URL in URLs" |
| robots.txt list | Nothing visible | Nothing | Opens the origin | Browser's own menu |
| One origin's blocked URLs | Tints the row | Nothing | Inspector | Menu |
| Inspector lists | – | Walks to that URL | – | – |

### How a URL looks, screen by screen

Measured in the page (`data/st01.mjs`), light theme.

| Where | Colour | Size | Cursor | Click |
| --- | --- | --- | --- | --- |
| URLs grid: URL, "Redirects to", "Final URL", Canonical | black | 11.2 px | `copy` | nothing |
| Links grid, both ends; a grouped target | blue | 12.1 px | pointer | inspects |
| Links grouped, the "on /page/" line | black | 12.1 px | `copy` | nothing |
| Sitemaps list | black | 12.1 px | `copy` | nothing |
| One sitemap: heading; URL cell (host shown in grey) | black | 16.7; 11.2 px | `copy` | nothing |
| robots.txt list: origin | black | – | default | nothing |
| One origin's blocked URL | black | 12.1 px | `copy` | nothing |
| Inspector: Inlinks, Outlinks, Images, Issues | blue | 12 px | pointer | walks |
| Issues > a check's rows | black | 12.1 px | `copy` | nothing |

Three looks, two sizes, and a `copy` cursor on text that doesn't copy when clicked.

### Column widths

| Grid, tab | 2560 × 1380 | 1280 × 720 |
| --- | --- | --- |
| URLs, Overview | URL 1,358 · Status 88 · Indexability 120 · **Title 208** · Depth 64 · Inlinks 80 · Issues 72 · Response 88 · Size 80 · Type 80 | URL **240**, the rest the same; sideways scroll (1,120 in 996) |
| URLs, Indexability | URL 1,382 · Indexability 120 · **Canonical 224** · Meta robots 176 · In a sitemap 112 · **Redirects to 224** | URL 240; scrolls |
| URLs, Content | URL 1,374 · **Title 208** · Title width 112 · **Description 320** · Description width 160 · H1s 64 | URL 240; scrolls |
| URLs, Performance | URL 1,414, then nine columns of 64 to 120 | URL 240; scrolls |
| URLs, Links | URL 1,430, then eight columns of 64 to 136 | URL 240; scrolls |
| URLs, All columns | URL 240, 54 columns, 7,976 px in 2,260 | the same in 996 |
| Links, All links | Source 501 · Target 932 · Anchor 360 · Kind 96 · Position 104 · Follow 116 · Target status 128 | Source **169** · Target **203** · Anchor 156, the last four unchanged (444 px) |
| Links, grouped | Target 1,504 · Status 128 · Linking pages 112 · Links 96 · Anchor 246 · Where 152 | Target 346, Anchor 140 |
| One sitemap | URL 420 · Outcome 168 · Status 104 · Indexability 168 · Title 320 · … = 1,620, then **640 px blank** | the same 1,620 in 996; scrolls |
| Sitemaps list | Sitemap 1,488 · Found via 117 · Status 234 · five counts of 63 to 92 | Sitemap 276 |
| robots.txt list | Origin 1,644 · robots.txt 232 · Blocked URLs 120 · Read 136 · Size 96 | fits |

## 2. Findings, ranked

### D-01 · Sitemaps, garden crawl · **Blocks** (data bug) · The sitemap reads "Read" with every count 0, and 257 of 257 pages "aren't in any sitemap"

It is a **data bug in the crawl file, caused by a recheck**. Not a stale copy, and not only wording.

What the screen shows (`a10-sitemaps`): `/sitemap.xml · Default location · Read · 0 · 0 · 0 · 0 · 0`, then "257 of the
site's 257 indexable pages aren't in any sitemap", and under it "Indexable page not in a sitemap 257". URLs agrees with
it (Sitemaps ▾: "In no sitemap 6,053") and contradicts it in the next menu (Depth ▾: "Only in a sitemap 2").

What the file holds (`garden-b.sicrawl`, which is this crawl id):

- `sitemaps`: one row, `status_code 200`, **`url_count 240`**. `notices`: "Read 1 sitemap(s) listing 240 URLs."
  `urls`: 192 rows with `source = 2` (found by a sitemap).
- `sitemap_urls`: **0 rows**.
- `rechecks`: three rows. The third is `check_id robots-txt-no-sitemap`, `kinds RobotsTxt,Sitemaps`, `status 3`
  (`state/summaries.json` calls it `failed`), `fetched 0`, started 2026-11-18 06:33:27 UTC. The site's own robots.txt row
  has `fetched_at` 0.4 s later, and the crawl's `updated_at` is 16 ms before it.
- `garden-a.sicrawl` is the same crawl before those rechecks (same `started_at` and `finished_at`, no `rechecks`
  table): its `sitemap_urls` has **240 rows**.
- Issue counts before → after: `not-in-sitemap` 18 → **257**, `sitemap-non-indexable` 1 → 0, `orphan-page` 1 → 0. The
  score went 83 → 85 because two warnings vanished with the list.

So pressing recheck on "robots.txt doesn't name the sitemap" emptied the sitemap's URL list and left its count. The
analysis that followed raised 239 false notices, dropped two real warnings and raised the score.

**Not proven:** which statement removed the rows. The recheck parks a sitemap's entries by negating `sitemap_id`, then
deletes or restores them (`engine/src/ShoutingIguana.Engine/Storage/CrawlWriter.Recheck.cs:498-549`, and `:577`). This
file was written by the owner's installed build of 18 Nov: its `sitemap_urls` has no `lastmod` column and there is no
`uncollected_facts` table, while the current insert names `lastmod` (`Storage/WriterStatements.cs:128`). Whether the
branch's engine still does it needs a test I couldn't run (no rechecks allowed): recheck that check on a crawl whose
sitemap answers, then count `sitemap_urls`.

Compare: synthetic lists 5 sitemaps, counts filled, "23 of the site's 239" (`data-sm-synth-01-list-2560`); jitsunami 6
sitemaps, "4 of the site's 17" (`data-sm-jitsu-01-list-2560`). Both match their files (220 and 14 entries).

Code that draws it: counts from `sitemap_urls` (`engine/…/Storage/SitemapListReader.cs:173`), the sentence
`screens/sitemaps/sitemapWords.ts:406-428`.

Fix:
1. Engine: a recheck that fails, at any point, leaves `sitemap_urls` as it found them (one transaction, rolled back),
   with a test that fails a sitemaps recheck half-way.
2. Engine: when a sitemap's `url_count` > 0 and it has no entries, the sitemap checks are "not checked", never 257
   findings.
3. Screen: the status cell reads "Read: 240 URLs, but this crawl no longer has the list" with "Crawl again to restore
   it", the counts read "–" (as an unreadable sitemap's do), and the sentence is replaced by one line saying the pages
   can't be compared. Never "Read" beside five zeros.
4. This file: the owner's real crawl needs a fresh crawl, or its entries restored from the earlier copy.

### D-02 · URLs, and every grid but Links · **Slows** · The URL is the thing you want, and clicking it does nothing

On URLs I clicked `/contact-us/`: nothing (`data-urls-02-click-url-text`). A click elsewhere on the row tints it
(`data-urls-01-single-click`). Only double-click or Enter opens the inspector (`data-urls-05-dblclick-inspector`), and
only the keyboard half of that is written anywhere. The same text in Links is blue and opens the inspector on one
click (`data-l-02-click-source`). The cursor over the black text is `copy`, and a click doesn't copy. "Redirects to",
"Final URL" and Canonical cells are the same dead text, so from a redirect's row you can't step to where it goes.
See the two tables above: this is the owner's Issues complaint, on five more screens.

Code: `screens/urls/urlColumns.tsx:597-603` (the URL cell: `<Url … copyable={false}>`, no `onGo`) and `:699` (the
related-URL cells); `ui/Url.tsx:240-242` (the plain branch) against `:207-222` (the link branch);
`styles/components.css:2130-2142` (`cursor: copy` on the plain text). Links passes `onGo`:
`screens/links/linkColumns.tsx:286-291`.

Fix: one rule for the whole app. A URL the crawl knows is a blue link; one click opens the inspector on it (on URLs,
the row's own; in a Canonical / Redirects to / Final URL cell, that URL). Pass `onGo` from `urlColumns.tsx:597` and
`:699`, `screens/sitemaps/SitemapDetail.tsx`, `screens/robots/RobotsFileView.tsx:579`, `screens/links/LinkGroupsGrid.tsx:479`
and `screens/issues/IssueRowsGrid.tsx:239, 371`. Drop `cursor: copy`. Keep double-click and Enter as they are, and add
"Click a URL to inspect it" to the grid's foot beside the key hints.

### D-03 · Links, grouped by target · **Slows** · The page that holds the broken link is behind a chevron, and isn't a link when you get there

Links > Broken links > Group by target (`data-l-06-broken-grouped`): each 404 shows `Linking pages 1 · Links 1 · Buy
Now · Content`. The page is a count. A click on the row does nothing; the only way in is the 16 px chevron (or
double-click, unlabelled). Open, it reads `on /blog/best-rattan-patio-sets/ "Buy Now" Content Followed`
(`data-l2-01-group-open`): the page is black, `copy` cursor, click does nothing. Double-clicking that line inspects
the page; its right-click menu offers "Inspect the target" and "Open the target", nothing for the page it names.
Second line: "Show the one link to this target, as a row".

Code: `screens/links/LinkGroupsGrid.tsx:474-486` (the line's `<Url>` without `onGo`), `:573` (the menu),
`screens/links/linkGroups.ts:115` (the words).

Fix: when a target has one linking page, show it in the group's row ("Linked from `/blog/best-rattan-patio-sets/`", a
link) and drop the chevron; with more, "Linked from 12 pages" opens the group on a click anywhere on the row. The
page in each line is a link to the inspector, and the line's menu gains "Inspect the source page" and "Open the source
page in your browser". Replace "Show the one link to this target, as a row" with "Show in the flat list".

### D-04 · URLs, one sitemap's URLs, the inspector's lists · **Slows** · Spare width all goes to the URL; the text you came to read is cut

At 2560 px on URLs > Content (`data-preset-urls-content-2560`): the URL column is 1,374 px, about 1,050 of it blank,
while Title is 208 px ("GardenFurnitureSets™ Huge…") and Description 320 px. Indexability: Canonical and "Redirects
to" 224 px each beside 1,382 px of URL. A status of "Network error" reads "Netw…" in an 88 px column with 1,000 px
blank beside it (`data-l-08-shown-in-urls`). At 1280 the URL column drops to 240 px, cutting the URLs
(`/rattan-f…rattan-patio-sets/`), and the grid scrolls sideways for Size and Type
(`data-preset-urls-overview-1280`). One sitemap's page does the opposite: fixed 1,620 px, 640 px blank on the right,
titles cut at 320 (`data-sm-synth-03-one-sitemap-2560`).

Code: `screens/urls/urlColumns.tsx:412-472` (fixed widths by column and type), `:476-493` (`urlColumnWidth`: the URL
takes whatever the rest leave, never under 240); `screens/urls/urlGridParts.ts:26-76` (the sitemap page's fixed
widths).

Fix: share spare width among the columns that hold prose or URLs (Title, Description, H1, Canonical, Redirects to,
Final URL, Redirect chain), each up to its longest visible value; cap the URL column at its longest visible URL plus
the icon. Give status-like columns a minimum that fits their longest word ("Network error", "Blocked by robots.txt").
When width is short, shrink Depth / Inlinks / Issues / Response / Size / Type to their content before the URL goes
under about 360 px. Use the same sizing on the sitemap page.

### D-05 · Inspector · **Slows** · 400 px wide in a 2,560 px window

Opened from any grid at 2560 × 1380 it is 398 px (`data-urls-05-dblclick-inspector`). Six of its ten tabs are under
"6 more" (`data-insp-00-more-menu`), so Images, Redirects, Headers, Structured data, Hreflang and Source each cost two
clicks. Its own URL wraps onto two lines. In Inlinks the source is middle-cut (`/rattan-fu…e/rattan-patio-sets/`) and
the anchor cut ("What Is Rattan and How Is It Different to W…"), which hides the "· Content" after it
(`data-insp-montage-1`). The Images table loses its columns off the edge. Source wraps at 40 characters
(`data-insp-montage-2`). There is a resize edge (it goes to 960 px and is remembered), but nothing shows it. And at
960 px the Inlinks table still cuts the URL and the anchor, because its columns are fixed
(`data-m-07-inspector-widened-inlinks`).

Docked at the bottom (`data-insp3-02-docked-bottom-inlinks`, `data-insp3-03-docked-bottom-images`): 320 px tall in a
1,380 px window, five rows and its own scrollbar; anchor text and alt text are cut at about 190 px beside a "From" /
"Image" column with 1,500 px blank. At 1280 × 720 on the right it leaves the grid 620 px, three and a half columns
(`data-n-01-urls-inspector-1280`).

Code: `styles/tokens.css:372-374` (400 px, 320 px), `:620`; `settings/schema.ts:86-87` (ranges);
`layout/InspectorSlot.tsx:52-92`; `ui/Tabs.tsx:105-174` (the "more" menu); `inspector/LinksSection.tsx:172-272` (fixed
`STATUS_WIDTH`, `TEXT_WIDTH`, `KIND_WIDTH`…).

Fix: default width `clamp(400px, 34vw, 880px)` so all ten tabs show at the owner's size; a visible grip on the edge;
bottom dock defaults to about 40% of the height. In the lists, the URL and the anchor/alt text share the spare width
and nothing is middle-cut while there is room. At widths where tabs still don't fit, scroll the tab row rather than
hiding six behind a menu.

### D-06 · Every grid · **Blocks** (bug) · Click, Shift+click, Ctrl+C copies the wrong thing

"Copy these rows into a spreadsheet", the mouse way: click row 8, Shift+click row 12. Five rows tint, and the browser
also selects their text (`data-t9-01-multi-select`, the darker blocks). Ctrl+C then put this on the clipboard:
`le⏎Frequently Asked Questions⏎1⏎518⏎6⏎90 ms…`: 41 lines, no tabs, no header, starting mid-word, one cell per line, and
no "Copied" notice. The other ways work: Shift+Down ×4 → header + 5 tab-separated rows and "Copied 5 rows"; Ctrl+click
→ 3 rows; Ctrl+A → "6,053 URLs selected", all copied.

Code: `grid/GridCore.tsx:638-641` ("Text selected inside the grid by mouse: let the browser copy that instead") and
the pointer handler at `:962-976`, which doesn't stop the browser extending its selection on Shift+click.

Fix: on a Shift+click (and Ctrl+click) in the grid, `preventDefault` on mousedown and clear any text selection; and
when rows are selected, Ctrl+C always copies the rows. Also add "Copy the rows of this view" to the Copy menu, which
today offers only "Copy the URLs of this view" and "Copy the <column> column".

### D-07 · Sitemaps list, robots.txt list · **Slows** · The name of the thing isn't a link, and for most rows nothing is

Sitemaps (`data-sm-synth-02-hover-name-2560`): a click on `/sitemaps/extra.xml` does nothing; double-click opens it.
The only links in a row are the non-zero counts ("179", "3"), 8 to 25 px wide. A sitemap that lists nothing or
couldn't be read (`empty.xml`, `missing.xml`, and garden's `/sitemap.xml` today) has no link at all. robots.txt
(`a11-robots`): the origin is plain text with a default cursor; the only link is a "1" in Blocked URLs. Nineteen of
garden's twenty origins have none, the site's own robots.txt among them. Neither list has a right-click menu.

Code: `screens/sitemaps/SitemapTreeGrid.tsx:263` (name, no link) and `:343-351` (activation only);
`screens/robots/RobotsOrigins.tsx:108-124` (origin as a `<span>`) and `:225`.

Fix: the sitemap's name and the origin are links to their pages, one click, with the same right-click menu as a URL
row (Open, Copy the URL, Open in the browser). Keep the counts as links to the filtered list. A click anywhere else
on the row may open it too: these are lists of places to go, not rows to select.

### D-08 · Inspector, walking · **Slows** · It loses where you came from and what you came for

From the 404 `/berlin-cushion-box/` I opened Inlinks and clicked its one source, `/blog/what-is-rattan/`
(`data-insp2-01-walked-to-source`). The panel now shows the blog page, on its Inlinks tab; the grid still highlights
`/berlin-cushion-box/`; the panel has no "back to" line (Back in history does step back, and returned me to the right
URL but on a different tab). To see the broken link on that page I opened Outlinks: 78 rows, no status sort,
no "broken only", only "Every kind ▾". Opening a source page from Links' Broken links does the same: Summary, with
nothing pointing at the link (`data-l2-02-links-inspect-source`).

Code: `inspector/inspectorContext.ts:16-17` (`walk`), `inspector/sections.ts` (the last tab is remembered for the
session, not per step), `inspector/LinksSection.tsx` (the Kind menu is the only filter).

Fix: a breadcrumb line at the top of the panel when you've walked ("← /berlin-cushion-box/"), and the grid's
highlight cleared or marked "inspecting another URL". When the inspector is opened on the source end of a link
(`end=from`, already in the address), open Outlinks with that link first and highlighted. Give Inlinks and Outlinks a
status filter ("Broken", "Redirects") beside the Kind menu and sort broken first.

### D-09 · URLs · **Slows** · The counts name things you can go to, and none is a link

`Inlinks 1`, `Issues 1`, `Outlinks 69`, `Used on pages 259`, `Linking pages`: clicking each does nothing
(`data-t1-03-urls-404`, logged in `t01b.mjs` and `t06.mjs`). The one route is right-click → "Show its inlinks in
Links"; there is no outlinks, issues or "used on" item. After that jump, Back restores the filter but not the row I
was on (`data-t1-05-back-from-links`).

Code: `screens/urls/urlColumns.tsx:561-575` (number cells are text); `screens/UrlsScreen.tsx:985-1018` (the row menu,
"Show its inlinks in Links" at `:1015`).

Fix: Inlinks → opens the inspector on that URL's Inlinks tab; Outlinks → its Outlinks tab; Issues → its Issues tab;
Used on pages → Links filtered to that image's uses. Zero stays plain. Add the matching items to the menu. On Back,
re-select and scroll to the row left (the place is already kept for sitemaps and origins: `rememberPlace`).

### D-10 · URLs, Indexability · **Slows** · "Not indexable" can't be asked, and "Indexable" answers with images

Indexability ▾ lists nine values with counts (`data-t1-01-statuscode-facet` shows the pattern). Picking one closes the
menu, and opening it again offers only the value just picked ("Indexability, of 3 URLs: Canonicalised 3",
`data-v-01-facet-second-open`): a second reason means removing the chip first. So "every page that isn't indexable"
is eight trips of three clicks, never one list. Picking Indexable gives
4,310 URLs; add Type ▾ Image and 4,048 remain, each row reading **"Not a page"** in the Indexability column under a
chip that says "Indexability is indexable" (`data-m2-01-indexable-images`). The image's inspector agrees with the chip:
"Indexable. Search engines can index it." The preset tabs don't help: Indexability and Content change columns only, so
both still list 6,053 URLs, 5,739 of them images.

Code: `grid/FacetMenu.tsx:94-97` (one value per choice, counted inside the filtered view); `screens/urls/urlColumns.tsx:395` and
`screens/urls/urlColumns.tsx:154-155` (the cell says "Not a page" from the type, the filter doesn't);
the presets at `screens/urls/urlColumns.tsx:20-60` (columns only).

Fix: tick-boxes in the facet (several values, stays open, counted as if its own filter were off), with "Not indexable
(any reason)" first. Make "Not a page"
a real value of the facet so cell, chip and count agree. Add a "Pages only" switch at the left of the filter bar, on
by default for the Indexability and Content tabs (HTML pages on this site), with the count it hides beside it.

### D-11 · URLs · **Blocks** · Missing or duplicate titles, descriptions and H1s can't be found from here

Missing title took eight clicks through Add filter (`data-t10-01-add-filter`, `data-t10-02-title-is-empty`) and
returned 5,793 of 6,053 URLs (`data-t10-03-title-empty-result`). Narrowed to Type is page: 49 rows, every one a
redirect, a 404 or another site's page. There is no "is duplicated" condition; the Title header can't be sorted (a
tooltip says why, nothing visible does); so duplicates can't be put side by side. The editor's help line reads "A to Z
match in capitals or small letters. Other letters, such as é and É, match only themselves."

Code: `grid/FilterEditor.tsx`, `grid/filterEditing.ts:109`; conditions in `api/grids.ts` (`GRID_FILTER_OPS`).

Fix: on the Content tab, three facets that read from the checks already run: Title ▾ (Missing · Duplicate · Too long ·
Too short), Description ▾, H1 ▾ (Missing · Several · Duplicate). "Duplicate" groups the rows by the shared value, a
group header carrying the title and "3 pages", so they sit side by side. Let Add filter take typing to find a column
in one step instead of About → Column. Cut the help line to "Ignores case."

### D-12 · URLs · **Slows** · A redirect's row doesn't say where it goes

Status ▾ → 3xx on garden lists 17 URLs with Status, Indexability "Redirected", and dashes
(`data-t3-garden-01-urls-3xx`). "Redirects to" exists only on the Indexability tab at 224 px, and isn't a link (D-02).
"Final URL", "Final status", "Redirect kind" and "Redirect chain" are in the column chooser and All columns only. A
chain is readable one URL at a time in the inspector's Redirects tab, which is good, and under "6 more". The filter
button says "Status", its chip says "Status class is 3xx"; the next button says "Status code", its chip "Status is
404".

Code: `screens/urls/urlColumns.tsx:20-60` (the presets: no redirect one); `grid/urlFilterWords.ts:8` (chip words).

Fix: a "Redirects" tab: URL · Status · Redirects to · Kind ("HTTP to HTTPS") · Ends at · Final status · Hops · Inlinks,
filtered to 3xx, sorted by hops. Destination cells are links. Name the two status filters "Status group" and "Status
code", chips to match.

### D-13 · Inspector, Summary · **Slows** · "In a sitemap: Yes. Open Sitemaps" doesn't say which

For a URL listed in `extra.xml` the row reads "Yes. Open Sitemaps", and the link goes to the list of all sitemaps
(`data-v-04-inspector-in-a-sitemap`). The robots.txt page already does it right
for the same URL: "Yes: extra.xml", a link to that sitemap.

Code: `inspector/SummarySection.tsx:566-580` (the comment says the list is deliberate);
`engine/…/Storage/UrlInspectorReader.cs:131` already reads the sitemaps that list the URL.

Fix: "Yes: extra.xml" (each a link to that sitemap's page with the URL highlighted), "and 1 more" when several. When
no: "No" plus, for an indexable page, "should be" linking to the not-in-sitemap list.

### D-14 · URLs, Links · **Slows** · "Clear filters" doesn't clear; Back half-restores

With a filter, a sort and the search "rattan" applied (53 URLs), Clear filters left `?q=rattan&sort=…`: 439 of 6,053,
the box still saying "rattan", and the Clear filters button gone, so the list is still narrowed with nothing left to
press but the small × in the search box (`data-v-03-after-clear-filters`, `data-t10-07-after-clear`). The search has no chip, so nothing but the box says the list is
narrowed. Typing in the search does nothing until Enter (a hint says so; fine). After Links and Back: filters, sort,
search and columns return; the selected row and the scroll place don't (`data-t10-08-back-state`). The sidebar's URLs
link always opens the bare list.

Code: `screens/UrlsScreen.tsx:270-274` (its own comment says the search goes; it stays, because `q` is applied by a
different handler at `:694-701`); `grid/FilterBar.tsx:196-206`.

Fix: show the search as a chip ("URL or title contains rattan ✕"), and make the button "Clear all" (filters and
search; sort and columns stay) with the sort shown in the header as now. Keep each grid's selected row and scroll
offset per history entry.

### D-15 · Inspector, an image · **Slows** · It contradicts itself and doesn't show the image

For `nice-garden.png`, 2.1 MB (`data-t6-03-image-inspector`): no picture, though Links > Images has a thumbnail. Under
"Timings and size": "⚠ Large images · 1.9 MB over the 200 KB limit", not a link. Two blocks down: "No issues. No check
flagged this URL in the last analysis." Above both: "Status checked only: the crawl … didn't read its content", then
"Indexable. Search engines can index it." with "Canonical is itself: it has no canonical". Links and discovery says
"Inlinks 0 hyperlinks from 0 pages" while the tab reads "Inlinks 1" (the line counts hyperlinks, the tab every kind;
the same gap on a page: "256 hyperlinks" under a tab that says 257, `data-v-04-inspector-in-a-sitemap`).

Code: `inspector/SummarySection.tsx:56, 116, 194-202` (image size), `:214` (the verdict), `:507`;
`inspector/IssuesSection.tsx:64`.

Fix: for an image, show the picture, its pixel size and "Used on 1 page: /blog/how-to-clean-garden-stones/" (a link)
at the top; drop the indexability checklist and canonical; make "Large images" a link to that check; replace "No
issues" with "1 issue, filed on the page that shows it" and the link. The Inlinks and Outlinks lines give the tab's total first ("1 link, an image").

### D-16 · Across the zone · **Polish** · Words that need decoding

- Links, after "Show its inlinks": chip "Link doesn't lead to its own page" (`data-t1-04-inlinks-in-links`). Say
  nothing, or "Not counting links to itself".
- Sitemaps' "23 aren't in any sitemap" → URLs under eight chips, among them "Found by is not a probe of the site's
  other versions", "HTML was read", "URL doesn't contain /cdn-cgi/" (`data-sm-synth-06-not-in-sitemap-urls-2560`).
  Collapse to one chip: "Indexable pages not in a sitemap".
- URLs, from Links' "Show in URLs": "Only the URLs that the links of a view with 1 filter lead to, each once."
  (`data-l-08-shown-in-urls`, `screens/urls/linkEnds.ts:37-41`). Say "The 9 URLs the broken links point to."
- Sitemaps list, Found via: "Sitemap index" on every child reads as the row's type. Say "In /sitemap.xml".
- Links' row menu has "Open in the browser" (the target, it turned out), "Copy the URL" (whichever cell was under the
  pointer) and, lower, "Open the source page / the target in your browser" (`data-l-05-rightclick`). Keep only the
  named pair, and "Copy the source URL / the target URL".
- Hand-edit a filter value and the refusal lists engine ids: "'indexability' is one of none, noindex, … blockedByRobots"
  (`data-m-01-indexable-images`).

### D-17 · Links, URLs at 1280 × 720 · **Polish** · The fixed columns keep their width and the URLs pay

Links: Source 169 px and Target 203 px, cut in the middle, while Kind, Position, Follow and Target status hold 444 px
between them; the toolbar wraps to two rows (`data-preset-links-all-links-1280`). URLs with the inspector open: the
filter bar wraps to four rows, 190 px of a 720 px window, over a 620 px grid (`data-n-01-urls-inspector-1280`).

Code: `screens/links/linkColumns.tsx:150, 163, 397-417`.

Fix: at narrow widths fold Kind + Position + Follow into one "Hyperlink · Content · Followed" column, and put the
facets behind one "Filters" button when they'd wrap.

### D-18 · One sitemap's page · **Polish** · A third kind of URL grid

Its heading cuts the sitemap's URL in the middle (`http://127.0.0.1:54384…itemaps/extra.xml`) in an empty 2,300 px
header. Its grid has no preset tabs, no column chooser and no Add filter; status is a bare "200" where URLs has the
coloured dot; URLs are shown whole with a grey host where URLs shows paths (`data-sm-synth-03-one-sitemap-2560`).

Code: `screens/sitemaps/SitemapDetail.tsx:365` (heading), `:150-355`; `screens/urls/urlGridParts.ts`.

Fix: render it as the URLs grid with a fixed "In sitemap: extra.xml" chip, so tabs, columns, status pills and sizing
come free. Never truncate the heading while it fits.

### D-19 · One origin's robots.txt · **Polish** · Three more things that should be links, and a tall empty box

"Blocked by: Disallow: /private/" doesn't jump to line 2 of the file beside it; the file's `Sitemap:` line doesn't
open that sitemap's page; the blocked URL is dead text (D-02) (`data-rb-synth-02-origin-2560`). At 1280 × 720 the
four-line file keeps a 340 px box and pushes the blocked URLs to the last 40 px of the window
(`data-n-06-robots-origin-1280`).

Code: `screens/robots/RobotsFileView.tsx:373-394, 579`; `styles/tokens.css:470-471`.

Fix: the three links; and where the panes stack, size the file's box to its lines (up to the 240 px floor it has now
as a ceiling for short files).

### D-20 · Small things

- URL text is 11.2 px on URLs and one sitemap's page, 12.1 px on Links, Issues and robots.txt.
- A file's URL reads `combined_main_….css /css/cache/` on URLs > Overview and `/css/cache/combined_main_….css` on
  Content (`a08-urls` against `data-preset-urls-content-2560`): the name-first form depends on the Type column being
  asked for (`screens/urls/urlColumns.tsx:154-155`, `:603`).
- The open-in-browser icon is invisible until the row is hovered or selected, then sits at the right end of the URL
  column, up to 1,330 px from the text (`data-urls-03-hover-url`; `styles/components.css:2234-2256`). Put it straight
  after the text.
- Double-clicking a column's edge puts its width back to the default; it doesn't fit the content. Dragging works and
  is remembered across crawls.
- Links to redirects on the synthetic crawl lists two links whose target status is 200 (meta refresh) with nothing in
  the row to say why they're there (`data-t3-synth-links-to-redirects`). Add an "Ends at" column to that tab.
- Opening the inspector on a row doesn't add a history step but walking inside it does, so Back from the first URL
  inspected leaves the screen rather than closing the panel.
- An export finished by another window showed its "garden-404.csv Saved" toast in mine (`data-l-06-broken-grouped`).

## 3. Not tested, and why

- **Recheck this URL, "Check again"**, any crawl: not allowed. So D-01's cause is shown from the file, and whether the
  branch's engine still loses the entries is unverified.
- **Export**: the dialog was opened and read, never run. Its summary said "these 11 columns" over a 10-column grid; not
  chased.
- **Open in the browser** in the real shell: `window.open` was stubbed and its URL logged. The app's own save dialog
  and browser hand-off weren't seen.
- **A running crawl**: every crawl was finished, so live refresh, the cursor holding its row and partial counts weren't
  judged.
- **Dark theme, density and zoom settings, high contrast**: light and standard only.
- **Keyboard only**: Enter, arrows, Shift+arrows, Space, Ctrl+A/C, Shift+Enter and Ctrl+K were used; Tab order, the
  header's keys and screen-reader names were not.
- **Hreflang and Structured data with real content**: seen on the garden page only (no hreflang; one JSON-LD block).
  The synthetic crawl's hreflang cases weren't opened.
- **Source tab**: opened and read; its search and any jump from an issue to a line weren't tried.
- **Column reorder and pin** (the header's right-click menu): listed, not exercised.
- **aptitude.co.uk**, and jitsunami beyond its Sitemaps list.
- **Sidebar collapsed, or widths between 1280 and 2560.**
