# Brief: integrate round 5 (finishing step 3) onto `ui/step-3`

Read `C:\Projects\ShoutingIguana\.claude\worktrees\_handoff\2026-11-11\ui-slice-rules.md` first: every rule holds.
Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Read `C:\Projects\ShoutingIguana\CLAUDE.md`
too: round 5 changed the engine, and its rules hold (only `Storage/` writes; never rename an issue id; the synthetic
site's expected counts).

Round 5 fixed every gap of step 3's closing audit, in four parts built at once on four branches. You merge the four
and join them where they meet, so the app works as one. You build nothing new except what joining them needs.

## Where you work

- Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\r5-integrate`, branch `ui/r5-integrate`, cut from
  `ui/step-3` (head in the message that launched you; it exists, fixtures copied). Ports `SI_DEV_SERVER_PORT=60874
  SI_DEV_SIDECAR_PORT=60875 SI_PREVIEW_PORT=60876 SI_E2E_SIDECAR_PORT=60877`, `SI_STORYBOOK_PORT=7206`, DevTools
  60878. Ports 50000-50059, 50870-51825 and 56376-56475 are reserved by Windows here.
- Scratch folder `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\a539aa23-e856-44c1-bd21-ca484e8740e2\scratchpad\r5-integrate\`
  (make `tmp` inside for `TEMP` and `TMP`); keep `checkpoint.md` there.
- Never push, never merge your branch anywhere, never touch another worktree. Stop only processes you started, by
  their own process id or your own ports. One heavy thing at a time. If vitest says "failed to find the runner",
  move `node_modules/.vite*` aside (a stale cache).

## Read first

- `docs/handoff/2026-11-14/brief-round5.md` (the parts and their edges), `closing-audit-findings.md` (the gaps), and
  `round5-reports.md` (each part's report, condensed, with its integration notes). Each part's full checkpoint is in
  the same scratchpad: `r5-simple\`, `r5-home\`, `r5-grid\`, `r5-shell\` `checkpoint.md`.

## What to do

1. Merge `ui/r5-simple` (A), `ui/r5-home` (B), `ui/r5-grid` (C) and `ui/r5-shell` (D), in that order, resolving every
   conflict so each side's behaviour and tests stand. Engine conflicts: keep both sides' changes and re-run the
   engine tests and the 100k bench (B changed what a page is; A added the start-address choice and analysis on stop;
   C added hidden checks and checks to the report and workbook).
2. **Join where the parts meet** (from the reports), each with a test:
   - B's Advanced Home adds `https://` itself (`HomeScreen.tsx`, around line 62): use A's `startUrlFor`
     (`crawl/actions.ts`), so a bare address starts where the site answers in both modes.
   - A's analysis on stop makes remove, delete and continue answer "busy" while a just-stopped crawl is analysed:
     B's Home row actions and running cards (and Simple's) must show that state and offer what the design allows
     (wait, or cancel the analysis), never a raw refusal.
   - The paused heading: A built it in `crawl/runningWords.ts`, B with `runningHeading` in `layout/Sidebar.tsx`. Keep
     ONE, with both modes' words (Simple: Checking now / Paused / Working out the results; Advanced: "2 crawling, 1
     paused", "1 analysing") and both parts' tests.
   - `layout/Sidebar.tsx`, `layout/AppShell.tsx` (B's `<CrawlFileDrop />`), `layout/ModeSwitch.tsx`,
     `ReportFrame.tsx` (A's `printWhenLoaded`), About and Updates: D owns them; keep A's, B's and C's lines in D's
     versions.
   - C's hidden checks and A's stopped-check analysis and Simple words: hidden checks stay out of Simple's counts,
     the Summary and What to fix, the report and Home's figures; the counts test (B's `e2e/counts-agree.spec.ts`)
     must still pass with a check hidden in this crawl and with one hidden in every crawl.
   - C's column layouts and B's counts: the URLs header ("N of M URLs found") with C's folded toolbars at 200%.
   - Drop to open: B listens to `getCurrentWebview().onDragDropEvent`; D's shell takes every drop and emits
     `shell://drag-drop` (`ipc.onDragDrop` in `app/src/shell/ipc.ts`, payload in `round5-reports.md`). Keep ONE path
     (D's, unless the real shell proves B's also fires and is the simpler): no double open. Prove it in the real
     shell.
   - Window titles (D, gap 26) name sites without the port: use A's host-and-port naming (gap 30).
   - Watch `inspector.spec.ts:251`, `GoToUrl.test.tsx` "looks a path up…" and `SimpleHome.test` under load: each
     flaked once on a part's branch. If one fails in your runs, find why and fix it rather than rerunning.
   - `overview/emptyCrawl.ts`'s robots sentence takes A's robots states.
   - Anything else a report lists for another part.
3. **Docs as one account**: `docs/v2-ui-architecture.md`'s round 5 sections, `v2-ui-ia.md`, `v2-ui-simple.md`,
   `v2-sidecar-api.md` and `v2-comparison.md` where two parts wrote about the same thing.
4. **The real shell**: build the release (`npm run build:sidecar`, `npm run tauri build -- --no-bundle`) and run
   `npm run smoke:shell` (Advanced and Simple walks, as D left it). Then walk by hand in the release build, with a
   screenshot of each: the mode switch with a crawl running at 1280 px (G1), the row menu after several
   right-clicks on URLs (G4), Filter by type with one click, a `.sicrawl` dropped on the window, a column dragged,
   resized and pinned. These are the things the browser couldn't prove.

## Checks, all seen green by you on the final commit

From `app/`: `npm ci`, typecheck, lint, format:check, `npm test` three times (one under load),
`node --test "scripts/**/*.test.mjs"`, build-storybook, `test:stories` (also `-- --forced` and
`-- --reduced-motion`), build:sidecar, `e2e:filled`, the full e2e last; each part's new e2e specs with
`--repeat-each 5`. In `app/src-tauri`: `cargo fmt --check`, `cargo clippy --all-targets -- -D warnings`,
`cargo test`. Engine: `dotnet test engine/ShoutingIguana.Engine.slnx`, `si bench --pages 100000`, `si readbench
<crawl> --gate`. Then `smoke:shell` and the hand walk above on a release build of the final commit.

## Report back

Short: commits; each conflict and how it was settled; each join and its test; the shell walk with its screenshots;
test counts; what was verified and what was not.
