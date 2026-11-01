# v2 sidecar API

The engine as a localhost HTTP API for the desktop UI. This is the contract between `app/` and
`engine/src/ShoutingIguana.Sidecar`, so both can be built at once. The app and the sidecar ship together, so the
API has no version beyond `/api/`.

Rules from [the rebuild doc's handoff](v2-rebuild.md#rules-the-ui-must-keep) still hold:

- one writer per crawl file;
- indexability as the analysis judged it;
- skipped checks are never shown as 0;
- secrets are asked for again on resume;
- nothing outside `Storage/` writes to a crawl file.

The sidecar's own state (the library list, the user's defaults) is not a crawl file, and lives in its state folder.

Built in stages:

- **E1** is marked below. It is the process, security, library, crawl control, live events, analysis, and the
  readers that exist today.
- **E2** is marked below too: the URL summary the grids read, and the analysis's own state (partial or not, why each
  check was skipped, whether it's out of date).
- **E3–E6** add the grids, export, search and the favicon. They come from the brief's
  [engine work the UI needs](v2-ui-brief.md#engine-work-the-ui-needs) and the handoff's
  [what's missing](v2-rebuild.md#whats-missing).

## Process (E1)

- **Start.** The Tauri shell starts `si-sidecar --data-dir <folder> --state-dir <folder>`.
  - `--data-dir` is where new crawls are saved. It defaults to `Documents/Shouting Iguana`.
  - `--state-dir` holds `library.json`, the user's defaults and the logs. It defaults to
    `<local app data>/ShoutingIguana`.
  The shell also passes `--stdin-lifeline`, described under Shutdown. `--port` fixes the port, for development; it
  defaults to 0, a free port.
- **Environment.**
  - `SI_SIDECAR_TOKEN` is the bearer token: 32 random bytes as hex, made by the shell for each launch. The sidecar
    removes it from its own environment after reading it, so processes it starts don't inherit it. Without it, the
    sidecar makes one up and includes it in the ready line, for development only.
  - `SI_SIDECAR_ORIGINS` is a comma-separated list of the origins allowed to call the API. The shell passes exactly
    its own webview's origin, plus the dev server's in debug builds. [The spike](v2-ui-spike.md) measured these:

    | Build | Origin |
    | --- | --- |
    | Windows release | `http://tauri.localhost` |
    | macOS and Linux release | `tauri://localhost` (from Tauri's documentation) |
    | `tauri dev` | `http://localhost:<dev server port>` |

  - `SI_PARENT_PID` is the shell's process. When it ends, the sidecar shuts down as below. It is the second guard,
    after stdin.
- **Ready line.** The sidecar binds `127.0.0.1` on a free port. When it's ready to serve, it writes exactly one line
  to stdout:

  ```json
  {"event":"ready","port":53123,"version":"2.0.0-alpha.1"}
  ```

  `"token"` is added only when the sidecar made one up. Nothing else ever goes to stdout: logs go to stderr and to
  `<state-dir>/logs`.
- **Shutdown.** The shell can't rely on killing the sidecar: `tauri-plugin-shell` leaves a sidecar it started from
  Rust running after a normal exit, and after a hard kill of the shell. So stdin is the lifeline. With
  `--stdin-lifeline`, a `shutdown` line on stdin, or stdin reaching its end because the shell died, shuts the sidecar
  down. It is opt-in, so a sidecar started any other way with no stdin doesn't stop at once. On Windows the shell
  also puts the sidecar in a job object that kills it when the shell's last handle closes, as a backstop for a
  sidecar that hangs.

  The sidecar shuts down on any of: a `shutdown` line or the end of stdin (with the lifeline), `POST /api/shutdown`,
  Ctrl+C, SIGTERM, or the parent process ending. Then:
  - every running crawl is stopped, and its file stays resumable;
  - every running analysis is cancelled, and stores nothing;
  - the process exits with code 0.

## Security (E1)

- **Token.** Every request needs `Authorization: Bearer <token>`, compared in constant time; otherwise 401. CORS
  preflights (`OPTIONS`) are the exception: they carry no credentials, and are answered only for allowed origins.
- **Host.** `Host` must be `127.0.0.1:<port>` or `localhost:<port>`, with the bound port; otherwise 421. This
  defeats DNS rebinding.
- **Origin.** When an `Origin` is sent, it must be on the allowlist; otherwise 403.
- **CORS.** Every call carries `Authorization`, so WebView2 sends a preflight before each one.
  - A preflight from an allowed origin gets `204` with:
    - `Access-Control-Allow-Origin: <the exact origin>`;
    - `Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS`;
    - `Access-Control-Allow-Headers: authorization, content-type, last-event-id`;
    - `Access-Control-Max-Age: 7200`, Chromium's cap;
    - `Vary: Origin`.
  - If the preflight asks with `Access-Control-Request-Private-Network: true`, the reply adds
    `Access-Control-Allow-Private-Network: true`.
  - Real responses repeat `Access-Control-Allow-Origin` and `Vary: Origin`, and carry `Cache-Control: no-store`.
  - No cookies and no credentials mode.
  - Chromium caches a preflight per URL, so query strings that change every call (paging) cost a preflight each. The
    grid queries (E3) are therefore `POST`s with a JSON body to one fixed URL per grid. The UI never sets
    `cache: 'no-store'` on a request, because that turns the preflight cache off.
- **Bodies.** A request body is at most 1 MB.
- **Paths from the UI** (open, add to library) must be absolute and end in `.sicrawl`. The file is checked to be a
  crawl before it's added.
- **Crawled text** (titles, anchor text, alert messages, details) comes back exactly as stored. The UI renders it as
  text, never as HTML.

## Conventions (E1)

- **JSON.**
  - camelCase.
  - Enums as camelCase strings (`"running"`, `"error"`).
  - Times in ISO 8601 UTC, durations in milliseconds (`elapsedMs`), sizes in bytes.
  - Spellings follow the engine (`analyze`, `analyzedAt`). The UI's text uses UK English.
- **Crawl ids.** 16 hex characters of the SHA-256 of the file's full path (case-folded on Windows and macOS). An id
  is stable while the file stays where it is.
- **Errors.** `application/problem+json`:

  ```json
  {"type":"crawl-busy","title":"This crawl is open in another process.","status":409,"detail":"…","errors":{"maxUrls":["…"]}}
  ```

  - `title` is a plain sentence the UI shows as it is. `detail` sits behind "Details". `errors` holds field-keyed
    validation messages.
  - Types:

    | Type | Status | When |
    | --- | --- | --- |
    | `invalid-options` | 422 | Validation failed |
    | `needs-secrets` | 422 | A secret must be given again |
    | `not-a-crawl` | 400 | The file isn't a crawl. SQLite error 26 means this, as in the CLI |
    | `crawl-not-found` | 404 | No crawl has this id |
    | `crawl-busy` | 409 | Running here or elsewhere, or an analysis is running |
    | `not-analysable` | 409 | The engine refuses: running or interrupted |
    | `file-error` | 400 | IO or permissions |
    | `not-analyzed` | 404 | The crawl has never been analysed |
    | `needs-analysis` | 409 | A grid needs the URL summary, which an earlier engine's analysis doesn't have (E3) |
    | `not-found`, `check-not-found` | 404 | No such route, or no check with this id |
    | `bad-request` | 400 | Malformed JSON, or a property the API doesn't know |
    | `unauthorized` | 401 | The token is missing or wrong |
    | `origin-not-allowed` | 403 | An `Origin` not on the allowlist |
    | `body-too-large` | 413 | A body over 1 MB |
    | `bad-host` | 421 | A `Host` other than `127.0.0.1` or `localhost` with the bound port |
    | `internal` | 500 | Anything else: the title is generic, and the detail is the exception message, never a stack trace |

  - An option value of the wrong type is a field error under `invalid-options`, not a 400.

  - The expected exceptions are the CLI's set (`CommandErrors`): `InvalidOperationException`, `SqliteException`,
    `IOException`, `UnauthorizedAccessException`, and `ArgumentException` for a path. Their messages are already
    written for users.

## Endpoints

### App (E1)

| Method and path | Returns |
| --- | --- |
| `GET /api/health` | `{ version, engineVersion, dataDir, stateDir, pid }` |
| `POST /api/shutdown` | 202, then the shutdown described above |

### Checks (E1)

`GET /api/checks` returns two lists:

- `categories: [{ id, title }]`, where the title is a plain name such as "Meta descriptions";
- `checks: [{ id, category, severity, title, description, fix }]`, in catalog order.

Both come from `IssueCatalog`. The engine gets public category titles, so the UI hard-codes none.

### Crawl options (E1)

| Method and path | Does |
| --- | --- |
| `GET /api/crawl-options/defaults` | `{ options, speedPresets }`: the user's saved defaults, or the engine's |
| `PUT /api/crawl-options/defaults` | Saves "my defaults". The start URL, header values and proxy credentials are never saved: the UI keeps secrets in the OS credential store, with the user's consent |
| `POST /api/crawl-options/validate` | `{ errors: { <field>: [messages] } }`, always 200. Field names are the JSON names of `CrawlOptions` properties. The engine gets a field-keyed `Validate`, and the CLI keeps its list of messages |

The options DTO has every public `CrawlOptions` property. `extraHeaders` is an object of name to value, and
`trustedCertificate` is never exposed.

`speedPresets` sets concurrency and a rate. These values are provisional until the IA sign-off:

| Preset | Concurrency | Rate |
| --- | --- | --- |
| `polite` | 2 | 2/s |
| `standard` | 5 | no cap, with automatic backoff (the engine's defaults) |
| `fast` | 16 | no cap |

### Library (E1)

`GET /api/crawls` returns `[CrawlListItem]`, newest first. It lists the `.sicrawl` files at the top of the data
folder, plus files opened from elsewhere, minus the ones removed from the library.

```json
{"id":"…","path":"C:\\…\\example.com-20261101-101500.sicrawl","fileName":"…","site":"example.com",
 "startUrl":"https://www.example.com/","status":"completed","startedAt":"…","finishedAt":"…","updatedAt":"…",
 "urlCount":4521,"pageCount":265,"issues":{"error":3,"warning":12,"notice":7},"analyzedAt":"…",
 "outOfDate":false,"sizeBytes":18276352,"missing":false}
```

- **`site`** is the host without a leading `www.`. The UI groups crawls by it.
- **`urlCount`** is the URLs fetched. URLs found but not fetched aren't counted. **`pageCount`** is the pages, as in
  `si stats`.
- **`status`** is one of:

  | Status | Meaning |
  | --- | --- |
  | `running`, `paused`, `stopping` | Live, in this process |
  | `analyzing` | An analysis is running, in this process |
  | `stopped` | Stopped, resumable |
  | `interrupted` | The file says running, but nobody holds its lock. The engine gets a public lock probe for this |
  | `completed` | Finished |
  | `failed` | Failed |
  | `inUse` | Running in another process |

- **`issues`** counts checks with at least one issue, by severity: the Issues screen's rows, not issue rows. It is
  null before the first analysis.
- **`outOfDate`** (E2) is true when the last analysis no longer stands, and false before the first one. That's when:
  - the crawl changed after it: the crawl row's `updated_at` isn't the one the analysis kept, because the crawl was
    continued, here or elsewhere, or is running here;
  - this engine would judge the crawl differently: an engine with another analysis version or other checks ran it,
    as every engine from before E2 did;
  - or the analysis ran with other thresholds than the sidecar's.

  `GET /api/crawls/{id}/analysis` gives the reasons.
- **Caching.** Each file's summary is read once and kept until the file's size or modified time changes, so the
  list stays cheap with many crawls. The engine gets a cheap summary reader; `CrawlStatsReader.Read` counts every
  link, which is too slow for a list.

| Method and path | Does |
| --- | --- |
| `GET /api/crawls/{id}` | One crawl's item |
| `POST /api/crawls/open` | Body `{ path }`. Adds a crawl file from anywhere and returns its item. Gives `not-a-crawl` for anything else |
| `DELETE /api/crawls/{id}` | Removes the crawl from the library, not from disk. Gives `crawl-busy` while live. Undone by `POST /api/crawls/{id}/restore` |
| `POST /api/crawls/{id}/delete-file` | Deletes the file, with its `-wal`, `-shm` and `-lock`. The UI sends it only after a confirmation that names the crawl. Refused while live or in use |

### Crawl control (E1)

`POST /api/crawls` takes `{ options, analyzeWhenDone = true }`:

- Every options field is optional: one left out takes the engine's default.
- It validates the options and gives `invalid-options` with field errors on failure.
- It creates `<host>-yyyyMMdd-HHmmss.sicrawl` in the data folder, using the CLI's naming, which moves into the
  engine.
- It starts the crawl and returns 201 with the item.
- `CrawlEngine.Start` runs off the request thread: resuming reads every URL into memory.
- When the crawl completes, it's analysed if `analyzeWhenDone` is set. Stopped crawls aren't analysed
  automatically.

| Method and path | Does |
| --- | --- |
| `POST /api/crawls/{id}/pause` | 204, or `crawl-busy` if the crawl isn't live here |
| `POST /api/crawls/{id}/resume` | 204, or `crawl-busy` if the crawl isn't live here |
| `POST /api/crawls/{id}/stop` | 202. The status goes to `stopping`, then `stopped` |
| `GET /api/crawls/{id}/options` | The stored options, from `CrawlDatabase.ReadOptions`: header names with empty values, and `hasRedactedProxyCredentials` |

`POST /api/crawls/{id}/continue` resumes a crawl from its file: any crawl that isn't live and whose file isn't in
use, as `si crawl --resume` allows. It's analysed when it completes.

- **Body.** `{ concurrency?, maxRequestsPerSecond?, requestTimeoutSeconds?, extraHeaders?, proxyUrl?,
  skipMissingHeaders = false }`.
- **What can change.** Only these fields, as with the CLI: a resumed crawl keeps the scope and rules it started
  with. The engine gets one helper that applies resume changes, and the CLI uses it too.
- **`needs-secrets`.** The call gives this with `{ missingHeaders: [names], proxyCredentials: bool }` when:
  - the proxy had credentials and no `proxyUrl` was given; or
  - stored header names weren't given values and `skipMissingHeaders` is false.

  The UI then asks for the missing secrets.

### Live events (E1)

`GET /api/events` is a `text/event-stream`. The UI reads it with `fetch`, since `EventSource` can't send the token.

- **First.** A `hello` event, `{ live: [crawlIds] }`, followed by the latest `progress` for each live crawl. A
  reconnecting UI is up to date at once.
- **Replay.** None. Events carry no ids, and `Last-Event-ID` is ignored. Alerts can be fetched with
  `GET /api/crawls/{id}/alerts`.
- **One stream.** Kestrel without TLS speaks HTTP/1.1, and Chromium allows 6 connections per host and port. An open
  stream holds one for good, so every live event shares this one stream.
- **Keep-alive.** A comment line every 15 s.
- **Slow clients.** Each client has a bounded queue. Progress is merged per crawl when a client falls behind; other
  events are never dropped. A client that stays 1,000 events behind is disconnected, and reconnects.

| Event | Data | When |
| --- | --- | --- |
| `progress` | `{ crawlId, status, elapsedMs, discovered, queued, inFlight, fetched, htmlPages, redirects, errors, blocked, robotsDisallowed, skipped, bytesDownloaded, pagesPerSecond }` | Every 500 ms per live crawl (the engine's pace). The UI batches to 4 renders a second |
| `alert` | `{ crawlId, level, code, message, at }` | Each `CrawlNotice` (see below) |
| `crawl` | `{ crawlId, status, error? }` | Every status change, with the library's statuses. An analysis shows as `analyzing`, then back to `completed` or `stopped` with a `library` event, and `error` if it failed |
| `library` | `{}` | The library changed. The UI fetches `/api/crawls` again |
| `analysis` | `{ crawlId, phase, done, total }` | E4 |

- **`level`** is `info`, `warning` or `error`.
- **`code`** is stable; key UI text and actions on it: `blocked`, `mostly-forbidden`, `start-redirects-away`,
  `robots-unreachable`, `site-unreachable`, `site-down`, `sitemaps`, `sitemaps-not-read`, `sitemaps-failed`.
- **`message`** is engine text and may include text from the site.

`GET /api/crawls/{id}/alerts` returns the alerts since the sidecar started, oldest first, up to the last 1,000 per
crawl. E4 persists them in the crawl file.

### Analysis (E1)

| Method and path | Does |
| --- | --- |
| `POST /api/crawls/{id}/analyze` | 202, with progress through events. Gives `not-analysable` while running or interrupted, in the sidecar's words (the engine's mention the CLI), and `crawl-busy` while an analysis already runs |
| `POST /api/crawls/{id}/analyze/cancel` | 202. Nothing is stored |
| `GET /api/crawls/{id}/analysis` | The last analysis, below. 404 `not-analyzed` if it never ran |

```json
{"analyzedAt":"…","engineVersion":"…","pagesAnalyzed":265,"durationMs":7012,"isPartial":true,"outOfDate":false,
 "outOfDateReasons":[],"notes":["…"],"counts":[{"checkId":"title-too-long","count":12}],
 "skipped":[{"checkId":"orphan-page","reason":{"code":"no-sitemap","text":"No sitemap could be read."}}]}
```

- **`isPartial`** (E2) is true when a check was skipped or a rule failed on some pages; the notes say which.
- **`skipped`** is every catalog check missing from the counts. Its `reason` (E2) has a stable `code` to key on, and
  a `text` to show beside the check ("No sitemap could be read."). A check skipped for several reasons gets the
  first; the notes give them all.
- **`outOfDate`** and **`outOfDateReasons`** (E2) are as the library item's: `crawlChanged`, `engineChanged`,
  `settingsChanged`. The UI offers to analyse again.
- **An earlier engine's analysis** never fails to read. Its `isPartial` and every `reason` are null, as it didn't
  store them, so the UI shows the notes. It's out of date with `engineChanged`, and analysing it again mends it.

| Skip reason | The check was skipped because |
| --- | --- |
| `sitemaps-off` | The crawl was told not to read sitemaps |
| `crawl-unfinished` | The crawl was stopped before it finished |
| `sitemaps-unfinished` | The crawl finished, but not every sitemap was read |
| `no-sitemap` | No sitemap could be read |
| `sitemap-unreadable` | A declared sitemap failed for a reason that says nothing about what it lists (a 5xx, a timeout, bad XML) |
| `sitemaps-not-read` | Sitemaps were left unread at the crawler's limits |
| `depth-limit`, `url-limit` | The limit kept internal URLs out of the crawl, so pages linked only from them would look orphaned |
| `start-folder` | The crawl stayed in its start folder, so links from the rest of the site weren't read |
| `external-links-off` | The crawl was told not to check links to other sites |
| `resources-off` | The crawl was told not to fetch images and other files |
| `site-versions-unchecked` | The site's other versions (www or not, http or https) weren't all checked |
| `newer-than-analysis` | The check is newer than the engine that ran the analysis |

### Data (E1: the readers that exist today)

| Method and path | Returns |
| --- | --- |
| `GET /api/crawls/{id}/stats` | `CrawlStatsReader.Read` |
| `GET /api/crawls/{id}/issues/{checkId}?limit=200&offset=0` | `{ rows: [{ url, detail, relatedUrl }], total }`. `total` is null for a check that was skipped, never 0 |

E3 replaces the issues endpoint with keyset paging and grouping. Its grid queries are `POST`s to one fixed URL
per grid (see CORS above). The URL grids read the URL summary each analysis builds (E2, `url_summary` in
[the engine's README](../engine/README.md#url-summary)): one row per URL, with indexes for the common sorts and
selective filters. A crawl without one, analysed by an earlier engine, gets `needs-analysis` rather than failing, and
the UI offers to analyse it. E3 adds:

- `/urls`;
- `/urls/{urlId}`, the inspector;
- `/links`;
- `/sitemaps`;
- `/robots`;
- `/issues/{checkId}/groups`;
- `/search` (E6);
- `/export` (E5).

## Engine changes E1 needs

Each has its own tests:

- A field-keyed `CrawlOptions` validation. The CLI keeps `Validate()`.
- Public display titles for `IssueCategory`.
- A public lock probe: is a crawl file in use by any process?
- A cheap crawl summary reader in `Storage/`: start URL, status, timestamps, URL and page counts.
- The default file name, moved from the CLI's `CrawlCommand` into the engine.
- One helper for resume changes and missing secrets, used by the CLI and the sidecar.

## Engine changes E2 made

Each has its own tests:

- **The URL summary.** `url_summary`, rebuilt by every analysis, and `UrlSummaryReader`: `IsAvailable` and
  `ReadAll` (rows as `UrlSummary`). Its indexability is the checks' own, which the end-to-end tests prove on the
  synthetic site.
- **The analysis's state.** `AnalysisInfo` gains `IsPartial`, `Settings`, `CrawlUpdatedAt`, `AnalysisVersion`,
  `ChecksFingerprint` and `HasUrlSummary`. `IssueReader.ReadSkippedChecks` gives each skipped check's reason
  (`SkippedCheck`, codes in `SkippedCheckReasons`), and `AnalysisSummary.Skipped` the same from `AnalyzeAsync`.
- **Out of date.** `AnalysisFreshness.Check`, and `CrawlFileSummary.OutOfDate` on the library's cached summary
  (which now carries the `Analysis`). The sidecar's analysis settings are `CrawlManager.AnalysisSettings`: the
  engine's defaults until Settings can change them.
- **Files from earlier engines** read as before. They get the new tables and columns on their next analysis; the
  crawl's own schema version is unchanged.
