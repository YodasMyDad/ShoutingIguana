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

## Part A: `ui/r5-simple` at `7f910d0` (ui/step-3 07ba6ba merged)

Gaps 1, 2, 6, 7, 11/G5, 12, 14, 25, 29, 30, G3, "last checked", 32 (Simple doc). All built.

- Gap 1, scheme: the UI's `withScheme` always added `https://`. Engine `StartAddress.ChooseAsync` asks https and
  http at once (10 s max, no extra headers), https if it answers at all, else http; new `POST
  /api/crawl-options/start-url`; Simple Home and New check use it (`startUrlFor` in `crawl/actions.ts`). The
  2-minute wait was robots.txt's RFC 9309 retry for an https origin that never answered: gone for bare addresses,
  kept (and shown) for an explicit `https://` that refuses. Robots states each get their own sentence on "No pages
  could be read"; "stay out" only when the rules refuse.
- Gap 2: analyse when it stops (not on first open): the sidecar analyses a crawl the user stopped (not one stopped by
  quitting, interrupted or failed). Simple's Summary: dashed "so far" ring with Continue, the toast said again with
  results, Open the report only with results; Advanced's Overview "Score so far". Consequence: remove, delete and
  continue answer "busy" while a just-stopped crawl is analysed; cancelling the analysis frees it.
- Words (6, 7, 29): palette and `?` sheet, every `exportWords.ts` line, export and refusal toasts, saving list, About
  and Updates ("Site checker"), the Settings note, the switch's hint, the sidebar heading (Checking now / Paused /
  Working out the results; Advanced "2 crawling, 1 paused"). `simple/simpleWords.test.tsx` (20 tests) fails on the
  glossary's Advanced-only words.
- G5: toast actions go under the text below 200 px (always for two); text 293-326 px both themes, 100% and 200%.
- Gap 12 "The one cause is shown" / "Showing 25 of 126 causes", the real origin; gap 14 the Summary's duration (start
  to finish, labelled, for older crawls) and Save as PDF (`/report?print=true`, prints once loaded); gap 25 no slow
  alert before 10 of the site's own answers; gap 30 sites by host and port; G3 a problem opens at its top and Back
  restores the list's scroll (`simple/scrollPlace.ts`); "last checked" = the start everywhere.
- Verified: typecheck, lint, format, scripts 44, unit 3,495 (one run under the engine tests had 15 timeouts in grid,
  inspector and overview tests; pass alone), storybook 494 x 3 modes clean, sidecar, e2e 188 + 3 `simple-home`
  failures fixed in `7f910d0` (file 5/5; full e2e NOT re-run after), new spec 30/30 at repeat 10, engine 1,434,
  269 and 28, 100k bench green (194 MB). Not verified: a real screen reader, the Tauri shell.
- Integration notes: B's Advanced Home (`HomeScreen.tsx:62`) still adds `https://`: use `startUrlFor`; B's Delete
  and Continue right after a stop must handle the analysing ("busy") state; A also did the paused heading in
  `crawl/runningWords.ts` (B did it with `runningHeading` in `Sidebar.tsx`: keep one); A changed
  `e2e/overview.spec.ts`'s stopped-crawl assertion; `overview/emptyCrawl.ts`'s robots sentence could take the robots
  state. In D's files: `layout/ModeSwitch.tsx` (one hint line), `layout/Sidebar.tsx`, `ReportFrame.tsx`
  (`printWhenLoaded`), About and Updates labels.

## Part D: `ui/r5-shell` at `4e513b6` (ui/step-3 07ba6ba merged)

G1, G2, G6, gaps 5, 18, 26, 31, Simple in `smoke:shell`. All built.

- G1: the top bar re-measures when a part changes size or a late font arrives. `e2e/shell-frame.spec.ts` (1280,
  1024, 200%) passed before the fix too (Chromium doesn't reproduce it); the proof is a new `smoke:shell` step: the
  release build at 1280 px with a crawl running in Advanced gives way (search, Home, date first), switch whole.
- G2: the shell's root and `main` are positioned; an e2e over every route, both modes, 1280x720 and 200%, fails if
  the window scrolls (seen failing: 161 px on crawl screens at 200%, 449 px on Simple's New crawl).
- G6: the Report toolbar's subtitle gets 360 px (one line at 1280); the report frame's copy gets the app's scroll
  bars, the saved HTML stays the engine's (`report.spec` checks).
- Gap 5: the page tells the shell its mode with its preferences; Simple: "Still checking…", tooltip "checking site,
  N addresses checked", "Stop checking and quit? You can continue the check later from Your sites."; Advanced
  unchanged except an analysis alone says "Still analysing". Rust tests both modes; seen in the real shell.
- Gap 18: updater `tauri-plugin-updater` 2.14.0 through the shell's own commands; Settings › Updates checks on
  request, shows what's new, installs on the user's word, never during a crawl or analysis; public key in
  `tauri.conf.json`. Release needs secrets `TAURI_SIGNING_PRIVATE_KEY` and `TAURI_SIGNING_PRIVATE_KEY_PASSWORD`, a build
  with `--config '{"bundle":{"createUpdaterArtifacts":true}}'` and a `latest.json` on the latest release (format in
  the architecture doc). Key pair at `%USERPROFILE%\.shouting-iguana\updater.key` (+ `.password`), not in the repo.
  Credential store `keyring` 3.6.3 (proxy password and header values as `defaults` or `crawl:<id>`; Continue sends
  what was kept). Data folder: a switch, not a move (reasons in the doc): Change folder…, Go back, Use the usual
  folder; refused while anything runs; `Sidecar::relaunch`. Proxy test: engine `POST /api/proxy/test` (9 engine + 7
  sidecar tests), "Test the proxy" beside every proxy field.
- Gap 26: `document.title` "Issues · site · Shouting Iguana" in the mode's words; the shell copies it to the window.
  Site names lack the port (the shared crawl-name helper: use A's host+port naming).
- Gap 31: Diagnostics "Open the app's log folder", "Open the engine's log folder"; About lists 434 packages (npm,
  Cargo, NuGet, .NET runtimes, fonts) with licences, regenerated by a Vite plugin each build (a Tauri build fails if
  a source can't be read).
- `smoke:shell` walks Simple (first start, a check, tray, back to the Summary, Report, Updates, quit): 17 + 8 steps
  pass on the release build.
- Drop: the shell emits `shell://drag-drop` with `{type: "enter"|"drop", paths, crawlFiles}` or `{type: "leave"}`;
  the page listens with `ipc.onDragDrop` in `app/src/shell/ipc.ts`. The shell takes all drops (HTML5 drop never reaches
  the page).
- Verified: typecheck, lint, format, unit 3,493 x 2 green (one run under load: a `SimpleHome.test` timing flake,
  passes alone), scripts 49, Rust 134 + 3, clippy, MSRV 1.90 builds, storybook 498 clean (forced: a proxy story had
  never rendered, fixed; `check-stories` now fails a story that didn't render), e2e 199 + 1 failure
  (`inspector.spec.ts:251`, then 54/54 at repeat 3), shell-frame 120/120 at repeat 10. Engine tests green in the
  fork's run (1,437, 274, 28), not re-run; bench not run. Not verified: Linux CI's new `libdbus-1-dev`; installing a
  real update (needs a release with `latest.json`). Real-shell screenshots: `app/test-results/r5-shell-smoke/`.
