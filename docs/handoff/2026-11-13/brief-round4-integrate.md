# Brief: integrate round 4 (Simple's screens) onto `ui/step-3`

Read `docs/handoff/2026-11-11/ui-slice-rules.md` in your worktree first: every rule holds. Commits end with
`Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

Round 4 of the Shouting Iguana v2 desktop app (React + TypeScript + Tauri, with a .NET sidecar) was built by three
agents at once, each on its own branch, each leaving stand-ins for the others' pieces. Meanwhile the hand-over UI
(shared export dialog, toast and the shell's export commands) was merged into `ui/step-3`. You merge the three
branches and replace every stand-in with the real piece, so Simple works end to end. You build nothing new except
what joining them needs.

## Where you work

- Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\r4-integrate`, branch `ui/r4-integrate`, cut from
  `ui/step-3` (head in the message that launched you). Ports `SI_DEV_SERVER_PORT=51874 SI_DEV_SIDECAR_PORT=51875
  SI_PREVIEW_PORT=51876 SI_E2E_SIDECAR_PORT=51877`, `SI_STORYBOOK_PORT=5956` (all below 65535; never 6406).
- Scratch folder `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\8e7045a4-9ede-460a-be29-971bc406a6c0\scratchpad\r4-integrate\`
  (has `tmp` for `TEMP` and `TMP`); keep `checkpoint.md` there.
- Never push, never merge your branch anywhere, never touch another worktree. Stop only processes you started, by
  their own process id or your own ports; never close a Playwright MCP tab that isn't yours. One heavy thing at a
  time. If a single-file vitest run says "failed to find the runner", clear `node_modules/.vite*` (a stale cache).

## Read first

- The round 4 brief: `...\scratchpad\brief-round4.md`.
- Each part's report, in its checkpoint: `...\scratchpad\r4-home\checkpoint.md` (Part A, `ui/r4-home`),
  `...\scratchpad\r4-summary\checkpoint.md` (Part B, `ui/r4-summary`), `...\scratchpad\r4-report\checkpoint.md`
  (Part C, `ui/r4-report`).
- The hand-over UI as merged: `docs/v2-ui-architecture.md` section "Hand over…", and `app/src/export/`.
- `docs/v2-ui-simple.md` (the design) and `docs/v2-ui-architecture.md` "Modes, Settings and New crawl (round 3)"
  (the mode table, `filled` project, `e2e:filled`).

## What to do

1. Merge `ui/r4-home`, `ui/r4-summary` and `ui/r4-report` into your branch, in that order, resolving conflicts so
   every side's behaviour and tests stand.
2. **The mode table's `overview` row**: A set it to its dispatcher `simple/SimpleOverview` (checking screen while
   running or analysing, then the Summary in place); B set it to `SummaryScreen`. Keep A's dispatcher and put B's
   `SummaryScreen` in its finished branch; drop B's running/analysing stand-in that falls back to Advanced's
   Overview. The hand-over from checking to the Summary (focus to the Summary's title) must hold.
3. **Every `TODO(round4-merge)` goes**, each replaced by the real piece:
   - B's `simple/ScoreRingStandIn.tsx` and `.si-ring-standin` CSS -> A's `ScoreRing` (`app/src/ui/ScoreRing.tsx`).
   - C's `screens/overview/OverviewScore.tsx` ring and band words -> A's `ScoreRing`.
   - C's `screens/overview/ScoreBreakdownStandIn.tsx` -> B's breakdown dialog (`ScoreBreakdownFromRoute` or its
     component), one dialog in both modes.
   - A's `SimpleOverview` TODO -> B's Summary (item 2).
   - B's `simple/handOver.ts` and C's Save and Spreadsheet of everything -> the hand-over's `app/src/export/`
     pieces (`openExportDialog(offer, { choiceId: 'report' | 'workbook' | … })`, `crawlFileChoices`,
     `beginExport`, the toast and the in-progress list). One export flow in the app.
   - **The shell**: C added a `reveal_export_file(exportId)` command and the hand-over added
     `reveal_export_file` and `open_export_file`. Keep ONE implementation (the hand-over's, reviewed and judged
     safe: it takes only an export id, asks the engine, acts only on a done job's `.csv`/`.xlsx`/`.html`) and one
     capability grant; C's callers use it. `cargo fmt`, `clippy -D warnings`, `cargo test` green, and the
     capabilities test lists both commands.
   - Anything else a part's report lists as a stand-in or "once X merges".
   `git grep -n "TODO(round4-merge)"` must be empty at the end.
4. **Links between the parts**, each tested end to end now that all three are in: Home's rows and the finished toast
   to the Report; the Summary's Open the report; the Summary's counts to What to fix; Advanced's Overview score to
   the breakdown; the checking screen's hand-over to the Summary; Ctrl+1 to Ctrl+3 in Simple, Ctrl+8 in Advanced.
5. **Favicons**: A's Home rows used the letter tile because the favicon UI wasn't merged: use the shared favicon
   component the hand-over added.
6. **Tests the parts skipped in the `filled` project** because another part's screen was missing: run them now,
   in both projects where they can, and say which still can't and why.
7. Docs: the architecture doc's round 4 sections as one account, and `docs/v2-ui-simple.md` where what was built
   differs from the design (each part listed its differences: keep them, settled).

## Checks, all seen green by you on the final commit

From `app/`: `npm ci`, typecheck, lint, format:check, `npm test` (both projects) three times (one under load),
`node --test "scripts/**/*.test.mjs"`, build-storybook, `test:stories` (also `-- --forced` and `-- --reduced-motion`),
build:sidecar, `e2e:filled`, the full e2e last; the three parts' own e2e specs with `--repeat-each 5`. In
`app/src-tauri`: `cargo fmt --check`, `cargo clippy --all-targets -- -D warnings`, `cargo test`. Then `npm run
smoke:shell` on a release build of the final commit: the walk passes, including the report's print step.

## Report back

Short: commits; each conflict and how it was settled; each stand-in and what replaced it; the links walked end to
end; test counts; what was verified and what was not.
