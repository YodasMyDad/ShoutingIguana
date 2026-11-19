# Data lane: state at the pause, 2026-11-19

Stopped at the coordinator's word, part-way. Nothing is running; `git status` is clean; no stash.

- Branch `ui/ux-data`, worktree `C:/Projects/ShoutingIguana/.claude/worktrees/ux-data`, base `2a2cc140`.
- **Head `5922ad44`**, 27 commits on the base (`git log --oneline 2a2cc140..ui/ux-data`).
- Scratch: `C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/da833503-3c78-46eb-9e8a-d618294b5af4/scratchpad/data-fix/`
  (`sub-common.md` is the brief the three helpers worked from; `checkpoint-links.md`, `checkpoint-sitemaps-robots.md`
  and the inspector helper's checkpoint hold their detail; `flows/`, `shots/`).

## How the lane was worked

The lead did the two early items and the column-hint type, then split the rest: three helper agents (links,
sitemaps-robots, inspector) worked in the same worktree on their own files while the lead was to do the grid and the
URLs screen. **The helpers finished and reported. The lead's own share (grid core and the URLs screen) was not
started beyond the type.** So Links, Sitemaps, robots.txt and the inspector are changed; URLs and `grid/**` are as
they were on the base, except `grid/types.ts`.

## The two early items other lanes depend on: both committed

| Item | Commit | State |
| --- | --- | --- |
| `Url`: open button straight after the text, always shown and muted, shown per row in a big grid (`si-open-on-row` on the grid's frame), no copy cursor on dead text; props `openUrl?: string \| undefined; openHint?: string \| undefined;` (icon opens `openUrl ?? url`, tooltip `openHint ?? 'Open in your browser'`) | `a57d5990` (+ `b12b645a`: a link keeps the pointer) | Committed. The text was already a link wherever `onGo` is given. **One known fault, below.** |
| `routing/inspector.ts`: opening pushes one history entry; the grid's cursor, a hop inside and "Go to URL…" while open replace it; Back closes it and returns to the view as it was | `f41d2b2d` | Committed, with its unit tests. |

What else `f41d2b2d` gives callers: `useInspector()` is `open(urlId, more?, show?)`, `walk(urlId, { show? })`,
`back()`, `close()`, `goTo()`. `show` is `{ section?: string; link?: number }`: the inspector section to arrive on and
the URL id at the other end of the link the reader came for (shown first, marked). The issues lane can pass it from a
"Linked from" row: `open(pageId, undefined, { section: 'outlinks', link: brokenUrlId })`. The hops are kept in the
entry's history state (`inspectTrail`); `inspectSteps` is gone.

## Findings

| Id | State | What exists, what's missing |
| --- | --- | --- |
| D-02 | **partly** | Done: `Url` (`a57d5990`), Links flat and grouped (`a8598ad9`), one sitemap's URLs (`39734520`), blocked URLs (`2bf79893`), inspector lists. **Missing: the URLs grid.** `screens/urls/urlColumns.tsx:597-603` (the URL cell) and `:699` (Canonical / Redirects to / Final URL) still pass no `onGo`; `UrlsScreen.tsx` doesn't give its grid `si-open-on-row`; "Click a URL to inspect it" isn't in the grid's foot (`grid/gridFoot.ts`). Issues' cells are the issues lane's. |
| D-03 | **done** | `a8598ad9`, `94cc3bdb`, `1f51c667`, `ccfc4a08`, `5922ad44`. Cost: `/links/groups` doesn't return the linking page, so each drawn target makes one limit-1 read. |
| D-04 | **partly** | Only the hints: `GridColumn.fit / fitText / fitExtra / maxWidth` (`58dd8840`, `grid/types.ts`), and the helpers marked their columns. **`grid/columnFit.ts` doesn't exist and `GridCore` ignores `fit`.** The URLs grid still hands all spare width to the URL (`urlColumns.tsx:476-493`). Each screen still shares width itself: `fittedLinkWidths` (Links), `SitemapTreeGrid`, `RobotsOrigins`, `RobotsFileView`, `URL_SHARE` in `inspector/LinksSection.tsx` and `ImagesSection.tsx`. |
| D-05 | **done, differently** | `145f484b`, `73df617a`, `6d5133b9`, `2748be92`. Default width `clamp(400px, 50vw - 360px, 920px)` (not 34vw / 880: ten tabs need about 905 px); bottom dock 40vh; a drawn grip. Tabs keep "N more" (4 + "6 more" at 1280). |
| D-06 | **not started** | `grid/GridCore.tsx:638-641`, `:962-976`. |
| D-07 | **done** | `a0934c2b`. A plain click anywhere on a row also opens it. |
| D-08 | **partly** | Done: back strip (`f41d2b2d`, `3a96f3f3`), the link come for first and marked, status filter and broken first for lists of 1,000 links or fewer, client-side (`fb9cff42`); Links passes `show` (`a8598ad9`). **Missing: the grid's highlight following the walk** (URLs screen); lists over 1,000 links get no status filter (the read takes `kinds` only). |
| D-09 | **partly** | Inspector Summary's counts are links (`15ef47e1`). **Missing: the URLs grid's counts** (`urlColumns.tsx:561-575`), the row menu's outlinks / issues items (`UrlsScreen.tsx:985-1018`). |
| D-10 | **not started** | |
| D-11 | **not started** | |
| D-12 | **not started** | |
| D-13 | **done** | `15ef47e1` (names the sitemaps), `39734520` (`/sitemaps/<id>?inspect=<urlId>` lands on the row). |
| D-14 | **not started** | (The inspector half of rule 9 is `f41d2b2d`.) |
| D-15 | **done** | `15ef47e1`, `4bd1f0e2`. Pixel sizes seen on the synthetic crawl only. |
| D-16 | **partly** | Done: Links' chip and named menu pair (`a8598ad9`), "In /sitemap.xml" (`a0934c2b`). Missing: the eight chips collapsed to one, `screens/urls/linkEnds.ts:37-41` words, engine ids in a filter refusal. |
| D-17 | **partly** | Links folds Kind + Position + Follow under 1,280 px of content and its tools become icons. URLs at 1280 not started (needs D-04). |
| D-18 | **partly** | `39734520`, `7b2f357f`: heading whole, status pill, paths. Still a fixed 1,456 px grid that scrolls sideways at 1280; no preset tabs, column chooser or Add filter. |
| D-19 | **done** | `2bf79893`. |
| D-20 | **partly** | Done: open icon after the text, Links' "Ends at", the history bullet. Not started: 11.2 px against 12.1 px URL text, the file name-first form, double-click on a column edge, the export toast in another window. |

Counts: **6 done** (D-03, D-05, D-07, D-13, D-15, D-19), **8 partly** (D-02, D-04, D-08, D-09, D-16, D-17, D-18,
D-20), **5 not started** (D-06, D-10, D-11, D-12, D-14).

## Known breakages

1. **Rule 2 is broken for a URL shown as its path.** With `siteOrigin`, `.si-url__full` (the full URL, height 0)
   still sets the link's width, so the open button sits a host's width (about 270 px) after the path. All three
   helpers reported it. Fix in `ui/Url.tsx` and the URL block of `styles/components.css`: size the link by the shown
   text (as `.si-url--file` already does), keeping the inline variant able to grow back after a truncation.
2. Middle truncation ignores letter-spacing, so it cuts a URL that fits in a heading (the sitemap heading uses
   `truncate="wrap"` to avoid it).
3. `startRow` selects nothing in `GridCore`; the sitemaps helper tints the cursor's row with CSS (`si-mark-cursor`).
4. The e2e specs below were rewritten without being run.
5. `smoke:shell`'s inspector step may press Alt+Left expecting a step back inside the inspector: it now closes it.

## Decisions

- Close (Esc) steps back to the entry the inspector opened from when that is still the same view, else drops
  `inspect` in place: no second entry for one view, and a filter changed while it was open isn't undone.
- The grid's cursor moving the inspector to another URL ends the walk; the same URL keeps it.
- The open button always shows by default; a big grid opts in to per-row with a class, so the issues lane's tables
  get "always visible" with no change.
- Column sizing was to be measured once on the rows read first, so columns never jump while scrolling; spare width
  beyond every column's need stays blank at the right. Not built.
- Crawl ids depend on the file's path: in this lane's copy garden is `/crawls/c5da28e2bca299d5`, synthetic
  `/crawls/6b3b1046801ed957`, jitsunami `/crawls/d8f212379b75bfd8` (not the ids in `audit-common.md`).

## `Shared:` commits

| Commit | File |
| --- | --- |
| `d1d85b18` | `app/src/screens/IssuesScreen.test.tsx` (history length 2 after the inspector opens) |
| `d9ff333a` | `app/src/screens/rowMenus.moving.test.tsx` (Links' "Copy the target URL") |
| `145f484b` | `app/src/styles/tokens.css` (inspector width and dock height) |
| `73df617a` | `app/src/layout/InspectorSlot.tsx` (the resize grip) |

Also touched and likely to meet other lanes at the merge: `app/src/styles/components.css`, `docs/v2-ui-ia.md`,
`docs/v2-ui-architecture.md`.

## What ran, and what did not

Run by the lead:
- `npx tsc -b` at head `5922ad44`: clean.
- Vitest, at `f41d2b2d` (before the helpers' work): `src/inspector`, `src/routing`, `src/ui/Url*`, `src/styles`,
  `GoToUrl.test.tsx`, `UrlsScreen.test.tsx`, `IssuesScreen.test.tsx`: 384 passed, 1 skipped, 1 failed
  (`classes.test.ts`, an unused class), then fixed and `classes.test.ts` + `Url*` rerun: 43 passed.
- The whole Vitest suite once, before the tests that pinned the old history model were rewritten: 2,845 passed, 13
  failed (7 were those tests, 1 the class, 5 timeouts or load that passed alone). **Not rerun whole since.**
- eslint on `src/ui`, `src/routing`, `Inspector.tsx`, `inspectorContext.ts` at `f41d2b2d`: clean.

Reported by the helpers, not seen by the lead: links 157 passed (and 1,416 passed, 1 skipped over `src/screens`,
`inspector`, `app`, `keyboard`, `routing`); sitemaps-robots 294 passed over 11 files; inspector 368 passed, 1
skipped, 1 timeout that passed alone. Each says tsc, eslint and prettier were clean on its own files.

**Not run at head:** `npm run lint`, `npm run format:check`, the whole `npm test`, any e2e spec (the worktree has no
sidecar binary: `npm run build:sidecar` first), `npm run test:stories`, `smoke:shell`.

E2e specs edited and never run: `inspector.spec.ts`, `links.spec.ts`, `links-grouped.spec.ts`, `sitemaps.spec.ts`,
`robots.spec.ts`, `sitemaps-robots.sweep.spec.ts`. Not checked at all: other specs that walk or press Back with the
inspector open (`found-on.spec.ts`, `urls*.spec.ts`, `overview.spec.ts`).

## Looked at in the live app?

**The lead has not looked at any "after" screen.** The lead ran one tour at 2560 × 1380 on the base
(`…/data-fix/shots/before-2560-*.png`) and opened one of those shots. Each helper says it drove its own screens at
2560 × 1380 and 1280 × 720 and left shots in `…/data-fix/shots/`: `links-before-2560-*`, `links-after-{2560,1280}-*`,
`sitemaps-robots-{before,after}-{2560,1280}-*`, `inspector-before-2560-*`, `inspector-after-{2560,1280}-*`. The
links helper says URLs are still cut at 1280 (Source 214, Target 240). Nobody has looked at the screens together
after all three finished, and URLs is unchanged.

## Next five steps

1. Fix the `Url` width fault (known breakage 1) with a test in `ui/Url.open.test.tsx`: every screen shows it.
2. Build `grid/columnFit.ts` and read `fit` in `GridCore` (D-04, D-17), then delete each screen's own width sharing
   (the lists are in the helpers' checkpoints) and `urlColumnWidth`.
3. The URLs screen: `onGo` on its URL cells and `si-open-on-row` (D-02), counts as links (D-09), a Redirects preset
   (D-12), the highlight following the inspector's walk (D-08).
4. The grid: Shift+click then Ctrl+C (D-06), "Clear all" and the row and scroll kept across Back (D-14), facets that
   take several values with "Not indexable", and title / description / H1 facets (D-10, D-11; these may need a
   sidecar query, in a `Shared:` commit).
5. Run the gates at head (lint, format, whole Vitest, `build:sidecar` then the e2e specs above, stories), start the
   sidecar and Vite on 49895 / 49894, and look at every data screen at 2560 × 1380 and 1280 × 720
   (`flows/tour.mjs` with `SI_TAG=after`) before calling anything verified.

Left for the owner, as features: URL include/exclude patterns, a per-URL new/gone comparison, `/links/groups`
returning each group's first link, a status filter on the link reads for lists over 1,000.
