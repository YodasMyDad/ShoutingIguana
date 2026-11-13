# round3-ui checkpoint

Worktree C:\Projects\ShoutingIguana\.claude\worktrees\round3-ui, branch ui/round3 (from ui/step-3 f61714d).
Ports 61874-61877, Storybook 6906. TEMP/TMP = scratch round3-ui/tmp. Commit trailer: Co-Authored-By: Claude Opus 5.5.
Brief: docs/handoff/2026-11-11/brief-round3-ui.md + ui-slice-rules.md. Design: docs/v2-ui-simple.md (switch, routes,
shell in Simple, top bar give-way order, settings table, new crawl table, keys).

## Phase 0 (done): read + setup
- npm ci done; fixtures copied.

## Plan / decisions
- mode in settings schema (`mode: 'simple'|'advanced'`, default simple); `useMode()` in src/mode/.
- Simple screens table: src/app/simpleScreens.ts, one line per route, pre-listed keys separated by comment lines so
  round-4 edits never touch adjacent lines. Report row = same screen both modes; sidebar Report shows when set.
- Advanced-only routes fixed list (urls, links, sitemaps, robots, crawl settings, settings sections crawl-defaults,
  analysis, shortcuts, diagnostics). Arriving there in Simple -> Advanced on + toast "Advanced is on" / Back to Simple.
  Guard runs on router onResolved + once at mount, never on mode change.
- Switching to Simple on an Advanced-only screen navigates to Summary (overview) / Settings General.
- Tests: test/setup.ts sets mode advanced before each test; ScreenApp default mode advanced; playwright config
  storageState si.settings {mode: advanced}; mode e2e uses empty storage.
- Home startCrawl must send saved defaults (POST /api/crawls fills left-out fields with ENGINE defaults, not saved).
- Secrets: memory only (form state); OS credential store = gap (no shell command), documented.
- Settings sections: one file each, discovered by import.meta.glob, so Analysis = one new file.

## Next
Phase 1: mode + table + router + guard + switch + commands.

## Phase 1 (done, committed 926adad): mode, table, guard, switch, sidebar, commands, top bar give-way (useGiveWay,
   9 steps incl. site shortened then gone), tests/stories default Advanced (setup.ts beforeEach dynamic import,
   preview loader, ScreenApp mode prop), playwright storageState ADVANCED_STORAGE (uncommitted -> in 3c2d1a5).
## Phase 2 (done, 3c2d1a5): crawl/options/{engineDefaults,form,words,useValidation,CrawlOptionsForm}.tsx,
   NewCrawlScreen (both modes), CrawlSettingsScreen (continue panel + used settings), ui/ChoiceCards, ui/Fold,
   mock persists PUT defaults. Visual check of /new both modes OK (shots in scratch/shots).
## Next: Settings sections (glob registry), Home startCrawl with saved defaults, alert actions targets,
   continueCrawl toast action, stories, unit tests, e2e, docs (architecture + ia), merge ui/step-3, all checks.
Mock dev server running in bg (task bbkn1itfn) on 61874 — stop by port at end.

## Phase 3-5 (done): settings sections 90b2994; tests a0609df; stories 22506b2; e2e specs + merge ui/step-3 (merge commit
   after e2e commit); fixes 07eff8c. e2e mode/new-crawl/topbar all green (switch median 7 ms). Give-way widths in
   app/test-results/round3-ui/give-way.json (running: simple 1140/1076/916/916/846/764/664/664/544; advanced
   1310/1250/1056/886/816/754/654/654/544).
## Next: docs (architecture Modes section + IA), then full checks on merged result: typecheck, lint, format:check,
   test x3 (one under load), build-storybook, test:stories (+forced, +reduced-motion), node --test scripts,
   build:sidecar (done post-merge), e2e full (+ --repeat-each 10 for timing-dependent: mode switch timing, topbar?).

## Phase 6 (in progress): final checks on the merged result (merge of ui/step-3 at be294da).
- green: typecheck, lint, format:check, node --test scripts (41), unit x3 (3rd under storybook-build load) 1388 pass,
  build:sidecar (post-merge), e2e mode/new-crawl/topbar (13+14 pass).
- running in bg (task bwyx022bp): build-storybook + test:stories x3 (normal, --forced, --reduced-motion) -> scratch stories.log
- next: full e2e, then --repeat-each 10 on mode + new-crawl specs; final topbar run for screenshots (test-results/round3-ui).
- docs committed 1ec25b8. Commits: 926adad 3c2d1a5 90b2994 a0609df 22506b2 (e2e) merge 07eff8c 1ec25b8 a06cd85 c1a5cd4

## DONE: all checks green on the merged result; screenshots in app/test-results/round3-ui (last run: topbar.spec). Report sent.
