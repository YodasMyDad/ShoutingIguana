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
- **E3a** is marked below: the grids' reads. The URL grid with its facets and lookup, the inspector with a URL's links
  and source, and a check's issues in groups.
- **E4** is marked below as well: the live data the crawl screens need. Progress by status class and code, with the
  crawl's phase and what it waits on; the history the "found and crawled" chart draws; the analysis's progress; and
  alerts kept in the crawl file.
- **E3b, E5 and E6** add the links grid, sitemaps and robots.txt, export, search and the favicon. They come from the
  brief's [engine work the UI needs](v2-ui-brief.md#engine-work-the-ui-needs) and the handoff's
  [what's missing](v2-rebuild.md#whats-missing).

## Process (E1)

- **Start.** The Tauri shell starts `si-sidecar --data-dir <folder> --state-dir <folder>`.
  - `--data-dir` is where new crawls are saved. It defaults to `Documents/Shouting Iguana`.
  - `--state-dir` holds `library.json`, the user's defaults, the cached summaries and the logs. It defaults to
    `<local app data>/ShoutingIguana`. **One sidecar uses a state folder at a time:** the sidecar holds an exclusive
    lock (`sidecar.lock`) on it for its lifetime, and a second sidecar on the same folder writes a line to stderr and
    exits with code 3, rather than two of them writing the same library and defaults and each missing the other's
    changes. The operating system drops the lock when the process ends, so a crash never leaves the folder locked.
  The shell also passes `--stdin-lifeline`, described under Shutdown. `--port` fixes the port, for development; it
  defaults to 0, a free port.
- **Environment.**
  - `SI_SIDECAR_TOKEN` is the bearer token: at least 32 characters, 32 random bytes as hex as the shell makes it for
    each launch. The sidecar removes it from its own environment after reading it, so processes it starts don't inherit
    it. A sidecar refuses to start without it (exit code 2), unless it is started in development mode with `--dev`:
    then, and only then, it makes a token up and includes it in the ready line for the dev UI to read from stdout.
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

  `"token"` is added only when the sidecar made one up, which happens only in development mode (`--dev`). Nothing else
  ever goes to stdout: logs go to stderr and to `<state-dir>/logs`.
- **Shutdown.** The shell can't rely on killing the sidecar: `tauri-plugin-shell` leaves a sidecar it started from
  Rust running after a normal exit, and after a hard kill of the shell. So stdin is the lifeline. With
  `--stdin-lifeline`, a `shutdown` line on stdin, or stdin reaching its end because the shell died, shuts the sidecar
  down. It is opt-in, so a sidecar started any other way with no stdin doesn't stop at once. On Windows the shell
  also puts the sidecar in a job object that kills it when the shell's last handle closes, as a backstop for a
  sidecar that hangs.

  The sidecar shuts down on any of: a `shutdown` line or the end of stdin (with the lifeline), `POST /api/shutdown`,
  Ctrl+C, SIGTERM, or the parent process ending. Then, **in this order**:
  - every running crawl is told to stop, and every running analysis is cancelled, as the very first step of shutting
    down — before the web server drains the requests in flight, so a slow request can't let a crawl fetch on after
    shutdown was asked;
  - each stopped crawl's file stays resumable, and each cancelled analysis stores nothing;
  - the sidecar waits for the crawls to close their files and the requests in flight to finish, then exits with code 0.
  - **How long it can take.** The wait is bounded (30 seconds). Stopping a crawl cancels its requests at once, so the
    wait is for each crawl to close its file and for requests in flight to finish; past the bound the sidecar exits
    anyway, leaving any crawl that hadn't closed marked interrupted and still resumable. The desktop shell should allow
    at least this long after asking the sidecar to shut down before killing it.
  - Ctrl+C or SIGTERM arriving while the sidecar is still starting up exits cleanly (code 0) without serving.

  **Exit codes:** 0 after a clean shutdown; 1 if it couldn't start (a port taken, a state folder it can't create);
  2 for arguments it can't use, or no token without `--dev`; 3 when another sidecar is using the state folder.

## Security (E1)

- **Token.** Every request needs `Authorization: Bearer <token>`, compared in constant time; otherwise 401. CORS
  preflights (`OPTIONS`) are the exception: they carry no credentials, and are answered only for allowed origins. The
  token is at least 32 characters. The shell always sets one; a sidecar makes its own only in development mode
  (`--dev`), and refuses to start otherwise.
- **Host.** `Host` must be `127.0.0.1:<port>` or `localhost:<port>`, with the bound port; otherwise 421. This defeats
  DNS rebinding. The UI only ever uses `127.0.0.1`; `localhost` is accepted on purpose, for tools and people trying the
  API by hand, and is safe because a browser resolves `localhost` to the loopback address itself (so it can't be a
  rebound name) and a page that calls it is cross-origin, so its `Origin` is still checked. `[::1]` is not accepted:
  the sidecar listens only on the IPv4 loopback.
- **Loopback only.** The sidecar binds `127.0.0.1` and refuses to start if anything (an `ASPNETCORE_*` environment
  variable, say) would have moved the listener to another address; the ready line always names the port it truly bound.
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
  - Real responses repeat `Access-Control-Allow-Origin` and `Vary: Origin`, and carry `Cache-Control: no-store`,
    `X-Content-Type-Options: nosniff` and `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`: no
    answer is a page, and no browser may take one for a page.
  - No cookies and no credentials mode.
  - Chromium caches a preflight per URL, so query strings that change every call (paging) cost a preflight each. The
    grid queries (E3) are therefore `POST`s with a JSON body to one fixed URL per grid, and the inspector, a URL's
    links and its source take the URL's id in the body, so inspecting URL after URL costs no preflight either. The UI
    never sets `cache: 'no-store'` on a request, because that turns the preflight cache off.
- **Bodies.** A request body is at most 1 MB.
- **Paths from the UI** (open, add to library) must be absolute and end in `.sicrawl`. The file is checked to be a
  crawl before it's added. A path to another computer is refused with `file-error` ("copy the crawl to this computer
  first"): on Windows, a UNC path (`\\server\share\…`) or a device path (`\\?\…`, `\\.\…`), since opening one would
  make the sidecar authenticate to that server. A mapped drive letter is the user's own choice and stays allowed. A
  crawl's id is the hash of its path with links resolved (see **Crawl ids**), so the same file reached two ways is one
  crawl, not two.
- **Crawled text** (titles, anchor text, alert messages, details) comes back exactly as stored. The UI renders it as
  text, never as HTML.

## Conventions (E1)

- **JSON.**
  - camelCase.
  - Enums as camelCase strings (`"running"`, `"error"`).
  - Times in ISO 8601 UTC, durations in milliseconds (`elapsedMs`), sizes in bytes.
  - Spellings follow the engine (`analyze`, `analyzedAt`). The UI's text uses UK English.
- **Crawl ids.** 16 hex characters of the SHA-256 of the file's full path with links resolved (symbolic links, and
  junctions on Windows), case-folded on Windows and macOS. Case folding follows the operating system, not the volume,
  so on a folder set to tell case apart (rare) two names differing only in case would share an id. An id is stable
  while the file stays where it is.
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
    | `not-a-crawl` | 400 | The file isn't a crawl: SQLite error 26 (as in the CLI), the wrong schema, or a view or trigger the engine never writes |
    | `crawl-not-found` | 404 | No crawl has this id |
    | `crawl-busy` | 409 | Running here or elsewhere, an analysis is running, another program holds the file's SQLite lock, or another program is part-way through reading the closed file, which keeps writers out (the grids' reads here never do: an analysis or a continue starting stops them for the moment it takes) |
    | `not-analysable` | 409 | The engine refuses: running or interrupted |
    | `file-error` | 400 | IO or permissions |
    | `not-analyzed` | 404 | The crawl has never been analysed |
    | `needs-analysis` | 409 | A grid sorts or filters on the URL summary while there is none to read: never analysed, analysed by another engine version, or changed since. `reason` says which, and `columns` names the columns (E3a) |
    | `invalid-query` | 422 | A grid query the engine can't run: an unknown column, an operator its type doesn't take, a value of the wrong type for its column, a field the operator doesn't take, a null in a list, a regular expression that doesn't compile, needs backtracking or is too complex for the crawl, a cursor from another query or whose row has changed or gone. `errors` is keyed by the field's path, such as `filters[1].value` (E3a) |
    | `url-not-found` | 404 | No URL of the crawl has this id or text (E3a) |
    | `not-found`, `check-not-found` | 404 | No such route, or no check with this id |
    | `bad-request` | 400 | Malformed JSON, or a property the API doesn't know |
    | `unauthorized` | 401 | The token is missing or wrong |
    | `origin-not-allowed` | 403 | An `Origin` not on the allowlist |
    | `body-too-large` | 413 | A body over 1 MB |
    | `bad-host` | 421 | A `Host` other than `127.0.0.1` or `localhost` with the bound port |
    | `internal` | 500 | Anything else: the title is generic, and the detail is the exception message, never a stack trace |

  - An option value of the wrong type is a field error under `invalid-options`, not a 400.
  - In a grid query, a field whose JSON type is fixed (`limit`, `offset`, `desc`, `urlId`, `after`, a column's or a
    kind's name, a group's `relatedUrlId`) is a `bad-request` 400 when it has another; a filter's `value` and `values`,
    which may be text, a number or a boolean, are checked against the column, under `invalid-query` (E3a).
  - A value a crawl file holds that this engine doesn't know (a newer engine's kind of link or outcome, a crafted
    file's number out of range) reads as null, as an unknown value does: never an error (E3a).

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
- `checks: [{ id, category, severity, title, description, fix, grouping }]`, in catalog order. `grouping` (E3a) is how
  the check's issues group: see [Issue groups](#issue-groups-e3a).

Both come from `IssueCatalog`. The engine gets public category titles, so the UI hard-codes none.

### Crawl options (E1)

| Method and path | Does |
| --- | --- |
| `GET /api/crawl-options/defaults` | `{ options, speedPresets }`: the user's saved defaults, or the engine's |
| `PUT /api/crawl-options/defaults` | Saves "my defaults". The start URL, header values and proxy credentials are never saved: the UI keeps secrets in the OS credential store, with the user's consent |
| `POST /api/crawl-options/validate` | `{ errors: { <field>: [messages] } }`, 200. Field names are the JSON names of `CrawlOptions` properties; a value of the wrong type is a message for its field too. A body that isn't JSON, or a property the options don't have, is a `bad-request` (400), as on every other call. The engine gets a field-keyed `Validate`, and the CLI keeps its list of messages |

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
 "fetchedUrlCount":4521,"pageCount":265,"issues":{"error":3,"warning":12,"notice":7},"analyzedAt":"…",
 "outOfDate":false,"sizeBytes":18276352,"missing":false}
```

- **`site`** is the host without a leading `www.`. The UI groups crawls by it.
- **`fetchedUrlCount`** is the URLs with a stored outcome: those the crawl requested, and those robots.txt kept it from
  requesting (stored as disallowed). URLs it only found (a nofollow link, past a limit) aren't counted. It matches
  `si stats`'s fetched count and a running crawl's `fetched` progress. (The full count of every URL in the file, fetched
  or not, is `/stats`'s `urlCount`.) **`pageCount`** is the pages — internal URLs that returned HTML the crawler
  parsed — as in `si stats`, and the same definition a running crawl's `htmlPages` uses, so the count doesn't shift
  when a crawl ends.
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
  | `unreadable` | The file couldn't be read (a lock held by another program, a damaged file, or a read that timed out) but may be a crawl. `error` says why in plain words. A lock or timeout is retried on a later list; a file that isn't a crawl at all (SQLite error 26, the wrong schema, or a view or trigger the engine never writes) is left out of the list entirely |

- **`issues`** counts checks with at least one issue, by severity: the Issues screen's rows, not issue rows. It is
  null before the first analysis.
- **`outOfDate`** (E2) is true when the last analysis no longer stands, and false before the first one. That's when:
  - the crawl changed after it: the crawl row's `updated_at` isn't the one the analysis kept, because the crawl was
    continued, here or elsewhere, or is running here;
  - this engine would judge the crawl differently: an engine with another analysis version or other checks ran it,
    as every engine from before E2 did;
  - or the analysis ran with other thresholds than the sidecar's.

  `GET /api/crawls/{id}/analysis` gives the reasons.
- **Caching.** Each file's summary is read once and kept until the file's size or modified time changes, so the list
  stays cheap with many crawls. The cache is persisted in the state folder (`summaries.json`, keyed by the file's
  stamp, the engine version and the cache's own format, which changes whenever a summary keeps more), so a restart
  doesn't re-read every crawl. A read is given a short time limit, so one
  file crafted to be slow can't stall the list. A read that fails for a passing reason (a lock another program holds)
  is retried on a later list rather than remembered until the file changes, and opening a crawl reads it afresh. The
  engine gets a cheap summary reader; `CrawlStatsReader.Read` counts every link, which is too slow for a list.

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

  The UI then asks for the missing secrets. The redacted values `GET /options` returns don't count as given: a header
  with an empty value (which isn't sent at all), and a proxy whose user name is the redaction marker `***`. So a form
  pre-filled from `GET /options` and posted straight back still gets `needs-secrets`, not a crawl resumed with empty or
  redacted credentials.

### Live events (E1)

`GET /api/events` is a `text/event-stream`. The UI reads it with `fetch`, since `EventSource` can't send the token.

- **First.** A `hello` event, `{ live: [crawlIds], analyzing: [crawlIds] }`, followed by the latest `progress` for each
  live crawl, then the latest `analysis` of each crawl being analysed. A reconnecting UI is up to date at once.
  `analyzing` (E4) lists the crawls being analysed here, so the `analysis` events that follow are for crawls the UI
  knows to be analysing, though it missed their `analyzing` event.
- **Replay.** None. Events carry no ids, and `Last-Event-ID` is ignored. Alerts can be fetched with
  `GET /api/crawls/{id}/alerts`, which reads them from the crawl file; an alert's `id` and `at` together tell the UI one
  it already has (see **Alerts**).
- **One stream.** Kestrel without TLS speaks HTTP/1.1, and Chromium allows 6 connections per host and port. An open
  stream holds one for good, so every live event shares this one stream.
- **Keep-alive.** A comment line every 15 s.
- **Slow clients.** Each client has a bounded queue. Progress is merged per crawl when a client falls behind; other
  events, `analysis` included, are never dropped. A client that stays 1,000 events behind is disconnected, and
  reconnects.

| Event | Data | When |
| --- | --- | --- |
| `progress` | `{ crawlId, status, elapsedMs, discovered, queued, inFlight, fetched, htmlPages, redirects, errors, blocked, robotsDisallowed, skipped, bytesDownloaded, urlsPerSecond, at, phase, internalFound, internalFetched, maxUrls, statusClasses, statusCodes, noResponse, waits, moreWaits }` | Every 500 ms per live crawl (the engine's pace). The UI batches to 4 renders a second. `urlsPerSecond` is the rate of every URL fetched, pages and the rest alike. The fields from `at` on are E4's, below |
| `alert` | `{ crawlId, id, level, code, message, at, data }` | Each `CrawlNotice` (see below) |
| `crawl` | `{ crawlId, status, error?, errorType? }` | Every status change, with the library's statuses. An analysis shows as `analyzing`, then back to `completed` or `stopped` with a `library` event, and `error` if it failed. `errorType` is the problem type when the UI can act on it: `crawl-busy` when the analysis found the file busy (someone reading it, another program writing to it), within seconds, so trying again can work |
| `library` | `{}` | The library changed. The UI fetches `/api/crawls` again |
| `analysis` | `{ crawlId, phase, done, total }` | E4. While an analysis runs: each phase as it starts (`done` 0) and as it ends (`done` equal to `total`), and at most every 250 ms between, the phases in the order under **Analysis progress** below. All of them come after the crawl's `analyzing` event and before the one that ends it |

#### Progress (E4)

Counters only: the engine keeps running totals as it stores each response, and reads a handful of gates when it
reports, so a progress event costs the same at a million URLs as at ten. Every count in one event is of the same
moment, as the engine's writer stored it, so they always agree with each other: the classes add up to `fetched`, and so
on, mid-crawl as at the end.

- **`at`** is when the numbers were taken: the time the history keeps them under.
- **`phase`** is what the crawl is doing as a whole. Whether it's paused is its `status`.

  | Phase | The crawl is |
  | --- | --- |
  | `starting` | Queuing the start URL, for a moment. Continuing a crawl reads every URL into memory before its first progress, while the library shows it `running` |
  | `readingRobotsTxt` | Waiting for robots.txt, and fetching nothing else: as the start URL does before the first fetch. A URL waiting for its robots.txt while others are fetched is still `crawling` |
  | `crawling` | Fetching |
  | `readingSitemaps` | Waiting for the sitemaps still being read, as nothing else is left to fetch. What only they list comes next |
  | `retrying` | Giving the site's URLs that got no response their one more try, once nothing else is left, after robots.txt's two-minute cool-down where it couldn't be read: from then until the last of those tries is over, with what they lead to fetched meanwhile. What's fetched after that (the URLs only sitemaps list, say) is `crawling` again, and a URL that gets no response then is tried once more in another `retrying` |
  | `finishing` | Done fetching: the last requests settle and the file closes. The last event, with the final status, has this phase. Nothing is waited for, so `waits` is empty |

- **`statusClasses`** is `{ "2xx", "3xx", "4xx", "5xx", other, noResponse, blocked }`: every URL fetched, once, so they
  add up to `fetched`. A firewall or bot challenge is `blocked` whatever its status; the rest go by status code, and
  `other` is a status outside 200–599 (LinkedIn's 999). `noResponse` is a URL with no status recorded.
- **`statusCodes`** is `{ "200": 2693, "301": 8, "404": 8 }`: the URLs with a status that weren't blocked, by code, so
  each class's codes add up to it. A block page's 403 is the firewall's, not the site's: "403 × 2" under 4xx is two
  genuine 403s.
- **`noResponse`** is `{ robotsDisallowed, timeout, networkError, notChecked, processingError }`, which add up to
  `statusClasses.noResponse`. `robotsDisallowed` is the same count as the top-level field (refused by a rule, or because
  robots.txt couldn't be read). `notChecked` is URLs never asked, their host having stopped answering: unknown, not
  broken, so the UI shouldn't call them errors. `networkError` doesn't include them.
- **`internalFound`** and **`internalFetched`** are the site's own URLs (internal, not the crawler's probes of its other
  versions) the crawl means to fetch, queued, in flight or fetched, and those it has fetched. URLs it skipped (nofollow,
  a limit) aren't counted. A URL limit counts these, so a crawl with one heads for the smaller of `internalFound` and
  the limit, and the UI can give an ETA from `internalFetched`'s rate. Once the limit is reached neither passes it: a
  probe of the site's other versions that the site links to after that stays a probe, as any URL found then is
  skipped. Without a limit, more can turn up until the queue is empty.
- **`maxUrls`** is the crawl's URL limit, or `null` when it has none (it's always there), so the ETA needs no other
  request.
- **`waits`** lists what holds part of the crawl up now, often nothing: `[{ reason, host?, urls?, durationMs? }]`. At
  most five for each reason, the longest first (the longest wait, or the most URLs waiting); **`moreWaits`** says how
  many more there are, by reason, for the UI to show as "and 212 more hosts": `{ "crawlDelay": 212 }`, and `{}` when
  every wait is listed, as it usually is. A site with every subdomain included can have hundreds of hosts under one
  Crawl-delay. The last event lists none.

  | Reason | Means | Fields |
  | --- | --- | --- |
  | `robotsTxt` | The site's URLs wait for the robots.txt of their host, which comes before its first fetch | `host`: whose robots.txt, `urls`: how many |
  | `otherSitesRobotsTxt` | Status checks of links to other sites wait for those sites' robots.txt. They hold up nothing of the site's own | `urls`: how many |
  | `crawlDelay` | The site's robots.txt asks for a Crawl-delay, which spaces every request to that host. Only with `respectCrawlDelay` on, and when it is longer than the crawl's own rate | `host`, `durationMs`: the delay |
  | `backoff` | The site answered 429, 503 or a block page, or keeps failing, so requests are spaced further apart. It halves every half second, so it comes and goes | `host`, `durationMs`: the extra spacing now |
  | `heldBack` | Requests to the host are held: it sent Retry-After, or it stopped answering and is asked again a minute after each try | `host`, `durationMs`: until they may go again |
  | `siteDown` | An origin of the site has stopped answering and is waited out. The crawl stops after 10 minutes of it, resumable. Only the one down longest is listed | `host`: the origin, its scheme and host, and its port unless it's the scheme's default (`https://example.com`, `http://127.0.0.1:8080`), `durationMs`: how long so far |
  | `robotsTxtCoolDown` | In any phase: robots.txt couldn't be read and is asked again after a two-minute cool-down, which something waits for: URLs it refused, whose one more try comes after it, or the sitemaps it declares, read once it has been asked again (the phase is then `readingSitemaps`, once nothing else is left) | `urls`: the URLs it refused, if any, `durationMs`: until it's asked again (the last, where several couldn't be read) |

  **Hosts.** A site's host is named one way in waits and alerts alike: as the start URL writes it, for both it and its
  www twin, which are one site sharing one budget. A crawl from `https://www.example.com/` says `www.example.com` for
  `example.com` too, and one from `https://example.com/` says `example.com` for `www.example.com`. With subdomains
  included, any other host of the site is named without a leading `www.` (`shop.example.com`). An origin (`siteDown`,
  and the alerts' `origin`) is named as it is, scheme and port included, since one of a host's origins can be down
  while another answers.

#### Analysis progress (E4)

An `analysis` event's `phase` is one of these, in this order, each counting what `done` and `total` count:

| Phase | The analysis is | `done` and `total` |
| --- | --- | --- |
| `reading` | Reading the crawl's URLs, their statuses and what the checks need into memory | URLs |
| `pageRules` | Running every page's checks, in one pass over the pages | Pages, counted ahead from the URLs read |
| `siteRules` | Running the checks across pages (duplicates, orphans, redirect chains, sitemaps...) once every page is read | Rules (9) |
| `urlSummary` | Building the URL grids' table, one row per URL | URLs |
| `writing` | Committing the results; it ends once the file is closed | 1 |

#### Alerts (E4)

An alert is a `CrawlNotice`, kept in the crawl file as the engine raises it (the `notices` table in
[the engine's README](../engine/README.md#crawl-file-sicrawl)), so any program that opens the crawl can show it.

- **`id`** numbers the crawl's alerts from 1, across every run of it: the same on the live event and in the file.
- **`at`** is when it was raised, to the millisecond.
- **An alert is its `id` and its `at` together**, so the UI keys alerts by both. Each run numbers on from the highest
  id the file has, and the engine stores an alert a moment after the UI hears of it, so a crash (or a hard kill past
  the shutdown bound) can lose the last few: the next run then gives their ids to new alerts. An alert heard live as
  id 7 and another read back later as id 7 are told apart by `at`.
- **`level`** is `info`, `warning` or `error`.
- **`code`** is stable; key UI text and actions on it. Each says once what it's about: `blocked` once per host, and
  `robots-unreachable` once per origin, in each run, so a crawl continued while it's still blocked says so again.
- **`message`** is engine text and may include text from the site.
- **`data`** is what the alert is about, for the UI to offer its action. Fields that don't apply are left out; it's null
  for an alert without facts.

  | Code | Level | `data` | What the UI can offer |
  | --- | --- | --- | --- |
  | `blocked` | warning | `host`, `reason` (the firewall or challenge, "Cloudflare challenge"), `count`: the host's responses blocked, 1 on the live event (the one that raised it), and read with `GET /alerts`, the host's blocked responses in the whole crawl so far, every run's. A crawl continued while still blocked has a `blocked` alert for the host in each run, each with that same count, so the UI shows it once, on the latest | Continue more slowly, add headers, another user agent |
  | `mostly-forbidden` | warning | `host`, `count` (the 403s), `sampled` (of the site's first 20 URLs) | As for `blocked` |
  | `start-redirects-away` | warning | `url`: where the start URL leads | Crawl from there instead |
  | `robots-unreachable` | warning | `origin`, `reason` (`HTTP 503`, or the error), `durationMs`: how long until it's asked again | Crawl again with robots.txt ignored |
  | `site-unreachable` | warning | `origin`, `reason` (the last error), `durationMs`: the pause between tries | Wait, or stop and continue later |
  | `site-down` | error | `origin`, `durationMs`: how long it hadn't answered | Continue once the site is back |
  | `sitemaps` | info | `count` (sitemaps read), `urlCount` (URLs they list) | Open Sitemaps |
  | `sitemaps-not-read` | warning | `url` (the sitemap left out), `reason` | Open Sitemaps |
  | `sitemaps-failed` | warning | `reason` | Details |

  `host` is the site's host as **Hosts** above names it, as in `waits`; `origin` is the exact origin, its scheme and
  host, and its port unless it's the scheme's default.

`GET /api/crawls/{id}/alerts` returns a crawl's alerts from its file, oldest first, up to the last 1,000: every run's,
for any crawl, whoever ran it. The engine stores each within a second or so; for a crawl live here, the sidecar adds the
few it hasn't stored yet. It takes those before it reads the file, so an alert the crawl stores as it closes, and the
sidecar then lets go of, is in one or the other, never missed. A crawl file from before E4 has none until it's
continued, which adds the table, and reads as `[]`. Files are shared, so an alert another program wrote oddly is read
as well as it can be: one without a code or a message is left out, a missing level reads as `warning` and a missing
time as the crawl's start, and a table of another shape reads as `[]`. Continuing a crawl whose alerts table the engine
couldn't store an alert in is refused (`not-a-crawl`), in words that say so.

### History (E4)

`GET /api/crawls/{id}/history` is a crawl's progress since it started, for the "found and crawled" chart. A UI that
opens mid-crawl draws it, then adds each `progress` event at its `at`.

```json
{"crawlId":"…","startedAt":"2026-11-01T14:13:02.114Z","liveFrom":"2026-11-01T14:13:02.114Z",
 "points":[{"at":"…","fetched":499,"htmlPages":420,"internalFetched":480,"internalFound":3912,"errors":3,"blocked":0,
            "elapsedMs":240000,"discovered":5384,"queued":3384,"skipped":1501}]}
```

- **`startedAt`** is when the crawl first started: the chart's zero.
- **`points`**, oldest first, end at the latest. A crawl live here has this run's points as the sidecar took them, one
  per `progress` event: at most 600 evenly spread, every half second at first, then every second, two seconds and so
  on, and the latest. Memory stays flat however long it runs.
- **`internalFound`** and **`internalFetched`**, on every point, are the progress event's: the chart's "found" and
  "crawled" lines meet the headline and its ETA.
- **`liveFrom`** is where the points taken live begin: the start of the run this sidecar is watching. Before it, and
  for a crawl not live here (`liveFrom` null), the points are counted back from the crawl file: a zero at `startedAt`,
  then at most 600 evenly spread over the seconds anything was fetched or found in, and the last.
- **What the file can say.** It keeps when each URL was fetched, so its points have `fetched`, `htmlPages`,
  `internalFetched`, `errors` and `blocked`. It doesn't keep when each URL was found, but it keeps the page that first
  linked it, and a URL is found as that page is fetched: so its points have `internalFound` too, counting each of the
  site's own URLs from that page's fetch (or its own, if that came first), and the start URL and the URLs only sitemaps
  list, which no page links, from the crawl's start, as the sitemaps are read then. A URL the crawl skipped isn't
  counted at any point, though a live count had one skipped for the URL limit until the limit was reached. The rest of
  what was found (`discovered`, `queued`, `skipped`) can't be counted back, so file points leave it out, and
  `elapsedMs`. Nor does the file keep when each run began and ended: a stretch where nothing was fetched can be a
  pause, a stop between sessions, or a site that wasn't answering. A URL fetched twice (a status check made a full
  fetch) counts from its last fetch.
- **Cost.** A live crawl's own points cost nothing to serve. Counting back from the file reads every response's time
  and every one of the site's URLs once: about 0.1 s for 100,000 URLs and 1.2 s for a million. It's done once and kept:
  for a live crawl that continued an earlier one, for that run; for a crawl not live here, until its file changes (its
  size or modified time, or its write-ahead log's, as the library's summaries are kept), for the 16 crawls read last.

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
| `rule-failed` | The check's rule failed once every page was read, so it has no results (the log says why) |
| `newer-than-analysis` | The check is newer than the engine that ran the analysis. A check an analysis by a newer engine than the sidecar's didn't run has no `reason`: that engine dropped it |

### Data (E1: the readers that exist today)

| Method and path | Returns |
| --- | --- |
| `GET /api/crawls/{id}/stats` | `CrawlStatsReader.Read`. `nonIndexableReasons` counts the site's URLs as the checks and the URL summary do: the probes of its other versions are left out, and so are the URLs that weren't checked, which `notCheckedCount` gives |
| `GET /api/crawls/{id}/issues/{checkId}?limit=200&offset=0` | `{ rows: [{ url, detail, relatedUrl }], total }`. `total` is null for a check that was skipped, never 0 |

E3 replaces the issues endpoint with keyset paging and grouping. Its grid queries are `POST`s to one fixed URL
per grid (see CORS above). The URL grids read the URL summary each analysis builds (E2, `url_summary` in
[the engine's README](../engine/README.md#url-summary)): one row per URL, with indexes for the common sorts and
selective filters. The summary is there only while it describes the crawl (`UrlSummaryReader.IsAvailable`): the last
analysis stored it with this engine's analysis version, and the crawl hasn't changed since. A crawl never analysed,
analysed by another engine version, or changed after its analysis (continued, running here or elsewhere, or stopped
again) has none: the URL grids then show only what the crawl itself recorded (the `urls` and `responses` columns:
the URL, its status, content type, depth, size, time, title...), a query that sorts or filters on a summary column
gets `needs-analysis` rather than failing, and the UI offers to analyse it. An analysis out of date only because
the checks or thresholds changed keeps its summary, shown behind the out-of-date notice. E3 adds:

- `/urls`, its facets and lookup (E3a, below);
- `/urls/{urlId}`, the inspector (E3a);
- `/issues/{checkId}/groups` and `/rows` (E3a);
- `/links` (E3b);
- `/sitemaps` (E3b);
- `/robots` (E3b);
- `/search` (E6);
- `/export` (E5).

The GET issues endpoint above stays as it is, for scripts and the design fixtures; the Issues screen uses `/rows`.

### URLs (E3a)

The URL grid: every URL the crawl found is a row, fetched or not (filter on `fetched` for the library's
`fetchedUrlCount`), the probes of the site's other versions included (filter on `source`). Each call reads one snapshot
of the file and holds it for that one page only.

- **A grid never keeps an analysis or a continue waiting.** Starting one here stops the grids' reads of that crawl for
  the moment it switches the closed file to WAL mode (milliseconds), and they read again after, so the UI sees a page
  a moment later, never an error. Another program part-way through reading the closed file (`si stats`, a SQLite
  browser, an export) isn't seen, and can still keep it out: the analysis or continue then fails with `crawl-busy`
  within a couple of seconds.
- **A request given up stops its read.** The webview aborts a page request when the grid scrolls on (it closes the
  connection): the read stops within milliseconds, so a fast scroll costs the page it stops on, not every page it
  passed.
- **Grid reads queue.** At most one a core runs at once; the rest wait without holding the sidecar up, so `/api/health`,
  the event stream and crawl control answer at once whatever the grids are doing.

`POST /api/crawls/{id}/urls` takes a query and returns one page of rows:

```json
{"columns":["url","statusCode","indexability","issues","title"],
 "filters":[{"column":"type","op":"in","values":["page"]},{"column":"statusCode","op":"between","min":400,"max":499}],
 "search":"rattan","sort":[{"column":"issues","desc":true}],"offset":0,"limit":100}
```

```json
{"columns":["id","url","statusCode","indexability","issues","title"],
 "rows":[{"id":119,"url":"https://www.example.com/rattan/old/","statusCode":404,"indexability":"clientError","issues":2,"title":null}],
 "offset":0,"matching":3,"total":6286,"next":"…","summary":{"available":true,"reason":null},"unavailable":[]}
```

- **`columns`** says what each row carries, so a query reads only what the view shows. `id`, the URL's id in the
  crawl, is always first; the default is `["id", "url"]`. A row's value is null when it is unknown (a URL never
  fetched has no status), never 0.
- **Paging.**
  - `offset` and `limit` reach any row, for a grid whose scrollbar can be dragged anywhere. `limit` is 1 to 1,000
    (default 100).
  - `next` is a cursor for the row after the last one, null at the end. Passed back as `after`, with the same
    filters, search and sort, it reads on from there (for exports) instead of from `offset`. Give `offset` or `after`,
    not both. A page read on from a cursor has `offset` null.
  - A cursor is under 100 characters whatever the row holds (a URL of thousands of characters included), so an export
    reads on to the end. It names its row: one that has since gone, or whose sort values have changed (a crawl running,
    analysed again), is refused with `invalid-query` on `after` ("start from an offset again"), never read on from a
    place that has moved.
- **Counts.** `matching` counts the rows the filters and search match, and `total` every URL in the crawl, so the UI
  can say "3 of 6,286 URLs". Both are cached per crawl file, analysis and filter set, so scrolling never counts again;
  a running crawl's are kept for 2 s, then counted again. Requests that want a count being counted (a page, its
  prefetch, the facets) wait for that count rather than counting it again.
- **`filters`** must all match. Each is `{ column, op, ... }` by the column's type:

  | Type | `op` | With |
  | --- | --- | --- |
  | number | `eq`, `ne`, `lt`, `lte`, `gt`, `gte` | `value` |
  | number | `between` (inclusive) | `min`, `max`, or both |
  | number, enum | `in`, `notIn` | `values`, 1 to 100 of them |
  | text | `contains`, `notContains`, `startsWith`, `endsWith` | `value`, matched ignoring the case of ASCII letters |
  | text | `eq`, `ne` | `value`, matched exactly |
  | text | `regex`, `notRegex` | `value`, a .NET regular expression, case-sensitive unless it starts `(?i)` |
  | boolean | `is` | `value`: true or false |
  | every type | `isEmpty`, `isNotEmpty` | Nothing. Empty is unknown (null), and for text also `""` |

  - A negative operator (`ne`, `notIn`, `notContains`, `notRegex`) matches unknown values too, as "exclude this value"
    should; add `isNotEmpty` to leave them out.
  - A field the operator doesn't take is refused, not ignored: `values` or `min` beside `eq`, `value` beside `in`.
    A null in `filters`, `columns` or `sort` is a field error (`filters[0]`).
  - Regular expressions run in .NET's non-backtracking mode, linear in the text. A pattern that needs backtracking
    (backreferences, lookarounds, atomic groups) is refused, and so is one too large to build (a repeat such as
    `{100000}`). Nested counted repeats (`((.{0,9}){0,9}){0,9}`) can still take minutes on one URL, so each match stops
    after 100 ms, and a read's matches after 3 s in all: a pattern that takes longer is refused with `invalid-query`
    on its `value`, "This pattern is too complex to run on this crawl. Simplify it.", so no pattern can hang a read.
    A simple pattern takes about 0.3 µs a title. Text values and patterns are at most 1,000 characters, and a query
    has at most 20 filters.
  - **`check`** is a filter, not a column: `{ "column": "check", "op": "in", "values": ["title-too-long"] }` matches the
    URLs that any of these checks flagged, and `notIn` the URLs none of them did. With one check, `group` narrows it to
    one of the check's groups (see Issue groups), so a group's count opens its URLs in this grid:
    `{"column":"check","op":"in","values":["empty-anchor-text"],"group":{"detail":null,"relatedUrlId":1}}`. It reads
    the last analysis's issues, as `/rows` does, so it works without the URL summary too (a crawl continued since its
    analysis); only a crawl never analysed refuses it, with `needs-analysis`.
- **`search`** matches the URL or the title containing the text, ignoring the case of ASCII letters. It's a plain scan
  for now (fast search comes in E6), at most 1,000 characters.
- **`sort`** is up to 3 `{ column, desc }`, with unknown values last whichever the direction, and the URL's `id`
  breaking ties, in the last key's direction (so an index walked backwards needs no sort). The default is crawl order.
- **The summary.** `summary` says whether the URL summary describes the crawl (see above), and `reason` why not:

  | Reason | The summary isn't there because |
  | --- | --- |
  | `crawling` | The crawl is running, here or in another program |
  | `notAnalyzed` | The crawl has never been analysed |
  | `engineChanged` | Another version of the engine analysed it |
  | `crawlChanged` | The crawl was continued after its analysis |

  Without it, the columns marked "Needs analysis" below have no values: they are listed in `unavailable`, and left out
  of each row. A filter or sort on one is refused with `needs-analysis` (409), whose `reason` is the one above and
  whose `columns` name the columns, rather than ignored. The other columns read the crawl's own tables, so a running
  crawl's grid works, live.

  Those tables have an index only for a URL and its id, so without the summary a read of a big crawl reads every row:
  on 1M URLs, a page sorted or filtered takes 0.3–0.9 s, and the facets 0.9 s, or 1.35 s filtered by a column (the
  pages), the one read over the 1 s budget. During a crawl, counts and facets are kept for 2 s, so Overview and a
  scrolled grid count at most that often.

`GET /api/url-columns` lists the columns, for the column chooser and the filter bar:
`[{ id, type, needsAnalysis, sortable, filterable, values, issueRowsOnly }]`, where `values` lists an enum's values
and `issueRowsOnly` marks a check's own columns, which only its rows have (see Issue groups).

| Column | Type | Needs analysis | Is |
| --- | --- | --- | --- |
| `id` | number | | The URL's id: the order the crawl found URLs in |
| `url` | text | | The URL, normalised |
| `fetched` | boolean | | The crawl got an answer, or robots.txt refused the request. False for a URL only found (a nofollow link, one past a limit) and for one not checked because its host had stopped answering |
| `isInternal` | boolean | | On the site being crawled |
| `source` | enum | | How the crawl found it: `seed`, `link`, `sitemap`, or `siteVersion` (the crawler's probe of the site's other versions, which nothing on the site leads to) |
| `depth` | number | | Clicks from the start URL. Null for a URL only a sitemap lists |
| `type` | enum | | `page`, `image`, `script`, `stylesheet`, `font`, `media`, `pdf` or `other`: by the content type of a successful response, otherwise by the extension, as the checks tell files from pages |
| `statusCode` | number | | The HTTP status. Null with no answer, or when not fetched |
| `statusClass` | enum | | `2xx`, `3xx`, `4xx`, `5xx`, `other` (any other code), or `noResponse` (fetched, but no HTTP answer: a timeout, robots.txt refusing it). Null when not fetched. Sorts as `statusCode` |
| `outcome` | enum | | `success`, `redirect`, `clientError`, `serverError`, `blocked` (a firewall or bot challenge), `robotsDisallowed`, `timeout`, `networkError`, `processingError`, or `notChecked` (its host had stopped answering). Null when not fetched |
| `redirectTarget`, `redirectTargetStatusCode` | text, number | | Where an HTTP redirect's first hop goes, and the status there |
| `indexable` | boolean | | As the analysis judged it (the crawl's own judgement where the analysis hasn't seen the URL). Null for a page that was only status-checked, whose HTML wasn't read, unless its response rules it out |
| `indexability` | enum | | Why not: `none` (indexable), `noindex`, `canonicalised`, `redirected`, `clientError`, `serverError`, `blockedByRobots`, `blocked` or `unreachable`. Null when `indexable` is |
| `noindex`, `nofollow` | boolean | | A robots meta tag or the `X-Robots-Tag` header says it, to the crawler or to Googlebot. Null when the HTML wasn't read and the header doesn't say it |
| `metaRobots`, `xRobotsTag` | text | | The robots meta tags' content and the `X-Robots-Tag` header, as the page sent them, for the internal URLs whose details the crawl stores |
| `canonicalUrl` | text | | The canonical a search engine honours: the first in the head or a `Link` header |
| `contentType` | text | | The `Content-Type` header |
| `title`, `description`, `h1` | text | | The first title, meta description and H1. Null for anything that isn't a parsed page |
| `h1Count` | number | | H1s on the page |
| `wordCount` | number | | Main-content words, for a page whose HTML was read |
| `outlinks` | number | | Hyperlinks on a page whose HTML was read |
| `sizeBytes` | number | | Bytes on the wire |
| `responseMs`, `ttfbMs` | number | | The whole response, and the first byte. Null when no request was made |
| `titleLength`, `descriptionLength` | number | Yes | The room the title and meta description take in search results, as the checks measure it: full-width characters and emoji count two |
| `finalUrl`, `finalStatusCode` | text, number | Yes | Where the URL's redirects end, meta refreshes included, and the status there. The URL itself when it doesn't redirect |
| `inSitemap` | boolean | Yes | Listed in a sitemap. False only when every sitemap was read |
| `inlinks`, `followedInlinks`, `nofollowInlinks`, `linkingPages` | number | Yes | Hyperlinks to the URL on the pages audited, each counted, a page's links to itself left out: all, those followed, the nofollow ones, and the pages they're on |
| `issues`, `errors`, `warnings`, `notices` | number | Yes | Checks that flagged the URL, in all and by severity |

- **Sorting.** `url` sorts, and every number, enum and boolean column but those read from each response (`outcome`,
  `noindex`, `nofollow`, `h1Count`, `ttfbMs`, and `redirectTargetStatusCode`, which is looked up per row): they filter
  but don't sort, as a sort by them would read every response, as a sort by a page's text would (1.4 s for a page half
  way down 1M URLs), so the other text columns are filters only too. `metaRobots` and `xRobotsTag` are read from each
  page's stored details, so they are shown only: filter on `noindex` and `nofollow` instead. `/api/url-columns` says
  which columns sort (`sortable`).
- The filter `check` needs the analysis, as the columns marked do.

#### Facets

`POST /api/crawls/{id}/urls/facets` counts the matching URLs by value, for the filter chips and Overview's breakdowns.
It takes `{ filters, search, facets }`, the filters and search as above (none for the whole crawl), and the facets to
count (all of them by default):

```json
{"matching":6286,"total":6286,"summary":{"available":true,"reason":null},"unavailable":[],
 "facets":{"statusClass":[{"value":"2xx","count":4300,"filter":{"column":"statusClass","op":"in","values":["2xx"]}},
                          {"value":null,"count":1765,"filter":{"column":"statusClass","op":"isEmpty"}}],
           "responseTime":[{"value":"250-499","min":250,"max":499,"count":822,
                            "filter":{"column":"responseMs","op":"between","min":250,"max":499}}]}}
```

- **Every count opens its URLs.** Each value carries the `filter` that selects it: added to the query's filters, it
  gives a grid of exactly `count` rows.
- **The facets.** Each lists only the values with URLs, and every URL counts once in each, except `severity`:

  | Facet | Values |
  | --- | --- |
  | `statusClass` | As the column, and null (not fetched) |
  | `statusCode` | Each status code, and null |
  | `indexability` | As the column, and null (not judged) |
  | `type` | As the column |
  | `depth` | Each depth, and null (only in a sitemap) |
  | `responseTime` | `0-249`, `250-499`, `500-999`, `1000-1999` and `2000+` ms, with `min` and `max` (no `max` for the last), and null (no request made) |
  | `inSitemap` | true, false, and null (not known) |
  | `severity` | `error`, `warning` and `notice` (URLs with at least one issue of that severity, so a URL can count in several), and `none` (no issues) |

- Without the URL summary, the facets that need it are listed in `unavailable` and left out: `inSitemap` and
  `severity`. A filter on a column that needs it is refused with `needs-analysis`, as above.

#### Lookup

`POST /api/crawls/{id}/urls/lookup` finds a URL by its text, for the palette's "Go to URL…" and for walking links:
`{ "url": "https://www.example.com/a?b=1#top" }` gives
`{ "id": 12, "url": "https://www.example.com/a?b=1", "requested": null }`.

- The text is normalised as the crawl stores URLs: the fragment goes, the scheme and host are lowercased.
- A path that starts with `/` is taken on the site's origin: where the start URL's redirects end, then the start URL's
  own. A crawl started at `example.com` that redirects to `https://www.example.com/` has its pages on the www origin,
  and `/` is its home page there, not the redirect.
- Text without a scheme takes that origin's: `www.example.com/a`, `example.com`, `//example.com/a`.
- The engine treats the bare domain and www, and http and https, as one site. A URL on the site that the crawl doesn't
  have as asked is looked for on its other versions. `url` is then the URL found, and **`requested`** the URL the text
  stood for, which the crawl doesn't have; `requested` is null when `url` is the URL asked for.
- `url-not-found` (404) says the crawl has none of them, and `invalid-query` (422) that the text isn't a URL.

### The inspector (E3a)

`POST /api/crawls/{id}/urls/inspect` with `{ "urlId": 119 }` gives one URL in full: the URL inspected is in the body,
not the path, so moving from URL to URL costs no preflight. `url-not-found` (404) for an id the crawl doesn't have (0,
below, or past what an id holds), and `invalid-query` without one.

```json
{"id":119,"url":"https://www.example.com/blog/?p=2","isInternal":true,"type":"page",
 "discovery":{"source":"link","depth":2,"foundOn":{"id":7,"url":"https://www.example.com/blog/"},"foundVia":"hyperlink",
              "fetchMode":"crawl","state":"done","notFetchedBecause":null},
 "response":{"fetched":true,"outcome":"success","statusCode":200,"contentType":"text/html; charset=utf-8","httpVersion":"2.0",
             "crawledAt":"2026-10-31T20:15:02Z","ttfbMs":180,"responseMs":240,"sizeBytes":18011,"bodyBytes":96340,
             "blockReason":null,"error":null},
 "redirects":{"hops":[],"final":{"id":119,"url":"https://www.example.com/blog/?p=2","statusCode":200},"loop":false,"tooLong":false},
 "indexability":{"indexable":false,"reason":"canonicalised","judgedBy":"analysis",
   "tests":[{"test":"answered","outcome":"pass","statusCode":200},{"test":"robotsTxt","outcome":"pass"},
            {"test":"noindex","outcome":"pass","meta":false,"header":false},
            {"test":"canonical","outcome":"fail","canonical":{"id":7,"url":"https://www.example.com/blog/","statusCode":200},
             "canonicalUrl":"https://www.example.com/blog/"}]},
 "content":{"titles":[{"text":"Blog, page 2","length":12}],"descriptions":[],"h1s":[{"text":"Blog","length":4}],
            "headings":[1,3,0,0,0,0],"wordCount":412,"language":"en-GB","charset":"utf-8","robots":[],"metaRefresh":null,
            "images":12,"imagesMissingAlt":0},
 "limits":{"titleMin":30,"titleMax":60,"descriptionMin":70,"descriptionMax":155,"minWords":200,"slowResponseMs":1000,
           "largePageBytes":1048576,"largeImageBytes":204800,"imageAltMaxLength":125,"maxUrlLength":115,"fromAnalysis":true},
 "links":{"inlinks":3,"followedInlinks":3,"nofollowInlinks":0,"linkingPages":2,"outlinks":45,"inSitemap":false},
 "headers":[{"name":"content-type","value":"text/html; charset=utf-8"}],
 "structuredData":[{"valid":true,"types":["BlogPosting"],"json":"{…}","error":null,"truncated":false}],
 "hreflang":[],
 "issues":[{"checkId":"title-too-short","severity":"notice","detail":"12 characters","related":null}],
 "namedBy":{"total":0,"checks":[]},
 "summary":{"available":true,"reason":null},
 "source":{"stored":false}}
```

- **`discovery`** is how the crawl first found it: `foundVia` is the kind of link (`hyperlink`, `image`, `canonical`,
  `redirect`...), `fetchMode` is `crawl` (fetched, parsed, its links followed) or `check` (status only), `state` is
  `queued`, `fetching`, `done` or `skipped`, and `notFetchedBecause` why a skipped URL wasn't fetched (`noFollow`,
  `notFollowedByOption`, `resourcesDisabled`, `externalsDisabled`, `depthLimit`, `urlLimit`).
- **`response`** is null for a URL never fetched.
- **A value this engine doesn't know** (a newer engine's, a crafted file's) is null, as an unknown one is: discovery's
  `source`, `fetchMode`, `state`, `foundVia` and `notFetchedBecause`, the response's `outcome`, the verdict's `reason`,
  a link's `kind` and `position`; a link's `flags` leave out a flag this engine doesn't know, and a number that doesn't
  fit (an infinite `responseMs`) is null too.
- **`redirects`** follows the URL's redirects hop by hop, an instant meta refresh counting as one, as the analysis
  does: `hops` lists each step `{ id, url, statusCode, outcome, via: "http" | "metaRefresh" }`, at most 20, and
  `final` where they end. `loop` is true when they come back on themselves, and `tooLong` when there are more than 20.
- **`indexability`** is the "Indexable, and why" checklist. Each test's `outcome` is `pass`, `fail` or `notChecked`
  (the crawl never read what decides it: a page only status-checked, a crawl that ignored robots.txt, a test that a
  failure before it makes moot). A file that answered (an image, a stylesheet, a PDF) can say noindex or name a
  canonical only in its headers, which were read, so it passes or fails those two on them:

  | Test | Passes when | With |
  | --- | --- | --- |
  | `answered` | The URL answered 2xx and doesn't move on with an instant meta refresh | `statusCode`, and `metaRefresh` (the target) when it does |
  | `robotsTxt` | robots.txt allowed the request | `unreachable`: true when robots.txt couldn't be read, which refuses everything |
  | `noindex` | No noindex in a robots meta tag or the `X-Robots-Tag` header | `meta` and `header`: where a noindex is |
  | `canonical` | No canonical, one to the URL itself, or one that leads back to it or nowhere | `canonical`: the target `{ id, url, statusCode }` when the crawl has it; `canonicalUrl`: the canonical as the page gives it, normalised, whether the crawl has it or not (a page only status-checked keeps no links, so its `Link` header's canonical may be no URL of the crawl's) |

  `indexable` and `reason` are the verdict, as the analysis judged it (`judgedBy: "analysis"`); without a URL summary
  they are the crawl's own judgement where the analysis hasn't seen the URL (`judgedBy: "crawl"`), and both are null
  for a page that was only status-checked.
- **`content`** is null for anything that isn't a parsed page. Titles, descriptions and H1s are each listed in page
  order with their `length` as the checks measure it; `headings` counts H1 to H6, `images` the images and
  `imagesMissingAlt` those without alt text; `robots` lists the directives that apply to the crawler or to Googlebot;
  `metaRefresh` is `{ delaySeconds, url }` for a page with a meta refresh, else null.
  **`limits`** are what the analysis judged the URL against: its stored settings (`fromAnalysis: true`), or the engine's
  defaults before the first analysis (`fromAnalysis: false`). They include an image's (`largeImageBytes`, which
  `image-too-large` goes by, and `imageAltMaxLength`) and a URL's (`maxUrlLength`).
- **`links`** comes from the URL summary, and is null without one (`summary` says why). `type` is judged as the
  grid's column is.
- **`headers`** are as stored: names lowercased, for internal URLs only. **`structuredData`** is each JSON-LD block,
  `valid` when it parses, with its `@type`s (those in a `@graph` included), or the parser's `error`; `truncated` when
  the page's block was cut at the parser's limit. **`hreflang`** is each annotation `{ language, href, url, target, inHead,
  fromHttpHeader }`, `target` being the URL it names in the crawl, with its status, or null.
- **`issues`** are the checks that flagged this URL in the last analysis, errors first, with the other URL involved (the
  first, where there are several: "3 broken URLs" names the first of them). **`namedBy`** counts the issues on other
  URLs that name this one among the URLs they're about, by check: every page linking to a 404, whether or not it's the
  first broken link on the page; the pages canonicalised to it; the other pages of a duplicate cluster it's the first of.
  Both come from the last analysis whatever its state, as the Issues screen's do, and are null before the first.
- **`source.stored`** says whether `/source` has the HTML.

`POST /api/crawls/{id}/urls/inlinks` and `/urls/outlinks` page the links to and from the URL whose id is in the body, a
page's links to itself left out, in the order they were found:

```json
{"urlId":119,"kinds":["hyperlink"],"offset":0,"limit":100}
```

```json
{"rows":[{"id":7,"url":"https://www.example.com/blog/","kind":"hyperlink","text":"Older posts","position":"content",
          "flags":[],"followed":true,"fragment":null,"statusCode":200,"indexable":true,"fetched":true}],
 "offset":0,"total":3,"next":"…"}
```

- `id`, `url` and the status columns are the other end's: the linking page for an inlink, the target for an outlink.
  `kinds` filters by the kind of link (`hyperlink`, `image`, `script`, `stylesheet`, `iframe`, `media`, `icon`,
  `canonical`, `hreflang`, `pagination`, `metaRefresh`, `redirect`; every kind by default).
- `text` is the anchor text of a hyperlink, the alt text of an image, or the language of an hreflang link. `position`
  is `content`, `navigation`, `header`, `footer`, `sidebar` or `head`. `flags` are among `noFollow`, `sponsored`,
  `ugc`, `imageLink`, `outsideHead`, `samePage` and `fromHttpHeader`; `followed` is whether search engines follow a
  hyperlink, which a nofollow on its page stops too.
- Paging is as the URL grid's: `offset`, `limit` (1 to 1,000), and `after` with the `next` cursor. Name each kind once:
  one named twice, or a null, is a field error (`kinds[2]`).

`POST /api/crawls/{id}/urls/source` with `{ "urlId": 119 }` gives the stored HTML, `{ "stored": true, "html": "…",
"bytes": 96340, "truncated": false }`, or `{ "stored": false, "reason": "notStored" | "notPage" }`: the crawl didn't
store HTML (`--store-html` is off by default), or this URL has none (not a page fetched successfully). It's written as
it's unpacked, a piece at a time, so a view of a 16 MB page holds a few megabytes (4 MB), not the page; it's escaped
as every answer is (`<` as `<`), so its bytes hold no markup. HTML over 16 MB unpacked is cut there, on a
character's boundary, and `truncated` says so; `bytes` is its size unpacked, before any cut, or null for a page over
512 MB, which isn't unpacked that far. A page whose stored HTML is damaged is `file-error`.

### Issue groups (E3a)

A check's issues grouped by their cause, so a template fault is one row, not 265. How a check's issues group is the
catalog's to say, per check, and `GET /api/checks` gives it as each check's `grouping`:

| `grouping` | The check's issues are grouped by | Checks like |
| --- | --- | --- |
| `detail` | The detail and the other URL involved: the detail names the cause, or the fault it's an instance of | `open-graph-missing` ("Missing og:image"), `internal-client-error` ("HTTP 404"), `structured-data-invalid` |
| `relatedUrl` | Each other URL the issue is about: the detail is a count, and the URLs are the cause | `broken-internal-links` (each broken target), `empty-anchor-text`, `canonicalised-page`, `title-duplicate` (the cluster, named after its first page) |
| `none` | Nothing: the detail is a measurement (78 characters, 1,250 ms, 3 images), or the same on every issue | `title-too-long`, `slow-response`, `noindex-page` |

The UI lists a `none` check's URLs flat (`/rows` below), with the detail as a column.

`POST /api/crawls/{id}/issues/{checkId}/groups` takes `{ offset, limit }` (limit 1 to 1,000, default 100):

```json
{"checkId":"empty-anchor-text","grouping":"relatedUrl","total":265,"groupCount":2,"offset":0,
 "groups":[{"detail":null,"relatedUrlId":1,"relatedUrl":"https://www.example.com/","pages":264,"share":0.9962,"likelyTemplate":true},
           {"detail":null,"relatedUrlId":31,"relatedUrl":"https://www.example.com/greenhouses/","pages":1,"share":0.0038,"likelyTemplate":false}]}
```

- Groups come largest first, then by detail and related URL. `total` is the check's flagged URLs, and `share` a
  group's part of them. A `relatedUrl` check's groups have no `detail`; a `none` check has no groups.
- **A `relatedUrl` check groups a page under every URL its issue names**, not just the first one `/rows` and the
  inspector show. A page linking to three broken URLs is in the group of each, and a broken link in the footer of
  every page is one group of every page. So the groups' `pages` add up to more than `total`, and their shares to more
  than 1. The group without a `relatedUrlId` holds the issues that name no other URL: for `broken-fragment-links`,
  links to sections missing on the page itself, as a template's "skip to content" makes on every page (a page missing
  sections there and on another page is in both groups).
- **Duplicates are grouped by cluster**: `title-duplicate`, `description-duplicate`, `h1-duplicate`,
  `duplicate-content` and `near-duplicate-content` have a group per set of pages that share the title (description,
  H1, content), named after its first page, which is in it too.
- **A few `detail` checks group by the fault each page's detail is an instance of**, as the detail itself differs on
  every page, and the group's `detail` is the fault while its rows' `detail` stays each page's own. A fault a template
  writes on every page is then one group:
  - `canonical-relative` by the form of the URL: `Root-relative: starts with /`, `Protocol-relative: starts with //`
    or `Relative to the page's own address`;
  - `canonical-invalid` by what's wrong: `Empty`, `Uses javascript: instead of http(s)` (whatever the scheme) or
    `Malformed URL`;
  - `structured-data-invalid` by the parser's message without where it stopped, nor the character it stopped at:
    `'…' is invalid after a value. Expected either ',', '}', or ']'.`.
- **`likelyTemplate`** marks a group that one fix probably clears, being in the site's template: it holds at least half
  of the check's flagged URLs, and at least 10 of them, and either they share the other URL (the logo link without
  text, a canonical every page names, the page itself for `broken-fragment-links`) or the detail names a fault in the
  page's own HTML (every page "Missing og:image"). A detail that says how the URL itself answered (every 404 says "HTTP
  404") is no evidence of a template. Where a check's detail takes only a value or two (`h1-missing`: no H1, or an
  empty one; `snippet-blocked`), the hint says no more than that most of its pages share the fault.
- A skipped check has `total` and `groupCount` null and no groups, never 0. `check-not-found` is a check the engine
  doesn't know, and `not-analyzed` a crawl never analysed.
- A crawl analysed by an engine from before these groups is grouped as that engine grouped it, by each issue's own
  detail and first other URL, until it's analysed again (it's out of date: `engineChanged`).

`POST /api/crawls/{id}/issues/{checkId}/rows` is the URL grid for one check: a row per URL the check flagged (an issue
is one check on one URL, so a page linking to three broken URLs is one row, "3 broken URLs", naming the first). It
takes the URL grid's query (`columns`, `filters`, `search`, `sort`, `offset`, `limit`, `after`) and answers as it does,
with `matching` the rows the filters match and `total` the check's flagged URLs, cached alike. Two more things:

- **The issue's own columns**, for `columns`, `filters` and `sort`: `detail` (text), `relatedUrlId` (number), and
  `relatedUrl` and `relatedStatusCode` (text and number, the other URL and its status, filters only). Without
  `columns`, a row has `url`, `detail` and `relatedUrl`.
- **`group`** narrows the rows to one of the check's groups: `{ detail, relatedUrlId }` as `/groups` gave it, where
  null matches null. A `relatedUrl` check's group is its `relatedUrlId` alone: the pages whose issue names it among its
  URLs. A fault's group is its `detail`, the fault. `total` is then the group's. A `none` check has no groups to narrow
  to.

```json
{"group":{"detail":null,"relatedUrlId":1},"columns":["url","inlinks","detail"],"sort":[{"column":"inlinks","desc":true}],"limit":100}
```

```json
{"columns":["id","url","inlinks","detail"],
 "rows":[{"id":2,"url":"https://www.example.com/about/","inlinks":264,"detail":"Links to 1 URL"}],
 "offset":0,"matching":264,"total":264,"next":"…","summary":{"available":true,"reason":null},"unavailable":[]}
```

A skipped check has `total` and `matching` null and no rows. Without a URL summary, the URL columns are the crawl's as
in `/urls`: the issues are the last analysis's, as on the Issues screen.

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

## Engine changes from the E2 review

Each has a test that fails without it. `AnalysisFreshness.VERSION` is 2, so every earlier analysis is out of date
(`engineChanged`) and its summary isn't read until the crawl is analysed again.

- **The summary never claims what wasn't read.** A page that was only status-checked (outside the start folder, an
  iframe's, another site's) has no indexability unless its response rules it out, and no outlinks; `in_sitemap` is
  null unless the URL is listed or every sitemap was read; `depth` is null for a URL only a sitemap lists. It gains
  `source`, so grids can hide or label the probes of the site's other versions.
- **A stale summary isn't read.** `IsAvailable` is false once the crawl changes after its analysis, and for a summary
  another analysis version stored. The crawl's stamp only moves forward, so a clock set back can't hide a change.
- **The stats agree with the summary.** The indexability breakdown leaves out the probes and the URLs not checked,
  and `notCheckedCount` gives those. The benchmark plants such URLs (a dead twin) to prove it.
- **A failed rule's checks are skipped.** `rule-failed`, rather than shown as run and clean. A newer engine's dropped
  check isn't called newer than the analysis.
- **Missing settings take their defaults.** Options and thresholds stored by an earlier engine, and saved defaults,
  read a property they lack as its default, never 0 or false.
- **A busy file is said to be busy.** An analysis or a continue that meets someone part-way through reading a closed
  crawl gives up within seconds with `crawl-busy` (`errorType` on the `crawl` event for an analysis).
- **One snapshot.** `GET /api/crawls/{id}/analysis` reads the analysis, its counts, notes, skip reasons and the
  crawl's stamp in one transaction (`IssueReader.ReadResults`).

## Engine changes E3a made

Each has its own tests, and each of those tests was seen to fail with the code it covers broken.

- **The grids' readers** in `Storage/`: `UrlGridReader` (`Query`, `Facets`, `Lookup`), `UrlInspectorReader` (`Read`,
  `ReadLinks`, `ReadSource`) and `IssueGroupReader` (`ReadGroups`, `ReadRows`), with the query model in
  `Storage/Models`. A query that can't run throws `UrlQueryException`, its messages by field (`invalid-query`); one
  that needs a URL summary there isn't throws `NeedsAnalysisException` (`needs-analysis`). The sidecar keeps one
  `ReadCache` for every crawl, so a filter set is counted once while its crawl stands still.
- **The URL summary** gains `title_length` and `description_length`, measured as the title and description checks
  measure them, so the grid sorts and filters on them. `AnalysisFreshness.VERSION` is 3: every earlier analysis is out
  of date (`engineChanged`) until analysed again.
- **Issue groups.** Each check in the catalog says how its issues group (`IssueDefinition.Grouping`, which
  `GET /api/checks` gives as `grouping`), and each analysis adds the index `ix_issues_group` on
  `issues (issue, detail, related_url_id)`.
- **`si readbench <crawl>`** times the grids' reads as the UI makes them and fails over a budget (`--max-p95-ms`), and
  **`si bench --db <file>`** keeps its crawl for it. CI runs both on the 100k crawl. Its cases were rare text filters
  only, and a common one took 0.9–1.7 s on 1M (three, up to 6 s): the review's fixes below brought every read of 1M
  under its budget but those they name, and gate CI by each class of read's growth
  ([Grid reads](../engine/README.md#grid-reads)).

## Engine changes E4 made

Each has a test that fails without it.

- **Richer progress.** `CrawlProgress` gains `Phase` (`CrawlPhase`), `StatusClasses`, `StatusCodes`, `NoResponse`,
  `InternalFound`, `InternalFetched` and `Waits` (`CrawlWait`). The writer counts as it stores each response, by outcome
  and status code (one slot per code, so counting never allocates), and a resumed crawl restores the counts grouped the
  same way. The site's own URLs are counted through every way one changes: found, skipped for a limit, queued again, a
  probe adopted, a response dropped to fetch the URL again. Every end-to-end crawl, resumed ones included, checks its
  last progress against SQL over the file.
- **Phases and waits.** The dispatcher sets the phase as it goes; `readingRobotsTxt` is worked out from two counters
  (URLs waiting for robots.txt, URLs being fetched). The waits read the site's host gates (`HostGate.ReadState`), the
  outages when there are any, and robots.txt's cool-down only while something waits for it (the review below changed
  when, and capped the gates' waits).
- **Alerts kept with the crawl.** `CrawlNotice` gains `Id`, `At` and `Data` (`CrawlNoticeData`). Each notice is stamped,
  numbered and queued for the writer, which stores it with its next batch and stores what's left as it closes, so a
  notice is never lost to a full channel and never waits on SQLite. The `notices` table is created
  `IF NOT EXISTS` whenever the writer opens a file, so the crawl's schema version is unchanged, and
  `CrawlNoticeReader.Read` finds none in a file that never had it. A continued crawl numbers on from the last id.
- **History.** `CrawlHistoryReader.Read(path, before)` counts a crawl's fetching back from its file, a second at a time,
  downsampled by `DownsampledSeries<T>`, which the sidecar's live history uses too.
- **Analysis progress.** `CrawlAnalyzer.AnalyzeAsync(path, settings, progress, cancellationToken)` reports
  `AnalysisProgress(Phase, Done, Total)`: `reading` (URLs), `pageRules` (pages, counted ahead from the URL table),
  `siteRules` (the cross-page rules, 9), `urlSummary` (rows), then `writing` (1, ending once the file is closed). It
  reports on the analysis's own thread, in order: the sidecar reports straight from it, as `Progress<T>` would hand each
  report to the thread pool, which can reorder them. The overload without it is unchanged.

## Engine changes from the E4 review

Each has a test that fails without it, but for reading the workers' two counts in one go, a race too narrow for a test
to catch. The schema version is unchanged, and no lock or allocation was added per response.

- **One event's numbers agree.** Only the writer's thread changes the counts, as plain fields; after each batch it
  stores, it publishes them as one immutable record (`CrawlCounts`), and every progress report is built from one record.
  The workers' counts of URLs waiting for robots.txt and being fetched are read in one go too. A test reads progress as
  fast as it can through a 4,000-page crawl and finds every report adding up.
- **`retrying` ends.** It lasts while the held-back URLs' last tries are being handed out or are in flight; a claim that
  returns URLs after that is `crawling` again. A retried URL leaves the held-back ones before it is recorded, so the
  claim that returns what it led to sees the tries over. `robotsTxtCoolDown` is listed in any phase while URLs it
  refused, or the sitemap discovery, wait for it.
- **A URL limit is never passed.** Once it's reached, a probe of the site's other versions that the site leads to stays
  a probe, with its answer, and isn't fetched again. Every end-to-end crawl with a limit checks it.
- **Waits name whose robots.txt**: the site's per host (`robotsTxt`), other sites' apart (`otherSitesRobotsTxt`). They
  name hosts as alerts do (`CrawlScope.HostName`), keep to five per reason with a count of the rest (`MoreWaits`), and
  the finishing phase lists none. `CrawlProgress` gains `MaxUrls`.
- **History counts the site's URLs found**, live and from the file (`CrawlHistoryPoint.InternalFound`); the sidecar keeps
  a closed crawl's history until its file changes, for the 16 crawls read last.
- **Alerts.** `GET /alerts` takes the unstored alerts before reading the file. The reader reads a notices table another
  program wrote as well as it can and one of another shape as empty; the writer refuses to continue a crawl whose notices
  table it couldn't store a notice in. A `blocked` alert's host and count go by the host's name, so www and the bare
  domain count together. The sidecar merges the unstored alerts by `id` and `at`.
- **`hello`** lists the crawls being analysed.

**Paused isn't stored, on purpose.** A pause lives only in the process running the crawl, and the file says `running`
throughout, as it always has. Storing it would add one thing: another program looking at the file while it's paused
here could say "paused" rather than "in use". Everything else stays as it is. Once the process ends, a crawl is either
stopped (quitting stops it, as the IA has it) or interrupted (a crash), and both resume the same way; a stored pause
would only make "Continue" ask whether to start paused, which nobody wants. It would cost a write, and a new stamp on
the crawl, at every pause and resume. The engine still reads a stored `paused` status as running or interrupted, as
it does today.

## Engine changes from the E3a review: queries and HTTP

Each has a test that fails without it. These changes leave the crawl's schema unchanged. Measured on the 1M crawl (1,123,028 URLs),
p95 on a 32-core machine, against `e81a98a`:

- **Text filters, regular expressions and searches are read once into sets.** A common one cost 0.9–1.7 s a read on
  1M (three, 3.2–6 s), re-scanning the crawl's tables three or four times: each is now read once into a set of URL ids
  (`UrlIdSet`), kept per filter set, which gives its count and which each row is tested against. Common text filters
  and searches take 0.15–0.48 s, three of them 0.53 / 0.64 s, a common pattern with a sort without an index
  0.62 / 0.72 s (first page / half way); the bench now times them.
- **No pattern can hang a read.** Each match stops after 100 ms and a read's matches after 3 s: the review's patterns
  that ran 0.6 s to 230 s are refused in about 0.1 s, `invalid-query` on their filter's `value`. A pattern too large to
  build is called too complex, not "needs backtracking".
- **A request given up stops its read.** Every reader takes a `CancellationToken`, and the sidecar passes the request's:
  the statement is interrupted. Twenty pages aborted in a fling cost 0.2–0.3 s of CPU, not 51 s, and the page the
  user stops on takes 0.18 s after them (it took 2.2–2.6 s), on 32 cores and on 4.
- **Grid reads queue** (`SidecarOptions.GridReads`, one a core), each on a thread of its own, so `/api/health` answers in
  4 ms during 20 heavy reads on 4 cores (it took up to 0.76 s). **Counting is shared**: 20 requests for one new filter
  set at once cost 0.7 s of CPU, not 41 s.
- **A writer here never waits on a grid** (`CrawlReadGate`): an analysis or a continue starting during a read on a
  closed crawl opens at once, where it was refused after 2.2 s; the read runs again after. A crawl closing switches
  back to one file even while grids read it.
- **Cursors are short and never stale**: a row's id and a hash of its sort values, under 100 characters, so a cursor
  after a URL of thousands of characters reads on (it was refused), and one whose row has changed or gone is refused.
- **`/source` is written as it's unpacked**: six views of big pages at once add 40 MB to the sidecar, given back, where
  they added 2.4 GB and kept it. A cut at 16 MB keeps a page's own characters at the cut.
- **The inspector, links and source are POSTs to fixed URLs** (`/urls/inspect`, `/urls/inlinks`, `/urls/outlinks`,
  `/urls/source`, the URL's id in the body): moving from URL to URL costs no preflight, and an id past what an id holds
  is `url-not-found`.
- **Stored values this engine doesn't know read as null**, in every reader: a kind of link, a position, an outcome, a
  source, an infinite number. A null in a request's list, and a field an operator doesn't take, are field errors; a
  kind named twice is refused. Every answer carries `nosniff` and a `default-src 'none'` policy.
- **The check filter without a summary** reads the last analysis's issues, as `/rows` does.
- **Without the URL summary** (a crawl running, or continued since), a running crawl's counts and facets are kept for
  2 s, then counted again; deep pages of a sort are picked out in memory, and the facets counted in one pass. Pages
  take 0.4–0.9 s on 1M; **the facets are the one read still slow there**: 0.9 s for every URL and 1.3 s for the
  pages, as every row of the crawl's own tables is read and judged.
- **`si readbench`** times what it missed (common text filters and searches, three text filters, the slowest walk, the
  page just past half way, read from the far end, the most linked URLs' links by kind, the URL most issues name, and
  every read without the summary) and holds each class of read to its own gate at 100k (`--gate`, which CI runs), set
  from the class's growth from 100k to 1M, so a read that would miss 1 s at 1M fails at 100k.

## Engine changes from the E3a review: groups, the inspector and lookup

Each has a test that fails without it. `AnalysisFreshness.VERSION` is 4, so every earlier analysis is out of date
(`engineChanged`) until the crawl is analysed again; until then its groups and "named by" are read as before.

- **Every URL an issue names.** The rules that find several targets on a page (broken internal and external links,
  links to redirects, nofollow links, links without text, broken section links, broken and large images, hreflang
  without return links or to broken or non-indexable alternates, mixed content) name every one, and each analysis
  stores them in `issue_targets` (`issue`, `related_url_id`, `url_id`). `issues` keeps one row per page and check, with
  the first target, so issue counts don't change. The groups of a `relatedUrl` check, a group's rows (in `/rows`, and
  in `/urls` with the check filter's `group`), and the inspector's `namedBy` read it. On the garden crawl, all 9 broken
  internal URLs now have a group (5 had one), and all 126 large images (61 had).
- **Duplicates and missing sections.** A duplicate is stored under its cluster's first page, so a cluster is one group.
  A link to a section missing on the page itself no longer names the page as the other URL (`issues.related_url_id` is
  null, or another page whose section is missing): those pages are one group without another URL.
- **Faults.** `canonical-relative`, `canonical-invalid` and `structured-data-invalid` group by the fault their detail is
  an instance of (`IssueDefinition.FaultOf`, stored in `issue_faults`), so a template's fault is one group with the
  likely-template hint.
- **Indexes.** `issue_targets` is keyed for a check's groups and a group's rows, and indexed by the URL named, for
  "named by", which no longer reads every issue. The first analysis of a crawl adds `ix_links_to_kind` on
  `links (to_id, kind)`, so the inspector's links of some kinds to a URL are counted, and reached at any offset, on it
  alone.
- **Lookup** takes a path on the origin the start URL's redirects end at, takes text without a scheme, and finds a URL
  on the site's other versions, saying what was asked for (`requested`).
- **The inspector.** A file's noindex and canonical tests pass or fail on its headers; the canonical test names the
  canonical by its URL (`canonicalUrl`), whether the crawl has it or not; `limits` gain `largeImageBytes`,
  `imageAltMaxLength` and `maxUrlLength`.
- **Facets** count a value no column has, which only a crafted file holds, as unknown whichever way they're counted,
  and never as another value: a negative indexability reason no longer reads as `none`, nor a negative time as
  `0-249`. Its filter, `isEmpty`, opens only the nulls.
