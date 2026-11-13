# Brief: fresh-eyes review of the URL grid slice (read-only)

Review the URL grid slice of the Shouting Iguana v2 desktop app (React + TypeScript + Tailwind v4 + Radix + TanStack
Router/Query, in a Tauri 2 shell, with a .NET sidecar it reads over a localhost API). It is branch `ui/urls-grid` at
`925a4e4`, cut from `ui/step-3` at `f61714d` and with `ui/step-3` at `be294da` merged in: review
`git diff be294da..925a4e4` (the slice's own changes) and the code it touches. You did not write it and nobody has
reviewed it.

- Yours: `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\8e7045a4-9ede-460a-be29-971bc406a6c0\scratchpad\urls-grid-review\`
  (it has `tmp` for `TEMP` and `TMP`; keep `checkpoint.md` there).
- What was asked: `docs/handoff/2026-11-11/brief-urls-grid.md` and `ui-slice-rules.md` (in the repo). What the
  builder says it did: `...\scratchpad\urls-grid\checkpoint.md`. Claims to check, not facts.

Work in a worktree of your own: from `C:\Projects\ShoutingIguana`,
`git worktree add --detach C:\Projects\ShoutingIguana\.claude\worktrees\urls-grid-review 925a4e4`. **Edit, commit
or push nothing in any other worktree.** In your own you may run `npm ci` and every check (`app/`), on ports
`SI_DEV_SERVER_PORT=57874 SI_DEV_SIDECAR_PORT=57875 SI_PREVIEW_PORT=57876 SI_E2E_SIDECAR_PORT=57877`, Storybook
`SI_STORYBOOK_PORT=6506` (never 6406). Copy the git-ignored fixtures first:
`cp -r C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3/app/design/data/fixtures app/design/data/`.
`npm run build:sidecar` builds the sidecar; `npm run dev` runs the app against copies of the real dev crawls (garden,
4,521 URLs; a 100k synthetic crawl). Analyse the copies, never `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`. The
engine's synthetic test site is the only site you may crawl. If you use the Playwright MCP browser, use only your own
ports and never close a tab that isn't yours. Other agents are at work: one heavy thing at a time; **stop only
processes you started, by their own process id or your own ports.** Never run `dotnet build-server shutdown`.

## Read first

`docs/v2-ui-brief.md` ("Every view has every state", "Showing data", "The grid", "Navigation and the keyboard",
"Accessibility", "Performance budgets", "Definition of done"), `docs/v2-ui-ia.md` (URLs, Keyboard),
`docs/v2-ui-architecture.md` ("The URL grid", the grid, "Rules every slice follows"), `docs/v2-sidecar-api.md`
("URLs", "Facets", columns), `CLAUDE.md`.

Already known, don't report again: no "Filter by this value" / "Exclude this value" in the row menu; no column
resize, reorder or pin; saved views only on URLs; 15 rows at 1280x720 at the default density; export is a slot.

## What to look for

Real defects, each with a concrete failure scenario, in order of weight:

1. **Every number lands on itself, on real data**: facet counts, preset and result counts (`matching`, `total`),
   "Show its inlinks in Links", Issues' "Show the page in URLs", the sitemap's and blocked-URLs' menu actions, on the
   synthetic, garden and 100k crawls. Filters on each column type and operator, values with quotes, commas, percent
   signs and non-ASCII, empty and null, written to the route and read back (reload, Back, Forward, a link pasted
   into "Go to", a saved view opened on another crawl that lacks a column or a value).
2. **Sort**: three keys, Shift+click and Shift+Enter, a fourth refused, a sorted column hidden, a sort the engine
   can't do, the order shown against the order the engine applies, `aria-sort`, cursors and pages after a sort
   change mid-scroll.
3. **The row menu on every grid**: the right rows (a selection vs the row under the pointer), focus back to the
   row, keyboard (Shift+F10, the menu key) and pointer, each action's target exact, a menu open while a crawl
   moves the rows, a menu at the window's edge, at 200% zoom.
4. **Saved views**: the 24 / 1,500-character limits and what the user is told, rename and delete with Undo, a view
   whose filter the engine now refuses, two windows or two crawls, a preference file written while another write
   is pending, what changed in `settings/schema.ts` (migration of an older settings file, unknown fields).
5. **`rowKey` on every grid** (URLs, Links, Issues' rows and groups, the inspector's link lists): under a running
   crawl the cursor stays on its row; a row that disappears; selection; Back and Forward.
6. **What the slice changed outside its own screens**: `GridCore` (`onSort`, `onContextMenu`, `sortOrder`,
   `headerDetail`), `ui/Menu.tsx` (`MenuAt`), `FacetMenu`, `ColumnChooser`, the registry, the two e2e tests it
   changed (`states.spec.ts` Tab order; `inspector.spec.ts` at 200% with the inspector docked): is each change
   right, or a regression papered over? Walk Links, Issues, Sitemaps and robots.txt after it.
7. **Copies**: the 10,000-row cap and its words, cancel, an error half way, order, duplicates.
8. **Accessibility and the keyboard**: grid semantics, the chooser, facet menus, filter editor and saved-view
   dialog (focus in and back, Esc, typeahead), announcements (once per change, nothing while a crawl runs), forced
   colours, reduced motion, 200% zoom, 1280x720, six-digit counts, very long URLs and titles.
9. **Performance**: the budgets on the 100k crawl as you measure them; requests per view change (duplicates, reads
   nobody shows); memory after far scrolls.
10. **Design-system and slice rules**: tokens only, components extended in `app/src/ui/`, words from the API and
   glossary, UK English, no raw engine ids, every state in Storybook, tests that actually test.

Verify each finding before reporting it: read the code path and, where you can, prove it (a failing test,
Playwright against `npm run dev` or Storybook, axe, a timing). Mark each CONFIRMED (shown) or PLAUSIBLE (reasoned).
No style preferences, and no speculation reported as a defect.

## Report back

A ranked list, most severe first, at most 30: severity (high / medium / low), file:line, one-sentence defect, the
failure scenario, CONFIRMED or PLAUSIBLE, and a one-line fix. Then a short "looks right" note. **Give the full list
in your final message** (you can't write a file named `findings.md`; the orchestrator saves it). Put your proof
tests and scripts in your folder and name them. No file dumps. When done, stop your processes and remove your
worktree (`git worktree remove --force` on your own folder only).
