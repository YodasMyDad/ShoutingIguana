# Step 3's closing audit: findings (2026-11-14)

Run on `ui/step-3` at `b8684fa` from `docs/handoff/2026-11-13/brief-closing-audit.md`, read-only, both modes. The
gallery re-shoot ran beside it; its defects are at the end. Screenshots named below are in the auditing session's
scratchpad (`a539aa23-…/scratchpad/closing-audit/shots/`, not kept in the repo).

How it was run: the brief's ports 50874-50877 sit in a Windows reserved range (50870-50969), so it used 52874-52878.
Dev app on copies of the garden and 100k crawls; three live crawls of the test site; for the first time, Simple walked
in the real release shell (Home, checking, close to the tray, the finish notice, back, Summary, Report, quit). Also
1280x720, 200% zoom, light, dark, forced colours, axe, live-region text, a keyboard-only flow per mode. Not done: a
real screen reader, a 1M-row crawl, frame rate and memory during a live crawl, 4K, compact density. The parity list,
the comparison and the design-doc check were read by sub-agents (code), spot-checked in the app.

## 1. Gaps, most serious first

1. Simple's wrong reason when no page could be read: `127.0.0.1:52877` typed without `http://`, https tried,
   robots.txt unreachable, and the Summary said robots.txt "tells checkers to stay out". False; the http version
   answered 200 and was never tried. `simple/nothingRead.ts:49-53` treats robots unreachable as disallow. Also took
   about 2 minutes. Fix in step 3. CONFIRMED (59, 60).
2. A stopped check isn't analysed: Simple's Summary shows "No score yet / Work out the results" with "Open the
   report" primary although there are no results; the design (`v2-ui-simple.md:263`, `summary-stopped.html`) shows
   a dashed "so far" score. Advanced the same ("Analyse it"). Fix. CONFIRMED (17, 20, m-summary-stopped).
3. Counts disagree between screens (`v2-ui-ia.md:95`: "N URLs" is one number everywhere): garden 4,521 on Home vs
   6,286 in URLs; 100k 110,251 vs 112,577; pages 101,107 Home vs 101,106 Issues/Simple; test site 3,145 vs 3,144.
   Fix. CONFIRMED (41, 28, 44).
4. Advanced Home is still the "Foundation version" (`HomeScreen.tsx:34-35`): flat list, Remove the only action, no
   visible Open. Promised (`v2-ui-ia.md:89-94`, design doc): grouped by site, a running-crawl card, Open, Continue,
   Crawl again, Analyse again, Show in folder, Delete. Nothing defers it. Fix or defer explicitly. CONFIRMED (41).
5. The shell speaks Advanced in Simple: the hide-to-tray notice "Still crawling: … keeps crawling in the tray"
   (`src-tauri/src/window.rs:213`, CONFIRMED in the shell's log); the tray tooltip "URLs crawled"
   (`tray/text.rs:45-57`) and the quit question "Stop crawling and quit? … from the library" (`quit.rs:31`)
   PLAUSIBLE (code). The finish notice is in Simple's words. Fix.
6. "Issues" as a heading in Simple's palette and `?` sheet (`keyboard/modeWords.ts:7-10`). Fix. CONFIRMED (10, 11).
7. Other Simple word leaks: export messages say crawl and analysis (`export/exportWords.ts:198, 208, 285, 296,
   310`; 310 always "Analyse the crawl again"); About "an SEO crawler and site auditor" (`About.tsx:20`); "Engine"
   in `Updates.tsx:26`, `About.tsx:25`, `ExportToast.tsx:86`. Fix. PLAUSIBLE (code).
8. Ctrl+F in the inspector's Source from the tab or its panel goes to the URLs grid's search (typing filtered the
   grid); works only with focus inside the viewer. `v2-ui-ia.md:363`. Fix. CONFIRMED (34).
9. The grid's row menu has no "filter by / exclude this value" (brief, "The grid"). Fix or defer. CONFIRMED.
10. Columns can't be resized, reordered or pinned (brief; `v2-ui-architecture.md:588` lists it, no step). Defer
    explicitly. PLAUSIBLE.
11. Toasts with two actions crush their text into a ~110 px column (`feedback/AppToaster.tsx`). Fix. CONFIRMED
    (04, 23, 59).
12. A problem's "Where it is" footer: "It is shown. Addresses are on gardenfurnituresets.co.uk." Counts causes, not
    addresses, and the list shows full URLs anyway (`simple/WhereItIs.tsx:190-193`). Fix. CONFIRMED (09).
13. At 200% the URLs header and filter bar fill the screen: about 2 rows, none with the inspector open. No sideways
    scroll. Fix. CONFIRMED (z200-adv-sheet).
14. The Summary's report card has no "Save as PDF" (`v2-ui-simple.md:236`); the subtitle has no duration. Small fix.
    CONFIRMED (06 vs m-summary).
15. No "% of Total" beside issue counts (one of the comparison's four things to take from Screaming Frog). Fix or
    defer. CONFIRMED (39).
16. Overview doesn't open with a totals row (Crawled, Internal, External). Defer, but say so. CONFIRMED (24).
17. Dropping a `.sicrawl` on the app doesn't open it (`crawl/openFiles.ts:1` says "later"). Defer explicitly.
18. No signed in-app updater (`Updates.tsx:13`), proxy test, OS credential store or movable data folder
    (`v2-ui-architecture.md:1606-1614`, no step). Later, with a step named.
19. Hidden checks: not per crawl, and exports don't leave them out (discovery decision 2). Fix or defer.
20. No "All columns" export preset (discovery L158). Small.
21. No key hints in the grid footer; `--grid-foot-height` unused (`tokens.css:402`). Fine to leave.
22. The inspector's link grids have no export. Fine to leave.
23. URL search applies on Enter only, with no hint (`UrlsScreen.tsx:609`). Fine, or a hint. CONFIRMED.
24. "Clear the filters" also drops the sort. Small. CONFIRMED.
25. A false "site is answering slowly" alert in the first ~3 s of a Polite check. Small. CONFIRMED (14).
26. `document.title` is always "Shouting Iguana". Small. CONFIRMED.
27. A raw column id can reach the user ("The grid can't sort by 'title'"), via a hand-edited URL only. CONFIRMED (29).
28. A site-wide fault in Advanced Issues reads "1 page, <0.1%" where Simple says "The whole site". CONFIRMED (39).
29. Simple's sidebar says "Checking now" while only working out results. CONFIRMED (03).
30. Simple Home groups sites by host, ignoring the port. Edge case. CONFIRMED.
31. About has no licences; Diagnostics has no "Open logs folder". Small. PLAUSIBLE.
32. Stale docs: `v2-comparison.md` lines 15, 212, 217, 220 (no export, sort or column chooser) and 85, 223 (Advanced
    groups by site); the Simple doc's `si report` (built `si export report`) and `no-pages`/`not-analysed` (built
    `noPages`/`notScored`); the comparison's "later" items (SERP snippet, duplicates diff, Insight) in no backlog.

## 2. Definition of done (`v2-ui-brief.md:261-272`)

| Item | Verdict | Evidence |
| --- | --- | --- |
| Every state built and in Storybook | Partly | 488 stories; live, paused, stopped, out of date, never analysed, filtered to nothing, error, "No pages could be read" seen live. Stopped-early not as designed (gap 2). Skeletons never visible (loads too fast). Engine lost not tested in the browser. |
| Keyboard-only; actions in the palette; shortcuts listed | Mostly | A keys-only flow in each mode. 27 Tabs from Overview's top to the first Fix-first row (a skip link exists). Gap 8. |
| Light/dark, comfortable/compact, 1280x720 to 4K, 200% | Partly | Light, dark, forced colours clean in both modes. 200%: gap 13. Compact and 4K not checked. |
| axe clean; screen readers announce key changes | Partly | 0 violations on 8 routes x 2 modes x 2 themes. Live region announces checking, finished, working out the results, is checked. No real screen reader. |
| Budgets on the 100k crawl; checked on the real-site crawl | Partly | Overview 238 ms (1 s), URLs first page 124 ms (300 ms), sorts 51-81 ms, search 33-119 ms, real shell usable Home 0.57 s (2 s). Frame rate during a live crawl, memory, 1M rows not measured. |
| Playwright e2e and visual snapshots pass | Met | e2e 188 on this code. |
| Copy follows the glossary; no raw engine errors or ids | Partly | Gaps 5, 6, 7, 27. |
| The owner has seen it | Pending | Gallery re-shot and published 2026-11-14. |

## 3. Keyboard tables (`v2-ui-ia.md:302-395`)

Everywhere: Ctrl+K, Ctrl+N, Ctrl+Enter, Ctrl+E, Ctrl+F, Ctrl+comma, Ctrl+1-8, Simple's Ctrl+1-3, Ctrl+Shift+A, F7,
F8, Alt+Left/Right, Esc and `?` work (`?` with gap 6). Ctrl+O not applicable in the browser; Ctrl+P covered by
smoke:shell; mouse back/forward not tested.

Grids: arrows, Enter, Space, Shift+arrows, Ctrl+A, Ctrl+C (TSV with header), Home, End, PageDown, Ctrl+End (row
112,578), Shift+F10, header Up then Enter to sort and Shift+Enter to add a sort all work; the menu key not tested.
URLs and Links: Ctrl+F and Ctrl+Shift+C work (capped at 10,000 with a toast); Links' Enter inspects the cursor's end.

Inspector: tabs' arrows, Enter walks a link, Alt+Left/Right along the walk, Esc (clears a search first), edge
Left/Right/Home/End (320-728 px) work. Ctrl+F in Source: gap 8. Search's Enter/Shift+Enter, Up/Down when docked and
the rest of the viewer's table not tested key by key.

Sitemaps and robots.txt: tree arrows, Enter in the tree, on origins, in a sitemap's URLs and blocked URLs, Ctrl+F,
Enter on a rule line work. Right/Left on an index: partly (in the treegrid they move between cells; Left didn't go to
the parent from a cell). Ctrl+Shift+C on the origin's file not confirmed.

Simple: J/K/Enter, Esc on the breakdown, `?` showing this mode's keys work. Ctrl+Shift+C on a problem and Ctrl+E on
the Summary not tested.

## 4. Parity list (`v2-ui-discovery.md`)

29 built, 14 partly, 2 missing (signed updates, proxy test), 4 later by plan (sitemap XML generator, custom
extraction, list mode, JavaScript rendering: step 4), 10 dropped by recorded decision (Chromium overlay, save/clear
queue/optimise database, bypass list, private-network guard, block images, viewport, plugin feeds, Refresh). Partly,
the missing piece with no step: native macOS menu, open the logs folder, drag-and-drop, About's licences, installers
(Windows x64 only), Advanced library grouping, credential store, data folder, filter by/exclude value, hidden checks
per crawl and in exports, "All columns" preset, the workbook taking filters.

## 5. What is clearly good

- Simple is very close to its mockups (Summary, What to fix, one problem nearly pixel for pixel); the plain words
  read well.
- The real shell in Simple works end to end: started in 0.6 s, the check carried on with the window closed, the
  finish notice in Simple's words, back from the tray onto the Summary, an idle quit in 25 ms.
- The 100k crawl is fast everywhere (Overview 238 ms, grid 124 ms, sorts under 100 ms, Ctrl+End to row 112,578).
- The grid's keyboard model is complete; the inspector walks with history.
- The live crawl screens in both modes, with an honest time estimate; Continue can change speed.
- The score breakdown is clear and auditable.
- axe clean in 32 runs; forced colours and dark clean in both modes.
- Error and empty states are real, with a way out.

## Seen by the gallery re-shoot (same head, real shell)

G1. The Simple | Advanced switch is cut off while a crawl runs in Advanced at 1280 px (3 of 3; the bar overflows
1094 > 1056; search and Home don't give way first). G2. A Simple problem page and New crawl let the whole window
scroll (absolutely positioned `sr-only` spans, no positioned ancestor in `main`); the wheel over the sidebar scrolls
the app off screen and the bar loses 10 px. G3. A problem opened from a scrolled What to fix list opens part way down,
its back link out of sight. G4. The URLs row menu opens where the previous right-click was (the first at the window's
top-left). G5 = gap 11. G6. The Report screen's subtitle wraps to 3-4 lines at 1280 px; its frame keeps Windows'
classic scroll bar. Minor: URLs' search placeholder cut ("Search URLs an"); Advanced's sidebar says "1 crawling" for a
paused crawl; Home's "last checked" 14:51 against 14:13 in the breadcrumb (end against start?); once, a click on
"Filter by type" didn't open its menu (Enter did).
