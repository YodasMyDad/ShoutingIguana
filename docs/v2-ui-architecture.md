# Step 3 app architecture

How the desktop app in `app/` is built, and the rules every slice follows. Read it with
[the brief](v2-ui-brief.md) (the bar), [the IA](v2-ui-ia.md) (the screens and keys), [the sidecar API](v2-sidecar-api.md)
(the contract) and [the spike](v2-ui-spike.md) (why the grid and the shell are built as they are). How to run, test and
build is in [app/README.md](../app/README.md).

## The pieces

```text
Tauri shell (Rust, app/src-tauri)          Sidecar (.NET, engine/src/ShoutingIguana.Sidecar)
  owns the sidecar: spawn, token,      ──►  127.0.0.1:<port>, bearer token, one SSE stream
  lifeline, restart, quit, tray             ▲
        │ IPC: sidecar_state, events         │ fetch (JSON, problem+json, SSE)
        ▼                                    │
Webview (React, app/src) ────────────────────┘
```

- **The shell** starts the sidecar, hands its port and token to the page over IPC, restarts it if it dies, stops it
  gracefully on quit, and owns the tray and system notifications. The page can't spawn anything.
- **The page** calls the sidecar directly over HTTP. The token never goes in a URL or `localStorage`.
- **The sidecar** is the engine. Everything the UI shows comes from it, except preferences.

## Folders

| Folder | Holds |
| --- | --- |
| `src/api` | The contract's types, the typed client, its errors, the transports (`http`, and `mock` over the design fixtures), query keys and options |
| `src/shell` | The IPC with Rust (`ipc.ts`), the connection store and its wait for a ready engine, OS actions with browser fallbacks (`os.ts`), and the start-up failure screen |
| `src/live` | The SSE parser and reconnecting stream, the live store, and the bridge from events to the rest of the app |
| `src/grid` | The grid primitive: windowed scroll space, virtualiser, keyboard and selection model, copy, paging hook |
| `src/keyboard` | Chords, the command registry, the app's commands, the palette and the `?` sheet |
| `src/settings` | Preferences: schema, store (the shell's settings commands, or `localStorage`), appearance |
| `src/feedback` | The loading rule, skeletons, toasts, error states, route errors, announcements |
| `src/layout` | The shell's layout: sidebar, top bar, inspector slot, banners, the stopping overlay |
| `src/screens` | One file per route, a screen's own pieces in a folder beside it (`screens/issues/`). Home, Overview, Issues, the URLs grid, Sitemaps and robots.txt work; the rest are stubs for their slices |
| `src/inspector` | The URL inspector: its sections, their pure logic, and the HTML source viewer (see The inspector) |
| `src/app` | The router, the query client, overlays, sections, the root component |
| `src/ui` | The component library: buttons, fields, menus, severity, figures, URLs and the rest (see Components) |
| `src/styles` | `tokens.css` (every design value), `base.css`, `components.css` (the library's look), the Tailwind entry |
| `src/crawl` | Crawl status words, the crawl actions, opening crawl files |
| `src-tauri` | The shell (see below) |
| `scripts` | Building the sidecar, `npm run dev`, synthetic crawls, the orphan check, the shell smoke test |
| `e2e` | Playwright against the real sidecar |

## Data flow

```text
transport (http | mock) → api client (typed errors) → TanStack Query (server state) ┐
SSE stream → live store (batched, 4 Hz) ─────────────────────────────────────────────┼→ components
                     └→ live bridge → query invalidation, toasts, announcements      ┘
route search params → screens; settings store → appearance, layout
```

- **Reads** go through TanStack Query with the options in `api/queries.ts`. They stay fresh until an event says
  otherwise: `staleTime` is infinite, nothing refetches on focus or reconnect, and `networkMode` is `always` (the
  sidecar is local, so "offline" never applies: a call made while it starts waits for it in the transport). Grid
  pages' keys are `['grid', <grid>, <crawl id>, ...]`, so `gridPagesOf(crawlId)` finds one crawl's pages.
- **Events** go into the live store at once, but React hears about them at most 4 times a second, and not at all while
  the window is hidden. Their side effects (refreshing the library, a crawl's reads, toasts, announcements) happen in
  the live bridge as each event arrives.
- **Writes** are plain client calls (`crawl/actions.ts`). Their results arrive as events.

### State ownership

| State | Owner | Why |
| --- | --- | --- |
| Anything from the sidecar | TanStack Query | Caching, deduping, keeping data while it refreshes |
| Live progress, statuses, alerts, rates | The live store | Twice a second per crawl: outside React, batched |
| A view's filters, sort, columns, search, inspected URL | Route search params | Back, forward and links restore them |
| Preferences (theme, density, zoom, panels, last views) | The settings store | Persisted; read before the first render |
| The sidecar's connection | The connection store | Set by the shell over IPC, or by `npm run dev` |
| Which overlay is open, and what's modal | `app/overlays.ts` | One at a time; the app behind it is inert |
| Anything else | The component | `useState`, nothing global |

## The shell

`app/src-tauri/src`: `sidecar/` (lifecycle), `live/` (its own event stream, for the tray and notifications), `tray/`,
`quit.rs`, `window.rs`, `webview2.rs`, `context_menu.rs`, `open_files.rs`, `commands.rs`, `settings.rs` (the
preferences file), `profile.rs` (where its per-user files go), `startup.rs` (when it can't start).

- **Starting.** A 32-byte token per spawn in `SI_SIDECAR_TOKEN`, the exact webview origin in `SI_SIDECAR_ORIGINS`
  (plus the dev server's in debug builds), `SI_PARENT_PID`, `--stdin-lifeline`. On Windows the sidecar joins a job
  object that kills it if the shell dies. The binary is the `externalBin`, built by `scripts/build-sidecar.*`
  (self-contained, single file, ReadyToRun, without IIS's in-process module). It is spawned on a thread of its own, so
  the window never waits for antivirus to scan a new 128 MB file, and it unpacks its native library into the app's
  local data folder (`engine\<app version>`, through `DOTNET_BUNDLE_EXTRACT_BASE_DIR`) rather than `%TEMP%`; other
  versions' folders there are removed.
- **The connection, to the page.** One state machine. Command `sidecar_state` returns it; event `sidecar://state`
  carries every change: `starting`, `ready` (port, token, version), `restarting` (attempt, attempts, and why:
  `crashed`, `start-failed` or `start-timed-out`), `failed` (reason, exit code, a plain message), `stopping`. Each has
  a rising `seq`: the page listens first, then asks, and keeps the newest.
- **Calls wait for the engine.** A call made while the shell starts or restarts the sidecar waits for it
  (`waitForConnection` in `shell/connection.ts`, used by the HTTP transport), so a cold start shows skeletons, never
  "The engine is starting.". It fails at once only when the engine has failed or the app is quitting, and a wait is
  bounded at 90 s. Queries that failed before the page's first ready sidecar (it had failed, then Try again) fetch
  again when it comes (`shell/readiness.ts`); later sidecars are the live bridge's.
- **Restarts.** Every path ends:
  - a start that never gets ready is tried three times in all (250 ms, then 1 s, between), then it's
    `failed: start-failed`, however long each try takes (a start times out at 20 s);
  - a sidecar that crashes after it was ready restarts with backoff (250 ms to 4 s), and the sixth crash without five
    minutes of stable running between is `failed: crashed`;
  - exit code 3 (another copy of the app, or its engine, holds the state folder) fails at once, with the shell's plain
    sentence: the engine's own names a command-line option, so it goes only to the log.

  Every retry is published as `restarting`, and the banner says why and which try it is. On any unexpected exit the
  shell logs the sidecar's last 50 stderr lines at WARN. The reason it shows prefers a .NET "Unhandled exception." line
  and is never a stack frame, and the sidecar writes unhandled exceptions to its own log too. Try again
  (`restart_sidecar`) starts afresh.
- **Quitting.** With a crawl running, one native question: "Stop crawling and quit?". Then the window shows
  "Stopping crawls…", the shell writes `shutdown` to the sidecar's stdin and waits up to 35 s (the contract's 30 s bound
  and a margin) before killing it. Closing the window while a crawl runs hides it to the tray instead, and says so once.
  A launch made while the app quits doesn't bring the dying window back: once the engine has stopped, the shell starts
  a new copy with that launch's files (`--relaunch-after <pid>` makes it wait until this one has gone).
- **The tray.** Present while a crawl is live or the window is hidden: a progress ring on the icon, the crawls with
  Pause and Resume, and Quit. Hidden, the webview is told it's invisible, its memory target is set Low, and the page is
  suspended: no rendering, timers or live updates while hidden, so memory doesn't creep back. It resumes when the
  window comes back and catches up from the engine, as after any reconnect (a sidecar replaced meanwhile included).
  The 50 MB tray budget holds over a long stay: with a crawl running, the shell and its WebView2 processes measured
  9.6 MB at 30 s, 20 MB at 3 minutes and a flat 23 MB from 6 to 11 minutes. The shell's own event stream keeps the
  tray and the notifications going, and a crawl the engine took with it when it crashed is told once the new engine
  reports it: "Crawl interrupted".
- **OS.** Single instance. A second launch focuses the window, and the `.sicrawl` paths of every launch wait in the
  shell until the page takes them, which it does once the engine can open them: `shell://open-crawl-files` says more
  have arrived, and `take_pending_open_files` hands each over once. Window size and position are remembered. The
  window stays on the app: any other navigation is refused, and an http(s) link opens in the browser.
- **Keys.** WebView2's browser keys are off, so the registry owns F5, Ctrl+F and the rest. Every reserved key was
  checked with real key presses (F7's caret browsing, Ctrl+S, Ctrl+J, Shift+Esc, Ctrl+P…) and none does anything in
  WebView2. F10, and a tap of Alt on its own, would put the window in Windows' menu mode and lose the next key, so the
  shell keeps them from it. The context menu keeps only editing items.
- **Permissions.** The page gets the events it listens to, `setTheme`, `setZoom`, open and save dialogs, http(s)
  links, and the shell's commands, none of which takes a path. Preferences go through `settings_read` and
  `settings_write`, bound to one file (`settings.json` in the app's config folder, a JSON object of at most 64 KB).
  Show in folder is `reveal_crawl_file`, which takes a crawl's id and finds the file in the engine's library. There's
  no `core:default`, no store and no reveal of any path: `tests/capabilities.rs` expands the generated ACL and fails if
  the page is granted a command that takes an unscoped path, or one from a plugin nobody has reviewed.
- **When it can't start.** A webview that can't be made, or a plugin that fails, ends the app with a native message
  box naming the error and the log's folder. A page whose start-up fails (the shell's IPC, say) shows a plain failure
  with "Copy diagnostics", never a blank window.
- **Test runs.** `SI_APP_PROFILE_DIR` gives a run of the built app a folder of its own for preferences, window state,
  the WebView2 profile, the engine's unpacked files and the log, and keeps it out of single instance. The smoke test
  and the orphan check use it, so they never touch the user's own copy, and run beside it.

## Packaging, signing and CI

- **The installer** is per-user NSIS. CI builds it on every push and installs it silently into a temporary folder,
  checks that `.sicrawl` opens with it, runs it, and uninstalls it (`scripts/smoke-installer.ps1`). A silent uninstall
  keeps the app's data and an empty `.sicrawl` key, as Tauri's installer does unless the user ticks "Delete the
  application data".
- **Signing.** `scripts/sign.mjs` signs the sidecar right after `dotnet publish`, and, as Tauri's `signCommand`, the
  shell, the installer's plugins, the uninstaller and the installer. It signs when a certificate is configured
  (`SI_SIGN_CERT_THUMBPRINT`, or `SI_SIGN_PFX_BASE64` and `SI_SIGN_PFX_PASSWORD`, with SHA-256 and an RFC 3161
  timestamp), and otherwise skips, saying so. There is no certificate yet: buying one is the owner's decision before
  the first release, as SmartScreen and application-control policies block unsigned code.
- **Releases** come from `.github/workflows/app-release.yml`, run by hand only: the installer, signed if the secrets
  are set, smoke-tested, kept as an artifact with its checksum, and optionally a draft GitHub release. The legacy WPF
  app's `release.yml` still runs on pushes to `master`.
- **CI** (`.github/workflows/app.yml`) pins every action to a commit and the shell's Rust in
  `src-tauri/rust-toolchain.toml`. It also builds the shell on Linux (clippy and tests, so code for other platforms
  compiles) and checks it on the oldest Rust `Cargo.toml` names.

## The API client

- `api/client.ts` has a function for every E1, E2 and E3a call, and E4's `/history`: the URL grid, its columns, facets and lookup, the
  inspector (one URL, its links both ways, its HTML) and a check's groups and rows. `api/grids.ts` and
  `api/inspector.ts` type them as the sidecar answers them:
  - the inspector, its links and its HTML are POSTs (`/urls/inspect`, `/urls/inlinks`, `/urls/outlinks`,
    `/urls/source`) with `{ urlId }` in the body;
  - lookup returns `requested`, null when the URL found is the one asked for;
  - a skipped check has `groupCount`, `matching` and `total` null, never 0, and a page read on from `after` has
    `offset` null;
  - each of the inspector's `issues` may carry `group`, the group it's in on `/issues/{checkId}/groups` (null for a
    check that doesn't group). It's optional: an engine from before it leaves it out (`inspector/issueLinks.ts` has
    what the link does then);
  - the canonical test includes `canonicalUrl`, and `limits` includes `largeImageBytes`, `imageAltMaxLength` and
    `maxUrlLength`;
  - an enum value this engine doesn't know is null (`source`, `fetchMode`, `state`, `foundVia`, `notFetchedBecause`,
    `outcome`, the verdict's `reason`, a link's `kind` and `position`);
  - columns without the URL summary are left out of `columns` and of every row, and listed in `unavailable`;
  - the `2000+` response-time facet filters with `gte`, and the severity facets on `errors`, `warnings`, `notices`
    and `issues`.
- The mock answers the grids by the same rules (`api/mock/gridQuery.ts`, `urlColumns.ts`): no URL summary for a
  running or unanalysed crawl (its columns left out and named, a sort or filter on them `needs-analysis`), a query it
  can't run `invalid-query` with messages by field, an unknown URL `url-not-found`. A crawl started in the mock grows
  its grid as its replay runs. Tests answer with the same functions, so screens are tested against the sidecar's
  failures as well as its pages.
- Every failure is a typed error with a plain `title` for the UI and a `detail` for "Details": `ProblemError` (the
  contract's problem+json, with `type`, field `errors`, `missingHeaders`, `proxyCredentials`), `SidecarUnavailableError`
  (no sidecar yet), `SidecarUnreachableError` (no answer) and `UnexpectedResponseError` (an answer off the contract).
  `isProblem(error, 'crawl-busy')` narrows.
- Never `cache: 'no-store'` (it turns off Chromium's preflight cache). Grid queries are POSTs to one URL per grid.

## The live stream

- One `GET /api/events` over `fetch`, parsed by `live/sse.ts` (any line ending, split chunks, comments, BOM).
- It waits while there's no sidecar, reconnects with backoff (250 ms to 4 s), drops a stream that's silent for 35 s
  (keep-alives come every 15 s), and moves to a new sidecar at once.
- `hello` is the truth about what's live, after a sidecar restart or a reconnect: any crawl it doesn't list stops
  looking live (its status is the library's again: a crawl the old sidecar was running reads `interrupted`, with
  Continue), and the bridge fetches its item and reads again. A reconnect to the same sidecar may have missed events
  (it drops a client 1,000 events behind), so every crawl's reads and grids refresh then; a new sidecar refreshes
  everything when it comes.
- While a crawl runs, its open grids' counts and rows refresh in place at most once a second (the bridge, on
  `progress`): never cancelling a page still on its way, with no skeleton and no progress line, and a "Live" mark by
  the count. The URL open in the inspector (`['crawl', id, 'inspect', …]`) refreshes with them, so a queued URL fills
  in once it's fetched, and its tabs' link counts with it; its links are grids, and refresh as grids do.
- A count the stream sends malformed is null, shown as "–", never 0. A status the contract doesn't have is dropped.
- E4's part of a progress event is read at the same boundary (`LiveProgressDetail`): when it was taken, the phase,
  the site's own counts, the URL limit (null is "no limit", undefined "not said"), the status classes and codes, why
  URLs got no response, and the waits. A breakdown with a part missing is null rather than short, as its parts
  wouldn't add up. An alert keeps its `id` and its facts; a hello's `analyzing` crawls are live.
- Each crawl keeps a **run clock** (`RunClock`): how long the run being watched has spent crawling. It advances by the
  time between progress events while the crawl's status is `running`, stands still while it's paused or stopping, and
  starts again when the crawl is continued (the engine's `elapsedMs` starts again). The engine has no such figure: its
  `elapsedMs` is the run's wall time, pauses included, which is where the clock starts for a run the page comes to
  part-way. It is the one clock the live screen's times go by.
- Each crawl keeps a **trail** for the found-and-crawled chart: a point per progress event at first, at most 600
  however long it runs (when full, every other point goes and from then on every second event is kept, then every
  fourth), with the newest always last, each placed on the run clock. `GET /history` gives what came before the page
  heard of the run; the trail carries the chart on (`screens/overview/foundAndCrawled.ts`). It starts again with a new
  run.
- Each crawl keeps its **pace samples**: one per progress event taken while it was running, as far back as a minute of
  the run clock, never thinned. A long crawl's pace is as exact as a short one's, and a pause is no part of it.
- Select from the live store with `useLive`, `useLiveCrawl` and `useLiveIds`. A selector returns stored values or
  primitives, never a new object.

## Routing

- Code-based TanStack Router (`app/router.tsx`), every IA route, fully typed. Screens other than Home load lazily.
- Search params are validated in `routing/search.ts`, at the router boundary: a view never sees a raw value. TanStack
  builds a route's search as `{ ...raw, ...validated }`, so every leaf route's validator returns every key the search
  has, its own checked and the rest `undefined` (`strictSearch`); the layout routes have none, so their children see
  the raw values to check. Anything unusable falls back to its default, never thrown; on load the router rewrites the
  address without it. Sorts are trimmed to 3 (each column once) and filters to 20, as the contract allows; a range
  that can't match and a group anywhere but on one check are dropped. Optional fields accept `undefined`, which
  removes a param.
- The inspector reads the current view's checked `inspect`, so it opens only on a view that has one. Its place in
  history is one model every screen uses (`routing/inspector.ts`, `useInspector`), so the inspector's slice inherits
  it: opened from a grid (Enter, a double-click) and following the grid's cursor while open, it's in place (a
  replace); walking a link inside it, or the palette's "Go to URL…" while it's open, is a push, each entry counting
  the steps walked from where it opened (`inspectSteps` in its history state), so back and forward retrace the walk; Esc or Close goes
  back over those steps to the entry it opened on, then drops `inspect` there in place. So Esc then Alt+Left leaves
  the screen, and from a walk A, B, C, Alt+Left shows B while Esc from C closes to the screen as it was before A. A
  browser keeps only so many entries (Chromium 50) and `history.go` past the oldest does nothing, silently: a close
  goes back no further than history holds, waits a second at most for it, and then closes on the entry it's on, so
  the inspector always closes (after a walk of 60, on the oldest entry left). One close runs at a time: Esc pressed
  again before history has gone back joins it, never goes back over the walk a second time. A
  screen's own replaces while it's open (Issues' filters and group) keep the history state (`state: true`). The
  palette's "Go to URL…" is `goToUrl`: on a view that has the inspector (`viewInspects`), a step of the walk when
  it's open and, when it's closed, opened in place as a grid opens it (a push would leave two entries for the same
  view once it's closed, and a back between them that shows nothing); from any other view (Overview, the crawl's
  settings) the crawl's URLs with the inspector open, a place of its own that back leaves.
- `/crawls/:crawl` opens the crawl's last view. The app reopens where it was left.
- Each screen's route renders `RouteError` when it fails (Try again where it could work, and Go to Home); the shell
  keeps working around it. Each part of the shell (sidebar, top bar, the engine's banner, the inspector) has its own
  `RegionBoundary`, so one that fails says so in its place. The root's `AppError` is for the frame itself, and offers
  to reload the window.

## The grid primitive

`grid/GridCore.tsx`, from the spike's measurements:

- **Windowed scroll space** (`scrollWindow.ts`): the real scroll height is at most 2,000,000 px, cut to a whole number
  of rows (`windowFor`: 28 px rows don't divide it, and the end would be 12 px out of reach), and maps onto the virtual
  offset through a base that moves in whole rows near the window's edges. A custom scrollbar shows the position in the
  whole list: its thumb is `border-control` (3:1), its travel is its own track's height, the wheel over it scrolls, and
  holding it tells the data layer the rows are being scrubbed.
- **Fixed-height virtualiser** (`rowWindow.ts`, `rowRange.ts`): the range is one division; nothing is stored per row.
  Overscan 6. Row heights come from `--row-height` (28, 32 or 40 px by density).
- **Paging** (`usePagedRows.ts`): pages of 200 rows as queries. The pages in view are asked for first, then the ones
  ahead in the direction of travel (further the faster), then one behind. A page fetch nobody waits for and that's
  beyond prefetch reach is cancelled, so its request aborts; one a copy waits for never is. While the thumb is held,
  nothing is prefetched and the pages in view are asked for at most every 150 ms, then at once when it's let go. At
  most 20 pages are cached for 5 s, trimmed when a page is added and when a fetch lands.
- **ARIA grid**: `aria-rowcount` and `aria-rowindex` include the header row, `aria-activedescendant` names the cursor's
  cell (an off-screen cursor row stays in the DOM, visually hidden; a row waiting for its page keeps its cells' ids),
  rows carry `aria-selected`.
- **Keys** (`gridNavigation.ts`, pure): arrows, Home and End within the row, Ctrl+Home and Ctrl+End to the first and
  last cell (the APG's rule), Ctrl+Up and Ctrl+Down to the first and last row (Cmd on macOS, whose laptops have no Home
  or End), Page Up and Down, Enter inspects, Space selects, Shift extends, Ctrl+A, Ctrl+C, Shift+F10 and the menu key.
  A row picked by a plain click follows the cursor; a selection built with Space or Shift stays put. Every move scrolls
  the cursor's cell into view, sideways too. Focus arriving lands on the first row wholly in view.
- **Selection** shows as the selected tint and the accent rule (3:1 or more) in a gutter that sticks to the grid's left
  edge; hover never covers it. Under forced colours a selected row is a Highlight outline, and the cursor on it
  CanvasText.
- **Copy**: the selection as tab-separated text with a header row, quoted as Excel does, up to 10,000 rows (fetching
  rows that aren't loaded). Crawled text is the site's, so a copied row's value starting with =, +, -, @, a tab or a
  carriage return gets a `'` in front (OWASP's advice against CSV injection): a spreadsheet shows it as text, without
  the quote. With nothing selected, Ctrl+C copies the cell under the cursor exactly as it is. A copy that fails says
  so in a toast; one that works is announced, with the rows it copied: a row that still can't be read, or has no
  text (a group's link to its rows), is left out, never copied as a blank line. A treegrid's `fetchRows` maps its
  rows to the pages they come from (Issues' groups).
- **Loading**: the first load's skeleton shows only after 150 ms, and once shown stays at least 300 ms, rows or not.
- **No rows**: `emptyState` says why, under the header. In a grid too short for it (a small window, the inspector
  docked at the bottom) it scrolls, and then, only then, it's a Tab stop with a name, so the keyboard can scroll it;
  with room for it the grid stays the one stop.
- Give it a new `key` when the rows change meaning (another sort or filter): that resets the scroll and selection.
- **Sorting from the header**: a column with `sortable` gets a button for a header, and the grid calls
  `onSort(id, focused)`; `grid/sort.ts` (`nextSort`) cycles ascending, descending, then the engine's order. The sort
  itself is the view's, in its route. The grid stays one Tab stop: the buttons are for the pointer (out of the tab
  order, and a press keeps focus on the grid), and the keyboard reaches the header the ARIA grid way: Up from the first
  row puts the cursor on the header row, Left and Right move along it, Enter or Space sorts, Down goes back to the
  rows. A sort brings a new grid (its `key`), so `useHeaderHandoff` hands the header to it (`focusHeader`): the new
  grid starts focused, with the cursor where the user pressed. Keys pressed on a header control never reach the rows.
- **Grouped rows** (`tree`): the grid becomes an ARIA `treegrid`. The caller maps its flat rows to a tree with four
  functions (`level`, `expanded`, `parent`, `onToggle`), so nothing is stored per row and a tree of thousands of groups
  pages and virtualises like any grid. Right on a closed group's first cell opens it, Left closes it or goes up from a
  row in it to its group, Enter and a double-click toggle a group (and activate any other row), and an element with
  `data-tree-toggle` (the chevron, `.si-tree-toggle`) toggles on press. `onToggle` returns where the group's row is
  afterwards when opening it moved it (Issues opens one group at a time, so opening one closes another above it): the
  cursor stays on it. Rows carry `aria-level` and `aria-expanded`; placeholders keep their level.
- **A place to start** (`startRow`): read when the grid mounts, the cursor starts on that row, which comes into view
  once the rows are there. For a grid shown again where it was left (the inspector's links).
- **Span rows** (`spanRow`): a row drawn as one cell across every column (`aria-colspan`), such as a group's pages and
  its "Show all N pages in URLs"; it is the cursor's cell whatever the cursor's column. Columns' `text` still gives
  every row's words for copying.
- Columns are fixed widths: a grid in a panel (Issues') shares the panel's measured width (`grid/useElementWidth.ts`)
  among them. Keep focusable controls out of cells (`Url` with `openable={false}`), so the grid stays one Tab stop.

The full grid (TanStack Table's column model, presets, chooser, filter bar, saved views) wraps this in the
slices.

## Keyboard

- One registry (`keyboard/registry.ts`). A command has an id, a title, a group, chords (`mod+k`, Cmd on macOS), an
  `enabled` check and a `run`. One keydown listener runs them. Precedence is explicit: a registration is the app's
  (`useAppCommands`, the shell's parts) or a screen's (the default), and a screen's registration of an id wins while
  the screen is shown, whatever order they were made in. Chords that type a character never fire in a text field;
  others only with `inInputs`.
- Chords match the key's character on a Latin layout (Ctrl+Z is the key marked Z on AZERTY), and the physical key
  (`event.code`) where the character is one no chord names: a letter on a non-Latin layout, a shifted digit,
  punctuation outside ASCII.
- A key that belongs to a command is taken from the browser even while the command can't run (F7 would start caret
  browsing), and so are Ctrl+E, Ctrl+F and Ctrl+P before their screens bring their commands. A held key runs its
  command once.
- While anything modal is open (the palette, the `?` sheet, a dialog, "Stopping crawls…"), the app behind it is
  `inert` and the registry runs none of its keys, but the one that toggles the overlay (`inOverlays`). When an
  overlay closes, focus goes back where it was, else to the view's grid, else to the main area (`restoreFocus`); a
  navigation that removes the focused element puts focus in the new view.
- A command chosen in the palette runs once the palette has given focus back (`afterFocusReturns` in
  `app/overlays.ts`), never a frame or a timer after the close: Radix gives focus back from a timer of its own once
  the palette's content has gone, nothing orders that before a frame, and a command that ran first had the focus it
  took (the inspector's for "Go to URL…", the source's search field, the checks' filter) taken back to the view. The
  wait is bounded (250 ms): then focus is given back anyway and the command runs, and Radix saying so later moves
  nothing. Focus is given back once per close, and only with no overlay open: the `?` sheet closing under the
  palette (Ctrl+K on it) keeps the place for when the palette closes. A menu needs none of this: its item acts
  while the menu is still open, and Radix gives no focus back once focus has left the menu.
- A view that takes away the control that had focus, without a navigation, gives the focus somewhere to go
  (`feedback/useFocusRescue.ts`): a banner's button goes once its action has worked (Resume, Continue) and focus goes
  to the page's title; the top bar's Pause becomes Resume and takes the focus, and Stop, which leaves no control
  while the crawl stops, hands it to the view; a filter's chip in Issues goes and focus goes to the filter field. It
  acts only when the element last focused in its container has left the document and nothing else took the focus,
  and after the view's own effects, so a particular move (the analysis banner's, to its Cancel) wins.
- **Esc**, innermost first: an open overlay (palette, `?` sheet, menu) closes; then the inspector; then the focused
  grid's selection clears. The grid asks the registry (`claims`) before taking Esc itself. A tooltip isn't a step: the
  Esc that closes it goes on to the rest (`keyboard/layers.ts` gives back the key Radix takes), except over a menu, a
  popover or a dialog, which close one Esc at a time.
- The same registry feeds button tooltips and `aria-keyshortcuts` (`Button`'s `command` prop), the `?` sheet, Settings
  > Shortcuts and the palette. A key can't be listed without working. Controls read it with `useCommandKeys`: one
  subscription each, a ranking worked out once per change and keys that keep their identity, so a change renders
  again only the controls whose keys changed, and a control without a command looks nothing up (grids put buttons and
  URLs in their cells).
- The grid's keys are registered with `scope: 'grid'`: listed, never dispatched (the focused grid handles them).
- The palette has two modes (`keyboard/paletteMode.ts`): the commands, and "Go to URL…" (`keyboard/GoToUrl.tsx`), a
  field that looks a URL or a path up in the open crawl (`POST /urls/lookup`) and opens the inspector on it. The
  command `go.url` opens it; so does a URL typed among the commands ("Go to URL /contact/": first for a scheme or a
  path, after the commands for a bare host, which could be a word with a dot). What's wrong is said under the field
  (`url-not-found`, `invalid-query`), never in a toast; a URL the crawl has only on another version of the site opens
  with a toast naming the one asked for (`requested`). Backspace in the empty field goes back to the commands, and
  however the palette closes (Esc, Ctrl+K again, a click outside) it opens on the commands the next time.
- The palette is an ARIA combobox over a listbox on a Radix dialog. cmdk was the brief's pick, but its last release
  (1.1.1) leaves `aria-activedescendant` naming a stale or removed option, so screen readers hear the wrong command.
  Option ids are the entries' own, so the attribute changes whenever the highlighted command does, and a live region
  that's always there says how many results there are once typing pauses.
- The palette and the `?` sheet are chunks of their own, fetched as soon as the app is idle
  (`layout/overlayChunks.ts`) and shown without `lazy` and Suspense (`lib/chunk.ts`), as the source viewer is.
  `lazy` suspends on its first render even when its chunk is there, and React holds back what follows a Suspense
  fallback for 300 ms: a session's first Ctrl+K took 320 ms in the production build with the main thread idle
  throughout, against 20 ms for every later one. A loaded chunk now renders in the render that opens it (first
  Ctrl+K 16 to 31 ms to the dialog on the 100k crawl); one not fetched yet shows nothing until it arrives.

## Theming and tokens

- `styles/tokens.css` holds every value: colour roles (surfaces, text, borders, accent, severity, status classes,
  charts, skeletons), the 4 px spacing scale, radii, elevation, type, motion, layout sizes and density. The values are
  direction B's (`app/design/directions/clarity`).
- It is one Tailwind v4 `@theme` with Tailwind's own palette and scales cleared, so `bg-surface`, `text-text-muted`,
  `rounded-md` and `shadow-2` exist and `bg-blue-500` doesn't. The spacing scale is closed: clarity's steps (0.5 to 16)
  only, so `p-13` is no class. Tokens without a namespace are used as `w-(--sidebar-width)`. Transitions default to the
  motion tokens, not Tailwind's. `narrow:` is below 1360 px, as the layout tokens' own query. ESLint refuses arbitrary
  values (`w-[37px]`, `[mask:x]`) and `dangerouslySetInnerHTML`.
- Themes use `light-dark()`: the page follows the OS unless `data-theme` says otherwise. Density is `data-density`.
- State indicators meet WCAG 2.2 AA (1.4.11): what's selected, current or highlighted carries the accent rule against
  its tint (`.si-current`, the grid's gutter, the palette's option), never the tint alone; text on it is 4.5:1 or
  more; placeholders are `text-muted`. High contrast maps the roles to system colours under `forced-colors: active`,
  with `surface-selected` as `Canvas`: selected and current things get a `Highlight` mark on the page's own colours,
  so no text sits on Highlight unreadable, and selected text is HighlightText on Highlight. Focus rings, the grid's
  included, are outlines, so they survive it. What can be selected and focused at once (a chosen segment, a pressed
  toggle, the current place, a selected row) carries its mark as a Highlight ring inside its edge, not an outline, and
  its focus ring is a CanvasText outline outside it, so both show (WCAG 2.4.7); what never takes focus itself (the
  grid's rows, the palette's options) keeps the Highlight outline.
- Fonts: Inter and JetBrains Mono are bundled (`styles/fonts.css`, the woff2 faces in `styles/fonts`), so the app
  looks the same offline; the platform's fonts follow them in the stacks.

## Components

`src/ui` is the component library every screen is made of, one component per file, in direction B's look
(`styles/components.css`, ported from `app/design/directions/clarity/app.css` under `si-` names). Each is in Storybook
under **Design system** in every state, forced where it can't be reached without touching (hover, focus and active
through `storybook-addon-pseudo-states`), in both themes and under forced colours; `npm run test:stories` runs axe on
every story. Screens compose these and write layout with the tokens; a look that only one screen has stays in its
screen, and one that a second screen needs moves here.

| Need | Use |
| --- | --- |
| An action | `Button` (primary for the one main action in view, secondary, ghost, danger only for losing data; `loading` shows a spinner inside after 300 ms, at the same width, gone the moment the work ends; `command` gives its tooltip and `aria-keyshortcuts` from the registry; `asChild` for a link that acts as a button). `IconButton` when there's no room for words: its `label` is required and is its tooltip |
| A hint | `Tooltip`: on hover after a rest, at once on keyboard focus, with the command's keys. Never the only place something is said. It describes its trigger by what the trigger's name lacks (`triggerName`: the hint, the keys), or not at all, so nothing is read twice; keycaps inside a control are `aria-hidden`, its keys given as `aria-keyshortcuts` |
| Text input | `TextField` (label, description, inline error, read-only, disabled, `lg` for Home's URL field) and `SearchField` (clear and Esc, the key that focuses it). Errors are inline, never toasts |
| A choice | `Checkbox` for a form's choice, `Switch` for one that applies at once, `SegmentedControl` for one of a few in view (counts and icon-only segments, a `description` per segment for what it means; the arrows choose), `Select` for one of a short list, `Tabs` for views of one thing |
| More actions | `DropdownMenu` and `ContextMenu` share `MenuItem` (with `command`, its keys on the right and disabled when it can't run), `MenuCheckboxItem`, `MenuRadioItem`, `MenuSeparator`, `MenuLabel`. `Popover` for content from a button; `Dialog` for what can't wait, `ConfirmDialog` only for what can't be undone (a failed action keeps it open, saying why inside it) |
| Severity | `SeverityMark` (shape and colour, the word its name) in dense rows; `SeverityBadge` (with the word) wherever there's room; `SeverityCount(s)` for counts. Notices are the quietest |
| Status | `StatusPill` for a crawl's status (the words of `crawl/status.ts`), `LiveBadge` and `LiveDot` for what updates live, `StatusCode` for a status code's class (only that meaning), `Badge` for a label without colour (`dashed`: what didn't happen) |
| Filters | `FilterChip` reads as a sentence, opens its editor, goes with ✕ or Delete (focus moves on to the next chip, else the one before, else Add filter); `AddFilterChip`; the `preview` chip beside a number that opens its URLs |
| Charts | `Sparkline` and `LineChart` (lines over time, drawn with visx in real pixels: the plot measures its box with `useElementSize`, so strokes keep their weight; no tweening), `StatusSplit` (the magnified status bar: the full bar, and the share that didn't answer 2xx across the full width beneath), `ColumnChart` (counts side by side; a number wider than its column is measured and written shorter, "313k", the whole number staying the column's name and tooltip), `StackBar` with `Legend` (a whole in a few parts). Every part that counts URLs is a `ChartLink` (the router's link) with its name and number, so a chart is never read by colour or by its bars alone; each chart says itself in words (`label`). Bars are elements, not SVG, so parts are real links with focus rings and names that wrap; under forced colours parts are told apart by their gaps and a hatch stays a hatch |
| Numbers | `Num` (counts, bytes, ms, rates, percentages; the unit smaller and muted), `Duration`, `RelativeTime` (kept current by one minute clock for the app, `lib/clock.ts`, idle while the window is hidden; the exact time in its tooltip and read after it); `Stat` and `StatLink` (a figure that opens its URLs) in `Stats` (`wrap` for more figures than columns), `MiniStat`s; `Meter` for a value on its check's scale (limits that would print over each other, the value being far past them, are one tick: "30–60"); `ShareBar` for a share; `ProgressBar` with its phase; `RefreshLine` for a panel that's refreshing; `Spinner` only inside what's working |
| Messages | `Callout` (insight, info, warning, error), `Banner` for a view's state with its one action (which goes under the words in a narrow panel; a live region, unless it states what something else announces, a crawl's status: `role="none"`), `EmptyState`, `FilteredEmptyState` (the filters and one-click clear), `SkippedState` (the reason in words). A callout's or banner's warning and error are the app's messages, never a finding's severity: they take the message tones (`--color-message-caution*`, quiet as clarity's out-of-date note; `--color-message-failure*`, the error hue under its own name, as the destructive fills) and Lucide's message icons, never the severity marks |
| URLs and places | `Url` (mono, truncated in the middle to its measured width, path-only on the crawl's origin, copy on click, open in the browser; with `onGo` a link in the app instead, in the link's colour, as the inspector walks to a URL, and `tabbable={false}` in a grid's cell), `Favicon`, `TextLink` and `NavItem` (router links; the current place keeps `.si-current`), `ListRow` and `ListHeading`, `PageHeader`, `SectionHeader`, `GridHeader` with `ResultCount`, `Facts`, `Card` |

The states rule: a component isn't done until each state it can be in (default, hover, focus-visible, active or
pressed, disabled, loading, invalid, selected or current, long text, empty) has its look in `components.css` and its
story, and passes axe in both themes. Every state indicator is 3:1 against what's beside it and never a tint alone;
focus is the one outline from `base.css`; under forced colours edges become system colours and what's selected or
current gets a Highlight ring beside the focus ring. Icons are Lucide's, through `Icon` (decorative unless labelled); severity's shapes
are drawn for the app and are never swapped for Lucide's.

## Feedback

- **Loading**: `useDelayedLoading` (skeleton after 150 ms, at least 300 ms once shown). Wherever a skeleton is used it
  stays up while `useDelayedLoading` says so, even when the data comes a moment after it appeared. Skeletons shimmer
  only when motion is allowed.
- **Toasts** (`notify.ts`): results with an action, Undo for anything reversible, errors with Retry when it could work
  and the detail copyable, progress with Cancel. Never for form validation: that's inline. A toast with an id replaces
  the one showing with that id: the same failure twice is one toast, and a crawl's toasts are keyed by crawl and event
  (`crawlToast`), so the live bridge dismisses one whose action no longer fits (Continue, once the crawl runs again).
  Never dismiss an id just before showing a toast with it: Sonner applies a dismissal two animation frames late, to
  whatever toast has the id by then, so the new toast goes as it appears (the bridge sends none as a crawl starts
  stopping, or with an event that carries the failure).
  Sonner is unstyled: every look is the app's, in unlayered rules more specific than Sonner's own stylesheet, so the
  app's colours and focus rings win, and a toast follows the theme setting, the OS's included.
- **Errors**: `ErrorState`, the plain cause first, "Details" behind a button, Try again only where trying again could
  work (`canRetry`: never for a problem the engine meant, such as a 404 or a 422), and the view's own actions (Analyse
  it, Reset the view). The crawl actions offer Retry per call: a Pause, Resume or Stop refused as `crawl-busy` (the
  crawl isn't live here) gets none.
- **Announcements**: `announce()` speaks through one polite and one assertive live region. Crawl status changes are
  announced by the live bridge, and by nothing else: a banner that states a crawl's status isn't a live region.
  A figure that moves with a crawl is never a live region either: the URL grid's count (`GridHeader`) and an
  inspector link grid's change about once a second while a crawl runs, and would be read out for as long as it
  lasts. A count is said once when the user changes the view (a filter, a search, a kind of link), through
  `useAnnounceResults`: when it's first known for the new view, in the words on screen (`formatShownOf`), and not on
  arriving at a screen or another URL, where focus and what has it are read.

## Rules every slice follows

1. Every view has every state in [the brief's table](v2-ui-brief.md#every-view-has-every-state), each in Storybook.
2. Tokens only. No raw colours, sizes or arbitrary values in components (ESLint refuses arbitrary values).
3. Server data through `api/` and TanStack Query; live data through the live store; a view's state in its route.
4. Every action is a registry command, so it has a key, a palette entry and a tooltip from one place.
5. Grids are `GridCore`. Lists of variable height use TanStack Virtual.
6. Crawled text renders as text, never HTML. Check titles and words come from the API, never hard-coded.
7. UK English and the glossary in every string. No raw engine ids or errors without plain words.
8. `aria-*` right the first time: axe runs on every story and in the end-to-end tests, with zero violations allowed.
9. Tests with every change: pure logic unit-tested, components with Testing Library, flows in Playwright. A test
   that waits for what a view says once a read has failed for good opens with `createQueryClientWithoutBackoff`
   (`test/queryClient.ts`): the app's two retries take 750 ms of real time, of the second a test waits, and on a
   busy machine the wait ran out first. A test never asserts an order that only a clock keeps: it holds what must
   come later (`test/lateTimers.ts`).
10. New npm packages pinned exactly, two weeks old at least, installed with `npm install --before=`.

## Left for the slices

- Every screen except Home, Overview, Issues, Sitemaps and robots.txt: the full URL and Links grids, New crawl, the
  crawl's settings, the rest of Settings. The inspector is built (below), and so are Sitemaps and robots.txt.
- The engine's E3b to E6: the links, sitemaps and robots grids, export, search, favicons. Analysis progress (E4) is in
  use on Issues and Overview (`crawl/analysis.ts` words its phases), as are E4's progress, alerts and history.
- Export (E5) on Issues: this check, or every check with a summary sheet, and Ctrl+E. Until the engine has it, Issues
  offers Copy URLs and no Export button.
- The URL grid's filter bar: it shows the route's filters as removable chips that read as sentences
  (`grid/filterSentence.ts`), so a group's "Show all N pages in URLs" lands somewhere legible, but nothing edits them.
- A grid sorted by something other than crawl order keeps its selection by row index while a crawl adds rows, so a
  live refresh can move rows under it: selecting by row id comes with the full grid.
- Each screen's own pieces, built from the components by its slice: the explanation band, charts. A piece two screens
  need joins `src/ui`.
- Drag-and-drop of `.sicrawl` files (the shell already delivers drops through Tauri's drag-drop event).

## Issues (the first slice)

`screens/IssuesScreen.tsx` and `screens/issues/`. What it settled, for the slices after it:

- **Pure first.** `checkList.ts` builds the list (each check's outcome, categories in catalog order, the filters,
  passed, skipped, hidden), the answer sentence and the first check to fix; `groupTree.ts` maps the open group onto the
  grid's rows; `copyUrls.ts` reads a check's rows 1,000 at a time with the `after` cursor. All unit-tested.
- **The route holds the view**: the check in the path (the first to fix, with a replace, when there's none), and
  `severity`, `q`, `category`, `group` (a `{ detail, relatedUrlId }` as `/groups` gave it), `sort` (a flat check's) and
  `inspect`. A click on a check is a new place; the arrow keys, the filter, the severity and opening a group replace.
  The first to fix is picked from the checks the severity, text and category filters keep (`CheckList.matching`), and
  a severity or category that leaves the selected check out (the sidebar's Errors, Warnings, Notices; a category's
  link) moves the selection to it, in place.
- **A category is a filter of its own** (`category`, the catalog's id for it), because its title as text matches other
  categories' checks ("URLs" is in "Links to broken internal URLs"). It comes from a link, stays through check
  selection, back and forward, and shows as a chip above the list whose ✕ removes it (focus then goes to the filter
  field). An id the catalog doesn't have names the checks listed under "Other checks".
- **One listbox** for the checks, a group per category named by `ListHeading level="none"`, options named by what they
  show; roving focus, so it is one Tab stop and Tab goes on to the check.
- **Groups are a paged treegrid**: one group open at a time (the route's), its first 10 pages, then "Show all N pages
  in URLs" (`check` `in` with `group`). A group the route names is found by reading `/groups` from the largest, 1,000
  at a time, as far as 50,000 (`issueGroupPlaceQuery`), then scrolled into view. One the check doesn't have (a link
  from before a re-analysis), or past those 50,000, leaves the route with a toast saying so. Copy URLs copies the group
  that was placed and is shown open, never the route's word for it. The likely-template callout sits above the grid, for the open group or else the
  largest that is one, as a fixed-height row can't hold a paragraph; the group's row carries a "Likely template" badge.
- **The analysis's grids don't refresh live**: a running crawl doesn't change a check's groups or rows, so the live
  bridge's once-a-second refresh leaves `issueGroups` and `issueRows` pages alone (`liveGridPagesOf`); the analysis
  that follows the crawl refreshes them.
- **Hidden checks** are a preference (`hiddenChecks` in the settings store), in every crawl, with Undo. Arriving on
  a check that's hidden (the inspector's link, the palette) shows the hidden checks, so the check the page is on is
  in the list, marked "Hidden"; hiding the one already open doesn't, and the list moves on.
- **Analysing from the screen**: "Analyse again" and "Analyse it" go when the progress banner comes, so focus goes to
  its Cancel; Cancel then says "Cancelling…" until the engine stops; when the banner goes, focus that was in it goes to
  the page's title. The start and the end are announced with the crawl's status (and the "Analysis finished" toast);
  a cancel is announced here. A crawl never analysed that can't be (interrupted, open in another window, unreadable,
  its file gone) says why, with Continue for an interrupted one.
- **Its commands** are in the registry's Issues group: Ctrl+F filters the checks, J and K move between them,
  Ctrl+Shift+C copies the check's (or the open group's) URLs, Ctrl+Shift+H hides or shows the check, and the palette
  has show passed, show hidden, Learn more and Cancel the analysis. Every check is in the palette by its title. J and
  K take focus in the list to the new check's option, and focus in the check (which goes with it) to the new check's
  title; anywhere else focus stays, and the new check is announced ("Title too long, 3 URLs").

## Overview (the second slice)

`screens/OverviewScreen.tsx` and `screens/overview/`. One route, two screens: the live crawl while it runs, then the
answer. What it settled:

- **Pure first**, as Issues: `statusSplit.ts` (the magnified bar's classes, what each is made of, the bars' widths and
  the filter that opens each class), `liveFigures.ts` (the pace over the last minute, the time left, the sparkline's
  window, what was found split three ways), `foundAndCrawled.ts` (the history joined to the trail, and round ticks),
  `alerts.ts` and `waits.ts` (plain words and the next action by the engine's stable codes), `answer.ts` (issues by
  severity, Fix first, categories, skipped checks by reason, indexability, depth, response times), `emptyCrawl.ts`
  (why a crawl read no pages) and `links.ts` (where every figure leads). All unit-tested, each figure with its link.
- **It builds on Issues' list** (`issues/checkList.ts`): the answer sentence, the totals and the severity counts are
  the same functions, and `rankChecksToFix` is the order both screens use (`firstCheckToFix` is its first).
  Analysing from a screen is one hook, `issues/useAnalysisControl.ts`, and what the analysis's read says of a crawl is
  another, `issues/useAnalysisAnswer.ts`: "not analysed" is an answer that comes as an error, which the query forgets
  the moment it's asked again, so a screen that went by the query alone swapped itself for its loading frame whenever
  a never-analysed crawl's analysis was refetched (as it ends, on a reconnect). The hook remembers it, so the screen
  keeps what it shows and treats the read as refreshing, and an analysis that has just ended stays "finishing" until
  its result arrives.
- **Hidden checks are left out of every figure**, on Overview, Issues and the sidebar alike: the answer's sentence,
  the severity counts, the categories and Fix first are all built from `countedEntries`, so they agree, and the
  sentence ends with what was left out ("1 hidden check isn't counted: it found 30 issues."), its count opening
  Issues, so nothing reads as clean because it's hidden. With every check that found issues hidden, Fix first lists
  none and says so (Issues still opens on one of them, which is its own rule).
- **Every number is a link to exactly what it counts.** A figure carries an `OverviewLink` (the URL grid's filters, or
  Issues at a check, a severity or a category by its id), which `overviewLinks.ts` checks against its route. The URL
  grid's chips word the columns those filters use (`grid/urlFilterWords.ts`), so the landing reads as a sentence.
- **Blocked is a class of its own**, live and done, so the two bars agree: E4's `statusClasses` count a firewall's
  answer as blocked whatever its status, and the finished crawl's facets are read under `outcome notIn [blocked]`, the
  blocked being what that filter left out. Each class links by `statusClass` with the same filter, and Blocked by
  `outcome`.
- **Not checked is not crawled.** A URL the crawl never asked, its host having stopped answering, is unknown, not
  broken. The URL grid has it as not fetched, with no status class, while E4's `fetched`, its `noResponse` class and
  the library's `fetchedUrlCount` count it (a stored outcome), and `/stats` counts it among the network errors. So
  the screen takes it out, live and done: Crawled, the status codes' total, No response and its reasons are without
  it, and a line under the classes says how many there are in the engine's words ("weren't checked because their
  sites had stopped answering"), opening `outcome in [notChecked]`. Live the count is `noResponse.notChecked`; done
  it is a second facets read beside the status one (`statusFacetsQuery`, the two as one query), because the facets
  can't tell these URLs from the ones only found and `/stats` is too slow to wait for. "N URLs crawled" and a stopped
  crawl's "after N URLs" are the status breakdown's total, which is what their link opens.
- **What the breakdowns count.** Status codes: every URL fetched, as the grid counts them. Indexability: the site's
  page URLs the crawl asked for (`isInternal`, `type` page, `fetched`), errors and redirects included, as "why not"
  names them. The crawler's own probes of the site's other versions (`source` `siteVersion`) are on the site and look
  like pages, but aren't the site's: every page filter leaves them out (`SITE_PAGES`), as the engine's own counts do,
  so an http site's https version not answering isn't one of its pages that "Didn't answer". The status codes count
  every URL fetched, probes included, live and done: a crawl's first "Network error × 1" is often that probe, which
  the live event counts (the engine's `statusClasses` are of every URL). Depth and response times: the site's pages,
  which is one count everywhere. A page is the engine's (`CrawlSchema.PAGE`): a URL of the site that answered 2xx with HTML the crawl read, so the filter ends with
  `wordCount isNotEmpty` (only parsed HTML has a word count; a URL whose status alone was checked, an iframe's target,
  has none). The cards' "N pages", the live "Pages" and its link, and the library's "pages read" are then the same
  number. Each card says its own count (the facets' `matching`).
- **The answer never waits for `/stats`**, which counts every link (1.1 to 1.4 s on the 100k crawl). It adds why URLs
  got no response to one row when it arrives, and a stopped crawl's queue to its banner. The answer is the analysis,
  the catalog and three facet reads, each card with its own skeleton and its own error.
- **One clock for the live screen's times**: the time the run being watched has spent crawling (the live store's
  run clock). Elapsed shows it, the chart's time axis is it, and the pace is read over the last minute of it, so the
  three agree, a pause moves none of them, and a resumed crawl's time left is right at once. A continued crawl's
  screen is of this run: its chart starts where the run did, at the counts it started with, and says "since it was
  continued". The earlier runs are in `/history`, counted back from the file, but the file doesn't keep when each run
  began and ended, so the hours or days between them can't be told from crawling, and on one axis they were most of
  the chart. The finished crawl's line says when it started and not how long it took, for the same reason: nothing
  the engine keeps is the time spent crawling (an engine gap; `finishedAt - startedAt` has every pause and stop in
  it).
- **The time left** shows only with a URL limit: what's left of the limit at the pace of the last minute of crawling,
  worded "done within" while the crawl may still find fewer URLs than its limit, and "about ... left" once it has
  found as many as it may fetch. At the limit there's none: the figure says the limit is reached.
- **Nothing moves as the numbers grow.** Figures update in place at most 4 times a second; each row of figures has a
  context line with room for two lines; six figures in a row are the smaller size unless the window is wide, so
  1,000,000 fits; the status bar keeps its height with nothing to magnify; what holds the crawl up (which comes and
  goes) is the last card of its column. Nor when the first figures come: before a crawl's first progress event the
  status codes' rows, the chart's box and key and the bar of what was found are all drawn, each figure a dash, and a
  URL limit nothing has spoken of yet (`maxUrls` undefined) isn't read as no limit.
- **Alerts.** While the crawl runs the list is a `log`, there from the start (an alert that arrives with its log isn't
  read out), and an alert's age, which the clock rewrites each minute, is marked `aria-live="off"` inside it. The
  newest twenty are drawn, the rest a press away, outside the log; the list and its items are memoised, so a progress
  event draws none of them again. They're ordered by when they were raised (`Date.parse`, never the text: the sidecar
  drops a time's trailing zeros). A blocked host's count is the file's as it was read, so it's said only of a crawl
  that isn't running ("37 responses blocked."); live, the Blocked row has the number.
- **A crawl's status is announced once**, by the live bridge. The banners that state it (paused, stopping, stopped,
  interrupted, failed, running elsewhere, the analysis's progress) take `Banner`'s `role="none"`: as live regions
  they had it read out two or three times, and the analysis's read out every phase and count.
- **Pause, Resume and Stop stay in the top bar**, on every screen, with F7 and F8. The screen's banners carry the next
  action for the state it's in: Resume when paused, Continue when stopped or interrupted, Analyse it, Analyse again.
- **States.** Live (running, paused, stopping, just started), the analysis running after the crawl (its phase and
  progress, with Cancel, over the crawl's breakdowns) or again (over the last answer), never analysed, out of date,
  partial (the notes), stopped and interrupted (how far it got, Continue), failed (the engine's reason), running in
  another window, unreadable, and a crawl that read no pages, which says how its start URL answered and offers the next
  action. Each is a story under Screens/Overview, the live ones from a scripted stream (2,900 URLs a second among them).
- **Its commands**: Cancel the analysis, and "Open the first check to fix" in the palette. The rest are the app's
  (Pause or resume, Stop, Continue, Analyse again).
- **Measured** (10 Nov 2026, this machine, the production build in headless Chromium at 1280x720, against the real
  sidecar). A live crawl of the engine's test site at the Fast preset (16 at a time; 66,283 URLs in 18 to 23 s, so
  2,900 to 3,600 URLs a second), three times (light, dark, 200% zoom): 60 fps from the first figure to the answer, 99%
  of frames within 16.8 ms and none over 33 ms. The 100k crawl from a click in the library: the answer is painted
  within 48 ms (62 at most over 7 opens) and every breakdown within 64 ms (78 at most); 136 ms the first time, before
  the sidecar has counted the crawl. A click on a status class shows URLs within 48 ms and its first rows within 63;
  Fix first shows its check in Issues within 52 ms; Tab moves focus within 18 ms.

## The inspector

`layout/InspectorSlot.tsx` is the sheet (where it docks, its size, the resize handle, Esc); `inspector/Inspector.tsx`
is what it shows. What it settled:

- **Pure first**, each unit-tested: `indexability.ts` (the four tests as a checklist, the failed ones, the verdict's
  words), `scale.ts` (a value against the limits the analysis used: under, within or over, by how much, and the
  check it trips), `redirectChain.ts` (the URL itself, then each hop, loops and too-long chains), `headers.ts`,
  `structuredData.ts` (a block to a tree, its children capped), `fetchState.ts` (what the crawl did with the URL:
  queued, skipped and why, blocked by robots.txt, no body…), `issueLinks.ts`, `hreflang.ts`, `links.ts`,
  `sections.ts`, `sourceSearch.ts`. The words for the engine's values are in `words.ts`.
- **One read for the URL, and sections with reads of their own.** `urlDetailQuery` (`/urls/inspect`) feeds the
  summary, issues, redirects, headers, structured data and hreflang. The links are grids
  (`['grid', 'inlinks' | 'outlinks', crawl, { urlId, kinds }]`, `GridCore` over `usePagedRows`), and the source is
  read only while its section is open (kept 30 s once it isn't: a page can be 16 MB). So a section fails alone: when
  `/urls/inspect` fails, its sections say so with Try again while the links and the source still load, and each
  section has its own `RegionBoundary`. A stale id (`url-not-found`) is said plainly, with Close.
- **Loading.** While another URL loads, the one shown stays for 150 ms (the query's placeholder), then the section's
  skeleton: a quick walk never flashes, and a slow one never shows the wrong URL for long. A refresh keeps what's
  shown under the thin line; a crawl's refreshes show nothing but the Live mark.
- **Sections are tabs** (`ui/Tabs`), the one chosen remembered for the session (`sections.ts`, in `sessionStorage`)
  and kept while walking, so inlink to inlink stays on Inlinks. A tab's count is what its section lists: the link
  tabs' counts are the link endpoints' totals (every kind), read once the inspector has stayed on a URL for 200 ms;
  the summary's `links` are the analysis's hyperlink counts, and are labelled so. The last URL's counts stay on the
  tabs until the next URL's are read, so the tabs never jump sideways. **The link counts are live**: their keys are
  under the URL's own (`['crawl', id, 'inspect', urlId, 'linkCount', …]`), so a crawl's once-a-second refresh reads
  them with the summary and a tab never says less than its section. That makes three small reads a second for the
  URL shown while a crawl runs (2 to 4 ms each on the 100k crawl): `/urls/inspect` doesn't carry the totals of every
  kind, and only the engine could make it one read.
- **Values on their scales.** `Meter` draws the title's and description's length, the word count, the response time,
  the HTML's size and an image's size (as sent, against `largeImageBytes`) against `limits`, which are the engine's
  (never hard-coded). A check is named beside a value only when the URL's `issues` have it: a value out of range
  that the analysis didn't flag says how far out it is, and no more. An image's check is on the pages that use it,
  so it's named when their issues name the image (`namedBy`), and its link opens their group. A value past its
  limit whose rounded words are the limit's own (1,015 ms and "1 s") is written to its last unit. A title's or
  description's `length` is the engine's display width (a full-width character or an emoji counts as two), so it's
  called "characters" only when it is the text's count of them (`characterCount`, user-perceived characters as the
  engine counts them); otherwise it reads as the engine's own details do, "48 characters (96 counting full-width
  characters as two)".
- **What the crawl did with a URL is said once, and no more than is known.** `fetchState.ts` takes whether the
  crawl is running: a URL it hasn't reached is on its way while it runs, and "The crawl stopped before it reached
  this URL" once it doesn't. Every section with nothing to show for the URL says that state's words, Redirects
  included (a URL that never answered isn't said to answer for itself). Where only a status code comes with a URL (a
  link's row, a canonical's or hreflang's target, a hop without an outcome), no code reads "No status": the crawl
  may never have asked (robots.txt), so "No response" and "Not fetched" would be guesses. Depth 0 is "the start
  URL" only for the URL the crawl started at; a probe of the site's other versions has depth 0 too.
- **An analysis that no longer stands is said where the URL's issues are**, in the summary and in the Issues
  section, by the Issues screen's own banner and its "Analyse again" (`crawl/LastAnalysisBanner.tsx`, which both
  use): never a second wording. The verdict's note says why it's the crawl's own judgement by where the analysis
  stands (out of date, not yet run, or running after the crawl).
- **A hidden check is still the URL's**: wherever the URL's issues are listed, a check the user hid from the Issues
  list comes after the others, marked "Hidden" (`splitHidden` in `sections.ts`), and is left out of the summary's
  and the section's counts and the tab's, as the sidebar and Overview leave hidden checks out of theirs. Its link
  lands on the check with the hidden checks shown.
- **Issues lead to Issues**: each check's title is a link to it with the issue's group open (the engine's `group`,
  or `issueLinks.ts`'s fallback for an engine without it), leaving the inspector; back returns to the URL. The
  fallback names a group only when it's sure to be there with the URL in it, else the check alone: a duplicate's
  cluster is named after its first page (the page itself, when other pages' issues name it), and is left out for an
  analysis another version made, which may have grouped the older way. `issueLinks.sweep.test.ts` checks every link
  it builds against the real groups of three analyses (`scripts/issue-links-fixture.mjs` makes its fixture).
- **Walking.** A URL of the crawl inside the inspector is `WalkUrl` (`Url` with `onGo`): a push through
  `useInspector().walk`. In the link grids Enter, a double-click or a click on the URL walks; the grid stays one Tab
  stop. A double-click is one step wherever it lands: the second click of one is no go of its own (`Url`), and a
  double-click counts for a link grid only when both its presses were on that grid, off the URL (the first click on
  a URL has walked, and the second may land on the next URL's grid).
- **Focus stays in the inspector when what had it goes**: a link walked from, another URL's content (a walk, back
  or forward), a button that replaces what it sits in ("Open Issues", "Try again", "Show more"). Focus that was in a
  section and is taken away with its element moves to the shown section's panel (`Inspector.tsx` watches for it: a
  `focusout` to nothing whose element has then gone, or, where taking an element away says nothing, the change to
  the page). It never falls to the page, from where the next arrow key would go to the view behind and change the
  URL inspected. Focus the user puts down themselves stays down. A link grid has a better place for it: the grid
  that comes takes the keyboard (another URL's, or every kind's after "Show every kind"), and starts on the row its
  cursor was left on (`GridCore`'s `startRow`; `links.ts` remembers each grid's row for the session), so back along
  a walk is where the walk left from. "Go to URL…" asks for a URL by name, so the inspector's panel takes the
  keyboard when it shows it (`useGoneToByName`), instead of leaving it on the view or, from Overview, on nothing.
- **The source** (`SourceViewer.tsx`) is CodeMirror 6, read-only but focusable, with a caret, so the keyboard moves
  through it and selects. It's a chunk of its own (239 kB), loaded while the app is idle once an inspector is open
  (`sourceViewerLoader.ts`), and without `lazy` and Suspense: React holds back what follows a Suspense fallback for
  300 ms, which made a session's first Source take a third of a second however small the page. It shows the stored HTML as text nodes: nothing of the page is
  ever markup in the app. Its theme and highlight colours are tokens (`--color-code-*`: each 4.5:1 on the surfaces
  and on a match's tint, system colours under forced colours). The search is the app's own field driving a state
  field (`sourceSearch.ts`): matches are marked whether or not the field has focus, counted to 10,000, stepped with
  Enter and Shift+Enter, searched 150 ms after typing pauses. Ctrl+F anywhere in the section focuses the field (the
  section takes the key before the registry does). To the registry the viewer is a text field (it has a caret), which
  keeps the keys a command doesn't claim `inInputs`: so its keymap runs those commands itself, first (`nav.back`,
  `nav.forward`, `shortcuts.show`), and every key of the app's works with focus in the HTML; docs/v2-ui-ia.md's
  keys table says whose each key is there. Turning "Wrap lines" on or off lays every line out again, which holds
  everything up while it lasts: nothing to notice on most pages, over half a second deep in a one-line page of
  megabytes (CodeMirror measuring one enormous line). It isn't made faster: the switch answers first, with the
  progress line, and the viewer is given the change a frame later, the line staying until it has drawn.
- **Docked at the bottom** the sheet is short, so its head is one line (what the URL is, the URL cut at its end, the
  actions).
- **The sheet's size has one rule in both docks** (`layout/inspectorSize.ts`): the view it shares the window with
  always keeps its minimum (`VIEW_MIN` in `settings/schema.ts`: a column of its grid beside the sheet, its head and a
  row or two above it), so the sheet's range is the settings' own (320 to 960 px wide, 200 to 720 px high) cut to
  the room that's left, measured from what holds them both. The resize handle's `aria-valuemin`, `aria-valuemax`
  and `aria-valuenow` are the sizes the sheet can really take and has; a size chosen in a bigger window stays stored
  and is clamped where it's read, so the first press of a key changes it. In a window too small for both minimums
  (1280x720 at 200%) the view keeps its own and the sheet has what's left.
- **Its commands** are the registry's Inspector group: open in the browser, copy the URL, dock at the bottom or on
  the right, show each section, search the HTML source. None has a key of its own: Esc closes, and the rest are in
  the palette and on the sheet's buttons. "Search the HTML source" is a request met once: the search field takes
  focus when it's there, and the request goes then, when another section is chosen and when the inspector shows
  another URL (each URL has a Source section of its own, which would otherwise take focus as it mounts).
- **Targets.** Links stacked in lists (issues, a tree's rows) are at least `--target-min` (24 px) tall.

## Sitemaps and robots.txt (their slice)

`screens/SitemapsScreen.tsx` with `screens/sitemaps/`, and `screens/RobotsScreen.tsx` with `screens/robots/`. Each
is two views on one route: a list, and one thing from it, named in the path. What they settled:

- **Pure first**, each unit-tested: `sitemapTree.ts` (the flat list as a tree's rows, what's open, each row's parent;
  whether a sitemap's counts are known), `sitemapLinks.ts` (the filter behind every count, the quick filters),
  `sitemapWords.ts` (how a sitemap was found, what became of it, the answer sentence, the pages no sitemap lists),
  `urlOutcome.ts`, `robotsWords.ts` (the four states, why a URL was blocked, a line's kind) and `robotsFile.ts` (how
  the viewer draws each line, which lines the crawl obeyed, what the list is narrowed to, the line of a URL).
- **Their reads are keyed as grids** (`sitemapKeys`, `robotsKeys` in `api/queries.ts`:
  `['grid', <grid>, <crawl id>, ...]`), the single reads too (the list of sitemaps, one origin's file). A running
  crawl changes every one of them, so the live bridge's once-a-second refresh of a crawl's grids keeps them current
  in place, with no skeleton and no progress line, and nothing was added to the bridge.
- **A link to a route with an optional part must name the part**, as `undefined` for "none". Left out, the router
  keeps the part the page is on: on one sitemap's page the sidebar's Sitemaps, Ctrl+5 and "All sitemaps" all led to
  the sitemap itself. The sidebar's and the palette's Sitemaps and robots.txt name it now.
- **A list is come back to where it was left** (`screens/listPlace.ts`): the row a sitemap or an origin was opened
  from is remembered for the session and handed to the grid as `startRow`, so Back, "All sitemaps" and the sidebar
  land on that row, not at the top of a thousand. The list of sitemaps is as tall as its rows, the ones under a
  closed index not counted, and never taller than the room there is.
- **A count of URLs in a sentence is a link too** (`screens/countSentence.ts`, drawn by `SentenceWithCounts.tsx`):
  a sentence is built as words and counts, each count with the URL grid's filters for what it counts, so the
  words and every landing are unit-tested. The robots.txt answer's "kept it from 1,204 URLs" opens the URLs whose
  `outcome` is `robotsDisallowed`, which is what the origins' counts add up to.
- **A stale place is said, with the list a press away**: a sitemap's id or an origin the crawl doesn't have
  (`sitemap-not-found`, `origin-not-found`, and a path that can't be an id) reads from `STALE_PLACE_WORDS` in
  `api/errors.ts`.

### Sitemaps

- **The list is a treegrid** (`GridCore` with `tree`): an index, the sitemaps it lists under it, to any depth the
  crawler reads. One Tab stop. Two things were added to the grid for it, both opt-in: `onActivateCell` (Enter or a
  double-click, with the column the cursor was on: each count cell leads somewhere else) and `GridTree.activates`
  (a group's row is a thing of its own, which Enter opens as it opens any row: an index has URLs of its own, those
  of every sitemap under it; Right, Left and the chevron open and close it).
- **Every count is a link to exactly its URLs**: that sitemap's page under the count's filter (`COUNT_FILTERS`:
  `fetched` is true or false, `indexable` is true or false, as the API says each is judged). The links are in the
  cells, out of the tab order, and Enter on a cell does what a click on its link does. A count of 0 is a plain 0
  (nothing to open). A sitemap whose contents the crawl doesn't know (left unread, not read yet, or failed before an
  entry was read) shows dashes, never 0. "Not crawled" is what's listed less what was fetched.
- **What isn't there is said.** No sitemap at all: a sentence by how far the crawl got (`discovery`), and where the
  crawl looks. None read: the same, under the list. How a sitemap was found, when the crawl can't say (`foundVia`
  null): "Not known", with why. The site's pages no sitemap lists: one read of the URL grid's `inSitemap` facet over
  the site's pages (`ANSWERED_SITE_PAGES`, as Overview counts pages). Both of its counts are links: the pages no
  sitemap lists, and the site's pages they're of. It needs the analysis: without one the line says so, and the
  counts that need none still show.
- **Without a URL summary** (never analysed, out of date, an earlier engine's, a crawl running) the list says which
  counts are the crawl's own judgement (indexable, non-indexable) and offers the analysis where it can run.
- **One sitemap is the URL grid narrowed to it** (`/sitemaps/urls`): the URL grid's columns and filter chips
  (exported from `UrlsScreen.tsx` as they stand: the full grid's slice gives them files of their own), with an
  Outcome column that goes by each URL's `outcome`, never `fetched` alone; quick search; sort from the headers, by
  the columns the engine says it sorts (`GET /api/url-columns`); the inspector on Enter. Its view is in the route
  (`filter`, `sort`, `q`, `inspect`), as the URL grid's.
- **Quick filters** are a segmented control: All, Not crawled, Non-indexable, Redirecting or broken (by `outcome`:
  a redirect, an error or no answer; not what robots.txt or a firewall kept the crawl from). Filters that aren't one
  of them (a count's link from the list) are a segment of their own while they last, "Other filters": a radio group
  chooses the segment focus lands on, so with none chosen, Tab onto the control replaced the filters.
- **Counts that must agree are one read's.** A quick filter's count is the list's; the one chosen, and All, take the
  grid's own count once it's known, so the header and the filter never disagree while a crawl runs.
- **A link can name a sitemap by its address** (`?url=`): Overview's "A sitemap was left out" does, and the list
  goes on to that sitemap's page, in place.
- **Its commands** are the registry's Sitemaps group: search this sitemap's URLs (Ctrl+F), show every sitemap, copy
  the sitemap's URL, open it in the browser, and each quick filter.

### robots.txt

- **The origins are a paged grid** (200 a page, as every grid): the site's own first, then the rest by the URLs they
  blocked, which is the engine's order. Each says what its answer meant to the crawl in words of its own: read (an
  empty file says so), missing (nothing refused), unreachable (its URLs refused) and unanswered (crawled anyway). A
  crawl told to ignore robots.txt says so at the top of both views.
- **The file is the inspector's source viewer.** The search field, its count, Wrap lines and the viewer were taken
  out of the Source section as they stood, as `inspector/SourcePane.tsx`, which both use: Ctrl+F, Enter and
  Shift+Enter, the 10,000-match limit and the caret are the same. The viewer gained a `language` (HTML, or plain),
  marks for lines (`inspector/sourceLines.ts`: a tone, a name, a note, a link) and a chosen line. The engine says
  what each line is and whether it's part of the group the crawl obeyed: nothing is parsed or matched in the app.
- **What the marks mean**: the obeyed group carries the accent's rule at its left edge, the other groups are muted, a
  directive's name is set apart, a line the crawler made nothing of says why after it, and a rule that blocked URLs
  says how many, as a link. A rule that blocked none says nothing (a real file has hundreds). The file's line above
  it says the same in words ("As ShoutingIguana, the crawl obeyed lines 5 to 9."). Under forced colours the rule is
  CanvasText against an edge of the page's own colour, and the chosen line is outlined in Highlight; the mark's
  class is on the editor's content, as a theme's rules inside a media query can't name the editor itself.
- **Choosing either way.** A line is chosen with a press on its link, or Enter with the caret on it: the route takes
  `line`, the list is that line's URLs, and the file marks it. Moving the list's cursor shows each URL's line in the
  file, focus arriving on the list included. The two cases no line accounts for (`unreachable`, `unknown`) are links
  over the list, with the route's `reason`. What narrows the list shows as a chip that removes it.
- **A URL blocked without a line says which case it is**: robots.txt couldn't be read, or nothing kept refuses it
  ("Only the last answer is kept, so what refused it is gone"), and no more than that.
- **Copying**: the file as it is, and the blocked URLs the list is narrowed to (Ctrl+Shift+C), read 1,000 at a time
  by the Issues screen's copy, with its progress toast.
- **The layout**: the file and the list side by side, stacked below the `split` width (a narrow window, or the
  inspector open beside them), each with a height of its own then.
- **Its commands** are the registry's robots.txt group: show every origin, copy the file, copy the blocked URLs,
  open the robots.txt in the browser, list every blocked URL.

### From elsewhere

- The inspector's "Blocked by robots.txt" leads to the file on that URL's own origin, and "In a sitemap" to
  Sitemaps. `/urls/inspect` says neither which line nor which sitemaps, so neither link can be more exact.
- The mock answers these endpoints from fixtures of their own (`api/mock/sitemapsRobots.ts`, saved by
  `design/data/extract/sitemaps-robots.mjs`). It counts each sitemap's URLs over the rows its own grid has.

### What the engine doesn't give

- `POST /robots` takes only `offset` and `limit`: no search, no filter to the origins that blocked something, and
  no count of them. The order puts them first after the site's own, and the screen says so.
- Nothing counts a sitemap's redirecting or broken URLs, so that quick filter carries no number.
- `/robots/blocked` can't be asked for one URL, and `/urls/inspect` doesn't carry the line: arriving from the
  inspector, the file can't show that URL's line until the URL is found in the list.

### Measured

10 Nov 2026, this machine, the production build in headless Chromium at 1280x720, against the real sidecar: the
100k crawl (112,577 URLs, 13 sitemaps listing 101,081 URLs) and the garden crawl (17 origins). Each is the median
of 7, from the key or pointer event to the frame that shows it, with the worst in brackets (the first of each, the
sidecar not having counted the crawl yet).

- **The first page of a grid (300 ms)**: the list of sitemaps 32 ms (83); one sitemap of 101,081 URLs 31 ms (75),
  its 2,348 non-indexable ones 32 ms (49); the origins 30 ms (64); an origin's file with its blocked URLs 33 ms
  (70); the garden crawl's origins 31 ms (33) and a real 403-line file 30 ms (31).
- **A sort or a filter (300 ms)**: a quick filter to its rows and count 33 ms (34); a sort from the header 30 ms
  (32); a rule's link to its URLs listed 31 ms (32).
- **A click or key to a visible response (100 ms)**: a quick filter chosen 13 ms (14); a rule's line marked 14 ms
  (15); Left closing an index 12 ms (14); Enter opening a sitemap 9 ms (17); Down in the blocked URLs 9 ms (14);
  Ctrl+F to the file's search 6 ms (14).

### The sweep

`e2e/sitemaps-robots.sweep.spec.ts` reads every count off both screens and follows it: each sitemap's four counts
(a 0 has no link, so the engine is asked what its link would open), each quick filter, the pages no sitemap lists
and the site's pages they're of (the engine is asked for both first, so the sweep waits for the sentence it knows
is coming and never passes for want of one), every URL robots.txt kept the crawl from,
each origin's blocked URLs, each line's, and the two cases no line accounts for, which must add up to the origin's.
It runs on the tests' own crawl of the synthetic site, and on a real one with `SI_E2E_CRAWL`.
