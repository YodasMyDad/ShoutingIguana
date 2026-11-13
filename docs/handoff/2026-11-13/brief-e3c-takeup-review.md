# Brief: fresh-eyes review of the E3c take-up slice (read-only)

Review the E3c take-up slice of the Shouting Iguana v2 desktop app (React + TypeScript + Tailwind v4 + Radix + TanStack
Router/Query, in a Tauri 2 shell, with a .NET sidecar it reads over a localhost API). It is branch `ui/e3c-takeup` at
`27e8286`, cut from `ui/step-3` at `f61714d` and with `ui/step-3` at `be294da` merged in: review
`git diff be294da..27e8286` (the slice's own changes) and the code it touches. You did not write it and nobody has
reviewed it.

- Yours: `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\8e7045a4-9ede-460a-be29-971bc406a6c0\scratchpad\e3c-takeup-review\`
  (it has `tmp` for `TEMP` and `TMP`; keep `checkpoint.md` there).
- What was asked: `docs/handoff/2026-11-11/brief-e3c-takeup.md` and `ui-slice-rules.md` (in the repo). What the
  builder says it did: `...\scratchpad\e3c-takeup\checkpoint.md`. Claims to check, not facts.

Work in a worktree of your own: from `C:\Projects\ShoutingIguana`,
`git worktree add --detach C:\Projects\ShoutingIguana\.claude\worktrees\e3c-takeup-review 27e8286`. **Edit, commit
or push nothing in any other worktree.** In your own you may run `npm ci` and every check (`app/`), on ports
`SI_DEV_SERVER_PORT=54874 SI_DEV_SIDECAR_PORT=54875 SI_PREVIEW_PORT=54876 SI_E2E_SIDECAR_PORT=54877`, Storybook
`SI_STORYBOOK_PORT=6256` (never 6406). Copy the git-ignored fixtures first:
`cp -r C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3/app/design/data/fixtures app/design/data/`.
`npm run build:sidecar` builds the sidecar; `npm run dev` runs the app against copies of the real dev crawls (garden,
4,521 URLs; a 100k synthetic crawl). Analyse the copies, never `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`. The
engine's synthetic test site is the only site you may crawl. If you use the Playwright MCP browser, use only your own
ports and never close a tab that isn't yours. Other agents are at work: one heavy thing at a time; **stop only
processes you started, by their own process id or your own ports.** Never run `dotnet build-server shutdown`.

## Read first

`docs/v2-sidecar-api.md` ("Engine changes E3c made" with the per-figure filter table in "Progress", "The score",
"Analysis" and `fixFirst`), `docs/v2-ui-architecture.md` ("Overview", "The inspector", the tray and its memory
budget, "Rules every slice follows"), `docs/v2-ui-brief.md` (states, accessibility, budgets), `CLAUDE.md`.

Already known, don't report again: Fix first counts a site-wide check as "1 page" (a later slice owns the subject
wording); Links' grid still says "No status" (`screens/links/linkColumns.tsx`); the tray's private working set was
measured to 3 minutes only.

## What to look for

Real defects, each with a concrete failure scenario. It is a small slice of nine items: check each one.

1. **Every figure lands on itself**: Overview live and done (status classes, the probes' line, crawled, pages, URLs
   not checked) on the synthetic, garden and 100k crawls, and a crawl with site-version probes that answer, fail
   and redirect. The probes left out everywhere they should be and nowhere else.
2. **"N URLs" is one number everywhere** (Home, the pill, the sidebar, both toasts, Overview) against the URL grid,
   live and done, on a crawl with URLs not checked.
3. **Time spent crawling and the chart's runs**: a crawl paused, stopped, continued a day later, a clock set back,
   an earlier engine's crawl (no `runs`, no `crawlingMs`); the words say which clock; the text alternative.
4. **The inspector**: reads per tick (count them against the real sidecar, not only the fake), `linkTotals` against
   the lists' own counts, `urlLength` on its limit.
5. **Outcomes on named URLs**: each outcome's words right for the state (queued while running, never reached by a
   stopped crawl, refused, blocked, not checked, no response), by `outcome` not `fetched`.
6. **Home at 200% zoom and the scroll bars**: no sideways page scroll at 640 px with and without the sidebar; the
   WebKit scroll-bar parts in both themes, forced colours left to the system, the tab strip scrolling its chosen
   tab into view; the side effect on headless e2e layouts (did a test get loosened to pass?).
7. **`fixFirst`**: Overview, Issues' first check, the palette; hidden checks; an older analysis; the singular words.
8. **What changed in others' files** (TopBar, Sidebar, three e2e tests updated): right, or papered over?
9. Accessibility (live regions: nothing announced per tick), tokens only, UK English, every state in Storybook,
   tests that actually test.

Verify each finding before reporting it (a failing test, Playwright, a script). Mark each CONFIRMED or PLAUSIBLE.
No style preferences, no speculation reported as a defect.

## Report back

A ranked list, most severe first, at most 15: severity, file:line, defect, failure scenario, CONFIRMED or
PLAUSIBLE, one-line fix. Then a short "looks right" note. **Give the full list in your final message** (you can't
write a file named `findings.md`). Put proofs in your folder. When done, stop your processes and remove your
worktree (`git worktree remove --force` on your own folder only).
