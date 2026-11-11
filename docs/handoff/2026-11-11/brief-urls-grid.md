# Brief: the URL grid, to the IA (UI, round 3)

Read `ui-slice-rules.md` beside this file first: it holds the rules every UI slice follows and the defects earlier
reviews kept finding. This brief adds your slice.

The app's most-used screen for an expert is the URL grid, and it is the one furthest from its design. A fresh-eyes
review listed what it lacks against the IA, and a comparison with Screaming Frog named it as the one place that
tool is plainly better for an expert: "an expert reads 20 rows of 7 columns at a glance, and ours shows 16 of 4",
with no header sort, no filter builder and no column chooser. The Links screen has since built the shared pieces
(filter bar and editor, facet menus, the column chooser, a whole-view copy). You bring URLs up to the IA with
them, and give every grid its row menu.

## Where you work

- Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\urls-grid`, branch `ui/urls-grid` (cut from `ui/step-3`
  with Links, Sitemaps and robots.txt merged). Ports: `SI_DEV_SERVER_PORT=60874 SI_DEV_SIDECAR_PORT=60875
  SI_PREVIEW_PORT=60876 SI_E2E_SIDECAR_PORT=60877`, `SI_STORYBOOK_PORT=6806`.
- Your scratch folder (all your own files, `tmp`, `checkpoint.md`):
  `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad\urls-grid\`.
- Two other UI agents work in parallel, in `.claude/worktrees/round3-ui` (the top bar, the mode switch, Settings,
  New crawl) and `.claude/worktrees/e3c-takeup` (Overview, Home, the inspector's figures). Stay out of
  `app/src/layout/` (the shell and top bar), the Settings and New crawl screens, Overview and Home. Shared files
  you can't avoid (`keyboard/registry.ts`, the router, `api/queries.ts`): add, don't reorder or reformat.

## Read as well

- `docs/v2-ui-ia.md`: the URLs screen, flows 3 to 5, and the Keyboard tables ("Everywhere", "In grids").
- `docs/v2-comparison.md`: "3. UI" and "What to take from Screaming Frog".
- `docs/v2-ui-architecture.md`: the grid, the shared grid pieces as Links left them, "Rules every slice follows".
- `docs/v2-sidecar-api.md`: "URLs" (filters, `search`, `sort` of up to three keys, the summary's states),
  "Facets", "Lookup", and the URL columns the engine lists.
- The Links review's list of what URLs lacks, in the same scratchpad: `links-review\findings.md`, section "The
  URLs screen against the IA", and the Links fixer's notes for whoever builds on its pieces:
  `links-fixes\checkpoint.md`.
- How Links uses the shared pieces: `app/src/screens/LinksScreen.tsx` and `app/src/screens/links/`.

## What to build

1. **The filter bar on URLs**, with the shared pieces: Add filter (every URL column the engine filters, with the
   operators its type allows), edit a chip in place, remove one, Clear filters; the facet menus the engine's URL
   facets support, with counts; everything in the route, as now, so links from Overview and Issues still land
   exactly (walk them).
2. **Columns**: the IA's column presets (Overview, Indexability, Content, Performance, Links) and the column
   chooser, with the chosen columns in the route (`cols`). A preset shows more than today's nine columns where the
   window has room, and scrolls sideways inside the grid when it hasn't: the page never does. "Show full URLs" as
   a view option.
3. **Header sort**: every column the engine sorts by sorts from its header (click, Enter, and the grid's key),
   up to the engine's three keys with a visible order, `aria-sort`, and a header that says why where a column
   can't sort. A sort whose column is hidden is dropped.
4. **Ctrl+F**, "Filter this grid": on URLs it puts the keyboard in the grid's search field, as on Links, and the
   palette lists it. One behaviour for the key across URLs, Links, one sitemap's URLs and Issues (the IA says
   which field each has).
5. **The row menu, on every grid**: right-click, Shift+F10 and the menu key open a menu for the row (or the
   selection) with what the IA's actions give: open in the inspector, open in the browser, copy the URL, copy the
   row or rows as tab-separated text, and the screen's own (on URLs: show its inlinks in Links; on Links: inspect
   the source, inspect the target; on Issues' pages: show the page in URLs). Built once, as a shared piece on the
   design system's menu component with its story, and used by URLs, Links, Issues' pages, one sitemap's URLs and
   an origin's blocked URLs. Focus returns to the row. `GridCore` already reports the three gestures.
6. **Saved views**: the IA's "Quick search and saved views". Save the current view (filters, search, sort,
   columns) under a name; open one from a menu and the palette; rename and delete with Undo; a saved view is a
   route, so it survives a restart and works on any crawl. Stored with the app's other preferences (see how
   hidden checks and the density are kept), not in a crawl file.
7. **Copy a whole view**: "Copy the URLs of this view", with the shared copy and its honest words about how many
   rows it takes.
8. **The cursor follows its row under a running crawl, in every grid.** `GridCore` has an opt-in `rowKey` (the
   Sitemaps slice added it): the URL grid, Issues' pages and the inspector's link lists still keep the cursor as a
   row number, so a refresh that moves rows moves the cursor to another URL. Opt each in, with a test per grid
   that fails without it.
9. **Room for export**: the header has the place Ctrl+E's Export will take (another slice builds export when the
   engine's is merged). Don't build a button that does nothing: leave the slot and register nothing.
10. **Every state** the brief's matrix asks of a grid, in Storybook, including a filter the engine refuses (the
   engine's own sentence, with "Change the filter" and "Remove the filter", as Links now does), a view that
   matches nothing, a crawl never analysed (filters that need the analysis say so), and a running crawl (the count
   moves in place, the cursor stays, facets aren't read on a tick with the menus closed).

## Measure

On the 100k crawl: first page of every preset and of ten filtered views inside 300 ms; a sort change, a filter
change and a column change to a visible response inside 100 ms; scrolling to the end of 100,000 rows with memory
flat. At 1280x720 at the default density: say how many rows and columns each preset shows, beside Screaming Frog's
figure quoted above. At 200% zoom (640x360) the grid keeps at least five rows with three chips on.

A sweep test walks every facet figure and every preset on the synthetic, garden and 100k crawls and compares each
with where it lands.

## Report back

As `ui-slice-rules.md` asks, plus: rows and columns on show per preset at 1280x720; the list of filters, sorts
and facets the engine couldn't give you (don't work around them: report them); what you left out and why.
