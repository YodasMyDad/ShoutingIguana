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
- **E2–E6** add the grids, export, search and the favicon. They come from the brief's
  [engine work the UI needs](v2-ui-brief.md#engine-work-the-ui-needs) and the handoff's
  [what's missing](v2-rebuild.md#whats-missing).

## Process (E1)

- **Start.** The Tauri shell starts `si-sidecar --data-dir <folder> --state-dir <folder>`.
  - `--data-dir` is where new crawls are saved. It defaults to `Documents/Shouting Iguana`.
  - `--state-dir` holds `library.json`, the user's defaults and the logs. It defaults to
    `<local app data>/ShoutingIguana`.
  The shell also passes `--stdin-lifeline`, described under Shutdown.
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
    | `internal` | 500 | Anything else: the title is generic, and the detail is the exception message, never a stack trace |

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
- **`outOfDate`** is true when the crawl changed after its last analysis. E2 makes it exact; E1 compares the crawl's
  `updated_at` with `analyzed_at`.
- **Caching.** Each file's summary is read once and kept until the file's size or modified time changes, so the
  list stays cheap with many crawls. The engine gets a cheap summary reader; `CrawlStatsReader.Read` counts every
  link, which is too slow for a list.

| Method and path | Does |
| --- | --- |
| `POST /api/crawls/open` | Body `{ path }`. Adds a crawl file from anywhere and returns its item. Gives `not-a-crawl` for anything else |
| `DELETE /api/crawls/{id}` | Removes the crawl from the library, not from disk. Gives `crawl-busy` while live. Undone by `POST /api/crawls/{id}/restore` |
| `POST /api/crawls/{id}/delete-file` | Deletes the file, with its `-wal`, `-shm` and `-lock`. The UI sends it only after a confirmation that names the crawl. Refused while live or in use |

### Crawl control (E1)

`POST /api/crawls` takes `{ options, analyzeWhenDone = true }`:

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

`POST /api/crawls/{id}/continue` resumes a stopped or interrupted crawl from its file.

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
| `crawl` | `{ crawlId, status, error? }` | Every status change: started, paused, resumed, stopping, stopped, completed, failed, analyzing, analyzed |
| `library` | `{}` | The library changed. The UI fetches `/api/crawls` again |
| `analysis` | `{ crawlId, phase, done, total }` | E4 |

- **`level`** is `info`, `warning` or `error`.
- **`code`** is stable; key UI text and actions on it: `blocked`, `mostly-forbidden`, `start-redirects-away`,
  `robots-unreachable`, `site-unreachable`, `site-down`, `sitemaps`, `sitemaps-not-read`, `sitemaps-failed`.
- **`message`** is engine text and may include text from the site.

`GET /api/crawls/{id}/alerts` returns the alerts since the sidecar started, oldest first. E4 persists them in the
crawl file.

### Analysis (E1)

| Method and path | Does |
| --- | --- |
| `POST /api/crawls/{id}/analyze` | 202, with progress through events. Gives `not-analysable` while running or interrupted, in the engine's words, and `crawl-busy` while an analysis already runs |
| `POST /api/crawls/{id}/analyze/cancel` | 202. Nothing is stored |
| `GET /api/crawls/{id}/analysis` | The last analysis, below. 404 `not-analyzed` if it never ran |

```json
{"analyzedAt":"…","engineVersion":"…","pagesAnalyzed":265,"durationMs":7012,"isPartial":null,"notes":["…"],
 "counts":[{"checkId":"title-too-long","count":12}],"skipped":[{"checkId":"orphan-page","reason":null}]}
```

`skipped` is every catalog check missing from the counts. E2 stores `isPartial` and each skip's reason; until then
they're null, and the UI shows the notes.

### Data (E1: the readers that exist today)

| Method and path | Returns |
| --- | --- |
| `GET /api/crawls/{id}/stats` | `CrawlStatsReader.Read` |
| `GET /api/crawls/{id}/issues/{checkId}?limit=200&offset=0` | `{ rows: [{ url, detail, relatedUrl }], total }` |

E3 replaces the issues endpoint with keyset paging and grouping. Its grid queries are `POST`s to one fixed URL
per grid (see CORS above). E3 adds:

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
