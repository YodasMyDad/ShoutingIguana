# Brief: round 5's last merge and the whole verify

Read `C:\Projects\ShoutingIguana\.claude\worktrees\_handoff\2026-11-11\ui-slice-rules.md` and
`C:\Projects\ShoutingIguana\CLAUDE.md` first. Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

Round 5 (finishing step 3) is integrated on `ui/r5-integrate` (`008b4b2`) and its review's findings were fixed on two
branches: `ui/r5-fix-app` (F1) and `ui/r5-fix-engine` (F2). Their reports, condensed, are
`docs/handoff/2026-11-14/round5-fixes-reports.md` in the `ui-step-3` worktree (read it there; the findings are in
`reviews/` beside it). You bring both onto `ui/r5-integrate` and prove the whole thing green.

## Where you work

The integration worktree `C:\Projects\ShoutingIguana\.claude\worktrees\r5-integrate`, branch `ui/r5-integrate`.
Ports 60874-60877, Storybook 7206, DevTools 60878. Scratch folder
`C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\a539aa23-e856-44c1-bd21-ca484e8740e2\scratchpad\r5-final\`
(make it with `tmp`; keep `checkpoint.md`). Never push. You are the only agent running; still one heavy thing at a
time. Stop only processes you started.

## What to do

1. Merge `ui/r5-fix-app`, then `ui/r5-fix-engine`. Both changed the export path's engine files for hidden checks
   (`Engine/Export/ExportFields.cs`, `Sidecar/Api/ExportRequests.cs`, `Cli/Commands/ExportCommand.cs`, possibly
   `Sidecar/Crawls/GridData.cs`): keep both (F1: grid exports take `hiddenChecks`; F2: a single check's export takes
   them and is numbered as the workbook is), with both sides' tests.
2. F2's ask: `app/src/export/offers.ts`, the `kind: 'check'` request of "This check" sends the crawl's hidden checks,
   with a test seen failing first (the exported file's numbering or check list).
3. `CrawlSettingsScreen.test.tsx` "continue without the headers" timed out under load once (`findBy`, 1 s default):
   find why it is slow (as F1 did for `GoToUrl`: a one-time cost, a missing await, a real slowness) and fix the cause,
   not the limit.
4. The whole verify, on the final commit, from a fresh log folder in your scratch folder, one script at a time:
   `bash docs/handoff/2026-11-13/tools/verify-engine.sh <repo> <logs>/engine` then
   `bash docs/handoff/2026-11-13/tools/verify-app.sh <repo>/app <logs>/app 60874 7206` (the tools are in the
   `ui-step-3` worktree; `<repo>` is your worktree). Then `e2e:filled`; `cargo fmt --check`, `cargo clippy --all-targets
   -- -D warnings`, `cargo test` in `app/src-tauri`; each round 5 e2e spec with `--repeat-each 5`; `npm test` once more
   under load. Then the release build (`build:sidecar`, `tauri build --no-bundle`) and `smoke:shell` (both walks).
   A failure: find the cause and fix it (with a test), then re-run that check and anything it touches; never call a
   rerun's pass the fix.

## Report back

Commits; each conflict and how it was settled; the two items above with their tests; every check with its counts and
exit codes (the summary lines of both scripts); `smoke:shell`'s steps; what was not verified.
