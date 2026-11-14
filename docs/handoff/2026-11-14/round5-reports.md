# Round 5: the builders' reports, condensed (for integration and review)

## Part B: `ui/r5-home` at `8e7f8c3`…`efed126` (ui/step-3 07ba6ba merged)

Gaps 3, 4, 15, 16, 17, 28, 31 (Open…), "1 crawling" for paused, 32 (`v2-comparison.md`). All built.

- Gap 3, one count: three nouns settled in `v2-ui-ia.md`: "URLs" = URLs crawled (site-version probes in,
  not-checked out); "URLs found" = the URLs grid's total (header now "264 of 6,286 URLs found"); "Pages" now
  excludes Cloudflare `/cdn-cgi/` endpoints (engine: `CrawlSchema.PAGE`, `NOT_CLOUDFLARE`, `CrawlWriter.IsPage`,
  `ReadOutcome`; reverses E7's decision), so the library's `pageCount` = `pagesAnalyzed` (100k: 101,106 everywhere).
  `e2e/counts-agree.spec.ts` on seeded, garden (4,521 / 6,286 / 265), 100k (110,251 / 112,577 / 101,106) and a
  paused-then-finished test-site crawl. The audit's 3,145 vs 3,144 not reproduced (live count settles after
  in-flight requests).
- Gap 4: Advanced Home (`screens/HomeScreen.tsx`, `screens/home/*`): running crawls pinned as live cards (Pause,
  Resume, Stop, Open), library grouped by site with older crawls folded, a status action per row, a row menu (Open,
  Continue, Crawl again…, Analyse again, Show in folder, Remove with Undo, Delete file… with confirmation), palette
  commands, visible Open… (Ctrl+O). "Foundation version" gone.
- Gap 17: drop a `.sicrawl` on the window, both modes (`crawl/crawlDrop.ts`, `crawl/CrawlFileDrop.tsx`), via
  `getCurrentWebview().onDragDropEvent` under the existing `core:event:allow-listen`: nothing needed from part D.
  Tested with a fake drop source only; never tried in the real shell.
- Gaps 15, 28: `checkReach` / `CountWithReach`: share of pages analysed (page checks) or URLs crawled (URL checks);
  site-wide checks read "Whole site". Gap 16: `overview/TotalsRow.tsx` (Crawled, Internal, External, Pages, Found,
  each its grid count). Sidebar `runningHeading` ("1 paused", "2 crawling, 1 analysing").
- Docs: `v2-comparison.md`, IA's "Room for what comes later" (SERP snippet, duplicates diff, Insight), architecture
  round 5 section, API "Engine changes round 5 made".
- Verified: typecheck, lint, format, scripts 44, storybook 499 x 2 themes (plain, forced, reduced) clean, e2e 196
  (2 skipped), counts-agree and home-library 60/60 at repeat 10, unit 3,479 (2 of 4 runs failed `GoToUrl.test.tsx`
  "looks a path up…" under load; passes alone; not B's code), engine 1,723, 100k bench green (peak 198 MB, +3 MB).
  Overview with totals 108-132 ms; row menu 4-16 ms. Tests written with or after the fixes, not seen failing first.
- Integration: one line in `layout/AppShell.tsx` (mounts `<CrawlFileDrop />`) and one in `layout/Sidebar.tsx`
  (running heading; part A changes the Simple half of the same line: a one-line conflict). Advanced Home uses
  `siteGroups` from `simple/home/sites.ts`, so A's host+port grouping reaches Advanced.

## Part C: `ui/r5-grid` at `8cb83aa` (ui/step-3 07ba6ba merged)

Gaps 8, 9, 10, 13, 19, 20, 21, 22, 23, 24, 27, G4, the placeholder, the Filter by type click, the workbook's
filters. All built.

- Gap 10: resize (edge drag, double-click resets; Shift+arrows on the header), move (header drag; Ctrl+Shift+arrows),
  pin (header menu: right-click or Shift+F10), kept per grid in `settings.gridColumns` (9 grids) and in saved views;
  URLs and Links keep order in the route's `cols`. Pointer events throughout (no HTML5 drag, so the shell's
  file-drop setting doesn't affect it).
- Gap 9: Filter by / Exclude this value (URLs, Links, a sitemap's URLs). G4: menus placed fresh on each opening (not
  reproduced in Chromium; seen in the release shell: check in the smoke walk). Gap 21: footer key hints on
  `--grid-foot-height`. Gap 24: Clear the filters keeps sort and columns. Gap 27: `queryRefusal.ts` names columns.
  Gap 23: Enter applies, with an Enter hint and a screen-reader description; placeholder "URL or title" fits.
  Filter by type: not reproduced in 7 sequences; guards added.
- Gap 13: below 480 px tall, URLs and Links fold presets, facets, filter bar and notes behind one button; 640x360
  shows 6 rows (0 before), 5 with the inspector (2 before).
- Gap 8: Ctrl+F from Source's tab or panel searches the source.
- Gap 19: hide a check "in this crawl" or "in every crawl" (badge, Undo); counts, lists, sidebar, the saved report,
  the Report screen and the workbook leave hidden checks out and say how many; the score stays the analysis's.
  Workbook takes the view's filters ("The checks listed: their spreadsheet"). Gap 20: All columns preset. Gap 22:
  the inspector's inlinks and outlinks export.
- Engine/API: `ReportOptions.HiddenChecks`, `ReportOptions.Checks`; export fields `hiddenChecks` (report, workbook)
  and `checks` (workbook); `GET /report?hiddenChecks=a,b`; `si export --hidden-checks`, `--checks`.
- Verified: typecheck, lint, format, unit 3,509 x 3 (under load), scripts 44, storybook 499 x 3 modes clean,
  sidecar, e2e 195 + the 3 updated 200% specs passing (2 skipped), r5-grid spec 100/100 at repeat 10, engine 1,729,
  100k bench green (peak 193 MB). Grid first page, sorts, menu-open and column-move not re-timed.
- Known: URL grid exports' issue-count columns still count hidden checks (the engine's URL summary, as on screen).
- Edits outside its area: `IssuesScreen.tsx`, `CheckDetail.tsx`, `OverviewScreen.tsx` (B's), `SiteRow.tsx`,
  `SummaryScreen.tsx`, `WhatToFixScreen.tsx` (A's), `ReportScreen.tsx`, `reportQuery.ts` (hidden-checks query; D owns
  the frame styling), `.si-grid-header` CSS, `ExportButton` icon-only variant, two fields in `settings/schema.ts`.

## Part A: `ui/r5-simple`: running

## Part D: `ui/r5-shell`: running
