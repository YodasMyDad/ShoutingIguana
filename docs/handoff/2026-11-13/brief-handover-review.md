# Brief: fresh-eyes review of the hand-over UI slice (read-only)

Review the hand-over slice of the Shouting Iguana v2 desktop app (React + TypeScript + Tailwind v4 + Radix + TanStack
Router/Query, in a Tauri 2 shell with Rust commands, and a .NET sidecar it reads over a localhost API). It is branch
`ui/handover` at `fc7d35d`, cut from `ui/step-3` at a426614 and with `ui/step-3` at 0a79de0 merged in: review
`git diff 0a79de0..fc7d35d` and the code it touches. You did not write it and nobody has reviewed it.

- Yours: `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\8e7045a4-9ede-460a-be29-971bc406a6c0\scratchpad\handover-review\`
  (it has `tmp` for `TEMP` and `TMP`; keep `checkpoint.md` there).
- What was asked: `...\scratchpad\brief-handover-ui.md` and `docs/handoff/2026-11-11/ui-slice-rules.md`. What the
  builder says it did: `...\scratchpad\handover-ui\checkpoint.md`. Claims to check, not facts.

Work in a worktree of your own: from `C:\Projects\ShoutingIguana`,
`git worktree add --detach C:\Projects\ShoutingIguana\.claude\worktrees\handover-review fc7d35d`. **Edit, commit or
push nothing in any other worktree.** In your own you may run `npm ci`, every check (`app/`), `cargo test` in
`app/src-tauri`, and the release build with `npm run smoke:shell` (the architecture doc says how; it needs this
desktop session), on ports `SI_DEV_SERVER_PORT=53874 SI_DEV_SIDECAR_PORT=53875 SI_PREVIEW_PORT=53876
SI_E2E_SIDECAR_PORT=53877`, Storybook `SI_STORYBOOK_PORT=6156` (never 6406). Copy the git-ignored fixtures first:
`cp -r C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3/app/design/data/fixtures app/design/data/`. Analyse
copies of the dev crawls, never `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`. The engine's synthetic test site is the
only site you may crawl. If you use the Playwright MCP browser, use only your own ports and never close a tab that
isn't yours. Other agents are at work: one heavy thing at a time; **stop only processes you started, by their own
process id or your own ports.** Never run `dotnet build-server shutdown`. If a single-file vitest run says "failed
to find the runner", clear `node_modules/.vite*`: a stale cache.

## Read first

`docs/v2-ui-ia.md` (flow 5 "Hand over", Keyboard), `docs/v2-ui-simple.md` ("The report", "Settings in each mode"),
`docs/v2-ui-architecture.md` (the new "Hand over…" section, "Feedback", secrets, the shell's commands and
capabilities), `docs/v2-sidecar-api.md` ("Export (E5)", "Report (E5)", both E5 sections of engine changes, search,
favicon, analysis settings, E6's sections), `docs/v2-ui-brief.md` (states, accessibility, budgets), `CLAUDE.md`.

Already known, don't report again: the sitemaps list and robots.txt origins have no export kind; a URL view narrowed
by `linkEnds` can't be exported (both engine gaps); the inspector skeleton test is flaky (`Inspector.test.tsx:1303`).

## What to look for

Real defects, each with a concrete failure scenario, in order of weight:

1. **Security of the shell's new commands**: `reveal_export_file` and `open_export_file` (and the notification).
   Can page script make the shell open or reveal any file it names (path traversal, a symlink, a junction, an
   export id of another crawl, a finished export whose file was replaced since, a `.html` that is not the report,
   `file:` or UNC paths, a race between the check and the open)? The capability ACL: is anything granted wider than
   needed? Opening an `.html` report in the default browser: what can it do there?
2. **An export is exactly what the grid shows**: every grid's export against the grid's own read, row for row, with
   its filters, search, sort and columns, on garden and 100k copies; a saved view; a view that changed after the
   dialog opened; a running crawl.
3. **The job's life**: the toast and the in-progress list against the `export` event (ordering, an event before the
   POST's answer, `hello.exports` after a reload, two exports at once, the fifth refused), cancel, every engine
   problem and its sentence and action (do the actions do what they say?), a window hidden, a notification.
4. **The palette's search**: warm-up, per-keystroke requests (out-of-order answers, a slow first search, a crawl
   switched mid-typing), the matched part marked correctly (non-ASCII, case, `%`), Enter to the inspector, "All N in
   URLs" landing on N.
5. **Favicons**: once per crawl per session, the letter tile on 204, failure and an undrawable image, no layout jump,
   what a hostile image could do in the app's window (content type, CSP).
6. **Settings > Analysis**: validation keyed as the API sends it (limit name, `settings`, `limits[n]`), put back one
   and all with Undo, crawls reading out of date, a save while an analysis runs, the shared preferences.
7. **Links -> URLs and `linkEnds`**: counts land; the route's `links` param read back; a saved view with it.
8. **Accessibility and the keyboard**: the dialog (focus in and back, Esc), the toast (announced once, not per
   progress tick), Ctrl+E on every grid, forced colours (re-run every story forced: the builder fixed one story
   after its forced run and didn't run all 409 again), reduced motion, 200% zoom.
9. Design-system and slice rules; tests that actually test.

Verify each finding before reporting it (a failing test, Playwright, a script, a shell run). Mark each CONFIRMED or
PLAUSIBLE. No style preferences, and no speculation reported as a defect.

## Report back

A ranked list, most severe first, at most 25: severity, file:line, defect, failure scenario, CONFIRMED or PLAUSIBLE,
one-line fix. Then a short "looks right" note. **Give the full list in your final message** (you can't write a file
named `findings.md`). Put proofs in your folder and name them. When done, stop your processes and remove your
worktree (`git worktree remove --force` on your own folder only).
