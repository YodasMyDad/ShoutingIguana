# URL grid slice: checkpoint

Brief: `docs/handoff/2026-11-11/brief-urls-grid.md` + `ui-slice-rules.md` (in the worktree). Orchestrator's changes win:
- Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\urls-grid`, branch `ui/urls-grid`, cut from ui/step-3 at f61714d.
- Ports SI_DEV_SERVER_PORT=60874 SI_DEV_SIDECAR_PORT=60875 SI_PREVIEW_PORT=60876 SI_E2E_SIDECAR_PORT=60877
  SI_STORYBOOK_PORT=6806. TEMP/TMP = this folder's `tmp`.
- Commits `App: ...` ending `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Links findings: docs/handoff/2026-11-11/reviews/links-findings.md; fixer notes: checkpoint-links-fixes.md.
- Before final checks: `git merge ui/step-3`, run every check on the merged tree. Never push/merge elsewhere.
- Stay out of app/src/layout/, Settings, New crawl, Overview, Home (round3-ui, e3c-takeup agents).

## Design decisions (so far)
- Presets = columns only (cols in route); a preset reads as chosen while route's cols are its own. `view` param unused.
- Multi-sort: plain header press = that column alone (cycle asc, desc, none); Shift+press/Shift+Enter adds a key
  (max 3). aria-sort on the primary only; order number shown on each when >1 key.
- Row menu: new `MenuAt` in ui/Menu.tsx (DropdownMenu at a point, hidden span trigger), `grid/RowMenu.tsx`;
  GridCore onContextMenu gets a 4th arg (rows of selection + copy). Focus returns to grid.
- Saved views + "Show full URLs" in settings (schema.ts additive fields).
- Issues' pages "Show the page in URLs" = URLs filtered `url eq <url>` with inspect.

## Status
- Phase 0: reading done, npm ci (exit 0), fixtures copied.
- Phase 1 DONE (commits cddf9e2 grid pieces, 285eb6e URL screen). Files: grid/GridCore.tsx (GridMenuContext,
  onSort 3rd arg extend, sortOrder/headerDetail in types.ts, copyRows), gridNavigation (Shift+Enter add), sort.ts
  (nextMultiSort, sortFullWords), ui/Menu.tsx (MenuAt), grid/RowMenu.tsx (RowMenu, UrlRowItems) +
  grid/rowMenuState.ts (useRowMenu, rowsWords, rowMenuLabel, copyRowUrls) [name clash: rowMenu.ts vs RowMenu.tsx on
  Windows!], grid/savedViews.ts, settings/schema.ts (savedViews, showFullUrls), FacetMenu `unavailable`,
  ColumnChooser `options`, keyboard/registry 'URLs' group, api/queries urlGridFacets key/query (['grid','urlFacets']).
  screens/urls/: urlColumns.tsx (presets, widths, cells, chooser, filter cols), urlFacets.ts, urlGridParts.ts
  (legacy 9 columns for SitemapDetail), UrlFilterChips.tsx, useUrlFilterWords.ts, SavedViews.tsx, useSavedViews.ts,
  urls.test.ts. UrlsScreen.tsx rewritten (re-exports old names).
- Unit: 1300ish pass after test updates (GridCore onSort 3rd arg; URL tests: url-columns route, cols for outcome sort,
  "Response time" chip words, Analyse it in main).
- Phase 2 DONE: a16748c (row menus on Links/Issues/sitemap/robots, rowKey on Links/Issues/inspector, searchCommand
  Ctrl+F), 87aa81d (tests; each rowKey test SEEN FAILING without it: URLs, Links, Issues flat, Issues groups,
  inspector inlinks), d0d78d1 (stories: Screens/URLs 25, Grid pieces +4, Menus +1; test/urlsSidecar.ts; empty states
  via urls/urlPlaces.ts). Story check (filtered screens-urls, grid-pieces, menus) default: no violations.
- Dev running (task bqlz69ct0, ports 60874/60875): garden 60e586f9bbdf7187, 100k f67dd8e53cf9231d (analyse POSTed).
  Real app looked right at 1280x720: 15 rows x 7 cols (Overview), 14 with a chip row.
- Phase 3 DONE: 3751c4c e2e urls.spec + urls-sweep (9 pass on synthetic, sweep 66 figs), garden sweep 121 figs pass,
  100k sweep 152 pass; af13c83 docs (architecture "The URL grid", grid primitive bullets; IA URLs + keys);
  552a64f measure spec + numbers (15 rows; cols 7/6/5/8/8; presets 21-29 ms; sort 21-31; facet 18; column 37;
  Ctrl+End 29 ms; heap 97->97 MB). Logs in this folder: sweep-garden.log, sweep-100k.log, measure-100k.log.
- NEXT: stop dev (task bqlz69ct0, by pid on ports 60874/60875), merge ui/step-3, full checks: npm ci, typecheck,
  lint, format:check, unit x3, build-storybook, test:stories (3 modes, full), node --test scripts, build:sidecar,
  e2e full, e2e x10 on timing specs, screenshots last (urls.spec writes test-results/urls-grid).
- 669fd2b name dialog focus -> grid (seen failing without). Dev stopped by pid (49912 node, 36840 sidecar).
- MERGED ui/step-3 (be294da) -> 0d8acfc, clean, no conflicts (e5 export API, catalog fixes, analysis v8).
- Final checks running: checks/run1.sh (npm ci .. build-storybook), results in checks/summary.txt + logs.
- On merged tree: npm ci 0, typecheck 0, lint 0 (after 0217c16), format 0, unit x3 1362 pass/1 skip (3rd under
  story load), scripts tests pass, build:sidecar 0, build-storybook 0, stories 316x2 no violations (default,
  forced, reduced). First full e2e: 118 pass, 2 FAILED from my toolbars (states Tab order; inspector 200% docked
  bottom) -> fixed in b5465b3. First x10 broke because I ran a 2nd playwright concurrently (my mistake).
- run3.sh running: e2e full, x10 (urls, grid-zoom, urls-sweep), x10 two fixed tests, shots. THEN rerun measure spec
  on 100k (changed after its run) and report.
- DONE. run3: e2e full 120 passed (1 skipped = measure spec), x10 urls+grid-zoom+urls-sweep 130 passed, x10 two
  fixed tests 20 passed, shots 8 passed -> app/test-results/urls-grid (16 png). Measure rerun on merged tree OK.
  HEAD 925a4e4, tree clean, ports free. NOTE for report: closed a Playwright MCP page that was on localhost:62874
  (another agent's?) when closing the browser at the end.
- OLD NEXT (done above): row menu on Links / Issues pages / sitemap URLs / robots blocked URLs; rowKey for Issues pages, inspector
  link lists (+tests that fail without); Ctrl+F unify; UrlsScreen behaviour tests; stories; e2e + sweep; measure; docs.

## Review fixes (12 Nov evening): findings in ..\urls-grid-review\findings.md
- 01012ae (1,6,7,12,13,15) row menu holds rows (GridMenuContext.row, rows read at open); 6359844 (2,3,4,8,11,14)
  facets beside filter, saved views bytes 4KB/32KB, prefs write failure toast + unknown fields, shell compact write +
  size check (cargo test settings:: 5 ok; fmt, build, clippy ok), sortShown keeps shown keys; 2464a94 (5,9,10,16);
  4618296 e2e proof 12 (39.5px off without fix); e5a8b08 docs.
- Every fix's test seen failing without. ui/step-3 still be294da (no merge needed).
- ui/step-3 MOVED to 588b515 (E6): merged -> e516ffc, clean. run4 (checks4/summary.txt) on e516ffc: all green but
  full e2e 117/4 failed: 3 are ui/step-3's own (robots/sitemaps words "HTTP 404" vs app "404 Not Found"; specs and
  app code identical to ui/step-3), 1 mine (issues.spec URL grid cursor: URL cell copy button took focus) -> fixed
  6712cc0 (copyable={false}). run5.sh: unit, e2e, x10, sweeps, shots on 6712cc0. Then filtered story check
  screens-urls (cells changed) and report.
