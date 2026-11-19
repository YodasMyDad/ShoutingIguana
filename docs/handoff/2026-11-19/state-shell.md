# UX fix round, shell lane: state when stopped (2026-11-19)

Stopped early on the coordinator's word (usage). The lane is about 40% done. Nothing is half-edited: the tree is
clean and every commit typechecks.

- Branch `ui/ux-shell`, worktree `C:/Projects/ShoutingIguana/.claude/worktrees/ux-shell`, base `2a2cc140`.
- Head: **`1f2419f9`** (15 commits on the base). `git status` clean, no stash.
- Scratch: `C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/da833503-3c78-46eb-9e8a-d618294b5af4/scratchpad/shell-fix/`
  (`checkpoint.md`, `flows/`, `shots/`, `patch/`, `gate.sh`, `data/`, `state/`).

## Findings

Done 9, partly done 2, not started 13.

| Id | State | Commit | Notes |
| --- | --- | --- | --- |
| H-01 | **done** | `142847f2` (Shared), `fbccb469` | Tooltips never take the pointer, close when the pointer leaves, stay shut after a press; sidebar tooltips open to the right. |
| H-02 | **done** (scroll) | `5fd418b5`, `2bd6e023`, `165b3d0b` | `layout/scrollMemory.ts`. Not done: the notes banner's open state. The banner is now usually absent from Overview (H-11); where it still shows, its open state is still lost on Back. |
| H-03 | **done, differently** | `20f0fc99` (Shared), `ed2a4ea0` | Simple says where it landed; Advanced again from that Summary is `history.back()`. Not built: landing on the inspected page's entry in What to fix (the ruling doesn't ask for it). |
| H-04 | **partly** | `37ccdb41` | Exists: "Crawl again…" button in Overview's Not checked card (`overview/AnswerSections.tsx`, `SkippedChecks`). Missing: palette commands "Crawl again…" and "Continue" (`keyboard/useAppCommands.ts:169-309`); the link on a skipped check's page (Issues, the issues lane's file); the banner's own text where the banner still shows (`screens/issues/AnalysisState.tsx`, `PartialNotes`). |
| H-05 | **done** | `1f2419f9` | Row click, count links, sortable headers, older crawl's URL. Not done: the sort isn't remembered when Home is left (a module variable for it failed the lint rule, so it was dropped). |
| H-06 | **done** | `9c79cf75` | `compare/movedChecks.ts`, `ComparisonCard.tsx`. Left as a feature: a per-URL new / gone / changed-status list. |
| H-07 | **partly** | `770d616b`, `25bf2ec7` (Shared), `acd96aac` | Done: the headline's counts, the Passed card, the cards' header counts, the crawl's line, the rechecks' check and result. Missing: Alerts' "1 sitemap lists 240 URLs" (`overview/AlertList.tsx`; "Open Sitemaps" sits under it); the score sheet's rows (`simple/ScoreBreakdownDialog.tsx`, the simple lane's). |
| H-08 | not started | | |
| H-09 | not started | | The engine was not read, so whether JavaScript is run is still unanswered. |
| H-10 | not started | | |
| H-11 | **done** | `41a38cbc` (Shared), `37ccdb41` | |
| H-12 | not started | | Nothing was sent to the simple lane. The audit's A-12 fix list stands as the request. |
| H-13 to H-17 | not started | | |
| H-18 | **done** | `02362cac` | `overview/fixFirstExample.ts`; twelve rows. |
| H-19 | **done** | `770d616b` | Totals are one strip (`.si-totals`), inside "The crawl" once there is an answer. |
| H-20, H-21, H-22 | not started | | H-21 includes Home's Crawl button being enabled with an empty field. |
| H-23 | **done, differently** | `acd96aac` | The check's name and the result line are the controls, not the whole row; dialog 1,120 px, URLs are links. |
| H-24 | not started | | |

## Decisions, and why

- **H-01**: kept the tooltip rather than printing keys in the row, because Issues' count already sits at the row's right.
- **H-02**: own scroll memory keyed by history entry (`__TSR_key`) plus which mode's screen shows; written to
  `sessionStorage` on `pagehide`, so a reload keeps the place. The router's own restoration stays off.
- **H-03**: found on the way that `SegmentedControl` chose a segment on any focus, so a closing toast giving focus back
  to "Simple" switched the mode back. Now only arrow-moved focus chooses.
- **H-11**: the banner is hidden on Overview only when the analysis has no more notes than the card has reasons
  (on the garden crawl its two notes say exactly what the card's two groups say).
- **H-11**: the reason's plural ("these checks need", "to check them") and "of Shouting Iguana" are patched in the app
  (`overview/answer.ts`, `skippedReasonWords`), not in the engine's text.
- **H-19**: totals stay at the top only while there is no answer (never analysed, nothing crawled).
- **H-18**: the example URL opens the check with `inspect=<id>`. A CSS rule in `.si-fix-example` overrides
  `.si-url__full` so the URL's box is as wide as its path; it depends on `Url`'s internals (the data lane's).

## Files touched outside the lane's list

| Commit | Files |
| --- | --- |
| `142847f2` Shared | `app/src/ui/Tooltip.tsx`, `Tooltip.test.tsx`, `app/src/styles/components.css` (`.si-tooltip`) |
| `20f0fc99` Shared | `app/src/ui/SegmentedControl.tsx`, `SegmentedControl.test.tsx` |
| `41a38cbc` Shared | `app/src/screens/issues/AnalysisState.tsx` (optional prop `notes`) |
| `25bf2ec7` Shared | `app/src/routing/search.ts` (`passed`), `app/src/screens/IssuesScreen.tsx` (one line: `useState(search.passed === 1)`) |
| `ed2a4ea0` (not labelled Shared) | `app/src/mode/switchMode.ts`, `useModeGuard.ts`, `mode.test.tsx`: no lane's list names `app/src/mode` |
| several App commits | `app/src/styles/components.css`, `app/src/styles/tokens.css` (new rules and three tokens) |

## What ran, and what did not

Ran:

- `tsc -b`: on every commit through `gate.sh`; clean at `1f2419f9`.
- `eslint --max-warnings 0` and `prettier --check`: **only on the files of each commit**, clean.
- Vitest, by file or folder only (each file runs in two projects, so file counts are doubled):
  - `src/screens/HomeScreen*.test.tsx` after the last edit: 6 files, 68 tests passed.
  - `src/screens/rechecks`, `src/screens/OverviewScreen*`: 24 files, 252 passed (before `acd96aac`).
  - `src/screens/OverviewScreen*`, `src/screens/overview`, `src/styles`: 30 files, 394 passed (at `02362cac`).
  - `src/screens/compare`, `src/screens/OverviewScreen*`: 28 files, 256 passed (at `9c79cf75`).
  - `src/screens/IssuesScreen.test.tsx`, `src/routing`: passed in the batch run before `770d616b`.
  - `src/mode`, `src/ui/SegmentedControl.test.tsx`, `src/layout`: 30 files, 160 passed (at `ed2a4ea0`).

Did **not** run:

- `npm run lint` and `npm run format:check` over the whole app.
- The full `npm test`, and `main.test.tsx`.
- `npm run test:stories`, and any e2e spec.
- The mode and layout tests were not rerun after the later Overview and Home commits.

Tests rewritten to pin new behaviour: `OverviewScreen.test.tsx` (headline, severity cards, status codes count, crawl
line), `OverviewScreen.fixLoop.test.tsx` and `ComparisonCard.test.tsx` (the moves' words), `RechecksCard.test.tsx` (the
control pressed), `mode.test.tsx` (one line waits for its toast).

Flake: `mode.test.tsx` "from an Advanced-only screen, goes to the nearest… and Back returns" failed 4 of about 50 runs
at its "Advanced is on is visible" line while the machine was busy, then 0 of 28 (14 on the base's files, 14 on mine).
The toast was in the page but hidden. Cause not found.

## What I looked at in the live app

All at 2560 × 1380 on the garden crawl unless said. Shots are in the scratch folder's `shots/`.

- H-01: a real mouse path down the sidebar lands every click (`h01-tooltip-resting.png`, `h01-overview-full.png`).
- H-02: Back, the top bar's Back, Forward and reload all return to the scroll left, at 2560 and at 1280 × 720 (flow
  log only; `h02-before-click.png` and `h02-after-back.png` were taken, not looked at).
- H-03: from URLs with the 4xx filter, Links, Sitemaps, robots.txt and crawl settings, Simple shows the toast and
  Advanced returns to the same address; the toast's button works (`h03-simple-landing-toast.png`).
- Overview after H-04/H-11/H-07/H-19/H-18: `o4-ov-fold.png`, `o3-compare-card.png`, `o4-fixfirst.png`. The new header
  links' filters were checked against the sidecar: 4,359, 4,362 and 269 match their labels.
- H-23: `h23-dialog.png` (8 URLs whole, as links); a URL click lands on the check with `inspect=325`.
- H-05: `h05-home-older.png`; row clicks, each count and each sort were driven and logged.
- At 1280 × 720: only Overview's first screen, **before** the totals moved (`o2-ov-n-fold.png`). `o4-ov-n-*.png` were
  taken and not looked at.

Not looked at: Home after its last edit (the unsorted-column icon); anything in dark theme, high contrast or forced
colours; the rechecks dialog, comparison card and Home at 1280 × 720.

**The audit's ten tasks were not redone and there are no new click counts.**

## Known breakages and risks

- None seen. But the whole suite, the stories and the e2e specs were not run, so these are unchecked:
  - e2e specs and stories that pin Overview's five total cards, "2 fixed · 2 more", the "URLs" recheck button, or Home's
    row.
  - Any test elsewhere that relied on a segment being chosen by `.focus()`, or on a tooltip being hoverable.
- Stories were not updated (`OverviewScreen`, `HomeScreen`, `FixLoopAdvanced`, `RechecksAdvanced`). They typecheck.
- Docs were not updated (`docs/v2-ui-*.md` still describe the old Overview, comparison card and mode switch).
- Overview now makes up to twelve extra one-row reads, one per Fix first check.
- Expect merge conflicts with the issues lane in `IssuesScreen.tsx` and `AnalysisState.tsx`.

## Crawls, test sites, processes

- No crawl, recheck or test site was started. Nothing to remove.
- The scratch `data/` holds copies of five crawl files and `state/` the sidecar's state; left for whoever resumes.
- **The crawl ids in a copy are not the audit's** (they follow the file's path, whatever `fix-common.md` says). In this
  copy: garden `17ee95f3bea206bf` (earlier crawl `3c1f88dffc8f3fcc`), jitsunami `27e600ffdb7abfe4`, aptitude
  `371c3b4e30746942`, synthetic `327815ab342234a1`. `flows/lib.mjs` has them.
- My sidecar (49905) and Vite server (49904) are stopped; 49904 to 49907 and 5968 are free.

## Next five steps

1. Run the whole gates in the lane (`npm run lint`, `npm run format:check`, `npm test`), fix what the Shared changes
   and the new Overview and Home words break, then the stories and the e2e specs for Home, Overview and the mode switch.
2. The frame: H-10 (`layout/Sidebar.tsx:163`, `:275`: "This crawl's settings", keep the crawl's block on `/settings/*`),
   H-16 (`layout/CrawlSwitcher.tsx:138`: carry the search across), H-17 (`layout/TopBar.tsx`).
3. H-08 and the rest of H-04: palette commands and words (`keyboard/useAppCommands.ts`, `paletteSearch.ts`,
   `CommandPalette.tsx`).
4. H-09 (read the engine first), H-21, H-20, H-15 (KB and MB, never bytes or bits), H-24.
5. Redo the ten tasks at both sizes with click counts, look at the 1280 × 720 shots, update stories and docs, then
   H-12's write-up, H-13, H-14, H-22.

To resume: restart the sidecar and Vite as `fix-common.md` says, with `--data-dir` and `--state-dir` on the scratch
folder. Commit only through `bash <scratch>/gate.sh <message file> <paths…>`. Write patch scripts as files: a shell
heredoc containing backticks or `${}` breaks the tool.
