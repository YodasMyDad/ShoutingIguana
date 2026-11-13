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
| `src/grid` | The grid primitive (windowed scroll space, virtualiser, keyboard and selection model, copy, paging hook) and the full grid's pieces: the filter bar and its editor, the column chooser, facets, copying a whole view |
| `src/keyboard` | Chords, the command registry, the app's commands, the palette and the `?` sheet |
| `src/settings` | Preferences: schema, store (the shell's settings commands, or `localStorage`), appearance |
| `src/feedback` | The loading rule, skeletons, toasts, error states, route errors, announcements |
| `src/layout` | The shell's layout: sidebar, top bar, inspector slot, banners, the stopping overlay |
| `src/screens` | One file per route, a screen's own pieces in a folder beside it (`screens/issues/`). Home, Overview, Issues, Links, the URLs grid, Sitemaps and robots.txt work; the rest are stubs for their slices |
| `src/inspector` | The URL inspector: its sections, their pure logic, and the HTML source viewer (see The inspector) |
| `src/app` | The router, the query client, overlays, sections, the root component |
| `src/ui` | The component library: buttons, fields, menus, severity, figures, URLs and the rest (see Components) |
| `src/styles` | `tokens.css` (every design value), `base.css`, `components.css` (the library's look), the Tailwind entry |
| `src/crawl` | Crawl status words, the crawl actions, opening crawl files |
| `src-tauri` | The shell (see below) |
| `scripts` | Building the sidecar, `npm run dev`, synthetic crawls, the orphan check, the shell smoke test and the shell smoke walk |
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
  What the tray holds depends on which of Windows' measures is read, so the budget names one: the 50 MB tray budget
  is of the **private working set** (the pages in RAM no other process shares: Task Manager's "Memory" column, summed
  over the shell and every process it started, its engine and what the engine started left out), and it holds. The
  earlier "flat 23 MB" (9.6 MB at 30 s, 20 MB at 3 minutes, 23 MB from 6 to 11 minutes) didn't say which it
  was; by its size it was this one (inferred: no other measure comes near it). The shell pass's 66 MB was the
  **working set**, which counts every shared page (the system's and WebView2's DLLs) once in each of the seven
  processes. `npm run smoke:shell -- --tray-stay <s>` now reads all three every 30 s while the window is hidden (`shellMemory` in `scripts/lib/shell.mjs`). Measured on 12 Nov 2026 (WebView2 157,
  Windows 11, a crawl of the test site running throughout, two runs, 30 s to 3 minutes hidden): private working set
  19 to 20 MB at 30 s, 28 MB at 60 s, 31 to 32 MB at 2 minutes, 33 to 34 MB at 3 minutes, still creeping by under
  1 MB a minute; working set 80 to 81 MB at 30 s and 95 to 96 MB at 3 minutes; **private bytes** (memory committed
  to the processes, in RAM or paged out, most of it WebView2's browser and GPU processes) a steady 316 to 321 MB.
  So the budget holds by the measure it names, and wouldn't by the other two, which no budget here is of. Not
  measured: a stay past 3 minutes this time, so whether it levels off near the earlier run's 23 MB plateau or
  higher isn't known. While hidden, the DevTools connection the walk drives the page through drops (the page
  doesn't: it comes back as it was), so the walk attaches again after a long stay. The shell's own event stream keeps the
  tray and the notifications going, and a crawl the engine took with it when it crashed is told once the new engine
  reports it: "Crawl interrupted".
- **OS.** Single instance. A second launch focuses the window, and the `.sicrawl` paths of every launch wait in the
  shell until the page takes them, which it does once the engine can open them: `shell://open-crawl-files` says more
  have arrived, and `take_pending_open_files` hands each over once. Window size and position are remembered. The
  window stays on the app: any other navigation is refused, and an http(s) link opens in the browser.
- **Keys.** WebView2's browser keys are off, so the registry owns F5, Ctrl+F and the rest. Every reserved key was
  checked with real key presses (F7's caret browsing, Ctrl+S, Ctrl+J, Shift+Esc, Ctrl+P…) and none does anything in
  WebView2. F10, and a tap of Alt on its own, would put the window in Windows' menu mode and lose the next key, so the
  shell keeps them from it. The context menu keeps only editing items. A key sent over DevTools tests none of this
  (see The shell smoke walk): the browser's keys and the shell's handler are checked with keys put in the webview's
  own queue.
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

## The shell smoke walk

Every other test of the UI runs in headless Chromium against a Vite build. `npm run smoke:shell`
(`scripts/smoke-shell.mjs`) walks the five flows in the real thing: the built exe, its own WebView2, its own sidecar.
It needs `npm run build:sidecar` and `npm run tauri build` (`-- --no-bundle` is enough) first, takes about a minute,
and isn't run in CI: it needs a Windows desktop session (a disconnected one will do, which is where it was written).

- **How the page is reached.** The app starts with `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=<n>`
  and its own data, state and profile folders in a temp folder (`shellEnvironment` in `scripts/lib/shell.mjs`), and
  Playwright's `connectOverCDP` attaches to that port. Three things differ from a page Playwright opened itself:
  - Playwright emulates a light colour scheme on every context it attaches to: `page.emulateMedia({ colorScheme: null })`
    gives the page the system's back.
  - The app reopens where it was left, so loading `/` isn't Home: the walk clicks the sidebar's Home.
  - A key sent over DevTools enters below WebView2's own key handling. It never raises `AcceleratorKeyPressed` (the
    shell's F10 and Alt handling isn't run), and Chromium skips its browser accelerators for it when it has no native
    key code (Playwright's `keyboard.press`) and runs them whatever `AreBrowserAcceleratorKeysEnabled` says when it
    has one: with one, F5 reloads the app and F3 opens the find bar, in a build that turns the keys off as in one
    that leaves them on. So Playwright's keys are used for the app's own commands only.
- **What DevTools can't reach** goes through `scripts/shell-window.ps1`, from outside the app: keys put in the
  webview's own message queue (its `Chrome_WidgetWin_1` window, given its thread's focus first), which WebView2 handles
  as it does a real key press; a click on the tray icon (the callback message Windows posts the icon's window); a
  button of a native dialog; closing the window; whether the window is shown, a native dialog is up, or the webview
  has opened a window of its own (a find bar, downloads).
- **What it walks** (14 steps, each recorded, a failed one not stopping the rest): the page answers on the DevTools
  port at `http://tauri.localhost`; the shell handed it a ready engine and the library lists the crawl; a crawl's
  answer; a status class to exactly its URLs; the inspector (Enter, a walked link, Alt+Left, Esc); the HTML source and
  Ctrl+F; Issues; the palette, "Go to URL…" and the `?` sheet; F5 and F3 pressed in the webview do nothing while the
  page still sees them; the engine killed and started again; a crawl of the engine's test site from Home, paused and
  resumed with F7; the window closed to the tray with the crawl going on, and back through the tray icon; "Stop
  crawling and quit?" (Cancel keeps both, Stop and quit ends both); nothing left running and no page error.
  `--shots <folder>` keeps a picture of the page after each step, `--skip keys,live,tray,restart` leaves parts out.
- **What the first walk by hand found** (10 Nov 2026, WebView2 157, Windows 11 at 150%): everything above works as in
  Chromium, with these differences. Back from the tray after a crawl ended there, Overview failed (fixed: see
  Overview's `useAnalysisAnswer`). Scrollbars show (headless Chromium hides them): they were Windows' classic ones,
  arrows and all, and one under the inspector's tabs. They are now `base.css`'s: thin, a border-control thumb on no
  track and no arrows, by the `::-webkit-scrollbar` parts in Chromium (which ignores the parts on an element that sets
  `scrollbar-width` or `scrollbar-color`, and with `scrollbar-width: thin` alone still draws small arrows), the
  standard properties in a browser without the parts, and the system's own under forced colours; a strip of tabs has
  none (`scrollbar-width: none`): a plain wheel scrolls it sideways, an end with more tabs past it fades
  (`--tabs-fade`), and its chosen tab is scrolled into view when it changes and when the tabs change size. Headless Chromium draws these parts too, so the
  end-to-end tests now lay out with the shell's 10 px bars. Seen in the release build through this walk, before and
  after (12 Nov 2026). The quit question and the tray's menu are Windows' own, so they don't follow the app's theme. With the browser
  keys turned off, WebView2 still raises nothing of its own for F5, Ctrl+R, F3, Ctrl+G, Ctrl+J, Ctrl+P, Ctrl+S, Ctrl+U,
  F12 or Shift+Esc, checked against a build that leaves them on, where each does its browser thing. F10 and a tap of
  Alt are handled by the shell and leave the next key alone; Shift+F10 and Alt+Left get through.

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
  in once it's fetched, and its tabs' link counts with it; its links are grids, and refresh as grids do. A window
  nobody can see (`document.hidden`) is left alone, for every grid: the bridge reads nothing for it, as the live store
  holds its renders back, and the crawls that ticked meanwhile refresh once, at the moment the window is shown again.
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
- `/crawls/:crawl` opens the crawl's last view, in the current mode. The app reopens where it was left.
- A route with a Simple screen renders it in Simple through the one table, `app/simpleScreens.ts`
  ([The routes' table](#the-routes-table-adding-a-simple-screen)).
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
- **Rows with an identity** (`rowKey`, opt in): the cursor is on a row, not at a number. When the rows change and
  another row is at the cursor's number (a running crawl put one above it), the cursor goes to where its own row
  went, looked for among the loaded rows up to 500 either side; a selection that follows the cursor goes with it,
  and no move is told, as the row is the same. A row that isn't found leaves the cursor at its number. With
  `startKey` beside `startRow`, a grid starts on the row it was left on wherever that row is by then. Without a
  key the cursor is a row number, as before. A list a running crawl changes should give one.
- **A header of two lines** (`si-grid-frame--two-line-head` on the grid's `className`, with a `headerHeight` 12 px
  taller): a long column name wraps over a narrow count column, and the rows' cells stay on one line.
- **Span rows** (`spanRow`): a row drawn as one cell across every column (`aria-colspan`), such as a group's pages and
  its "Show all N pages in URLs"; it is the cursor's cell whatever the cursor's column. Columns' `text` still gives
  every row's words for copying.
- Columns are fixed widths: a grid in a panel (Issues') shares the panel's measured width (`grid/useElementWidth.ts`)
  among them. Keep focusable controls out of cells (`Url` with `openable={false}`), so the grid stays one Tab stop.
- **Which column** (`onActivate`'s third argument, `onActiveColumnChange`): Enter says the id of the column the cursor
  was in, and a double-click the cell's, for a grid whose rows hold more than one thing to open (a link's two ends);
  the cursor's column is reported as it changes, for an action on that column (copy it).
- **A header that doesn't sort says why** (`sortHint`): in a grid whose other columns sort, a column that can't is
  no button, its reason is its header's tooltip, and Enter or Space on its header announces it. A key that does
  nothing reads as broken.
- **What a slow first load is doing** (`loadingNote`): a line of words over the first load's skeleton, shown with it
  (after 150 ms, never over rows already there) and outside the grid's rows. The view announces the same words.

- **A sort of several keys** (the URL grid's three): Shift+click or Shift+Enter on a header adds the column as the
  sort's next key (`onSort`'s third argument, `extend`); `grid/sort.ts` `nextMultiSort` has the rules (a plain press
  sorts by that column alone; with Shift a key turns about, then leaves; a fourth is refused and said). A sorted
  column of a sort of several keys shows its place beside its arrow (`sortOrder`); only the first key's header carries
  `aria-sort`, the others say "sorted second, descending" in words.
- **What a column's values share** (`headerDetail`): quieter words after a header's name ("URL on example.com"), never
  copied with the header.
- **The row menu**: `onContextMenu(index, column, anchor, menu)` on a right-click, Shift+F10 or the menu key. `menu`
  is the rows it acts on (the selection when the row is in it, else the row; a right-click outside the selection first
  makes that row the selection, one inside it moves the cursor there and keeps it), held as they were when it opened
  (`menu.row`, and `rows()`, read at once where they weren't loaded): a running crawl that moves the rows while it's
  open never turns it onto another row, so a screen never reads its row again by number. It copies them as Ctrl+C
  does and gives the keyboard back to the grid, with the cursor on the row (never left on the header). From the keys
  it opens at the row's own place, worked out from the scroll space (its one cell's for a row drawn as one), where
  the keys have just scrolled it into view. The browser's own menu, which the same keys ask for on the focused grid,
  is kept away.
  `grid/RowMenu.tsx` is the menu (`MenuAt`, at the point) and the items every grid of URLs shares; see
  [The URL grid](#the-url-grid).

The full grid's pieces (the filter bar and its editor, the column chooser, facets, copying a whole view) wrap this:
see [The full grid's pieces](#the-full-grids-pieces). Column presets and saved views are the URL grid's: see
[The URL grid](#the-url-grid).

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
| More actions | `DropdownMenu` and `ContextMenu` share `MenuItem` (with `command`, its keys on the right and disabled when it can't run), `MenuCheckboxItem`, `MenuRadioItem`, `MenuSeparator`, `MenuLabel`; a `MenuItem`'s `detail` is what it would give (a facet value's count), read with its name. `Popover` for content from a button, or from an `anchor` that something else opens (a filter chip's editor; focus goes back to the anchor); it opens with the keyboard in the first control of what it holds, or the one its content marks `data-autofocus` (the value of a filter being changed), never on its head's Close button; `Dialog` for what can't wait, `ConfirmDialog` only for what can't be undone (a failed action keeps it open, saying why inside it) |
| Severity | `SeverityMark` (shape and colour, the word its name) in dense rows; `SeverityBadge` (with the word) wherever there's room; `SeverityCount(s)` for counts. Notices are the quietest |
| Severity in Simple | `SeverityWord` (Serious, Worth fixing, Worth a look, Couldn't check, Nothing wrong, with Error's, Warning's and Notice's shapes and colours; `badge`, `large`, `inline`, or `mark` named in the word), `plainSeverityCount` ("2 serious problems"); `SeverityMark` takes a `label` for a name other than Advanced's word. `PlainCheckRow` is a check in Simple (its named mark, plain title, `OneFixTag`, first sentence, count and share, a place number in Fix first), a link with `asChild`, in a `PlainCheckList` |
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

- Every screen is built in Advanced: Home, Overview, Issues, URLs, Links, Sitemaps, robots.txt, the inspector, New
  crawl, the crawl's settings and Settings (below, and [Modes, Settings and New crawl](#modes-settings-and-new-crawl-round-3)).
  Simple's own screens are round 4's, each one row of `app/simpleScreens.ts`.
- The engine's E3b to E6: the links, sitemaps and robots grids, export, search, favicons. Analysis progress (E4) is in
  use on Issues and Overview (`crawl/analysis.ts` words its phases), as are E4's progress, alerts and history.
- On every grid: column resize, reorder and pin, "filter by this value" and "exclude it" in the row menu. None is
  built: a grid's columns are fixed widths in the order chosen. Saved views are the URL grid's only. Export is built:
  see [Hand over, search, favicons and the analysis's limits](#hand-over-search-favicons-and-the-analysiss-limits).
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
  title; anywhere else focus stays, and the new check is announced ("Title too long, 3 pages"; "Internal URL returns
  4xx, 9 URLs").
- **A check's count is called what the check flags** (`crawl/checkSubject.ts`, the one place for it, so Issues, Fix
  first and the inspector can't word a check two ways), by the catalog's `subject`. A `page` check flags only pages:
  "Pages affected", "Share of pages, of the N pages analysed", "Affected pages", a "Pages" column, "Show all N pages in
  URLs", and "3 pages" in its row and when J or K announces it. A `url` check can flag any URL the crawl crawled:
  "URLs affected", "Share of URLs, of the N URLs crawled", "Affected URLs", a "URLs" column, "Show all N in URLs". A
  check without a `subject` (an engine from before E3b, the design fixtures) reads as a `url` check: a page is a URL,
  and every URL it flags is one the crawl crawled, so that's true whatever it flags.
- **A share is of the analysis's own total, stored with the check's count** (`shareWhole` and `checkShare` in
  `crawl/checkSubject.ts`: the one place that decides it). A `page` check's is "of the N pages analysed": the
  analysis's `pagesAnalyzed`, the pages its page rules ran on. A `url` check's is "of the N URLs crawled": the
  analysis's `crawledUrlCount`, the URLs crawled when it ran (the score's `covers.urls`; the URL grid's `fetched is
  true` as it was then, which is where "Open them in URLs" lands while the crawl hasn't changed). Both were counted by
  the analysis that counted the check, so a share is true however old the analysis is, and is shown while the crawl
  runs again and after it was continued: a check that flags 52 of the 1,661 pages analysed is 3.1%, never "0.6% of
  the 9,206 pages crawled". Never the library's counts or a grid's, which are of the file as it is now (the same
  numbers while the analysis is current, but for a Cloudflare `/cdn-cgi/` page the checks leave out of the pages: 152
  against 153 on the tests' crawl). Not the URLs found either: no analysis keeps them, and they are more than the
  URLs crawled (6,286 against 4,521 on the garden crawl; 41,629 against 9,464 on a crawl stopped early). Every URL a
  check flags was crawled on the crawls looked at (the garden crawl and two of the test site, 13 URL checks: none of
  the flagged URLs has `fetched` false), and the walk below holds every check to it. There is no share of URLs for
  an analysis that sends no `crawledUrlCount` (an engine from before the score ran it: the design fixtures' analyses
  among them), nor any share when the count is past its total. Nothing is read for a share: both totals come with the
  analysis. Fix first and the inspector show counts, never a share. `e2e/issues-subject.spec.ts` walks every check of
  the catalog on the tests' crawl: its words by its `subject`, its total against the analysis's own and against the
  URL grid's count, its count within it, and every URL it flags a crawled one.

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
  the same functions, and `rankChecksToFix` is the order both screens use (`firstCheckToFix` is its first): the
  engine's, the analysis's `fixFirst` (severity, a fault of the whole site before any other of its severity, the URLs
  flagged, catalog order), taken as it comes and never sorted again by count, the hidden checks left out of what's
  listed; the app's own (severity, then count, then catalog order) only for an earlier engine's analysis, which has
  no `fixFirst`. Fix first counts each check in what it flags ("3 pages", "4 URLs": `crawl/checkSubject.ts`), and says
  which order it is: "Most severe first, site-wide faults ahead, then most URLs", or the app's "then the
  most pages or URLs affected".
  Analysing from a screen is one hook, `issues/useAnalysisControl.ts`, and what the analysis's read says of a crawl is
  another, `issues/useAnalysisAnswer.ts`: "not analysed" is an answer that comes as an error, which the query forgets
  the moment it's asked again, so a screen that went by the query alone swapped itself for its loading frame whenever
  a never-analysed crawl's analysis was refetched (as it ends, on a reconnect). The hook remembers it, so the screen
  keeps what it shows and treats the read as refreshing, and an analysis that has just ended stays "finishing" until
  its result arrives. It never lets the mark go while the crawl reads as crawling: back from the tray after a crawl
  ended there (the page was suspended, so its item still said running for a moment while the analysis had arrived),
  marking and letting go on every render was React's "too many re-renders", and Overview failed.
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
  can't tell these URLs from the ones only found and `/stats` is too slow to wait for.
- **"N URLs" is one number everywhere** (E3c): the URLs the grid lists as crawled (`fetched` is true), the library's
  `crawledUrlCount` and a running crawl's `crawled` (`crawledUrls` in `crawl/status.ts`): Home's library, the top
  bar's status pill, the sidebar's running crawls, the Crawl stopped and Crawl finished toasts, the finished
  Overview's "N URLs crawled" and a stopped crawl's "after N URLs". From an earlier engine, which sends neither, the
  same subtraction is made from `fetchedUrlCount` or `fetched`.
- **What the breakdowns count.** Status codes: every URL fetched, as the grid counts them. Indexability: the site's
  page URLs the crawl asked for (`isInternal`, `type` page, `fetched`), errors and redirects included, as "why not"
  names them. The crawler's own probes of the site's other versions (`source` `siteVersion`) are on the site and look
  like pages, but aren't the site's: every page filter leaves them out (`SITE_PAGES`), as the engine's own counts do,
  so an http site's https version not answering isn't one of its pages that "Didn't answer". The status codes are
  the site's own too, live and done (E3c): each class, code, reason and the URLs not checked less the probes' part
  (live, the progress event's `siteVersions`; done, the probes' own rows in the URL grid, a handful on any site, read
  beside the facets in `statusFacetsQuery` and counted as the event counts them, `probeCountsOf`), each link with
  `source notIn [siteVersion]` beside its filters, so an http-only site no longer shows "Network error × 1" from its
  first seconds. A line under the classes says the probes apart ("N URLs the crawler tried for the site's other
  versions (http or https, www or not) aren't counted here"), opening `source in [siteVersion]` with `outcome
  isNotEmpty`: the classes' total and it add up to Crawled. Live, the line's room is kept from the start, as the
  probes answer in a crawl's first seconds. `e2e/overview.spec.ts` pauses a live crawl of the test site and walks
  every figure (Crawled, Pages, each class, the probes, the URLs not checked) to its count in the grid. Depth and
  response times: the site's pages,
  which is one count everywhere. A page is the engine's (`CrawlSchema.PAGE`): a URL of the site that answered 2xx with HTML the crawl read, so the filter ends with
  `wordCount isNotEmpty` (only parsed HTML has a word count; a URL whose status alone was checked, an iframe's target,
  has none). The cards' "N pages", the live "Pages" and its link, and the library's "pages read" are then the same
  number. Each card says its own count (the facets' `matching`).
- **The answer never waits for `/stats`**, which counts every link (1.1 to 1.4 s on the 100k crawl). It adds why URLs
  got no response to one row when it arrives, and a stopped crawl's queue to its banner. The answer is the analysis,
  the catalog and three facet reads, each card with its own skeleton and its own error.
- **One clock for the live screen's times**: time spent crawling, which stands still while the crawl is paused.
  Elapsed shows the crawl's, the chart's time axis is it, and the pace is read over the last minute of this run's (the
  live store's run clock), so the three agree, a pause moves none of them, and a resumed crawl's time left is right at once. A continued crawl is
  drawn whole (E3c's `/history.runs`): the earlier runs' points, counted back from the file, are placed by the time
  their stretches had spent crawling (`crawlingAt` in `foundAndCrawled.ts`), so the days between runs and the pauses
  take no room, and this run follows on from their total; each later run's start is a dashed rule (`LineChart`'s
  `markers`), and the chart's name says the runs and where each continued ("over 10 min 4 s of crawling, in 2 runs,
  continued at 10 min of it"). The run being watched is the one begun from the history's `liveFrom`: a run before it
  that never ended (its process died) is an earlier one, and the bridge reads the history again when a crawl starts
  or is continued. Elapsed is the progress event's `crawlingMs` (every run's time spent crawling, which a live
  client takes), so it never waits for the history; from an engine without it, the run clock and the earlier runs'
  time. From an engine without `runs`, the chart is of this run, starting where it did and saying "since it was continued". The finished
  crawl's line says how long it spent crawling (`crawlingMs`), "not counting pauses or the time between runs", and
  for a crawl an earlier version made, that it isn't known, never `finishedAt - startedAt`.
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
- **Measured in the shell** (10 Nov 2026, this machine, the release build in its own WebView2 157 at 1280x720 and 150%
  display scale, the same marks taken in the page; 10 runs, done twice; see The shell smoke walk for how the page is
  reached). The 100k crawl from a click in the library: the answer is in the page within 38 ms and painted within 61
  (99 at most over 20 opens), every breakdown painted within 62 (99 at most). The first open after the engine starts
  paints the answer within 39 ms too, but its status codes took 1.1 s once, the engine counting the crawl for the first
  time. A click on a status class shows URLs within 30 ms (45 at most) and its first rows within 66 (104 at most). The
  session's first Ctrl+K has the palette in the page within 37 ms (53 at most; 16 to 31 in Chromium) and painted within
  61 (89 at most); a later one within 29. A live crawl at the Fast preset, three times (dark twice, light; 66,283 URLs
  in 27 to 39 s, the machine being busier than for the Chromium run): no frame over 33 ms, and one over 16.8 ms (of
  20.8 ms) in 31,827. WebView2's frame clock ran at 240 a second in that session (4.2 ms a frame), not Chromium's 60,
  so "painted" comes sooner after "in the page" than it did there and the frames a second say nothing: the count of
  long frames does. Nothing measured is slower in WebView2 by more than the runs differ among themselves.

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
  included (a URL that never answered isn't said to answer for itself). A URL it names with no status code (a link's
  row, a redirect's hop and end, a canonical's, a meta refresh's or an hreflang's target) says what became of it from
  its `outcome` (E3c), in the words the inspector uses for the URL itself (`noStatusWords` in `words.ts`, held to
  `fetchState`'s titles by a test): Blocked by robots.txt, Blocked by the server, Not checked, Timed out, Couldn't be
  reached, Couldn't be read, No response, and Not fetched: never "yet", as a named URL with no outcome may be queued
  or left alone (not followed, past a limit), and it comes without its state to tell which. From an earlier
  engine, which sends only the code, it reads "No status": the crawl may never have asked (robots.txt), so "No
  response" and "Not fetched" would be guesses.
- **The tabs' link counts come with the URL** (E3c's `linkTotals`): a running crawl's tick reads the URL shown once,
  where it read `/urls/inlinks` and `/urls/outlinks` too for their totals (three reads a tick, counted both ways by a
  test); an earlier engine's inspect has no totals, and the lists are read for them. A page's `urlLength`, the check's
  own measure, is drawn on the analysis's `maxUrlLength` as the other values are. Depth 0 is "the start
  URL" only for the URL the crawl started at; a probe of the site's other versions has depth 0 too.
- **An analysis that no longer stands is said where the URL's issues are**, in the summary and in the Issues
  section, by the Issues screen's own banner and its "Analyse again" (`crawl/LastAnalysisBanner.tsx`, which both
  use): never a second wording. The verdict's note says why it's the crawl's own judgement by where the analysis
  stands (out of date, not yet run, or running after the crawl).
- **A hidden check is still the URL's**: wherever the URL's issues are listed, a check the user hid from the Issues
  list comes after the others, marked "Hidden" (`splitHidden` in `sections.ts`), and is left out of the summary's
  and the section's counts and the tab's, as the sidebar and Overview leave hidden checks out of theirs. Its link
  lands on the check with the hidden checks shown. The issues on other URLs that name this one are counted in what
  their check flags ("5 pages", "2 URLs").
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
- **Measured** (the 100k crawl, 1280x720, timed in the page from the key or click to the frame that shows it). In
  headless Chromium, the production build (the inspector's slice): Enter on a row to the summary 46 ms (47 at most), a
  tab's click to its section 13 to 15 ms, the grid's cursor to the next URL's summary 30 ms, a walked link 24 ms (49
  at most). In the shell's WebView2 (10 Nov 2026, the release build, 10 runs, done twice): Enter to the summary 20 ms
  (33 at most), a tab's click to its section 5 ms (9 at most). The shell's are the shorter only because its frame
  clock ran four times as fast in that session (see Overview's measurements): both are a frame or two.

## Links

`screens/LinksScreen.tsx` and `screens/links/`: every link the crawl stored, in the one grid, over the links' own
columns and each end's (`POST /links`, `POST /links/facets`, `GET /api/link-columns`). It is the first screen with the
full grid's pieces, which are any grid's (below). What it settled:

- **Pure first**, as the slices before it: `linkWords.ts` (every column's and value's words, and how a filter reads as
  a sentence), `linkColumns.tsx` (what's shown, what the query asks for to draw it, how each cell reads),
  `presets.ts`, `facets.ts`, `readCost.ts` (which reads pass over every link), `states.ts` (what each state says) and
  `linkPlaces.ts` (where other screens land here). All unit-tested (`links.test.ts`).
- **The route holds the view**: `filter`, `q`, `sort`, `cols`, `inspect` and `end`, checked by `validateLinksSearch`,
  which takes the links' columns (`to.statusClass`, `from.depth`) and one sort key, as the engine sorts links. A
  filter, a search, a preset and a facet's value are each a new place (back retraces them); a sort and the columns
  replace. `end=from` beside `inspect` says the inspector is on the page a link is on, and follows the cursor down the
  sources; a target, the default, has no word. It is set in the same step as the URL (`Inspector.open(urlId, more)`),
  so a reload, back, forward and a link to the view keep following the end they were on.
- **A sort goes with its column.** A sort is seen and changed at its column's header, so hiding the column drops a
  sort by it in the same step (`sortShown`), and a view that arrives sorted by a column it doesn't show has the sort
  dropped from its route, in place, before anything is read by it.
- **The columns are the engine's** (`/api/link-columns`): their types say how a cell is drawn, which sort and which
  need the analysis. Their names are the app's: a link's own, and each end's as the URL grid names that column under
  "Source" or "Target" ("Target status", "Source H1": `grid/urlFilterWords.ts` now names every URL grid column). A
  column a newer engine adds is worded from its id, never shown as one. The default set is the source and target
  URLs, the anchor or alt text, the kind, the position, Follow and the target's status; the rest are in the chooser,
  grouped (the link, its source page, its target). The first page never waits for the engine's list: the default
  columns are asked for at once, and only columns a route names are checked against it first (one this engine
  doesn't have is left out, not sent).
- **Follow is one column** made of the engine's four (`followed`, `noFollow`, `sponsored`, `ugc`), in the words the
  inspector's link grids use ("Followed", "nofollow, sponsored", "Not followed"). Filters are on the four.
- **A target with no status says what happened**, from the crawl's own outcome, which the query asks for with the
  status: "Blocked by robots.txt", "Timed out", "Not checked", and "Not crawled" ("so far" while the crawl runs) only
  for a URL the crawl has no outcome for, in the URL grid's words for the same URL (`urlStatusWords`, one helper for
  both). Never a guess, and never 0. Each end's URL is asked for whether its column shows or not: a row's menu opens
  and copies it.
- **Presets are only filters.** Broken links (`to.statusClass` in 4xx, 5xx), Links to redirects (3xx), Nofollow
  links (`noFollow` is yes), No anchor text (hyperlinks whose `text` is empty) and External links (`to.isInternal`
  is no) put their filters in the route, where they show as chips; a preset reads as chosen exactly while the
  route's filters are its own. So a preset's count is the grid's, and nothing is filtered that the bar doesn't say.
- **Facets are one read** of `/links/facets` under the view's own filters and search, so each facet's counts add up
  to the links in view and are of one moment of a running crawl. Choosing a value puts the engine's own `filter` for
  it in the route, in place of any filter on the same column (the values counted are the ones that filter left, so
  the new one says all the old one did): the grid then holds exactly the value's count. **They are read when a facet
  menu opens, and never before**: a count is a pass over the view's links (on a running crawl about 200 ms a million:
  46 ms at 43k links, 200 ms at 1M, 298 to 386 ms at 1.6M, measured on a crawl of the test site, against 2 to 33 ms
  for page 0), and nothing but an open menu shows it. The presets and the facets' buttons draw without it; the menu
  says "Counting…" until it comes. The facets' key is among the grids' (`['grid', 'linkFacets', …]`), so while a menu
  is open a running crawl's refresh keeps the counts fresh, and with the menus closed it only marks them stale.
- **Each end is a place.** A URL in a row opens the inspector on it; Enter opens the end the cursor's column is of
  (the page the link is on from one of its columns, the target from anywhere else), a double-click likewise. While
  the inspector is open it follows the cursor down the same end, in place, as it follows the URL grid's rows. Which
  end is the route's (`end`), never the screen's own state.
- **From the inspector**: Inlinks and Outlinks have "All N in Links" ("Show it in Links" for one link, never "All
  1"), the same links here: `to.id` (or `from.id`) is the URL, the kind chosen stays, and `selfLink` is no, because
  the inspector's lists leave out a page's links to itself and the links grid doesn't. N is the section's own count,
  and the grid's (the sweep checks it). The
  summary's "N hyperlinks" is not a link here: it is the analysis's count, of the links on the pages it audited, and
  differs from the links stored by one or two for a heavily linked page (24 of 200 URLs sampled on the 100k crawl).
- **No "show these pages in URLs".** The source pages or the targets of a filtered set of links can't be asked of
  the URL grid: it has no filter for "is an end of a link matching these filters", and the target's filters without
  their prefix match URLs no link leads to as well. Nothing is offered that would land on another number; the screen
  copies the set's source or target URLs instead. It needs the engine.
- **Speed, said honestly.** Most views are found from the counts the analysis keeps and cost a page. A search, a
  filter on what the anchor text says or on its source page, and any filter or sort while the crawl runs or without a
  current analysis, read every link once (`readCost.ts` has the engine's rule). A first page still on its way after
  150 ms says what it's doing over the grid's skeleton ("Reading all 5,053,909 links: a search looks at each link's
  anchor text and both its URLs."), announced once; the rest of the screen stays usable, and changing the view gives the
  read up (its request is aborted, as any page nobody waits for is).
- **Another column keeps the grid's place.** The rows are asked for again with it, and the count of the same view
  stands meanwhile, so the grid keeps its scroll, its cursor and its rows' places instead of a skeleton.
- **The engine is the judge of a filter.** The editor checks only what it can know (a number, a range's ends, some
  text of at most 1,000 characters); a pattern goes to the engine as typed, which runs .NET's regular expressions
  (inline options such as `(?m)`, `(?s)`, `(?x)` and `a(?i)b` among them, which a browser can't compile). One the
  engine won't run comes back as `invalid-query` with its sentence keyed by the filter's path (`filters[1].value`):
  the rows' place then names the filter and shows the engine's own sentence, with "Change the filter" (the chip's
  editor opens on the value, the sentence under it until it's changed) and "Remove the filter". The other filters,
  the search, the sort and the columns stay. A query refused for anything else shows the engine's sentence with
  "Reset the view". A filter by an end's check (`to.check`) reads as the check's title, as the URL grid's chip does
  (`grid/useCheckWords.ts`). A view takes the 20 filters a query does: at 20, "Add filter" says why it takes no more,
  and a facet's value that would be the twenty-first says so in a toast instead of being dropped on the way to the
  route.
- **A short window scrolls the screen.** Links and URLs share a frame (`grid/GridScreen.tsx`): the toolbars keep
  their own height, and the grid takes the rest, never less than its header, five rows and its scrollbar (`GridCore`'s
  `minRows`), nor less than all its empty state says. At 200% zoom of 1280x720 (640x360) the toolbars wrap onto four
  rows and the screen is taller than the window: the main region scrolls to the grid, where five rows are whole, and
  "No links match" with its Clear is whole in the window (`e2e/grid-zoom.spec.ts` measures both screens there).
- **States**, each a story under Screens/Links: first load, refreshing, live (links arrive, the count and the
  facets move, nothing jumps, the cursor stays), never analysed, being analysed (it says so, with nothing to press:
  never "hasn't been analysed yet"), continued since its analysis, analysed by another version (each says what still
  works here and what waits, with Analyse it or Analyse again), a filter that needs an analysis there isn't (its
  button worded by the reason, `analyseWords`: "Analyse again" for a crawl analysed before), a filter the engine
  refuses and its editor, a query it refuses, a view with all the filters it takes, no links, none yet, none matching
  (the filters, and one press to clear them), a slow read, a read error, the engine's list of columns unreadable (the
  links are still listed; sorting, Add filter and Columns aren't offered, and a banner says so with Try again), a
  huge anchor text, a link to a URL never fetched, and five million links.
- **Its commands** are the registry's Links group: Ctrl+F searches the links, Ctrl+Shift+C copies the target URLs,
  and the palette has the source URLs, the column the cursor is in, each preset ("Show broken links"), Add a filter,
  Choose the columns and Clear. A copy reads the whole view by cursor, 1,000 rows at a time as far as 10,000
  (`grid/copyAll.ts`), with progress and Cancel in a toast when it takes more than a moment. The URLs copied are the
  distinct ends of the links it read, so on a view of more than 10,000 links each copy is named for what it is ("Copy
  the first 10,000 links' target URLs", `links/copyWords.ts`) and says whose URLs it copied before how many ("Copied
  the first 10,000 links' target URLs. 1 URL, each once. The view has 583,554 links…"). A view's distinct ends need
  the engine.
- **Tested** against the real sidecar (`e2e/links.spec.ts`, on the synthetic crawl's planted links) and swept
  (`e2e/links-sweep.spec.ts`): the header's count under each view, every value of every facet under each view, and
  "All N in Links" both ways, each against where it lands and against the sidecar's own count for that view. It runs
  on the tests' crawl with the rest, and on a real crawl's file with `SI_E2E_CRAWL`.

- **Measured** (10 Nov 2026, this machine, the production build in headless Chromium at 1280x720 against the real
  sidecar, on the 100k crawl's 5,053,909 links; each from the input to the frame after, five times). Links from the
  sidebar shows within 52 ms and its first rows within 62; a preset's button and chip within 33 ms and its count and
  rows within 57; a facet's menu within 31 ms and a value's rows within 50; a sort's header within 31 ms and its first
  page within 49; Down moves the cursor within 33 ms, Enter opens the inspector within 33, and Ctrl+End draws the last
  of five million rows within 59; the chooser opens within 31 ms and another column's rows fill within 47. The
  sidecar's reads (the first call for a view, then the median of seven more): every view the analysis's counts answer
  is inside the 300 ms budget (crawl order 3 to 5 ms at any depth; the presets 6 to 114 ms, then 3 to 24; the facets
  126 ms, then 4; a sort by the target 24 to 51 ms). **Over budget, and said on screen**: a search not made before
  (0.94 to 1.87 s; 1.24 to 1.32 s to its rows on screen, the grid saying what it's doing from 175 to 181 ms); a filter
  on what the anchor text says (0.53 s for a common word, 0.83 s for a pattern) or on its source page's depth (1.46 s);
  half way down the links to one heavily linked page with its own links to itself left out (336 ms into 202,212, then
  42); and, on a crawl with no link summary (one an earlier engine analysed), the first read of every filtered view
  and of the facets (0.2 to 1.2 s), the unfiltered grid staying at 3 to 8 ms. Each is once per view: its pages after
  cost 1 to 9 ms. The garden crawl's slowest read of all is 20 ms.

### The full grid's pieces

In `src/grid`, for any grid; Links is the first to use them all, and the URL grid takes its chips from them.

- **`FilterBar` and `FilterChips`**: a view's filters as chips that read as sentences, each removable (focus goes
  to the next chip, else Add filter), its quick filters before them, "Add filter" and "Clear filters". Given what a
  filter can be on (`FilterColumn`s in groups), a chip the editor can hold opens it; one it can't (a URL named by
  its id, a check's group) is read and removed. The view can open a chip's editor too (`editing`,
  `onEditingChange`), hand it the filter the engine refused (`refused`), and say how many filters it takes
  (`maxFilters`: at the limit "Add filter" says why, in place of the editor). An open editor is of the filter it was
  opened on, not of its place: when the view's filters change under it (back, forward, a link) it closes, rather than
  show whichever filter is in that place now. In an empty state (`FilteredEmptyState`) a chip longer than the state
  is cut short in it, with its remove button, never run out under the grid's edge.
- **`FilterEditor`** (`filterEditing.ts` has its rules): what it's on, how it compares, and with what, by the
  column's type: a number or a range, text or a pattern, some of an enum's values, yes or no, empty or not. Nothing
  is applied until Apply or Enter; what the editor can tell is wrong is said under the field it's wrong in, and what
  only the engine can judge (a pattern) is applied: `refusal` is the engine's sentence for a filter it wouldn't run,
  shown under the value until the filter is changed. Under a text value it says how the engine matches it
  (`matchHint`): A to Z in either case, every other letter only as typed, which is SQLite's `LIKE` (on a crawl with
  accented anchor texts "contains menu" found "MENU" and "contains été" didn't find "ÉTÉ"); a pattern tells capitals
  from small letters unless it starts `(?i)`, which folds every letter. A column the analysis fills is listed, and
  can't be chosen without one. It sits in a `Popover` that floats from the chip (`anchor`: something else opens it,
  and focus goes back to the chip when it closes) or from Add filter; a new filter starts in its first field, a
  filter being changed in its value.
- **`RefusedFilter`** and `refusedFilter` (`filterEditing.ts`): a filter the engine wouldn't run, found from the
  `invalid-query` answer's field paths, as a state in the rows' place: the filter as its chip reads, the engine's
  sentence, "Change the filter" and "Remove the filter". `ErrorState` takes a plain `description` for it.
- **`GridScreen` and `GridScreenBody`**, with `GridCore`'s `minRows`: the frame of a screen that is one grid under
  its toolbars. The toolbars keep their height; the grid takes the rest and never less than `minRows` rows (and all
  its empty state says); a window too short for both scrolls the main region. Any screen laid out as toolbars over a
  grid takes them instead of its own `flex h-full` column.
- **`ColumnChooser`**: every column the view can show, grouped, with a search; a tick applies at once and a column
  ticked goes last. The last column shown can't be unticked.
- **`FacetMenu`**: a facet as a menu of values with their counts (`MenuItem`'s `detail`), read with each value's
  name; counting, failed (with Try again) and nothing to count each say so. `onOpenChange` tells the view when to
  count: only while a menu is open.
- **`copyAll`**: what a whole view holds, read by cursor. `truncated` words a copy of a part of the view whose count
  isn't a count of rows.
- **`NeedsAnalysis`, `summaryWords.ts`, `useUrlWords`, `useCheckWords`**: the URL grid's words for a column that
  waits for an analysis (and for one being analysed now), its state for a filter on one, with the button worded by
  the reason (`analyseWords`), and a URL's or a check's words by its id, moved here from the URL grid's screen.

## The URL grid

`screens/UrlsScreen.tsx` and `screens/urls/`: every URL the crawl found, in the one grid, over the engine's URL
columns (`POST /urls`, `POST /urls/facets`, `GET /api/url-columns`), with the full grid's pieces as Links has them.
What it settled:

- **Pure first**: `urlColumns.tsx` (the presets, what's shown, what the query asks for, each cell, widths, the
  chooser's groups and the filter bar's columns), `urlFacets.ts`, `urlGridParts.ts` (the nine fixed columns one
  sitemap's URLs still show, exported from `UrlsScreen` under their old names), `urlPlaces.ts`, `useUrlFilterWords.ts`;
  `grid/savedViews.ts` and `grid/searchCommand.ts` are any grid's. Unit-tested in `urls/urls.test.ts` and
  `grid/savedViews.test.ts`.
- **The route holds the view**: `filter`, `q`, `sort` (up to three keys), `cols`, `inspect`. A filter, a search, a
  facet's value and a saved view are each a new place; a sort and the columns replace. `view` stays unused: a preset
  and a saved view are recognised from the route itself, so a link, back and forward always say which.
- **Column presets** (Overview, the default; Indexability; Content; Performance; Links) are only columns: a preset
  reads as chosen exactly while the route's columns are its own, in its order, and the default set isn't written to
  the address. Each leads with the URL; the URL column takes the room the others leave and never less than 240 px, so
  a preset wider than the window scrolls inside the grid, never the page. The chooser lists every column the engine
  has (a check's own and the id left out) in six groups, with "Show full URLs" under them.
- **The host named once**: URLs on the crawl's own origin show their path, and the URL column's header says "on
  example.com" (`headerDetail`). "Show full URLs" (a preference, `settings.showFullUrls`, and a palette command) shows
  them whole. A URL is always copied whole. An enum's values are the engine's words (`valueTitles` of
  `/api/url-columns`) in the cell, the facet, the filter editor and the chip alike, this version's only where the
  engine gives none, so the grid says what the export says.
- **A URL with no status says what happened**, from its outcome (`urlStatusWords`): "Blocked by robots.txt", "Not
  checked", and "Not crawled" (or "so far" while the crawl runs) only for a URL with neither a code nor an outcome.
- **A sort goes with its column**, as on Links (`sortShown`): hiding a column drops its key alone, the others kept in
  their order, and a view that arrives sorted by a hidden column keeps its shown keys. A header that can't sort says
  why: text "would read every page", a column read from each response "would read every one", with "filter by it
  instead".
- **Facets** (status, status code, indexability, type, depth, response time, sitemaps, issues) are read only while a
  menu is open (`['grid', 'urlFacets', …]`, among the grids' keys, so an open menu stays fresh on a running crawl), and
  a value's filter goes beside the view's (`withFacetValue`), taking the place only of a list its value is one of: the
  counts are of the view as filtered, so a range the view has (Response time over 300 ms) stays, and the grid lands
  on the count the menu showed. The two the analysis counts say why they can't be, in the menu, on a
  crawl without one. A URL counts once in every facet but Issues, where it counts once for each severity it has.
- **Saved views** are preferences (`settings.savedViews`, checked as a route is when read back): 24 at most, each at
  most 4 KB and all together at most 32 KB of UTF-8, so the preferences file stays inside the 64 KB the shell keeps
  whatever script their text is in. What's too long says what takes the room (its filters, its search, its columns),
  rather than being cut. A view is a name and the route's filters, search, sort and columns. The menu's button names
  the view the grid is on; save (a dialog, the name checked under its field), open (the menu, and a palette command
  per view), rename and delete with Undo; an Undo that would cost another view (one of its name saved since, or no
  room) says why instead. A saved view opens on any crawl.
- **Preferences are never lost in silence**: a write the shell refuses is said once in a toast, until a write works
  again, and fields a later version stored are written back as they were. The shell writes the file compact and
  refuses a write whose file would be over the size it reads (`src-tauri/src/settings.rs`).
- **Copies**: the URLs of the whole view (Ctrl+Shift+C) and the column the cursor is in, read by cursor as far as
  10,000 rows, named "the first 10,000" on a bigger view.
- **The row menu**: inspect, open in the browser, copy the URL (or the selection's URLs, each once), copy the row (or
  rows) as Ctrl+C does, and "Show its inlinks in Links" (the inspector's "All N in Links" filters). The same piece on
  Links (each end: the end the menu was opened in, and Inspect the source page, Inspect the target), Issues' pages
  ("Show the page in URLs": the URL grid filtered to its URL, `url eq`, or by its id for a URL longer than a filter's
  1,000 characters, with the inspector on it) and groups (the group's URLs copied, its row, "Show all N pages in
  URLs"), one sitemap's URLs ("Show the URL in URLs") and an origin's blocked URLs ("List what line N
  blocked"). Its accessible name says what it's of ("Menu for /about/", "Menu for 12 URLs").
- **The cursor follows its row** (`rowKey`) on the URL grid, Links, Issues' flat rows (a URL's issue, by detail and
  related URL) and groups, and the inspector's link lists (a link by its other end, kind, position, text, fragment
  and flags: two links alike in all of these are the same to the cursor). Each has a test seen failing without it.
- **Ctrl+F** is one command shape on URLs, Links, one sitemap and Issues (`searchCommand`): the screen's field
  focused, its text selected, "filter this grid" among its palette words.
- **Export** is at the end of the toolbar (`data-slot="export"`), with Ctrl+E: see
  [Hand over](#hand-over-search-favicons-and-the-analysiss-limits).
- **States**, each a story under Screens/URLs: the garden crawl and each preset, sorted by two keys, every kind of URL,
  full URLs, filtered, on a saved view, first load, refreshing, live (URLs sort in, the cursor stays), never analysed,
  continued since its analysis, being analysed, a filter that needs an analysis, a filter the engine refuses, a query
  it refuses, all the filters a view takes, no URLs matching, no URLs yet, a read error, the columns' list
  unreadable, and a hundred thousand URLs. The menu at a point, the URL chooser, the saved views' menu and the name
  dialog are under Design system.
- **Tested** against the real sidecar (`e2e/urls.spec.ts`) and swept (`e2e/urls-sweep.spec.ts`: the header's count
  under each preset and view, and every value of every facet under each view, against where it lands and the
  sidecar's own count): 66 figures on the tests' crawl, 121 on garden (6,286 URLs), 152 on the 100k crawl (112,577).
- **Measured** (12 Nov 2026, this machine, the production build in headless Chromium at 1280x720 against the real
  sidecar, on the 100k crawl analysed by this engine; `e2e/urls-measure.spec.ts`, from the input to the frame after).
  At the default density (32 px rows) every preset shows 15 rows; Overview 7 columns whole of its 11, Indexability 6
  of 10, Content 5 of 8, Performance 8 of 8, Links 8 of 11 (Screaming Frog's figure: about 20 rows by 7). A preset's
  columns and rows are on screen 19 to 29 ms after its press; the sidecar's page 0 of each takes 2 to 30 ms, and of
  ten filtered views 2 to 52 ms (a title search's first read the slowest), each view open from its address, the app
  started, in 76 to 115 ms. A header's sort arrow shows in 20 to 32 ms with rows at most 8 ms later; a facet's value's
  chip in 18 to 29 ms, rows at most 25 ms later; a column ticked in the chooser in 25 to 37 ms. Ctrl+End draws the
  last of 112,577 rows in 29 to 30 ms, and the heap is the same before and after twenty more jumps between the ends
  (97 MB, then 116 MB, in two runs). Two runs, before and after the merge of ui/step-3 at be294da.

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
  in place, with no skeleton and no progress line. One read is left out of it (`SELF_REFRESHED_GRIDS`): an
  origin's file, which is read whole (a robots.txt of 500 KB is 1.4 MB as lines). Its view reads it again when the
  list beside it counts another number of blocked URLs than the file does, and every 10 s for its answer; an origin
  the crawl hasn't come to yet is looked for every 2 s. Measured on a crawl of 25 s: 4 reads of the file, where the
  once-a-second refresh made 25.
- **Nothing is read while the window is hidden**, as on URLs and Links: the bridge's refresh of a crawl's grids
  waits until the window is shown (see The live stream), and the file's own look is an interval that doesn't run in
  the background (`refetchIntervalInBackground: false`, said outright). Shown again, the next look is made.
- **All four views are in the grid screens' frame** (`GridScreen` and `GridCore`'s `minRows`: see Links). What's
  over a grid keeps its height, a grid never has less than five rows' room, and a window too short for both scrolls
  the screen (200% zoom of 1280x720 is 640x360). One sitemap's URLs fill what the screen leaves, as URLs and Links
  do. The two lists are as tall as their rows: a list of five rows or fewer is never squeezed, and a longer one
  gives way down to five. An origin's file and its blocked URLs are the frame's body, side by side: the blocked URLs
  keep five rows there, and where the two stack. The sweep measures all four at 640x360.
- **A link to a route with an optional part must name the part**, as `undefined` for "none". Left out, the router
  keeps the part the page is on: on one sitemap's page the sidebar's Sitemaps, Ctrl+5 and "All sitemaps" all led to
  the sitemap itself. The sidebar's and the palette's Sitemaps and robots.txt name it now.
- **A list is come back to where it was left** (`screens/listPlace.ts`): the row a sitemap or an origin was opened
  from is remembered for the session and handed to the grid as `startRow`, so Back, "All sitemaps" and the sidebar
  land on that row, not at the top of a thousand. The origins are paged and a running crawl sorts new ones in among
  them, so their place is remembered by the origin too (`startKey`). The list of sitemaps is as tall as its rows,
  the ones under a closed index not counted, and never taller than the room there is.
- **The cursor stays on its row while a crawl runs**: all four grids give `GridCore` a `rowKey` (the origin, the
  sitemap's id, the URL's id). Without one, an origin the crawl asked that sorted ahead of the cursor's, or a
  sitemap it took up under an index above it, left the cursor on another row, and Enter opened the wrong one.
- **A count of URLs in a sentence is a link too** (`screens/countSentence.ts`, drawn by `SentenceWithCounts.tsx`):
  a sentence is built as words and counts, each count with the URL grid's filters for what it counts, so the
  words and every landing are unit-tested. The robots.txt answer's "kept it from 1,204 URLs" opens the URLs whose
  `outcome` is `robotsDisallowed`, which is what the origins' counts add up to.
- **A place the crawl doesn't have is said, with the list a press away** (`missingPlaceWords` in
  `api/errors.ts`): a sitemap's id or an origin the crawl has none of (`sitemap-not-found`, `origin-not-found`,
  and a path that can't be an id). Once the crawl has ended the link is stale ("It may be from before the site was
  crawled again"). While it hasn't, the crawl may yet find the sitemap or come to a URL on the origin: the words
  are "not so far", and the page fills in when it does. There, only the way back to the list can run: the other
  commands of the place are disabled, so the palette greys them and their keys do nothing.
- **A failure is said by its kind** (`lib/failure.ts`, shared with Overview's alerts): the engine records how a
  request failed in its own text ("TLS handshake failed: Cannot determine the frame size or a corrupted frame was
  received."). A sentence takes the kind's plain words (the DNS name wasn't found, the connection was refused, it
  timed out, a secure connection couldn't be made, the request failed; a status is said as it is), and the engine's
  text is the tooltip and the accessible description (`title`), never the sentence. A sitemap whose XML is broken
  says at which line.

### Sitemaps

- **The list is a treegrid** (`GridCore` with `tree`): an index, the sitemaps it lists under it, to any depth the
  crawler reads. One Tab stop. Two things were added to the grid for it, both opt-in: `onActivateCell` (Enter or a
  double-click, with the column the cursor was on: each count cell leads somewhere else; called in place of
  `onActivate`, which is told the column too since Links, so the two could be made one) and `GridTree.activates`
  (a group's row is a thing of its own, which Enter opens as it opens any row: an index has URLs of its own, those
  of every sitemap under it; Right, Left and the chevron open and close it).
- **Every count is a link to exactly its URLs**: that sitemap's page under the count's filter (`COUNT_FILTERS`).
  The links are in the cells, out of the tab order, and Enter on a cell does what a click on its link does. A count
  of 0 is a plain 0 (nothing to open). A sitemap whose contents the crawl doesn't know (left unread, not read yet,
  or failed before an entry was read) shows dashes, never 0.
- **"Crawled" means the crawl requested the URL**, which is what a person takes it to mean here. The engine's
  `fetched` is wider: every URL the crawl is done with, the ones robots.txt refused included, which were never
  requested. So the list's counts are Listed, Crawled, Blocked by robots.txt, Not crawled and Non-indexable, and
  **Listed = Crawled + Blocked by robots.txt + Not crawled**. Crawled is `fetched` less the URLs robots.txt
  refused (`fetched` is true and `outcome` is not `robotsDisallowed`): a URL that was requested and didn't
  answer is crawled, and its Outcome says what happened. Blocked by robots.txt is `outcome` `robotsDisallowed`.
  Not crawled is what's listed and not `fetched`.
- **The list reads the two counts the E3c review added to `GET /sitemaps`**: the listed URLs robots.txt refused,
  and those that redirect or are broken (`urls.robotsDisallowed`, `urls.redirectingOrBroken`, read in
  `sitemapLinks.ts` only, typed in `api/grids.ts`, sent by the mock). The engine sends both since E3c, so Crawled
  and Blocked by robots.txt are numbers on every sitemap whose counts are known, Redirecting or broken carries its
  count, and one sitemap's page reads nothing more than its grid. The fields stay optional in the type: from a
  sidecar without them the list would show a dash for Crawled and for Blocked by robots.txt, with a tooltip that
  says why, never `fetched` under the name "Crawled", and one sitemap's page would count the two itself, each by a
  read of its grid under that filter for the count alone (`sitemapCountQuery`).
- **An index's counts are its sitemaps'**, so they're known only once the crawl has every sitemap under it and has
  read each (`subtreeOf`, `wholeSubtree`, `countsKnown` in `sitemapTree.ts`). The crawl takes an index's
  sitemaps up one at a time, so while it's still reading sitemaps (`discovery` isn't `done`), or when it stopped
  first, it may not have them all; one left unread at its limits stands for more like it; and one it couldn't read
  lists an unknown number. Until then the index's own row shows dashes, never 0, and its status says how far the
  crawl got ("Read, 11 of its 12 sitemaps", "Read, has 1 of its sitemaps", "Read, 4 sitemaps so far"); each sitemap
  under it keeps its own counts, and the index's page still lists the URLs of those that were read. How many
  sitemaps an index's file lists isn't given by the API (only how many the crawl has), so its badge counts them
  only once the crawl has them all.
- **Five counts fit a 1280 px window**: "Blocked by robots.txt" takes two lines over its count, the other columns
  are as wide as their names and a count in the millions need, and an index's badge leaves its count out where the
  address would have no room (below 260 px).
- **In a frame too narrow for every column the counts come straight after the address**, with Status and Found via
  after them. The list measures its own frame (never the window): 200% zoom of 1280x720 leaves it 534 px, where
  Listed began at the frame's last 44 px and the other four counts were a sideways scroll away. Listed, Crawled and
  Blocked by robots.txt are whole in the window there now; the grid still scrolls sideways for the rest. The origins
  do the same with Blocked URLs, which then comes before what the origin's answer meant. A frame that isn't laid
  out yet (no width) keeps the order every column fits in.
- **What isn't there is said.** No sitemap at all: a sentence by how far the crawl got (`discovery`), and where the
  crawl looks. None read: the same, under the list. Nothing declared one and the default location has none (a 404
  or a 410 at `/sitemap.xml`): "Nothing declared a sitemap, and there's none at /sitemap.xml.", and its row "None
  there", never a sitemap that was found and couldn't be read. How a sitemap was found, when the crawl can't say
  (`foundVia` null): "Not known", with why. One left unread at the crawl's limits says which limit, once (the
  engine's "Not read: " is dropped). The site's pages no sitemap lists: one read of the URL grid's `inSitemap`
  facet over the site's pages (`ANSWERED_SITE_PAGES`, as Overview counts pages). Both of its counts are links: the
  pages no sitemap lists, and the site's pages they're of. It needs the analysis: without one the line says so, and
  the counts that need none still show. And it needs the analysis to have made the comparison: when it skipped the
  check (the crawl didn't finish, a sitemap wasn't read), the engine answers null for a page, never false, and the
  line says the analysis didn't count them, with the analysis's own reason for skipping `not-in-sitemap`. A page
  the engine can't place is never told it's in a sitemap.
- **Without a URL summary** (never analysed, out of date, an earlier engine's, a crawl running) the list says which
  counts are the crawl's own judgement (indexable, non-indexable) and offers the analysis where it can run. The note
  isn't a live region, and while a crawl runs it's there with the list from the start: it used to mount when the
  first sitemap was read, which pushed the tree down 52 px and read the banner out mid-crawl.
- **One sitemap is the URL grid narrowed to it** (`/sitemaps/urls`): the URL grid's columns and filter chips
  (exported from `UrlsScreen.tsx` as they stand: the full grid's slice gives them files of their own), with an
  Outcome column that goes by each URL's `outcome`, never `fetched` alone, in the inspector's words (a URL that
  answered says "Answered": "Crawled" is no outcome); quick search; sort from the headers, by the columns the engine
  says it sorts (`GET /api/url-columns`); the inspector on Enter. Its view is in the route (`filter`, `sort`,
  `q`, `inspect`), as the URL grid's. What it says of a column that waits for an analysis is the URL grid's too
  (`grid/summaryWords.ts`, `grid/NeedsAnalysis.tsx`): the button is worded by why the analysis can't be read
  ("Analyse again" for a crawl that was analysed before), and while the crawl is being analysed the columns "fill
  in when the analysis that's running is done", with nothing to press.
- **Its search field is the same field whatever is searched for** (`grid/useSearchText.ts`, which the URL grid uses
  too): controlled by its own text, which follows the route's `q` when that changes from elsewhere. Keyed on `q`,
  as it was, Enter made a new field and the one with focus was gone.
- **Quick filters** are a segmented control: every count of the list's row, so each lands on a filter with a name
  and its count (All, Crawled, Blocked by robots.txt, Not crawled, Non-indexable), and Redirecting or broken (by
  `outcome`: a redirect, an error or no answer; not what robots.txt or a firewall kept the crawl from). Filters
  that aren't one of them are a segment of their own while they last, "Other filters": a radio group chooses the
  segment focus lands on, so with none chosen, Tab onto the control replaced the filters. The search shares the
  filters' line, taking what's left of it down to 144 px. Where one line can't hold the filters they take a second
  (`wrap` on `SegmentedControl`): at 640 px the six of them ran out of the window, and the screen scrolled sideways.
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
- **Whether the crawl obeyed robots.txt goes into every sentence that says what a file meant to it** (`Obeyed`:
  true, false, or null when it couldn't be read): a crawl told to ignore robots.txt is never told it obeyed a file
  ("Read: rules ignored"), a line ("it would have obeyed lines 5 to 9") or a rule. One origin's page holds those
  words until it knows (the list of origins hands it what it found, so there's usually nothing to wait for), and
  says no more than holds either way when it can't find out.
- **Only the last answer is kept**, so an answer that refuses nothing (missing, empty, no answer, ignored) on an
  origin with URLs stored as refused is said as the last answer only ("No answer when last asked"), with where
  those URLs come from: refused earlier in the crawl, or by an earlier version of the engine, which took no answer
  for a refusal. That was the garden crawl's contradiction ("crawled anyway" beside 1 blocked URL).
- **An origin in the route is read as a URL** and its own origin is what the engine is asked for: the engine keeps
  one spelling (scheme and host in lower case, the host as punycode, no default port), so capitals, a slash after
  it or an international name as it's written all open the origin the crawl has.
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
  over the list, with the route's `reason`. What narrows the list shows as a chip that removes it: "Line is 2:
  Disallow: /private/", which holds of any line a link can name. With nothing listed the words go by the line: the
  file has no such line, the line isn't a rule, or it's a rule that blocked no URLs. The note over the list has two
  lines' room whatever it holds, so the list stays put when the note wraps or first comes.
- **A line the crawler made nothing of says why by what the line is for**: an empty `Sitemap:` names no sitemap;
  only a `Crawl-delay` that isn't a number, or is below 0, is told so.
- **A URL blocked without a line says which case it is**: robots.txt couldn't be read, or nothing kept refuses it
  ("Only the last answer is kept, so what refused it is gone"), and no more than that.
- **Copying**: the file as it is, and the blocked URLs the list is narrowed to (Ctrl+Shift+C), read 1,000 at a time
  by the Issues screen's copy, with its progress toast.
- **The layout**: the file and the list side by side, stacked below the `split` width (a narrow window, or the
  inspector open beside them). Stacked, the file has a height of its own (360 px) and the blocked URLs at least
  that: never less than their header, their note and five rows of the list.
- **Its commands** are the registry's robots.txt group: search the file (Ctrl+F from anywhere on an origin's page:
  the pane's search field is asked to take focus, as the inspector's Source is by its command; with focus in the
  file the pane takes the key itself), show every origin, copy the file, copy the blocked URLs, open the robots.txt
  in the browser, list every blocked URL.

### From elsewhere

- The inspector's "Blocked by robots.txt" leads to the file on that URL's own origin, and "In a sitemap" to
  Sitemaps. `/urls/inspect` says neither which line nor which sitemaps, so neither link can be more exact.
- The mock answers these endpoints from fixtures of their own (`api/mock/sitemapsRobots.ts`, saved by
  `design/data/extract/sitemaps-robots.mjs`). It counts each sitemap's URLs over the rows its own grid has.

### What the engine doesn't give

- `POST /robots` takes only `offset` and `limit`: no search, no filter to the origins that blocked something, and
  no count of them. The order puts them first after the site's own, and the screen says so.
- Done (E6 review, item 7): the engine names a status in the crawl's own words as an issue's detail does ("404 Not
  Found", a sitemap's `error`, a robots.txt's reason; "HTTP 520" for one with no name), and both screens name a
  status they word from its code the same way (`statusWords`); a crawl an earlier engine stored still says "HTTP
  404", which `failureOf` names too, so the two screens and Issues say a status alike for every crawl.
- It doesn't say how many sitemaps an index's file lists, only how many of them the crawl has: "the crawl has 1 of
  its sitemaps" can't say of how many.
- An origin can read `unanswered` and still have blocked URLs (a crawl continued after a run that read a file
  which refused them, or a file from the first engine, when no answer meant a refusal): the contract says so now,
  and the UI says the URLs were refused earlier.
- `/robots/blocked` can't be asked for one URL, and `/urls/inspect` doesn't carry the line: arriving from the
  inspector, the file can't show that URL's line until the URL is found in the list.

### Measured

10 Nov 2026, this machine, the production build in headless Chromium at 1280x720, against the real sidecar: the
100k crawl (112,577 URLs, 13 sitemaps listing 101,081 URLs) and the garden crawl (17 origins). Each is the median
of 7, from the key or pointer event to the frame that shows it, with the worst in brackets (the first of each, the
sidecar not having counted the crawl yet).

- **The first page of a grid (300 ms)**: the list of sitemaps 32 ms (102); one sitemap of 101,081 URLs 32 ms (48),
  its 2,348 non-indexable ones 32 ms (36); the origins 31 ms (79); an origin's file with its blocked URLs 40 ms
  (103); the garden crawl's origins 33 ms (34) and a real 403-line file 30 ms (31).
- **A sort or a filter (300 ms)**: a quick filter to its rows and count 32 ms (33); a sort from the header 30 ms
  (31); a rule's link to its URLs listed 31 ms (31).
- **A click or key to a visible response (100 ms)**: a quick filter chosen 13 ms (14); a rule's line marked 14 ms
  (15); Left closing an index 6 ms (16); Enter opening a sitemap 10 ms (15); Down in the blocked URLs 7 ms (14);
  Ctrl+F to the file's search 6 ms (17).

Watched while a real crawl of the synthetic site ran (the same build and sidecar, sampled twice a second beside
the engine's own answers): a sitemap index's counts filled in place, the origin's blocked count and the file's rule
and list went from 0 to 10 within about a second of the engine, the Live mark showed throughout, the list of
sitemaps kept its height, and the loading frame never came back.

Watched again on 11 Nov 2026, after the review's fixes (the dev build and the real sidecar, sampled four times a
second): through a crawl of the synthetic site the list of sitemaps kept its top edge (208 px from the first frame
with rows; before, it moved from 156 to 208 px when the note on the crawl's own judgement mounted) and no banner
was a live region; the blocked URLs kept theirs (243 px) while the list went from none to ten. On a crawl of a
small local site with a robots.txt of 500 KB (10,468 lines, 1.36 MB as the engine answers it) the file was read 4
times in 25 s, where the once-a-second refresh read it 25 times, and its rule and the list still moved together
(5 URLs, then 10). That site's sitemap lists ten URLs its robots.txt disallows: the engine's `fetched` is 312 of
312, and the sitemap's page says Crawled 302, Blocked by robots.txt 10, Not crawled 0. While the engine doesn't
count the refused URLs for the list, one sitemap's page makes up to 4 reads of its grid a second during a crawl
(the rows, and a count each for crawled and blocked) where it made 2.

Checked on 12 Nov 2026, with `ui/step-3` merged in (the engine sends the two counts), against the real sidecar:

- **Crawled and Blocked by robots.txt, each number against where it lands** (the sweep, on copies analysed by this
  engine). The 100k crawl: 13 sitemaps; each of the ten of 10,000 URLs says Listed 10,000, Crawled 10,000, Blocked
  0, Not crawled 0; `extra.xml` 1,081, 1,081, 0, 0, with 2 redirecting or broken; the index and the one that
  answered 404 show dashes (107 links followed to their rows, 22 counts asked of the engine, 10 dashes). The garden
  crawl: its one sitemap is the default location's 404, so five dashes. The test site's crawl, in every e2e run:
  the sitemap that lists `/private/listed`, which robots.txt disallows, says Blocked 1 and Crawled one fewer than
  the engine's `fetched`, and the 1 opens that URL alone.
- **At 640x360** (the dev build): the list of 13 sitemaps is 214 px, five rows under its two-line header, with
  Listed, Crawled and Blocked by robots.txt at 261 to 537 px of a 640 px window (Listed began at 571 px, and the
  list showed three rows); one sitemap's grid is 202 px, five rows, and its screen no longer scrolls sideways (it
  was 814 px wide in a region of 584); the garden crawl's 17 origins keep five rows, with Blocked URLs at 341 to
  461 px (it began at 573); an origin's blocked URLs are 264 px under its file. At 1280x720 nothing moved: every
  column fits, in the order it had. At 1280x420 the blocked URLs keep 202 px and the screen scrolls.

### The sweep

`e2e/sitemaps-robots.sweep.spec.ts` reads every count off both screens and follows it: each sitemap's five counts
(a 0 has no link, so the engine is asked what its link would open; what each must be is restated there from the
API's numbers, not taken from the app: a dash only where an index isn't read whole or a sitemap wasn't read; the
engine's two counts must be there, and on the test site's own crawl one sitemap must say 1 blocked), that crawled,
blocked and not crawled add up to listed, each quick filter, Redirecting or broken included, that the page's
counts are the list's own and that the three add up to All on one sitemap's page, the pages no sitemap lists (or
that the analysis didn't count them)
and the site's pages they're of (the engine is asked for both first, so the sweep waits for the sentence it knows
is coming and never passes for want of one), every URL robots.txt kept the crawl from,
each origin's blocked URLs, each line's, and the two cases no line accounts for, which must add up to the origin's.
A third test measures the four views at 640x360: each list keeps five rows (all of them, with five or fewer), the
counts each list is for are whole in the window, one sitemap's grid and an origin's blocked URLs have room for five
rows, a sitemap's six filters are each whole in the window, and no screen scrolls sideways (`e2e/gridView.ts`
measures a grid in the window, for this spec and `grid-zoom.spec.ts`).
It runs on the tests' own crawl of the synthetic site, and on a real one with `SI_E2E_CRAWL`.

## Modes, Settings and New crawl (round 3)

Simple and Advanced ([Simple mode](v2-ui-simple.md)) are one app with two views. This round built the frame: the
mode, its switch, the routes' table round 4 plugs Simple's screens into, the shell in Simple, the top bar's order of
giving way, Settings, New crawl in both modes and the crawl's own settings.

### The mode

- **One preference**, `settings.mode` (`simple` | `advanced`), kept with the others (`settings.json` in the shell,
  `localStorage` in a browser). A first start, with nothing stored, is Simple; preferences stored before the mode
  existed (or with a mode this app doesn't know) read as Advanced: that's someone who has used the app as it was.
- **Read** through `useMode()` (or `getMode()` outside React), in `mode/mode.ts`. **Changed** through
  `switchMode(router, mode)` (`mode/switchMode.ts`), which the switch, Ctrl+Shift+A (`app.mode`), the palette's
  "Switch to …", Settings > General > Mode, "Show them in Advanced" and "Back to Simple" all call. A change the user
  made is announced once ("Advanced is on").
- **Switching keeps the route.** The same route renders its other view, its search params kept (Simple ignores
  Issues' `group`, `severity`, `inspect`), so switching back restores them. From an Advanced-only screen, switching to
  Simple navigates (a push, so Back returns) to the nearest screen Simple has: the crawl's Summary
  (`/crawls/:crawl/overview`), or Settings > General. `app/modeRoutes.ts` says which screens are Advanced only
  (`advancedOnlyScreen`) and where a switch lands (`simpleLanding`).
- **Arriving at an Advanced-only screen in Simple** (a link, Back or Forward, a reopened session) turns Advanced on,
  with the toast "Advanced is on" and **Back to Simple** (`mode/useModeGuard.ts`). The guard looks at where each
  navigation lands (`onResolved`) and at the place the app opens on, never at a change of mode, so switching to Simple
  on such a screen (which then leaves it) isn't undone. It turns Advanced on only for a screen that's there: a crawl
  the library has, and a section of Settings with a file (the route sends any other to General). New crawl does the
  same, with the same toast, for a setting Simple doesn't show (`focus=userAgent` in Simple). A link in Simple to an
  Advanced-only screen says so in its words ("See it in Advanced").
- **`/crawls/:crawl`** opens the crawl's last view in the current mode: in Simple, a last view Simple doesn't have
  opens the Summary.
- **No inspector in Simple**: the shell leaves the inspector's slot out while a Simple screen is shown
  (`useSimpleScreenShown`). A route still showing its Advanced screen in Simple works as it does in Advanced.
- **The palette and the `?` sheet** list what's registered, and `useAppCommands` registers each mode's own: in Simple
  its sections (Summary, What to fix, the Report once it exists) on Ctrl+1 to Ctrl+3, its words ("Check a website",
  "Pause the check", "Work out the results again", "Open a site check file…"), and no Advanced-only command (Go to
  URLs, Go to URL…, Density) but "Switch to Advanced". Both list their groups in the mode's words ("Site check", "Your
  sites") and, in Simple, none of the grids' or the inspector's keys (`keyboard/modeWords.ts`). A check is found by
  either of its titles, and shows the mode's own.
- **Tests, stories and the e2e start in Advanced**, which every screen built before Simple was written for:
  `test/setup.ts` puts `mode: 'advanced'` in place before each test (`replaceSettings`, which doesn't save),
  `.storybook/preview.tsx` before each story, and the e2e's browsers start with it stored (`e2e/storage.ts`,
  `ADVANCED_STORAGE`). A test of Simple says so: `replaceSettings({ mode: 'simple' })`, `<ScreenApp mode="simple">`,
  or `test.use({ storageState: { cookies: [], origins: [] } })` for a first start.

### The routes' table: adding a Simple screen

`app/simpleScreens.ts` is the one table from a route to its Simple screen. A row that is null shows the route's
Advanced screen in either mode; all four are null today. **To give a route its Simple screen, set its row and change
nothing else**: the router (`app/modeScreen.tsx`), the switch, the sidebar, the keys and the inspector's slot all read
the table. Each row is one line with a comment line between it and the next, so rows set on separate branches never
touch the same lines:

```ts
  // `/crawls/:crawl/overview`: Checking a site while it runs, then the Summary.
  overview: simpleScreen(() => import('../simple/SummaryScreen'), 'SummaryScreen'),
```

- `home` is `/`, `overview` is `/crawls/:crawl/overview`, `issues` is `/crawls/:crawl/issues/:check?` (What to fix and
  one problem: the screen reads `check`), and `report` is `/crawls/:crawl/report`.
- `report` is the same screen in both modes. While its row is null the route is not found and nothing links to it;
  set, it's the sidebar's third section in Simple (Ctrl+3) and eighth in Advanced, after a rule (Ctrl+8). Its search
  (`cut`: `full` or `summary`) is validated already (`validateReportSearch`).
- The router preloads the screen the mode shows, and the other mode's once the app is idle, so a switch never waits
  for a chunk.
- New crawl and Settings aren't rows: each is one screen that reads the mode itself.
- Every search param the design's Routes table names for round 4's screens is accepted already: Overview's `score`
  (`validateOverviewSearch`), the Report's `cut`, and Issues' own for What to fix and one problem. A route's
  validator serves both views.
- **The frame is tested with every row set.** The unit suite runs twice (`vitest.config.ts`): as the app is (`app`),
  and with the table served by `src/test/simpleScreens.filled.tsx`, a stub screen in every row (`filled`, through
  `scripts/filled-table.mjs`). `npm run e2e:filled` runs the mode's, the top bar's and New crawl's end-to-end tests the
  same way. A test that depends on whether a row is set reads the table (`SIMPLE_SCREENS.report ? 1 : 0`), so both
  pass whichever rows are set. `src/app/simpleScreens.test.tsx` holds the frame to a filled table too: the Simple
  screen in Simple and the Advanced one in Advanced on the same route, the inspector left out, the Report's sidebar
  item, keys and route.

### The shell in Simple, and the top bar

- **The sidebar** in Simple: Home; the open site; **Summary**, **What to fix** (counting the checks with issues that
  are Serious or Worth fixing, Errors and Warnings, from the same entries as Issues) and **Report** (once it exists);
  "Checking now"; Settings. Advanced's is as built, with Report after a rule once it exists.
- **The top bar**: the switch at its end (`layout/ModeSwitch.tsx`, a segmented control in words; what it does and
  Ctrl+Shift+A are on the group, as its description and `aria-keyshortcuts`, and in its tooltip), Simple's section names in the crumbs, the status pill in Simple's words (`StatusPill words`:
  Checking, Stopped early, Working out the results, "2,713 checked"), and search as its icon in Simple. Outside a crawl
  the crumbs name the page (New crawl or Check a website, Settings).
- **The order of giving way** (`layout/useGiveWay.ts`), measured on the bar's own width, so it holds whatever the
  sidebar is doing. The bar's parts never shrink. Steps: 1 the keys in buttons, 2 the crawl's date, 3 the search
  field to its icon, 4 Home, 5 the status pill's count, 6 the controls' words (to icons), 7 the site shortened (to
  `--topbar-site-min`), 8 the site gone, 9 Forward, 10 the controls and search into one More menu (named for what's
  in it, its items with their keys), 11 the status pill to its dot, 12 the section shortened (to
  `--topbar-section-min`), 13 the section gone. The bar takes the fewest steps that let everything fit: each step
  is marked on the bar as `data-give-N` (cumulatively), each part hides or narrows on its own step's mark
  (`group-data-give-3/topbar:…`), and the measure runs before paint (a ResizeObserver on the bar, a MutationObserver
  on its content, the fonts arriving), usually in one or two reads. What gives way keeps its name: the date, the
  site, the count, the pill's words, the section and the controls' and search's words become `sr-only`; Home and
  Forward become `sr-only` too, and show while they have focus; the pill and the section have their whole words in
  a tooltip, and the breadcrumb's tooltip has the whole trail. Never: Back and the switch. `e2e/topbar.spec.ts`
  sweeps the window every 10 px from 1440 to 400 CSS px in both modes, idle and running, at 100% and 200%: the bar
  and the crumbs hold their parts, the switch is whole and in the window, and nothing the bar shows is cut narrower
  than 40 px.

### Settings

- **One file a section**, in `screens/settings/sections/`, found by `screens/settings/registry.ts`
  (`import.meta.glob`). A section file exports `id` (its place in `/settings/:section`), `title`, `order` (General
  10, Crawl defaults 20, Analysis 30, Data folder 40, Shortcuts 50, Diagnostics 60, Updates 70, About 80) and
  `function Section()`. **The Analysis section is one new file, `sections/Analysis.tsx`, and nothing else**: it is
  Advanced only already (`ADVANCED_ONLY_SETTINGS`), Simple's note names "the checks' limits" once it exists, and
  until then Analysis isn't listed. An address naming a section with no file opens General.
- **Each mode's list**: Simple lists General, Data folder, Updates and About, with a note naming the Advanced-only
  sections that exist and **Show them in Advanced** (focus goes to the first section it brings into the list).
- **General**: Mode (the switch's value, as two choice cards, each with its line), Theme, and Density in Advanced.
- **Crawl defaults**: New crawl's form in Advanced, without a start URL, saved a moment after each change once the
  engine finds nothing wrong (`PUT /api/crawl-options/defaults`), with "Put back the engine's defaults" (with Undo).
  Every new crawl starts from them, and so does Home's Enter: `startCrawl` sends the saved defaults, since a setting
  left out of `POST /api/crawls` takes the engine's default, not the user's.
- **Data folder**, **Diagnostics** (Copy diagnostics: versions, folders, mode, theme, zoom, page, browser; nothing
  from a crawl, the home folder as `%USERPROFILE%` or `~` and the account name nowhere), **Updates**, **About** (with Copy diagnostics in Simple) and **Shortcuts** (the registry's list) show
  what is true today. Neither the shell nor the sidecar can move the data folder or look for updates, so there's no
  control for either (below).

### New crawl, and the crawl's settings

- **One form** (`crawl/options/`): `form.ts` is the model (every `CrawlOptions` property; numbers held as typed, so
  a half-typed value stays and reaches the engine as typed for its message; the HTML limit in MB), `words.ts` each
  setting's name and line, the presets' words in each mode (from the presets' numbers) and each folded section's
  line, `CrawlOptionsForm.tsx` Simple's four questions or Advanced's start card and five folded sections. New crawl
  draws it in the mode shown, from the same state, so what is typed goes across the switch.
- **Validation** is the engine's (`POST /api/crawl-options/validate`, a moment after the last change), shown beside a
  field once it's been left or changed, and every field once Start was pressed; a folded section holding a message
  says so in its line. A refused Start opens the first field's section and puts focus on it. In Simple, a message
  about a setting Simple doesn't show, and settings changed from the engine's defaults, are said above the buttons
  with "Show it in Advanced" or "Show them in Advanced".
- **Start** is the form's submit and `crawl.start` (Ctrl+Enter, from any control: the form takes the key before a
  checkbox, which keeps Enter for itself), one start at a time however it's asked for. A header goes with its value
  only: a name saved or copied without one is never sent (`sendableHeaders`, which Home's Enter uses too). An HTML
  limit keeps its bytes unless it's changed. "Use these choices every time" (Simple) saves the form's options once
  the crawl has started, and "Save as my defaults" (Advanced) at once, each without a start URL or a secret. In
  Simple, a message about one of its questions is tied to it and in Simple's words.
- **Secrets** (header values, a proxy's user name and password) live only in the form's state while the app runs:
  never in saved defaults (the app strips them before the sidecar does), never in a crawl file. The OS credential
  store the brief offers, with the user's consent, needs a shell command the shell doesn't have (below), so nothing is
  remembered between runs.
- **`/new` params**: `url` (the start URL), `from` (a crawl's id: "Crawl again with these", its options and start
  URL, with a note that header values and a proxy password must be given again; a crawl the library no longer has
  gives the saved defaults, with a note), `focus` (`speed`, `headers`, `proxy`, `userAgent`, `robots`: that setting's section open and focus on it; `robots` arrives with robots.txt ignored; in
  Simple, a setting only Advanced has turns Advanced on). The engine's own defaults, for "changed from the default",
  are a copy in `crawl/options/engineDefaults.ts`, which `e2e/new-crawl.spec.ts` checks against a sidecar with none
  saved.
- **The crawl's settings** (`/crawls/:crawl/settings?focus=`): what it used, by section, as words, from
  `GET /api/crawls/{id}/options`; "Crawl again with these"; and Continue, for a stopped crawl, with what a continue
  may change (the speed, the timeout, the headers with their values, the proxy), each checked as it's typed (a value
  that isn't a number is never sent, as the engine would read it as unchanged). It lists the proxy it used by its
  address, without the credentials. `needs-secrets` is answered in place: the headers whose values must be given again, the proxy's, and "Continue without" for headers. A running
  crawl's card says to stop it first; an ended one has nothing to continue. Overview's alerts lead here: a blocked
  crawl's "Continue more slowly" (`focus=speed`) and "Add headers" (`focus=headers`), and to New crawl for "Crawl as
  another user agent" and "Crawl again with robots.txt ignored" (`from` this crawl). Continue's toast, when a proxy
  password is needed again, opens this screen on the proxy (`focus=proxy`).

### What the shell and the engine don't give

- **The OS credential store.** The shell has no command for it (a keychain crate and two commands, read and write by
  a key the app chooses, asking the user first). Until it has, secrets last as long as the form.
- **Moving the data folder.** The sidecar takes its data folder at start (`--data-dir`, from the shell) and has no
  call to change it; Settings shows it, with Copy.
- **Looking for updates.** The shell has no updater; Updates says how a new version arrives.
- **A proxy test.** The parity list keeps "Test proxy connection"; the sidecar has no call for it, so the form has no
  Test button.
- **The engine's own defaults** once the user has saved theirs: `GET /api/crawl-options/defaults` gives one or the
  other. The app keeps a copy, checked end to end.

## Hand over, search, favicons and the analysis's limits

Round 3's last slice: flow 5 of the IA ("Hand over"), the palette's search (E6), the sites' icons (E6) and Settings >
Analysis (E6), with what Links and the URL grid lacked of E6's link ends. `app/src/export/`, `keyboard/urlSearch.ts`,
`crawl/favicons.ts`, `screens/settings/sections/Analysis.tsx`, `screens/urls/linkEnds.ts`.

### Export

- **One dialog, one toast, one store: the shared pieces.** A screen builds an offer of what it shows
  (`export/offers.ts`: its choices, each a request builder over the grid's own read, its file's words and its column
  titles) and registers Export with `useExportCommand(offer, deps, { group })` (Ctrl+E, the palette's "Export this
  view…" and, on Overview and Issues, "Save the report…" and "Save the spreadsheet of everything…" from
  `CRAWL_FILE_COMMANDS`); `ExportButton` is its button. `openExportDialog(offer, { choiceId })` opens the one
  `ExportDialogHost` (in `AppShell`). **The Report screen (round 4) should call `openExportDialog` with
  `crawlFileChoices(item, simple)` for Save and Spreadsheet of everything, or `beginExport` directly with a path, and
  get the same toast.**
- **The dialog** asks what (a grid's view; Issues' this check, the open group's pages, the report in its two cuts,
  every check) and as what (CSV or Excel; Full report or Summary page), then the shell's native save dialog asks where,
  opened on `<site> <crawl's date> <view>.<ext>` in the folder the last export went to (`settings.exportFolder`; the
  last grid format is `settings.exportFormat`). A browser has no save dialog: the dialog has a path field instead
  (which the e2e uses). A refusal the engine gives before writing a row is said in the dialog, with its action; a
  typed extension sets the format.
- **The store** (`export/exportStore.ts`) keeps every export by id, from the call's answer, each `export` event and,
  on every `hello`, `hello.exports` (read once each, so an export running before a reload keeps its toast; one the app
  followed that is no longer running is read once to learn how it ended, and `export-not-found` is "lost"). A newer
  read never takes an ended export back to running nor a running one back in rows; the bar's share (`peak`) never
  goes back. `startExports()` (main.tsx) wires it.
- **The toast** (`export/ExportToast.tsx`, shown by `showExportToast`) follows its export in the store: starting,
  counting, writing (rows written of rows expected, the count text kept out of the live region: the bar's value),
  then Saved with the rows and the engine's time (`finishedAt − startedAt`, the engine's clock), cut cells, empty
  columns and "the crawl was running"; Cancelled; Stopped (lost); or the problem in the app's words
  (`exportProblemWords`) with the actions that help (Choose another file…, Try again, Export again, Export as CSV…,
  Analyse the crawl / Work out the results, Export in crawl order). Open file and Open folder are the shell's
  (`open_export_file`, `reveal_export_file`: they take the export's id, and the shell asks the engine for its path,
  only a done export's `.csv`, `.xlsx` or `.html`). Closing a running export's toast leaves the export going.
- **Exports in progress** (`export/ExportsInProgress.tsx`) are listed at the bottom of the sidebar under the running
  crawls; a press brings the toast back. The shell's own event stream tells an export that was saved or failed while
  the window was hidden or not focused, once (`live/notify.rs`, `export_notice`).
- **Every grid**: URLs, Links, one sitemap's URLs, an origin's blocked URLs (all, one line's or one reason's, each
  named by it), Issues and Overview. On Issues, "This check" of a check that groups is its sheet of the workbook
  (`kind: check`); of a check listed flat it is that list as the grid shows it, in its sort (`issueRows`, `url` and
  `detail`). Ctrl+E works from a text field too (every grid has its search field). `export-busy` is one type for three
  causes, told apart by the engine's title: a file being written, four exports running, the engine shutting down.
  Not exportable, said by Ctrl+E: the list of sitemaps and robots.txt's origins (the engine has no kind for them), and
  a URL view narrowed to a links view's ends (export takes no `linkEnds`). The URL grid's status column goes out with
  `outcome` beside `statusCode`, and the links grid's follow column as its four flags.

### The palette's search

`keyboard/urlSearch.ts`: as the palette opens (Advanced, on a crawl) an empty `POST /search` reads the crawl's text;
then every keystroke searches, the one before aborted, no debounce. Results are under the commands as "URLs in this
crawl": the title over the address, the typed text marked in each (ASCII case ignored, as the engine ignores it), the
status in words ("404 Not Found", or the outcome's), and "All N in URLs" (the URL grid searched for the text, which
lands on N). Enter opens the inspector (`goToUrl`). States: searching, results, none, failed (said in the palette).

### Favicons

`crawl/favicons.ts`: a crawl whose item says `hasFavicon` is asked for its icon once a session, whatever shows it
(`CrawlFavicon` on Home's rows, the top bar's crumb and the sidebar's crawl); the bytes are an object URL. A 204, a
failure or an image that can't be drawn (`Favicon`'s `onError`) keep the letter tile, the same size.

### Settings > Analysis

`screens/settings/sections/Analysis.tsx`: every limit from `GET /api/analysis-settings` with its description, its
unit and its range (`analysisLimits.ts` names each and words the range), checked by `validate` 250 ms after a change
(what's typed sent as typed, so text gets the engine's own message), saved 600 ms after when nothing is wrong. Errors
by a limit's name go beside it whichever limit was changed (the engine keys "the longest must be more than the
shortest" on the longest), and above the form too, named, when the limit wasn't the one changed; `settings` and
`limits[n]` above the form. A save's answer that arrives after more typing isn't said saved: the newer draft is next. Put back one limit, or every limit, each
with Undo. A save reads the library and every crawl's reads again: a crawl analysed with other limits reads "Analysis
out of date" and its analysis's `settingsChanged` reason, where the app shows that.

### Links to the URL grid (E6's link ends)

The URL grid slice hadn't built it. Links has **Show in URLs** (their targets, or the pages they're on), each counted
as the menu opens (`POST /links/ends`, `limit: 1`). The URL grid takes `links: { end, filter, q }` in its route, as
`linkEnds` beside its own filters in its reads, facets and copies, with a line saying which end of which links ("Only
the URLs that the broken links lead to, each once."), Back to the links (to the links view's filters, search, sort and
columns) and Show every URL. A saved view keeps the narrowing: it is part of the view's route.

### Measured

13 Nov 2026, this machine (shared with other agents), the production build in headless Chromium at 1280x720 against
the real sidecar, on the 100k crawl (`e2e/handover-measure.spec.ts`, from the input to the frame after): Ctrl+E to the
dialog 18 to 24 ms; a palette keystroke to its marked results 27 to 34 ms ("product", a letter at a time); the whole
URL grid (112,577 URLs) as Excel from Export to Saved 889 ms in the page, the toast saying the engine's 862 ms, a
10.1 MB file.

### Not done, and why

- **No export of the sitemaps' list or robots.txt's origins**, nor of a URL view narrowed to link ends: the engine has
  no kind for the first two and its export takes no `linkEnds`. Asked of the engine.
- **Open file and Open folder** are driven in the release build by `npm run smoke:shell` (its export step: an export
  started through the engine, the toast's Open folder seen as an Explorer window on the folder, Open file as the CSV
  open in its program, both closed again by `scripts/shell-opened.ps1`). **The native save dialog and the
  notification** are not driven anywhere: the walk can't operate a native dialog, and the notification is covered by
  the shell's unit tests of its words.

## Simple: Home, checking a site and the finished toast (round 4, part A)

Simple's Home and its checking screen ([Simple mode](v2-ui-simple.md#home), "While it checks the site", "The report
at the end"), in `app/src/simple/`, and the score ring in `app/src/ui/`.

- **The rows set**: `home` is `simple/home/SimpleHome` and `overview` is `simple/SimpleOverview`, which shows the
  checking screen (`simple/checking/CheckingScreen`) while the crawl runs, is paused or stopping, while its results
  are worked out, and for up to 1.5 s after a crawl completes before its analysis begins (`HAND_OVER_GRACE_MS`, so it
  never flashes "never analysed"); then the Summary in place, focus that was on the checking screen going to the
  Summary's title. Until part B's Summary is merged, the Summary is Advanced's Overview (`TODO(round4-merge)`).
- **The score ring** (`ui/ScoreRing.tsx`, the words in `ui/score.ts`): the accent's arc on a neutral track, never a
  severity colour; `partial` dashes the arc (one dash array, no mask, so no id to keep unique) and says "so far" in
  the ring and its word; `null` is a dashed track and "–". Sizes are the `--ring-*` tokens. One image to a screen
  reader, with its word when `word` sets it beside the ring ("Score 85 out of 100, Good").
- **Home**: the field ("Web address", Enter starts the check with the saved defaults, as Advanced's Home does), and
  the line under it from `GET /api/crawl-options/defaults` against the app's copy of the engine's
  (`simple/home/savedSettings.ts`). Your sites (`simple/home/sites.ts`) groups the library by site, the newest check
  the row and the older ones folded under it, the checks running now pinned above. A row's kind decides its words
  and its one action: live (progress), scored, stopped early (dashed, "so far", Continue), never worked out (Work
  out the results), worked out by an engine before the score (Update the results), no pages read, file not found
  (Locate the file…, in the shell), unreadable. What needs fixing counts the problems as What to fix does: the
  library's Error and Warning counts, or, with checks hidden, the row's analysis read and the hidden ones left out.
  The row's menu has Advanced's actions in Simple's words; Open the report only once the Report screen exists.
- **Checking a site**: three steps, three figures taken from one progress event (or, once the crawl has ended, the
  crawl file's stats), one bar, and the time left as a floor: what's queued over the current rate, said as "at least"
  ("about" when the URL limit is nearer than the queue's end, as nothing more can be found to read then). What's in
  the way (`simple/checking/checkingWords.ts`) is the design's table: the alerts of the current run (an earlier
  run's are left out, by instant) and the progress's waits, one line a kind, in a log so a new one is read once,
  each action landing where Advanced's alert does and saying "in Advanced" when it leads there. The figures aren't
  links (the design draws them plain, and the screens they'd open are Advanced only): the end-to-end test holds
  them, paused, to the URL grid's crawled count, the queue and the pages read.
- **The finished toast** (`live/liveBridge.ts`, words in `simple/finishedWords.ts`): in Simple "{site} is checked",
  "Score 85: 6 problems to fix", with See the summary and Open the report (the action, once the Report screen
  exists; `notifyResult`'s new `secondary` is drawn before it); in Advanced its words, and Open the report as its
  second action. A stopped check has Simple's words too. The problems leave out hidden checks, reading the analysis
  afresh when there are some.
- **The system notification** (`src-tauri/src/live/notify.rs`): the same words in Simple, from the mode and the
  hidden checks the page stored (`settings::preferences`, read as the page reads them: nothing stored is Simple), the
  library item's `score` and `pageCount`, and, with checks hidden, `/api/checks` and the analysis's counts. The
  notification plugin has no buttons on the desktop, so See the summary and Open the report are the toast's only.
- **Tests of these screens through the whole app** run in the `app` project only (`simple/tableStubbed.ts`): the
  `filled` project's rows are stubs.

## Simple's results (round 4, part B)

The Summary, the score's breakdown, What to fix and one problem ([Simple mode](v2-ui-simple.md)), in `src/simple/`,
plugged in as two rows of the routes' table: `overview` (`SummaryScreen`) and `issues` (`WhatToFixScreen`, which shows
one problem when the route names a check).

- **One model.** `simple/results.ts` (`plainResults`) makes everything the three screens count from the catalog, the
  last analysis and the checks hidden in Advanced, through Issues' `checkEntries` and `rankChecksToFix`: the problems
  by severity in the engine's `fixFirst` order, the five to fix first (errors and warnings only), what couldn't run
  grouped by its reason, what passed, the categories with nothing found, a serious fault of the whole site. Hidden
  checks are left out of every list and count, as Advanced leaves them out; the score is the engine's and counts them,
  so "Fixing these five takes the score from 85 to 98" is said only when the engine's `ifFixed.checkIds` are the five
  shown. The sidebar's What to fix count comes from the same entries.
- **Words.** `simple/plainWords.ts` builds every sentence that isn't a check's from counts only: the Summary's sentence
  (band lead, problems, "so far", a site-wide fault leading by its plain title, nothing to fix), what the score covers
  (pages, checks, robots.txt, a tiny site), a count's share ("9 of 4,521 addresses", "The whole site", "Once, for the
  whole site", by the check's `subject`, `siteWide` and `reportedOnce`, out of the analysis's own totals), points and
  who fixes it. A check's own words are the engine's; a check with no plain words shows its technical ones.
- **Every figure lands on itself.** The five counts beside the score open their part of What to fix by its hash
  (`simple/fixSections.ts`: `#serious`, `#worth-fixing`, `#worth-a-look`, `#couldnt-check`, `#nothing-wrong`); a part
  that folds opens when it's landed on, and its heading takes focus. A count of 0 is no link. Each problem's row opens
  its page, whose head says the same count and whose Where it is lists exactly that many. `e2e/simple-results.spec.ts`
  walks every figure on the seeded crawl, or on a real one with `SI_E2E_CRAWL`.
- **The breakdown** is `ScoreBreakdownFromRoute` (`simple/ScoreBreakdownDialog.tsx`): open while Overview's route has
  `score=1`, so Back keeps it; Esc, Close and ✕ take `score` off the address. Advanced's Overview renders the same
  component (part C). Its points table is version 1's and shows only for a score of version 1.
- **One problem**: what's wrong (`plainWhat`), how to fix it (`plainFix`, numbered), who fixes it (`fixer`), points,
  the One fix callout when the check's largest group is `likelyTemplate` (`plainOneFix`), Where it is
  (`simple/WhereItIs.tsx`: the check's groups 25 at a time to 200, each cause with its first three pages by title then
  address and Show them; a check that doesn't group lists its pages flat with what was measured; past 200, "See all N in
  Advanced"), Copy the list (Ctrl+Shift+C: both titles, the site and date, the steps, then each cause with its full
  addresses, read with the rows' cursor), Save as a spreadsheet (Ctrl+E: E5's `check` export through the save dialog
  and a progress toast, `simple/handOver.ts`) and See it in Advanced (`switchMode`, the same route). A skipped check's
  page says what it looks for, why it couldn't run and what would let it; a passed check's says its good news.
- **Keys**: J and K on What to fix move through the problems (the row the keys are on is `aria-current`, its link
  focused, so Enter opens it); Ctrl+Shift+C and Ctrl+E on a problem; Esc closes the breakdown. Registered by the
  screens, so they're there in Simple only.
- **States**: first load (the skeleton after 150 ms), done, perfect, nothing but things worth a look, stopped early,
  at its limit, turned away part way, failed, results that couldn't be worked out (the live `error` of a crawl that
  didn't fail), out of date with its reasons, never analysed, scored by an earlier engine (no score), nothing read
  (`simple/nothingRead.ts`, Simple's words for how the start address answered). Each is a story under
  **Screens/Simple results**, over `test/simpleSidecar.ts`: the catalog with its plain words and the garden crawl's
  analysis with its score, captured from today's engine (`test/simple/*.json`), and the states made from them.
- **Stand-ins until the other parts merge** (`TODO(round4-merge)`): the score ring (`simple/ScoreRingStandIn.tsx`,
  part A's), checking a site (a running or analysing crawl shows Advanced's live Overview, part A's), and the hand-over
  slice's export dialog and toast.
