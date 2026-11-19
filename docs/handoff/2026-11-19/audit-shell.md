# UX audit, zone `shell`: Advanced mode's shell and everything around the data

2026-11-19. Driven in headless Chromium against the running app at 2560 × 1380 first, then 1280 × 720. Garden crawl
(`/crawls/33454603b74b57f1`) unless said. Shots are in the audit scratchpad's `shots/` folder, all prefixed `shell-`;
flows are in `scripts/shell/`. Code paths are under `app/src/` unless they start with `engine/`.

## 1. The tasks I tried

| # | What an SEO wants | Path | Clicks | Where it broke |
| --- | --- | --- | --- | --- |
| 1 | State of the site and the three things to fix first | Home → site name → Overview (score 85, Fix first) → "Internal URL returns 4xx" row → Issues with the 8 URLs and their "Found on" | 2 to the first URL plus fault | Home answers nothing itself, and only the site's name (95 to 177 px of an 1,800 px row) is clickable (A-05). At 720 high Fix first is a screen and a half down, under five figure cards (A-19). Fix first rows carry no example URL (A-18). |
| 2 | Click every figure, card, row and count | 55 links followed, 25 non-links clicked | n/a | Every link's count matched its list. The dead ones are listed in A-07. Back from any of them returns to the top of Overview (A-02). |
| 3 | What changed since the last crawl | Overview card → "2 fixed" / "2 more" → Issues filtered, URLs marked Fixed or New | 1 | The links work. The card's words don't: "2 more" hides an 18 → 257 jump, the headline says "16 fewer", none of the six figures is a link, and there's no URL-level difference at all (A-06). |
| 4 | Which checks didn't run, why, what to do | Overview "Not checked on this crawl", the notes banner, a skipped check's page | 0 to read, 3+ to act | The answer is "Crawl again", written as plain text in three places. The control is two screens away (A-04). The paragraph is 22 names in one sentence (A-11). |
| 5 | Find a URL, check, screen, setting or action | Ctrl+K, 30 queries | 1 | URLs, checks, screens, other crawls: found. Settings, Crawl again, Recheck, Compare, Copy, the severity filters: not found. "404" doesn't find the 4xx check (A-08). |
| 6 | Switch crawls and sites, Back/Forward, reload | Switcher from 6 screens; an 8-step trail, 5 Back, reload, 3 Forward, 6 Back | 2 per switch | The trail and the selected check survive reload. The switcher keeps the section and the check but drops filters (A-16). The sidebar's own tooltip blocks the next item (A-01). App Settings drops the crawl (A-10). |
| 7 | Report for a client, list for a developer | Report → Full / Summary, Spreadsheet…, Save…, Print; every Export and Copy menu | 1 to 2 | The report reads well. Nothing in it leads back to the check in the app, "2 on more" is garbled, 8 rows are cut to 5 (A-12). Export and Copy names are inconsistent (A-13). |
| 8 | Start a crawl | `/new`, every section opened; crawled the synthetic site twice (600 pages Polite: paused, resumed, stopped; 40 pages Fast: ran to the end) | n/a | No include/exclude patterns, no JavaScript rendering, no URL-list mode (A-09). Live screens, pause, resume, continue and the end toast are good; small faults in A-22. |
| 9 | Settings | Crawl Settings, all eight app sections; changed one analysis limit and put it back | n/a | The "out of date → Analyse again" loop is clear on Overview and Home. Limits are typed in bytes and bits in one 2,900 px column (A-15). The crawl's "Settings" isn't settable (A-20). |
| 10 | Simple/Advanced switch | Flipped on 12 screens, both ways | 1 | From URLs, Links, Sitemaps, robots.txt and crawl Settings it lands on the Summary without a word, and flipping back doesn't return (A-03). |

## 2. Findings, ranked

### A-01 · Sidebar · **Slows** (bug): an item's tooltip covers the next item and eats the click

Hover "URLs" for a second, or click it, then move the mouse down to "Links" and click: nothing happens. The tooltip
("URLs Ctrl 3") opens below the item, exactly over the next one, stays open while the pointer is on it, and stays open
after a click even with the mouse parked elsewhere (focus keeps it up). Measured with a real mouse path at three speeds:
the element under the pointer at "Links" was `span.si-tooltip__text`, and the URL didn't change. Every top-to-bottom
walk of the sidebar hits this. Shots `shell-13-tooltip-covers`, `shell-13-tooltip-after-click`,
`shell-13-tooltip-mouse-on-links`.

- Code: `ui/NavItem.tsx:65` (`side={collapsed ? 'right' : 'bottom'}`), `ui/Tooltip.tsx:133-152` (hoverable content,
  opens on focus after a click).
- Fix: open the expanded sidebar's tooltips to the right (`side="right"`) like the collapsed one, set
  `disableHoverableContent`, and close on click. Better: drop the tooltip when expanded and print the keys at the row's
  right edge on hover.

### A-02 · Overview · **Slows**: Back returns you to the top, not to the card you clicked

Scrolled to Status codes (scrollTop 1,300), clicked "4xx", pressed Back (browser Back and the top bar's Back both):
Overview reloads at scrollTop 0. Every figure below the fold costs a re-scroll per look. The notes banner's open state
is lost the same way, and on reload.

- Code: `app/router.tsx:217` (`scrollRestoration: false`); the scroller is `<main>` in `layout/AppShell.tsx`.
- Fix: keep `<main>`'s scrollTop per history entry and restore it once the screen's data has painted; keep "notes open"
  in the search params as `score=1` already is.

### A-03 · Simple/Advanced switch · **Slows**: from five screens it throws you to the Summary and can't bring you back

| From (Advanced) | Simple lands on | Advanced again |
| --- | --- | --- |
| `/`, `/new`, `/settings/*`, Overview, Issues + check + filter, Report + tab | the same route | same place (Overview's scroll resets to the top) |
| URLs with a 4xx filter | Summary | Overview: filter gone |
| URLs with a URL inspected | Summary | Overview: URL gone |
| Links, Sitemaps, robots.txt, crawl Settings | Summary | Overview |

No toast says why the screen changed. Shots `shell-28-simple-5` to `-12`.

- Code: `app/modeRoutes.ts:47-58` (`simpleLanding` returns Overview for Advanced-only screens); `layout/ModeSwitch.tsx:32`.
- Fix: remember the Advanced location left behind (path and search) and go back to it when Advanced is chosen again
  from the Summary it landed on. With a URL inspected, land in Simple on that page's entry in What to fix.

### A-04 · Overview, notes banner, skipped checks · **Slows**: "Crawl again" is the answer and it isn't a control

"Crawl again to check it." is plain text in the Not checked card (twice), in the notes banner (twice) and on each
skipped check's page. Clicking it does nothing. The real control is Home → ⋯ → "Crawl again…" or sidebar Settings →
"Crawl again with these", and the palette finds neither ("crawl again": 0 results). Shots `shell-10-notchecked`,
`shell-07-notes`, `shell-10-skipped-landing`.

- Code: `screens/overview/AnswerSections.tsx:268-300` (reason rendered as a string); the action exists at
  `screens/CrawlSettingsScreen.tsx:99` and `screens/home/CrawlMenu.tsx:78`.
- Fix: one "Crawl again…" button in the Not checked card's header and in the notes banner, to `/new?from=<crawl>`;
  the same link on a skipped check's page; add "Crawl again…" and "Continue" to the palette.

### A-05 · Home, library · **Slows**: one small link per row, and the counts aren't links

Clicked the errors count, URLs, Pages, score, status, the grey URL and the favicon on the garden row: nothing. Row
cursor is `auto`; double-click does nothing; no right-click menu. Only the site's name is a link. "3 errors · 4
warnings · 22 notices" name three lists you can't open from here. Columns don't sort. An older crawl's second line is
its file name (`garden-a.sicrawl`), not its URL. Shots `shell-01-home-w`, `shell-02-home-older-w`.

- Code: `screens/home/LibraryTable.tsx:218-233` (row and the one link), `:356` (`SeverityCounts`, no links), `:56`
  (plain headers).
- Fix: make the whole row open the crawl; each severity count a link to `/crawls/<id>/issues?severity=[…]`; URLs to
  `/urls`; the score to `/overview?score=1`; sortable Site, URLs, Score and Started headers.

### A-06 · Overview, comparison card · **Slows** (URL changes: **Blocks**)

- "Checks: 2 fixed · 2 more" is the only clickable part. "2 more" reads as nothing; it means "2 checks flag more pages".
- The biggest change on the site, "Indexable page not in a sitemap" 18 → 257 pages, shows only as grey "· was 18" at
  the end of a Fix first row, while the card's headline is "about 16 fewer worth a look".
- Dead text: "Since 17 Nov, 06:49 (4,360 URLs, done)" (the earlier crawl), "Score 83 → 85", "Things to fix 75 → 74",
  "Worth a look 879 → 863", "About 2 things fixed".
- 4,360 URLs then, 4,362 now: nothing says which URLs are new, gone, or changed status. Can't be done from here.

Shots `shell-03-overview-w`, `shell-09-fixed`, `shell-09-more`, `shell-07-compare-menu`.

- Code: `screens/compare/ComparisonCard.tsx:181-202` (figures as text), `:230-256` (`ChecksLine`).
- Fix: list the moved checks in the card, one line each with both counts and a link ("Indexable page not in a sitemap
  18 → 257 pages", "Orphan page in a sitemap 1 → 0"), worst first. Link "Since 17 Nov" to that crawl, "Things to fix"
  to `issues?change=…`, the score to the score sheet. Add a URLs line: "2 new · 0 gone · n changed status", each to a
  filtered URLs list.

### A-07 · Overview · **Slows**: figures that name a list and aren't links

Clicked each; all dead (cursor `auto`):

- the sentence under the title: "About 74 things to fix", "about 863 more worth a look", "29 checks found 1,499
  issues", "118 passed", "23 were skipped";
- the Passed card ("118 checks") beside three sibling cards that are links;
- "23 checks skipped" in the Not checked header;
- Rechecks table: the check's name and "0 of 8 fixed · 8 still · 0 new" (only a far-right "URLs" button responds);
- The crawl line: "260 pages read", "Rechecked 3 times";
- card headers "4,359 URLs", "269 page URLs on the site", "260 pages";
- Alerts: "1 sitemap lists 240 URLs.";
- the score sheet: "22 things worth a look", "8 of 4,362 addresses", "Fixed since 17 Nov (2)" rows.

Nothing lands somewhere less specific than its label: all 55 links matched their counts.

- Code: `screens/overview/AnswerSections.tsx:88-100` (Passed `Stat` has no link), `screens/issues/checkList.ts:327`
  (the sentence), `screens/rechecks/RechecksCard.tsx:30-73`, `screens/OverviewScreen.tsx:348`,
  `screens/overview/CrawlSections.tsx:46`.
- Fix: Passed card → Issues with "Show the checks that passed" on; "23 were skipped" → Issues scrolled to the skipped
  group; recheck check name → that check; each header count → the same filter its rows use.

### A-08 · Command palette · **Slows**: finds places and URLs, misses actions and settings

Found: URLs by path or title (with status), checks by name, screens, other crawls, export, theme, density, mode.
0 results: "crawl again", "recheck", "compare", "user agent", "speed", "proxy". Wrong results: "404" gives "Soft 404"
and an image with 7404 in its path, not "Internal URL returns 4xx"; "errors" gives two unrelated checks, not the Errors
list; "copy" gives two checks; "settings" lists "Open settings" and "Go to Settings" with nothing to tell them apart.
From Home, a check or URL query returns nothing and doesn't say a crawl must be open. Choosing a URL opens URLs
unfiltered at row 1 with the inspector on it: the grid isn't scrolled to the row. Shots `shell-14-palette-empty`,
`shell-14-palette-berlin`, `shell-14-palette-berlin-landing`.

- Code: `keyboard/paletteSearch.ts:12-17` (title and keywords only), `keyboard/useAppCommands.ts:169-309` (the command
  list), `keyboard/CommandPalette.tsx:86` (checks), `:148` (URLs).
- Fix: add commands "Crawl again…", "Continue", "Compare with…", "Recheck this check", "Copy URLs", "Errors /
  Warnings / Notices"; index each setting's label to its Settings section or New crawl section (`focus`); give checks
  keywords (404, 410, 5xx, noindex…); name the two settings "App settings" and "This crawl's settings"; scroll the grid
  to the inspected row.

### A-09 · New crawl · **Blocks**: three things a working SEO looks for aren't there

Opened all six sections (`shell-19-new-open-w-0` to `-3`). Present and clear: speed presets, folder/subdomain scope,
nofollow, canonical/hreflang/pagination targets, URL/depth/size limits, robots.txt, Crawl-delay, sitemaps, user agent
(7 choices), Accept-Language, headers, proxy with a test, cookies, Shopify signature, timeout, retries, concurrency.
Missing, with nothing saying so:

- include / exclude URL patterns (skip `/basket/`, `?sort=`, only `/blog/`), and query-string handling;
- JavaScript rendering: no option and no line saying pages are read as raw HTML;
- crawl a pasted list of URLs.

Defaults are sensible (Standard speed, obey robots.txt, find sitemaps, no limit). "Store HTML" is off by default, so
the inspector's source view is empty unless you knew to turn it on.

- Code: `crawl/options/CrawlOptionsForm.tsx`, `crawl/options/words.ts`.
- Fix: add "Only crawl URLs matching" and "Skip URLs matching" to Scope; say in Advanced "Pages are read as the server
  sends them; JavaScript isn't run" until it is; a "List of URLs" start mode.

### A-10 · Sidebar · **Slows**: app Settings drops the crawl, and two items are called Settings

Inside a crawl the sidebar has "Settings" (sliders icon, the crawl's) and, at the bottom, "Settings" (gear, the app's).
Clicking the bottom one removes the crawl switcher and every crawl item: only Home is left, so the way back is Back or
Home → site. Shot `shell-13-app-settings-from-crawl`.

- Code: `layout/Sidebar.tsx:163` and `:275`.
- Fix: keep the crawl's block in the sidebar on `/settings/*` while a crawl was open; rename the crawl's item "Crawl
  settings" (its screen's own title) or "How it was crawled".

### A-11 · Overview, "Not checked on this crawl" · **Slows**: unreadable, and said four times

22 check names in one comma-run sentence, each a link with no link styling, then the reason in grey under it with
"this check" in the singular. The same fact is also in the title sentence ("23 were skipped"), the notes banner and the
Passed card ("23 were skipped, with why below"). "254 pages … were fetched by an earlier version" doesn't say an earlier
version of what, or why 254 of 260. Shots `shell-10-notchecked`, `shell-04-overview-n-1`.

- Code: `screens/overview/AnswerSections.tsx:277-300` (`listed(...)` of `TextLink variant="subtle"`).
- Fix: lead with the reason and the action: "22 checks need a fresh crawl: this one was made by an older version of
  Shouting Iguana. [Crawl again…]", then the names as a two- or three-column list. Drop the banner when the card is on
  the same screen.

### A-12 · Report · **Slows**

- No way from the report back into the app: a section heading ("1. Pages or files that can't be found") and the "Fix
  these first" rows aren't links; all 134 links go to the live site. The report is 17,691 px with no contents list.
- Summary page: "2 problems fixed, 2 on more." (shot `shell-16-report-summary-c`).
- "The first 5 of 8 … And 3 more: the full list is on sheet 1 of the spreadsheet": a client is sent to a second file
  for three rows.
- Three buttons, two of which open the same Export dialog with a different radio ticked; "Save…" doesn't say what.

- Code: `engine/src/ShoutingIguana.Engine/Reporting/ReportDocument.cs:383` ("on more"), `:829` (row cap);
  `screens/report/ReportScreen.tsx:116-153`, `:333`.
- Fix: "2 fixed, 2 got worse"; show all rows up to about 25; make "Fix these first" rows anchors to their section; in
  the app's preview add "Open in Issues" beside each section heading; name the buttons "Save as HTML…", "Save
  spreadsheet…", "Print or save as PDF".

### A-13 · Export and Copy everywhere · **Polish**

| Screen | Control | What it offers | Fault |
| --- | --- | --- | --- |
| Overview, Report | Export / Save… / Spreadsheet… | one dialog: The report (Full / Summary) or Spreadsheet of everything | the spreadsheet's default name is "… report.xlsx", same stem as the report |
| Issues | Export | This check · This group's pages · The report · Every check; Excel, CSV | says "8 pages" where the list says "8 URLs"; "This group's pages" needs decoding; Excel first here, CSV first on URLs and Links |
| Issues | Copy URLs | copies at once, toast "Copied 8 URLs" | fine |
| URLs | Copy | "Copy the URLs of this view" / "Copy the URL column" | can't tell the two apart |
| Links | Copy | "…target URLs" / "…source URLs" / "Copy the Source URL column of the first 10,000 links" | items 2 and 3 read the same |
| Sitemaps, robots.txt | none | | no Export or Copy at all |

Shots `shell-17-issues-Export-c`, `shell-17-urls-Export-c`, `shell-17-links-Copy`, `shell-08-export-c`.

- Code: `export/offers.ts:139-442` (format order per offer), `export/ExportDialog.tsx`, `screens/UrlsScreen.tsx:418`,
  `screens/links/copyWords.ts:15`.
- Fix: one format order (Excel, CSV); "Copy all N URLs (every page of this view)" versus "Copy the rows shown"; add
  Export to Sitemaps and Copy to robots.txt.

### A-14 · Score sheet in Advanced · **Slows**: the same check has two names

Overview says "Internal URL returns 4xx" and "Paginated page canonicalised to page 1". "How it's worked out", opened
from the same screen, says "Pages or files that can't be found", "Later pages of a list that point Google at the first
page" and "addresses". Shot `shell-07-score`.

- Code: `screens/overview/OverviewScore.tsx:52` opens `simple/ScoreBreakdownDialog.tsx`.
- Fix: pass the mode's titles to the dialog; in Advanced show the technical name and "URLs".

### A-15 · Settings → Analysis · **Slows**

23 limits in one 480 px column, 2,926 px of scroll, with 1,500 px blank to the right at 2560; no grouping. "Largest
image" is typed in bytes (204800 for 200 KB, 1048576 for 1 MB); near-duplicates in "bits … of 256"; each helper says
its size twice ("The engine's is 200 px (about 20 characters). About 20 characters of ordinary text."). After a change
the page says nothing about what went out of date. The rest of the loop is good: Overview shows "These issues are out
of date … Analyse again" and Home "Analysis out of date. Analyse again". Shots `shell-26-settings-analysis-w`,
`shell-27-analysis-changed`, `shell-27-outofdate-overview`.

- Code: `screens/settings/analysisLimits.ts:51-76`, `screens/settings/SectionPage.tsx:14`.
- Fix: KB/MB fields; near-duplicates as "% the same"; group as Titles, Descriptions, Content, Images, Links in two
  columns; after a change show "5 crawls are now out of date · Analyse them again".

### A-16 · Crawl switcher · **Slows**: keeps the screen, drops the filter

From Issues on a check with Errors only, switching to the site's older crawl keeps the check and drops the severity
filter. From URLs filtered to 4xx it lands on all URLs, so "the 404s then versus now" means rebuilding the filter.
Typing in the open menu doesn't filter it. Shots `shell-12-switcher`, `shell-12-older-garden`.

- Code: `layout/CrawlSwitcher.tsx:138` (`goToSection(navigate, sectionOf(pathname) ?? 'overview', id)`).
- Fix: carry the search params across when the target is the same site; add a filter box once there are more than
  about eight crawls.

### A-17 · Top bar · **Polish**

Only "Home" in the breadcrumb is a link: the site, the crawl's date and the "Done" pill do nothing. Back and Forward
are never disabled, even in a fresh window. At 2560 the search button reads "Search or run a co…" beside 1,500 px of
empty bar. Shot `shell-03-overview-w`.

- Code: `layout/TopBar.tsx:116-125`, `:137-175`, `:290-298`.
- Fix: site → the crawl's Overview; date → opens the crawl switcher; pill → the "The crawl" section; disable Back and
  Forward at the ends; let the search button take its full label when the bar has room.

### A-18 · Overview, Fix first · **Polish**

Eight rows, then "And 20 more checks with issues", then about 300 px of blank card at 2560 × 1380. Descriptions are cut
with "…" in a 950 px card. No row shows one affected URL, so the first fixable fact is always a click away. Shot
`shell-03-overview-w`.

- Code: `screens/overview/answer.ts:52` (`FIX_FIRST_LIMIT = 8`), `screens/overview/AnswerSections.tsx:152`.
- Fix: fill the card's height with rows; show the worst URL under each check's name in place of the catalogue
  description (which is one click away in Issues).

### A-19 · Overview · **Slows**: numbers that look like they disagree

"Crawled 4,362" but "Status codes 4,359 URLs"; "Pages 260" but "Indexability 269 page URLs on the site"; "Found
6,053". Each severity card carries three units ("3 checks · About 9 things to fix, from 13 issues"). The five figure
cards take 140 px and push Fix first below the fold at 1280 × 720. Shots `shell-04-overview-w-1`,
`shell-04-overview-n-0`.

- Code: `screens/overview/TotalsRow.tsx:56`, `screens/overview/AnswerSections.tsx:80-86`,
  `screens/overview/CrawlSections.tsx:46`.
- Fix: one figure per card with the others on hover; say the difference where it shows ("4,359 of the 4,362: 3 are the
  site's other versions"); put the figure cards under Fix first, or shrink them to one line.

### A-20 · Crawl "Settings" · **Polish**: a record, not settings

Read-only, in a 694 px column. For a finished crawl its first card is "Continue: Only a stopped crawl can be
continued", which is noise. Nothing marks what differed from the defaults (URL limit 5,000, 2 at a time). No analysis
limits and no "Analyse again" here. Shot `shell-25-crawl-settings-w`.

- Code: `screens/CrawlSettingsScreen.tsx:87`, `:302`, `:333`.
- Fix: show the Continue card only for a stopped crawl; mark non-default values; add "Analysed with" and "Analyse
  again".

### A-21 · New crawl, layout and small things · **Polish**

- A 694 px column in a 2,310 px frame; with sections open the page is 3,657 px and "Start crawl" scrolls out of sight.
- Home's "Crawl" button is enabled with an empty field.
- Shopify: the text says "remembered only if you tick Remember", but the tick appears only after a signature is typed.
  A lapsed signature is said twice in two wordings. Validation itself is immediate and clear. Shot
  `shell-30-shopify-typed`.
- "Crawl again…" opens the same "New crawl" title and "Every setting has a default" line with nothing saying the form
  now holds that crawl's settings. Shot `shell-20-crawl-again`.

- Code: `screens/NewCrawlScreen.tsx:102`, `:441`; `crawl/options/CrawlOptionsForm.tsx:255`;
  `crawl/options/ShopifyAccessFields.tsx`; `screens/HomeScreen.tsx:109`.
- Fix: sticky footer for Start crawl; two columns of sections at wide widths; show the Remember tick from the start,
  disabled until there's something to remember; title "Crawl gardenfurnituresets.co.uk again" with "Settings from the
  crawl of 17 Nov".

### A-22 · Live crawl · **Polish**

Good: the URL goes straight to the crawl's Overview, figures tick, Pause is instant with a toast and a "Resume" banner,
Home shows a running card with Pause / Stop / Open, Issues and Report say when they'll fill, and the end swaps Overview
to results with a toast "Crawl finished … Open the report · View issues". Faults:

- "No URL limit, so no end time" with Found 809, Queued 792 and 1.8/s on screen: an estimate is one division away.
- The "Live" badge stays beside the title while paused; paused, Overview says 0.8/s and Home 0/s.
- Stop sits 8 px from Pause and stops at once with no question. "Continue" recovers it, so it's a nuisance only.
- Nothing shows what is being fetched now.
- Two local sites both read "127.0.0.1" in the breadcrumb; the port is only in the switcher.

Shots `shell-21-live-03`, `shell-22-paused`, `shell-23-after-stop`, `shell-24-finished`.

- Code: `screens/overview/LiveOverview.tsx:189`, `:257`.
- Fix: "About 7 min left at this rate" when the queue has stopped growing; badge "Paused"; show the last few URLs
  fetched under the chart.

### A-23 · Rechecks card and its dialog · **Slows**

"URLs" at the far right of a 1,800 px row opens a 640 px dialog in which the 404 paths are cut with "…" and aren't
links. Shot `shell-08-recheck-urls-c`.

- Code: `screens/rechecks/RecheckRowsDialog.tsx:88`, `:141`; `screens/rechecks/RechecksCard.tsx:73`.
- Fix: make the whole recheck row open it; widen the dialog to its content; each URL opens the inspector, with an
  open-in-browser icon beside it.

### A-24 · Settings pages at 2560 · **Polish**

Every app Settings section hugs the left in about 760 px with the rest of the frame blank, while Overview is centred
and New crawl is centred narrower: three layouts for three sibling screens. Shot `shell-13-app-settings-from-crawl`.

- Code: `screens/settings/SectionPage.tsx:14` (`max-w-(--form-max)`).
- Fix: one page measure for forms, centred like New crawl, or two columns where there are many fields.

## 3. What I did not test, and why

- **Anything native to the installed app**: Ctrl+O, drag and drop, the save dialog (the browser shows a text field),
  Print, Windows Credential Manager, Updates, licences. The audit runs in a browser.
- **Analyse again, Remove from library, Delete file, Recheck**: opened the menus and the Delete confirmation only.
- **A real-site crawl and the proxy test**: forbidden. Live screens were judged on the synthetic site only.
- **Dark theme, density, zoom, and high contrast.**
- **Simple mode's own screens**, beyond where the switch lands.
- **jitsunami.com and aptitude.co.uk** were opened only through the switcher.

## 4. What I left behind

- Two synthetic crawls are now in the audit library (the brief says not to delete crawls):
  `/crawls/c16c8832510111b2` (127.0.0.1:47311, stopped at 119 URLs) and `/crawls/09868d3ea55d0d1d` (127.0.0.1:47321,
  done, 427 URLs). Both test sites are stopped.
- Settings → Analysis → "Fewest words on a page" was 201 for about ten seconds and is back at 200 (checked after a
  reload: "Put back every limit" is disabled again, and no crawl reads as out of date).
- A dummy Shopify signature was typed into New crawl and cancelled; nothing was started or saved.
