# Brief: fresh-eyes review of round 4 as integrated (Simple's screens: Home, the checking screen, the Summary, What to fix, the Report, the score), read-only

Review round 4 of the Shouting Iguana v2 desktop app (React + TypeScript + Tailwind v4 + Radix + TanStack
Router/Query, in a Tauri 2 shell, with a .NET sidecar it reads over a localhost API). It is branch `ui/r4-integrate`
at `37a58c3`: three parts built separately (A `ui/r4-home`: Home, the checking screen, `ui/ScoreRing`, the Rust
notification; B `ui/r4-summary`: the Summary, What to fix, the score breakdown dialog; C `ui/r4-report`: the Report
in both modes, printing, the score on Advanced's Overview), then merged by an integration agent onto `ui/step-3` with
the hand-over UI (export dialog, shell `reveal_export_file`/`open_export_file`) and E7 (`pagesAnalyzed`). Review
`git diff e350053..37a58c3` (`e350053` is `ui/step-3` as merged in) and the code it touches. You did not write it and
nobody has reviewed it: each part was checked only by its builder, the integration by its integrator.

- Yours: `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\01db96e9-0efc-4b99-b15c-66d1db13a110\scratchpad\r4-review\`
  (it has `tmp` for `TEMP` and `TMP`; keep `checkpoint.md` there, updated at each phase).
- What was asked, in the repo at `docs/handoff/2026-11-13/`: `brief-round4.md` (the three parts),
  `brief-round4-integrate.md` (the merge), and `docs/handoff/2026-11-11/ui-slice-rules.md`. What the builders say they
  did: `checkpoint-r4-home.md`, `checkpoint-r4-summary.md`, `checkpoint-r4-report.md`, `checkpoint-r4-integrate.md`
  (same folder). Claims to check, not facts.

Work in a worktree of your own: from `C:\Projects\ShoutingIguana`,
`git worktree add --detach C:\Projects\ShoutingIguana\.claude\worktrees\r4-review 37a58c3`. **Edit, commit or push
nothing in any other worktree.** In your own you may run `npm ci` and checks (`app/`), on ports
`SI_DEV_SERVER_PORT=56874 SI_DEV_SIDECAR_PORT=56875 SI_PREVIEW_PORT=56876 SI_E2E_SIDECAR_PORT=56877`, Storybook
`SI_STORYBOOK_PORT=6456` (never 6406: Docker holds it). Copy the git-ignored fixtures first:
`cp -r C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3/app/design/data/fixtures app/design/data/`.
`npm run build:sidecar` builds the sidecar; `npm run dev` runs the app against copies of the real dev crawls.
Analyse the copies, never `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`. The engine's synthetic test site is the only
site you may crawl. If you use the Playwright MCP browser, use only your own ports and never close a tab that isn't
yours. **The orchestrator is running the full story and e2e suites on `r4-integrate` meanwhile**: run targeted
tests, single specs and the dev server only, never a whole suite (`npm test`, `test:stories`, `npm run e2e`), one
heavy thing at a time. **Stop only processes you started, by their own process id or your own ports.** Never run
`dotnet build-server shutdown`.

## Read first

`docs/v2-ui-simple.md` (all of it, especially "What round 4 builds" and "As built"), the mockups in
`app/design/simple/` (home, checking, summary, what-to-fix, report), `docs/v2-ui-ia.md` (Routes, Keyboard),
`docs/v2-ui-brief.md` (states, accessibility, budgets, definition of done), `docs/v2-ui-architecture.md` ("Simple mode
(round 4)", the hand-over/export sections), `docs/v2-sidecar-api.md` (library, score, export), `CLAUDE.md`.

Already known, don't report again: no updater; the data folder can't be moved; the design fixtures' regeneration
cross-check reads a gone `urlCount` field; `PageCount` can exceed `pagesAnalyzed` by Cloudflare's `/cdn-cgi/` pages
(by design: those pages aren't audited and their links count in no inlinks); the release build and `smoke:shell`
haven't been run on `37a58c3` yet (the orchestrator runs them).

## What to look for

Real defects, each with a concrete failure scenario, in order of weight:

1. **The seams the integration made**: the `overview` row (A's `SimpleOverview` dispatcher rendering B's
   `SummaryScreen`: running, analysing, finished, failed, stopped, a crawl with no analysis yet, a crawl deleted
   while open, a re-analysis); every replaced stand-in (A's `ScoreRing` in the Summary, the Overview and the dialog;
   B's `ScoreBreakdownFromRoute` from C's Overview score, its new `onCloseAutoFocus`); the Report's Save and
   Spreadsheet and the problem's "Save as a spreadsheet" going through the hand-over's `openExportDialog` (right
   choice, right cut, right file name, in both modes); the shell's ONE `reveal_export_file`/`open_export_file` and its
   grant (read `src-tauri`: commands, capabilities, `notify.rs`'s `Notice::new`); anything a part did that another
   part's code now contradicts; leftover dead code from a dropped side (C's `ExportItem`, B's `handOver.ts` pieces,
   stand-in CSS).
2. **Simple's screens on real data**: Home (rows, favicons, `pagesAnalyzed ?? pageCount` beside the score, row menu,
   first start, many crawls, a failed or empty crawl), the checking screen (live progress, pause/stop, the hand-over
   to the Summary with focus on its title), the Summary (score, counts, links to What to fix and the Report), What to
   fix (each problem, its pages, the jump to Advanced), the Report (both modes, printing, a crawl with no problems, a
   huge one). Numbers must agree across Home, the Summary, the Report and Advanced's Overview for the same crawl.
3. **The switch across round 4's routes**: every Simple route switched to Advanced and back, deep links and reloads,
   Back and Forward, Ctrl+1/2/3 in Simple and Ctrl+8 in Advanced, the finished toast's "Open the report" in both
   modes, the notification's words in each mode.
4. **The score**: the ring's value, band word and colour against `ui/score`'s `scoreWord`, the breakdown's sums
   against the engine's score, rounding, a crawl with no score, forced colours.
5. **Accessibility and the keyboard**: focus on every hand-over (checking -> Summary, dialog close, toast actions),
   names and roles of the ring and the rows, announcements once, forced colours, reduced motion, 200% zoom, print
   styles.
6. **Design-system and slice rules**: tokens only, components extended in `app/src/ui/`, Simple's words and UK
   English, no raw engine ids, every state in Storybook, tests that actually test (a test that passes with the
   feature removed is a defect).

Verify each finding before reporting it (a failing test, Playwright against `npm run dev` or Storybook, axe).
Mark each CONFIRMED (shown) or PLAUSIBLE (reasoned). No style preferences, and no speculation reported as a defect.

## Report back

A ranked list, most severe first, at most 30: severity (high / medium / low), file:line, one-sentence defect, the
failure scenario, CONFIRMED or PLAUSIBLE, and a one-line fix. Then a short "looks right" note. **Give the full list
in your final message** (the orchestrator saves it). Put proofs in your folder and name them. No file dumps. When
done, stop your processes and remove your worktree (`git worktree remove --force` on your own folder only).
