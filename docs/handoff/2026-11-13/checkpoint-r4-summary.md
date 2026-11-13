# r4-summary (Part B) checkpoint

Worktree C:\Projects\ShoutingIguana\.claude\worktrees\r4-summary, branch ui/r4-summary from e8176c6.

## Decisions
- PORTS: brief's 66874-66877 are invalid (>65535). Using 56874 (dev), 56875 (sidecar), 56876 (preview), 56877 (e2e
  sidecar), Storybook 7406, mockup server 56878 (python pid 48580, rooted at app/). env in scratch r4-summary/env.sh.
- NEVER use the shared Playwright MCP browser: I once navigated r4-report's tab by accident (restored with Back).
  Screenshots via app/.shot.mjs (untracked; delete at the end) -> scratch r4-summary/shots.
- overview row = simple/SummaryScreen; running/paused/analysing -> Advanced OverviewScreen stand-in
  (TODO(round4-merge) Part A). A and B may both set the overview row: report.
- Score ring stand-in simple/ScoreRingStandIn.tsx + .si-ring-standin CSS (TODO(round4-merge) Part A).
- handOver.ts: Save as a spreadsheet via api.startExport + pickSavePath + notifyProgress (TODO(round4-merge)
  hand-over slice). No "Open folder": shell has no reveal-a-path command.
- Where it is headings are generic by grouping (engine has no per-check column words).
- One fix callout uses plainOneFix; the mockup's middle sentence is check-specific, not available.

## Commits
- 354b05a severity word + plain check row (ui/SeverityWord.tsx, ui/PlainCheckRow.tsx, ui/severity.ts additions,
  SeverityMark label, SimpleSeverity.stories.tsx, styles/simple-results.css).
- f4f0462 Summary, breakdown dialog (simple/ScoreBreakdownDialog.tsx: ScoreBreakdownFromRoute for C), What to fix,
  one problem; rows overview + issues set. Unit suite 2974 green (both projects) at this commit.

- 401c5b3 tests; 39a14fa stories (Screens/Simple results, 51 stories with design-system ones; axe green light/dark
  after skeleton role fix); c676643 e2e/simple-results.spec.ts (6 green on seeded, garden, 100k); 3a055ee docs;
  a644a1a skipIf(TABLE_STUBBED) (tableStubbed.ts copied byte for byte from ui/r4-home).
- Part A built SimpleOverview (overview row) with TODO for my SummaryScreen: merge = take A's row, point its Summary
  at simple/SummaryScreen, drop my OverviewScreen stand-in branch and my .si-ring-standin (A's ScoreRing, same tokens).
- Dev server and mockup server stopped; app/.shot.mjs deleted; my MCP tabs closed.
- Measured (e2e, release build): Summary first paint with data 162-206 ms incl. navigation (seeded/garden/100k);
  mode switch 6-16 ms.

## Final checks (on ui/step-3 merge = already up to date, no new commits)
- typecheck, lint, format:check green. Unit x3 green (3012 passed, 28 skipped), one under storybook build load.
- build-storybook green. test:stories running...

## DONE 2026-11-13 (final, on merge 7564956 = ui/step-3 0a79de0)
typecheck/lint/format green; unit x3 (3166 passed, 28 skipped; run 2 under storybook-build load had 1 failure in
src/inspector/Inspector.test.tsx skeleton timing, not my code, passed runs 1 and 3); build-storybook; test:stories
405 x2 themes clean, --forced clean, --reduced-motion clean; node --test scripts 43 pass; build:sidecar ok; e2e:filled
29 passed; simple-results spec --repeat-each 10: 60 passed; 100k spec 6 passed; full e2e 159 passed 1 skipped
(urls-measure, not mine). Shots app/test-results/simple-results/. Report written.

## Verified so far
- typecheck, lint on my files, full unit suite once. Screens seen on the real garden copy (85 Good, 2/4/13, 64 of 83).

## Next
1. Unit tests for the screens (Summary states, What to fix keys/hash landing, problem page, dialog Esc).
2. Stories: every state, both themes, forced, 200%, reduced motion.
3. e2e: simple-results.spec.ts + sweep of every figure (seeded crawl; SI_E2E_CRAWL garden/100k).
4. Measure first paint on 100k (dev copy 832c732940d3ba2f) and mode switch.
5. Docs: architecture (round 4 B section, Components rows), ia if needed.
6. Merge ui/step-3, all checks, e2e last, screenshots to app/test-results/<name>/.
