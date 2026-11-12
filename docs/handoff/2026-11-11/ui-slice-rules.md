# Rules for every UI slice of step 3 (read with your own brief)

The app is the Shouting Iguana v2 desktop app: React + TypeScript + Tailwind v4 + Radix + TanStack Router/Query in a
Tauri 2 shell, with a .NET sidecar it reads over a token-protected localhost API. Your brief names your worktree,
branch, ports and screens. These rules hold for every slice.

## Working rules

- Work ONLY in your worktree. Small commits (`App: ...`, saying what now holds), each ending with
  `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`. Never push, merge or touch other worktrees or branches. No bare
  `git stash`. **Never rename an existing exported function or component.** Other agents work in parallel: keep
  shared components and `app/src/api/*` backwards compatible (extend, don't rename or remove), and keep edits to
  shared files as small as the work allows.
- Read first: `CLAUDE.md` (repo root); `docs/v2-ui-brief.md` (the bar: "super slick, enterprise level", "Every view
  has every state", "Showing data", "The grid", "Navigation and the keyboard", "Accessibility", "Performance
  budgets", "Definition of done"); `docs/v2-ui-ia.md` (routes, your screens, the five flows, Keyboard);
  `docs/v2-ui-design.md`; `docs/v2-ui-architecture.md` ("Rules every slice follows", "Components", the grid, the live
  stream, "Feedback", "Overview", "The inspector"); `docs/v2-sidecar-api.md` (Conventions and your endpoints).
- Build with the components in `app/src/ui/` and the grid in `app/src/grid/`. Extend a component there, with its
  story, rather than restyling locally. Tokens only. Words from the API and the glossary only, UK English, no raw
  engine ids or error codes shown.
- If the engine answers something wrong, too slowly, or lacks something a screen needs, don't work around it in the
  UI: report it with evidence. An engine agent works in parallel on `engine/`: stay out of `engine/`.
- From `app/`: `npm ci`, then all green and seen green by you at the end: `typecheck`, `lint`, `format:check`, `test`,
  `build-storybook`, `test:stories` (also `-- --forced` and `-- --reduced-motion`),
  `node --test "scripts/**/*.test.mjs"`, `build:sidecar`, `e2e`. Set `SI_DEV_SERVER_PORT`, `SI_DEV_SIDECAR_PORT`,
  `SI_PREVIEW_PORT`, `SI_E2E_SIDECAR_PORT` and `SI_STORYBOOK_PORT` to your ports (never 6406: Docker holds it). Run
  `npm run dev` and `e2e` with `TEMP` and `TMP` set to a `tmp` folder inside your own scratch folder: every worktree
  shares the default temp folder. Stopping a background task leaves node and the sidecar alive: stop the processes
  on your ports by id. **Stop only processes you started, by their own process id or your own ports, never by a
  command-line match**: that has killed other agents' runs twice. Never run `dotnet build-server shutdown`. The
  machine is close to its memory limit with several agents at work: run one heavy thing at a time (a build, a
  test suite, Storybook, the e2e), and if a run dies for lack of memory, wait and run it again alone before
  you believe a failure. Copy the git-ignored fixtures first:
  `cp -r C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3/app/design/data/fixtures app/design/data/`.
- Real data: `npm run dev` runs the app against copies of the dev crawls (a real site of 4,521 URLs, "garden", and a
  100k synthetic crawl). The copies' analysis is out of date for the current engine: analyse the copies. Never edit
  `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`. Never crawl a real site: `si testsite` serves the engine's synthetic
  site, and `app/e2e/global-setup.ts` shows how the e2e starts it.
- Keep command output to summaries and failures. Update a `checkpoint.md` in your scratch folder at each phase
  boundary (done, decisions, verified vs not, next step), so a pause or a context compaction loses nothing. If told
  to pause: commit (prefix `WIP:` if unfinished) and write `handoff-<name>.md` there.
- Don't edit `docs/v2-rebuild.md`. Do update `docs/v2-ui-architecture.md` and `docs/v2-ui-ia.md` for what you add or
  settle differently.

## What the first slices' reviews found: don't repeat these

Three slices were reviewed after they were built, and 56 defects were found. The same kinds kept coming up:

1. **A number that doesn't land on itself.** Every count is a link, and the link must open exactly that count. Write
   a test that walks every figure of your screen on real crawls and compares it with where it lands. Know the traps:
   a URL not checked because its host stopped answering (`outcome: notChecked`) is not fetched; the crawler's probes
   of the site's other versions (`source: siteVersion`) are not the site's pages; a "page" is what the engine calls
   one (it was read: `wordCount` is not empty); checks the user has hidden are left out of counts.
2. **Words that are wrong for the state.** Every "not fetched" state has its own true sentence (queued while a crawl
   runs, never reached by a stopped crawl, refused by robots.txt, blocked, not checked, no body). Go by `outcome`,
   not `fetched`. A robots.txt that didn't answer is `unanswered` (the crawl went ahead) or `unreachable` (the origin
   was refused): two different sentences.
3. **Focus dropped.** A button that removes itself or its banner when pressed leaves focus on `body`. Use
   `useFocusRescue` (or hand focus to what replaces it). After a palette command, focus stays where the command put
   it. Back and forward keep the place.
4. **Too many announcements.** A figure that moves with a crawl is never a live region. A banner that states what
   the announcer already said takes `role="none"`. A change the user made is announced once.
5. **Live crawls.** Counts read at different moments disagree: take what must agree from one read. Nothing jumps the
   layout as figures arrive: draw the frame from the start. Never show the loading frame over content already on
   screen (a refetch is "refreshing"). At most 4 updates a second.
6. **Times and clocks.** Never compare times as text. Say which clock a duration is on.
7. **Sizes at the edges.** Six-digit counts, very long URLs and texts, 200% zoom, 1280x720 and a narrow panel: nothing
   overprints, and a shortened value keeps its full form in its accessible name and tooltip.
8. **Tests that pass by luck.** An end-to-end test that depends on timing must pass `--repeat-each 10`. An axe pass
   after a theme change waits for the colour tweens (`colourTweensEnd` in `e2e/overview.spec.ts`: share it, don't
   copy it). Run the unit suite three times, once under load. A fix or a tricky behaviour gets a test you have seen
   fail without it.

## Report back

Short: commits (hash + one line), what the screens do, each state and its story, packages added (exact versions and
publish dates; installed with `npm install --save-exact --before=2026-10-21`), measured numbers against the budgets
(first page of a grid 300 ms; a click or key to a visible response 100 ms), the screenshot folder
(`app/test-results/<name>/`, 1280x720 in both themes and 200% zoom, taken after your last e2e run), test counts, and
what you left out or found in the engine, with evidence. State plainly what was verified and what was not. No file
dumps.
