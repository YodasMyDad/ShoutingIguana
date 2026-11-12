# Simple design: checkpoint

Brief: `..\brief-simple-design.md`. Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\simple-design`, branch
`ui/simple-design`. Never push/merge. Never edit `docs/v2-rebuild.md`.

## Phase 0: reading. DONE (2026-11-11)

Read: brief; v2-rebuild "Simple first" + "Where step 3 stands"; v2-ui-ia; v2-ui-design; v2-ui-brief; v2-ui-discovery;
clarity README, tokens.css, app.css, build scripts; directions BRIEF; data README; sidecar API (checks, library,
analysis, issue groups); catalog list (86: 17 Error, 35 Warning, 34 Notice); gallery shots 01, 02, 17, 22, 23.

## Real data (garden), from an analysed COPY: `crawls\garden.sicrawl`, extract in `data\garden.json`

- CLI built in the worktree: `engine/src/ShoutingIguana.Cli/bin/Release/net10.0/si.exe`.
- 82 of 86 checks ran, 4 skipped (orphan-page, not-in-sitemap: no sitemap; duplicate-site-version: versions not
  checked; near-duplicate-content: earlier version). 19 found something, 63 passed.
- 933 issues: 14 error, 78 warning, **841 notice** (the brief's 829 is stale: description-missing and h1-missing
  became Notices). Checks: 2 Error, 4 Warning, 13 Notice.
- Errors: internal-client-error 9 (all 404 pages), broken-internal-links 5 pages (9 targets).
- Warnings: image-too-large 61, description-duplicate 8, title-duplicate 6, slow-response 3.
- Notices: empty-anchor-text 265 (264 link to "/" = logo, template), image-missing-dimensions 230,
  description-too-long 210, title-too-long 86, description-missing 11, open-graph-missing 11, title-too-short 9,
  canonical-missing 8, low-word-count 7, canonicalised-page 1, h1-missing 1, security-headers-missing 1,
  sitemap-missing 1.
- 4,521 URLs fetched, 265 pages, 264 indexable, 6,286 found, 1,765 not followed; 31 Oct 2026 14:13 to 14:51
  (37 min 25 s), Polite. Engine defaults today: Standard speed, no URL limit.
- Fixtures aren't in this worktree (gitignored). Playwright: use ui-step-3's `app/spike/node_modules/playwright`
  (read only). No PDF rasteriser on the machine: paginate the report in the shoot script, check against page.pdf().
- Comparison doc: NOT there yet at 07:10 (only its checkpoint). Check again before finalising words/score/report.

## Decisions so far (each with why)

- Mode is an app setting, not a route. Shared routes render per mode (overview = Summary, issues/:check = problem,
  report, home, new, settings). Advanced-only: urls, links, sitemaps, robots, crawl settings, inspect.
- Switch: top bar, right end, segmented "Simple | Advanced", never shrinks; collapse order of the rest defined.
  Why: the top bar is the only chrome identical at every width; the sidebar collapses to icons under 1024 px.
  Same setting also in Settings > General; palette command; Ctrl+Shift+A.
- Going to an Advanced-only screen from Simple: only via controls that say "in Advanced"; turns Advanced on, toast
  "Advanced is on. Back to Simple". Switching to Simple on an Advanced-only screen goes to the crawl's Summary.
- Simple words: check a site (crawl), problem (check with issues), Serious / Worth fixing / Worth a look
  (Error / Warning / Notice, same shapes and colours), address (URL), couldn't check (skipped), Your sites (library).
- Score v1: start 100; each Serious or Worth-fixing check takes points by how much of the site it affects:
  a few (<1%) 2/1, some (1% to <10%) 4/2, many (10% to <50%) 7/3, most or whole site (>=50% or site-wide) 10/4.
  Notices cost nothing (many can't be cleared). A site-wide Serious fault keeps the score below 50. No score when no
  pages read / never analysed. Garden = 85 (2+4 serious, 3+2+2+2 worth fixing). Denominator: pages for
  subject=page, addresses checked for subject=url. Needs: `scope: site` on site-level checks; new check
  "most of the site is hidden from search" (so all-noindex can't score 100).
- Report: self-contained HTML, system font stack, plain words + technical name small; 10 example pages per problem.
- Sidebar Simple: Home; site block: Summary, What to fix (count = serious + worth fixing), Report; Settings.
  Advanced gains Report as the 8th section (Ctrl+8), 1 to 7 unchanged.
- Pause/Stop: top bar only in both modes (one place).
- Simple offers no options on Home; "Options…" opens New crawl (Simple): address, speed, stop after, check links to
  other sites. All else in Advanced.

## Next
1. Write build + mockups in `app/design/simple/` (data/garden.json trimmed, build/, simple.css, pages).
2. Screenshots (light/dark 1440x900; shell 1280x720, narrow 900, 200% zoom) -> screens/ + _gallery/simple/shots + index.json.
3. docs/v2-ui-simple.md; small edits to IA, design, glossary; .gitignore line for app/design/simple/screens/.
4. Commits. Final report.

## Phase 1: mockups built (2026-11-11, ~07:50). NOT committed yet

In the worktree, `app/design/simple/`:
- `build/extract.mjs` (reads an analysed COPY, writes `data/garden.json`), `build/build.mjs` (18 pages),
  `build/shoot.mjs` (`--shell`, `--report`, `--full`; needs `SI_PLAYWRIGHT=<ui-step-3>/app/spike`),
  `build/lib/{words,score,data,icons,ui,shell}.mjs`, `build/pages/{simple,screens,report}.mjs`, `simple.css`.
- Pages: home, home-first, new-simple, checking, summary, score, summary-perfect, summary-stopped, fix, problem,
  problem-template, report-screen, report (the document), settings-simple, settings-advanced, new-advanced,
  advanced-overview, advanced-live.
- Screens in `app/design/simple/screens/` (need a .gitignore line). Report prints to 11 pages (PDF), 12 in my
  paginator.

Changed since the first decisions (and why):
- Comparison doc read ("For Simple mode"): five fixes first (not three); a count and a share on every item; notices
  apart from to-dos; the score capped while an Error stands; "says what would move it".
- Score v1 final: Serious 2/4/7/10 by extent, 60 for a site-wide fault; Worth fixing 1/2/3/4; Worth a look 0.
  While any Serious stands the score can't be above 89. Bands: 90+ Very good, 70+ Good, 50+ Needs work, else Poor.
  Garden = 85. Nearly clean (invented) = 98. Badly broken (invented) = 36. Tiny 5-page site with one page missing a
  title = 89. Reference implementation: `build/lib/score.mjs`.
- Top bar gives way by the BAR's own width (container query), in a stated order; the switch never gives way.
- Engine gains per check: plainTitle, plainWhat (sentence 1 <= 120 chars), plainFix steps, fixer, plainPassed
  (the good news line: a problem's title reads wrong beside a tick), siteWide; plus plain category titles and
  plain skip reasons.
- Report: 5 examples per problem (3 for worth a look), worth-a-look entries compact; summary cut = page 1.

## Next
1. Look at the remaining screenshots (score, report-screen, settings x2, new x2, advanced-overview, perfect,
   stopped, home-first, dark theme spot checks), fix.
2. Write docs/v2-ui-simple.md; edit IA, design doc, glossary; .gitignore; README in app/design/simple.
3. Copy shots to `_gallery/simple/shots/` + index.json. Commit in small commits. Final report.

## Phase 2: DONE (2026-11-11). Everything committed on ui/simple-design, nothing pushed

Commits (on top of d497b1b):
- deeae75 Design: Simple mode is mocked on the garden crawl, with the switch in both modes
- 9a95de6 Docs: the Simple design: screens, the switch, the score, plain words, the report
- 21aaacc Docs: the IA, the design doc and the glossary gain Simple
- c20d02b Design: the report numbers its problems, and a passed check's line stays true ...

Deliverables:
- docs/v2-ui-simple.md (new); small edits to docs/v2-ui-ia.md, v2-ui-design.md, v2-ui-discovery.md; .gitignore line.
- app/design/simple/: 18 pages, README, simple.css, build/ (extract, build, shoot, gallery, lib, pages), data/garden.json.
- Screenshots: app/design/simple/screens/ (gitignored) and 51 PNG + index.json in
  C:\Projects\ShoutingIguana\.claude\worktrees\_gallery\simple\shots\.
- Every screenshot looked at (full size for the light pages, contact sheets in montage\ for dark and the shell).

Late changes: problems numbered in the report (= workbook sheet number); plainPassed must stay true when the check
had nothing to look at; report prints to 11 pages in Chromium's PDF (12 in the shoot script's layout).

Nothing is running. Nothing left to do but the final report.
