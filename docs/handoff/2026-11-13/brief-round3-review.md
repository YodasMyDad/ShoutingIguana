# Brief: fresh-eyes review of round 3's UI (the mode, the switch, the top bar, Settings, New crawl), read-only

Review round 3's UI slice of the Shouting Iguana v2 desktop app (React + TypeScript + Tailwind v4 + Radix + TanStack
Router/Query, in a Tauri 2 shell, with a .NET sidecar it reads over a localhost API). It is branch `ui/round3`
(head named in the message that launched you), cut from `ui/step-3` at `f61714d` and with `ui/step-3` at `be294da`
merged in: review `git diff be294da..<head>` and the code it touches. You did not write it and nobody has reviewed
it. Round 4 (three agents building Simple's screens) will be built on this frame, so a defect in the frame costs
three times: weigh those highest.

- Yours: `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\8e7045a4-9ede-460a-be29-971bc406a6c0\scratchpad\round3-review\`
  (it has `tmp` for `TEMP` and `TMP`; keep `checkpoint.md` there).
- What was asked: `docs/handoff/2026-11-11/brief-round3-ui.md` and `ui-slice-rules.md` (in the repo). What the
  builder says it did: `...\scratchpad\round3-ui\checkpoint.md`. Claims to check, not facts.

Work in a worktree of your own: from `C:\Projects\ShoutingIguana`,
`git worktree add --detach C:\Projects\ShoutingIguana\.claude\worktrees\round3-review <head>`. **Edit, commit or
push nothing in any other worktree.** In your own you may run `npm ci` and every check (`app/`), on ports
`SI_DEV_SERVER_PORT=56874 SI_DEV_SIDECAR_PORT=56875 SI_PREVIEW_PORT=56876 SI_E2E_SIDECAR_PORT=56877`, Storybook
`SI_STORYBOOK_PORT=6456` (never 6406: Docker holds it). Copy the git-ignored
fixtures first: `cp -r C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3/app/design/data/fixtures app/design/data/`.
`npm run build:sidecar` builds the sidecar; `npm run dev` runs the app against copies of the real dev crawls.
Analyse the copies, never `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`. The engine's synthetic test site is the only
site you may crawl. If you use the Playwright MCP browser, use only your own ports and never close a tab that isn't
yours. Other agents are at work: one heavy thing at a time; **stop only processes you started, by their own process
id or your own ports.** Never run `dotnet build-server shutdown`.

## Read first

`docs/v2-ui-simple.md` ("One app, two views", "The switch" with "Routes", "The shell in Simple", "Settings in each
mode", "New crawl in each mode", "Keys", "What round 3 builds", "What round 4 builds"), the mockups in
`app/design/simple/` (`settings-*.html`, `new-*.html`, `advanced-live.html`), `docs/v2-ui-ia.md` (the shell, Routes,
New crawl, flows 1 and 2, Keyboard), `docs/v2-ui-brief.md` (states, accessibility, budgets, definition of done),
`docs/v2-ui-architecture.md` ("Modes, Settings and New crawl (round 3)", how preferences and secrets are kept),
`docs/v2-sidecar-api.md` ("Crawl options", "Crawl control", "Alerts", "App"), `CLAUDE.md`.

Already known, don't report again: no OS credential store command in the shell (secrets live only in the form's
state); the data folder can't be moved; no updater; no proxy test call; Analysis waits for E6; the Simple screens
are round 4's; the real Tauri window and the real dev crawls weren't tried by the builder (you may).

## What to look for

Real defects, each with a concrete failure scenario, in order of weight:

1. **The frame round 4 builds on**: `simpleScreens.ts` and its guard. Will three branches each adding a row merge
   cleanly? Fill the table with stubs yourself and walk every route both ways: where a switch lands, Advanced-only
   screens turning Advanced on with "Back to Simple", Back and Forward across a switch, deep links and reloads in
   each mode, the inspector's routes in Simple, a route with search params, a crawl that no longer exists, the
   first start. Anything a round 4 agent would have to change outside its row is a defect here.
2. **The mode**: one value for the switch and Settings > General; stored and read back (localStorage now, the
   shell's `settings.json` in Tauri: read the code path for the latter); a settings file from before the mode
   existed; two windows; what every test, story and e2e now starts in (the builder set them all to Advanced: is
   anything then never tested in Simple?).
3. **New crawl**: every one of the 27 options sent as set (start crawls and read the engine's stored options back),
   defaults saved and used (Home's Enter now fetches them: a failure there, a slow sidecar, saved defaults the engine
   now refuses), "Use these choices every time" vs "Save as my defaults", the changed-settings line in Simple,
   what crosses the switch, validation field by field from `validate` (a refused value, a value fixed, the engine
   answering slowly or failing), secrets never saved or logged, Ctrl+Enter from every control, `from` and `focus`.
4. **The crawl's settings and Continue**: what is shown against what the crawl used; Continue asking again for
   secrets; "Continue without"; "Crawl again with these"; each Overview alert action landing on its setting.
5. **The top bar's order of giving way**: measured on the bar; nothing overprints or shrinks to a letter at any
   width (sweep every 10 px from 1440 down to 400, both modes, idle and crawling, 100% and 200% zoom); each thing
   that gives way keeps its name for a screen reader and a tooltip; the switch never moves.
6. **Settings**: each section's states and words; each setting applies at once and is remembered; Simple's four
   sections and its note; Copy diagnostics (what it copies: anything secret or personal?).
7. **What changed outside the slice**: the alerts and their actions, the palette's check entries (either title),
   `ScreenApp`'s new props, `test/setup.ts` / `.storybook/preview.tsx` / `playwright.config.ts` starting in
   Advanced, the rewritten `states.spec.ts` 200% test (right, or a regression papered over?), `SitemapsScreen.test`.
8. **Accessibility and the keyboard**: the switch (role, name, state, announced once, focus kept), Ctrl+Shift+A
   from a text field against typing, the folded sections, the form's errors (linked to fields, announced once),
   forced colours, reduced motion, 200% zoom.
9. **Design-system and slice rules**: tokens only, components extended in `app/src/ui/`, Simple's words and UK
   English, no raw engine ids, every state in Storybook, tests that actually test.

Verify each finding before reporting it (a failing test, Playwright against `npm run dev` or Storybook, axe).
Mark each CONFIRMED (shown) or PLAUSIBLE (reasoned). No style preferences, and no speculation reported as a defect.

## Report back

A ranked list, most severe first, at most 30: severity (high / medium / low), file:line, one-sentence defect, the
failure scenario, CONFIRMED or PLAUSIBLE, and a one-line fix. Then a short "looks right" note. **Give the full list
in your final message** (you can't write a file named `findings.md`; the orchestrator saves it). Put proofs in your
folder and name them. No file dumps. When done, stop your processes and remove your worktree
(`git worktree remove --force` on your own folder only).
