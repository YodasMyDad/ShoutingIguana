# handover-ui checkpoint

Worktree C:\Projects\ShoutingIguana\.claude\worktrees\handover-ui, branch ui/handover (cut at a426614).
Brief: scratchpad\brief-handover-ui.md. Ports dev 64874 sidecar 64875 preview 64876 e2e-sidecar 64877 storybook 7206.

## Plan
- Shell (Rust): commands reveal_export_file / open_export_file (take exportId, shell GETs /api/exports/{id}, needs status done,
  extension .csv/.xlsx/.html/.htm), export notification when window hidden (live follower parses `export` event).
- app/src/export/: export store (jobs by id, from events + hello.exports + start answer), dialog (format/choice),
  toast (progress), tray-of-exports list, file names, problem sentences, Ctrl+E command, per-screen export targets.
- Palette search via POST /search. Favicons (one request per crawl per session). Settings > Analysis section.
- Links "Show these pages in URLs" / linkEnds: check URL grid report first.

## Status
- Phase 0: reading docs (done: brief, slice rules, API export/report/search/favicon/analysis settings, IA flow 5, simple report).

## 2026-11-13 progress
- 88dab95 Shell: reveal_export_file/open_export_file by export id (shell GETs /api/exports/{id}, done + csv/xlsx/html only),
  export notification when window not in front (live/mod.rs export_ended, notify::export_notice). cargo test 114+3 pass, clippy clean, fmt.
- 40df06b App export: app/src/export/{exportWords,exportStore,exportOffer,exportFlow,ExportDialog,ExportToast,exportToasts,
  ExportsInProgress,startExports,offers,useExportCommand.ts,ExportButton.tsx}. Wired: Urls, Links, SitemapDetail, RobotsFileView,
  Issues (check/group/report/workbook), Overview (report/workbook); SitemapList & RobotsOrigins give 'not exportable' offer.
  Settings gained exportFolder, exportFormat. main.tsx startExports(); AppShell ExportDialogHost; Sidebar ExportsInProgress.
  Verified manually in browser dev (tab 6, isolatedContext handover-ui): garden URLs xlsx export worked, toast "Saved".
- Engine gaps: no export kind for sitemaps list / robots origins; export doesn't take linkEnds.
- dev server running: npm run dev on 64874/64875 (background task bc0khqnzc).

## Next
palette search, favicons, Settings>Analysis, linkEnds (URL slice did NOT build it), stories, e2e, measures, docs, merge ui/step-3, checks.

## progress 2
- 4ad1974 palette search (keyboard/urlSearch.ts, CommandPalette urlSearch prop, Advanced only)
- 6963cd8 favicons (crawl/favicons.ts, crawl/CrawlFavicon.tsx; Sidebar/TopBar/HomeScreen)
- b7cb9ad Settings > Analysis (screens/settings/sections/Analysis.tsx, analysisLimits.ts)
- 305bccf linkEnds: Links "Show in URLs" menu, URL grid `links` route param + banner (screens/urls/linkEnds.ts, routing/search.ts parseLinkEnds)
- mode.test fixed (analysis section exists now). Inspector skeleton test flaky under full-suite load (passes alone).
- stories: export/Export.stories.tsx, keyboard/PaletteSearch.stories.tsx, Settings stories (Analysis*), Navigation favicons. test:stories filtered: no violations.
## Next: e2e/export.spec.ts + handover.spec.ts (+ xlsx reader), measures (SI_E2E_MEASURE on 100k), docs (architecture + IA), merge ui/step-3, full checks.

## progress 3 (final checks)
- e2e/export.spec.ts, e2e/handover.spec.ts, e2e/files.ts, e2e/handover-measure.spec.ts committed; new specs 9/9, repeat-each 10 of timing tests 40/40.
- Measures (100k): Ctrl+E->dialog 18-24ms; keystroke->results 27-34ms; 112,577 URLs xlsx Export->Saved 889ms UI vs 862ms engine, 10.1MB.
- docs committed. ui/step-3 local = a426614 (already merged; origin older).
- typecheck/lint/format ok; unit 2x green (3186), 3rd under load: Inspector skeleton test flaky (pre-existing, passes alone).
- node --test scripts 43/43. test:stories all running (bg bdnqqwriq). Then --forced, --reduced-motion, build:sidecar, full e2e last.

## DONE (final)
- Merged ui/step-3 @0a79de0 -> fc7d35d. On the merge: typecheck/lint/format ok; unit app 1593 + full 3186 pass (Inspector "skeleton only once" flakes ~1 in 3 full two-project runs; passes alone 9/10, app-project 1/1); scripts 43/43; build:sidecar ok; full e2e 162 passed, 2 skipped (measure specs).
- Storybook: build ok; test:stories 409x2 themes no violations; reduced-motion no violations; forced: 1 problem found (analysis-refused story threw, no QueryClient) -> fixed af067c1, my 4 story groups rerun forced: no violations. Full forced rerun of all 409 after the fix NOT done.
- Screenshots: app/test-results/handover/ (after the last e2e).
