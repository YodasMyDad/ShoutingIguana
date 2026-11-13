# Brief: the closing audit of step 3, in both modes (read-only)

Shouting Iguana v2 is a desktop SEO crawler: a .NET engine with a sidecar API, and a React + TypeScript app in a
Tauri 2 shell. Step 3, the UI, is about to be called finished. The owner set the bar on 2026-11-01: "super slick
enterprise level UI and UX ... really really good", and on 2026-11-10 asked for a Simple mode "super easy for anyone
to understand" with Advanced one switch away. Before step 3 is called finished, you audit what is built against
what was designed and promised, in both modes, and list every gap. You change nothing.

## Where you work

- Branch `ui/step-3` at the head named in the message that launched you. Make your own detached worktree: from
  `C:\Projects\ShoutingIguana`, `git worktree add --detach C:\Projects\ShoutingIguana\.claude\worktrees\closing-audit <head>`.
  **Edit, commit or push nothing in any other worktree.**
- Your folder: `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\8e7045a4-9ede-460a-be29-971bc406a6c0\scratchpad\closing-audit\`
  (has `tmp` for `TEMP` and `TMP`; keep `checkpoint.md` there, and put proofs and screenshots there).
- Ports `SI_DEV_SERVER_PORT=50874 SI_DEV_SIDECAR_PORT=50875 SI_PREVIEW_PORT=50876 SI_E2E_SIDECAR_PORT=50877`,
  Storybook `SI_STORYBOOK_PORT=5856`. Copy the fixtures first:
  `cp -r C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3/app/design/data/fixtures app/design/data/`.
  `npm run build:sidecar`, then `npm run dev` runs the app against copies of the real crawls (garden: 4,521 URLs;
  a 100k synthetic crawl). Analyse the copies, never `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`. The engine's
  synthetic test site (`si testsite`) is the only site you may crawl. You may build the release shell and run
  `npm run smoke:shell` (it needs this desktop session).
- Stop only processes you started, by their own process id or your own ports; never close a Playwright MCP tab that
  isn't yours. One heavy thing at a time. If a single-file vitest run says "failed to find the runner", clear
  `node_modules/.vite*`.

## What to audit against

1. `docs/v2-ui-brief.md`: "Screens" (every row), "Every view has every state" (the states matrix), "Showing data",
   "The grid", "Navigation and the keyboard", "Accessibility", "Performance budgets", and above all **"Definition of
   done"**, item by item.
2. `docs/v2-ui-ia.md`: the shell, Routes (every route exists and lands right, in both modes), every screen's
   section, "The five flows" (walk each one, in Advanced, and the three of Simple), the **Keyboard** tables (press
   every key in the place it's listed and say what happened), the tray, "Room for what comes later".
3. `docs/v2-ui-simple.md`: every screen's section against the built screen and its mockup in `app/design/simple/`
   (open the mockups from disk), "Simple's words" (any engine or Advanced word leaking into Simple), "The score",
   "Plain words" (shown as designed), "The report" and "The report at the end", "Settings in each mode", "New crawl
   in each mode", "Keys", "What the three readers got stuck on" (is each fix there?), "What round 3 builds",
   "What round 4 builds".
4. `docs/v2-ui-design.md`: the visual direction, tokens, density, both themes, forced colours.
5. `docs/v2-comparison.md`: "What to take from Screaming Frog" and the UI comparison: say which are now done.
6. `docs/v2-ui-discovery.md`: the parity list of the old app's features: each one built,
   deliberately later (step 4 or 5, as `docs/v2-rebuild.md` says), or missing without a reason.

Walk the app yourself in both modes, at 1280x720 and 200% zoom, light and dark, on the garden and 100k copies, with
a live crawl of the test site (start, pause, resume, stop, continue, analyse, the report at the end). Use a screen
reader's view where you can (the accessibility tree), and keyboard only for one full flow in each mode.

## What to report

1. **Gaps**: each one a line: where (doc and section, file:line if known), what was promised, what is built, severity
   (blocks "finished" / should be fixed in step 3 / fine to leave for later with a reason), CONFIRMED (seen) or
   PLAUSIBLE. At most 40, most serious first.
2. **The definition of done**, item by item: met, partly, not met, with the evidence.
3. **The Keyboard tables**: every key, works / doesn't / not applicable.
4. **The parity list**: built / later (by plan) / missing.
5. **What is clearly good**: a short list, with what impressed you, for the owner.
6. **Screenshots** of anything you report as a gap, in your folder, named in the list.

**Give the whole report in your final message** (you can't write a file named `findings.md`). No file dumps. When
done, stop your processes and remove your worktree (`git worktree remove --force` on your own folder only).
