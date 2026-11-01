# Step 3 stack spike: results

The spike checked two things before step 3 commits to them: that the recommended libraries meet the
[performance budgets](v2-ui-brief.md#performance-budgets), and that the Tauri shell and a .NET sidecar work together as
planned. The code is throwaway and lives in `app/spike/` (git-ignored build output; nothing in `engine/` or `src/`
changed). This report is the deliverable. Measured on 1 November 2026.

## Verdict

- **Shell and sidecar: works as planned.** The token goes by environment variable, the sidecar binds `127.0.0.1:0` and
  announces its port in one JSON line, and the webview calls it with a bearer token. WebView2 sends
  `Origin: http://tauri.localhost` in a release build and `http://localhost:51873` (the dev server) under `tauri dev`,
  and preflights every request because of the `Authorization` header. SSE over `fetch` works in WebView2, including a
  reconnect to a restarted sidecar (477 ms from crash to events flowing again). Two things are not free: the shell
  plugin leaves a Rust-spawned sidecar running after a normal exit, and a hard kill of the shell orphans it whatever
  the plugin does. Both have tested fixes.
- **Grid: the recommended stack meets the budgets**, with one required addition. TanStack Table, TanStack Virtual and
  TanStack Query scrolled 1,000,000 rows at normal speed with no dropped frames and no blank rows at 1× and at 4×, in
  Chromium and in WebView2, and showed the first page in 65–68 ms (263–313 ms at p95 at 4×, at the edge of the 300 ms
  budget). The addition: Chromium cannot draw a 28M px scroll area correctly (at 125% scaling rows drift past about
  row 480,000, and the extent is capped at 33.5M px), so the big grids need a windowed scroll space. The spike built one; it costs nothing
  measurable. A canvas grid (Glide) was not needed.
- **Live updates: the UI held 60 fps** while the grid scrolled during a simulated 2,900 pages/s crawl, with both chart
  libraries (at most 0.5% of frames over 16.7 ms at 4×), and batching held renders to 4 a second even at 20 events a
  second. **Use visx**: it is a twelfth of the size, accessible and themeable through CSS, and just as fast here.
- **Cold start: well inside 2 s.** The release build painted its shell 331 ms after launch (515 ms on a first launch),
  before the sidecar was ready, and showed rows at 474 ms. With a sidecar that took 3 s to start, the shell still
  painted at 321 ms.
- **Memory: the open question.** The JS heap stays near 21–25 MB, but WebView2's processes together start at 267 MB and
  reached 455 MB after scrolling 54,000 rows, mostly renderer memory that pressure does not return. Whether the 300 MB
  budget is met depends on what it counts; see [risks](#risks-and-gotchas).

## Environment

| Item | Version or detail |
| --- | --- |
| Machine | Intel Core i9-14900K (24 cores, 32 threads), 32 GB, NVIDIA RTX 4090, 3840 × 2160 display at 240 Hz, Windows 11 Pro 10.0.26200 |
| Toolchain | Node 24.22.1, npm 11.19.1, Rust 1.99.1 (`stable-x86_64-pc-windows-msvc`) with MSVC 14.29.30133 from VS 2019 Build Tools, .NET SDK 10.0.403 |
| Browsers | WebView2 Runtime 156.0.4314.58; Playwright's Chromium 156.0.8078.4 (same major version) |
| UI packages (exact) | `react` and `react-dom` 19.3.1, `@tanstack/react-table` 9.3.1 (`table-core` 9.3.1), `@tanstack/react-virtual` 3.14.25 (`virtual-core` 3.19.10), `@tanstack/react-query` 5.105.1 (`query-core` 5.105.1), `echarts` 6.1.0 (`zrender` 6.1.0), `@visx/shape`, `@visx/scale` and `@visx/group` 4.0.0 (`d3-scale` 4.0.2, `d3-shape` 3.2.0), `@tauri-apps/api` 2.12.4 |
| Build and test packages (exact) | `vite` 8.3.5 (`rolldown` 1.2.16), `@vitejs/plugin-react` 6.1.2, `typescript` 7.0.2, `@types/react` and `@types/react-dom` 19.3.0, `@tauri-apps/cli` 2.12.4, `playwright` 1.64.1 |
| Crates (from `Cargo.lock`) | `tauri` 2.12.5, `tauri-build` 2.7.4, `tauri-plugin-shell` 2.6.0, `tauri-runtime-wry` 2.12.3, `wry` 0.57.1, `webview2-com` 0.39.1, `getrandom` 0.4.3, `windows-sys` 0.61.2, `serde` 1.0.229, `serde_json` 1.0.154, `tokio` 1.54.1 |
| Dummy sidecar | ASP.NET Core 10 minimal API (`CreateSlimBuilder`, Kestrel, built-in `TypedResults.ServerSentEvents`), published framework-dependent single-file for `win-x64` (209 KB; needs the .NET 10 runtime) |

The machine is far faster than the mid-range laptop the budgets are written for, so every browser measurement was
repeated with Chromium's CPU throttling at 4× (`Emulation.setCPUThrottlingRate`). Other sessions on the machine ran an
engine benchmark and a test suite during part of the work. A run made under that load was discarded; from then on the
harness waited for machine load below 8% before each run and recorded it during every measurement (4–17%, which
includes the browser's own threads). The first 60 Hz run predates that check; where it is quoted, it is shown as a range
with a checked run.

## Method

- **Data.** The sidecar generates 1,000,000 rows deterministically (row *i* is always the same): URL of 61–120
  characters, status, indexability and reason, title, content type, size, depth, response time, inlinks, outlinks and
  words. Pages of 200 rows are JSON objects (about 68 KB each) and each request waits 20–50 ms (fixed per offset, with
  the Windows timer at 1 ms, so the delay is real). The client saw page latencies of about 38–45 ms at p50 and 50–58 ms
  at p95, including the CORS preflight and JSON parsing.
- **Grid.** TanStack Table 9 provides the column model (12 columns, server-side sorting state, sizing, ordering,
  pinning and visibility features); TanStack Virtual renders rows of a fixed 28 px with an overscan of 12; TanStack Query
  caches pages (`staleTime: Infinity`, `gcTime: 30 s`). Pages in view are fetched as queries, and pages ahead are
  prefetched in the direction of travel: 1 to 8 pages, enough for the next 0.4 s at the current speed. Placeholder rows
  are static (no shimmer). The markup follows the ARIA grid pattern, with a sticky header and a row count. A windowed
  scroll space keeps the real scroll height at 2,000,000 px (see [risks](#risks-and-gotchas)).
- **Live panel.** The sidecar streams progress over SSE every 500 ms, simulating 2,900 pages/s, with a notice every 10 s.
  The UI keeps stream state outside React, publishes it at most 4 times a second, and only the live panel subscribes, so
  progress never re-renders the grid. Counters, a pages/s sparkline (last 5 minutes, 600 points) and a status-class bar
  were built twice, once with ECharts (canvas renderer, tree-shaken) and once with visx (SVG).
- **Driving the page.** Playwright 1.64.1 loads the production build from a local static server on a free port, so the
  page is cross-origin to the sidecar as it is in Tauri. Viewport 1536 × 864 at a device pixel ratio of 1.25 (a 1080p
  laptop at 125%). Scrolling uses `Input.synthesizeScrollGesture` with a mouse-wheel source at three speeds: **normal**
  1,500 px/s (about 54 rows/s, steady wheel scrolling while reading), **fast** 20,000 px/s (about 710 rows/s, a hard
  fling) and **sweep** 50,000 px/s (about 1,800 rows/s, which also visits about 54,000 rows to fill the cache).
- **Frames.** A `requestAnimationFrame` logger records each frame interval, and how many rows inside the viewport were
  placeholders (data not loaded) or blank (no row rendered). A frame is *dropped* when its interval exceeds 1.5 times
  the median interval: at 60 Hz, a frame that missed its 16.7 ms deadline. The headless shell paces frames at 60 Hz
  like a typical laptop panel; a second run in Chromium's new headless mode followed this display's 240 Hz.
- **CPU and memory.** Main-thread busy share is Chromium's own task time (`Performance.getMetrics`, `TaskDuration`)
  over wall time. Memory is the JS heap after a forced GC, plus the private bytes of every browser process (Chromium's
  renderer, GPU, browser and network processes; in Tauri, the host process and all `msedgewebview2.exe` processes).
- **Tauri.** The release build (`tauri build`, no installer) was driven over CDP: WebView2 opens a debugging port when
  started with `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=N`, and Playwright attaches with
  `connectOverCDP`, so the same measurements ran inside the real WebView2 window. The shell writes timings and the
  sidecar's diagnostics as JSON lines to the file named by `SPIKE_LOG`.

## Budgets against measurements

Chromium figures are from the headless shell at 60 Hz with a device pixel ratio of 1.25. WebView2 figures are from the
Tauri release build in a real window on this machine (240 Hz, device pixel ratio 1.5, GPU compositing), where a frame
over 16.7 ms is one a 60 Hz panel would have dropped. "Empty rows" counts placeholder or blank rows inside the viewport
per frame. All grid figures are for 1,000,000 rows with the windowed scroll space.

| Budget | Target | Chromium 1× | Chromium 4× | WebView2 1× | WebView2 4× |
| --- | --- | --- | --- | --- | --- |
| Cold start to a usable home screen | 2 s; shell before sidecar | n/a | n/a | First paint 331 ms (median of 5 warm launches), 515 ms on a first launch with a new WebView2 profile; rows on screen at 474 ms | n/a |
| First page of a grid, from mount | 300 ms | 65–68 ms p50, 70–78 ms p95 (two runs) | 197–253 ms p50, 263–313 ms p95 (two runs) | 77 ms | not measured |
| Scrolling at normal speed (1,500 px/s) | 60 fps, no blank rows | 0% dropped, 0 empty rows | 0.2% dropped, 0 empty rows | 0% over 16.7 ms, 0 empty rows | 0.1% over 16.7 ms, 0 empty rows |
| Fast fling (20,000 px/s), not a budget | | 0% dropped, 0 empty rows | 9.4% dropped, 0 empty rows | 0% over 16.7 ms, 0 empty rows | 95% over 16.7 ms (about 35 fps), 0 empty rows |
| Jump to the middle / the end, to a filled viewport (includes 20–50 ms of query time) | 100 ms (any click or key) | 47 ms / 46 ms; other jumps 66 ms max | 114 ms / 84 ms; others 120 ms max | 40 ms / 47 ms; others 68 ms max | 110 ms / 103 ms; others 125 ms max |
| Live crawl at 2,900 pages/s while the grid scrolls | 60 fps | 0% dropped with ECharts or visx | 0% dropped with either | 0% over 16.7 ms with either | 0.5% over 16.7 ms with either |
| UI memory with a 1M-row crawl open | 300 MB | After a 54,000-row sweep: JS heap 22 MB, renderer 227 MB, all browser processes 285 MB | 25 MB, 259 MB, 318 MB | At start: JS heap 7 MB, all UI processes 267 MB. After the sweep: 21 MB and 455 MB | 25 MB and 446 MB |

Not measured, because they depend on the engine's readers rather than the UI stack: opening a 100k-page crawl, and
sort or filter times.

Other results worth knowing:

- **Sweeps** (50,000 px/s, about 1,800 rows/s) stayed at 60 fps with no empty rows at 1× in both browsers. At 4× they
  dropped to about 20 fps and showed placeholders in about a third of frames in Chromium (2% in WebView2): the main
  thread, not the data, is the limit there.
- **The live panel is cheap.** Idle, with only the stream running, it used 1–2% of the main thread at 1× and 3–7% at
  4× in either library. With the stream forced to 20 events a second, the UI still rendered 3.9–4.0 times a second, as
  designed.
- **The windowed scroll space costs nothing measurable:** with it off (`?window=0`) the same runs gave the same
  results, and with a deliberately small window (400,000 px, re-basing about every 100,000 px of scrolling) there were
  still no dropped frames or empty rows at 1×.
- **The same picture at 240 Hz.** In Chromium's new headless mode, which follows this display's 240 Hz, no frame took
  longer than 16.7 ms at 1× in any scenario, and at 4× normal-speed scrolling had 0.5% of frames over 16.7 ms.
- **Sidecar restart.** In the Tauri build, after the sidecar was killed the shell started a new one (ready 419 ms after
  the kill, including a deliberate 250 ms pause) and the SSE stream was flowing again 477 ms after the kill; grid pages
  loaded from the new sidecar at once.

## The shell and sidecar contract

These are the facts the real sidecar and shell must follow. Values marked *seen* were recorded from real requests.

### Starting the sidecar

- Rust generates a 32-byte token from the OS random generator (hex, 64 characters) for each sidecar launch and passes
  it in the environment (`SI_TOKEN`), never in argv. The sidecar refuses to start without it and removes it from its
  own environment after reading, so its children don't inherit it.
- The sidecar binds `127.0.0.1` on port 0 (Kestrel `Listen(IPAddress.Loopback, 0)`), reads the real port from
  `IServerAddressesFeature` after `StartAsync`, and writes exactly one line to stdout: `{"event":"ready","port":N}`.
  Nothing else ever goes to stdout; diagnostics go to stderr as JSON lines (the shell logs them).
- Rust spawns it with `app.shell().sidecar("spike-sidecar")`. `tauri-plugin-shell` delivers stdout one line per
  `CommandEvent::Stdout` (line mode is the default). The shell parses the ready line, then hands `{port, token,
  generation}` to the webview through a command (`sidecar_connection`, `null` until ready) and the events
  `sidecar://ready` and `sidecar://lost`, sent to the main window only. The webview must subscribe before it asks, or a
  ready event can fall between the two.
- Spawn to ready line took 112–134 ms in the Tauri build (framework-dependent single file, JIT). Tauri's own start-up
  runs first: the setup hook, where the sidecar was spawned, ran 220–240 ms after `main()` began, so the sidecar was
  ready 30–40 ms after the shell's first paint. Spawning it at the top of `main()` would have it ready long before the
  webview, and the first rows would follow the first paint by one page fetch (an estimate: 60–90 ms sooner).
- The webview gets no shell permissions (`core:default` only), so it cannot spawn anything: Rust owns the sidecar.
  App commands are callable from every window by default; the real app should declare permissions for them.
- File naming: `bundle.externalBin: ["binaries/spike-sidecar"]`, with the file at
  `src-tauri/binaries/spike-sidecar-x86_64-pc-windows-msvc.exe` (name, `-`, the Rust host triple, `.exe`). The build
  fails if it is missing. The build copies it next to the app as `spike-sidecar.exe` (triple stripped), even with
  `bundle.active: false`. The other platforms need `-aarch64-apple-darwin`, `-x86_64-apple-darwin` and
  `-x86_64-unknown-linux-gnu`, built from the .NET RIDs `osx-arm64`, `osx-x64` and `linux-x64`.

### Requests from the webview

| | Dev (`tauri dev`, Vite dev server) | Release (`tauri build`, Windows) |
| --- | --- | --- |
| `Origin` (*seen*) | `http://localhost:51873` (the `devUrl`) | `http://tauri.localhost` |
| `Host` (*seen*) | `127.0.0.1:<port>` | `127.0.0.1:<port>` |
| `Sec-Fetch-Site` / `Sec-Fetch-Mode` (*seen*) | `cross-site` / `cors` | `cross-site` / `cors` |
| `Referer` (*seen*) | `http://localhost:51873/` | `http://tauri.localhost/` |

- macOS and Linux release builds use `tauri://localhost` according to Tauri's documentation (not tested here), and
  `https://tauri.localhost` appears on Windows only if `useHttpsScheme` is turned on. The sidecar should accept exactly
  the origin of the build it belongs to, plus the dev server's in debug builds.
- The sidecar rejects any `Host` other than `127.0.0.1:<its port>` (421), which defeats DNS rebinding; any other
  `Origin` (403); and a missing or wrong bearer token (401, constant-time comparison). All four checks were tested.
- **CORS.** Every request carries `Authorization`, so WebView2 sends a preflight first (*seen*): `OPTIONS` with
  `Access-Control-Request-Method: GET` and `Access-Control-Request-Headers: authorization`, and no token. The sidecar
  must answer it without authentication: `204`, `Access-Control-Allow-Origin: <the exact origin>`,
  `Access-Control-Allow-Methods: GET, POST, OPTIONS`, `Access-Control-Allow-Headers: authorization, content-type,
  last-event-id`, `Access-Control-Max-Age: 7200` (Chromium's cap) and `Vary: Origin`. Real responses repeat
  `Access-Control-Allow-Origin` and `Vary`. WebView2 156 sent no `Access-Control-Request-Private-Network` header, so
  Private/Local Network Access did not get in the way of `tauri.localhost` calling `127.0.0.1`. The sidecar still
  answers that header if it ever appears.
- **Preflight cost.** Chromium caches a preflight per URL, so every distinct page URL (`?offset=…`) costs a preflight:
  one per page request, measured in both Chromium and WebView2. Worse, `fetch(…, { cache: 'no-store' })` turns the
  preflight cache off altogether. With POST and a JSON body to one fixed URL, and no `no-store` on the request (the
  sidecar sends `Cache-Control: no-store` instead), a 21-page fling sent no preflights at all. Grid queries should use
  that shape.
- The CSP must allow the random port: `connect-src 'self' ipc: http://ipc.localhost http://127.0.0.1:*` worked in the
  release build.

### Server-sent events

- `EventSource` cannot send an `Authorization` header, so the UI reads the stream with `fetch` and a small parser (about
  60 lines: `event`, `data`, `id`, comments, any line ending). WebView2 delivered the events, with the bearer header, at
  the rate they were sent (about 2 a second).
- Reconnect: the client aborts a stream that has been silent for 5 s, backs off from 250 ms to 4 s, and a new connection
  (`sidecar://ready` with a new port and token) ends the backoff at once. In the browser bench, a hard-killed sidecar was
  noticed in 10–11 ms and the stream was flowing again from its replacement 165–253 ms after the kill (100–150 ms of
  that is the new sidecar starting). In the Tauri build the shell noticed the crash, waited 250 ms, started a new
  sidecar (ready 419 ms after the kill) and the stream was flowing again 477 ms after the kill.
- Event ids restart with each sidecar process, so the client drops `Last-Event-ID` when the generation changes. The real
  engine should either make ids unique across restarts or not rely on them.
- Kestrel without TLS speaks HTTP/1.1 to browsers, and Chromium allows 6 connections per host and port. An open SSE
  stream holds one of them for good, so the real UI should use one multiplexed stream for all live events.

### Lifecycle

Tested on the release build by closing the window (`WM_CLOSE`) or killing the process (`TerminateProcess`, as Task
Manager does):

| Case | Result |
| --- | --- |
| Normal close, no cleanup code | Sidecar left running (orphan) |
| Normal close, `child.kill()` on `RunEvent::Exit` | Sidecar gone with the app (262 ms after `WM_CLOSE`), exit code 1 |
| Normal close, `shutdown` written to the sidecar's stdin, kill after 2 s | Graceful stop in 22 ms, exit code 0 |
| Hard kill, no guard | Sidecar left running (orphan) |
| Hard kill, sidecar watches its parent PID (`SI_PARENT_PID`) | Sidecar gone 34 ms after the kill |
| Hard kill, sidecar stops on stdin EOF | Gone after 30 ms |
| Hard kill, Windows job object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` | Gone after 11 ms (killed by the kernel) |
| WebView2 processes, all cases | None left behind |

`tauri-plugin-shell` 2.6.0 kills only processes spawned from the JavaScript API on exit, which is why a Rust-spawned
sidecar is orphaned by default. Its new `set_process_group` uses a job object on Windows, but without
`KILL_ON_JOB_CLOSE`, so it does not help with a hard kill. The fix the real shell should use: stdin is the lifeline.
On a normal exit the shell writes `shutdown` and waits up to 2 s before killing; when the shell dies, stdin reaches EOF
and the sidecar stops gracefully (on every OS, with no PID-reuse risk). The parent-PID watch is a second guard, and on
Windows the job object is the kernel-level backstop for a hung sidecar. The job kill is abrupt, which the engine
already survives because an interrupted crawl resumes.

## Recommendations

### Grid: keep TanStack, add a windowed scroll layer

- **TanStack Table 9** for the column model: sizing, ordering, pinning, visibility, multi-sort and filter state, all
  manual (server-side). Feed it only the rows of the pages in view; it never sees 1M rows.
- **Row virtualisation for the 1M-row grid: our own fixed-height virtualiser over a windowed scroll space**, about
  150 lines, rather than TanStack Virtual with patched internals. With a fixed row height the range maths is one
  division; TanStack Virtual's strengths (measuring variable heights, scroll anchoring, reconciliation) are not needed
  there, and its internals had to be patched to make the windowed space work. Keep TanStack Virtual for the
  variable-height lists (inspector panels, grouped issues). If we would rather not own this, the alternative is to ask
  TanStack for public hooks for the scroll extent and position read-back (two functions); the spike shows the rest
  works.
- **TanStack Query** for the page cache, with `staleTime: Infinity` for a finished crawl, a short `gcTime` (30 s kept the
  cache to about 270 pages, about 22 MB of JS heap after a 54,000-row sweep), queries for the pages in view and
  `prefetchQuery` for pages ahead, scaled by scroll speed. For a crawl that is still running, invalidate pages on a
  progress tick instead of polling.
- **Grid requests as POST to one URL** (`/api/urls/query` with offset, limit, sort and filters in the body), so a
  session pays for one preflight instead of one per page. Pages of 200 rows worked well (other sizes were not tried):
  one or two requests cover a viewport, and a fling needs at most 8 in flight.
- **Glide Data Grid is not needed.** TanStack met the scrolling budgets at normal speed even at 4× throttling, so the
  canvas grid was not tried. Choosing it would mean rebuilding what the brief requires and the DOM gives for free: the ARIA
  grid pattern, the visible focus ring, forced-colours mode, text selection and copy, and zoom (speculative: based on
  how canvas grids work, not measured here).

### Charts: visx for the live panel

Both libraries kept the UI at 60 fps with live updates while the grid scrolled, at 1× and 4×, so the choice rests on
other grounds, all in favour of visx:

- **Size:** the visx chunk was 37 KB (14 KB gzipped) against 495 KB (168 KB gzipped) for ECharts with only the line and
  bar charts, the grid and aria components and the canvas renderer.
- **Accessibility:** visx draws SVG in the DOM, so text alternatives are ordinary markup, and Windows high-contrast
  (forced-colours) mode can restyle it. ECharts draws on a canvas: its `aria` option adds a generated description and
  decal patterns, but nothing inside the chart is in the accessibility tree and forced-colours mode cannot reach it.
- **Theming:** visx takes colours from the design tokens through CSS (`fill: var(--s2xx)`); ECharts needs the token
  values read in JavaScript and the chart rebuilt on every theme switch.
- **Cost:** the same, within noise. Idle with only the stream running, ECharts took 1.0% of the main thread at 1× and
  4.4% at 4× in Chromium (2.0% and 6.4% in WebView2), visx 0.9% and 3.6% (1.5% and 6.1%). Neither dropped a frame
  while the grid scrolled at normal speed. During flings at 4× every variant dropped frames, with no consistent
  difference between the libraries across runs.
- **When canvas would win:** tens of thousands of points redrawn often, such as a scatter of response time against size
  for every URL. If a view like that is wanted later, load ECharts (or a canvas layer) lazily for that view only.

### The rest of the library list

- No change to the brief's list: Vite (8 with Rolldown built the spike in about 0.2 s), TanStack Router (not exercised),
  Query, Table, Radix, Tailwind, cmdk, CodeMirror 6, Sonner and Vitest were not contradicted by anything measured.
- Add: our own fixed-height virtualiser with a windowed scroll space and a custom scrollbar for the URL-scale grids,
  and a small SSE-over-fetch client (or the `eventsource-parser` package) instead of `EventSource`.
- TypeScript 7 (the native compiler) type-checked the spike without problems.
- The sidecar's packaging is still open: the spike used a framework-dependent single file (needs the .NET runtime;
  ready in 100–150 ms). Self-contained adds the runtime to the download; Native AOT would start faster but needs the
  engine (SQLite, Playwright) to be AOT-safe. Decide when the real sidecar is built.

## Risks and gotchas

1. **A 1M-row grid cannot use a 1:1 scroll space.** 1,000,000 rows of 28 px is 28,000,000 px. Chromium clamps any
   scroll extent at 33,554,428 px (measured), so 1.2M rows, or 1M rows taller than 33 px, simply cannot be reached.
   Worse, the compositor works in float32: past about 16.7M device pixels (row 480,000 at 125% scaling), positions
   are rounded to 2 and then 4 device pixels. Screenshots showed rows unevenly spaced, cells in the same row on
   different baselines, and, at the end of the list, part of a row showing above the sticky header, while the DOM
   geometry reported perfect positions, so no DOM-based test would catch it. The spike fixed it with a windowed scroll
   space (`app/spike/src/grid/scrollWindow.ts`): the real scroll height is at most 2,000,000 px and maps onto the virtual offset through a base that moves (in
   whole rows, in the same task) when the scroll position nears an edge of the window. After the fix the end of the list
   renders exactly at 100%, 125% and 150% scaling. The windowed space needs a custom vertical scrollbar, because the
   native thumb now shows the position within the window (not built in the spike).
2. **TanStack Virtual has no supported hook for a virtual scroll space.** Its `observeElementOffset` and `scrollToFn`
   options are public, but it also reads the DOM scroll position and height directly to clamp targets and reconcile
   scrolls. The spike replaced two private members (`_elementMaxScrollOffset`, `_readElementOffset`). That works but
   relies on private names that can change in any release. A first version also missed jumps that left `scrollTop`
   unchanged (no scroll event fires), so the window must notify the virtualiser itself after re-basing.
3. **One preflight per page request** unless grid queries use one fixed URL (see the contract). `cache: 'no-store'` on
   a request silently disables Chromium's preflight cache.
4. **First page at 4× is at the edge of the budget.** At 4× the first page took 197–253 ms at p50 and 263–313 ms at
   p95 from mount across two runs (417–555 ms from navigation; the higher figures are from the run made before the load
   check): the 20–50 ms query, a preflight, and React mounting the shell and grid four times slower. The real app will
   carry more code on its first route, so keep that route's chunk small (the chart libraries were lazy-loaded here) and
   use the POST shape to drop the preflight.
5. **UI memory is over the 300 MB budget if the budget counts every WebView2 process.** In the Tauri build the UI's
   processes (host, WebView2 browser, GPU, renderer and utilities) held 267 MB private at start, before any scrolling,
   and 455 MB after a 54,000-row sweep; the JS heap was 7 MB and 21 MB. On this machine the GPU process alone is about
   150 MB (an RTX 4090 driving a 4K display); a laptop's will differ. The growth is in the renderer (62 MB to 240 MB),
   and it is neither the JS heap nor the page cache: a critical memory-pressure signal returned almost none of it;
   dropping the page cache (after the 30 s `gcTime`) lowered the JS heap to 13 MB but left the renderer at about
   180 MB; and recycling row elements instead of mounting new ones made no difference. Sweeping further, new parts of
   the list added 31–42 MB each time (renderer 218, 259, 290 and 324 MB after 50k, 100k, 150k and 200k rows) plus
   about 3.5 MB of JS heap per 100,000 rows (small objects holding numbers, not diagnosed). Step 3 should agree what
   the budget measures (the renderer, or everything, in which case WebView2's 267 MB baseline leaves little room) and
   profile the renderer's growth with Chromium's memory-infra tracing before the grid is built for real.
6. **Live updates must stay out of the grid's React tree.** Holding stream state in a store outside React and letting
   only the live panel subscribe is what keeps a 2,900 pages/s crawl from re-rendering 60 rows twice a second. A naive
   `useState` at app level would re-render the grid on every event.
7. **Rust picks the wrong MSVC on this machine.** `rustc` chose Visual Studio 18 Community, which has no C++ libraries
   installed, and linking failed with `LNK1104: cannot open file 'msvcrt.lib'`. Building inside the VS 2019 Build Tools
   environment (`app/spike/scripts/with-msvc.ps1`) works. Developer machines and CI need one known MSVC toolset.
8. **Supply chain.** Two of the latest versions pinned here (`@tanstack/react-virtual` 3.14.25, `@tanstack/react-query`
   5.105.1) were published on the day of the spike. The real project should pin versions at least two weeks old.
   TanStack Table 9 is three months old (9.0.0 shipped on 4 August 2026), with a new API (`useTable`, `tableFeatures`)
   and fewer examples than v8; it ships agent skill files in the package, which helped.
9. **The token is not secret from the same user.** Any process running as the same user can read another process's
   environment and memory. The token stops web pages and other users, not local malware; that is the right bar for a
   desktop app, but don't log it.
10. **Windows timers.** `Task.Delay` rounds up to the 15.6 ms system tick unless the process raises the timer
    resolution. It only mattered for the dummy latency here, but anything timing-sensitive in the engine on Windows
    should know it.
11. **Shared Playwright cache.** `npx playwright install chromium` deleted older browser builds it considered unused
    from the per-user `ms-playwright` cache (`chromium-1247`, `chromium_headless_shell-1247`, `webkit-2368`). Another
    project on this machine that still needs them will download them again.

## Rerunning the spike

From `app/spike/` (Windows, with the toolchain above):

```powershell
npm install
npx playwright install chromium
dotnet publish sidecar -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o sidecar/bin/publish
Copy-Item sidecar/bin/publish/spike-sidecar.exe src-tauri/binaries/spike-sidecar-x86_64-pc-windows-msvc.exe
npm run build                                   # type-check (TypeScript 7) and Vite build
node bench/smoke.mjs                            # sidecar guards and endpoints, no browser
node bench/run.mjs --rates 1,4                  # Chromium bench (add --new-headless for the display's refresh rate)
node bench/precision.mjs                        # row positions deep in the list (add window=0 to see the bug)
powershell -ExecutionPolicy Bypass -File scripts/with-msvc.ps1 npx tauri build
node bench/tauri.mjs all                        # cold start (resets the spike's WebView2 profile), lifecycle, reconnect, WebView2 runs
powershell -ExecutionPolicy Bypass -File scripts/dev-origin.ps1   # the Origin seen under `tauri dev`
```

Results land in `app/spike/results/` (git-ignored). The page takes `?charts=visx|echarts|none`, `?live=0`,
`?fetch=post`, `?sseInterval=50` and `?window=0` to switch variants.

Where to look: the shell is `src-tauri/src/main.rs` (spawn, ready line, restart, exit modes, job object); the dummy
sidecar is `sidecar/Program.cs` (guards, CORS, pages, SSE, parent and stdin watches); the grid is
`src/grid/UrlGrid.tsx` with `src/grid/scrollWindow.ts`; the SSE client and batching are `src/live/sse.ts` and
`src/live/liveStore.ts`; the measurements are in `bench/` and `src/perf/probe.ts`.
