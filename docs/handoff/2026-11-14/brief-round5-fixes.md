# Brief: fix round 5's review findings, two agents at once

Read `C:\Projects\ShoutingIguana\.claude\worktrees\_handoff\2026-11-11\ui-slice-rules.md` and
`C:\Projects\ShoutingIguana\CLAUDE.md` first: every rule holds, including the engine's. Commits end with
`Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

Round 5 (finishing step 3) is integrated on `ui/r5-integrate` and was reviewed by two fresh reviewers. Their findings
are in the repo: `docs/handoff/2026-11-14/reviews/round5-review-r1-findings.md` (the app, R1-1 to R1-12 and "Also
noted") and `round5-review-r2-findings.md` (engine, API, shell, R2-1 to R2-11). The owner's word is "fix everything":
fix every finding, the "Also noted" items and the two the integration left. **For each: reproduce it first (a test
that fails), fix it, see the test pass.** If a finding turns out not to be a defect, show why with evidence and leave
it. Read the reviewers' proofs in the session scratchpad (`r5-review-1\`, `r5-review-2\`) where they help.

## Where you work

`S5` = `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\a539aa23-e856-44c1-bd21-ca484e8740e2\scratchpad`.

| Fixer | Worktree | Branch (cut from `ui/r5-integrate` at the head in your launch message) | Ports (dev, sidecar, preview, e2e sidecar; Storybook; DevTools) | Scratch |
| --- | --- | --- | --- | --- |
| F1 (app) | `.claude/worktrees/r5-fix-app` | `ui/r5-fix-app` | 61874-61877; 7606; 61878 | `S5\r5-fix-app\` |
| F2 (engine and shell) | `.claude/worktrees/r5-fix-engine` | `ui/r5-fix-engine` | 62874-62877; 7706; 62878 | `S5\r5-fix-engine\` |

Worktrees exist with fixtures copied. Make your scratch folder with `tmp` for `TEMP` and `TMP`; keep `checkpoint.md`.
Ports 50000-50059, 50870-51825 and 56376-56475 are reserved by Windows here. Never push; commit on your branch only.

## F1: the app (`app/src/`, `app/e2e/`)

- R1-1 to R1-12, and both "Also noted" items (the URLs grid's Issues column and its exports leave hidden checks out,
  as every other count does).
- R1-1 also in Simple's `SiteRow` (same pattern): a test for each that deletes the crawl the dialog was opened for.
- R1-2, R1-7 and the Issues column may need the engine's grid filter or URL summary to take something new ("is not X
  or is empty", hidden checks). Prefer a form the engine already supports; if it can't, F1 changes the engine's grid
  query and filter code itself (`engine/` grid/filter/URL-summary readers only, with engine tests, `v2-sidecar-api.md`,
  and `dotnet test` green). F2 does not touch those files.
- R2-1's app side: Home and Simple's quick start send the crawl defaults with secrets restored
  (`api.crawlOptionDefaults()`) to `start-url`.
- R2-2: after a successful delete-file, forget `crawl:<id>` in the credential store (`keepSecrets(\`crawl:${id}\`,
  {})`), and when a Remove's Undo expires.
- Integration's leftover 1: Advanced Home names a site by host AND port (two crawls of one host on different ports
  are two named groups), updating every e2e lookup by `site()` that needs it.
- Integration's leftover 2: find why the `GoToUrl` tests take seconds under load (each renders the whole app and types
  into the palette) and make them fast and deterministic, then put their time limit back to the suite's default.
- Own: `app/src/` except `app/src-tauri/`; `app/e2e/`; `docs/v2-ui-*.md` for what you change.

## F2: the engine, the sidecar API and the shell (`engine/`, `app/src-tauri/`, `app/scripts/`)

- R2-1's engine side (407, the proxy's own 5xx and tunnel errors are no answer), R2-3, R2-4, R2-5 to R2-11.
- R2-4: the stopped notification's words in both modes, as the page says them.
- R2-6: decide with the brief's intent ("a crawl the user stopped is analysed") and fix code, comment and
  `v2-sidecar-api.md` to agree.
- R2-10: refuse the data folder switch and an update install while an export runs, in plain words.
- R2-11: `smoke:shell`'s Simple walk checks the tray tooltip and menu labels as they change (analysing included).
- Stay out of the engine's grid query, filter and URL-summary readers (F1 may change those).
- Keep `dotnet test`, `si bench --pages 100000`, `si readbench <crawl> --gate`, `cargo fmt/clippy/test` green.
- Own: `engine/`, `app/src-tauri/`, `app/scripts/`, `docs/v2-sidecar-api.md`, the architecture doc's shell section.
  Touch `app/src/` only for an API type the engine change needs.

## Both

Before your final checks, `git merge ui/r5-integrate` (it may have moved). Then all green and seen by you: from `app/`
typecheck, lint, format:check, `npm test` (three times, one under load), scripts tests, build-storybook,
`test:stories` (and `--forced`, `--reduced-motion`), build:sidecar, the full e2e; F2 also the engine checks above,
cargo, and `smoke:shell` (both walks) on a release build at the end. One heavy thing at a time: the other fixer runs
at the same time.

## Report back

Each finding by number: what was wrong (or why it wasn't), the fix, the test seen failing then passing. Commits.
Checks run. Anything you need from the other fixer. What was not verified.
