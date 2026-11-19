# State of the simple lane after the fix session (2026-11-19, stopped on the coordinator's word for usage)

- **Branch** `ui/ux-simple`, worktree `.claude/worktrees/ux-simple`. **Head `24907078`** (a `WIP:` commit, see "Not run").
  Not pushed. Tree clean. `ui/ux-simple-report` is merged in (fast-forward, no conflict).
- Servers stopped (Vite 49914, sidecar 49915 killed by PID; nothing on 49916/49917).
- Scratch: `…/scratchpad/simple/` under session `848ba86b-…` (flows, shots, logs `e2e-1.log` … `e2e-3.log`, data copy).
  In that data copy the garden crawl is `8cfb9447fd619f11` (previous `bb2b94305e127078`: the earlier copy had to be
  named `garden-old.sicrawl`, the lower id is taken as the current crawl when two share a time).
- Note: the session scratchpad root is shared by sibling agents. Early on I created `data/`, `state/`, `logs/`, `bin/`
  at its root and deleted then restored `data/garden-a.sicrawl` there before moving to `simple/`. If another lane used
  the root `data/`, check it.

## Findings

| Finding | State |
| --- | --- |
| S-01 back link, sidebar item, breadcrumb, Ctrl+2 | **Done**, unit tests (5) |
| S-02, S-03, S-04, S-07 "Where to fix it" | **Done**: page-first card for dead addresses and link faults, Open page at the link's words, shared links one row, unlinked list with sitemap, links everywhere in "Where it is", open button always visible; one merged row on What to fix and Fix these first; the link fault's page leads with the card |
| S-05 Copy the list / spreadsheet | **Copy the list done** (Page / Link / leads to). Spreadsheet: as the report lane left it (first linking page in its own cells; **one row per link not built**) |
| S-06 | **Partly**: "What's wrong with one page?" search on What to fix, done. **Top-bar search finding pages in Simple: not done** (needs `keyboard/CommandPalette.tsx`, the shell lane's). "Worst pages" list: a feature, for the owner |
| S-08 | Engine lane's (`changes/**`), untouched |
| S-09 false one fix | **Done in the app** (callout, tag "Fix once", sitemap sentence). **Report (engine `ReportDocument.cs:782`) not changed** |
| S-10 | **Partly**: What to fix keeps "Show all"/"Nothing wrong" open and its scroll across Back. **Not done**: mode switch keeping place, "See it in Advanced" adding history, plain title in Advanced (need `app/modeRoutes.ts` / `mode/switchMode.ts`, shell lane) |
| S-11, shell A-12 report | Done by the report sub-lane (merged); its e2e (`report.spec.ts` 6, `simple-links.spec.ts` 4) **ran and passed**. Its loose ends in `state-simple-report.md` stand (37 unmirrored details, stories fixture) |
| S-12 what got worse | **Done**: row line with ▲/▼ in the warning colour, nothing when unchanged, "N got worse" link on the Summary. "See what changed" does not filter the list (**not done**) |
| S-13 | **Done** for long titles/descriptions (whole text, cut part grey, length and overshoot). Short titles say their length only: the engine's "shorter than about 20 characters" measures pixels, **engine wording left** |
| S-14 | **Partly**: jumps in the head, link fault's list first. **Not done**: folding the explanation, a filter box and "Show all" in long lists |
| S-15 | **Partly**: "and N more" under a recheck's list, addresses as links. Words are the engine lane's branch. "Check again" form title: shell lane's, not done |
| S-16 | **Done**: setting names after the plain words, robots.txt line with Copy |
| S-17 | **Done** (lines dropped; "couldn't run" said once on the Summary) |
| S-18 | **Done**: fixed column shares, page to 1,400 px, "Where they end up", "3 addresses that end at 2 addresses" |
| S-19 | **Done**: "+6 to your score", one figure on the page, "8 addresses". Score dialog untouched |
| S-20 | **Partly**: rows say "than last time" when both checks share a day. Switcher and Home rows not changed |
| S-21 | **Partly**: folded names are links. Home's counts as links, "Update the results", export dialog words, score dialog's second table: not done |
| Section 5 words | **Partly**: those in the app's own strings. Those in the engine's catalog texts ("rules file", "forward", "main address", "can be sent without the secure connection", "extras in search results", the skip reason) **left**: they need `IssuePlainTexts.cs` and a recapture of `app/src/test/simple/engine-checks.json`, which I did not touch |

## What ran

- Engine at the merged head (engine unchanged since): `dotnet build -c Release` clean; `dotnet test`: Engine 2,428, Sidecar 333,
  Cli 45, 0 failed.
- `npm run typecheck`, `npm run lint`, `npm run format:check`: clean, one commit before head (before the last e2e-only edits and
  the forwarded/target-size change was linted with `eslint src/simple e2e`: clean).
- Vitest `src/simple` + `src/screens/report`, project `app`: 39 files, 645 passed, at the tree of head minus the two e2e edits.
  Both projects earlier (at `fbc0f8d2`): 62 passed, 16 skipped files; 1,083 passed, 205 skipped tests.
- e2e, 12 specs (report, simple-links, simple-results, simple-r5, simple-home, fix-loop-simple, counts-agree, mode,
  what-matters, handover, found-on, evidence), freshly built sidecar: last run **53 passed, 4 failed**
  (`found-on.spec.ts:166`, `simple-results.spec.ts:223` and `:433`, `what-matters.spec.ts:179`).
- Owner's task on the garden copy, Simple: Home → site → first job → Open page = **3 clicks at 2560 × 1380 and at
  1280 × 720** (2 from the Summary), opening `/blog/best-rattan-patio-sets/#:~:text=Buy%20Now`. Shots in
  `scratchpad/simple/shots/` (`w-o*`, `n-o*`, `w-*`, `n-*`); I read about ten of them.

## Not run

- **The whole Vitest suite** (`npx vitest run --maxWorkers=4`) at any head of this lane.
- e2e after the last two edits (`e2e/fixList.ts` now treats redirect sources as forwarded, not pages; the short
  description's pattern in `simple-results.spec.ts`): they are meant to clear the 4 failures, **unverified**.
- Stories (`test:stories`), the 100k bench / `verify-engine.sh`, smoke:shell, Linux Docker.
- The other eight audit tasks with click counts; dark theme; the built shell (opening in the system browser).
- Tests not seen failing first: most new tests were written with their code (S-01's were seen red).

## Next steps

1. Run the 12 e2e specs (env: `SI_PREVIEW_PORT=49916 SI_E2E_SIDECAR_PORT=49917`); fix what is left of the 4.
2. Run whole Vitest, lint, format check, stories; then amend or follow the `WIP:` commit.
3. Engine words (section 5, S-13's short-title limit, S-09 in the report) with a fixture recapture at integration.
4. S-06 palette, S-10 mode switch, S-15 form: with the shell lane's files after the merge.
5. One row per link in the sheet (engine lane's `GridExport.WriteGrid`).

## Merge notes

- `Shared:` `c1b7c50d`: `app/src/layout/Sidebar.tsx`, `layout/TopBar.tsx` (the crumb `Here` takes `listOf`),
  `app/goToSection.ts` (`check: undefined`). Shell lane owns all three.
- Not in a `Shared:` commit but outside Simple: `app/src/ui/PlainCheckRow.tsx` (+ test: tag "Fix once"),
  `app/src/test/setup.ts` (`forgetPlaces()` before each test), `app/src/test/simpleSidecar.ts` (a `/links` route),
  `docs/v2-ui-simple.md`, e2e specs `fix-loop-simple`, `found-on`, `simple-results`, `what-matters`, new `e2e/fixList.ts`.
- `components.css`, `tokens.css`, `Url.tsx`: **not touched**. All new styles are in `styles/simple-results.css`
  (it overrides `.si-open-url`'s opacity for Simple's lists; if the data lane changes that class, check it).
- Simple uses its own `simple/OpenLink.tsx`, not `Url`'s `openUrl` prop.
- `plainRows.test.ts` (report parity) passed: no sentence in the parity file was changed. `plainTargetLine` lost
  "loads when scrolled to" (not in the parity file; the engine's report may still say it).
- Uses the links grid with `to.id in […]` and `from.title` (readbench already times that query shape).
