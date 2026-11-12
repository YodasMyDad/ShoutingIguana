# Brief: the mode and its switch, the top bar, Settings, New crawl (UI, round 3)

Read `ui-slice-rules.md` beside this file first: it holds the rules every UI slice follows and the defects earlier
reviews kept finding. This brief adds your slice.

The app is gaining a Simple mode, for people who aren't SEOs, with Advanced (everything built so far) one switch
away. The design is done: `docs/v2-ui-simple.md`, with mockups in `app/design/simple/`. Its section **"What round
3 builds"** is your list, items 1 to 5: the mode setting, the switch in the top bar, the top bar's order of
giving way, Settings, and New crawl in both modes. Round 4 then builds the Simple screens themselves, with three
agents at once, on the frame you leave. Export (items 6 and 7) is another slice.

## Where you work

- Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\round3-ui`, branch `ui/round3` (cut from `ui/step-3` with
  Links, Sitemaps and robots.txt merged). Ports: `SI_DEV_SERVER_PORT=61874 SI_DEV_SIDECAR_PORT=61875
  SI_PREVIEW_PORT=61876 SI_E2E_SIDECAR_PORT=61877`, `SI_STORYBOOK_PORT=6906`.
- Your scratch folder (all your own files, `tmp`, `checkpoint.md`):
  `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad\round3-ui\`.
- Two other UI agents work in parallel, in `.claude/worktrees/urls-grid` (the URL grid, the grids' shared pieces,
  a row menu) and `.claude/worktrees/e3c-takeup` (Overview's, Home's and the inspector's figures, scroll bars).
  The shell, the top bar, the sidebar, the router's shape, Settings and New crawl are yours; stay out of the grid
  screens, `app/src/grid/`, Overview's content and the inspector. Shared files (`keyboard/registry.ts`,
  `api/queries.ts`): add, don't reorder or reformat.

## Read as well

- `docs/v2-ui-simple.md`: "One app, two views", "The switch" with "Routes", "The shell in Simple" with "Top bar",
  "Simple's words", "Settings in each mode", "New crawl in each mode", "Keys", "What round 3 builds", "What round 4
  builds" (so you know what will be plugged into your frame). The mockups: `settings-simple.html`,
  `settings-advanced.html`, `new-simple.html`, `new-advanced.html`, `advanced-overview.html`, `advanced-live.html`
  and the shell shots in `app/design/simple/` (its README lists them).
- `docs/v2-ui-ia.md`: the shell, Routes (`/new`, `/crawls/:crawl/settings`, `/settings/:section?`), New crawl,
  the crawl's settings, flows 1 and 2, Keyboard. `docs/v2-ui-brief.md`: the settings rows of "Screens", the states
  matrix. `docs/v2-ui-discovery.md`: "Crawl settings" in the parity list (every setting the old app had and what
  became of it), the glossary.
- `docs/v2-sidecar-api.md`: "Crawl options" (`GET` and `PUT /api/crawl-options/defaults`, `validate`, the speed
  presets), "Crawl control" (start, continue, crawl again with a crawl's options, what Continue asks for again),
  "Alerts" (each alert's action and where it should lead), "App". `docs/v2-ui-architecture.md`: the shell, how
  preferences are kept, secrets and the OS credential store, the tray, "Feedback".
- The real shell pass's open findings, which your top bar work closes: the search field shrinking to one letter
  beside Pause and Stop at 1280 px, and Overview's breadcrumb crushing its date at 200% zoom.

## What to build

1. **The mode.** A preference stored with the app's others, `simple` on a first start, read through one
   `useMode()`. The setting in Settings > General and the switch are the same value.
2. **The switch**, exactly as "The switch" says: a two-part control at the right end of the top bar, in words, on
   every screen, never moving and never giving way; Ctrl+Shift+A; the palette's two commands; announced once when
   it changes; focus stays on it. Switching keeps the user on the matching screen.
3. **Mode-aware routes, as a frame for round 4.** One table, in one file, from a route to its Simple screen. It is
   empty today, so every route shows its Advanced screen in either mode, as the design says for this round. Round
   4's agents will each add their rows and nothing else, so make adding a row a one-line change that can't
   conflict with another's, and document it in the architecture doc with an example. Include what the design
   settles for a route Simple doesn't have (Advanced-only screens turn Advanced on, with the "Back to Simple"
   toast) and for where a switch lands, both ways, with tests that will hold once the table fills.
4. **The shell in Simple**: the sidebar's three sections for an open crawl (Summary, What to fix with its count,
   Report), the top bar's words and search as an icon, as the design gives them. Summary and What to fix lead to
   the routes Overview and Issues have (their Simple screens arrive in round 4); Report appears when its screen
   exists (leave it out until then, behind the same table). Advanced's sidebar is unchanged apart from what the
   design adds.
5. **The top bar's order of giving way**, measured on the bar's own width and not the window's, as the design
   lists it, in both modes, idle and with a crawl running, at 1280x720, 900 px and 200% zoom (the mock shots are
   the target). Nothing overprints or shrinks to a letter at any width between; a thing that gives way keeps its
   full name for a screen reader and in a tooltip.
6. **Settings**: every section the IA lists, to the design's table of what each mode shows: General (Mode, Theme,
   and Density in Advanced), Crawl defaults (the New crawl form, saved as defaults), Data folder, Shortcuts,
   Diagnostics, Updates, About; Simple's four sections and its note with "Show them in Advanced". Each setting
   applies at once, says what it does in a line, and is remembered. **Analysis** (the checks' limits) is listed in
   Advanced with its section built by a later slice, once the engine can save limits: build the frame so that
   slice adds one section file, and until then leave Analysis out of the list rather than show an empty page.
   Where the shell or the sidecar can't yet do what a section needs (moving the data folder, checking for
   updates), build what is true today and report the gap: no control that does nothing.
7. **New crawl in both modes**: one form and one route, to the design's table. Simple's four questions; Advanced's
   folded sections that show their current values in a line, every setting the engine's crawl options have, each
   with its one-line "what this does"; inline validation from `validate`, field by field; Start (Ctrl+Enter);
   "Use these choices every time" and "Save as my defaults"; the line that says which Advanced settings differ
   from their defaults when Simple hides them; what is typed goes with the user across the switch. Secrets (header
   values, a proxy password) go where the architecture doc says, never into saved defaults. Home's "Settings…"
   opens it, and Ctrl+N.
8. **The crawl's settings** (`/crawls/:crawl/settings`): what this crawl used, read-only, with Continue (asking
   again for what wasn't saved) and "Crawl again with these". Overview's alert actions get their real targets:
   continue more slowly, add headers, another user agent each open this screen or New crawl with that setting
   in focus.
9. **Every state** in Storybook (both modes, both themes, forced colours, 200% zoom, reduced motion), the palette
   and the shortcut sheet listing what works in the current mode, and e2e for: a first start is Simple; the switch
   by click, key and palette, kept across a restart; a crawl started from New crawl in each mode with changed
   settings arriving at the engine as set; defaults saved and used; validation; the top bar at the three sizes.

## Measure

A switch of mode to a visible response inside 100 ms with no layout jump outside the parts that change. The top
bar's widths at which each thing gives way, in a table in your report, beside the design's.

## Report back

As `ui-slice-rules.md` asks, plus: the one-line recipe for round 4 to add a Simple screen; every crawl option the
form shows and any the engine has that it doesn't; each gap in the shell or the sidecar that a Settings section
ran into; what you left out and why.
