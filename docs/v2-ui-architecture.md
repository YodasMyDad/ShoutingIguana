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
| `src/shell` | The IPC with Rust (`ipc.ts`), the connection store, and OS actions with browser fallbacks (`os.ts`) |
| `src/live` | The SSE parser and reconnecting stream, the live store, and the bridge from events to the rest of the app |
| `src/grid` | The grid primitive: windowed scroll space, virtualiser, keyboard and selection model, copy, paging hook |
| `src/keyboard` | Chords, the command registry, the app's commands, the palette and the `?` sheet |
| `src/settings` | Preferences: schema, store (Tauri store or `localStorage`), appearance |
| `src/feedback` | The loading rule, skeletons, toasts, error states, route errors, announcements |
| `src/layout` | The shell's layout: sidebar, top bar, inspector slot, banners, the stopping overlay |
| `src/screens` | One file per route. Home and the URLs grid work; the rest are stubs for their slices |
| `src/app` | The router, the query client, overlays, sections, the root component |
| `src/styles` | `tokens.css` (every design value), `base.css`, the Tailwind entry |
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
  sidecar is local, so "offline" never applies). Grid pages' keys are `['grid', <grid>, <crawl id>, ...]`, so
  `gridPagesOf(crawlId)` finds one crawl's pages.
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
`quit.rs`, `window.rs`, `webview2.rs`, `context_menu.rs`, `open_files.rs`, `commands.rs`.

- **Starting.** A 32-byte token per spawn in `SI_SIDECAR_TOKEN`, the exact webview origin in `SI_SIDECAR_ORIGINS`
  (plus the dev server's in debug builds), `SI_PARENT_PID`, `--stdin-lifeline`. On Windows the sidecar joins a job
  object that kills it if the shell dies. The binary is the `externalBin`, built by `scripts/build-sidecar.*`
  (self-contained, single file, ReadyToRun).
- **The connection, to the page.** One state machine. Command `sidecar_state` returns it; event `sidecar://state`
  carries every change: `starting`, `ready` (port, token, version), `restarting`, `failed` (reason, exit code, a plain
  message), `stopping`. Each has a rising `seq`: the page listens first, then asks, and keeps the newest.
- **Restarts.** A lost sidecar restarts with backoff (250 ms to 4 s); five in a minute is `failed`, with Try again
  (`restart_sidecar`). Exit code 3 (another sidecar holds the state folder) is retried for about 40 s.
- **Quitting.** With a crawl running, one native question: "Stop crawling and quit?". Then the window shows
  "Stopping crawls…", the shell writes `shutdown` to the sidecar's stdin and waits up to 35 s (the contract's 30 s bound
  and a margin) before killing it. Closing the window while a crawl runs hides it to the tray instead, and says so once.
- **The tray.** Present while a crawl is live or the window is hidden: a progress ring on the icon, the crawls with
  Pause and Resume, and Quit. Hidden, the webview is told it's invisible and its memory target is set Low, which held
  the app at 10 to 25 MB in the spike.
- **OS.** Single instance (a second launch focuses the window and forwards `.sicrawl` paths: `shell://open-crawl-files`
  and `take_pending_open_files`). Window size and position remembered. WebView2's browser keys are off, so the
  registry owns F5, Ctrl+F and the rest, and its context menu keeps only editing items.
- **Permissions.** The page gets core IPC, `setTheme`, `setZoom`, open and save dialogs, http(s) links, show in folder,
  the store and the shell's five commands. Nothing that spawns, reads files or notifies.

## The API client

- `api/client.ts` has a function for every E1, E2 and E3a call: the URL grid, its columns, facets and lookup, the
  inspector (one URL, its links both ways, its HTML) and a check's groups and rows. `api/grids.ts` and
  `api/inspector.ts` type them as the sidecar's code at 46a2703 answers, which differs from the contract's text in a
  few places, each noted where it's typed:
  - a wrong JSON type anywhere but a filter's `value` or `values` is `bad-request` (400), not `invalid-query`;
  - `outcome`, `noindex`, `nofollow`, `h1Count` and `ttfbMs` don't sort (go by `/api/url-columns`' `sortable`);
  - a page's `offset` is null when it was read from a cursor, and `matching` and `total` are null for a skipped check;
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
  the count.
- A count the stream sends malformed is null, shown as "–", never 0. A status the contract doesn't have is dropped.
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
- The inspector reads the current view's checked `inspect`, so it opens only on a view that has one.
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
  so in a toast; one that works is announced.
- **Loading**: the first load's skeleton shows only after 150 ms, and once shown stays at least 300 ms, rows or not.
- Give it a new `key` when the rows change meaning (another sort or filter): that resets the scroll and selection.

The full grid (TanStack Table's column model, presets, chooser, filter bar, saved views, grouping) wraps this in the
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
- While anything modal is open (the palette, the `?` sheet, "Stopping crawls…"), the app behind it is `inert` and the
  registry runs none of its keys, but the one that toggles the overlay (`inOverlays`). When an overlay closes, focus
  goes back where it was, else to the view's grid, else to the main area (`restoreFocus`); a navigation that removes
  the focused element puts focus in the new view.
- **Esc**, innermost first: an open overlay (palette, `?` sheet, menu) closes; then the inspector; then the focused
  grid's selection clears. The grid asks the registry (`claims`) before taking Esc itself.
- The same registry feeds button tooltips and `aria-keyshortcuts` (`Button`'s `command` prop), the `?` sheet, Settings
  > Shortcuts and the palette. A key can't be listed without working.
- The grid's keys are registered with `scope: 'grid'`: listed, never dispatched (the focused grid handles them).
- The palette is an ARIA combobox over a listbox on a Radix dialog. cmdk was the brief's pick, but its last release
  (1.1.1) leaves `aria-activedescendant` naming a stale or removed option, so screen readers hear the wrong command.
  Option ids are the entries' own, so the attribute changes whenever the highlighted command does, and a live region
  that's always there says how many results there are once typing pauses.

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
  with `surface-selected` as `Canvas`: selected and current things get a `Highlight` outline on the page's own colours,
  so no text sits on Highlight unreadable, and selected text is HighlightText on Highlight. Focus rings, the grid's
  included, are outlines, so they survive it.
- Fonts: Inter and JetBrains Mono are named first in the stacks but not bundled yet (the design-system pass adds them).

## Feedback

- **Loading**: `useDelayedLoading` (skeleton after 150 ms, at least 300 ms once shown). Wherever a skeleton is used it
  stays up while `useDelayedLoading` says so, even when the data comes a moment after it appeared. Skeletons shimmer
  only when motion is allowed.
- **Toasts** (`notify.ts`): results with an action, Undo for anything reversible, errors with Retry when it could work
  and the detail copyable, progress with Cancel. Never for form validation: that's inline. A toast with an id replaces
  the one showing with that id: the same failure twice is one toast, and a crawl's toasts are keyed by crawl and event
  (`crawlToast`), so the live bridge dismisses one whose action no longer fits (Continue, once the crawl runs again).
  Sonner is unstyled: every look is the app's, in unlayered rules more specific than Sonner's own stylesheet, so the
  app's colours and focus rings win, and a toast follows the theme setting, the OS's included.
- **Errors**: `ErrorState`, the plain cause first, "Details" behind a button, Try again only where trying again could
  work (`canRetry`: never for a problem the engine meant, such as a 404 or a 422), and the view's own actions (Analyse
  it, Reset the view). The crawl actions offer Retry per call: a Pause, Resume or Stop refused as `crawl-busy` (the
  crawl isn't live here) gets none.
- **Announcements**: `announce()` speaks through one polite and one assertive live region. Crawl status changes are
  announced by the live bridge.

## Rules every slice follows

1. Every view has every state in [the brief's table](v2-ui-brief.md#every-view-has-every-state), each in Storybook.
2. Tokens only. No raw colours, sizes or arbitrary values in components (ESLint refuses arbitrary values).
3. Server data through `api/` and TanStack Query; live data through the live store; a view's state in its route.
4. Every action is a registry command, so it has a key, a palette entry and a tooltip from one place.
5. Grids are `GridCore`. Lists of variable height use TanStack Virtual.
6. Crawled text renders as text, never HTML. Check titles and words come from the API, never hard-coded.
7. UK English and the glossary in every string. No raw engine ids or errors without plain words.
8. `aria-*` right the first time: axe runs on every story and in the end-to-end tests, with zero violations allowed.
9. Tests with every change: pure logic unit-tested, components with Testing Library, flows in Playwright.
10. New npm packages pinned exactly, two weeks old at least, installed with `npm install --before=`.

## Left for the slices

- Every screen except Home: Overview (live and done), Issues, the full URL and Links grids, Sitemaps, robots.txt,
  New crawl, the crawl's settings, the inspector's content, the rest of Settings.
- The engine's E3b to E6: the links, sitemaps and robots grids, analysis progress, export, search, favicons.
- A grid sorted by something other than crawl order keeps its selection by row index while a crawl adds rows, so a
  live refresh can move rows under it: selecting by row id comes with the full grid.
- Fonts, icons and the final look, from the design-system pass.
- Drag-and-drop of `.sicrawl` files (the shell already delivers drops through Tauri's drag-drop event).
