# Brief: fresh-eyes review of the Links slice (read-only)

Review the Links slice of the Shouting Iguana v2 desktop app (React + TypeScript + Tailwind v4 + Radix + TanStack
Router/Query, in a Tauri 2 shell, with a .NET sidecar it reads over a localhost API). It is branch `ui/links` at
`2175872`, cut from `ui/step-3` at `17b340d`: review `git diff 17b340d..2175872` (24 commits, 82 files, about 8,450
lines). You did not write it and nobody has reviewed it.

- S1 = `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\0dc66a3f-9b82-4a98-a271-d8ca3b28f098\scratchpad`
  (the build's files, read-only).
- S2 = `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad`
  (yours: `S2\links-review\`).

Work in a worktree of your own: from `C:\Projects\ShoutingIguana`,
`git worktree add --detach C:\Projects\ShoutingIguana\.claude\worktrees\links-review 2175872`. **Edit, commit or push
nothing in any other worktree.** In your own you may run `npm ci` and every check (`app/`), on ports
`SI_DEV_SERVER_PORT=57874 SI_DEV_SIDECAR_PORT=57875 SI_PREVIEW_PORT=57876 SI_E2E_SIDECAR_PORT=57877`, Storybook on
`SI_STORYBOOK_PORT=6506` (never 6406). Copy the git-ignored fixtures first:
`cp -r C:/Projects/ShoutingIguana/.claude/worktrees/links/app/design/data/fixtures app/design/data/`.
`npm run build:sidecar` builds the sidecar. `npm run dev` runs the app against copies of the real dev crawls (a real
site of 4,521 URLs, "garden", and a 100k synthetic crawl with 5,053,909 links); run it and `e2e` with `TEMP` and
`TMP` set to `S2\links-review\tmp`, as every worktree shares the default temp folder. Analyse the copies, never the
files in `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`. The engine's synthetic test site (`si testsite`, see
`app/e2e/global-setup.ts`) is the only site you may crawl.

Other agents are at work on this machine (`.claude/worktrees/e3c`, a `sitemaps-review` worktree) and memory is short
(about 5 GB free): run one heavy thing at a time. **Stop only processes you started, by their own process id or your
own ports**: never by a command-line match, which has killed other agents' runs twice. Never run
`dotnet build-server shutdown`.

## Read first

- `S1\ui-slice-rules.md`: the rules the slice was built to, and the eight kinds of defect earlier reviews found.
- `S1\brief-links.md` (what was asked) and `S1\links\report.md` (what the build says it did, measured and left out).
- `docs/v2-ui-brief.md` ("Every view has every state", "Showing data", "The grid", "Navigation and the keyboard",
  "Accessibility", "Performance budgets", "Definition of done"), `docs/v2-ui-ia.md` (Routes, Links, Keyboard),
  `docs/v2-ui-design.md`, `docs/v2-ui-architecture.md` (the grid, "Rules every slice follows", what this slice
  added), `docs/v2-sidecar-api.md` ("Links", `/links/facets`, `/api/link-columns`, "Checks" and `subject`).
  `CLAUDE.md` for conventions.

Already known, don't report again: the timings the report lists as misses (a new search 0.94 to 1.87 s, text filters
0.5 to 0.8 s, source depth 1.46 s, half-way-down reads over 300 ms, a copy only an earlier engine analysed); the
things it lists as not built (row menu, column resize, reorder and pin, saved views, export, "show the sources or
targets in URLs"); the mock's limits; the inspector's `inlinks` differing from the stored links by 1 or 2;
`/issues/groups` calling its count `pages` for URL checks. The engine branch `ui/e3c` is not merged into this
branch yet: don't report the lack of its fields.

## What to look for

Real defects, each with a concrete failure scenario, in order of weight:

1. **Every number lands on itself, on real data.** Facet counts, preset counts, the result count (`matching` and
   `total`), the inspector's "All N in Links" (and its self-link filter), on the synthetic, garden and 100k crawls.
   Filters over `link.*`, `from.*` and `to.*` columns: each operator, values with quotes, commas, percent signs and
   non-ASCII, empty and null, and how each is written to the route and read back (reload, Back, Forward, a link
   pasted into "Go to"). Search, sort and cursors: a sort changed mid-scroll, a page asked for past the end, a
   cursor from before a filter changed, descending order, the last page.
2. **Job 1, the Issues wording by a check's `subject`** (commits `2dae51b` `178f14c` `50ff9d1` `2dd251e` `48bfb1b`
   `b3f7efb`). Nobody has read it line by line: you do. For all 86 checks on real crawls: the noun, the count, and
   the share's denominator on Issues, Overview's Fix first and the inspector. **Settle the open question**: the
   build used "of the N URLs found" for a `url` check and `pageCount` rather than `pagesAnalyzed` for a `page`
   check. Read the engine (`engine/src/ShoutingIguana.Engine/Analysis`, the sidecar's DTOs) to see what each count
   is and what each check can flag, then say which denominator is right for each subject and why, with the cases
   where they differ. Also: hidden checks, checks skipped, a running crawl (no share shown), plurals and six-digit
   counts.
3. **A running crawl.** The count moves in place and the grid is not drawn again; the cursor and selection stay;
   answers are for one moment (rows, counts and facets agreeing); a sort or filter changed while it runs; the crawl
   ending, being continued, and the hand-over to the analysed state; what re-renders per tick; at most 4 updates a
   second; nothing while the window is hidden.
4. **Copying a whole view** (target URLs, source URLs, the cursor's column): how many rows it will fetch and hold
   (the 100k crawl has 5 million links), what the user is told, cancel, an error half way, duplicates, the order.
5. **What the slice changed outside its own folder.** `GridCore` (the activated column, the cursor's column, the
   header that says why it doesn't sort, the slow first load's words): is the URL grid's, the Issues treegrid's and
   the inspector lists' behaviour unchanged? The shared grid pieces (filter bar and editor, column chooser, facet
   menu): `UrlsScreen` now uses the shared chips, so walk URLs properly. The test setup change (`ResizeObserver`),
   `colourTweensEnd` moved out of `e2e/overview.spec.ts`, the registry and palette (shortcut clashes; a command
   shown where it can't run), the mock transport, `routing/search.ts`.
6. **Job 2, `npm run dev` keeping analysed copies** (`2abefcd`): a copy kept when its original changed; a copy
   analysed by an earlier engine; two worktrees at once; anything that could write to the dev crawls.
7. **Accessibility and the keyboard**: grid semantics and names; the column chooser, facet menus and filter editor
   (focus in, focus back, Esc, typeahead); focus when a chip or preset removes itself; what a screen reader hears
   when the view changes (once) and while a crawl runs (nothing); forced colours; reduced motion; 200% zoom;
   1280x720; six-digit counts and very long URLs and anchor texts.
8. **Performance**: first page 300 ms and click or key to a visible response 100 ms, on the 100k crawl; memory
   after scrolling far through 5 million rows; requests made per view change (duplicates, reads nobody shows).
9. **Design-system and slice rules**: tokens only, components extended in `app/src/ui/` rather than restyled
   locally, words from the API and glossary only, UK English, no raw engine ids or error codes shown, every state
   in Storybook, and tests that actually test (nothing weakened elsewhere to get green).

One more job, a list and not a defect of this slice: the real-shell pass found the **URLs** screen has no Ctrl+F
("Filter this grid" in the IA's Keyboard table), no header sort and no row menu. Say exactly which of those URLs
now has through the shared pieces, and what is still missing, against the IA.

Verify each finding before reporting it: read the code path and, where you can, prove it (a failing test,
Playwright against `npm run dev` or Storybook, axe, a timing). Mark each CONFIRMED (shown) or PLAUSIBLE (reasoned).
No style preferences, and no speculation reported as a defect.

## Report back

A ranked list, most severe first, at most 30: severity (high / medium / low), file:line, one-sentence defect, the
failure scenario, CONFIRMED or PLAUSIBLE, and a one-line fix. Then the denominator verdict, the URLs-screen list,
and a short "looks right" note on what you checked and found sound. **Write the full list to
`S2\links-review\findings.md`** (a fixer works from it) with your proof tests and scripts beside it, named in the
report, and give the same list in your final message. Keep a `checkpoint.md` there as you go. No file dumps. When
done, stop your processes and remove your worktree (`git worktree remove --force` on your own folder only).
