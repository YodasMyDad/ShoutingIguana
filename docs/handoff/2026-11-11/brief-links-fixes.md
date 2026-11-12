# Brief: fix what the review found in the Links slice

A fresh-eyes review of the Links slice of the Shouting Iguana v2 desktop app (React + TypeScript + Tailwind v4 +
Radix + TanStack Router/Query in a Tauri 2 shell, a .NET sidecar behind a localhost API) found 14 defects. Fix them.
Work ONLY in the worktree `C:\Projects\ShoutingIguana\.claude\worktrees\links` (branch `ui/links`, HEAD `2175872`,
clean). Small commits, one per finding where they separate cleanly (`App: ...`, saying what now holds, not "fix
review item 3"), each ending with `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`. Do not push, merge or
touch other worktrees or branches. No bare `git stash`. Stay out of `engine/`.

- S1 = `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\0dc66a3f-9b82-4a98-a271-d8ca3b28f098\scratchpad`
  (the build's files, read-only).
- S2 = `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad`
  (yours: `S2\links-fixes\`, create it; all your own files, and `tmp`, go there).

## Read first

1. `S2\links-review\findings.md`: the 14 findings (where, defect, scenario, proof, suggested fix), the denominator
   verdict, the URLs-screen list and the "not defects, worth knowing" notes. Proofs are beside it (read-only; copy
   what you use).
2. `S2\ui-slice-rules.md`: the rules every slice follows and the kinds of defect earlier reviews found.
3. `CLAUDE.md` (repo root). **Never rename an existing exported function or component.**
4. `S1\brief-links.md` and `S1\links\report.md` (what was built and why), `S1\links\checkpoint.md`.
5. `docs/v2-ui-architecture.md` (the grid, what this slice added), `docs/v2-ui-ia.md` (Links, Issues, Keyboard),
   `docs/v2-sidecar-api.md` (Links, facets, "Checks" and `subject`, "Analysis": `pagesAnalyzed`,
   `outOfDateReasons`).

## How to fix each one

- Reproduce it first (the review's proof, or a test of your own that fails). Fix the cause, not the symptom the
  scenario happened to show. The suggested fix is a suggestion: if you see a better one, take it and say why.
- **Every fix gets a test that fails without it.** Prove that: with the fix in place the test passes; revert only
  the fix and see the test fail; restore the fix. Note per finding the test's name and that you saw it fail. A
  PLAUSIBLE finding (14, and the cost half of 4) is shown to be real first: if it isn't, say so with the evidence.
- Look for the same mistake elsewhere before you close a finding.

Decisions already made (by the orchestrator), and notes on particular findings:

- **1 (the share)**: a `page` check's share is over `analysis.pagesAnalyzed`, worded "of the N pages analysed". A
  `url` check's share stays "of the N URLs found", and is not shown while the analysis is out of date because the
  crawl changed (`outOfDateReasons` has `crawlChanged`). The same on Issues, Overview's Fix first and the
  inspector: one function decides it. Bring the architecture doc's paragraph into line. Record as an engine ask in
  your report: store the URL count with the analysis.
- **2 (200% zoom)**: the grid keeps a least height that shows at least five rows at 640x360 with three chips on,
  and the "no links match" state with its Clear is always visible. Check URLs, Issues' pages grid, and the
  Sitemaps-style layouts for the same squeeze, and fix the shared layout piece if there is one, not just Links.
- **4 (facets)**: read the facets when a facet menu opens (and keep them fresh while it is open); never on a
  crawl's tick with the menus closed. The preset chips must not need a facets read to draw.
- **5, 6**: the engine is the judge of a pattern. The editor shows the engine's own sentence in place when it
  refuses one, keeps the other filters and the chosen columns, and offers "Change the filter" and "Remove the
  filter". Add the story.
- **7 (hidden window)**: fix it in `live/liveBridge.ts` for every grid: nothing is read or redrawn while the
  document is hidden, and one refresh happens when it is shown again. Keep the change small: another branch
  (`ui/sitemaps-robots`) will merge beside yours.
- **8**: the inspected end lives in the route.
- **9**: say it in the command and its toast when a view has more than 10,000 links ("the first 10,000 links'
  targets"). Record the engine ask (a view's distinct ends).
- **14**: find out what the engine does with non-ASCII letters (read the contract and the reader, and try an
  accented anchor text against the sidecar on a crawl of the test site), then make the words true.
- **The "worth knowing" notes** are yours too: Links says the crawl is being analysed while it is; a popover opens
  with focus on its first field, not on Close (check what the design system's other popovers do, and make them one
  way); a 21st filter isn't dropped silently (say why it wasn't added); `copyCrawl` refuses the original's own
  folder.

Out of scope here, don't build: what the URLs screen is missing against the IA (Ctrl+F, header sort, the row menu,
the filter bar, the column chooser). A separate piece of work builds those on your shared pieces after this merges:
leave the shared pieces ready for it, and say in your report anything that work should know.

## Then: take in `ui/step-3`

When your fixes are done and green, the orchestrator will send you a message to merge `ui/step-3` into your branch
(the engine's E3c will have landed there, touching `app/src/api/{types,grids,client,inspector}.ts`). Don't merge
anything before that message. Report first.

## Checks

From `app/`, all green at the end and seen green by you: `npm ci`, `typecheck`, `lint`, `format:check`, `test`
(three runs, one under load), `build-storybook`, `test:stories` (also `-- --forced` and `-- --reduced-motion`),
`node --test "scripts/**/*.test.mjs"`, `build:sidecar`, `e2e` (the Links specs also with `--repeat-each 10`). Your
ports: `SI_DEV_SERVER_PORT=57874 SI_DEV_SIDECAR_PORT=57875 SI_PREVIEW_PORT=57876 SI_E2E_SIDECAR_PORT=57877`,
`SI_STORYBOOK_PORT=6506` (never 6406). Run `npm run dev` and `e2e` with `TEMP` and `TMP` set to `S2\links-fixes\tmp`.
Other agents are running suites on this machine and memory is short: one heavy thing at a time. **Stop only
processes you started, by their own process id or your own ports, never by a command-line match.** Never run
`dotnet build-server shutdown`. Never edit `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`; never crawl a real site.
After the last e2e run, retake the screenshots the fixes change into `app/test-results/links/`. Keep command output
to summaries and failures. Update `S2\links-fixes\checkpoint.md` after every few findings (done, verified, next).

## Report back

Short: per finding, the commit, the fix in a line, the test and "seen failing without the fix" (or why not fixed);
anything else you found; engine asks; the final check results with counts; what is not verified. No file dumps.
