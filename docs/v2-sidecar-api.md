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
- **E4** is marked below as well: the live data the crawl screens need. Progress by status class and code, with the
  crawl's phase and what it waits on; the history the "found and crawled" chart draws; the analysis's progress; and
  alerts kept in the crawl file.
- **E3, E5 and E6** add the grids, export, search and the favicon. They come from the brief's
  [engine work the UI needs](v2-ui-brief.md#engine-work-the-ui-needs) and the handoff's
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
  - Real responses repeat `Access-Control-Allow-Origin` and `Vary: Origin`, and carry `Cache-Control: no-store`.
  - No cookies and no credentials mode.
  - Chromium caches a preflight per URL, so query strings that change every call (paging) cost a preflight each. The
    grid queries (E3) are therefore `POST`s with a JSON body to one fixed URL per grid. The UI never sets
    `cache: 'no-store'` on a request, because that turns the preflight cache off.
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
    | `crawl-busy` | 409 | Running here or elsewhere, an analysis is running, another program holds the file's SQLite lock, or someone is part-way through reading the closed file, which keeps writers out |
    | `not-analysable` | 409 | The engine refuses: running or interrupted |
    | `file-error` | 400 | IO or permissions |
    | `not-analyzed` | 404 | The crawl has never been analysed |
    | `needs-analysis` | 409 | A grid sorts or filters on the URL summary while there is none to read: never analysed, analysed by another engine version, or changed since (E3) |
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
