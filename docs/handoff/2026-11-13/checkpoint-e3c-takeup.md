# Checkpoint: e3c-takeup (UI takes up E3c, 9 items)

Brief: `C:\Projects\ShoutingIguana\.claude\worktrees\e3c-takeup\docs\handoff\2026-11-11\brief-e3c-takeup.md` + `ui-slice-rules.md`.
Worktree `.claude/worktrees/e3c-takeup`, branch `ui/e3c-takeup` from f61714d. Ports 62874-62877, SB 7006.
Commits end `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Never push/merge out. Before final checks:
`git merge ui/step-3` into my branch, run everything on merged result. Items 7, 8 need `npm run smoke:shell`.
TEMP/TMP = scratch `tmp`. npm ci done, fixtures copied.

## Decisions
- Item 1 (probes): status-code card, live AND done, shows the site's own (figure less siteVersions part); class/code
  links add `source notIn siteVersion` (NOT_PROBE). notChecked line = site's own, link [NOT_CHECKED, NOT_PROBE].
  New line: probes with an outcome, link [source in siteVersion, outcome isNotEmpty]; words "N URL(s) the crawler
  tried for the site's other versions (http or https, www or not) isn't/aren't counted here". Done: probe part from
  a URL-grid rows read under [PROBE] (columns statusClass,statusCode,outcome; few rows), in statusFacetsQuery.
  Done "found but not crawled" line unchanged (all URLs with no outcome). Crawled stat stays `crawled` (all, probes in).
- Item 2: Home/StatusPill/toasts use crawledUrlCount / live `crawled` (fallbacks for older engines).

- Item 3: CrawlFacts sentence " It spent X crawling, not counting pauses or the time between runs." / null -> "How
  long it spent crawling isn't known: an earlier version ... didn't keep the time." / undefined -> nothing.
  Chart: progressSeries uses history.runs: earlier points via crawlingAt(stretches), this run offset by earlier
  crawlingMs; runStarts -> LineChart `markers` (dashed `.si-chart__marker`); Elapsed = offset + runMs.
- GOTCHA: python writes must use newline='\n' (CRLF crept in once; fixed).

## Status
- [x] 1, 2: f41c43a (14 new tests, seen failing without). [x] 3: bd06146 (6 tests seen failing without).
- [x] 4: 4712ffd (reads/tick 3 -> 1 in unit test; 4 tests seen failing). [x] 5: c52e779 (8 seen failing).
- [x] 9: 368326d (6 seen failing; singular "The 1 check counted passed.").
- [x] 6: 25cecdc (mock app 640x360: main scrolled 832/584, Site 54px -> fits, Site 160; e2e test added, not yet run).
- DONE 12 Nov 22:05: merged ui/step-3 at be294da (6ae232f), 27e8286 last. All checks green on merged result
  (typecheck, lint, format, unit x4 incl. under load 1350 pass, stories x3 286 no violations, scripts 43, build:sidecar,
  e2e 113 pass, live walk + home zoom --repeat-each 10 = 20/20). Shots app/test-results/e3c-takeup/. Report sent.
- ALL 9 ITEMS COMMITTED (up to b062548). 7: f4799d0 + acdf844 (webkit parts; final after shots
  scratch/shots/shell-after, 14/14 PASS smoke-after4.log; before shots shell-before). 8: 3f5789d tooling + doc;
  numbers: PWS 19-20@30s, 28@60, 31-32@120, 33-34@180; WS 80-81 -> 95-96; private bytes 316-321 MB.
  NEXT: stop vite mock (port 62874), git merge ui/step-3, full checks, e2e, shots to app/test-results/e3c-takeup/.
- 7 committed f4799d0 (Tabs tests seen failing). Before shots: scratch/shots/shell-before (04-inspector shows classic
  arrows + bar under tabs). Before smoke run: tray step FAILED because my 30-s sample used page.evaluate on the
  suspended page -> fixed to fetch directly; after-build + smoke running (task bv0w56n0r, logs smoke-after.log).
  Docs arch+IA edited (uncommitted, CRLF working copy kept). e2e live walk test added (uncommitted, not run).
- [~] 7 (old note): base.css `* {scrollbar-width: thin}` + forced-colors auto; .si-tabs scrollbar-width none;
  Tabs.tsx scrolls active tab into view; Tabs.test.tsx. Release "before" build running (at HEAD 25cecdc frontend,
  vite step done before CSS edit) -> log scratch/tauri-build-before.log. Then smoke:shell --shots before; rebuild;
  shots after. Rust target seeded by copying ui-step-3's target/release (read only).
- [~] 8: uncommitted: smoke-shell `--tray-stay <s>` + shellMemory() (lib/shell.mjs, tested) measuring working set,
  private working set, private bytes of shell+descendants minus sidecar. Need runs + doc edit (arch doc line ~119).
- NEVER use the Playwright MCP browser: it is shared with other agents (I navigated another agent's tab once by
  mistake, 60874). Use own node playwright scripts (copy into app/scripts/.tmp to resolve the package, delete after).
- Vite mock server running on 62874 (background task b1zytb8em); stop by port at the end.
- e2e walk of live figures of live figures (paused crawl) still to write; docs to update
  (v2-ui-architecture.md, v2-ui-ia.md) at the end.
- REVIEW ROUND DONE: aa55c34, all checks green (unit 1372 x3, stories 294 x3, e2e 114, scripts 43).
