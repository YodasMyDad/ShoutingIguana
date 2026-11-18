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
- **E5** is marked below: [export](#export-e5) of what a grid shows as CSV or Excel, the workbook of everything, and
  [the report](#report-e5), with each column's title and its values' words on the column lists.
  [Engine changes E5 made](#engine-changes-e5-made) has what was built and what was measured.
- **E3c** is marked below where it adds to an answer, each addition a new field and nothing taken away: what the
  Overview and inspector screens found missing once they were built on the real thing. `/stats` read fast and kept;
  the crawler's own probes said apart in the counts; the URLs crawled counted as the grid counts them; the time spent
  crawling; the inspector's totals of every kind of link, the URL's length, and what became of each link's other end.
  [Engine changes E3c made](#engine-changes-e3c-made) has what changed and what was measured.
- **E6** is marked below where it adds, each addition a new field or a new call: search as it's typed, the site's
  icon, the analysis's limits read and changed, a links view's ends and the URL grid narrowed to them, robots.txt's
  origins searched and filtered, the inspector's exact links to a sitemap and a robots.txt line, and a pass over the
  links split over connections. [Engine changes E6 made](#engine-changes-e6-made) has what changed and what was
  measured.
- **The score** is marked below too, each addition a new field: what Simple mode needs of the engine that isn't export
  ([the Simple design](v2-ui-simple.md#what-the-engine-needs)). A site's score, worked out and stored by the analysis;
  the checks' plain words; which checks are a fault of the whole site, and which are reported once; and a status
  code's name in an issue's detail. [Engine changes the score made](#engine-changes-the-score-made) has what changed.
- **The catalog's fixes** change no shape: one check's rule, what three checks do, two new skip reasons
  (`robots-txt-ignored`, `start-not-root`), one check's `subject`, what the inspector's `charset` holds, and texts.
  [Engine changes the catalog's fixes made](#engine-changes-the-catalogs-fixes-made) has them.
- **Evidence** (step 3b) is marked below, each addition a new field, a new column or a new default, nothing taken away
  but the order rows come in when none is asked for: what each check needs to show so a site owner can fix what it
  found from its own views ([the per-check table](v2-evidence.md)). Each check's evidence columns and target facts;
  five new URL columns (`redirectChain`, `robotsRule`, `usedOn`, `pixelWidth` and `pixelHeight`, `fetchError`);
  targets and groups that say what each URL is; the image checks naming their images; details that name what they
  count; a template hint for a check that doesn't group; rows by importance; and exports that carry it all.
  [Engine changes evidence makes](#engine-changes-evidence-makes) has the engine's side.
- **Pixel widths** (step 3b) change shapes the app reads, as the app and the sidecar ship together: the title and
  description limits are in pixels under new names (`titleMaxPixels`, with `pixelsPerCharacter` for the characters
  hint), `titleLength` and `descriptionLength` count characters, and the URL grid gains `titleWidth`,
  `descriptionWidth`, `titleInResults` and `descriptionInResults`; the inspector's measured texts gain `width` and its
  limits the new names. [Pixel widths](v2-pixel-widths.md) has the design as built.
- **The fix loop** (step 3b) is marked "(fix loop)" below, each addition a new field, a new body field or a new call:
  a crawl compared with the site's previous crawl (or another of its crawls) by check, by cause and by URL, and
  "Recheck these N pages", which fetches a problem's URLs again into the same crawl file, analyses it and says "4 of 5
  fixed". The design, with its reasons, is [the fix loop](v2-fix-loop.md); the engine's side is under
  [Engine changes the fix loop makes](#engine-changes-the-fix-loop-makes).

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
    | `invalid-settings` | 422 | The analysis's limits can't be saved as sent: `errors` is keyed by limit (E6) |
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
    | `sitemap-not-found`, `origin-not-found` | 404 | The crawl has no sitemap with this id, or no robots.txt for this origin (E3b) |
    | `not-found`, `check-not-found` | 404 | No such route, or no check with this id |
    | `invalid-export` | 422 | An export or a report that can't be made as asked: an unknown kind or format, a kind in a format it isn't written in, a field of another kind, a path that isn't a file of the format on this computer, examples out of range. `errors` is keyed by the field (E5) |
    | `export-not-found` | 404 | No export has this id: nobody started it, or it ended more than 64 exports ago (E5) |
    | `export-busy` | 409 | 4 exports are running already, another is writing the same file, or the sidecar is shutting down (E5) |
    | `export-not-writable` | 400 | The file can't be written there: the folder isn't there, the user may not write to it, or the file is read-only (E5) |
    | `export-file-in-use` | 409 | A file of that name is open in another program, as Excel holds a workbook it's showing (E5) |
    | `export-disk-full` | 507 | The disk filled up while the file was written; nothing is left behind (E5) |
    | `export-crawl-changed` | 409 | The crawl changed under the export: analysed again, continued or finished (E5). A pause doesn't |
    | `export-too-large` | 422 | More rows than an Excel file holds here (16 sheets of 1,048,575); CSV has no limit (E5) |
    | `check-skipped` | 409 | The last analysis didn't run the check, so it has no rows to export (E5) |
    | `crawl-not-comparable` | 409 | A comparison with a crawl of another site, one never analysed or whose analysis read no page, one live or interrupted, or one whose file is missing or unreadable (fix loop) |
    | `site-busy` | 409 | A recheck while another crawl of the same site (`siteKey`) is live here: the two would double the load on the site (fix loop) |
    | `recheck-not-latest` | 409 | A recheck of a crawl that isn't its site's latest (fix loop) |
    | `recheck-not-found` | 404 | The crawl file has no recheck with this id (fix loop) |
    | `recheck-unsupported` | 422 | A recheck of a check a recheck can't settle (its `recheck` is empty: `orphan-page`, `no-https`), or of a check the last analysis skipped (fix loop) |
    | `bad-request` | 400 | Malformed JSON, or a property the API doesn't know |
    | `unauthorized` | 401 | The token is missing or wrong |
    | `origin-not-allowed` | 403 | An `Origin` not on the allowlist |
    | `body-too-large` | 413 | A body over 1 MB |
    | `bad-host` | 421 | A `Host` other than `127.0.0.1` or `localhost` with the bound port |
    | `internal` | 500 | Anything else: the title is generic, and the detail is the exception message, never a stack trace |

  - An option value of the wrong type is a field error under `invalid-options`, not a 400.
  - In a grid query, a field whose JSON type is fixed (`limit`, `offset`, `desc`, `urlId`, `after`, a column's or a
    kind's name, a group's `relatedUrlId`, and E3b's `sitemapId`, `origin`, `line` and `reason`) is a `bad-request` 400
    when it has another; a filter's `value` and `values`, which may be text, a number or a boolean, are checked against
    the column, under `invalid-query` (E3a).
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

- `categories: [{ id, title, plainTitle }]`, where the title is a plain name such as "Meta descriptions", and
  `plainTitle` (the score) is the category in Simple's words, such as "Search descriptions";
- `checks: [{ id, category, severity, title, description, fix, why, learnMore, grouping, subject, siteWide,
  reportedOnce, plainTitle, plainWhat, plainFix, fixer, plainPassed, plainOneFix, plainLooksFor, evidence,
  targetFacts, clears }]`, in catalog order.
  `description` is what is wrong, `why` is why it matters and `fix` is what to do. `learnMore` is an absolute URL, or
  null when no authoritative page exists. `grouping` (E3a) is how
  the check's issues group: see [Issue groups](#issue-groups-e3a).
- **`references`** (found-on) is how the check's flagged URLs stand to the references between URLs, so the UI shows
  where to fix each: `sources` (each URL is something other URLs refer to: a 404, a redirect, a sitemap entry; read
  where it's found with [`/urls/found-on`](#where-a-url-is-found-found-on)), `targets` (each page holds the bad
  references: read what its issue names with [`/issues/{checkId}/targets`](#where-a-url-is-found-found-on)) or
  `none`. 13 checks are `sources`, 17 `targets` ([the list](v2-ui-ia.md#where-its-found-uifound-on)). Only how a
  check is shown: not stored with an analysis, nor in the checks' fingerprint.
  **(evidence)** 16 are `sources` and 20 `targets`: `http-page`, `url-uppercase` and `url-underscores` become
  `sources` (the links to update), and `image-missing-alt`, `image-alt-too-long` and `image-missing-dimensions`
  become `targets` (the images at fault: see [Issue groups](#issue-groups-e3a)).
- **`clears`** (step 3b, related checks) is the checks a fix of this one also clears, by id, as the catalog declares
  them: this check's `references` is `sources` (it flags URLs others refer to) and each check named is a `targets` check
  of pages, grouped by `relatedUrl` (it flags the pages that refer to them). `internal-client-error`,
  `internal-server-error` and `internal-unreachable` clear `broken-internal-links`, `image-broken`, `broken-resources`,
  `canonical-broken`, `hreflang-broken-target`, `pagination-broken` and `social-image-broken`; `redirect-chain` clears
  `links-to-redirects` and `pagination-broken`; `redirect-loop` clears `broken-internal-links` and `canonical-broken`;
  `redirect-broken` `broken-internal-links`, `image-broken`, `broken-resources`, `canonical-broken` and
  `social-image-broken`. Empty for the rest: a canonical
  loop's pages aren't also reported as canonicalised to a non-indexable page, and a canonical header on a redirect is
  the redirect's own fault.
  What a fix clears **on a crawl** is counted, never assumed: the analysis's `counts[].clears`. Only how a check is
  shown: not stored with an analysis, nor in the checks' fingerprint.
- **`evidence`** (evidence) is the URL columns that show what the check found, in the order to show them, beside each
  row's `url` and `detail`: `["title", "titleInResults", "titleWidth", "titleLength"]` for `title-too-long`,
  `["metaRobots", "xRobotsTag"]` for `noindex-page` (where the noindex comes from), `["redirectChain"]` for the
  redirect checks, `["robotsRule"]` for `blocked-by-robots`. Each is a column of [`/rows`](#issue-groups-e3a) and
  of [the URL grid](#urls-e3a), so a view asks `/rows` for `url`, `detail` and these. Empty for a check whose URL and
  detail say it all (27 checks have some); [the per-check table](v2-evidence.md#the-checks) has every one. A column
  that needs the URL summary has no values without one, and is listed in `/rows`' `unavailable`, as any is.
- **`targetFacts`** (evidence) is what to say about each URL the check's issues name, in the order to say it: the
  fields of a target on [`/issues/{checkId}/targets`](#where-a-url-is-found-found-on), and of a group's `related` URL
  on [`/groups`](#issue-groups-e3a). `image-too-large` has `["sizeBytes", "contentType", "pixelWidth", "pixelHeight",
  "img", "statusCode", "final", "position", "links"]`; `links-to-redirects` `["statusCode", "final", "text",
  "position", "links"]`, `final` being the URL to link to instead. Empty for a check that names no URL. A field
  that's null on a target (no `final` for a URL that doesn't redirect) is left unsaid.
- `evidence` and `targetFacts` are the catalog's, as `grouping` is: not stored with an analysis, nor in the checks'
  fingerprint.
- **`subject`** (E3b) is what the check's flagged URLs are, so the UI can say "pages affected, of the N pages crawled"
  only where that's true:
  - `page`: every URL it flags is a page, as the analysis counts them (`pagesAnalyzed`): an internal URL that answered
    with HTML the crawler read, and that the analysis audits. Its count is a part of the pages the analysis read. (The
    library's `pageCount` is the same pages since round 5: Cloudflare's `/cdn-cgi/` endpoints are in neither.)
  - `url`: it can flag any URL the crawl has: a file (`internal-client-error` flags a missing image, as its text says),
    a redirect, a URL that didn't answer or wasn't fetched, a URL on another of the site's versions. Its count is of
    URLs, not of pages, and may be more than the pages crawled.

  It is the catalog's to say, per check, as the grouping is: it isn't stored with an analysis, so it reads the same
  for an analysis by any engine.
- **`siteWide`** (the score) is true for a check that finds one fault of the whole site, not a page's:
  `robots-txt-unreachable`, `no-https`, `site-hidden-from-search`, `duplicate-site-version` and
  `robots-txt-unreachable-other-origin`. [The score](#the-score) counts such a check as the whole site, whatever its
  count.
- **`reportedOnce`** (the score) is true for a check whose rule reports one issue for the whole site, on the home page
  (the start URL when no home page was read; `site-hidden-from-search` is reported on a home page only), or one for
  each other version of the site: the five site-wide checks,
  and `robots-txt-missing`, `sitemap-error`, `sitemap-missing` and `security-headers-missing`. Its count isn't a share
  of the pages or the URLs, so the UI says "checked once, for the whole site" where "1 of 265 pages" would misread.
  Several origins or sitemaps at fault are one issue whose detail lists them; `duplicate-site-version` has one issue
  for each other version that answers, three at most. It is for wording only: the score never reads it. Every
  `siteWide` check is `reportedOnce`.
- **The plain words** (the score) are a check's texts for a reader who isn't an SEO, beside the technical ones and
  never in place of them ([the Simple design's template](v2-ui-simple.md#the-template)):

  | Field | What it is |
  | --- | --- |
  | `plainTitle` | The problem as its owner would say it: sentence case, no count, 60 characters at most |
  | `plainWhat` | What's wrong and why it matters, as 2 to 4 sentences, **a sentence a string**. The first stands alone, in 120 characters at most: lists show only it |
  | `plainFix` | How to fix it, as 1 to 4 steps, **a step a string**: each one sentence, which starts with a verb or with "If …," |
  | `fixer` | Who usually makes the fix: `you`, `developer` or `either` |
  | `plainPassed` | The good news when the check finds nothing, as a fact: "Every page has a title" |
  | `plainOneFix` | Optional. What a fault in the site's template usually is, in one sentence |
  | `plainLooksFor` | Optional. What the check looks for, as a noun phrase of 60 characters at most ("A missing sitemap"): for a list of checks that couldn't run, where a title that is a sentence would read as a finding |

  - **Every check has them.** All 87 checks have `plainTitle`, `plainWhat`, `plainFix`, `fixer` and `plainPassed`.
    `plainOneFix` is filled for 26 of them and `plainLooksFor` for 12, and each is null for the rest.
  - **All or none, still.** A check has every one of the five, or none of them, and **a check with no plain words
    says null in every plain field**, the two optional ones included: never an empty string, never the technical
    text. No check of this catalog is without (a test holds that), and a UI should take null all the same: a check
    added to the catalog before its words are written would say it.
  - **Limits are filled in.** A plain text names a threshold as a token (`{limit:titleMaxPixels}`), and the API serves
    it filled, with the value and its unit as a sentence says them: "200 words", "200 KB", "1 MB", "1 second", "1.5
    seconds", and a title's or description's width in pixels as the ordinary characters it holds, "about 60
    characters" ([pixel widths](v2-pixel-widths.md#6-settings)). No served text holds a token. **`GET /api/checks`
    fills them from the thresholds an analysis would run with now; `GET /api/checks?crawl={id}` from the ones that
    crawl's last analysis ran with**, so a crawl's words say the limits it was judged by. A crawl never analysed, or
    whose stored settings can't be read, takes the current ones. `crawl` is one crawl's id: `crawl-not-found` when no
    crawl has it, `bad-request` when it is empty or given twice. Everything else in the answer is the same for every
    crawl.

    Ten checks name a limit (`title-too-long`, `title-too-short`, `description-too-long`, `description-too-short`,
    `low-word-count`, `image-alt-too-long`, `image-too-large`, `url-too-long`, `slow-response`, `large-page`). For a
    crawl analysed with titles to 658 px and 2.5 seconds to answer, `title-too-long`'s first sentence is "These pages
    have a title too long to show in full in search results, where there's room for about 70 characters." and
    `slow-response`'s "These pages took more than 2.5 seconds to arrive when the check asked for them."; without
    `crawl`, at the defaults, they say "about 60 characters" and "1 second".
  - A category's `plainTitle` is filled for all 17. Like every plain word it is words, not results: none of them is
    stored with an analysis, and writing one makes no analysis out of date.

Both come from `IssueCatalog`, the plain words from `IssuePlainTexts` and `PlainWords` beside it. The engine gets
public category titles, so the UI hard-codes none.

### Crawl options (E1)

| Method and path | Does |
| --- | --- |
| `GET /api/crawl-options/defaults` | `{ options, speedPresets }`: the user's saved defaults, or the engine's |
| `PUT /api/crawl-options/defaults` | Saves "my defaults". The start URL, header values and proxy credentials are never saved: the UI keeps secrets in the OS credential store, with the user's consent |
| `POST /api/crawl-options/start-url` | Round 5: `{ address, options? }` gives `{ startUrl, reason }`, 200: where an address typed without a scheme starts. An address with its scheme comes back as typed (`reason` `typed`, nothing asked). Otherwise the site's home page is asked for on https and http at once, with `options` (the saved defaults when left out: their proxy, user agent and timeout, never their extra headers), each for at most 10 s: https when it gives any HTTP status (`https`), else http when it does (`http`), else https (`noAnswer`, which the crawl then reports as not answering). Through a proxy, the proxy's own answers are not the site's: a 407 (the saved defaults mask its password, so the app sends them with its secrets restored), a refused tunnel, and a 5xx over plain http through an http proxy (its gateway error, or the site's own: no proof either way, so https) (round 5's review, R2-1). Why: a site that only serves http, typed without `http://`, was crawled on https, read nothing and was blamed on robots.txt (the closing audit's gap 1). No `address` is a `bad-request`; `options` a crawl would refuse (an unusable proxy, say), their start URL aside, are `invalid-options` (422) with the field messages, and nothing is asked (R2-7: they reached the fetcher, which threw a 500) |
| `POST /api/crawl-options/validate` | `{ errors: { <field>: [messages] } }`, 200. Field names are the JSON names of `CrawlOptions` properties; a value of the wrong type is a message for its field too. A body that isn't JSON, or a property the options don't have, is a `bad-request` (400), as on every other call. The engine gets a field-keyed `Validate`, and the CLI keeps its list of messages |

The options DTO has every public `CrawlOptions` property. `extraHeaders` is an object of name to value, and
`trustedCertificate` is never exposed.

`speedPresets` sets concurrency and a rate. These values are provisional until the IA sign-off:

| Preset | Concurrency | Rate |
| --- | --- | --- |
| `polite` | 2 | 2/s |
| `standard` | 5 | no cap (the engine's defaults) |
| `fast` | 16 | no cap |

### Proxy test (round 5)

| Method and path | Does |
| --- | --- |
| `POST /api/proxy/test` | `{ proxyUrl, url }`: fetches `url` once through the proxy and answers 200 with `{ outcome, status, elapsedMs, message }`, whatever the fetch found |

- `proxyUrl` is the proxy as the options take it (`http`, `https` or `socks5`, with a host), its user name and password
  in it when it has them: they're used for this one fetch and kept nowhere, not even in the log. `url` is a whole
  `http` or `https` address without a user name. Anything else is a `bad-request` (400) whose title says what to type.
- The fetch is a crawl's (no redirect followed, the body never read, the crawler's user agent) through that proxy only,
  never the system's, with 10 s to connect to the proxy and 15 s in all.
- `outcome`: `ok` (the site's answer came back, any status, in `status`), `proxyAuthRequired` (407: the proxy wants a
  user name and password, or didn't take the ones given), `proxyRefused` (nothing answered at the proxy's address, or
  it couldn't be found), `proxyUnreachable` (no connection to the proxy in time), `siteUnreachable` (the proxy answered
  502 or 504, or couldn't open a tunnel to the site; `status` when there was one), `timeout`, `tls` (a secure
  connection couldn't be set up) and `failed` (anything else).
- `status` is null when no HTTP answer came back. `message` is one plain sentence the UI shows as it is: "The proxy
  answered and the site replied 200 OK in 182 ms.", "The proxy asks for a user name and password (407).", "Nothing
  answered at the proxy's address.".

#### SOCKS5 with a login (step 3b)

A `socks5://user:password@host:port` proxy logs in with that user name and password (RFC 1929), for the proxy test
and for every request a crawl makes: .NET's own SOCKS5 client does it from the proxy's credentials, which
`ProxyFactory` takes out of the address. Tested against a SOCKS5 server of the tests' own that requires a login
(`TestSite/Socks5ProxyServer`): a crawl through it reads the site exactly as a crawl straight to it does, every
connection logged in with the password as typed; the proxy test answers `ok` through it (`Socks5CrawlTests`,
`ProxyTesterTests`, `ProxyEndpointTests`). The engine renders no page in a browser, so nothing else needs the login.

A SOCKS5 proxy's refusal comes from .NET as a failed tunnel whatever it was; the engine reads which from the runtime's
own words (`SocksRefusals`) and says it plainly:

- a wrong user name or password: the proxy test's `proxyAuthRequired`, "The SOCKS5 proxy didn't accept the user name
  and password."; a crawl stores "The SOCKS5 proxy didn't accept the user name and password" as the request's error
  (its robots.txt answer's, when the start origin's robots.txt is what it asked first) and doesn't ask again as if it
  could pass;
- none given to a proxy that wants one: `proxyAuthRequired`, "The SOCKS5 proxy asks for a user name and password.";
- a proxy that let the crawler in but couldn't reach the site: `siteUnreachable`, "The SOCKS5 proxy let the test in,
  but couldn't reach the site." (a crawl: "The SOCKS5 proxy couldn't reach the site", asked again as any network
  failure is).

Before step 3b both refusals of a login read "The proxy answered, but it couldn't reach the site.".

### Library (E1)

`GET /api/crawls` returns `[CrawlListItem]`, newest first. It lists the `.sicrawl` files at the top of the data
folder, plus files opened from elsewhere, minus the ones removed from the library.

```json
{"id":"…","path":"C:\\…\\example.com-20261101-101500.sicrawl","fileName":"…","site":"example.com",
 "startUrl":"https://www.example.com/","status":"completed","startedAt":"…","finishedAt":"…","updatedAt":"…",
 "crawlingMs":41250,"fetchedUrlCount":4521,"crawledUrlCount":4516,"pageCount":265,"pagesAnalyzed":265,"issues":{"error":3,"warning":12,"notice":7},
 "analyzedAt":"…","score":{"value":85,"band":"good","partial":false},"outOfDate":false,"sizeBytes":18276352,"hasFavicon":true,"missing":false}
```

- **`site`** is the host without a leading `www.`. The UI groups crawls by it.
- **`crawlingMs`** (E3c) is the time the crawl has spent crawling, over every run of it: each run from its start to its
  end, less the time it was paused. Neither a pause nor the time between a stop and a continue is in it, so it's the
  figure for "crawled in 41 seconds", which `finishedAt` less `startedAt` isn't for a crawl stopped and continued. A
  running crawl's is the progress event's `crawlingMs`. **Null when it isn't known**: a crawl an engine from before
  E3c made, or made any run of, didn't keep the time that run took, so there's no total, and no part of one is given.
  A run cut short (the process died) counts up to when the engine last stored it, within about five seconds of its end.
- **`crawledUrlCount`** (E3c) is the URLs crawled: those the crawl requested, and those robots.txt kept it from
  requesting (stored as disallowed). It is the URL grid's `fetched` column: `{"column":"fetched","op":"is","value":true}`
  lists exactly this many, so it's the count to show as "N URLs". A running crawl's is the progress event's `crawled`.
  It's counted from the file as it's read, so every crawl has it, whichever engine made it.
- **`fetchedUrlCount`** is the URLs the crawl is done with: every URL with a stored outcome. That is the URLs crawled
  **and those that weren't checked** (never asked, their host having stopped answering: the grid's `outcome`
  `notChecked`, which it has as not fetched), so no filter on `fetched` gives it; `crawledUrlCount` is the grid's
  count. URLs only found (a nofollow link, past a limit) are in neither. It matches `si stats`'s outcomes added up and
  a running crawl's `fetched` progress. (The full count of every URL in the file is `/stats`'s `urlCount`.)
  **Why two counts** and not one made smaller: `fetched` is what the live figures add up to (the status classes, and
  with the queued, in-flight and skipped URLs, every URL found), mid-crawl and at the end, and a URL that wasn't
  checked has its place in those sums as `noResponse.notChecked`. Taking it out of `fetched` would break every sum a
  client makes with it, the UI's own among them, to save one subtraction; so the count that leaves it out has its own
  name.
- **`pageCount`** is the pages — internal URLs that returned HTML the crawler parsed, but for Cloudflare's own
  `/cdn-cgi/` endpoints — as in `si stats`, and the same definition a running crawl's `htmlPages` uses, so the count
  doesn't shift when a crawl ends. **Since round 5 it is the analysis's definition too** (`CrawlSchema.PAGE`), so it is
  `pagesAnalyzed` while the crawl hasn't changed since its analysis: the 100k crawl reads 101,106 in both, where E7 had
  101,107 against 101,106 ([Engine changes round 5 made](#engine-changes-round-5-made)).
- **`pagesAnalyzed`** (E7) is the pages the last analysis audited, which its score covers: the analysis's own
  `pagesAnalyzed` and the score's `covers.pages`, as they stood when it ran. **Beside the score, show this one**, not
  `pageCount`. Null before the first analysis; a sidecar from before E7 doesn't send it.
- **`status`** is one of:

  | Status | Meaning |
  | --- | --- |
  | `running`, `paused`, `stopping` | Live, in this process |
  | `analyzing` | An analysis is running, in this process |
  | `rechecking` | A recheck is fetching some of the crawl's URLs again, in this process; `analyzing` follows it (fix loop) |
  | `stopped` | Stopped, resumable |
  | `interrupted` | The file says running, but nobody holds its lock. The engine gets a public lock probe for this |
  | `completed` | Finished |
  | `failed` | Failed |
  | `inUse` | Running in another process |
  | `unreadable` | The file couldn't be read (a lock held by another program, a damaged file, or a read that timed out) but may be a crawl. `error` says why in plain words. A lock or timeout is retried on a later list; a file that isn't a crawl at all (SQLite error 26, the wrong schema, or a view or trigger the engine never writes) is left out of the list entirely |

- **`issues`** counts checks with at least one issue, by severity: the Issues screen's rows, not issue rows. It is
  null before the first analysis.
- **`score`** (the score) is the last analysis's score in brief: `value` (0 to 100), `band` (`veryGood`, `good`,
  `needsWork`, `poor`) and `partial` (the crawl didn't read all of the site, so the score is of what it read so far).
  **Null when there is none**: the crawl was never analysed, its analysis read no page, or an engine from before the
  score analysed it. It is the stored score, so it stays as it was while the crawl is continued, with `outOfDate`
  saying the analysis no longer stands. The whole of it, and why there is none, is on
  [the analysis](#the-score). It is read from the analysis's own row, which the summary already reads, and kept in the
  summary cache: listing costs what it did.
- **`outOfDate`** (E2) is true when the last analysis no longer stands, and false before the first one. That's when:
  - the crawl changed after it: the crawl row's `updated_at` isn't the one the analysis kept, because the crawl was
    continued, here or elsewhere, or is running here;
  - this engine would judge the crawl differently: an engine with another analysis version or other checks ran it,
    as every engine from before E2 did;
  - or the analysis ran with other thresholds than the sidecar's, which Settings can change
    ([Analysis settings](#analysis-settings-e6)).

  `GET /api/crawls/{id}/analysis` gives the reasons.
- **`hasFavicon`** (E6) is true when the crawl kept the site's icon, which [`/favicon`](#favicon-e6) then gives; false
  for a site with no icon the engine keeps, for a crawl made before E6, and for a running crawl until its icon has
  arrived, a few requests after its first page. It is one lookup as the summary is read, and kept with it.
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
- When the crawl completes, it's analysed if `analyzeWhenDone` is set. Since round 5 (the closing audit's gap 2) a
  crawl the user stops is analysed too, the same way: its status goes `stopping`, `stopped`, `analyzing`, then
  `stopped` again with its results "so far" (a partial score), so Simple's Summary and Advanced's Overview have them
  when it ends. Why when it stops, not on first open: an analysis costs what a completed crawl's does (the 100k crawl's
  takes about 7 s), the user is watching the screen that turns into the results, and Home's score for the site is
  there without opening it. Not analysed then: a crawl stopped by the sidecar shutting down (the app quitting), a
  crawl that stopped itself (it gave up on a site that stopped answering: it is there to be continued once the site is
  back, so Continue isn't held up as busy; round 5's review, R2-6), and a crawl that failed or was interrupted; those
  say "No score yet" with Work out the results. While the analysis runs,
  the crawl is live (`crawl-busy` for remove, delete and continue), as after completing; cancel it to continue at once.

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

- **First.** A `hello` event, `{ live: [crawlIds], analyzing: [crawlIds], exports: [exportIds] }`, followed by the
  latest `progress` for each live crawl, then the latest `analysis` of each crawl being analysed, then the latest
  `export` of each export running. A reconnecting UI is up to date at once.
  `analyzing` (E4) lists the crawls being analysed here, so the `analysis` events that follow are for crawls the UI
  knows to be analysing, though it missed their `analyzing` event. `exports` (E5) lists the exports running here, by
  id, for the same reason.
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
| `progress` | `{ crawlId, status, elapsedMs, discovered, queued, inFlight, fetched, htmlPages, redirects, errors, blocked, robotsDisallowed, skipped, bytesDownloaded, urlsPerSecond, at, phase, internalFound, internalFetched, maxUrls, statusClasses, statusCodes, noResponse, waits, moreWaits, crawled, siteVersions, crawlingMs }` | Every 500 ms per live crawl (the engine's pace). The UI batches to 4 renders a second. `urlsPerSecond` is the rate of every URL fetched, pages and the rest alike. `elapsedMs` is this run's time, pauses included. The fields from `at` on are E4's, and the last three E3c's, below |
| `alert` | `{ crawlId, id, level, code, message, at, data }` | Each `CrawlNotice` (see below) |
| `crawl` | `{ crawlId, status, error?, errorType? }` | Every status change, with the library's statuses. An analysis shows as `analyzing`, then back to `completed` or `stopped` with a `library` event, and `error` if it failed. `errorType` is the problem type when the UI can act on it: `crawl-busy` when the analysis found the file busy (someone reading it, another program writing to it), within seconds, so trying again can work |
| `library` | `{}` | The library changed. The UI fetches `/api/crawls` again |
| `recheck` | `{ crawlId, recheckId, status, phase, done, total, result }` | Fix loop. While a recheck runs, as it starts, at most every 500 ms and as it ends; then once more when the analysis after it has settled it, with `result`. Never merged or dropped. See [Rechecks](#rechecks-fix-loop) |
| `analysis` | `{ crawlId, phase, done, total }` | E4. While an analysis runs: each phase as it starts (`done` 0) and as it ends (`done` equal to `total`), and at most every 250 ms between, the phases in the order under **Analysis progress** below. All of them come after the crawl's `analyzing` event and before the one that ends it |
| `export` | The export, whole: `{ exportId, crawlId, kind, format, path, status, rowsWritten, rowsExpected, startedAt, finishedAt, result, error }` | E5. As an export starts, at most every 250 ms while it writes rows, and as it ends (`done`, `cancelled` or `failed`). Never merged or dropped. Its exact shape is under **Exports** below; what an export is, under [Export](#export-e5) |

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
  robots.txt couldn't be read). `notChecked` is URLs never asked, their host having stopped answering, or asked and
  only ever answered 429 Too Many Requests, the site asking the crawler to slow down (their `error` starts "Not
  checked: the site asked the crawler to slow down"; one held in a section the site limits apart whose turn didn't come
  before the crawl gave up on the section goes on "(429 Too Many Requests), and the crawl gave up on the pages it
  limits before this URL had its turn"), or on a private network address a public site's crawl doesn't
  ask ("Not checked: private network address; not checked from a public site"): unknown, not broken, so the UI
  shouldn't call them errors. `networkError` doesn't include them.
- **`crawlingMs`** (E3c) is the time spent crawling so far, over every run of the crawl: the earlier runs' time, and
  this run's without its pauses, so it stands still while the crawl is paused, where `elapsedMs` (this run's alone)
  runs on. It ends at the library item's `crawlingMs`, to the millisecond. Null when an earlier run's time wasn't
  kept (see the library item).
- **`crawled`** (E3c) is the URLs crawled so far, as the URL grid counts them (`fetched` is true): `fetched` less
  `noResponse.notChecked`. It ends at the library item's `crawledUrlCount`, as `fetched` ends at `fetchedUrlCount`.
- **`siteVersions`** (E3c) is the part of the event's figures that is the crawler's own probes of the site's other
  versions (http or https, www or not: the URL grid's `source` `siteVersion`), which nothing on the site leads to:
  `{ fetched, statusClasses, statusCodes, noResponse }`, each shaped as the event's own and counted within it.

  ```json
  {"fetched":3,"statusClasses":{"2xx":0,"3xx":2,"4xx":0,"5xx":0,"other":0,"noResponse":1,"blocked":0},
   "statusCodes":{"301":2},"noResponse":{"robotsDisallowed":0,"timeout":0,"networkError":1,"notChecked":0,"processingError":0}}
  ```

  - **What the figures count.** Every figure of the event counts every URL the crawl has, its own probes among them,
    as the URL grid lists them: so each figure lands on a count in the grid, under the filter below, and `fetched`,
    the classes and what was found add up as they always have. The probes are the crawler's questions, though, not
    the site's URLs: the https probe of a site served over http only is a network error from the crawl's first
    seconds, and it isn't the site failing to answer. `siteVersions` says how much of each figure they are, so the UI
    shows the site's own (the figure less its part here) and says the probes apart, as it does the URLs that weren't
    checked.
  - **Each lands on its count, under its own filter.** The event's classes aren't the grid's `statusClass` values:
    the event has a class for block pages, which the grid files by their status (a firewall's 403 is among the grid's
    `4xx`), and it counts the URLs that weren't checked under `noResponse`, which the grid has as not fetched and so
    in no class. The grid's `outcome` column tells both apart, so each figure is the grid's `matching` under:

    | Figure | Filters on the URL grid |
    | --- | --- |
    | `crawled` | `fetched` is true |
    | `fetched` | No single filter: `crawled`, and the URLs under `noResponse.notChecked` |
    | `statusClasses["2xx"]`, `["3xx"]`, `["4xx"]`, `["5xx"]`, `.other` | `statusClass` in that class, **and** `outcome` notIn `blocked` |
    | `statusClasses.blocked` | `outcome` in `blocked` |
    | `statusClasses.noResponse` | `outcome` in `robotsDisallowed`, `timeout`, `networkError`, `processingError`, `notChecked`. (`statusClass` in `noResponse` gives it less `noResponse.notChecked`) |
    | `statusCodes["404"]` | `statusCode` eq 404, **and** `outcome` notIn `blocked` |
    | `noResponse.robotsDisallowed`, `.timeout`, `.networkError`, `.processingError`, `.notChecked` | `outcome` in that one. The grid's `networkError` leaves the URLs not checked out, as the event's does |
    | Any figure's part in `siteVersions` | The figure's filters, and `source` in `siteVersion` |
    | Any figure less that part: the site's own | The figure's filters, and `source` notIn `siteVersion` |

    Without the filter on `outcome` a class is out by the block pages with a status in it, and `noResponse` by the
    URLs not checked: on a crawl of the test site with one block page and five URLs not checked, `4xx` is 21 where
    the grid's `statusClass` gives 22, and `noResponse` 14 where it gives 9.
  - **Live and done agree.** `/stats` gives the same part of a crawl at rest (its `siteVersions`), and the URL
    facets count under any filter, that one included.
  - A probe the site turns out to link to, list in a sitemap or redirect to becomes one of the site's own URLs (its
    `source` changes), and leaves these counts as it does.
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
  | `backoff` | The site asked the crawler to slow down (429, or 503 with Retry-After) or kept failing, so the crawler learned a slower pace for it, which rises slowly while it answers well. Listed while that pace is slower than the crawl's settings | `host`, `durationMs`: the time between requests now (1000 / `durationMs` requests a second) |
  | `heldBack` | Requests to the host are held: it sent Retry-After, or it stopped answering and is asked again a minute after each try | `host`, `durationMs`: until they may go again |
  | `siteDown` | An origin of the site has stopped answering and is waited out. The crawl stops after 10 minutes of it, resumable. Only the one down longest is listed | `host`: the origin, its scheme and host, and its port unless it's the scheme's default (`https://example.com`, `http://127.0.0.1:8080`), `durationMs`: how long so far |
  | `sectionLimited` | The site limits one section apart from the rest (its product pages, say): its URLs are read one at a time, an interval apart it learns from the 429s and from what gets through, while the rest of the site goes on. Listed while any are left | `host`, `section` ("/products/"), `urls`: how many are left, `durationMs`: how long until the next is tried |
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
| `siteRules` | Running the checks across pages (duplicates, orphans, redirect chains, sitemaps...) once every page is read | Rules (10) |
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
- **`code`** is stable; key UI text and actions on it. Each says once what it's about: `blocked` and `rate-limited` once
  per host, and `robots-unreachable` once per origin, in each run, so a crawl continued while it's still blocked says so
  again.
- **`message`** is engine text and may include text from the site.
- **`data`** is what the alert is about, for the UI to offer its action. Fields that don't apply are left out; it's null
  for an alert without facts.

  | Code | Level | `data` | What the UI can offer |
  | --- | --- | --- | --- |
  | `blocked` | warning | `host`, `reason` (the firewall or challenge, "Cloudflare challenge"), `count`: the host's responses blocked, 1 on the live event (the one that raised it), and read with `GET /alerts`, the host's blocked responses in the whole crawl so far, every run's. A crawl continued while still blocked has a `blocked` alert for the host in each run, each with that same count, so the UI shows it once, on the latest | Continue more slowly, add headers, another user agent |
  | `mostly-forbidden` | warning | `host`, `count` (the 403s), `sampled` (of the site's first 20 URLs) | As for `blocked` |
  | `rate-limited` | info | `host`, `reason` (`429 Too Many Requests`), `durationMs`: the pause it asked for, when it did. Once per host in each run: the crawler waits as asked, goes on at a slower pace it learns, and asks again at the end for the URLs it was refused. Not a block | Nothing: it is handled |
  | `section-rate-limited` | info | `host`, `url`: the section ("/products/"), `reason` (`429 Too Many Requests`), `durationMs`: the first interval between its URLs. Once per section in each run: its URLs are held back and read one at a time, and the rest of the site isn't slowed | Nothing: it is handled; the `sectionLimited` wait says how it goes |
  | `rate-limited-unchecked` | warning | `host` (of the first such URL), `reason` (`429 Too Many Requests`), `count`: the site's URLs stored as not checked (`outcome` `notChecked`) because it kept asking the crawler to slow down: asked and still answered 429 at their last try, or held in a section it limits apart and never asked, the crawl having given up on the section before their turn came. The message says how many of each, how many times those asked were refused, and why the others weren't asked. Said at the end of a run, only when there are some | Crawl again later, or at the Polite speed |
  | `start-redirects-away` | warning | `url`: where the start URL leads | Crawl from there instead |
  | `robots-unreachable` | warning | `origin`, `reason` (`503 Service Unavailable`, or the error), `durationMs`: how long until it's asked again | Crawl again with robots.txt ignored |
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

#### Exports (E5)

An `export` event's data is the export, whole: the object `POST /api/crawls/{id}/export` answered with and
`GET /api/exports/{exportId}` reads, with the same twelve properties in the same order, every one of them always
there (a value not yet known is `null`, never left out). Nothing else is in it, and no other event tells an export.

```json
{"exportId":"3f9a61c2d0b4e87a","crawlId":"…","kind":"links","format":"xlsx",
 "path":"C:\\Users\\me\\Documents\\example.com 2026-10-31 links.xlsx","status":"running","rowsWritten":41000,
 "rowsExpected":5053909,"startedAt":"2026-11-01T17:42:00.118Z","finishedAt":null,"result":null,"error":null}
```

| Property | Type | Is |
| --- | --- | --- |
| `exportId` | string | 16 hex digits. What an export is keyed by: several can run at once, and for one crawl |
| `crawlId` | string | The crawl it reads |
| `kind` | string | `urls`, `links`, `sitemapUrls`, `robotsBlocked`, `issueRows`, `check`, `workbook` or `report` |
| `format` | string | `csv`, `xlsx` or `html` |
| `path` | string | The file it writes, as the sidecar resolved the path it was given |
| `status` | string | `running`, then once `done`, `cancelled` or `failed` |
| `rowsWritten` | number | Rows of data written so far, headers apart. 0 for the report throughout |
| `rowsExpected` | number or null | The rows it will write: null until its first page is read, and for the report throughout. Never less than `rowsWritten` |
| `startedAt` | string | When it started: ISO 8601, UTC |
| `finishedAt` | string or null | When it ended; null while it runs |
| `result` | object or null | Once `done`: `{ rows, bytes, sheets, unavailableColumns, truncatedCells, crawlWasRunning }`, three numbers, an array of column ids, a number and a boolean. `sheets` is 0 for a file that isn't Excel's |
| `error` | object or null | Once `failed`: the problem, `{ type, title, status }` with `detail`, `errors`, `reason` and `columns` where the problem has them, as a call's answer would |

```json
{"exportId":"3f9a61c2d0b4e87a","crawlId":"…","kind":"links","format":"xlsx","path":"…","status":"done",
 "rowsWritten":5053909,"rowsExpected":5053909,"startedAt":"2026-11-01T17:42:00.118Z",
 "finishedAt":"2026-11-01T17:42:20.530Z",
 "result":{"rows":5053909,"bytes":167143014,"sheets":5,"unavailableColumns":[],"truncatedCells":0,"crawlWasRunning":false},
 "error":null}
```

```json
{"exportId":"9c04e1b7a25d3f68","crawlId":"…","kind":"urls","format":"csv","path":"…","status":"failed",
 "rowsWritten":0,"rowsExpected":null,"startedAt":"2026-11-01T17:43:02.007Z","finishedAt":"2026-11-01T17:43:02.031Z",
 "result":null,
 "error":{"type":"needs-analysis","title":"…","status":409,"reason":"notAnalyzed","columns":["inlinks"]}}
```

- **When.** One as the export starts (`running`, `rowsWritten` 0, `rowsExpected` null), one as soon as its rows are
  counted, then at most one every 250 ms while rows are written, and one as it ends: the only event whose `status`
  isn't `running`, and the last. An export of a few rows is two or three events.
- **In order, and all of them.** One export's events are sent in the order its states were, and none is merged or
  dropped. The first is sent as the export is started, so it can reach the stream before the call that started it is
  answered: the UI keys what it shows by `exportId`, from whichever of the two it has first.
- **A stream that connects later.** `hello.exports` lists the exports running then, and each one's latest event
  follows. An export that ended while no stream was connected is in neither: the UI reads it once with
  `GET /api/exports/{exportId}`, which knows the last 64 that ended. `export-not-found` then means that 64 others
  have ended since, or that the sidecar that ran it has gone: a sidecar that is shut down cancels its exports, and
  one that dies leaves an export's file unwritten, never a short file under the name asked for. Its part files are
  deleted by the next sidecar to start (see **The job**, "A sidecar that is killed").
- **In the app**, the event is one of `SidecarEvent` (`app/src/api/types.ts`), its data an `ExportJob`
  (`app/src/api/exports.ts`). The live store reads it at the boundary (`parseExportJob`: an event that isn't an
  export is dropped), and hands it to its listeners (`onEvent`). It keeps none of it: showing an export's progress is
  its screen's.

### History (E4)

`GET /api/crawls/{id}/history` is a crawl's progress since it started, for the "found and crawled" chart. A UI that
opens mid-crawl draws it, then adds each `progress` event at its `at`.

```json
{"crawlId":"…","startedAt":"2026-11-01T14:13:02.114Z","liveFrom":"2026-11-01T14:13:02.114Z",
 "points":[{"at":"…","fetched":499,"htmlPages":420,"internalFetched":480,"internalFound":3912,"errors":3,"blocked":0,
            "elapsedMs":240000,"discovered":5384,"queued":3384,"skipped":1501}],
 "crawlingMs":251250,
 "runs":[{"startedAt":"2026-11-01T14:13:02.114Z","endedAt":"2026-11-01T14:18:13.364Z","crawlingMs":251250,
          "crawling":[{"from":"2026-11-01T14:13:02.114Z","to":"2026-11-01T14:15:02.114Z"},
                      {"from":"2026-11-01T14:16:02.114Z","to":"2026-11-01T14:18:13.364Z"}]}]}
```

- **`startedAt`** is when the crawl first started: the chart's zero.
- **`points`**, oldest first, end at the latest. A crawl live here has this run's points as the sidecar took them, one
  per `progress` event: at most 600 evenly spread, every half second at first, then every second, two seconds and so
  on, and the latest. Memory stays flat however long it runs.
- **`internalFound`** and **`internalFetched`**, on every point, are the progress event's: the chart's "found" and
  "crawled" lines meet the headline and its ETA.
- **`liveFrom`** is where the points taken live begin: the start of the run this sidecar is watching: when the sidecar
  was asked to run it, a moment before the engine's own start of the run in `runs`, which is when the crawl file was
  open and crawling began (continuing a big crawl reads its URLs first). Before it, and
  for a crawl not live here (`liveFrom` null), the points are counted back from the crawl file: a zero at `startedAt`,
  then at most 600 evenly spread over the seconds anything was fetched or found in, and the last.
- **What the file can say.** It keeps when each URL was fetched, so its points have `fetched`, `htmlPages`,
  `internalFetched`, `errors` and `blocked`. It doesn't keep when each URL was found, but it keeps the page that first
  linked it, and a URL is found as that page is fetched: so its points have `internalFound` too, counting each of the
  site's own URLs from that page's fetch (or its own, if that came first), and the start URL and the URLs only sitemaps
  list, which no page links, from the crawl's start, as the sitemaps are read then. A URL the crawl skipped isn't
  counted at any point, though a live count had one skipped for the URL limit until the limit was reached. The rest of
  what was found (`discovered`, `queued`, `skipped`) can't be counted back, so file points leave it out, and
  `elapsedMs`. A URL fetched twice (a status check made a full fetch) counts from its last fetch.
- **`runs`** (E3c) is when the crawl was crawling: each run of it, oldest first, from its `startedAt` to its
  `endedAt`, with the time it spent crawling (`crawlingMs`) and the stretches it spent it in (`crawling`, each
  `{ from, to }`): one stretch for a run never paused, and a pause is the gap between two. The top-level
  **`crawlingMs`** is their sum, as the crawl file has it: the library item's for a crawl at rest. While a crawl runs
  the item's is the progress event's, which is ahead of the file's by up to about five seconds (the stretch going on
  is stored that often), so the two differ until the run ends, and a client showing a live crawl's time takes the
  progress event's.
  - **In order, never overlapping.** A stretch begins by the wall clock and lasts by a clock that only runs forward.
    Where the wall clock was set back between two (a time sync), the later is given as beginning when the one before
    it ended, in one run or across two: none begins before the last ended, no run ends before it started, and each
    keeps its length, so the total is as stored.
  - **Time asleep isn't crawling.** A stretch leaves out any time the machine spent asleep in it (a gap of more
    than five minutes between two readings of its clock, which is read twice a second), so it can be shorter than
    the wall time from its `from` to the next thing that happened.
  - **Plotting crawled time.** A moment's place on an axis of time spent crawling is the stretches' time up to it:
    every stretch that ended before it, and the part of the one it falls in. A moment in no stretch (a pause, the
    time between runs, a site waited out isn't one: that's crawling) sits where the stretch before it ended, so the
    gaps close.
  - **A run that hasn't ended** has `endedAt` null: the run going now, or one cut short when its process died (the
    item's status says which). Its last stretch's `to` is as the engine last stored it, at most about five seconds
    before the present for a crawl that's running: a client drawing a live crawl takes the stretch to run on to
    its latest `progress`.
  - **Null when not known**, both of them: for a crawl any run of which was made by an engine from before E3c, whose
    time wasn't kept. A chart then has only clock time, as before. Also null past 1,000 stretches (a crawl paused a
    thousand times), which aren't listed; `crawlingMs` is still given then.
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
 "outOfDateReasons":[],"notes":["…"],"counts":[{"checkId":"title-too-long","count":12,"likelyTemplate":false,
   "causes":12,"templateCauses":0,"clears":[]},
   {"checkId":"internal-client-error","count":9,"likelyTemplate":false,"causes":9,"templateCauses":0,
    "clears":[{"checkId":"broken-internal-links","pages":5,"all":true}]}],
 "skipped":[{"checkId":"orphan-page","reason":{"code":"no-sitemap","text":"No sitemap could be read.",
   "plainText":"No sitemap, the list of your pages for search engines, could be read, so there was nothing to compare with.",
   "plainAction":"Add a sitemap, or fix the one that can't be read, then check the site again."}}],
 "crawledUrlCount":4521,"fixFirst":["internal-client-error","broken-internal-links","image-too-large","…"],
 "score":{"version":1,"value":85,"band":"good","lost":15,"beforeCeiling":85,"heldBySerious":false,"partial":false,
   "covers":{"pages":265,"urls":4521,"checksRun":83,"checksSkipped":4,"complete":true,"reason":null,"blockedByRobots":0},
   "lines":[{"checkId":"internal-client-error","severity":"error","count":9,"of":4521,"unit":"urls","extent":"few","points":2},
            {"checkId":"broken-internal-links","severity":"error","count":5,"of":265,"unit":"pages","extent":"some","points":4}],
   "ifFixed":{"checkIds":["internal-client-error","broken-internal-links","image-too-large","description-duplicate","title-duplicate"],"value":98}},
 "noScoreReason":null}
```

- **`counts[].likelyTemplate`** (evidence) is true for a check that doesn't group (`grouping` `none`), whose
  `subject` is `page` and which isn't `reportedOnce`, when it flagged at least 90% of `pagesAnalyzed` and at least 10
  pages: every page lacks a viewport, or x-default. It is probably the site's template, so one fix: the UI says so. False
  otherwise, and for every other check, whose groups say it instead (`likelyTemplate` on each group). Worked out from
  the counts whenever the analysis is read, so an analysis by any engine has it.
- **`counts[].causes` and `counts[].templateCauses`** (step 3b, headline counts) say what a check's issues come to as
  work: the separate things to change, and how many of those are likely in the site's template. They are counted from
  the check's groups as [Issue groups](#issue-groups-e3a) gives them: a group that shares a cause (one related URL, such
  as a broken link's target or a large image) or is `likelyTemplate` is one thing; every other flagged URL is one of
  its own. A check that doesn't group is one thing (in the template) when its `likelyTemplate` is true, else a thing a
  URL; so is a check grouped by how each URL answered (`internal-client-error`: two 404s are two fixes). **Never more
  than the URLs it flagged**: a page naming 30 images without dimensions is one page's markup to change at most (the
  garden crawl's 230 pages name 3,811 such images, mostly from shared templates no group can see), and a page linking
  to several broken URLs has them counted by the check of those URLs. So the logo link without text on 264 pages is 1,
  40 titles too long are 40. The UI says "about 40 things to change, 3 of them in the site's template", leaving out a
  check that another's fix clears entirely (`clears[].all`), as that's the same job. **Null** for a check this engine doesn't know, for a check that found nothing, and when another
  analysis finished while this one was read (read again). Kept with the grid reads' counts while the file stands still.
- **`counts[].clears`** (step 3b, related checks) is what a fix of the check also clears: for each check its catalog
  `clears` names that also found something, **`pages`**, the pages it flagged whose every target is a URL this check
  flagged (a page that also links to another broken URL keeps its issue, so isn't counted), and **`all`**, true when
  that is every page it flagged: the fix clears it entirely, and the UI folds it under this one. Only checks with
  `pages` above 0, in the order the catalog names them. Empty for every other check, and for an analysis from before
  every target was kept (`AnalysisFreshness.VERSION` 4), which kept only a page's first target and so can't tell.
- **`isPartial`** (E2) is true when a check was skipped or a rule failed on some pages; the notes say which.
- **`skipped`** is every catalog check missing from the counts. Its `reason` (E2) has a stable `code` to key on, and
  a `text` to show beside the check ("No sitemap could be read."). A check skipped for several reasons gets the
  first; the notes give them all. **`plainText` and `plainAction`** (the score) are the reason in plain words, by its
  code: why the check couldn't run, and what would let it, a sentence each. Every code this engine gives (the 15
  under **Skip reason**, below) has both. **Null, both, for a code it has no words for** (a later engine's, in a
  file that engine analysed), never the engine's sentence in their place.
  A limit in them is filled from the settings this analysis ran with.
- **`crawledUrlCount`** (the score) is the URLs crawled **as they stood when the analysis ran**: the library item's
  `crawledUrlCount`, as it was then. A check whose `subject` is `url` flagged a share of these, as a `page` check
  flagged a share of `pagesAnalyzed`: "9 of the 4,521 URLs crawled" is the analysis's 9 over the analysis's 4,521.
  **Use these two, not the library's counts, beside a check's count**: a crawl continued since has more URLs than its
  analysis saw, and the library counts those. Null when an engine from before the score ran the analysis.
- **`fixFirst`** (the score) is the checks that found something, in the order to fix them: the most severe first;
  within a severity, a fault of the whole site (a `siteWide` check) before any other; then the most URLs flagged;
  then catalog order ([The score](#the-score) says how it is made, and why). It is the engine's order, so every host
  agrees: **take the order from here, don't sort again by count**, which would put a site-wide fault last of its
  severity. **Checks the user has hidden are in it**: hiding is the app's own preference, which
  the engine doesn't know, so the UI takes hidden checks out of what it lists. A check a newer engine counted, which
  this catalog doesn't have, isn't in it. It is worked out from the counts whenever the analysis is read, so an
  analysis by any engine has it.
- **`outOfDate`** and **`outOfDateReasons`** (E2) are as the library item's: `crawlChanged`, `engineChanged`,
  `settingsChanged`. The UI offers to analyse again.
- **An earlier engine's analysis** never fails to read. Its `isPartial` and every `reason` are null, as it didn't
  store them, so the UI shows the notes. It's out of date with `engineChanged`, and analysing it again mends it. It
  has no score (`noScoreReason` is `notScored`) and no `crawledUrlCount`.

#### The score

One number, 0 to 100, for a site ([the Simple design](v2-ui-simple.md#the-score) has the formula and its reasons). It
starts at 100, and each check that found a problem takes points off: up to 10 for an error (60 if it is a fault of
the whole site) and up to 4 for a warning, by how much of the site it affects. While any error check has issues the
score can't be above 89, and notices take nothing off.

**The analysis works it out and stores it**, from its own counts, the pages it read and the URLs crawled as it read
them. What the API serves is that stored score: nothing in it is counted from the crawl as it is now, so a count from
one moment is never set against a total from another, and the app, the report and `si score` give the same number.

| Field | What it is |
| --- | --- |
| `version` | The formula's version: 1. A changed formula is a new version |
| `value` | The score: 100 less `lost`, no lower than 0, and no higher than 89 while an error check has issues |
| `band` | The score as a word: `veryGood` (90 and over), `good` (70 and over), `needsWork` (50 and over), `poor` |
| `lost` | The points every line takes off, added up. It can pass 100 |
| `beforeCeiling` | The score before the ceiling: 100 less `lost`, no lower than 0 |
| `heldBySerious` | The ceiling is what holds the score: it would be `beforeCeiling` with no error check's issues standing |
| `partial` | The crawl didn't read all of the site, so the score is of what it read so far. Always `!covers.complete` |
| `covers.pages` | The pages the analysis read: `pagesAnalyzed`. What a page check's count is a share of |
| `covers.urls` | The URLs crawled when the analysis ran: `crawledUrlCount`. What a URL check's count is a share of |
| `covers.checksRun`, `covers.checksSkipped` | The checks that ran, whatever they found, and the ones the analysis couldn't run. A skipped check takes nothing off and adds nothing |
| `covers.complete` | The crawl read all it set out to: it finished, and no limit kept internal URLs out |
| `covers.reason` | Why not, or null when it is complete: `stopped` (stopped, or it stopped itself, before it finished), `failed`, `urlLimit` or `depthLimit` (it finished, but the limit kept internal URLs out). A crawl kept to its start folder is complete: that is its scope |
| `covers.blockedByRobots` | The site's URLs robots.txt kept the crawler out of, which weren't read and so aren't covered: the `blocked-by-robots` check's own count |
| `lines` | Every check that found something, **in `fixFirst` order**, a notice among them at 0 points |
| `lines[].checkId`, `severity`, `count` | The check, its severity when the analysis ran, and the URLs it flagged |
| `lines[].unit` | What the count is a share of: `pages` (a check whose `subject` is `page`), `urls` (any other check) or `site` (a `siteWide` check: nothing, it is the whole site) |
| `lines[].of` | That total: `covers.pages` or `covers.urls`. For a `site` line it is `covers.urls`, and isn't for showing: say "the whole site" |
| `lines[].extent` | How much of the site: `few` (under 1 in 100), `some` (1 in 100 or more), `many` (1 in 10 or more), `most` (half or more), or `site` (a site-wide fault, whatever its count) |
| `lines[].points` | Points off: 2, 4, 7, 10 or 60 for an error by extent, 1, 2, 3, 4 or 4 for a warning, 0 for a notice |
| `ifFixed.checkIds` | The first five checks in `fixFirst` order that take points off (errors and warnings; never a notice). Fewer when fewer found anything, and empty on a site with nothing to fix |
| `ifFixed.value` | The score without them: "Fixing these five takes the score from 85 to 98" |

- **`score` is null when there is nothing honest to say, and `noScoreReason` says why**: `noPages` (the analysis read
  no page: a start URL that failed or was refused, a site robots.txt blocks) or `notScored` (the analysis stored no
  score this engine reads: an engine from before the score ran it, and it is out of date). With a score,
  `noScoreReason` is null. A crawl never analysed has no analysis to ask: `not-analyzed`, as before.
- **Out of date.** An analysis scored by another version of the formula is out of date with `engineChanged`, as one
  by another analysis version is. Its score is still served, as stored, until the crawl is analysed again.
- **How the order to fix is made.** `fixFirst`, and so `lines` and `ifFixed`, is made in four steps, each settling
  only what the one before left level:
  1. **Severity**: errors, then warnings, then notices.
  2. **A fault of the whole site leads its severity.** Every `siteWide` check that found something comes before every
     other check of its severity, **however small its count**. Among themselves, site-wide checks are in catalog
     order: their counts don't order them (a site-wide fault's count is of nothing: 1 on most sites, 3 for a site
     that answers at three other addresses).
  3. **The most URLs flagged**, among the rest.
  4. **Catalog order**, where checks are still level (titles before meta descriptions).

  Why step 2: a site-wide fault is one issue, so by its count it would come after every check of its severity that
  flagged more, and on a site with five other errors it would be outside the first five to fix, and outside
  `ifFixed`, while taking 60 points off: the one thing to fix first, listed sixth. So a site not on HTTPS with six
  errors on hundreds of pages each has `no-https` first, and `ifFixed` takes it and the four largest away. Where no
  check is site-wide the order is what the UI's "Fix first" has always had (severity, count, catalog). A warning
  never comes before an error, site-wide or not, and a notice never before a warning.
- **Hidden checks count.** The score, its lines and `ifFixed` take in every check, hidden in the app or not.
- **A partial score is still a score.** It changes no number: it says the crawl didn't read all of the site.

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
| `pages-unread` | 1 in 20 or more of the site's pages the crawl tried to read refused it (a firewall, a bot challenge, a rate limit) or didn't answer, so their links are unknown and pages linked only from them would look orphaned (`orphan-page`) |
| `start-not-root` | The crawl didn't start at the site's root (its start URL has a path other than `/`), so its first page isn't known to be the site's home page: `site-hidden-from-search` can't be judged |
| `external-links-off` | The crawl was told not to check links to other sites |
| `resources-off` | The crawl was told not to fetch images and other files |
| `hreflang-off` | The crawl wasn't told to follow hreflang links (the default), and a language version they name was left unread, so which versions nothing else leads to can't be told |
| `robots-txt-ignored` | The crawl was told to ignore robots.txt. It asked for every URL, so none was refused (`blocked-by-robots`); it read no other origin's robots.txt (`robots-txt-unreachable-other-origin`); and it read the site's own only for sitemap discovery, so with sitemaps off `robots-txt-unreachable` and `robots-txt-missing` are skipped too |
| `site-versions-unchecked` | The site's other versions (www or not, http or https) weren't all checked |
| `rule-failed` | The check's rule failed once every page was read, so it has no results (the log says why) |
| `earlier-version` | The crawl, or some of the pages the check needs, was made by an earlier version, which didn't record what the check compares (for near-duplicate content, the 256-bit fingerprint). Crawling again mends it |
| `newer-than-analysis` | The check is newer than the engine that ran the analysis. A check an analysis by a newer engine than the sidecar's didn't run has no `reason`: that engine dropped it |

### Analysis settings (E6)

The limits every analysis here judges by ("a title wider than this is too long"), for Settings to show and change.
They're the engine's until the user saves their own, which are kept in the state folder (`analysis-settings.json`)
and outlive a restart. The names are the JSON names of the engine's `AnalysisSettings` properties, which are also
what each analysis stores in its crawl file.

| Method and path | Does |
| --- | --- |
| `GET /api/analysis-settings` | The limits in force, with what each is |
| `PUT /api/analysis-settings` | Saves limits, and answers as `GET` does. Every limit is optional: one left out, or null, takes the engine's value |
| `POST /api/analysis-settings/validate` | `{ errors: { <limit>: [messages] } }`, 200: what a save of the same body would refuse, as it's typed. Nothing is saved |
| `POST /api/analysis-settings/reset` | Body `{ "limits": ["titleMaxPixels"] }`: puts those limits back to the engine's values and leaves the rest; with no body, or none named, all of them. Answers as `GET` does |

```json
{"settings":{"titleMinPixels":200,"titleMaxPixels":580,"descriptionMinPixels":400,"descriptionMaxPixels":985,"minWordCount":200,
             "duplicateMinWordCount":50,"nearDuplicateMaxBits":24,"imageAltMaxLength":125,"largeImageBytes":204800,
             "largePageBytes":1048576,"slowResponseMs":1000,"maxUrlLength":115},
 "limits":[{"id":"titleMaxPixels","value":580,"default":580,"unit":"pixels","min":1,"max":100000,
            "description":"A title wider than this in search results is flagged as too long: Google's desktop results would likely cut it off. Measured in Arial at 20 px, as Google shows titles.",
            "pixelsPerCharacter":9.4}],
 "isDefault":true}
```

- **`settings`** is each limit's value by name: the body `PUT` and `validate` take. **`limits`** lists every limit in
  the order Settings shows them, each with its `value` (the same number), the engine's `default`, its `unit`
  (`characters`, `pixels` for text, `px` for an image's width, `words`, `bits`, `bytes`, `ms`, `links`, `clicks` or `points`), the
  `min` and `max` it may be set within, and a
  `description`: one sentence saying what it does, to show as it is. A width in `pixels` also has
  **`pixelsPerCharacter`** (9.4 for a title, 6.2 for a description; no other limit has the field): an ordinary
  character's width, so the value over it is the characters it holds, rounded to the nearest 5 from 20 on and to the
  nearest whole below, as the plain words round it ("about 60 characters"). **`isDefault`** is true while every limit
  is at the engine's value.

  | Limit | Default | Unit | Range | A page, image or URL is flagged when |
  | --- | --- | --- | --- | --- |
  | `titleMinPixels` | 200 | pixels | 0 to 100,000 | its title is narrower, in Google's desktop results (about 20 characters) |
  | `titleMaxPixels` | 580 | pixels | 1 to 100,000 | its title is wider (about 60 characters) |
  | `descriptionMinPixels` | 400 | pixels | 0 to 100,000 | its meta description is narrower (about 65 characters) |
  | `descriptionMaxPixels` | 985 | pixels | 1 to 100,000 | its meta description is wider (about 160 characters) |
  | `minWordCount` | 200 | words | 0 to 1,000,000 | it has fewer words of main content (thin) |
  | `duplicateMinWordCount` | 50 | words | 0 to 1,000,000 | (a page with fewer words is left out of the duplicate content checks) |
  | `nearDuplicateMaxBits` | 24 | bits | 0 to 32 | its content fingerprint is within this many bits, of 256, of another page's |
  | `imageAltMaxLength` | 125 | characters | 1 to 10,000 | an image's alt text is longer |
  | `largeImageBytes` | 204,800 | bytes | 1 to 10 GB | an image is larger |
  | `largePageBytes` | 1,048,576 | bytes | 1 to 10 GB | its HTML is larger |
  | `slowResponseMs` | 1,000 | ms | 1 to 3,600,000 | it takes longer to answer |
  | `maxUrlLength` | 115 | characters | 1 to 10,000 | the URL is longer |

  The inspector's `limits` are these as the URL's analysis ran with them, under the inspector's own names (the four
  widths' are the same).
- **Checked limit by limit.** A save that can't be made is `invalid-settings` (422) with `errors` keyed by the limit, and
  saves nothing: a value outside its range ("This must be from 1 to 100,000 pixels."), one that isn't a whole
  number (text, a fraction, true: a field error here, not a 400, as an option of the wrong type is), and a widest
  width that isn't more than its narrowest, reported on the widest (`titleMaxPixels`, `descriptionMaxPixels`: "The
  widest title allowed must be more than the narrowest (700).") and judged with the engine's value for whichever of
  the two wasn't sent. A rule of the engine's that no one limit breaks, only the limits together, is keyed
  `settings`: so `errors`' keys are limits' names, `settings`, or (for `reset`) `limits[<index>]`, and a client shows
  a key it has no field for above the form. A limit that doesn't exist, or a body that isn't an object of limits, is a
  `bad-request` (400), as a property the API doesn't know is everywhere: the names the limits had in characters
  (`titleMaxLength` and the rest) included. `reset` with a name that's no limit's is `invalid-settings` on
  `limits[<index>]`, and changes nothing.
- **Resetting one of a pair** that would leave the widest under the narrowest (the narrowest title raised to 700, the
  widest put back to 580) puts both back.
- **What a change does to the crawls already analysed.** An analysis stores the limits it ran with, and one that ran
  with other limits than those now in force is out of date: `outOfDate` on its library item and `outOfDateReasons`
  `settingsChanged` on `/analysis`, from the next read of either, for every crawl, open or not. Nothing else changes
  until it's analysed again: its counts, its URL summary and its grids stay as they are behind the notice. Putting the
  limits back makes it stand again. No event is sent: the UI reads the library again after its own save.
- **An analysis under way keeps the limits it began with.** It takes them once, as it starts, runs to its end with
  them and stores them: limits saved meanwhile reach the next analysis, and the one that was running is out of date
  (`settingsChanged`) the moment it's done.
- **What's kept.** Only the limits that differ from the engine's are written, so a limit never changed follows the
  engine's default if a later version moves it. A file edited by hand keeps the values that could be saved today,
  and the rest take the engine's; a pair that's broken goes back together.
- **Limits saved in characters** (`titleMinLength`, `titleMaxLength`, `descriptionMinLength`,
  `descriptionMaxLength`), by a version before pixels, are converted as the file is read, and logged in one line: a
  limit at its old default takes the new default, any other its characters times `pixelsPerCharacter`, so 70
  characters of title is 658 px; when the file has the new name too, it wins. The next save writes only the new
  names. A crawl's stored analysis settings are read converted the same way
  ([pixel widths](v2-pixel-widths.md#6-settings)).

### Data (E1: the readers that exist today)

| Method and path | Returns |
| --- | --- |
| `GET /api/crawls/{id}/stats` | `CrawlStatsReader.Read`. `nonIndexableReasons` counts the site's URLs as the checks and the URL summary do: the probes of its other versions are left out, and so are the URLs that weren't checked, which `notCheckedCount` gives |
| `GET /api/crawls/{id}/issues/{checkId}?limit=200&offset=0` | `{ rows: [{ url, detail, relatedUrl }], total }`. `total` is null for a check that was skipped, never 0 |

**`siteVersions`** (E3c), on `/stats`, is the part of `outcomes` and `statusCodes` that is the crawler's probes of the
site's other versions, `{ "fetched": 3, "outcomes": { "redirect": 2, "networkError": 1 }, "statusCodes": { "301": 2 } }`:
`outcomes` and `statusCodes` count every stored outcome, the probes' among them, as the live figures do, and this is
the progress event's `siteVersions` for a crawl at rest (see **Progress**). `fetched` is how many probes have an
outcome.

**`/stats` is read as a grid is** (E3c), and its other answers are what they always were:

- **One snapshot.** Every number is of the same moment of the file, a running crawl's included.
- **Kept.** The numbers are kept with the grids' counts, per crawl file, crawl stamp and analysis: they're read once
  while the crawl stands still, so Overview's call costs a read once per crawl, and a running crawl's are kept for 2 s.
- **Served as the grids' reads are.** It takes one of the grid read slots, a request given up stops its read, and an
  analysis or a continue starting never waits on it.
- **What the first read costs.** One pass over the URLs and one over the responses, whatever is counted from them
  (it was a pass for each number). Where the analysis's link summary counts the crawl as it stands, no link is read:
  `linkCount` is the links grid's `total`, and `unlinkedPageCount` comes from the summary's count of each URL's links
  from other pages. Without it (a crawl running, never analysed, continued since its analysis, or analysed before
  E3b), the links are counted and each page a sitemap led to is looked up among them. Measured under
  [Engine changes E3c made](#engine-changes-e3c-made).

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
- `/urls/inspect`, `/urls/inlinks`, `/urls/outlinks` and `/urls/source` (E3a, the URL's id in the body);
- `/issues/{checkId}/groups` and `/rows` (E3a);
- `/links` and its facets (E3b, below);
- `/sitemaps` and `/sitemaps/urls` (E3b);
- `/robots`, `/robots/file` and `/robots/blocked` (E3b);
- `/search` (E6);
- `/links/ends`, and `linkEnds` on the URL grids (E6);
- `/favicon` (E6);
- `/links/groups` and `/urls/images` (step 3b);
- `/export` (E5).

The GET issues endpoint above stays as it is, for scripts and the design fixtures; the Issues screen uses `/rows`.

### URLs (E3a)

The URL grid: every URL the crawl found is a row, fetched or not (filter on `fetched` for the library's
`crawledUrlCount`), the probes of the site's other versions included (filter on `source`). Each call reads one snapshot
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
  | boolean | `is`, `isNot` | `value`: true or false. `isNot` is the other value or unknown (round 5 review R1-2) |
  | every type | `isEmpty`, `isNotEmpty` | Nothing. Empty is unknown (null), and for text also `""` |

  - A negative operator (`ne`, `notIn`, `notContains`, `notRegex`, `isNot`) matches unknown values too, as "exclude
    this value" should; add `isNotEmpty` to leave them out.
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
- **`search`** matches the URL or the title containing the text, ignoring the case of ASCII letters, at most 1,000
  characters. It reads every URL and every title once per text, and keeps the URLs that matched: 26 to 50 ms for the
  first page on the 100k crawl at p95, whatever the text (one letter, forty characters, nothing matching), which E6
  measured against an index and left as it is ([Engine changes E6 made](#engine-changes-e6-made)). The palette's
  search as it's typed is [`/search`](#search-e6), which gives the same count.
- **`linkEnds`** (E6) narrows the grid to the URLs at one end of the links a view of the links grid matches:
  `{ "end": "from" | "to", "filters": [link filters], "search": "…" }`, where `filters` and `search` are the links
  grid's own ([Links](#links-e3b)) and none of either means every link. `from` is the pages those links are on, `to`
  the URLs they lead to. It narrows beside `filters` and `search`, as one more filter, and `matching` under it alone is
  `total` of [`/links/ends`](#link-ends-e6) for the same links: the number a links view can state before it opens
  "these pages in URLs". `/urls/facets`, a check's `/rows` and `/sitemaps/urls` take it too. Its errors are keyed
  under it (`linkEnds.end`, `linkEnds.filters[0].column`), a link filter that needs the URL summary without one is
  refused with `needs-analysis` as the links grid refuses it, and a cursor read under other links is refused on
  `after`. What it costs is what the list of ends costs. While a crawl runs, `total` under it is the URLs of the
  links' moment the ends are of, not the count of URLs an earlier read kept (round 5's final verify: that one could be
  older than the moment, "526 that are ends, of 516"; `RunningCrawlReadsTests.TheURLGridOfALinksViewsEndsIsOfTheMomentItsEndsAreOf`).
- **`hiddenChecks`** (round 5 review, also noted) is the ids of the checks the user hid, every count in the app
  leaving them out: `["title-too-long","thin-content"]`. The issue counts (`issues`, `errors`, `warnings`,
  `notices`) leave their issues out wherever the query uses them: each row's values, the sort, the filters
  (`errors gte 1`, `issues eq 0`) and so `matching`. Each is the summary's count less the URL's issues of those checks
  (of its own severity, for `errors`, `warnings` and `notices`), counted as the summary counts them. An id the
  catalog doesn't have is taken, as the report takes it (a check hidden by another version, kept in the app's
  settings): it hides the issues stored under it, if any, so a stale id never breaks a grid. At most 1,000 ids, none
  null or longer than 200 characters: anything else is `invalid-query` on `hiddenChecks`. None, or the field left
  out, reads exactly as before.
  - With checks hidden those four columns have no index: a sort by one reads and sorts the matching rows, as a sort by
    an unindexed column does, and a filter on one is tested per row, never driving the read.
  - Counts are kept per set of hidden checks (in any order), so a count kept without them is never given with them;
    a cursor read with other checks hidden is refused on `after`.
  - `/urls/facets` takes it for `severity` (below), a check's `/rows` and `/sitemaps/urls` take it as `/urls` does,
    and the links grid's `/links`, `/links/facets` and `/links/ends` for the `from.` and `to.` issue counts. `linkEnds`
    reads its link filters with the same checks hidden.
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
`[{ id, type, needsAnalysis, sortable, filterable, values, issueRowsOnly, title, unit, valueTitles }]`, where `values`
lists an enum's values and `issueRowsOnly` marks a check's own columns, which only its rows have (see Issue groups).
**`title`**, **`unit`** and **`valueTitles`** (E5) are the column in words: its name as a header shows it ("Status",
"Response time"), the unit of its numbers (`ms`, `bytes`, or null), and an enum's values as a cell shows them, by
value (`{"clientError":"Client error"}`; empty for other types). They are the engine's, and an [export](#export-e5)
writes them, so a grid that shows them and the file exported from it can't name a column or a value two ways.

**Columns whose value is an object** (evidence): `type` `object` is a new type, for a value that is a list or a
record rather than one value: `redirectChain` and `robotsRule`. Such a column is shown only (`sortable` and
`filterable` false, so a filter or sort on one is `invalid-query`, as on any column that doesn't take it); its value
in a row is the JSON object below, or null; its titles are "Redirect chain" and "robots.txt rule". An
[export](#export-e5) writes it as one line of text: `301 → https://www.example.com/b/ (301) →
https://www.example.com/c/ (200)` (the URL's own status, then each hop's URL and status; `(meta refresh)` before a
hop reached by one; `, a loop` or `, more than 20 hops` at the end), and `Disallow: /cart/ (line 12 of
https://www.example.com/robots.txt)`, `robots.txt couldn’t be read` or `No matching rule`. A new **`unit`**, `px`, is
`pixelWidth`'s and `pixelHeight`'s ("Image width (px)"), and since [pixel widths](v2-pixel-widths.md) `titleWidth`'s
and `descriptionWidth`'s.

```json
{"id":31,"url":"https://www.example.com/a/","statusCode":301,
 "redirectChain":{"hops":[{"id":44,"url":"https://www.example.com/a","statusCode":301,"outcome":"redirect","via":"http"},
                          {"id":31,"url":"https://www.example.com/a/","statusCode":301,"outcome":"redirect","via":"http"}],
                  "final":{"id":31,"url":"https://www.example.com/a/","statusCode":301,"outcome":"redirect"},"loop":true,"tooLong":false},
 "robotsRule":null}
```

```json
{"id":905,"url":"https://www.example.com/cart/","statusCode":null,"redirectChain":null,
 "robotsRule":{"origin":"https://www.example.com","reason":"rule","line":12,"rule":"Disallow: /cart/"}}
```

Each is worked out for the rows of the page asked for, a row at a time (a chain at most 20 lookups, a robots.txt
parsed once per origin per read), and only when the column is asked for: a page of 100 rows costs what 100
inspector chains do.

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
| `language` (evidence) | text | | The page's `<html lang>` as it writes it (`en-GB`, `english`), what `lang-invalid` judges. Null for anything that isn't a parsed page, and for a page without one. Filters; doesn't sort |
| `wordCount` | number | | Main-content words, for a page whose HTML was read |
| `outlinks` | number | | Hyperlinks on a page whose HTML was read |
| `sizeBytes` | number | | Bytes on the wire |
| `responseMs`, `ttfbMs` | number | | The whole response, and the first byte. Null when no request was made |
| `titleLength`, `descriptionLength` | number | Yes | The first title's and meta description's characters, as a reader counts them: user-perceived, white space run together, an emoji or an accented letter once |
| `titleWidth`, `descriptionWidth` | number | Yes | How wide the first title and meta description are in Google's desktop results, in pixels (unit `px`): what the length checks judge ([pixel widths](v2-pixel-widths.md)) |
| `titleInResults`, `descriptionInResults` | text | | The first title and meta description as Google's desktop results will likely show them: up to where they're cut, with "…", or all of it when it fits, against the analysis's `titleMaxPixels` and `descriptionMaxPixels` (the engine's before the first analysis). Shown only: worked out for the rows of the page, as `redirectChain` is |
| `finalUrl`, `finalStatusCode` | text, number | Yes | Where the URL's redirects end, meta refreshes included, and the status there. The URL itself when it doesn't redirect |
| `inSitemap` | boolean | Yes | Listed in a sitemap. False only when every sitemap was read |
| `inlinks`, `followedInlinks`, `nofollowInlinks`, `linkingPages` | number | Yes | Hyperlinks to the URL on the pages audited, each counted, a page's links to itself left out: all, those followed, the nofollow ones, and the pages they're on |
| `issues`, `errors`, `warnings`, `notices` | number | Yes | Checks that flagged the URL, in all and by severity |
| `usedOn` (evidence) | number | Yes | The pages audited that refer to the URL by any kind of link (hyperlink, image, script, stylesheet, iframe, canonical, hreflang…), each page once, a page's links to itself left out: where an image, a script or a stylesheet is used, which `inlinks` (hyperlinks only) reads as 0 |
| `pixelWidth`, `pixelHeight` (evidence) | number | | An image's own size in pixels, read from the first bytes of its file as the crawl fetched it: PNG, GIF, JPEG, WebP, and AVIF where its header says. Null for anything else (an SVG included), an image whose header couldn't be read, and every URL of a crawl made before the crawler read them. Shown only: they neither sort nor filter, as a crawl made before them has no column to read |
| `fetchError` (evidence) | text | | What went wrong when the request didn't succeed, as the crawl recorded it ("No response within 20 seconds", "Name not resolved", "robots.txt couldn't be read"). Null for a URL that answered with success, and for an error the crawl recorded no words for. Filters; doesn't sort |
| `redirectChain` (evidence) | object | | The URL's redirects hop by hop, to where they end, come back on themselves, or stop: exactly the inspector's `redirects`, `{ hops: [{ id, url, statusCode, outcome, via }], final: { id, url, statusCode, outcome }, loop, tooLong }`. Null for a URL that doesn't redirect |
| `redirectKind` (evidence) | text | | What the URL's redirect changes, from the URL and where it leads: its kinds, comma separated in this order, of `httpToHttps`, `httpsToHttp`, `addsWww`, `removesWww`, `otherHost` (then nothing more is said), `addsSlash`, `removesSlash`, `changesCase`; `other` when the path or query changes some other way (after a scheme or www change, if any). Null for a URL that doesn't redirect. Shown only: it neither sorts nor filters |
| `robotsRule` (evidence) | object | | For a URL robots.txt refused, why, as the inspector's `robotsTxt` test says it: `{ origin, reason, line, rule }`, `reason` being `rule` (with the stored file's `line` and its text as `rule`), `unreachable` or `unknown`. Null for a URL robots.txt didn't refuse |

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
- **`hiddenChecks`** (round 5 review, also noted), as the grid takes it: `severity` counts as the issue counts do, the
  hidden checks left out, so its `error` bucket opens its URLs in a grid given the same `hiddenChecks`.

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
{"id":119,"url":"https://www.example.com/blog/?p=2","urlLength":33,"isInternal":true,"type":"page",
 "discovery":{"source":"link","depth":2,"foundOn":{"id":7,"url":"https://www.example.com/blog/"},"foundVia":"hyperlink",
              "fetchMode":"crawl","state":"done","notFetchedBecause":null},
 "response":{"fetched":true,"outcome":"success","statusCode":200,"contentType":"text/html; charset=utf-8","httpVersion":"2.0",
             "crawledAt":"2026-10-31T20:15:02Z","ttfbMs":180,"responseMs":240,"sizeBytes":18011,"bodyBytes":96340,
             "blockReason":null,"error":null},
 "redirects":{"hops":[],"final":{"id":119,"url":"https://www.example.com/blog/?p=2","statusCode":200,"outcome":"success"},"loop":false,"tooLong":false},
 "indexability":{"indexable":false,"reason":"canonicalised","judgedBy":"analysis",
   "tests":[{"test":"answered","outcome":"pass","statusCode":200},{"test":"robotsTxt","outcome":"pass"},
            {"test":"noindex","outcome":"pass","meta":false,"header":false},
            {"test":"canonical","outcome":"fail","canonical":{"id":7,"url":"https://www.example.com/blog/","statusCode":200,"outcome":"success"},
             "canonicalUrl":"https://www.example.com/blog/"}]},
 "content":{"titles":[{"text":"Blog, page 2","length":12,"width":112}],"descriptions":[],"h1s":[{"text":"Blog","length":4,"width":null}],
            "headings":[1,3,0,0,0,0],"wordCount":412,"language":"en-GB","charset":"utf-8","robots":[],"metaRefresh":null,
            "images":12,"imagesMissingAlt":0},
 "limits":{"titleMinPixels":200,"titleMaxPixels":580,"descriptionMinPixels":400,"descriptionMaxPixels":985,"minWords":200,"slowResponseMs":1000,
           "largePageBytes":1048576,"largeImageBytes":204800,"imageAltMaxLength":125,"maxUrlLength":115,"fromAnalysis":true},
 "links":{"inlinks":3,"followedInlinks":3,"nofollowInlinks":0,"linkingPages":2,"outlinks":45,"inSitemap":false},
 "linkTotals":{"inlinks":4,"outlinks":63,"hyperlinkInlinks":3},
 "headers":[{"name":"content-type","value":"text/html; charset=utf-8"}],
 "structuredData":[{"valid":true,"types":["BlogPosting"],"json":"{…}","error":null,"truncated":false}],
 "hreflang":[],
 "issues":[{"checkId":"title-too-short","severity":"notice","detail":"112 px, under 200 (12 characters)","related":null,"group":null}],
 "namedBy":{"total":0,"checks":[]},
 "summary":{"available":true,"reason":null},
 "source":{"stored":false},
 "listedIn":{"total":1,"sitemaps":[{"id":3,"url":"https://www.example.com/sitemaps/pages-0.xml"}]}}
```

- **`urlLength`** (E3c) is the URL's length as the check `url-too-long` measures it, to set against
  `limits.maxUrlLength`: the URL as people read it, its escapes decoded and its host in its own script (`/каталог/` is
  9 characters, not 42), in characters as a reader counts them. The check and the inspector measure it with the same
  code. It's given for any URL, though the check looks only at indexable pages; null only for a stored URL that isn't
  one (a crafted file's).
- **`discovery`** is how the crawl first found it: `foundVia` is the kind of link (`hyperlink`, `image`, `canonical`,
  `redirect`...), `fetchMode` is `crawl` (fetched, parsed, its links followed) or `check` (status only), `state` is
  `queued`, `fetching`, `done` or `skipped`, and `notFetchedBecause` why a skipped URL wasn't fetched (`none` when no
  narrower reason was stored, `noFollow`, `notFollowedByOption`, `resourcesDisabled`, `externalsDisabled`,
  `depthLimit`, `urlLimit`). A URL that was fetched has `notFetchedBecause` null.
- **`response`** is null for a URL never fetched. **`response.fetched`** is the URL grid's `fetched` column: the crawl
  stored an outcome for the URL, which it also does when robots.txt refused the request, so a URL robots.txt blocked
  has `fetched: true` with no status, no times and `outcome: "robotsDisallowed"`. It's false only for a URL not
  checked because its host had stopped answering (`outcome: "notChecked"`). **Go by `outcome`** for what happened:
  `fetched` says only that the crawl is done with the URL.
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
  | `robotsTxt` | robots.txt allowed the request | `unreachable`: true when robots.txt couldn't be read, which refuses everything. And (E6) `origin`, `reason`, `line` and `rule`, below |
  | `noindex` | No noindex in a robots meta tag or the `X-Robots-Tag` header | `meta` and `header`: where a noindex is |
  | `canonical` | No canonical, one to the URL itself, or one that leads back to it or nowhere | `canonical`: the target `{ id, url, statusCode }` when the crawl has it; `canonicalUrl`: the canonical as the page gives it, normalised, whether the crawl has it or not (a page only status-checked keeps no links, so its `Link` header's canonical may be no URL of the crawl's) |

  `indexable` and `reason` are the verdict, as the analysis judged it (`judgedBy: "analysis"`); without a URL summary
  they are the crawl's own judgement where the analysis hasn't seen the URL (`judgedBy: "crawl"`), and both are null
  for a page that was only status-checked.

  **The `robotsTxt` test links exactly** (E6), so the inspector can open the file that decided the URL at the line
  that did:

  ```json
  {"test":"robotsTxt","outcome":"fail","unreachable":false,"origin":"https://www.example.com","reason":"rule","line":2,"rule":"Disallow: /private/"}
  ```

  - **`origin`** is the origin whose robots.txt decides the URL, named as the list of origins names it, to pass to
    `/robots/file` and `/robots/blocked`. It's there whatever the outcome when the crawl has a robots.txt answer for
    that origin, and always for a URL robots.txt refused; left out when the crawl never asked that origin for one (so
    there's no file to open).
  - **`reason`**, for a URL robots.txt refused, is why, as its row in `/robots/blocked` says: `rule` (with the
    **`line`** of the origin's stored file that refused it, and that line's text as **`rule`**), `unreachable`
    (robots.txt couldn't be read when the URL came up) or `unknown` (the file kept doesn't refuse it: a later run read
    it again, and it had changed). It's worked out by the crawler's own matcher from the stored file, as that list is,
    for this URL alone: the origin's file is parsed once and kept, so inspecting one of a hundred thousand refused URLs
    matches one. `unreachable` stays as it was: true where the crawl stored that robots.txt couldn't be read with the
    URL itself.
  - A field that doesn't apply is left out, as the other tests' are.
- **`content`** is null for anything that isn't a parsed page. Titles, descriptions and H1s are each listed in page
  order with their `length`, the characters as a reader counts them, and their `width`, in pixels as the checks
  measure it in Google's desktop results (null for an H1, which no result shows); `headings` counts H1 to H6,
  `images` the images and `imagesMissingAlt` those without alt text; `robots` lists the directives that apply to the
  crawler or to Googlebot; `charset` is the character set the page declares: its meta tag's, or with no meta tag the
  encoding its byte-order mark declares (`utf-8`, `utf-16le`, `utf-16be`), else null (the header's is in `headers`);
  `metaRefresh` is `{ delaySeconds, url }` for a page with a meta refresh, else null.
  **`limits`** are what the analysis judged the URL against: its stored settings (`fromAnalysis: true`), or the engine's
  defaults before the first analysis (`fromAnalysis: false`). A title's and a description's are widths in pixels
  (`titleMinPixels`, `titleMaxPixels`, `descriptionMinPixels`, `descriptionMaxPixels`), to set against `width`. They
  include an image's (`largeImageBytes`, which `image-too-large` goes by, and `imageAltMaxLength`) and a URL's
  (`maxUrlLength`).
- **`links`** comes from the URL summary, and is null without one (`summary` says why). `type` is judged as the
  grid's column is.
- **`linkTotals`** (E3c) is every link to the URL and from it, `{ inlinks, outlinks }`: links of every kind, a page's
  links to itself left out, which is the `total` that `/urls/inlinks` and `/urls/outlinks` give with no `kinds`. So the
  inspector's Inlinks and Outlinks tabs have their counts without a read each. They're counted from the links
  themselves (the inlinks from the link summary, where there is one), so they're there with or without a URL summary,
  a running crawl's included, where `links` (hyperlinks only, on the pages audited) is null; a running crawl's are
  kept for 2 s, as its other counts are.
  - **`linkTotals.hyperlinkInlinks`** (E6) is the hyperlinks among those to the URL, every one the crawl stored: the
    `total` of `/urls/inlinks` with `"kinds": ["hyperlink"]`, and **the links grid's `matching`** under
    `to.id` `eq` this URL, `kind` `in` `["hyperlink"]`, `selfLink` `is` false. It's the figure to show as a link to
    Links ("N hyperlinks"), because it lands.
  - **`links.inlinks` can be less, and isn't that figure.** The analysis counts the hyperlinks on the pages it
    audited, and it doesn't audit Cloudflare's own pages (a URL with `/cdn-cgi/` in it, such as the email-protection
    page), which the crawl fetches and parses like any other and whose links it stores. So for a URL such a page
    links to, `hyperlinkInlinks` is `links.inlinks` plus that page's hyperlinks to it, and for every other URL the
    two are equal. On the 100k benchmark crawl that is 24 of 112,590 URLs (the menu's targets, each one more, the
    home page two); on the garden crawl, none. The URL grid's `inlinks` column, sort and filters are the analysis's.
- **A URL the inspector names carries its `outcome`** (E3c) beside its status, as the URL grid's `outcome` column has
  it: `redirects.final`, a test's `canonical` and `metaRefresh`, and an hreflang's `target`, as each hop of `redirects`
  already did. A status is null both for a URL never fetched and for one that gave no HTTP answer; the outcome tells
  them apart (null, or `robotsDisallowed`, `timeout`, `networkError`, `notChecked`...).
- **`headers`** are as stored: names lowercased, for internal URLs only. **`structuredData`** is each JSON-LD block,
  `valid` when it parses, with its `@type`s (those in a `@graph` included), or the parser's `error`; `truncated` when
  the page's block was cut at the parser's limit. **`hreflang`** is each annotation `{ language, href, url, target, inHead,
  fromHttpHeader }`, `target` being the URL it names in the crawl, with its status, or null.
- **`issues`** are the checks that flagged this URL in the last analysis, errors first, with the other URL involved (the
  first, where there are several: "3 broken URLs" names the first of them). Each has its **`group`** (E3b): the key of
  the group the issue is in on `/issues/{checkId}/groups`, `{ detail, relatedUrlId }`, to pass as `/rows`' `group` or the
  check filter's, or null for a check that doesn't group (`grouping` `none`, or a check this engine doesn't know). It
  is the group's key, not the issue's own values: for a check grouped by the other URL, `detail` is null and
  `relatedUrlId` the first URL the issue names (it's in the group of each URL it names; null is the group of those
  naming none); for one grouped by a fault (`canonical-relative`, `canonical-invalid`, `structured-data-invalid`),
  `detail` is the fault, worked out by the catalog's own rule as the groups are, not the page's own detail. For an
  analysis from before the groups were stored, it's the issue's own detail and first other URL, as that analysis
  groups. **`namedBy`** counts the issues on other
  URLs that name this one among the URLs they're about, by check: every page linking to a 404, whether or not it's the
  first broken link on the page; the pages canonicalised to it; the other pages of a duplicate cluster it's the first of.
  Both come from the last analysis whatever its state, as the Issues screen's do, and are null before the first.
- **`source.stored`** says whether `/source` has the HTML.
- **`listedIn`** (E6) names the sitemaps that list the URL, `{ total, sitemaps: [{ id, url }] }`, in the order the
  crawl found them: `id` is the sitemap's in [`/sitemaps`](#sitemaps-e3b), which `/sitemaps/urls` takes, so the
  inspector opens the sitemap itself. `total` is how many list it and `sitemaps` the first 20 of them. Only the
  sitemaps whose own file lists the URL are named, not the indexes over them. It's read from what the crawl stored,
  so it's there with or without a URL summary and while a crawl runs, where `links.inSitemap` (the analysis's, which
  also says "in none, and every sitemap was read") is null without one. `total` 0 with `links.inSitemap` null means
  no sitemap read so far lists it.

`POST /api/crawls/{id}/urls/inlinks` and `/urls/outlinks` page the links to and from the URL whose id is in the body, a
page's links to itself left out, in the order they were found:

```json
{"urlId":119,"kinds":["hyperlink"],"offset":0,"limit":100}
```

```json
{"rows":[{"id":7,"url":"https://www.example.com/blog/","kind":"hyperlink","text":"Older posts","position":"content",
          "flags":[],"followed":true,"fragment":null,"statusCode":200,"indexable":true,"fetched":true,"outcome":"success"}],
 "offset":0,"total":3,"next":"…"}
```

- `id`, `url` and the status columns are the other end's: the linking page for an inlink, the target for an outlink.
  **`outcome`** (E3c) is what became of the request for it, as the URL grid's `outcome` column: so a link to a URL
  that robots.txt refused, that timed out or that wasn't checked says which, where `statusCode` is null for each of
  them as it is for a URL never fetched (`outcome` null).
  `kinds` filters by the kind of link (`hyperlink`, `image`, `script`, `stylesheet`, `iframe`, `media`, `icon`,
  `canonical`, `hreflang`, `pagination`, `metaRefresh`, `redirect`, `socialImage` (an og:image or twitter:image given as a full URL); every kind by default).
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

#### A page's images (step 3b)

`POST /api/crawls/{id}/urls/images` with `{ "urlId": 119, "limit": 100 }` lists the images the page shows, each image
file once, **the largest first**: the inspector's Images.

```json
{"urlId":119,"total":14,"links":17,
 "rows":[{"id":2210,"url":"https://www.example.com/img/hero.jpg","links":1,"text":"A teak bench","positions":["content"],
          "target":{"statusCode":200,"outcome":"success","contentType":"image/jpeg","sizeBytes":2202009,
                    "pixelWidth":4000,"pixelHeight":3000,"final":null,"indexability":null,"canonical":null}}]}
```

- **`rows`** are the page's image links (`img`, `srcset` candidates, and the rest the links grid calls `image`) to
  other URLs, grouped by file: **`links`**, how many times the page shows it; **`text`**, the alt text of the first
  of them that has one (null when none has); **`positions`**, where on the page they sit; **`target`**, what the file
  is, as a check group's `related` reads: its size and pixels **after any redirects**, which is what
  `image-too-large` judges it by. The inspector flags one over the analysis settings' `largeImageBytes`
  (`limits.largeImageBytes` of `/urls/inspect`). In order of size, largest first, those of no known size last, then
  in the order the page shows them.
- **`total`** is the image files the page shows, each once, and **`links`** its image links; **`limit`** (1 to 500,
  default 100) caps the rows. **`pixelSizes`** says whether the crawl measured images' pixel sizes, as
  `/links/groups`' does.
- **What it costs**: the page's own image links, on the links' index by source (at most the parser's 10,000 links a
  page), and each file's facts. No URL summary is needed.
- Errors: `url-not-found` for a URL the crawl doesn't have, `invalid-query` without a `urlId` or with a `limit` out of
  range, `bad-request` for a field it doesn't take.

### Issue groups (E3a)

A check's issues grouped by their cause, so a template fault is one row, not 265. How a check's issues group is the
catalog's to say, per check, and `GET /api/checks` gives it as each check's `grouping`:

| `grouping` | The check's issues are grouped by | Checks like |
| --- | --- | --- |
| `detail` | The detail and the other URL involved: the detail names the cause, or the fault it's an instance of | `open-graph-missing` ("Missing og:image"), `internal-client-error` ("404 Not Found"), `structured-data-invalid` |
| `relatedUrl` | Each other URL the issue is about: the detail is a count, and the URLs are the cause | `broken-internal-links` (each broken target), `empty-anchor-text`, `canonicalised-page`, `title-duplicate` (the cluster, named after its first page) |
| `none` | Nothing: the detail is a measurement (612 px, 1,250 ms, 3 images), or the same on every issue | `title-too-long`, `slow-response`, `noindex-page` |

The UI lists a `none` check's URLs flat (`/rows` below), with the detail as a column.

`POST /api/crawls/{id}/issues/{checkId}/groups` takes `{ offset, limit }` (limit 1 to 1,000, default 100):

```json
{"checkId":"empty-anchor-text","grouping":"relatedUrl","total":265,"groupCount":2,"offset":0,
 "groups":[{"detail":null,"relatedUrlId":1,"relatedUrl":"https://www.example.com/","pages":264,"share":0.9962,"likelyTemplate":true,"urls":264},
           {"detail":null,"relatedUrlId":31,"relatedUrl":"https://www.example.com/greenhouses/","pages":1,"share":0.0038,"likelyTemplate":false,"urls":1}]}
```

- Groups come largest first, then by detail and related URL. `total` is the check's flagged URLs, and `share` a
  group's part of them. A `relatedUrl` check's groups have no `detail`; a `none` check has no groups.
- **`urls`** (E6) is how many URLs the group holds, and is the name to read: it's true of every check. **`pages` is
  the same number under its first name**, kept for what already reads it: for a check whose `subject` is `url`
  (`internal-client-error` flags a missing image) the URLs it counts aren't all pages, so word the count by the
  check's `subject`, as its `total` is worded. Where this section says a group's `pages`, it's that count.
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

**What a group is about** (evidence). Each group says what its cause is, so the group's row can be read without opening
it:

```json
{"checkId":"image-too-large","grouping":"relatedUrl","sort":"size","total":212,"groupCount":3,"offset":0,"likelyTemplate":false,
 "groups":[{"detail":null,"relatedUrlId":812,"relatedUrl":"https://www.example.com/img/hero.jpg","sharedText":null,
            "related":{"statusCode":200,"outcome":"success","contentType":"image/jpeg","sizeBytes":1887436,"pixelWidth":4000,"pixelHeight":3000,
                       "final":null,"indexability":"none","canonical":null},
            "pages":205,"share":0.967,"likelyTemplate":true,"urls":205}]}
```

```json
{"checkId":"title-duplicate","grouping":"relatedUrl","sort":"pages","total":14,"groupCount":5,"offset":0,"likelyTemplate":false,
 "groups":[{"detail":null,"relatedUrlId":31,"relatedUrl":"https://www.example.com/sofas/","sharedText":"Sofas | Example",
            "related":{"statusCode":200,"outcome":"success","contentType":"text/html; charset=utf-8","sizeBytes":18011,
                       "pixelWidth":null,"pixelHeight":null,"final":null,"indexability":"none","canonical":null},
            "pages":4,"share":0.2857,"likelyTemplate":false,"urls":4}]}
```

- **`related`** is what the group's related URL is, for every group that has one (a `relatedUrl` check's, and a
  `detail` check's that names one: `redirect-temporary`'s target, `redirect-broken`'s end): its `statusCode` and
  `outcome`, `contentType`, `sizeBytes`, `pixelWidth` and `pixelHeight`, `final` (where its redirects end, below),
  `indexability` (as the URL grid's column, null when not judged) and `canonical` (the URL it's canonicalised to, or
  null). The fields are a target's ([found-on](#where-a-url-is-found-found-on)) less the page's link to it, which is
  each page's own; the check's `targetFacts` say which to show. Null for a group without a related URL.
- **`sharedText`** names a duplicate cluster by what its pages share: the title for `title-duplicate`, the meta
  description for `description-duplicate`, the H1 for `h1-duplicate`, as the cluster's first page writes it (the
  check compares them with case and spacing set aside). Null for every other group: content clusters are named by
  their first page, as before.
- **`sort`** orders the groups: `pages` (the most URLs first, as before) or `size` (the largest file first: the related
  URL's `sizeBytes` where its redirects end, then that size times the group's pages, then the related URL's id; a
  group without a size last). The body takes it, `{ "offset": 0, "limit": 100, "sort": "size" }`; without it,
  `image-too-large` is `size` and every other check `pages`. The answer says which it used. `size` on a check whose
  groups aren't by a URL is `invalid-query` on `sort`.
- **The image checks are grouped by image.** `image-missing-alt`, `image-alt-too-long` and
  `image-missing-dimensions` are `relatedUrl` checks now, as `image-broken` and `image-too-large` are: a page is in
  the group of each image at fault on it, so a logo without alt text on every page is one group, `likelyTemplate`
  true. An image without a usable URL (no `src` to resolve, or past the 10,000 links a page's links are kept to) is
  counted in the page's detail and puts the page in the group without a `relatedUrlId`.
- **`likelyTemplate`** on the answer itself is the check's `counts[].likelyTemplate` on [`/analysis`](#analysis-e1):
  for a check that doesn't group, whether it flagged nearly every page audited. False for a grouped check, whose groups
  carry their own.

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
- **Rows by importance** (evidence). Without `sort` (left out, or empty), the rows come by how much each URL matters
  to the site, not in crawl order: for a check whose `subject` is `page`, `inlinks` descending, then `depth`, then
  `url`; for one whose `subject` is `url`, `usedOn` descending (the pages referring to it by any link: a broken image
  on 200 pages first), then `depth`, then `url`. Without a URL summary, which `inlinks` and `usedOn` need, it is
  `depth`, then `url`. So a group's pages, a check's export and the report's examples come in the same order. Crawl
  order is still there to ask for: `[{ "column": "id" }]`. A cursor is of the order its page was read in, as always.
- **A check's evidence** is asked for as any columns are: `"columns": ["url", "detail", "title", "titleInResults",
  "titleWidth", "titleLength"]` for `title-too-long`, the check's `evidence` from [`/checks`](#checks-e1).

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

### Where a URL is found (found-on)

Two reads for a page of a check's URLs at once, so a grid's rows cost one request, never one a row. Both take
`{ urlIds, limit }`: 1 to 100 URL ids (an id past what a URL can have names none), and how many references of each to
list, 0 to 50 (5 when left out); none, too many or too many listed is `invalid-query` on `urlIds` or `limit`. Each
answers in the order asked, each URL once, leaving out a URL it has nothing for. They read in one snapshot, a URL at a
time on the links' indexes, so they cost what the URLs' own links do.

`POST /api/crawls/{id}/urls/found-on`: where each URL is found. It needs no analysis (it's the crawl's own links).

```json
{"urlIds":[812,977],"limit":2}
```

```json
{"urls":[{"urlId":812,"links":3,"pages":3,"pagesExact":true,
          "sources":[{"id":40,"url":"https://www.example.com/garden-furniture/","kind":"hyperlink","text":"Old range","position":"footer",
                      "flags":[],"followed":true,"fragment":null,"statusCode":200,"indexable":true,"fetched":true,"outcome":"success"}],
          "sitemaps":{"total":0,"sitemaps":[]},"start":false}]}
```

- **`links`** is every link to the URL, of every kind (hyperlinks, images, scripts, canonicals, hreflang, pagination,
  meta refreshes, redirects), a page's links to itself left out: the inspector's inlinks total, and what Links
  filtered to `to.id` and `selfLink` false counts.
- **`pages`** is the pages those links are on. It's counted over the URL's first 5,000 links
  (`FoundOnReader.PAGES_COUNTED_LINKS`); past them `pagesExact` is false and `pages` is a floor: the more of the pages
  those links are on and the pages linking to it by hyperlink (the URL summary's `linkingPages`). A footer link on
  every page of a million has a million links, and the links' index by target holds no page.
- **`sources`** are its first `limit` links in crawl order, each as `/urls/inlinks` gives it (`id` and `url` the page
  linking, its status, the link's kind, text, position, flags and whether it's followed): the same rows.
- **`sitemaps`** are the sitemaps listing it (`total` and the first 20, as the inspector's `listedIn`), none read
  where the URL summary says no sitemap lists it. **`start`**: it's the crawl's start URL.

`POST /api/crawls/{id}/issues/{checkId}/targets`: what each of a check's flagged pages names (its broken targets, its
canonical, the alternate that doesn't link back), from `issue_targets` (an earlier engine's analysis: the issue's one
related URL). `check-not-found` and `not-analyzed` as `/rows`; a check the analysis skipped names nothing; a page
the check didn't flag is left out.

```json
{"checkId":"broken-internal-links","urls":[{"urlId":31,"total":3,
  "targets":[{"id":812,"url":"https://www.example.com/old/","statusCode":404,"fetched":true,"outcome":"clientError",
              "kind":"hyperlink","text":"Old range","position":"footer"}]}]}
```

- **`total`** is every URL the page's issue names; **`targets`** the first `limit` by id (the crawl's order of finding
  them), each with its status and outcome, and the page's first link to it (its kind, text and position; a canonical
  check's canonical, a hreflang check's hreflang, an image check's image first), null where the page has no stored
  link to it.

**What each target is** (evidence). A target says what the URL is and how the page links to it, so "the image is
1.8 MB, 4000×3000 px, shown at 400×300" or "this link redirects; link to https://…/new/ instead" can be said beside
the page without opening anything:

```json
{"checkId":"image-too-large","urls":[{"urlId":31,"total":2,
  "targets":[{"id":812,"url":"https://www.example.com/img/hero.jpg","statusCode":200,"fetched":true,"outcome":"success",
              "contentType":"image/jpeg","sizeBytes":1887436,"pixelWidth":4000,"pixelHeight":3000,"responseMs":240,
              "final":null,"indexability":"none","canonical":null,
              "kind":"image","text":"Garden sofa","position":"content","fragment":null,"flags":[],"links":1,
              "img":{"width":"400","height":"300","alt":"Garden sofa","hasAlt":true,"altLength":11,"srcset":true,"loading":"lazy"}}]}]}
```

```json
{"checkId":"links-to-redirects","urls":[{"urlId":31,"total":1,
  "targets":[{"id":90,"url":"https://www.example.com/old","statusCode":301,"fetched":true,"outcome":"redirect",
              "contentType":"text/html; charset=utf-8","sizeBytes":18011,"pixelWidth":null,"pixelHeight":null,"responseMs":95,
              "final":{"id":91,"url":"https://www.example.com/new/","statusCode":200,"outcome":"success","hops":1,"loop":false},
              "indexability":"redirected","canonical":null,
              "kind":"hyperlink","text":"Shop","position":"content","fragment":null,"flags":[],"links":2,"img":null}]}]}
```

- **What the URL is**:
  - `contentType`, `sizeBytes`, `pixelWidth` and `pixelHeight` are the response the URL ends at, through its redirects
    if it has any (what a browser gets: an image that redirects is judged by its end, as `image-too-large` judges it),
    as the URL grid's columns of those names have them;
  - `responseMs` is how long the URL's own answer took (not its redirects' end's: a slow redirect is slow), as the URL
    grid's column; null when it wasn't fetched. `slow-external-links` names it;
  - `redirectKind` is what the URL's own redirect changes, as the URL grid's column; null when it doesn't redirect.
    `links-to-redirects` names it;
  - `final` is where its redirects end, `{ id, url, statusCode, outcome, hops, loop }`, null when it doesn't
    redirect: `hops` the redirects followed, `loop` true when they come back on themselves (then `final` is the URL
    the loop returns to). Meta refreshes count, as the analysis counts them;
  - `indexability` is as the URL grid's column (`none` for indexable; null when not judged);
  - `canonical` is the URL it's canonicalised to, `{ id, url, statusCode, outcome }`, when its canonical names
    another URL; null otherwise. For a canonical chain, this is the chain's next hop.
- **How the page links to it**: `kind`, `text` and `position` as before, of the page's first link to it of the
  check's kind; `fragment` that link's `#fragment`, as written after the `#` (for `broken-fragment-links`, the first
  link to it that has one); `flags` its flags, as a link's (`noFollow`, `imageLink`, `fromHttpHeader`, …); and
  `links`, how many links of any kind the page has to it (a nav link and a footer link to the same 404 are 2). All
  null, and `flags` empty, where the page has no stored link to it.
- **`img`** is the `<img>` as the page writes it, for a target the page shows as an image: `width` and `height` the
  attributes as written (`"400"`, `"100%"`, null when missing), `alt` its alt text (null when it has none), `hasAlt`
  whether the attribute is there at all (`alt=""` is there, and fine), `altLength` its length as `image-alt-too-long`
  measures it, `srcset` whether it has one, and `loading` its `loading` attribute. The first `<img>` of the page with
  that URL; null for a target that isn't an image, or one the page names only another way (a CSS background, a link).
- **Order**: by id, as before, except `image-too-large`, whose largest file comes first.
- An earlier engine's analysis, or a crawl made before the crawler read images' sizes, has these fields null where
  the crawl didn't record them, never an error.

### Links (E3b)

The links grid: every link the crawl stored is a row, in the order it stored them (a page's links together, in page
order), with both ends' URLs and everything the URL grid knows about each end. It reads as the URL grid does: one
snapshot per call, a request given up stops its read, reads queue, and an analysis or a continue never waits on one.

`POST /api/crawls/{id}/links` takes the URL grid's query shape and returns one page of links:

```json
{"columns":["from.url","to.url","kind","text","followed","to.statusCode"],
 "filters":[{"column":"kind","op":"in","values":["hyperlink"]},{"column":"to.statusClass","op":"in","values":["4xx"]}],
 "sort":[{"column":"to.url"}],"offset":0,"limit":100}
```

```json
{"columns":["id","from.url","to.url","kind","text","followed","to.statusCode"],
 "rows":[{"id":8812,"from.url":"https://www.example.com/blog/","to.url":"https://www.example.com/old/","kind":"hyperlink",
          "text":"Our old range","followed":true,"to.statusCode":404}],
 "offset":0,"matching":31,"total":21914,"next":"…","summary":{"available":true,"reason":null},"unavailable":[]}
```

- **`columns`**, **`filters`**, **`offset`**, **`limit`** (1 to 1,000, default 100), **`after`** with the `next` cursor,
  `matching`, `total`, `summary` and `unavailable` are the URL grid's, with the links' columns below. `id`, the link's
  id, is always first; the default columns are `from.url`, `to.url`, `kind` and `text`. `total` is every link in the
  crawl. Counts are cached as the URL grid's are.
- **While a crawl runs, each answer is of one moment of it**, at most 2 s old. `matching`, `total`, the rows a page
  holds, the facets and a cursor are all of the links the crawl had at that moment, whichever calls counted them: a
  page is never short of what `matching` and its offset say it holds, each facet's counts add up to `matching`,
  `matching` is never more than `total`, and the newest links are a full page. Only what the columns of a link's two
  URLs show is read as the page is (a target fetched since shows its status). A crawl takes links away only when it
  fetches a page again, whose links it stores again: a call that finds links of its moment gone answers for the
  crawl as it then is.
- **A link's own columns** say what the link is. **`from.<column>`** and **`to.<column>`** are the URL grid's columns
  for the page the link is on and for its target, every one of them: `to.statusCode`, `to.indexability`, `from.depth`,
  `to.inlinks`, and `from.check` and `to.check` as filters. They mean, filter and need the analysis exactly as in the
  URL grid, because the same code reads them: a filter on them is run as a URL grid filter, and a link matches when
  its end is one of the URLs that filter matches. A filter or sort on one that needs the URL summary, without one, is
  refused with `needs-analysis`, the column named as it was asked for (`to.inlinks`).

  | Column | Type | Sorts | Is |
  | --- | --- | --- | --- |
  | `id` | number | Yes | The link's id: the order the crawl stored links in. The default sort |
  | `kind` | enum | | `hyperlink`, `image`, `script`, `stylesheet`, `iframe`, `media`, `icon`, `canonical`, `hreflang`, `pagination`, `metaRefresh`, `redirect` or `socialImage` |
  | `text` | text | | The anchor text of a hyperlink, the alt text of an image, or the language of an hreflang link |
  | `position` | enum | | Where on the page: `content`, `navigation`, `header`, `footer`, `sidebar` or `head` |
  | `noFollow`, `sponsored`, `ugc` | boolean | | The link's own `rel` says it |
  | `imageLink` | boolean | | A hyperlink around an image, whose text is the image's alt text |
  | `selfLink` | boolean | | The link leads to the page it's on |
  | `followed` | boolean | | Whether search engines follow a hyperlink: no nofollow on it, nor on its page. Null for anything but a hyperlink |
  | `fragment` | text | | The `#fragment` the link names, without the `#` |
  | `from.id`, `from.url`, `from.<column>` | as the URL grid's | | The page the link is on |
  | `to.id`, `to.url`, `to.<column>` | as the URL grid's | Where the URL grid sorts by it | The link's target |

  Every column filters, with the operators of its type, but `from.metaRobots`, `from.xRobotsTag` and their `to.` twins,
  which are shown only, as in the URL grid. A value this engine doesn't know (a newer engine's kind of link) is null.
- **`search`** matches the links whose text, or either end's URL, contains the text, ignoring the case of ASCII
  letters.
- **`sort`** is one key: `id` (the default), or a column of the target that the URL grid sorts by (`to.url`,
  `to.statusCode`, `to.inlinks`...), with unknown values last. A target sort keeps each target's links together: ties
  go by the target's id, in the key's direction, and a target's links are in crawl order. More than one key is a field
  error (`sort`), and so is a column that doesn't sort (`sort[0].column`).
- **Paging** is the URL grid's. A cursor names its link: one that has gone, or whose target's sort value has changed,
  is refused with `invalid-query` on `after`.

`GET /api/link-columns` lists the columns as `/api/url-columns` does, `from.` and `to.` columns included:
`[{ id, type, needsAnalysis, sortable, filterable, values, title, unit, valueTitles }]`. The last three (E5) are the
column in words, as the URL grid's are: a link's own ("Kind", "Anchor or alt text"), and either end's as the URL
grid's under "Source" or "Target" (`from.url` is "Source URL", `to.statusCode` "Target status").

#### Link facets

`POST /api/crawls/{id}/links/facets` takes `{ filters, search, facets }` and counts the matching links by value, each
value with the `filter` that selects it, as the URL grid's facets do:

```json
{"matching":21914,"total":21914,"summary":{"available":true,"reason":null},"unavailable":[],
 "facets":{"kind":[{"value":"hyperlink","count":17641,"filter":{"column":"kind","op":"in","values":["hyperlink"]}}],
           "follow":[{"value":"followed","count":17600,"filter":{"column":"followed","op":"is","value":true}},
                     {"value":"nofollow","count":41,"filter":{"column":"followed","op":"is","value":false}},
                     {"value":"notHyperlink","count":4273,"filter":{"column":"followed","op":"isEmpty"}}]}}
```

| Facet | Values |
| --- | --- |
| `kind` | As the column, and null (a kind this engine doesn't know) |
| `position` | As the column, and null |
| `follow` | `followed`, `nofollow` and `notHyperlink` |
| `to.statusClass` | The target's status class, as the URL grid's, and null (not fetched) |
| `to.isInternal` | true (the site's own URL) and false |

#### Link ends (E6)

`POST /api/crawls/{id}/links/ends` lists the URLs at one end of the links a view matches, each once: the view's
targets, or the pages its links are on, however many links the view holds. It is what "Copy the target URLs" reads
for a view of more than a page of links, and what [`linkEnds`](#urls-e3a) narrows the URL grid to.

```json
{"end":"to","filters":[{"column":"kind","op":"in","values":["hyperlink"]},{"column":"to.statusClass","op":"in","values":["4xx"]}],
 "offset":0,"limit":100}
```

```json
{"end":"to","total":4572,"links":50318,"offset":0,
 "rows":[{"id":212,"url":"https://www.example.com/old/","links":31}],
 "summary":{"available":true,"reason":null}}
```

- **`end`** is `to` (the URLs the links lead to) or `from` (the pages they are on); it must be given. **`filters`** and
  **`search`** are the links grid's, and none of either is every link. **`offset`** and **`limit`** (1 to 1,000,
  default 100) page the list; there is no cursor, and a page past the end has no rows. The links grid's own `/links`
  and `/links/facets` refuse `linkEnds` (`bad-request`): they take the filters themselves.
- **`rows`** are in the order the crawl found the URLs (by `id`), each with **`links`**, how many of the view's links
  have it at that end. **`total`** is how many URLs there are at that end, and is the URL grid's `matching` under the
  same `linkEnds`; top-level **`links`** is the links the view matches, the links grid's `matching`. So the rows'
  `links` add up to it.
- **What it costs.** The targets come from what the links grid counts by target anyway: the link summary's rows where
  it holds what the filters test (20 to 58 ms for the first page on the 100k crawl at p95: every link's 112,588
  targets, the 4,572 targets of the hyperlinks to 4xx URLs), else the pass over the links that gives the view its
  count. **The pages take a pass over every link**, as nothing counts links by the page they're on: once per set of
  filters, kept as the view's other counts are, and split over connections as any pass is. On the 100k crawl's 5
  million links that is 155 to 280 ms where the sidecar has read slots free, **inside the budget**, and 475 to 740 ms
  on one connection (a two-core machine, every slot taken, a running crawl), **over it**; the same for the targets
  of a view beyond the link summary (a text filter: 170 ms split, 505 to 565 on one). The URL grid narrowed to
  either end costs the same, as it reads the same set. Not measured at 1M URLs: the pass grows with the links.
- **While a crawl runs** an answer is of one moment of it, as the links grid's are: its rows, `total` and `links`
  are of the links the crawl had then, and the URL grid narrowed within the same 2 s counts the same URLs.
- Errors: `invalid-query` on `end`, `filters[…]`, `search`, `offset` and `limit`; `needs-analysis` for a filter that
  needs the URL summary, without one.

#### Links grouped by target (step 3b)

`POST /api/crawls/{id}/links/groups` lists the targets of the links a view matches, one row a target, with what the
links that lead to it are: a broken footer link is one row, however many pages carry it. It is the Links screen's
"Group by target", for any filter or preset the grid has.

```json
{"filters":[{"column":"to.statusClass","op":"in","values":["4xx","5xx"]}],"sort":[{"column":"pages","desc":true}],
 "offset":0,"limit":100}
```

```json
{"total":9,"links":312,"pages":265,"offset":0,"sort":{"column":"pages","desc":true},
 "rows":[{"id":4410,"url":"https://www.example.com/old-range/","links":265,"pages":265,"text":"Old range","texts":1,
          "positions":["footer"],"likelyTemplate":true,
          "target":{"statusCode":404,"outcome":"clientError","contentType":"text/html","sizeBytes":5120,
                    "pixelWidth":null,"pixelHeight":null,"final":null,"indexability":"clientError","canonical":null}}],
 "summary":{"available":true,"reason":null}}
```

- **`filters`** and **`search`** are the links grid's; none of either is every link. **`offset`** and **`limit`**
  (1 to 200, default 100) page the list; there is no cursor, and a page past the end has no rows.
- **`sort`** is one key at most: `pages` (the default, descending: the target the most pages link to first),
  `links`, or a column of the target that the links grid sorts by (`to.url`, `to.statusCode`, `to.sizeBytes`...), in
  the order that grid's own sort puts the targets (unknown values last, ties by the target's id in the key's
  direction). By `pages`, ties go by `links` and then by the target's id; by `links`, by `pages` and then the id. A
  column the links grid doesn't sort by, or more than one key, is a field error (`sort.column`, `sort`); a sort by a
  target column that needs the URL summary, without one, is `needs-analysis`, as the links grid's is. The answer's
  `sort` is the order used.
- **Each row** is a target: `id` and `url`; **`links`**, the view's links to it; **`pages`**, the pages those links
  are on, each once; **`text`**, the text most of them have (the anchor, an image's alt text), among its first 200
  matching links in crawl order, the first found on a tie, and null when none of those has any; **`texts`**, how many
  texts those 200 have between them (none counted as one); **`positions`**, where on their pages its links sit, in
  the positions' order; **`likelyTemplate`**, linked from at least 10 pages and at least half the pages the view's
  links are on, as a check's group is called one; and **`target`**, what it is, as a check group's `related` reads
  (its status, and its type, size and pixels after any redirects, where those end, why it isn't indexable).
- **`total`** is how many targets there are (`/links/ends`' `total` for the same links), top-level **`links`** the
  links the view matches (the links grid's `matching`, which the rows' `links` add up to), and **`pages`** the pages
  the view's links are on, each once (`/links/ends`' `total` with `end` `from`).
- **What it costs.** A page's links are stored together, one after another, so a target's links from one page come
  one after another in crawl order: the pages are counted in **one pass over every link in crawl order**, a number
  a target, never a set of pages, kept per filter set as the view's other counts are, and split over connections as
  any pass is (the page that straddles two parts is counted once). It never reads the links on an index, which
  gives a target's links in another order. Then each page of the list reads its rows only: each target's text, on
  the links' index by target, and its facts. Measured on the 100k crawl's 5 million links (112,609 targets), the
  first page, half way down and just past it, p95 over ten runs each, on a 32-core desktop other lanes were loading:

  | The view | One connection | Split (eight) | The pass kept |
  | --- | --- | --- | --- |
  | Every link, by pages | 822 to 1,039 ms | 249 to 255 | 9 to 16 |
  | The hyperlinks to 4xx URLs (4,572 targets) | 497 to 539 | 121 to 128 | 4 to 5 |
  | The images, the largest first (103 targets) | 264 to 277 | 90 to 101 | 10 to 17 |
  | The links whose text contains a common word | 574 to 926 | 168 to 186 | 11 to 21 |
  | Every link, by target URL | 836 to 971 | 209 to 227 | 4 to 9 |

  So a grouped view's first read is **inside the 300 ms budget where the sidecar has read slots free, and over it on
  one connection** (a two-core machine, every slot taken, a running crawl, whose pass is never split), as
  `/links/ends` for the pages is; every read after it, another page or another order, is a few milliseconds. A
  filter that names one URL (`to.id`) takes the pass too. Not measured at 1M URLs: the pass grows with the links.
- **While a crawl runs** an answer is of one moment of it, as the links grid's are.
- **`pixelSizes`** (round 2) is whether the crawl measured images' pixel sizes: false for a crawl made before they were
  read, and not continued since, whose `pixelWidth` and `pixelHeight` are all null. `/links` and `/urls/images` say it
  too. The app then leaves out its pixel size columns and says the dimensions weren't measured.
- **Export**: the export kind `linkGroups` writes the grouped view one row a target ([Export](#export-e5)).
- Errors: `invalid-query` on `filters[…]`, `search`, `sort`, `sort.column`, `sort[i].column`, `offset` and `limit`;
  `needs-analysis` for a filter or sort that needs the URL summary, without one; `bad-request` for a field it doesn't
  take (`end`, `columns`).

#### How the links are read

A crawl of 100,000 pages stores 5 million links, and one of a million 50 million, so no read may look at every link
and stay in budget. The analysis of a crawl therefore stores a **link summary**, two small tables of counts
([the engine's README](../engine/README.md#link-summary)), both by what a link is: its kind, position, flags,
whether it leads to its own page, has text, and sits on a nofollow page. It counts the links, which only a run of the
crawl changes, so it's kept under the crawl's own stamp: analysing the crawl again leaves it as it is, and it's read
whichever engine analysed the crawl last, with or without a URL summary.

- **By target** (`link_summary`, 216,000 rows for the 100k crawl's 5 million links). It answers, without reading a
  link: `matching` for any filters on `kind`, `position`, the flags, `selfLink`, `followed`, whether `text` is empty,
  and anything about the target (`to.*`); the facets, under those filters; for a sort by the target, how many matching
  links each target has, so a page anywhere is found by adding them up; and which targets a filter's links lead to.
- **By block of 16,384 consecutive link ids** (`link_blocks`, 3,400 rows). It says where in crawl order the links of a
  filter on what a link is are, so a page anywhere is read from the blocks that hold it: at most a block for each
  link of the page, however few links match and however large the crawl.

So a page in crawl order is read:

- with no filter, by the links' ids, which are the places;
- under filters on what a link is, from the blocks that hold the page;
- under filters on the target, on the links' index by target, which holds each target's links in crawl order:
  - through the targets, all of their links read once and kept, when they have few in all (the links to the crawl's
    4xx URLs);
  - a page at a time, when they have many (a menu's targets: a few dozen URLs with half the crawl's links between
    them). The index counts the targets' links between two ids without reading them, so the id a page starts after
    is closed in on from where an even spread of the links would put it, and the page is read from the ids after
    it: no link is read to be passed over, at any depth, and nothing is kept;
  - with more targets than are listed for the index (20,000), a page near either end is walked to, and deeper in
    the links' ids are read through the targets once, an index entry a link, and kept.

  All of that needs the index to hold what the filters ask of a link: its target, and at most one kind (`kind` in
  one value). One target named outright (`to.id` equal to one URL, or `to.url` equal to one) is walked on the index at
  any depth, whatever else is asked.
- under a filter on the target that **most of the links it could match pass** (E3c), from the blocks again: the links
  to the site's own URLs, the hyperlinks to URLs that answered, the navigation links to the pages a click from the
  start. The blocks count links by what they are, so they count the links the rest of the filters match (every
  link, when the target is all that's asked); the others among those, which lead to the targets that fail, are the
  fewer, and are read once through those targets. A block's matching links are the difference, so a page anywhere
  is read from the blocks that hold it, whatever else is asked of the link (its kind, position, flags), nothing is
  read for every matching link, and link ids missing (a crawl that fetched pages again) change nothing.
- under filters on the target together with what that index doesn't hold (a link's position, its flags, whether it
  has text, more kinds than one), **where the fewer links pass**, or the others are too many to read (more than an
  eighth of the crawl's links): through the targets when they have few links in all; otherwise by walking the
  links from the nearer end when the page is near one; otherwise from a set of the matching links' ids, read once in
  one pass over the links and kept (a bit per link). That pass is over budget, as the next paragraph's are.

**A pass over every link is split over several connections** (E6) where the sidecar has read slots free: the links
are divided by id into as many stretches as there are connections (eight at most: the read's own and up to seven
more), each read on a thread of its own, and what each kept and counted is put together. The answer is the one
connection's, to the link.

Measured on the 100k crawl's 5 million links, the first read of a view, the same read timed in turn by the engine
from before E6 and by this one, on one connection, on four and on eight (three rounds of ten runs; other work was
loading the machine throughout, which is why one connection takes half as long again as on a quiet one). First
page, p50 over the rounds, in ms:

| The view's filter | Before E6 | One connection | Four | Eight |
| --- | --- | --- | --- | --- |
| Text contains a common word (2.2 million links) | 778 to 914 | 665 to 934 | 172 to 187 | 113 to 123 |
| Text contains rare text (28 links) | 268 to 283 | 273 to 281 | 98 to 101 | 64 to 66 |
| A search (7,609 links) | 1,301 to 1,615 | 1,233 to 1,592 | 289 to 297 | 183 to 211 |
| The facets of a text filter | 847 to 1,156 | 815 to 968 | 191 to 196 | 127 to 145 |
| Text matches a pattern | | 853 to 1,284 | 240 to 249 | 146 to 161 |
| From pages four clicks from the start (3.6 million links) | | 819 to 1,536 | 357 to 606 | 210 to 228 |

So a pass on one connection takes what it took, a pass on four a quarter of it, and a pass on eight a sixth: **with
eight, each of these reads' first page is inside the 300 ms budget at p50**, where four left the search at it and the
filter on the source's depth over it. That is why a pass takes up to seven helpers and not three: twice the cores,
for a fraction of a second. At p95 the worst of the eight-connection reads (first page, half way down or just past
it) was 86 to 489 ms over the three rounds, and of the four-connection ones 139 to 532, with one of 1,295 as the
machine was busiest; so they are **inside or near the budget when cores are free, not always inside it**, and as
they were when none is. A page half way down takes some 50 to 100 ms more than the first on several connections
(the rows it shows aren't ones the read's own connection has just passed over). Not measured: a quiet machine, and
a crawl of a million URLs.

- **No more read slots than the rule allows.** A connection more is a read slot more, taken only from those free at
  that moment, never waited for, and never the last one free, so a read arriving meanwhile starts at once; with none
  free the pass runs on its own connection. On a two-core machine it never splits, and on four cores it takes two
  helpers at most. A pass having taken a slot looks again, and gives it back if that was the last after all (a read
  or another pass took the one it counted on leaving). Two requests for the same view (its page and its facets)
  share one pass: the second waits for the first's.
- **What it holds while it runs**: each connection keeps its own ids and counts until they are put together, about a
  megabyte a connection for 5 million links (so some 10 MB a connection, briefly, for a crawl of a million pages).
- **A running crawl's pass is never split.** It stays on the read's one connection and one snapshot, which is the
  moment it answers for, so the time a running crawl's log goes unfolded is what it was.
- **Only for crawls worth it**: from 500,000 links. Nothing is stored for it, in the file or by the analysis.
- **A writer still never waits.** Each connection is a read like any other to an analysis or a continue starting:
  all are stopped together, and the read runs again after.
- A regular expression's time limit (3 s for a read's matches) is each connection's own, so a pattern near the limit
  on one connection may pass on several.

**Without the link summary, or beyond it, every link is read once per filter set.** That is: a crawl that's running,
never analysed, continued since it was last analysed, or last analysed by an engine from before E3b (whose analysis
is not out of date for that, and gains the link summary when the crawl is analysed again); on any crawl, a filter
on the link's text or
fragment, on its `id`, on the page it's on (`from.*`), or a `search`; and, deep in a grid whose targets have many
links, a filter on the target together with one on a link's position or flags that the fewer links of that position
or those flags pass, whose count and facets the summary still gives. One pass then gives the set of matching ids, with the count and the facets where the summary doesn't,
kept per filter set (a running crawl's for the 2 s its reads answer for one moment), so the pages that follow cost a
page. A filter
that matches few source pages (`from.id`, or `from.*` matching up to a twentieth of the crawl's URLs) reads only their
links, through the links' index by source, as a page's links are stored together. The pass is the one read over
budget; its cost is under [Engine changes E3b made](#engine-changes-e3b-made). An unfiltered grid never needs it,
analysed or not: its rows are found by their ids, and only the number of links is counted, every 2 s while a crawl
runs (a crawl that fetched a page again has gaps in its link ids: without a link summary, its ids are then read once).

### Sitemaps (E3b)

`GET /api/crawls/{id}/sitemaps` lists every sitemap the crawl looked for, in the order it found them:

```json
{"discovery":"done","summary":{"available":true,"reason":null},
 "sitemaps":[{"id":1,"url":"https://www.example.com/sitemap.xml","parentId":null,"foundVia":"robotsTxt","state":"read",
              "statusCode":200,"isIndex":true,"entries":0,"sitemaps":3,"error":null,
              "urls":{"listed":265,"fetched":262,"indexable":250,"nonIndexable":12,"robotsDisallowed":1,"redirectingOrBroken":4},
              "listedSitemaps":3},
             {"id":4,"url":"https://www.example.com/sitemaps/missing.xml","parentId":1,"foundVia":"index","state":"failed",
              "statusCode":404,"isIndex":false,"entries":0,"sitemaps":0,"error":"404 Not Found",
              "urls":{"listed":0,"fetched":0,"indexable":0,"nonIndexable":0,"robotsDisallowed":0,"redirectingOrBroken":0},
              "listedSitemaps":null}]}
```

- **`discovery`** says how far the crawl got with its sitemaps: `off` (it was told not to read them), `running` (it's
  still reading them), `unfinished` (it stopped before it had read them all, so the list isn't all there is) or
  `done`.
- **`foundVia`** is how the crawl came by the sitemap: `robotsTxt` (the start URL's robots.txt declares it), `options`
  (the crawl was given it), `defaultLocation` (nothing declared one, so the crawler tried `/sitemap.xml`), or `index`
  (a sitemap index lists it: `parentId` is that index). It's worked out when read, from the stored robots.txt and the
  crawl's options, so it's null for a sitemap neither names any more (robots.txt was read again by a later run, and
  had changed).
- **`state`** is `read`; `failed` (it couldn't be read, or not to its end: `error` says why, "404 Not Found", "Timed out",
  the XML parser's message, and the URLs read before the failure are still listed); `notRead` (left unread at the
  crawler's limits of 1,000 sitemaps and 3 levels of index: `error` says which, and there may be more like it); or
  `pending` (found, not read yet: the crawl is running, or stopped first).
- **`entries`** is the URL entries the crawl read in the file, as it counted them: every `<loc>`, repeats included.
  **`sitemaps`** is the sitemaps an index lists that the crawl has.
- **`listedSitemaps`** (E6) is how many sitemaps the file itself lists, as the crawl read it: every `<sitemap>` entry,
  repeats and entries that aren't URLs included, so "the crawl has 3 of its 3 sitemaps" is `sitemaps` of
  `listedSitemaps`. It's never less than `sitemaps`.
  - **An index over the crawler's limits** says so here: the file is read to its end whatever the limits, so an index
    of 5,000 sitemaps has `listedSitemaps` 5,000, while `sitemaps` counts those the crawl has of it: the ones it
    read, up to its limit of 1,000 sitemaps in a crawl, and the first it left unread (`notRead`), whose `error` says
    which limit. The same goes for an index nested deeper than 3 levels.
  - It's 0 for a sitemap that lists only URLs. A file whose read failed part-way has what it listed up to there, as
    `entries` does.
  - **Null when it isn't known**: the file's body was never read (it failed first, as a 404 does; it was left unread;
    it's still pending), or the crawl was made by an engine from before E6, which didn't keep the count. A crawl
    continued by this engine has it for the sitemaps read from then on.
- **`urls`** counts the URLs the sitemap lists, each once: for an index, the URLs of every sitemap under it, a URL
  two of them list counted once. `fetched`, `indexable` and `nonIndexable` are as the URL grid's `fetched` and
  `indexable` columns judge them, so each count opens its URLs in the grid below with that filter (`fetched` is true,
  `indexable` is true or false); the listed URLs with neither verdict weren't fetched, or were only status-checked.
  Without a URL summary they are the crawl's own judgement, as in the URL grid.
  - **`robotsDisallowed`** and **`redirectingOrBroken`** (the E3c review) count the listed URLs by what became of the
    request for each, as the URL grid's `outcome` column has it, read from each URL's response with the URL summary or
    without. Each is its rows in the sitemap's grid under one filter:

    | Count | The listed URLs | Filter on `/sitemaps/urls` |
    | --- | --- | --- |
    | `robotsDisallowed` | robots.txt kept the crawl from | `{"column":"outcome","op":"in","values":["robotsDisallowed"]}` |
    | `redirectingOrBroken` | that answered with a redirect or an error, or didn't answer | `{"column":"outcome","op":"in","values":["redirect","clientError","serverError","timeout","networkError"]}` |

    `redirectingOrBroken` leaves out the URLs robots.txt or a firewall kept the crawl from, and those not checked
    (their host had stopped answering): none of those is known to be broken. Both are among `fetched` (the crawl is
    done with them), so "crawled and answered" is `fetched` less `robotsDisallowed`. An index's are of every sitemap
    under it, each URL once, as its other counts are. A sidecar from before the review sends neither.
- A sitemap is stored under the URL it was found at; where that redirects to, and when it was fetched, isn't stored.
- A crawl with no sitemaps answers with an empty list, never an error.

`POST /api/crawls/{id}/sitemaps/urls` is the URL grid for one sitemap: the URLs it lists (an index: every sitemap
under it). It takes `{ "sitemapId": 1 }` with the URL grid's query (`columns`, `filters`, `search`, `sort`, `offset`,
`limit`, `after`) and answers as the URL grid does, every column, filter and sort included, with `total` the URLs the
sitemap lists and `matching` those the filters match. `sitemap-not-found` (404) for an id the crawl doesn't have, and
`invalid-query` without one. With no filter, `matching` is `total`: one count. While a crawl runs the grid reads as
the URL grid does, its rows live and its counts kept for 2 s, so under a filter the two counts, and the rows against
them, can be a moment apart.

```json
{"sitemapId":1,"columns":["url","statusCode","indexability"],"filters":[{"column":"indexable","op":"is","value":false}],"limit":100}
```

### robots.txt (E3b)

Each origin's robots.txt as the crawl read it, and the URLs it kept the crawl from. The crawl reads robots.txt on
every origin it asks for a URL on, other sites' included, so a crawl can have thousands: the list pages.

`POST /api/crawls/{id}/robots` takes `{ offset, limit, search, blocked }` (limit 1 to 1,000, default 100), every field
optional:

```json
{"search":"example","blocked":true,"offset":0,"limit":100}
```

```json
{"total":17,"offset":0,"respected":true,"userAgent":"ShoutingIguana","blocked":2,
 "origins":[{"origin":"https://www.example.com","isInternal":true,"state":"read","statusCode":200,
             "fetchedAt":"2026-10-31T20:13:58.588Z","error":null,"bytes":79,"blocked":2}],
 "matching":1,"counts":{"all":3,"blocked":1,"notBlocked":2}}
```

- **`search`** (E6) finds origins by their name: those whose origin contains the text, case ignored, at most 1,000
  characters (`invalid-query` on `search` past that). **`blocked`** (E6) is the filter: `true` for only the origins
  that blocked at least one URL, `false` for only those that blocked none. The list keeps its order under both, and
  `offset` and `limit` page what matches.
- **`matching`** (E6) is the origins the search and the filter match, which the page is of. `total` stays every
  origin of the crawl, and the top-level `blocked` every URL the crawl was kept from, whatever is asked.
- **`counts`** (E6) is what a filter bar shows: of the origins the `search` finds (every origin with none), `all`, those
  that `blocked` something and those that didn't (`notBlocked`). Each is the `matching` its filter gives under the
  same search, so a chip's number is the rows it opens: `blocked` with `"blocked": true`, `notBlocked` with
  `"blocked": false`, `all` with neither. A sidecar from before E6 sends neither `matching` nor `counts`, and refuses
  `search` and `blocked` as fields it doesn't know (`bad-request`).

- The site's own origins come first, the start URL's before the others, then the rest by the URLs they blocked, most
  first, then by name.
- **`state`** is what the answer meant to the crawl:
  - `read`: a 2xx, and its rules applied.
  - `missing`: another 4xx than 429, or redirects that couldn't be followed. No rules, so everything was allowed.
  - `unreachable`: it couldn't be read, and **the crawl refused the origin's URLs** until it could, as RFC 9309 asks:
    a 5xx or a 429 from any origin, or no answer at all from the start URL's own origin. Those URLs are the origin's
    `blocked`, each with the reason `unreachable`.
  - `unanswered`: no answer at all (a timeout, a refused connection, a failed TLS handshake) from an origin other
    than the start URL's, another site's or one of the site's own (its `www` twin, its other scheme). **The crawl gave
    no verdict and went ahead**: nothing was refused, and the origin's URLs show their own network errors. That
    departs from RFC 9309 on purpose (an origin that never answers isn't a site that asks not to be crawled), so
    `isInternal` with `unanswered` is not a site that was refused.

  `error` is the failure when there was no status. An engine from before the E3b review said `unreachable` for both
  of the last two.
- **The state is the last answer's; `blocked` is the whole crawl's.** Only the last answer from an origin is kept,
  while a URL refused stays refused in the file until it's fetched. So an origin can read `unanswered` (or `missing`,
  or `read` with rules that refuse nothing) **and still have blocked URLs**, and that isn't a contradiction:
  - **A crawl continued later.** An earlier run read a file that refused those URLs (or couldn't read it, and
    refused them as `unreachable`); the later run asked again, got no answer, and kept that. The URLs were refused
    earlier in the crawl, by an answer the file no longer has: the lines that refused them aren't there to show, so
    the origin's file (below) counts them under `unmatched`, as `unreachable` or `unknown`.
  - **A file made by the first engine** (before `111c7f3`, 31 October 2026), which refused an origin's URLs on no
    answer whichever origin it was. This engine reads that stored answer as `unanswered` for any origin but the start
    URL's, as it would now treat it, though the engine that made the file did refuse them.

  A UI can say of both "refused earlier in the crawl": `blocked` over zero beside a state that refuses nothing
  means exactly that. The engine doesn't keep which of the two it was, nor the earlier answer: that would take a row
  for every answer an origin gave, for a sentence the count already backs.
- **`fetchedAt`** is when it was last read. A crawl continued later reads each robots.txt again, and keeps the last.
- **`bytes`** is the size of the text kept (at most 512 KB is read), or null when none was.
- **`blocked`** is the URLs on the origin that the crawl stored as refused by robots.txt (the URL grid's `outcome`
  `robotsDisallowed`); the top-level `blocked` is the crawl's.
- **`respected`** is false for a crawl told to ignore robots.txt: it still reads the start URL's, for the sitemaps it
  declares, and blocks nothing. **`userAgent`** is the name the crawl looked for its group by.

`POST /api/crawls/{id}/robots/file` with `{ "origin": "https://www.example.com" }` gives one origin's file, line by
line. `origin-not-found` (404) for an origin the crawl has no robots.txt for, and `invalid-query` without one.

```json
{"origin":"https://www.example.com","isInternal":true,"state":"read","statusCode":200,"fetchedAt":"…","error":null,
 "bytes":79,"blocked":12,"userAgent":"ShoutingIguana",
 "lines":[{"number":1,"text":"User-agent: *","kind":"userAgent","applies":true,"blocked":null,"problem":null},
          {"number":2,"text":"Disallow: /private/","kind":"disallow","applies":true,"blocked":12,"problem":null},
          {"number":3,"text":"","kind":"blank","applies":null,"blocked":null,"problem":null},
          {"number":4,"text":"Sitemap: https://www.example.com/sitemap.xml","kind":"sitemap","applies":true,"blocked":null,"problem":null}],
 "unmatched":{"unreachable":0,"unknown":0},"respected":true}
```

- **`respected`** (E6) is the list of origins' `respected`: false for a crawl told to ignore robots.txt, which blocked
  nothing by it. It's here so the file's page needn't read the list for it.

- **`lines`** is the text as stored, split at its line breaks, numbered from 1; empty unless `state` is `read`.
- **`kind`** is `userAgent`, `allow`, `disallow`, `sitemap`, `crawlDelay`, `comment`, `blank`, `other` (a directive the
  crawler doesn't use, such as `Host`) or `invalid` (not a `name: value` line).
- **`applies`** is whether the line is part of the group the crawl obeyed: the groups that name its user agent, or the
  `*` groups when none does. A `sitemap` line applies to everyone. Null for the other kinds.
- **`blocked`**, on a `disallow` line that applies, is how many of the origin's blocked URLs that line refused: the
  rule that won, the longest that matches (an `allow` wins a tie). Null on every other line. They add up, with
  `unmatched`, to the file's `blocked`.
- **`problem`** is why the crawler made nothing of a line, or null: `noUserAgent` (a rule before any `User-agent`
  line, which belongs to no group), `invalidValue` (a `Crawl-delay` that isn't a number), `unknownDirective` or
  `notADirective`.
- **`unmatched`** counts the blocked URLs no line accounts for: `unreachable` (robots.txt couldn't be read when the
  URL came up, which refuses everything) and `unknown` (the file kept doesn't refuse the URL: a later run read it
  again, and it had changed).

`POST /api/crawls/{id}/robots/blocked` with `{ origin, line, reason, urlId, offset, limit }` pages the origin's blocked
URLs, in crawl order; `line` narrows them to one line's, `reason` to one reason's and `urlId` (E6) to one URL, and all
three are optional:

```json
{"origin":"https://www.example.com","total":12,"matching":12,"offset":0,
 "rows":[{"id":3411,"url":"https://www.example.com/private/1","reason":"rule","line":2,"rule":"Disallow: /private/","error":null}]}
```

- **`reason`** is `rule` (with the `line` that refused it, and the line's text as `rule`), `unreachable` (`error` is how
  robots.txt failed, when stored) or `unknown`.
- **The matching rule isn't stored: it's worked out when read**, from the stored file, by the crawler's own parser and
  matcher (`RobotsTxtParser`, `RobotsTxt`), for the crawl's own user agent. There's no second implementation, so the
  line shown is the line the crawl obeyed, as long as the file kept is the one it read then.
- `total` is the origin's blocked URLs, and `matching` those the line or reason narrows to. Paging is by `offset` and
  `limit` (1 to 1,000, default 100): the list is worked out once per origin and kept, so any offset costs the same.
- **`urlId`** (E6) answers for one URL: its row, with why it was refused, and `matching` 1; or no rows and `matching`
  0 for a URL this origin didn't block (another origin's, one that was fetched, an id the crawl doesn't have). It's a
  number, as every URL's id is (`bad-request` otherwise). The inspector gives the same facts with the URL
  ([its `robotsTxt` test](#the-inspector-e3a)), so a screen that has inspected the URL needn't ask.
- An origin's answer, its lines and its blocked URLs are read and kept as one, so while a crawl runs the file's
  `blocked` is the URLs its lines and `unmatched` account for, and `/robots/blocked` lists that many. The list of
  origins is kept apart (for 2 s while a crawl runs): an origin's `blocked` there can be a moment behind its file's.

#### What blocking a URL costs (step 3b)

Each row of `/robots/blocked` also says what blocking the URL costs, as the analysis counted it, so the owner can see
which refusals matter: a URL the site's sitemap lists, or its menu links to, is one the site asks for and blocks.
The page says how many of the origin's blocked URLs do, and `conflict` lists them alone:

```json
{"origin":"https://www.example.com","total":12,"matching":1,"offset":0,
 "rows":[{"id":3411,"url":"https://www.example.com/private/listed","reason":"rule","line":2,"rule":"Disallow: /private/","error":null,
          "linkingPages":0,"inSitemap":true,"sitemaps":1,"sitemap":{"id":3,"url":"https://www.example.com/sitemaps/extra.xml"},
          "inMenu":false,"conflict":true}],
 "conflicts":1}
```

- **`linkingPages`**: the pages that link to the URL, a page's links to itself left out (the URL grid's
  `linkingPages`). The Links grid's links to it are `to.id` equal to its `id`, `selfLink` false, `kind` `hyperlink`.
- **`inSitemap`**: a sitemap lists it; `false` only when every sitemap was read and none does, `null` otherwise when
  none of those read does (the URL grid's `inSitemap`). **`sitemap`** is the first sitemap that lists it, in the order
  the crawl found them, by the `id` the Sitemaps screen opens, and **`sitemaps`** how many do; both null where
  `inSitemap` isn't true.
- **`inMenu`**: a page links to it from its navigation (a hyperlink whose `position` is `navigation`), a page's link
  to itself left out.
- **`conflict`**: `inSitemap` or `inMenu`. **`conflicts`**, on the page, is how many of the origin's blocked URLs
  conflict, whatever `line`, `reason`, `urlId` or `conflict` narrow the rows to.
- **`conflict` in the request** (optional): `true` lists only the URLs that conflict, `false` only those that don't;
  with `line`, `reason` and `urlId` as they narrow. `matching` is what it narrows to; `total` stays the origin's.
- **All of these are null** (and `conflicts` too, and `conflict` in the request lists nothing) while the crawl has
  changed since its analysis or was never analysed, as the URL grid's columns of the URL summary are: what they count
  is the analysis's. The UI says they're known once the crawl is analysed.
- **Cost of the read**: two lookups by key for each of the origin's blocked URLs (its row of the URL summary; a row of
  the link summary, or the links to it on their `(to_id, kind)` index where an older analysis left no link summary),
  never a pass over the links, kept as the origin's list is. `sitemap` and `sitemaps` are looked up for the page's
  rows alone, a lookup by key in each sitemap's list. readbench, 100k bench crawl (11 blocked URLs, 20 runs, nothing
  cached): the site's blocked URLs 47.8 ms at p95, the conflicts alone 48.6 ms, one blocked URL's row 50.0 ms (the
  `robots` gate is 200 ms). On a copy of it with 50,011 of its URLs stored as blocked: the site's file (no costs)
  105 ms at p50, its blocked URLs with their costs 141 ms at p50, 169 ms at p95.
- **The export** (`robotsBlocked`) takes `conflict` too, and writes every row with five more columns: Linked from
  (pages), In a sitemap (Yes or No), Sitemap (the first's address), In a menu (Yes or No) and Conflict ("In your
  sitemap but blocked", "In your menu but blocked", "In your sitemap and menu but blocked"), each empty while not
  known. `si export --kind robotsBlocked --conflict true` is the same.

### Export (E5)

What a grid shows as CSV or as an Excel workbook, the workbook of everything, and the report, each written **by the
sidecar** to a path the user chose in the shell's native save dialog. An export is a job, as an analysis is: a call
starts it and is answered at once, `export` events tell how far it has got and how it ended, and it can be cancelled.
An export only reads the crawl.

`POST /api/crawls/{id}/export` starts one and answers **201** with the export:

```json
{"kind":"urls","format":"xlsx","path":"C:\\Users\\me\\Documents\\example.com 2026-10-31 URLs.xlsx",
 "columns":["url","statusCode","indexability","title","inlinks"],
 "filters":[{"column":"type","op":"in","values":["page"]}],"search":"rattan","sort":[{"column":"inlinks","desc":true}]}
```

```json
{"exportId":"3f9a61c2d0b4e87a","crawlId":"…","kind":"urls","format":"xlsx",
 "path":"C:\\Users\\me\\Documents\\example.com 2026-10-31 URLs.xlsx","status":"running","rowsWritten":0,
 "rowsExpected":null,"startedAt":"2026-11-01T17:42:00.118Z","finishedAt":null,"result":null,"error":null}
```

- **These filters, these columns.** A grid's export is the grid's own read: its `columns`, `filters`, `search` and
  `sort`, as the grid's call takes them, with no `offset`, `limit` or `after` (an export writes every row; one of
  those in the body is a `bad-request`, as any property the API doesn't know is). The engine reads the rows with the
  code the grid reads them with, a page at a time by the grid's own cursor, so the file holds exactly what the grid
  shows, in its order.
- **`kind`** says what is exported, and which other fields the body takes. A field that belongs to another kind is
  refused (`invalid-export`, against that field), not ignored:

  | `kind` | Writes | `format` | Takes |
  | --- | --- | --- | --- |
  | `urls` | The URL grid | `csv`, `xlsx` | `columns`, `filters`, `search`, `sort`, `hiddenChecks` (round 5 review), and `linkEnds` (E7) as the grid takes it |
  | `links` | The links grid | `csv`, `xlsx` | The same but `linkEnds`, over the links grid's columns; one sort key |
  | `sitemapUrls` | One sitemap's URLs | `csv`, `xlsx` | `sitemapId`, and the URL grid's five |
  | `issueRows` | A check's rows, or one group's | `csv`, `xlsx` | `checkId`, `group` (as `/rows` takes it), and the URL grid's five, the check's own columns among them |
  | `robotsBlocked` | An origin's blocked URLs | `csv`, `xlsx` | `origin`, and `line`, `reason` and `conflict` (step 3b) to narrow them, as `/robots/blocked` takes them |
  | `check` | One check's sheet of the workbook, as a file of its own | `xlsx`, `csv` | `checkId`, `hiddenChecks` (round 5's review) |
  | `workbook` | The spreadsheet of everything | `xlsx` | `hiddenChecks`, `checks` (round 5) |
  | `report` | [The report](#report-e5) | `html` | `cut`, `examples`, `lookExamples`, `hiddenChecks` (round 5) |
  | `sitemaps` (E7) | The list of sitemaps, as `GET /sitemaps` lists them | `csv`, `xlsx` | Nothing |
  | `robotsOrigins` (E7) | The origins' robots.txt, as `POST /robots` lists them | `csv`, `xlsx` | `search` and `blocked`, as `/robots` takes them |
  | `linkGroups` (step 3b) | The links grouped by target, as [`/links/groups`](#links-grouped-by-target-step-3b) lists them: one row a target | `csv`, `xlsx` | `filters`, `search`, `sort` (one key: `pages`, `links` or a column of the target) and `hiddenChecks`, as the grouped view takes them; no `columns` |

- **`hiddenChecks`** (round 5, `report`, `workbook` and `check`): the ids of the checks the user hid, at most 500. They are
  left out of the lists, the counts and the sheets (discovery decision 2: "the analysis still runs it, and exports
  leave it out"); the score is the analysis's, and the document says how many checks that found something it left
  out ("2 checks that found something are hidden, so they’re left out of this report. The score still counts them.";
  the workbook's summary has a "Hidden checks left out" row). An id the catalog doesn't have is ignored: a check hidden
  by another version. `GET /report` takes them as `hiddenChecks=a,b`, so the report shown is the report saved. A
  `check`'s file is named as its sheet is in the workbook with the same checks hidden ("4 …", numbered afresh without
  them; R2-9: it was numbered against every check), and without a number when the check is hidden itself. **`checks`**
  (`workbook`): only these checks, the ones an Issues view listed; its summary says "Checks listed". `si export` takes
  them as `--hidden-checks` and `--checks`, comma-separated.
- **`hiddenChecks` on a grid's export** (round 5 review, also noted: `urls`, `links`, `sitemapUrls`, `issueRows`) is
  the grid's own ([URLs](#urls-e3a)): the file's issue counts leave those checks out as the grid's do, in its values,
  sort and filters. Its limits are the grid's, not the report's: an id the catalog doesn't have is taken, and more
  than 1,000 ids, a null or one over 200 characters is `invalid-query` on `hiddenChecks`, as on the grid's call.
  `si export --hidden-checks` takes it for these kinds too.
- **`columns`** are the file's columns, in the order given; without any, the grid's own (`url`; for `issueRows`,
  `url`, `detail` and `relatedUrl`; for `links`, `from.url`, `to.url`, `kind` and `text`). The id is a column only
  when asked for (`id`: "Crawl order"). A column that needs the URL summary, on a crawl that has none, is in the file
  with no values, and the result names it in `unavailableColumns`, as the grid lists it in `unavailable`.
  **(evidence)** `issueRows` without columns has `url`, `detail`, the check's `evidence`, then `relatedUrl`; and
  without `sort`, its rows come by importance, as `/rows` gives them. A column whose value is an object
  (`redirectChain`, `robotsRule`) is written as its line of text ([URLs](#urls-e3a)).
- **`path`** is the file to write: a full path on this computer, whose name ends in the format's extension (`.csv`,
  `.xlsx`, `.html` or `.htm`), so an export can't be made to write a program or a script. A name holding a character
  no file's name may have is refused (`invalid-export` on `path`): on Windows `\ / : * ? " < > |`, so `d.txt:x.csv`,
  which Windows would take for another stream of `d.txt`, is refused rather than written there. A path to another
  computer (`\\server\share\…`, a device path) is refused, as a crawl's is; a mapped drive letter stays allowed. The
  crawl's own file is never one. A file already there is replaced, once the new one is whole.

**The words are the engine's.** A file's header row is each column's `title`, with its `unit` in brackets where it has
one ("Response time (ms)", "Size (bytes)"), and an enum's value is written as its `valueTitles` say it ("Client
error"); true and false are "Yes" and "No"; an unknown value is an empty cell. `GET /api/url-columns` and
`GET /api/link-columns` give all three for every column, so a grid that shows the engine's titles and words shows what
its export writes:

```json
{"id":"responseMs","type":"number","needsAnalysis":false,"sortable":true,"filterable":true,"values":[],
 "issueRowsOnly":false,"title":"Response time","unit":"ms","valueTitles":{}}
```

A link's own columns have their titles ("Kind", "Anchor or alt text", "Links to its own page"), and either end's is
the URL grid's under "Source" or "Target": `from.url` is "Source URL", `to.statusCode` "Target status". The fixed
kinds have fixed headers: `robotsBlocked` writes "Blocked URL", "Blocked by" ("A rule", "robots.txt couldn’t be read"
or "No matching rule"), "Line", "Rule" and "Error"; `check` and the workbook's sheets write "Address", "Page title", "What
was found" and "Other address". The two lists (E7) write their screens' columns, in their cells' words:

- `sitemaps`: "Sitemap", "Index" (Yes or No), "Listed by" (the index that lists it), "Found via" ("robots.txt",
  "Crawl settings", "Default location", "Sitemap index", "Not known"), "Status" ("Read", "Read in part: its XML is
  broken at line 17", "Couldn’t be read: 404 Not Found", "None there: 404 Not Found", "Left unread", "Not read yet",
  "Never read", "Read, 11 of its 12 sitemaps"…), "Error" (what the crawl recorded), then the counts "Listed",
  "Crawled", "Blocked by robots.txt", "Not crawled", "Indexable", "Non-indexable" and "Redirecting or broken". A count
  is empty where the list shows a dash: a sitemap whose URLs aren't known, and an index until the crawl has read every
  sitemap under it. "Crawled" is `fetched` less `robotsDisallowed`, and "Not crawled" `listed` less `fetched`. The rows
  are the list's, in its order (the order the crawl found them), not the tree's: "Listed by" carries the tree.
- `robotsOrigins`: "Origin", "This site" (Yes or No), "robots.txt" ("Read", "Read: empty", "Read: rules ignored",
  "Missing: nothing refused", "Missing when last asked", "Couldn’t be read: URLs refused", "Couldn’t be read: ignored",
  "No answer: crawled anyway", "No answer when last asked", "No answer: ignored", "None kept"), "Status", "Error",
  "Blocked URLs", "Read" (a date, in the computer's own time) and "Size (bytes)", in the list's order.
- `linkGroups` (step 3b): "Target", "Status" (its code), "Outcome" (what became of the request, in the URL grid's words),
  "Linking pages", "Links", "Text" (the text most of its links have), "Other texts", "Positions" ("Footer, Navigation"),
  "Likely template" (Yes or No), "Content type", "Size (bytes)", "Pixel width", "Pixel height" (after any redirects)
  and "Redirects to" (where its redirects end), one row a target in the grouped view's order, on a sheet called
  "Links by target". Read from one snapshot of the file, a page of targets at a time: a running crawl's are of one
  moment of it. It costs the grouped view's pass over the links, then each target's row as the view reads it.

#### The files

- **CSV** is RFC 4180 as Excel reads it: UTF-8 with a byte-order mark, rows ended CRLF, a field holding a comma, a
  quote or a line break in quotes with its quotes doubled. Numbers are written as their digits. **No field is longer
  than Excel reads as one cell: 32,758 characters**, nine short of the 32,767 a cell holds. Excel ends the row where
  a field passes that, and starts a new row with the rest of the line, in column A, where nothing guards it: a formula
  at the end of a site's long URL (nothing caps a URL's length) would run. Longer text is cut to 32,758 characters,
  its guarding apostrophe among them, and the result counts the fields cut (`truncatedCells`). The limit was found by
  opening fields of every length around it in Excel 16: 32,758 is read whole in any column and any alphabet; 32,759
  isn't.
- **Excel** (`.xlsx`) is written a row at a time, never held in memory. Numbers are numbers and dates are dates; the
  header row is bold, stays in view and filters; a column is as wide as its first 200 rows need, within 9 to 80
  characters. A sheet holds 1,048,576 rows, so **an export of more rows goes on to further sheets** ("Links", "Links
  (2)"…), each under the same header, and the result's `sheets` says how many. A table may fill **16 sheets
  (16,777,200 rows)**: one with more is refused before anything is written (`export-too-large`: "export it as CSV,
  which has no limit, or narrow it with a filter"), never cut short. A cell holds 32,767 characters: longer text is
  cut to that, and the result counts the cells (`truncatedCells`).
- **Text from the site can't run.** A cell that starts with `=`, `+`, `-`, `@`, a tab or a carriage return is what a
  spreadsheet runs as a formula. In CSV such text gets an apostrophe before it (`'=HYPERLINK(…)`), which a spreadsheet
  shows as text; a number in a numeric column is written as the number it is, negative or not. In a workbook every
  text is a text cell, which nothing runs whatever it holds, and one that looks like a formula is also marked as Excel
  marks text typed after an apostrophe, so that editing the cell doesn't turn it into one. Control characters and text
  shaped like the format's own escapes come back as they went in.
- **The workbook** (`workbook`) is a "Summary" sheet, then a sheet for each check that found something:
  - *Summary*: the site, when it was checked and when its results were worked out, the pages read and the addresses
    checked, the checks run, the score with its word and version, and the five counts; then a row for **every check**:
    its number, its sheet, whether it found something, found nothing or couldn't run (with why), its severity as the
    report says it and as the checks call it, its plain title and its technical one, its id and category, its count,
    what that's a count out of, its share, and the points it takes off the score.
  - *A check's sheet* is named by the check's number in the report and the start of its title, in the 31 characters a
    sheet's name holds ("3 Large images that slow pages…"), and holds every URL the check flagged, in crawl order: the
    address, the page's title, what was found, and the other address involved.
    **(evidence)** By importance, not crawl order (as `/rows` without `sort`). After "Other address" come the check's
    evidence columns, each under its title ("Title", "Title length", "Meta robots", "Redirect chain"; "Page title"
    already being there, `title` isn't repeated), then where to fix it. A `sources` check's where-columns are as they
    were. A `targets` check's are named by what its targets are, each target written with what it is and how the page
    links to it:

    | Checks | Columns | One target in the cell |
    | --- | --- | --- |
    | The links checks | "Links to (addresses)", "Links to" | `https://x/old/ (404; footer, “Old range”)`; one that redirects `https://x/old (301 → https://x/new/ 200; content, “Shop”)`; with a fragment `https://x/faq/#specs (200; content, “Specs”)`; an image link `https://x/ (200; header, image link)` |
    | The canonical checks | "Canonical" (one column: a page has one canonical) | `https://x/a (301 → https://x/b/ 200)`, `https://x/a/ (200; noindex)`, `… ; from the HTTP header` |
    | The image checks | "Images (addresses)", "Images"; and for `image-too-large` "Largest image (bytes)", a number, so the sheet sorts by it | `https://x/hero.jpg (200; 1.8 MB, image/jpeg, 4000×3000 px, shown at 400×300; content, alt “Sofa”)`; without alt `… ; no alt`; without a height `… ; no height; the file is 1200×800 px` |
    | The hreflang checks | "Alternates (addresses)", "Alternates" | `https://x/de/ (404; “de”)`, `https://x/de/ (200; noindex; “de”)` |
    | `mixed-content` | "HTTP resources (addresses)", "HTTP resources" | `http://x/a.js (200; script in the head)` |

    Up to 10 a cell, then "and N more", as before. [The per-check table](v2-evidence.md#what-the-exports-carry) has
    which check is which.
  - `check` writes one of those sheets as a file of its own (a check that found nothing: its header alone).

#### The job

- **`status`** is `running`, then `done`, `cancelled` or `failed`. Only `done` leaves a file: the file is written
  beside its target under another name and moved into place once whole, so a cancelled or failed export leaves
  nothing, and a file it was to replace is as it was.
- **`rowsWritten`** of **`rowsExpected`** is how far it has got: rows of data, headers apart. `rowsExpected` is null
  until the first page is read, and for the report; the workbook's is its checks' rows. It is never less than
  `rowsWritten` (see **While a crawl runs**).
- **`result`**, once done: `{ rows, bytes, sheets, unavailableColumns, truncatedCells, crawlWasRunning }`.
  `truncatedCells` counts the cells cut to what a spreadsheet takes: 32,767 characters in a workbook, 32,758 in CSV.
- **`error`**, once failed, is the problem a call would have been answered with: `{ type, title, status, detail?,
  errors? }`. Its `title` is a plain sentence to show as it is.
- **Events.** An `export` event carries the export, whole, as above: when it starts, at most every 250 ms while rows
  are written, and when it ends. A stream that connects meanwhile is told the running exports in `hello` (`exports`,
  their ids) and gets each one's latest event after it. An export that has ended is no longer in `hello`.

| Method and path | Does |
| --- | --- |
| `GET /api/exports/{exportId}` | The export, as above: one that's running, or one of the last 64 that ended. `export-not-found` (404) otherwise |
| `POST /api/exports/{exportId}/cancel` | 202. A running export stops within a page of rows and leaves no file; one that has ended stays as it ended. `export-not-found` for an id nobody started |

- **Refused at once**, before anything is read or written, with the answer a problem and no export started: a body
  that isn't an export (`invalid-export`, with `errors` by field: `kind`, `format`, `path`, a field of another kind,
  `examples`…); a query the grid would refuse (`invalid-query`, as the grid's own call gives it); an unknown check
  (`check-not-found`); a folder that isn't there or can't be written to (`export-not-writable`); a file of that name
  open in another program, as Excel holds a workbook it's showing (`export-file-in-use`); too many exports, or one
  already writing that file (`export-busy`).
- **Said by the export as it fails**, in its `error`, as only the crawl can tell: `not-analyzed`, `needs-analysis`
  (with `reason` and `columns`), `check-skipped` (the last analysis didn't run the check: it has no rows, which isn't
  none), `sitemap-not-found`, `origin-not-found`, `export-too-large`, `export-crawl-changed`, and what can go wrong
  with the file once it's being written: `export-disk-full`, `export-not-writable`, `export-file-in-use`.
- **At most 4 exports run at once**, each on a thread of its own; a fifth is `export-busy`. They don't take the
  grids' read slots.
- **Shutdown** cancels every export as it begins, with the crawls and analyses, and nothing new starts: an export
  part-way through its file stops within a page and deletes what it wrote.
- **A sidecar that is killed** (the shell ends it, or it crashes) has no chance to delete an export's part file, which
  would stay in the user's folder (`.links.xlsx.<32 hex digits>.tmp`, and `….strings` beside a workbook's). So the
  sidecar notes each part file's full path in its own state folder (`exports-in-progress.json`) before it creates the
  file, and takes it out once the file has gone. A sidecar that starts with that state folder deletes each file the
  note names that is still there, and only those (the user's folder is never searched for files that look like one),
  then empties the note, before it serves anything. A file it can't delete is left, and logged. `si export` keeps no
  note: Ctrl+C cancels its export, which deletes its part file; a `si export` that is killed leaves its part file.

**While a crawl runs**, an export is of the crawl **as it stood when the export began**: nothing the crawl finds
after that is written, and `crawlWasRunning` says so. Each row is read, and tested against the filters, as its page is
read, so a URL shows the status it had then (as the links grid's pages do). Its rows are in crawl order, or by URL: a
`sort` by anything a running crawl changes (a status, a count) is refused with `invalid-query` on `sort`, as rows
would change places between two pages. `rowsExpected` is the grid's count as the export began, which can be a moment
older than the rows the export reads: where more rows turn up than were counted, `rowsExpected` rises with
`rowsWritten`, and never stands below it. It can also end a little above it, where the count had rows the export's
bound leaves out.

**An export never holds the file.** Each page is a read of its own, on a snapshot it lets go of before the next, as a
grid's page is: an analysis or a continue starting here is kept waiting no longer than by a grid, and a running
crawl's write-ahead log is never held open across two pages, so the log can be folded into the file and started again
however long the export takes. In return, **a crawl that changes under an export ends it** (`export-crawl-changed`):
analysed again, continued or finished, its pages would be of two different crawls. The export says so and leaves no
file; exporting again exports the crawl as it now is. A crawl that is paused, or resumed, is the same crawl, and the
export goes on to its end, of the crawl as it stood when it began.

### Report (E5)

The report is the one document a crawl ends in: a single self-contained HTML file, to
[the design](v2-ui-simple.md#the-report). Inline CSS, inline SVG, no script, no web font, light only; it prints to A4
and Letter from the print dialog, which is also how it becomes a PDF.

`GET /api/crawls/{id}/report?cut=full|summary&examples=5&lookExamples=3` returns the document, as `text/html` in
UTF-8. `not-analyzed` (404) for a crawl never analysed: it has no report. The export job saves the same document to a
path (`kind` `report`), and `si report` writes it from the command line.

- **`cut`** is `full` (the default), or `summary`: the report's first page alone (its head, the summary, what to fix
  first, how the score is worked out, and the foot). The first list says where each problem is explained by its
  section ("under the same number in “What to fix”"), never by a page: which page that starts on depends on the paper
  and on how long the score's table is (page 4 for the 100k crawl's 45 lines). The summary is the full report's own
  opening, so the two can't differ.
- **`examples`** and **`lookExamples`** are how many of its first pages (by importance: see below) each problem to fix and each
  thing worth a look lists: 5 and 3 by default, 0 to 50. The rest are in the workbook, on the sheet with the problem's
  number. A value out of range, another `cut` or another parameter is `invalid-export`, by field.
- **Its outline** is the design's: the head; the summary (the score as a ring, its word, what it covers, the five
  counts); "Fix these first" (up to five, by their numbers); "How the score is worked out"; then, from a new page,
  "What to fix" (every check that found something, in three groups, numbered in the order to fix them: its plain title
  with its technical title and severity beside it, its count and share, its points, who usually fixes it, what's wrong,
  how to fix it, a "One fix" line where one cause is behind most of its pages, and its first pages); "What couldn't be
  checked", with why; "What was checked and found nothing wrong"; and "About this check".
- **A problem's pages** (evidence) are its most important first, as `/rows` without `sort` gives them (`examples`
  counts them from there, not in crawl order). Each says, beside the page:
  - *What was found*: the detail, then the check's evidence, a value each, in the check's order: "780 px, over 580 (78
    characters) · “Garden sofas and chairs for every garden and every…” · Title in results: Garden sofas…", "noindex
    (meta robots)", `301 → https://x/b/ (301) → https://x/c/ (200)`. In plain words the four title and description
    length checks say instead where Google will likely cut, "Google will likely cut this title off after “…for every
    garden and”", or "Only 10 characters long", as Simple does, never px;
  - *Where to fix it*: for a `sources` check, the pages linking to it with where on each the link is and its text
    ("/garden/ (footer, “Old range”), /sofas/ (content, “Sale”) and 3 more"), the sitemaps listing it, the start
    address; for a `targets` check (new: these said only "and N others"), what the page names, each with what it is,
    in the workbook cell's words (`Image https://x/img/hero.jpg (1.8 MB, 4000×3000 px, shown at 400×300) and 1 other`).
- **"One fix"** (evidence) is said too for a check that doesn't group, when its `likelyTemplate` is true ("This is on
  nearly every page, so it's probably your site's template: one fix clears it."); and for the image checks, now grouped
  by image, when one image is behind most of their pages.
- **Its length follows the checks that found something, never the site's size**: what it reads is counts, and a few
  rows a check.
- **Every state is the document's own to say.**
  - *Stopped early, or failed*: a note under the site ("This check was stopped early: 312 pages read, 1,380 addresses
    still to check. This report covers only what was read."), the score "so far" on a dashed ring, and how to finish
    under "Finished?" (continue the check).
  - *At its URL limit, or its limit of clicks from the start*: the same note in its own words ("This check stopped at
    its limit of 500 addresses: 212 pages read, 1,840 addresses past the limit."; "…its limit of 3 clicks from the
    start: … further in than that."), the score "so far", and "check the site again with a higher limit".
  - *Still running*: "This check hasn't finished", with what is still to check.
  - *Nothing to fix*: "Nothing to fix" in place of the first list, with how many things are worth a look, and no "What
    to fix" unless there are those.
  - *No score*, with its reason in the score's place (the analysis's `noScoreReason`): no page could be read
    (`noPages`: it names the start URL, and says so if robots.txt kept the crawl out), or the results were worked out
    before the score was (`notScored`: an analysis by an engine older than the score, which says to work the results
    out again). The ring is its track alone, and "How the score is worked out" is left out.
  - *Checks that couldn't run* are listed under "What couldn't be checked", each by what it looks for (`plainLooksFor`,
    or its plain title) with the reason's plain text and what to do about it (`plainText`, `plainAction`); a reason a
    later engine wrote, which this one has no words for, is said in its stored text.
  - *Results that no longer stand* (the crawl went on, or the checks changed) are said to be out of date under the
    site, with their date. That the crawl went on since is said whoever asks for the report (the CLI too); that the
    checks or their thresholds changed, when the sidecar asks (it knows the thresholds an analysis would run with
    now). **A crawl that went on** leaves out what could only be read from the crawl as it now stands, which would sit
    beside figures of the results ("Pages that can appear in search: 4,064 of 3,113"): the pages that can appear in
    search, what wasn't read, the note of what is still to check; "Finished?" says it isn't known from these results.
  - *What robots.txt kept out* is the site's own addresses its robots.txt refused, as the analysis counted them for
    the score (`covers.blockedByRobots`, or the `blocked-by-robots` check's count where there is no score): an address
    on another website that its own robots.txt refuses is no part of "the site's rules file".
- **The score, the order and the words are the analysis's and the catalog's**; the document works out none of its
  own.
  - The score, its lines, "so far", the ceiling and "Fixing these five takes the score from 85 to 98" are the score
    the analysis stored (`GET /analysis`'s `score`), line for line. "Addresses checked" is the URLs crawled when the
    analysis ran (its `crawledUrlCount`), which its shares are of, not the crawl's count now.
  - Problems are numbered in the engine's "Fix first" order (`fixFirst`): the first list, the sections and the
    workbook's sheets carry one number.
  - A check's title, what's wrong, how to fix it, who fixes it, its good news and its "One fix" sentence are its
    plain words, with its technical title and severity beside the title. A limit named in them (`{limit:…}`) is
    filled with the thresholds **the analysis ran with**, as stored with it, so a crawl analysed with a title limit of
    70 says 70. A check with no plain words has its technical texts instead.
  - **A check reported once for the whole site** (`reportedOnce`: one row, on the home page, however many pages are
    at fault) says "Checked once, for the whole site" where another says "5 of 265 pages": in its section's first
    line, in "Fix these first", and in the workbook's "Of what" (with "Out of" and "Share" left empty). In the score's
    table, a fault of the whole site (`siteWide`) reads "The whole site", and a check reported once that isn't one
    (`sitemap-error` is the only such error) reads "Checked once, for the whole site (counts as a few)", since its
    points are those of "a few". That last wording is the engine's own: the design has no line for the case.
- **In print**, a problem's heading stays with its text and its examples stay whole; "What to fix" starts a page; the
  score's table repeats its header on each page it runs to, and its sum is printed once, at its end. Nothing names a
  paper size, so the printer's own is used: the garden crawl's report is 12 pages of A4 and 13 of Letter.

**What a site sends can't become markup.** Every string from the crawl is escaped where it's written, as text and
inside an attribute alike; control characters, and the marks that reverse the text after them, are left out. A URL is
a link only when it is `http` or `https` (a `javascript:` or `data:` URL a site wrote is shown as text), and a link
opens apart from the document (`target="_blank" rel="noopener noreferrer nofollow"`). The document carries
`<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'sha256-…'; base-uri 'none';
form-action 'none'">`: nothing may be loaded or run, and the only style is its own one sheet, named by its hash. There
isn't a script, a style attribute, an image or a form in it.

**Showing it.** The answer carries the API's own headers like every other, so its address is nothing a browser could
open as a page: the UI reads the document as text (`api.report`) and gives it to a sandboxed frame, as its `srcdoc`.
What that frame needs was tried in Chromium 156 (what WebView2 is built on), WebKit and Firefox, with the garden
crawl's report in a `srcdoc` frame of a page served under each policy below:

- **A `srcdoc` frame answers to two policies**: the document's own (its `<meta>`), and **the policy of the page that
  holds it**, which a `srcdoc` document inherits. Both must allow the report's one inline style sheet, or the report
  shows unstyled: its text in the browser's default look.
- **The app's policy must allow that sheet in `style-src`**, by one of:
  - `'unsafe-inline'`, with **no nonce and no hash in the same directive**. The app's policy today (`style-src 'self'
    'unsafe-inline'` in `app/src-tauri/tauri.conf.json`) is this, and the report shows styled under it, as that
    policy stands.
  - the sheet's hash, `'sha256-…'`, which is in the document's own policy (read it from there, or from the engine:
    `ReportDocument.ContentSecurityPolicy`). It is the same for every report one build of the engine makes, and
    changes whenever the sheet does, so it is copied at build time, not typed.
  - **Not** a nonce beside `'unsafe-inline'`: a browser ignores `'unsafe-inline'` as soon as the directive holds a
    nonce or a hash, and the report then shows unstyled (tried: `style-src 'self' 'nonce-…' 'unsafe-inline'`). Tauri
    can add nonces and hashes to a policy when it builds the app; if it adds one to `style-src`, the report's hash
    must be listed too, which works beside a nonce (tried). **Not tried in the built shell**: what the built app's
    `style-src` holds has to be looked at there.
- **Nothing else is needed.** The report loads nothing, so `default-src`, `img-src`, `font-src` and `connect-src` don't
  matter to it, and `frame-src` doesn't govern a `srcdoc` frame (tried with `frame-src 'none'`).
- **`sandbox`** with no tokens is enough to show it: no script runs (there is none), and the report's links do
  nothing, as they open a new window and the frame may not (`allow-popups` would let them; in the saved file they
  work). **To print the frame from the app** (`frame.contentWindow.print()`), the frame needs
  `sandbox="allow-same-origin allow-modals"`: without the first the page may not reach into the frame (a
  `SecurityError`), and without the second a sandboxed document may not open the print dialog. Scripts stay off
  either way. Reaching `print` was tried; the dialog itself, and printing in the built shell on each OS, were not.
- The frame's text is the string the app was given, so the page that holds the frame must itself be read as UTF-8
  (the app's is).

### Search (E6)

`POST /api/crawls/{id}/search` is search as it's typed, for the palette: the best few of the crawl's URLs whose URL or
title contains the text, on every keystroke. A `POST` with the text in the body, so a keystroke costs no preflight.

```json
{"text":"gard","limit":10}
```

```json
{"results":[{"id":31,"url":"https://www.example.com/garden-sofas/","title":"Garden sofas | Example","isInternal":true,
             "statusCode":200,"outcome":"success","rank":"start","matchedIn":"url"}],
 "matching":412,"total":6286}
```

- **`text`** is what's typed so far, at most 1,000 characters. **`limit`** is 1 to 50 (default 10). No `text`, one too
  long or holding a zero character, or a `limit` out of range is `invalid-query` (422) on that field; a `text` that
  isn't a string is a `bad-request`, as every fixed-type field is.
- **What matches is what the URL grid's `search` matches**: the URL or the title contains the text, the case of ASCII
  letters ignored. So **`matching`** is the grid's `matching` under `{"search": text}`, and "All 412 in URLs" lands on
  412 rows; **`total`** is every URL in the crawl.
- **The order** is the page a person means first:

  | `rank` | The text |
  | --- | --- |
  | `exact` | is the URL: the whole of it, with or without its scheme, or its path (with its query string, if it has one). The slashes around the path don't count: `about`, `/about` and `about/` all name `https://www.example.com/about/`, and `/` names the home page |
  | `start` | starts the URL's host name, with or without its `www.` (`fac` in `https://www.facebook.com/`), or a segment of its path (`gard` in `/shop/garden-sofas/`; text that begins with a slash starts one wherever that slash is one of the path's); never the scheme (`http` starts nothing), and never inside the query string or the fragment, whatever slashes they hold. In a title: at its start, or after a character that isn't a letter or a digit (`(garden` in `Sets: (garden) sofa`), whatever the text begins with |
  | `anywhere` | is somewhere else in the URL or the title |

  Within a rank: the site's own URLs before other sites'; a match in the URL before one in the title; the shorter URL
  (or title) first, so `/blog/` comes before `/blog/2024/a-long-post/`; then the URL the crawl found first. A URL that
  matches both ways takes the better of the two, and **`matchedIn`** (`url` or `title`) says which its rank is for.
- **Each result** carries what a list shows and what opens it: `id` (for `/urls/inspect`), `url` and `title` as the crawl
  stored them (`title` null without one), `isInternal`, and `statusCode` and `outcome` as the URL grid's columns have
  them.
- **An empty `text` finds nothing**: no results, and `matching` equal to `total`, as the grid with no search matches
  every URL. It reads the crawl's text (below), so the palette can ask it as it opens and the first letter typed is
  answered as fast as the rest.
- **Nothing is stored for it.** The crawl's URLs and titles are read into the sidecar's memory by the first search of
  a crawl as it stands (one pass over the URLs and one over the responses, in a grid read slot), and kept there while
  the crawl stands still: every search after that is a pass over that text and reads the file only for its few results'
  statuses, so it takes no read slot and never queues behind the grids' reads. A crawl made by any engine, analysed or
  not, is searched the same way. The text takes about 106 bytes a URL (11.4 MB for the 100k crawl's 112,590), and two crawls' are
  kept: the one being searched and the one before it.
- **While a crawl runs, each answer is of one moment of it**, at most 2 s old: the results, their titles, `matching` and
  `total` are of the URLs and titles the crawl had at that moment, and only each result's `statusCode` and `outcome` are
  read as they now stand (a result fetched since shows its status). The text is read again once it's 2 s old, by the
  next search.
- **What it costs**, on the 100k crawl (112,590 URLs), p95: a keystroke with the text kept 1.3 to 4.5 ms, whatever is
  typed (one letter that every URL matches, forty characters that none does); the first search of a crawl, which reads
  the text, 55 to 89 ms (49 to 78 ms at p50), over five runs on a shared machine, the review's among them. That first read is the one over the palette's 50 ms: the empty search
  as the palette opens is what keeps it off the first keystroke.

### Favicon (E6)

`GET /api/crawls/{id}/favicon` gives the site's icon as the crawl kept it, for the library, the top bar and the
sidebar: **the image's bytes, under the media type they are**, `200`. A crawl that has none answers `204` with no
body, and the app draws the site's first letter. A crawl's item says which to expect: `hasFavicon`
([Library](#library-e1)). Like every call it needs the token, so the app fetches it and shows the bytes; an
`<img src>` can't ask for it.

- **Where the crawl looks.** The first page of the site that a run parses names the icons: in a new crawl that is the
  start URL's page, or the page it redirects to. Its `<link rel="icon">` elements are the candidates (`rel="shortcut
  icon"` is one; a touch icon and a mask icon aren't), then `/favicon.ico` at that page's origin, where browsers look
  when a page names none.
- **Which is kept.** The app shows the icon in a tile of 16 to 32 px, twice that on a dense screen, so the best is the
  smallest icon the page declares (`sizes`) as 64 px or more; failing that, the largest it declares below 64; then the
  icons with no size, in page order. They are tried in that order, three of the page's at most, then `/favicon.ico`,
  and the first that turns out to be an image the engine keeps is the icon. So a crawl asks a site for its icon one to
  four times, and usually once.
- **What is kept is told by its bytes, never by what the site calls it.** The icon is content from the site, so it is
  hostile until shown otherwise: a site can send a page, a script or an SVG under any name and any `Content-Type`.
  Kept: **PNG, ICO, GIF, JPEG and WebP**, each known by its signature, its size read from its header, and its
  structure whole to the end of its image data: a PNG's chunks and their checksums to its header, its image data
  and its end; a GIF's blocks to an image and its trailer; a JPEG's segments to its scan and its end; a WebP's
  chunks inside its length. So a header with nothing after it, or with a page after it, isn't kept. An ICO's
  directory must hold up too: every image it lists inside the file, each a PNG that holds up as one or a bitmap whose
  header and pixels do, of the size the directory gives it and no more than 1,024 px a side; the icon's size is its
  largest image's. The pixels aren't decoded, so an image whose compressed data is damaged can still be kept: the app
  then shows a broken image, and nothing in it can run. Not kept:
  **anything else, SVG above all** (it can carry script; one is never even asked for, by its declared type or its
  file name), anything over **512 KB** (it is read no further than that), and any image whose size can't be read or
  is over **1,024 px** a side (a few hundred kilobytes can declare a picture that takes gigabytes to draw).
- **Politely.** Each request waits for its origin's robots.txt and obeys it when the crawl does, takes its turn at
  the host's gate as any request to that host does (its concurrency, its rate, a crawl delay, a backoff), stops while
  the crawl is paused, and has the crawl's request timeout. A redirect is followed three hops at most, each held to
  the same rules. An icon on another site is asked for only when the crawl checks other sites' links.
- **It is no URL of the crawl.** Nothing is discovered, stored or counted for the fetch: it is in no grid, no count,
  no progress figure and no byte total, and a host that fails it is not a site that is down. (An icon the page links
  is, as before, also a link of kind `icon` and a URL the crawl checks: that check and this fetch are two requests.
  And if the icon's origin is one whose robots.txt the crawl hadn't read yet, that robots.txt is kept and listed as
  any the crawl reads is.)
- **It never fails a crawl or holds one up.** It runs beside the crawl, which hears of none of its failures. A crawl
  that completes while its icon is still on its way gives it 5 s, by the wall clock, then goes on without (a crawl
  of a handful of pages is over before its icon arrives; a request's own timeout is 20 s). A crawl that is stopped,
  or fails, doesn't wait at all.
- **Once.** A crawl that has its icon keeps it: a run that continues it asks for none, and the first kept stands. A
  crawl that has none (its site had none the engine keeps, its first page never answered, or an engine from before
  E6 made it) looks once more when it is continued, from the first page that run parses, by the same rules.
- **Where it is kept.** In the crawl file, in a table of its own (`favicon`, one row: the bytes, where they came
  from, when), written only by the crawl's writer. A crawl file can come from anywhere, so the file is trusted no
  more than the site was: as the icon is read, its bytes are told to be an image again, more than 512 KB of them are
  never read, and what is served is the type the bytes are, whatever the file calls them. A file whose "icon" is a
  page, an SVG or anything else answers `204`.
- **The headers that keep it an image** are those every answer has: `X-Content-Type-Options: nosniff`, so a browser
  never guesses another type from the bytes; `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`,
  so nothing would run or load were it ever opened as a document; and `Cache-Control: no-store`. The `Content-Type`
  is one of `image/png`, `image/x-icon`, `image/gif`, `image/jpeg` and `image/webp`, and no other.
- **While a crawl runs** it answers `204` until the icon is kept, a few requests after the first page, and the image
  from then on; `hasFavicon` turns true on the crawl's item the next time its file is read.
- Errors: `crawl-not-found` (404), `unauthorized` (401).

### Changes since another crawl (fix loop)

A crawl compared with the site's previous crawl, or with another crawl of the site: by check, by cause and by URL. The
design, its states and why each rule is as it is: [the fix loop](v2-fix-loop.md#2-what-is-compared-and-where).

**On each library item** (`GET /api/crawls`, `GET /api/crawls/{id}`):

```json
{"siteKey":"example.com",
 "previous":{"id":"41ab…","startedAt":"2026-11-03T09:12:00Z","score":{"value":71,"band":"good","partial":false},
             "comparable":"full","needsAnalysis":false},
 "changes":{"score":14,"partial":false,"fixed":["internal-client-error","no-https"],"new":["sitemap-error"],
            "fewer":["broken-internal-links","title-too-long"],"more":["image-too-large"],"newCheck":[],"notCheckedNow":[]},
 "rechecks":{"count":1,"last":{"id":1,"endedAt":"2026-11-17T14:02:41Z","checkId":"broken-internal-links","judged":5,"fixed":4}}}
```

- **`siteKey`** is the site as the library groups it: `site` (the host without `www.`), then `:port` when the start
  URL has a port of its own (`127.0.0.1:5001`). The app groups by it, and the sidecar picks the previous crawl by it, so
  the two can't disagree. Null for a file with no start URL.
- **`previous`** is the crawl this one is compared with by default, or null when there is none (a first crawl): the
  newest crawl of the same `siteKey` that started before it, has an analysis that read a page, and isn't live, in use,
  interrupted, missing or unreadable. **One with the same start URL comes first**; only without one is it the newest of
  the site. `comparable` is as on `/changes` (below); `needsAnalysis` is true when the two analyses were made by other
  rules (engine, checks or limits), so the comparison is counts only until the previous one is analysed again.
  - **As built (S1).** "Started before" is Home's order: the earlier `startedAt`, and of two that started at the same
    moment the one with the **larger id** (Home lists the smaller first, as the newer). "An analysis that read a page" is
    `pagesAnalyzed` above 0. A crawl that is removed from the library, or whose file is gone from the data folder, isn't
    a candidate. `previous` is **null for a crawl never analysed** too: there is nothing to compare. `startedAt` and
    `score` are the previous crawl's, as its own item has them.
- **`changes`** is the check-level change, worked out from the two crawls' cached summaries without opening either
  file: `score` (now less then; null when either has no score or they were scored by different versions), and the
  checks by state. Every check is in it, hidden or not: the app leaves out the hidden ones, as it does everywhere.
  **`partial`** is true when either crawl didn't read all it set out to (its score is partial, or it has none and didn't
  complete): the change is "so far". A check now at 0 is listed under `fewer`, never `fixed`, when **this** crawl may
  not have read what the other flagged (it stopped early, or read under half the other's pages), the rule `/changes`
  has without its rows; otherwise it is `fixed`, which `/changes`, with its rows, can make `fewer` where some of them
  weren't read again. `notCheckedNow` lists the checks that ran then and were skipped now. Null with `previous`, and
  **while the crawl is live here, interrupted, in use elsewhere or unreadable**: its analysis then doesn't describe it.
  Worked out by the engine's `CrawlComparability.FromSummaries`, which a test holds to `/changes` on every pair the
  comparison has a case for.
- **`rechecks`** is the crawl's rechecks: how many, and the last one in brief. Null for a crawl never rechecked.
- **Cost.** None to list: the library's summary cache keeps each check's count and a fingerprint of the options a
  comparison looks at, beside what it kept before (its format changes, so each file is read once more after an
  upgrade). As built: `summaries.json` is format 6; a test counts the cache's file reads while listing and getting a
  crawl, and finds none.

**`GET /api/crawls/{id}/changes?with={otherId}`**, the comparison in full. Without `with`, or with `with=previous`,
it is with `previous`. `with` names a crawl of the same `siteKey`; another site's, or one that can't be compared,
is `crawl-not-comparable` (409), and a crawl with no previous answers 200 with `with` null and nothing else, which the
UI shows as nothing at all (never zeros). `not-analyzed` when this crawl was never analysed.

As built (S1): exactly `{"crawlId":"…","with":null}` for a crawl with no previous one. `with` may name a crawl that
started later (the comparison reads "until"). `crawl-not-comparable` also for `with` naming the crawl itself, and when
the other crawl's analysis has gone by the time it is read; `crawl-not-found` for an id no crawl has; `bad-request` for
`with` given twice or empty. The comparison is the engine's `CrawlComparisonReader.ReadSummary`, served field for field
(a test holds every field to it), read in a read slot and kept in the read cache under both files' stamps.

```json
{"crawlId":"9f2c…",
 "with":{"id":"41ab…","startedAt":"2026-11-03T09:12:00Z","finishedAt":"2026-11-03T09:49:25Z","status":"completed",
         "startUrl":"https://www.example.com/","analyzedAt":"…","pagesAnalyzed":265,"crawledUrlCount":4480,"chosen":"previous"},
 "comparable":"partial",
 "differences":[{"code":"stoppedEarly","side":"now"},
                {"code":"limits","field":"maxUrls","then":null,"now":5000}],
 "previousNeedsAnalysis":[],
 "score":{"then":{"value":71,"band":"good","partial":false},"now":{"value":85,"band":"good","partial":true},"change":14,
          "lines":[{"checkId":"no-https","then":60,"now":0},{"checkId":"broken-internal-links","then":7,"now":4}]},
 "totals":{"checks":{"fixed":5,"new":2,"fewer":6,"more":1,"same":60,"newCheck":1,"notCheckedNow":0},
           "issues":{"fixed":38,"gone":3,"notChecked":3,"still":120,"new":5,"newUrl":1,"notComparedThen":0}},
 "checks":[{"checkId":"broken-internal-links","severity":"error","severityThen":"error","state":"fewer","then":9,"now":4,
            "fixed":5,"gone":0,"notChecked":0,"still":4,"new":0,"newUrl":0,"notComparedThen":0}]}
```

- **`with.chosen`** is `previous` (the default) or `picked` (named by `with`).
- **`comparable`**: `full`; `partial` (the crawls differ in what they covered: see `differences`); or `countsOnly`
  (the start URL's scheme or host differs, so no URL can be matched, or `previousNeedsAnalysis` isn't empty). With
  `countsOnly`, every check's row split (`fixed` to `notComparedThen`) and `totals.issues` are null, and `score.change`
  is null where the scores' versions differ.
- **`differences`**, each `{ code, field?, side?, then?, now? }`:

  | `code` | What differs | Makes it |
  | --- | --- | --- |
  | `startOrigin` | The start URL's scheme or host | `countsOnly` |
  | `startPath` | The start URL's path | `partial` |
  | `scope` | `field`: `stayInStartFolder`, `includeSubdomains`, `followNofollow`, `crawlCanonicals`, `crawlHreflang` or `crawlPagination` | `partial` |
  | `limits` | `field`: `maxUrls` or `maxDepth` | `partial` |
  | `fetching` | `field`: `checkResources`, `checkExternalLinks`, `discoverSitemaps`, `extraSitemaps` or `respectRobotsTxt` | `partial` |
  | `client` | `field`: `userAgent`, `acceptLanguage`, `headers` (the names) or `useCookies` | Noted only |
  | `timeouts` | `field`: `requestTimeoutSeconds` or `maxRetries` | Noted only |
  | `stoppedEarly` | `side` (`then`, `now` or `both`) didn't read all it set out to (`score.covers.complete` false) | `partial` |
  | `coverage` | One read fewer than half the pages the other did: `then` and `now` are the two `pagesAnalyzed` | `partial` |
  | `rules` | The analyses were made by other rules, and the previous one can't be analysed again | `countsOnly` |

  A value that isn't stored (a header's value) can't differ here. As built: `field` and `side` are left out where a
  difference has none, and `then` and `now` are given for every option's difference (`null` for none, as a limit's
  none), and otherwise only where the difference has values (`startOrigin`, `startPath`, `coverage`, a `rules`
  version), so `{"code":"rules","field":"settings"}` and `{"code":"stoppedEarly","side":"now"}` have none.
- **`previousNeedsAnalysis`** is the other crawl's `outOfDateReasons` against this crawl's analysis (`engineChanged`,
  `settingsChanged`): the two analyses differ in analysis version, checks fingerprint, score version or limits.
  `POST /api/crawls/{otherId}/analyze` mends it; the comparison is then like for like. Simple's app does it by itself,
  Advanced's on a click.
- **`score`**: each side's stored score in brief, `change` (now less then), and `lines`, every check that took points
  on either side, with its points then and now (0 where it took none). Null when either crawl has no score.
- **`checks`** is every check that ran on either side, in this crawl's `fixFirst` order and then catalog order, each
  with its severity now and then, and:

  | Field | What it is |
  | --- | --- |
  | `state` | `new` (ran then and found nothing; finds something now), `fixed` (found something then; nothing now, and none of what it flagged went unchecked), `fewer`, `more`, `same`, `newCheck` (didn't run then: newer than that analysis, or skipped), `notCheckedNow` (ran then, skipped now) |
  | `then`, `now` | The URLs it flagged in each: `issue_types.url_count`. Null on the side it didn't run |
  | `fixed` | Flagged then; read again now and not flagged |
  | `gone` | Flagged then; this crawl didn't find the URL. For a `sources` check that means nothing links to it any more ("no longer linked") |
  | `notChecked` | Flagged then; this crawl found it but didn't read it, read it and got no usable page (an error, a block, a rate limit, robots.txt refusing it, a redirect away), or the check didn't run now. **Never counted as fixed** |
  | `still` | Flagged then and now |
  | `new` | Flagged now; the other crawl read the URL and didn't flag it |
  | `newUrl` | Flagged now; the other crawl didn't find the URL |
  | `notComparedThen` | Flagged now; the other crawl found the URL but didn't read it, or the check didn't run then |

  A URL is the same URL when its stored text is the same. One a response-code check flagged then that fails another
  way now ("404 then, 500 now") is `fixed` for the old check and `new` for the new one.
- **`totals`**: the checks by state, and the rows added up over every check. Every check is counted, hidden or not;
  the app counts from `checks` to leave hidden ones out.
- **Cost and caching.** The check level reads `issue_types`, `skipped_checks` and `analysis` in each file. The row
  split is one streamed pass per check over each file's issues and their URLs, then index lookups for the rows that
  moved: about 32,000 issues a side on the 100k benchmark, 323,000 on the 1M crawl. It's kept with the grids' counts,
  keyed by both files' stamps and analyses, so Overview and Issues read it once, and it takes a read slot as a grid
  does. Each file is read in its own snapshot, through its own read gate; nothing is written.

**`compareWith` on a check's groups and rows.** `POST /api/crawls/{id}/issues/{checkId}/groups` and `…/rows` take
`"compareWith": "previous"` or a crawl id in their bodies. Nothing else about them changes (the order, the columns,
the paging); each group and row gains `change`, and the answer gains `compared`:

```json
{"checkId":"broken-internal-links","grouping":"relatedUrl","total":4,"groupCount":2,"offset":0,
 "compared":{"with":"41ab…","startedAt":"2026-11-03T09:12:00Z","comparable":"full","fixedGroups":3},
 "groups":[{"detail":null,"relatedUrlId":812,"relatedUrl":"https://www.example.com/old-range/","pages":3,"share":0.75,
            "likelyTemplate":false,"urls":3,"change":{"state":"fewer","then":5}}]}
```

```json
{"columns":["id","url","detail"],
 "rows":[{"id":31,"url":"https://www.example.com/sofas/","detail":"Links to 1 broken URL",
          "change":{"state":"still","detailThen":"Links to 2 broken URLs"}}],
 "offset":0,"matching":4,"total":4,"compared":{"with":"41ab…","startedAt":"2026-11-03T09:12:00Z","comparable":"full"}}
```

- A group's **`change.state`** is `new` (no such cause then), `fewer`, `more` or `same`, with `then` its URLs then. A
  cause is matched by its related URL's text, or by its detail or fault with the related URL's text. A duplicate
  cluster (`title-duplicate`, `description-duplicate`, `h1-duplicate`, `duplicate-content`,
  `near-duplicate-content`) is matched with the cluster then that shares the most of its pages, as its name (its
  first page) can change when that page is fixed.
- A row's **`change.state`** is `still`, `new`, `newUrl` or `notComparedThen` (as on `/changes`), and `detailThen` its
  detail then (null unless `still`).
- **`compared.fixedGroups`** is how many causes then have no URL now: what `/fixed` lists by cause.
- With `comparable` `countsOnly`, every `change` is null. A `compareWith` that can't be compared is
  `crawl-not-comparable`.
- Cost: at most two index lookups a group or row of the page, in the other file.
- **As built (S1).** Without `compareWith`, and with `"previous"` for a crawl that has no previous one, the answers are
  exactly as before: no `compared`, and no `change` on any group or row. With `countsOnly`, a row's `change` is `null`
  and a group's is **left out** (the engine's `IssueGroup.Change` isn't written when null). `compareWith` on `/urls`,
  `/links` or `/sitemaps/urls` is `bad-request`; `not-analyzed` when this crawl was never analysed.

**`POST /api/crawls/{id}/issues/{checkId}/fixed`**: what the check flagged in the other crawl and doesn't flag now,
by URL or by cause.

```json
{"compareWith":"previous","level":"rows","states":["fixed","gone"],"group":null,"offset":0,"limit":100}
```

```json
{"checkId":"broken-internal-links","compared":{"with":"41ab…","startedAt":"2026-11-03T09:12:00Z","comparable":"full"},
 "level":"rows","total":5,"offset":0,
 "rows":[{"url":"https://www.example.com/garden-furniture/","urlId":40,"thenUrlId":38,"state":"fixed","reason":null,
          "detailThen":"Links to 1 broken URL","relatedUrlThen":"https://www.example.com/old-range/"},
         {"url":"https://www.example.com/winter-sale/","urlId":null,"thenUrlId":977,"state":"gone","reason":null,
          "detailThen":"Links to 1 broken URL","relatedUrlThen":"https://www.example.com/old-range/"}]}
```

- **`level`**: `rows` (each URL) or `groups` (each cause with no URL now: `{ detail, relatedUrl, sharedText, then,
  fixed, gone, notChecked }`).
- **`states`** narrows to `fixed`, `gone` and `notChecked`; all three when left out. **`group`** narrows rows to one
  cause then, as `{ detail, relatedUrl }` (the related URL's text, as ids differ between files).
- A row's **`urlId`** is the URL's id in this crawl, null when this crawl didn't find it; **`thenUrlId`** its id in the
  other. **`reason`** says why a `notChecked` row wasn't judged: `{ code, text }` with `code` one of `notRead` (found
  but not fetched: a limit, stopped early, robots.txt couldn't be read), `failed` (an error or no answer, its status
  in `text`), `blocked`, `rateLimited`, `robots` (robots.txt refuses it now), `redirects` (it now forwards elsewhere),
  `checkSkipped`.
- Rows come in the other crawl's order by importance ([evidence](#issue-groups-e3a)), then by its URL id; `total` is
  the rows that match. The pass behind it is the check's, kept as `/changes` keeps it.
- **As built (S1).** The answer has `rows` at level `rows` and `groups` at level `groups`, never both. A crawl with no
  previous crawl answers `{"checkId":"…","compared":null,"level":"rows","total":null,"offset":0,"rows":[]}`. A level or
  a state the API doesn't have, and paging out of range, are `invalid-query` (`errors` keyed `level`, `states[0]`,
  `limit`); an unknown check `check-not-found`; `compareWith` as on `/changes`. The rows and causes are the engine's
  `CrawlComparisonReader.ReadCleared`, field for field.

**The report and the workbook.** `GET /api/crawls/{id}/report` takes `compareWith` (`previous` by default, `none`, or
a crawl id): with a comparison, the report gains "Since your last check on 3 November" after its Summary (the score
then and now, each problem fixed, new, fewer and more, and the crawl's rechecks), and its summary cut a line of it.
The workbook (`kind: workbook`) takes `compareWith` in its request: its summary sheet gains the count then and the
change, and a sheet lists the rows `/fixed` lists. A `compareWith` that can't be compared leaves the part out and
says why in About this check; it never fails the report.

As built (S1), in the engine's `Reporting/` and `Export/` (`ReportOptions.CompareWith`, the other crawl's file, and
`NotCompared`, the sentence to say instead; the sidecar resolves `compareWith` to them):

- **The report.** "Since your last check on 3 November 2026" comes after the Summary: the score then and now ("The score
  was 71 on …, and is 85 now: up 14."; "worked out another way" when the versions differ), one caution sentence for a
  partial or counts-only comparison, then the groups **Fixed**, **New**, **Fewer** and **More** (each check by its plain
  title, with its count then or now), then **Rechecked** (the crawl's settled rechecks, the latest five: "Rechecked on 17
  November 2026: 5 pages, 4 fixed"). The summary cut has one note: the score's sentence and the counts ("5 problems
  fixed, 2 new, 6 on fewer pages, 1 on more"). About this check gains "Compared with: The check of 3 November 2026, 09:12"
  (or the reason it wasn't: "Not compared: that crawl is of another site.") and "Rechecked: 3 times, last on …". Hidden
  checks and a view's checks are left out of the lists as everywhere in the report.
- **`GET /report?compareWith=`** takes `previous` (the default), `none`, or a crawl's id; given twice or empty it is
  `invalid-export`. A crawl with no previous one has no part and no "Compared with".
- **The export.** `compareWith` in the request of `kind` `report` or `workbook` (`previous` when left out); any other kind
  refuses it (`invalid-export`, `errors.compareWith`). It is the sidecar's own field, not the engine's `ExportFields`.
- **The workbook.** The summary sheet gains a "Compared with" row (and "Rechecked" with the count), and its table two
  last columns, "Found on 3 Nov" (the count then) and "Change" (`Fixed`, `New`, `Fewer`, `More`, `Same`, "Not looked for
  then", "Not checked now"). A last sheet, "Fixed since 3 Nov", lists every row the comparison cleared, check by check in
  the comparison's order and each in the other crawl's order by importance: the problem, the check's id, the address,
  how it moved ("Fixed", "No longer linked" for a check of what others refer to, "No longer found", "Not checked this
  time"), why it wasn't checked, what was found then and the other address then. None with a counts-only comparison.

### Rechecks (fix loop)

"Recheck these N pages": a problem's URLs fetched again **into the same crawl file**, the crawl analysed, and the result
kept as a record in the file ("rechecked 5 pages on 17 Nov: 4 fixed"). The design: [the fix
loop](v2-fix-loop.md#3-recheck-these-n-pages).

**`recheck` on each check** (`GET /api/checks`): what a recheck of it reads again, a list of `urls` (its URLs and what
it needs to judge them), `sitemaps` (every sitemap the crawl read), `robotsTxt` (the robots.txt of the origins it
names) and `siteVersions` (the probes of the site's other versions). **Empty for a check a recheck can't settle**:
`orphan-page` (only a full crawl can show that something now links to a page) and `no-https` (moving to HTTPS changes
every address). It is the catalog's, as `references` is: not stored, not in the fingerprint.

| `recheck` | Checks |
| --- | --- |
| `["urls"]` | The rest: 108 of the 122 the catalog has with the new checks' batches 1 to 3 |
| `["urls","sitemaps"]` | `sitemap-non-indexable`, `sitemap-url-other-host`, `not-in-sitemap` |
| `["sitemaps"]` | `sitemap-error`, `sitemap-missing` |
| `["robotsTxt"]` | `robots-txt-unreachable`, `robots-txt-unreachable-other-origin`, `robots-txt-missing` |
| `["siteVersions"]` | `duplicate-site-version` |
| `[]` | `orphan-page`, `no-https`, and of the new checks `canonical-unlinked`, `non-indexable-inlinks-only` and `deep-page` (each depends on every link to a page, which only a full crawl reads) |

**`POST /api/crawls/{id}/rechecks/plan`**: what a recheck would do, without doing it. Nothing is written.

```json
{"checkId":"broken-internal-links","group":null}
```

```json
{"checkId":"broken-internal-links","group":null,"urlList":false,"kinds":["urls"],
 "judged":5,"phase1":8,"phase2Max":0,"limit":1000,"capped":false,"notRechecked":0,
 "estimateMs":21000,"suggestFullCheck":false,"analysisFirst":false,
 "secrets":{"missingHeaders":[],"proxyCredentials":false},"refused":null}
```

- **The body** names a check (`checkId`), optionally one of its groups (`group`, as `/rows` takes it), **or** a list of
  URLs (`urlIds`, 1 to 1,000, for Advanced's selection; `checkId` then optional, to say which check's result to give).
  Both or neither of `checkId` and `urlIds`, or more than 1,000 ids, is `invalid-query`.
- **`judged`** is the URLs whose result the recheck gives (the check's flagged URLs, or the group's, or the list).
  **`phase1`** is what it fetches first: those URLs, with, for a `targets` check, every URL their issues name.
  **`phase2Max`** is, for a `sources` check, the most it fetches next: the pages referring to the URLs still failing
  after phase 1. Neither counts what pages now link to that the crawl doesn't have yet, fetched one step, nor robots.txt.
- **`capped`** is true when that would pass `limit` (1,000): the recheck then takes the most important first (the
  check's rows in their default order) and leaves `notRechecked` of them as the crawl found them.
- **`estimateMs`** comes from the crawl's own pace (its URLs crawled over its time spent crawling, no faster than the
  slow start's), plus robots.txt and the analysis after (the last analysis's `durationMs`). **`suggestFullCheck`** is
  true when the recheck would fetch more than half the crawl's URLs: a full crawl would take about as long and settle
  every check.
- **`analysisFirst`** is true when the crawl's analysis is out of date: the recheck analyses it first, so the "before"
  it records is today's.
- **`secrets`** is what Continue would ask for (`needs-secrets`'s fields): header values and proxy credentials the file
  doesn't keep.
- **`refused`** is null, or why it can't run now, as the problem `POST /rechecks` would give: `{ type, title }` with
  `type` one of `crawl-busy`, `site-busy`, `recheck-not-latest`, `recheck-unsupported`, `not-analyzed`,
  `not-analysable`. The UI shows `title` beside a disabled button.
- **As built (S2).**
  - **The body** takes `checkId`, `urlIds`, or both: with both, the list's URLs are judged by the check (as the
    engine's `RecheckAsk` has it). Neither, more than 1,000 ids (or none), and a `group` without its `checkId` or with
    `urlIds`, are `invalid-query`; an unknown check is `check-not-found`; an id the crawl has no URL for is
    `invalid-query` (`errors.urlIds`, the engine's words); a field the body doesn't have is `bad-request`.
  - The answer also gives **`countBefore`**, the check's count in the analysis as it stands (null without a check).
    `secrets` is what a recheck asked for with none would need.
  - **`refused`** is the first that applies, in this order: the crawl is live here (`crawl-busy`); what the engine finds
    in the file (`crawl-busy` when another program has it, `not-analysable` when interrupted, `not-analyzed`,
    `recheck-unsupported`), with the engine's titles ("This check hasn’t been analysed yet.", "Only checking the whole
    site again can show whether this is fixed."); another run of the site live here (`site-busy`: "A check of this site
    is running. Its results will show what’s fixed."), which comes before `recheck-not-latest` ("Recheck works on the
    site’s latest check.") as such a run is usually the newer crawl. The latest crawl is the newest of the site's that
    the library lists, in Home's order, a missing file apart.

**`POST /api/crawls/{id}/rechecks`** starts one: the plan's body, plus Continue's `extraHeaders`, `proxyUrl` and
`skipMissingHeaders`. It answers 202 with `{ recheckId, …plan }`, and the crawl's status goes `rechecking` (an
`analyzing` first when `analysisFirst`), then `analyzing`, then what it was. Errors: those under `refused`, and
`needs-secrets` as Continue gives it.

- **What it does**, in order: the analysis if out of date; then in one run of the crawl (the file locked, as Continue
  locks it) it stores the recheck and its "before" (the check's issues on its URLs, the check's count, the score),
  fetches phase 1, then phase 2 for what still fails, follows redirects to their end, fetches one step of the new URLs
  those pages link to (recording, not fetching, what they link to), and lets go the URLs nothing leads to any more;
  then it analyses the crawl, which settles the recheck.
- **The crawl is left as it was**: its status (completed, or stopped with its queue untouched), its options. Its stamp
  moves, so its analysis is out of date between the run and the analysis, as after Continue.
- **All or nothing.** The run is stored in one transaction as it ends. A run that fails anywhere (the file, the
  network, the run) is rolled back whole: the file is as it was, its analysis still today's, and it has no recheck. The
  analysis after it isn't run; the `recheck` event says `failed` (no result) and the crawl event's `error` says why ("The
  recheck failed: …"). A process that dies part-way leaves the file as it was too. A robots.txt or sitemap read again
  that gets no answer (the connection failed, or it timed out) leaves the crawl's as it was, and the URL it judges
  couldn't be checked (`answers.noAnswer`): never fixed.
- **Pace** is the crawl's own options, through the same per-host pacing as a crawl. A site that stops answering ends a
  recheck after a minute (a crawl waits ten).
- **Stop** is the crawl's: `POST /api/crawls/{id}/stop`. What was fetched stays, the analysis runs, and the recheck is
  `stopped`. Cancelling that analysis (`/analyze/cancel`) leaves the recheck unsettled until the next.
- **One at a time.** A crawl being rechecked is live (`crawl-busy` for continue, analyse, remove, delete, and another
  recheck). Rechecks and crawls of other sites run beside it; a crawl of the same site can't start while it runs, nor it
  while one does (`site-busy`).
- **As built (S2).**
  - The 202's `recheckId` is the next number in the file, which the run then takes (nothing else can while it is live
    here); the plan in it is as it stood when asked, before the analysis first. `secrets` lists the headers skipped.
    `needs-secrets` says "Rechecking this crawl needs …"; options the secrets make invalid are `invalid-options`.
  - **The flow**: `analyzing` (when `analysisFirst`) with its `analysis` events, then the plan is made again from
    today's analysis (a refusal now ends it, with the reason as the crawl event's `error`); `rechecking`; `analyzing`
    (the analysis that settles it); then the file's status, with a `library` event. A failed run stored nothing and
    isn't analysed: its end says why.
  - **Stop**: during the run, the crawl event says `stopping`, the run ends (`recheck` event `stopped`), and the analysis
    runs and settles it. During the analysis first, it cancels that analysis and no recheck is run (no `recheck`
    event). During the analysis after, `crawl-busy`, as for any analysis: `/analyze/cancel` cancels it. A sidecar shut
    down mid-run stops it and doesn't analyse: the next analysis settles it.
  - **Busy**: a crawl being rechecked is `crawl-busy` also for pause, resume and a plan's `refused`. A new crawl, or the
    continuing of a crawl, of a site being rechecked here is `site-busy` ("This site is being rechecked. Start the check
    once that’s done."); a running analysis doesn't make a site busy, as it fetches nothing.

**`GET /api/crawls/{id}/rechecks`**, the crawl's rechecks, newest first, and **`GET /api/crawls/{id}/rechecks/{n}`**,
one (`recheck-not-found`, 404, for an id the file doesn't have):

```json
{"rechecks":[{"id":1,"status":"completed","startedAt":"2026-11-17T14:02:12Z","endedAt":"2026-11-17T14:02:31Z",
              "settledAt":"2026-11-17T14:02:41Z","crawlingMs":19200,"checkId":"broken-internal-links","group":null,
              "urlList":false,"kinds":["urls"],"judged":5,"fetched":8,"countBefore":5,
              "result":{"fixed":4,"still":1,"notChecked":0,"notRechecked":0,"new":0,"scoreBefore":85,"scoreAfter":88},
              "answers":{"read":5,"rateLimited":0,"blocked":0,"robotsTxt":0,"noAnswer":0,"notAsked":0,"refused":0}}]}
```

- **`status`**: `running` (here), `completed`, `stopped`, `failed`, or `interrupted` (its run ended with the process; the
  analysis after it settled what it had fetched).
- **`result`** is null until the analysis after it settles it. It is kept as that analysis found it, and later
  analyses don't change it: it records what that recheck showed. `still` URLs are what the check flags now among those
  it judged; `notChecked` couldn't be judged (no answer, an error that isn't the check's own fault, blocked,
  rate-limited, refused by robots.txt, now forwarding elsewhere, or the check was skipped); `notRechecked` were past the
  cap or the stop; `new` were in scope, not flagged before and flagged now.
- **`crawlingMs`** is the recheck's own time. The crawl's `crawlingMs` leaves rechecks out.
- **`fetched`** is the requests the run sent the site, each counted as the site sees it: its pages (each try, each hop
  of a redirect), robots.txt and the sitemaps. A URL it never asked isn't one, nor a connection that couldn't be made.
- **`answers`** is how the site answered it for the URLs it judges, kept as its run ended, with or without a check:
  `read` (an answer of the site's own: a page, a redirect, an error code); refused again, by why: `rateLimited` (429),
  `blocked` (a firewall or bot check), `robotsTxt` (disallowed, or robots.txt unreadable), `noAnswer`; and `notAsked`
  (past the cap, not reached before it ended, its time for a section the site limits ran out first, or its host had
  stopped answering). They add up to `judged`; `refused` is the four refusals together. Null for a recheck by an engine
  before they were kept.

**`POST /api/crawls/{id}/rechecks/{n}/rows`**, the URLs of one recheck, paged as `/rows` is:

```json
{"outcomes":["still","notChecked"],"roles":["judged"],"offset":0,"limit":100}
```

```json
{"total":1,"offset":0,
 "rows":[{"urlId":31,"url":"https://www.example.com/sofas/","role":"judged","outcome":"still",
          "flaggedBefore":true,"detailBefore":"Links to 2 broken URLs","detailAfter":"Links to 1 broken URL",
          "statusCode":200,"reason":null}]}
```

- **`role`**: `judged` (a URL whose result the recheck gives), `named` (what a `targets` check's pages name), `referring`
  (a `sources` check's phase 2), `new` (fetched one step), `letGo` (no longer linked: let go).
- **`outcome`** for a judged URL: `fixed`, `still`, `notChecked`, `notRechecked`, or `new` (wasn't flagged, is now);
  null for the other roles. `reason` as on `/fixed`.
- **As built (S2).** A recheck's fields are the engine's `RecheckRecord`'s, its times in UTC, `group` as
  `{ detail, relatedUrlId }`. `recheck-not-found` also for a number that isn't one (`/rechecks/first`). The rows come
  judged first, then in crawl order; `outcomes` and `roles` take the names above (another is `invalid-query`,
  `errors["outcomes[0]"]`), `limit` 1 to 1,000 (100 when left out), `offset` 0 or more. **`reason` isn't built**: the
  engine's `RecheckRow` has none, so rows leave it out.

**Events.** The crawl's `crawl` events carry `rechecking` as its status. `progress` events are sent as for any run,
`phase` `rechecking`. A `recheck` event carries the recheck's own counts (`phase` 1 or 2, `done` of `total` fetches),
at most every 500 ms, and a last one with `result` once settled, which is the UI's cue to read the check again.

As built (S2): a `progress` event during the run has `status` `rechecking` (`stopping` once stopped) and `phase`
`rechecking`, and the crawl's own counts; it isn't added to the live history (the chart reads the run from the file once
it ends). `recheck` events are `{ crawlId, recheckId, status, phase, done, total, result }`: one as the run starts, then
with the progress at most every 500 ms (`running`), one as the run ends (the file's status: `completed`, `stopped` or
`failed`, `result` null), and one once settled (`result`), after the analysis's events and before the crawl's last
event. `phase` is null before the run's first progress. A stream that connects meanwhile gets the latest after its
`hello` and the crawls' progress, until the crawl is no longer live here.

**The library afterwards.** The item's `rechecks` (above); its counts, score and `changes` are the analysis's after
the recheck, as for any analysis. As built: `rechecks` comes from the summary cache (format 7: the count and the latest
recheck); `last.fixed` is null until the latest is settled. While the crawl is rechecked here, the item is its summary
from before, with `status` `rechecking` and `outOfDate` true.

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
  of date (`engineChanged`) until analysed again. (Since [pixel widths](v2-pixel-widths.md), VERSION 12, the two
  count characters, and `title_width` and `description_width` hold what the checks measure, in pixels.)
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

## Engine changes E3b made

Each has its own tests. The crawl's schema version is unchanged, and so is `AnalysisFreshness.VERSION` (6): nothing an
analysis already stored changed, so no analysis is out of date for it. A crawl analysed before E3b lacks the link
summary, which the links grid reads without (see [How the links are read](#how-the-links-are-read)), and an index the
inspector's group keys are read on, which they're read without too; both come when the crawl is analysed again.

A review of this work changed some of it, and the list below is as it now stands: how a running crawl's links are
read, how a page among the links of a few heavily linked targets is found, what a link summary must say to be
believed, its version, and robots.txt's `state`. What changed and why is under
[Engine changes from the E3b review](#engine-changes-from-the-e3b-review), with the reads it added to the table here.

- **The link summary.** `link_summary` (the links counted by target) and `link_blocks` (by block of 16,384 link ids),
  each by kind, position, flags, whether the link leads to its own page, has text, and sits on a nofollow page, built
  inside the analysis's transaction in one pass over the links, each counted by a function SQLite calls per row
  (`LinkSummaryBuilder`; grouping them in SQL took 18 s on the 100k crawl). `link_summary_state` keeps the crawl
  row's stamp they were counted under, with the number of links, their highest id, the size of a block (which a
  read takes from the file) and, since the review, the version of the counting: they stand until a run of the
  crawl moves the stamp on, so analysing a crawl again doesn't count them again, and an engine from before them
  analysing the file leaves them standing, and right. On the 100k benchmark crawl (5,054,060 links) the pass takes
  1.5 s of the first analysis, and the three tables with the new index 4.5 MB of a 487–494 MB file.
- **The links grid's reader**, `LinkGridReader` (`Query`, `Facets`, `Columns`) in `Storage/`: a link's own columns,
  and either end's as the URL grid's (`from.`, `to.`), compiled by the URL grid's own compiler into sets of URL ids.
  A running crawl is answered for one moment of it (`LinkMoment`), and a page among the links to a filter's targets is
  found on the links' index by target.
- **Sitemaps.** `SitemapListReader.Read`, and the URL grid narrowed to what a sitemap lists
  (`UrlGridReader.QuerySitemap`). Nothing was added to what a crawl stores: how a sitemap was found is worked out
  from the stored robots.txt and options.
- **robots.txt.** `RobotsReader` (`ReadOrigins`, `ReadFile`, `ReadBlocked`). Nothing was added to what a crawl stores:
  `RobotsTxtParser.Parse` can keep each line as it reads it, each rule knows its line, and `RobotsTxt.Match` gives the
  rule that decides a URL, which `IsAllowed` goes by: one matcher for the crawl and the screen. An origin's `state`
  (`RobotsState`) is read, missing, unreachable or unanswered, as the crawler's cache took its answer.
- **The catalog** says what each check's flagged URLs are (`IssueDefinition.Subject`, which `GET /api/checks` gives as
  `subject`): 68 checks flag pages only, 18 any URL.
- **The inspector's issues** carry their group's key (`UrlIssue.Group`, by `IssueGroupReader.GroupOf`, beside the code
  that makes the groups), read on a new index of `issue_targets` by URL (`ix_issue_targets_url`).
- **`si readbench`** times the links grid, the sitemaps and robots.txt, in five more classes of read
  ([Grid reads](../engine/README.md#grid-reads)).

Measured on the 100k benchmark crawl (112,589 URLs, 5,054,060 links) with `si readbench --gate --runs 20`, as CI
runs it: p95, each read on a fresh connection with nothing kept, on a 32-core Windows machine. A second run measured
the same or faster, but for a robots.txt file with what each line blocked (34 ms) and a page past half way of the
hyperlinks sorted by their target's inlinks (55 ms).

| Read | First page | Half way down |
| --- | --- | --- |
| Links in crawl order, either direction | 1.4 ms | 1.6 ms |
| Hyperlinks (4.4 million), links in the footer (300,000) | 2.2 ms | 3.2 ms |
| Nofollow links (2,327), images without alt text (3,227) | 18 ms, 9 ms | 17 ms, 10 ms |
| Links to 4xx URLs, to other sites, to URLs containing text (4,600 to 22,000) | 27–37 ms | 25–37 ms |
| Hyperlinks in the content to 404s (three filters) | 33 ms | 42 ms |
| Hyperlinks to non-indexable pages (54,500) | 47 ms | 46 ms |
| Not followed (the pages that say nofollow are read first) | 69 ms | 60 ms |
| Sorted by the target's URL, or its status | 45–52 ms | 48–50 ms |
| Hyperlinks sorted by the target's inlinks | 42 ms | 42 ms |
| Facets: every link, the hyperlinks, links to other sites | 49–51 ms | |
| Links from one page; links to one page (202,000) | 22 ms; 4 ms | ; 9 ms |
| The sitemaps, each with its URLs counted | 18 ms | |
| A sitemap's URLs (101,000) by URL; its non-indexable ones by inlinks | 11 ms; 29 ms | 21 ms; 31 ms |
| robots.txt: the origins; a file with what each line blocked; its blocked URLs | 19 ms; 30 ms; 31 ms | |

**Over budget: every read that passes over the links.** On the same crawl: a rare text filter 0.32 s, a common one
0.50 s, its facets 0.53 s, a search 0.82–0.87 s; and without the link summary (a crawl running, or not analysed since
it changed), a common filter 0.72–0.75 s, a sort by the target 0.86–1.23 s, the facets 0.71 s, a rare filter 0.21–0.23
s (the unfiltered grid, 23–36 ms: only the links are counted). They grow with the links, so about ten times that on a
million pages, which wasn't measured. The pass runs once per filter set (while a crawl runs, at most every 2 s), and
its pages after that cost a page. A table of the distinct link texts, counted, would take the text filters out of it;
that is not built.

## Engine changes from the E3b review

Each has a test that fails without it. The crawl's schema version and `AnalysisFreshness.VERSION` (6) are unchanged,
and so is the checks' fingerprint: no analysis is out of date for any of this. Measured on the 100k benchmark crawl
(112,589 URLs, 5,054,060 links) with `si readbench --gate --runs 20`, p95, each read on a fresh connection with
nothing kept, on a 32-core Windows machine that other work was sharing.

- **A running crawl's links are answered for one moment of it.** What the links grid counts is kept between calls,
  and while a crawl runs each call's snapshot is later than the last: the number of links, their highest id, a filter
  set's ids, its counts by target, each URL's facts for the facets and a sort's order each had a 2 s clock of their
  own, so one answer could be of several moments. `/links/facets` answered 500 (a count's targets ran past the end of
  older facts: 135 of 1,050 calls from five clients in the review), the newest-first page came back empty with every
  link matching, facets didn't add up to `matching`, and `matching` passed `total`. Now the links and URLs are counted
  with their highest ids in one statement, kept for 2 s (`LinkMoment`); whatever a call counts for that moment it
  counts within those ids, and keeps under the moment, so later snapshots add the same links, a crawl only ever adding
  links. A call whose own snapshot is older than the moment kept takes one of its own. A call that finds links its
  moment counted gone (the crawl fetched their page again) starts over on a moment of its own snapshot. Tested by four
  readers making every kind of links call, the facets, robots.txt and the sitemaps included, through one cache while a
  crawl of the synthetic site grows: some 11,000 calls, each answer checked against itself, none wrong.
- **The same in robots.txt and the sitemaps.** An origin's answer, its lines and its blocked URLs are read and kept
  as one (a file's lines could account for more URLs than its origin said it blocked). A sitemap's URL grid counts
  what the sitemap lists once when there's no filter (its `total` and `matching` were two counts, each with its
  clock). Under a filter, and in its rows, a sitemap's grid is the URL grid, which reads a running crawl's rows live
  beside counts kept for 2 s: that is unchanged.
- **A page of the links to a few heavily linked targets costs a page at any depth.** A filter that listed several
  targets (`to.id` in a few, or any filter on the target matching up to 1,000 URLs) walked the links to its page, for
  every page: for a menu's targets, with half the crawl's links between them, half way down took 0.7 to 3 s cold,
  and nothing was kept. A page is now found on the links' index by target, by counting between ids
  ([How the links are read](#how-the-links-are-read)), and nothing needs keeping:

  | Read | First page | Half way down |
  | --- | --- | --- |
  | Links to the twelve most linked pages (1.3 million) | 7.7 ms | 36 ms |
  | Links to URLs under `/c/` (2.0 million) | 14 ms | 74 ms |
  | Links to URLs with over 50,000 inlinks (2.5 million) | 30 ms | 73 ms |
  | Links to pages a click from the start (2.7 million); the hyperlinks among them | 9.3 ms, 5.6 ms | 59 ms, 53 ms |
  | Links to one page (202,000) | 6.8 ms | 12 ms |
  | Links to the site's own URLs (5.0 million, most links: kept as all but the rest) | 71 ms | 113 ms |
  | Links to pages four clicks from the start (1.4 million, to 74,000 targets: read through them once, kept) | 86 ms | 237 ms |
  | Hyperlinks to the site's own URLs (4.4 million, to 107,000 targets: kept as all but the rest) | 43 ms | 158 ms |

  The last two read an index entry for every matching link, or for every other one, once per filter set: they're
  inside the 300 ms budget at 100k and grow with the links, so `si readbench` holds them in a class of their own
  (`link-targets`), and the review's filters in the links grid's (`link-grid`, 200 ms on CI's runner), where the gate
  would have failed them. **Still over budget, and now said so:** a filter on the target together with one on a link's
  position or flags, deep among targets with many links (navigation links to pages a click from the start, 2.1
  million: 565 ms half way, once per filter set). The index by target doesn't hold a position, so the matching ids
  are read in one pass over the links, in order; `link-pass` times it.
- **A link summary is believed only as far as it can be checked.** `link_summary_state`'s count of links and their
  highest id were taken as stored, and the grid sizes what it keeps by them: two integers changed in an 18 MB file
  made it report a billion links and keep 128 MB per filter set, 1.9 GB in the review's run. They're now taken only
  when the highest id is the links' own and the count is no more than the ids from the lowest to the highest, nor than
  a file of this size can hold; otherwise the summary is passed over, the grid reads the links themselves, and the
  next analysis counts it again. The sets of URL ids a links read fills are a bit an id only while ids don't run far
  past the URLs there are.
- **The link summary has a version** (`CrawlSchema.LINK_SUMMARY_VERSION`, in `link_summary_state.version`), checked
  where the summary is trusted: a fix to how links are counted that keeps the tables' columns moves it on, and the
  next analysis counts again though the crawl hasn't changed. A summary without one (built by this branch before the
  review, which only development copies have) is passed over and counted again the same way.
- **robots.txt: `unanswered`.** The contract said an unreachable robots.txt refused everything on the site itself;
  the crawler refuses on no answer only for the start URL's origin, and gives any other origin that never answers no
  verdict. The code was right. An origin's `state` now says which: `unreachable` (its URLs were refused) or
  `unanswered` (the crawl went ahead), [as above](#robotstxt-e3b). The benchmark crawl's dead https twin read
  `isInternal`, `unreachable`, nothing blocked; it reads `unanswered`.
- **Tests the review's mutations got past.** A cursor that leads nowhere new now fails a test's walk where it made it
  loop for ever; a 429 from robots.txt, the origins' whole order, and the note of a walk that took long each have an
  assertion that fails without the code.
- **Not changed:** the inspector's `issues[].group` in the client's types (finding 6), which the inspector's own
  branch already has as optional.

## Engine changes E3c made

What the Overview and inspector slices found the engine lacked. Each has its own tests. Measured on the 100k benchmark
crawl (112,589 URLs, 5,054,060 links), p95 over 20 runs with `si readbench`, each read on a fresh connection with
nothing kept, on a 32-core Windows machine that other work was sharing.

- **`/stats` in one pass.** `CrawlStatsReader.Read` read the responses six times, a statement a number, and looked
  every page a sitemap led to up among the links: 0.96 s at p50 and 1.16 s at p95, and nothing was kept. It now reads
  the URLs once and the responses once (the rows counted as they're read, by outcome, status and reason at once), on
  one snapshot, through the grids' own way in (`GridReadContext`), so it's stopped, queued and kept as their reads
  are. With the link summary it reads no link. The answers are the same: the tests hold every number to a statement
  of its own, as the reader first had them, on a crawl analysed, continued since, never analysed, and holding values
  no engine writes.

  | Read | Before | Now |
  | --- | --- | --- |
  | `/stats`, with the link summary | 0.76 to 0.96 s (0.95 to 1.16 at p95) | 98 to 126 ms (108 to 174 at p95, over six runs of 20) |
  | `/stats`, without it: the links counted, each sitemap page looked up among them | the same read | 188 to 320 ms (209 to 366 at p95, over nine runs of 20) |
  | `/stats` again, the crawl unchanged | as the first | under 1 ms |

  With the link summary, which a crawl analysed and not continued since has, it's inside the 300 ms budget at p95,
  first read included. **Without it, it isn't** (a crawl running, never analysed or continued since): the review's
  three runs gave 327, 329 and 366 ms at p95 (318 to 320 at p50), and through the sidecar the first `/stats` of such
  a crawl took 246 to 329 ms; the six runs made here, on a machine other work was loading and on two crawl files,
  gave 189 to 322 and, after the review, 209 to 287. So the first read of a running or never-analysed 100k crawl's
  headline numbers takes about a third of a second, and a running crawl's is then kept for 2 s: a **known
  limitation**, not met by anything cheap that was found (the time is the links counted and each page a sitemap led
  to looked up among them; the link summary is what makes both free, and a running crawl has none). It grows with
  the URLs, so the first read of a million URLs would take about a second with the link summary and some three
  without, neither measured; the reads after it are kept. `si readbench` times all three and holds each to its own
  gate: `stats` to twice the desktop's worst, for CI's runner; `stats-pass` to 450 ms, the worst measured and a
  quarter, where the first gate of 600 would have let it double unseen (a runner slower than this desktop needs its
  own figure, which no CI run has yet given); and the kept read, `stats-kept`, to 10 ms, which it sat under the 350
  of `stats` before, so that numbers read all over again could never have failed it.
- **The probes' part of the counts.** A crawl's live figures count its own probes of the site's other versions, as its
  file and the URL grid have them, so a crawl of a site served over http only showed "No response: Network error × 1"
  from its first seconds: the https probe. The figures still count them, so each lands on its count in the grid and
  the sums a client makes hold; the progress event now says how much of each figure the probes are (`siteVersions`:
  `CrawlProgress.SiteVersions`, a second set of the writer's counters), and `/stats` says the same of a crawl at rest
  (`CrawlStats.SiteVersions`, two more tallies of its one pass), so the UI can show the site's own figures, live and
  done, and say the probes apart. A probe the site comes to lead to leaves the part, whether its answer is kept for
  the site or it's fetched again, and a continued crawl restores it; every end-to-end crawl checks the part of its
  last progress against SQL over the file, as it does the whole. **Not done:** taking the probes out of the figures
  themselves. That would move every count of a crawl by its probes (found, fetched, each class, the library's
  counts), so that none met the URL grid's rows without a filter on `source`: a change to what the user sees
  everywhere for a handful of URLs, and not one to make behind the UI's back.
- **Two counts of a crawl's URLs, each named for what it is.** `fetched` and the library's `fetchedUrlCount` count the
  URLs with a stored outcome, and that includes the URLs never asked because their host had stopped answering, which
  the URL grid has as not fetched: the API said a filter on `fetched` gave the count, and it didn't. The count that
  does is new: `crawled` on the progress event (`CrawlProgress.Crawled`) and `crawledUrlCount` on the library item
  (`CrawlFileSummary.CrawledUrlCount`, counted in the summary's one pass over the responses, so every crawl file has
  it; the sidecar's kept summaries are read again once, their format having moved on). `fetched` stays as it is, for
  the sums made with it ([why](#library-e1)).
- **Time spent crawling.** Nothing said how long a crawl had spent crawling: `elapsedMs` is one run's, pauses
  included, and the file kept only the crawl's start and end. The crawl writer now keeps it, a stretch of crawling at
  a time, in a new table, `run_time` ([the engine's README](../engine/README.md#time-spent-crawling)): created
  whenever the writer opens a file, as `notices` is, so the crawl's schema version is unchanged and either engine
  reads and continues either file. It's served as `crawlingMs` on the library item, the progress event and
  `/history`, and as `/history`'s `runs`, each with its stretches, so a chart can close the time between runs and the
  time paused. It costs a row at each start, pause, resume and end, and one small update every five seconds while a
  crawl runs, which bounds what a run cut short can lose. **Older files read as null**, and so does any crawl a run
  of which wasn't timed: a file from before the table that's continued here, or one an engine without it continued
  since (told by the crawl row's stamp, which every run moves and the last stretch keeps). The time is given whole or
  not at all. Tested on a clock the test moves: three runs with pauses and an hour between two, to the millisecond,
  live and from the file.
- **The inspector's totals of every kind of link, and the URL's length.** `/urls/inspect` counted a URL's hyperlinks
  only (`links`, from the URL summary), so the inspector's Inlinks and Outlinks tabs read `/urls/inlinks` and
  `/urls/outlinks` for their counts, on every tick of a running crawl. It now gives both totals (`linkTotals`,
  `UrlDetail.LinkTotals`), counted where the lists' own totals are and kept under the same key, so the tabs' counts
  and the lists can't differ; the inlinks come from the link summary where there is one, a few rows where the links'
  index has an entry for every link (the first page of the start URL's 202,222 inlinks took 5.2 ms and takes 1.8).
  Inspecting a URL takes 1.8 ms where it took 1.2. `urlLength` is the URL's length by the check's own measure
  (`UrlRules.MeasuredLength`, which the check now calls too).
- **What became of a link's other end.** A link row had the other URL's status, null alike for a URL never fetched
  and for one that robots.txt refused or that timed out. Each row now has its `outcome` (`LinkRow.Outcome`, read from
  the other end's response with or without the URL summary, as the URL grid's column reads it), and so does every URL
  the inspector names with a status: `redirects.final`, a test's `canonical` and `metaRefresh`, an hreflang's
  `target` (`UrlTarget.Outcome`). The tests hold every named URL of the synthetic site, and every link list, to what
  the URL grid shows of that URL.
- **A filter most links pass is read from the blocks, link ids missing or not.** Deep in the links to the site's own
  URLs, the matching ids were kept as every id but the others', which takes a crawl with no link id missing; on one
  with gaps (pages fetched again) they were read through every target instead, an index entry for each of 4.6
  million links. Measured cold on a copy of the benchmark crawl with one link id in eleven missing and 100,000 links
  stored again at the end (`make_gaps.py`, then analysed), that missed the budget. The links are now counted by
  block as the blocks' own count but for the others, read through the targets that fail
  ([How the links are read](#how-the-links-are-read)), and the page read from its blocks: nothing is read per
  matching link, nothing is kept but a few thousand counts, and gaps don't matter. It serves a filter on the target
  with one on a link's position or flags too, where most links of that position pass: the read the E3b review left
  over budget (navigation links to pages a click from the start, 565 ms half way down) is one of those.

  | Read, the page half way down | Before | Now |
  | --- | --- | --- |
  | Links to the site's own URLs, link ids missing (4.6 million) | 407 to 459 ms (472 to 664 at p95) | 97 to 132 ms (149 to 153) |
  | Hyperlinks to them, link ids missing (4.0 million) | 386 to 606 ms (430 to 871) | 99 to 111 ms (111 to 147) |
  | Links to the site's own URLs, no id missing (5.0 million) | 105 ms (157) | 126 ms (149) |
  | Hyperlinks to them, no id missing (4.4 million) | 249 ms (266 to 289) | 107 ms (113) |
  | Navigation links to pages a click from the start (2.1 million) | 565 ms | 16 ms (19) |

  **Still one pass:** a filter on the target with one on a link's position or flags that the fewer links of that
  position pass, or whose others are more than an eighth of the crawl's links, deep among targets with many links:
  the content links to pages four clicks from the start (1.3 million of 2.2 million content links, the others spread
  over every other page) take 455 ms half way down, once per filter set. What it would take is the links counted by
  block and by target at once, which is a row for nearly every link; reading the others, or the matches, through
  their targets reads as many rows as the pass does. It isn't cheap, and isn't done.
- **A table of the distinct link texts: measured, and not built.** The design: `link_texts` (an id for each distinct
  text) and the text's id in the key of `link_summary`, so a filter on a link's text is a set of text ids, counted
  and faceted from the summary as a filter on the target is; and, to place a page anywhere, the id in the key of
  `link_blocks` too. Measured on the benchmark crawl, which has 396,932 distinct link texts (some four a page), by a
  prototype of the tables and by the counting put into the analysis for the measurement, then taken out:

  | Cost | Measured |
  | --- | --- |
  | `link_texts` | 10.6 MB, 2.2% of the 478 MB file |
  | `link_summary` with the text's id | 215,758 rows become 512,454: 3.1 MB becomes 8.0 MB |
  | `link_blocks` with the text's id | 3,436 rows become 2,096,793: 0.06 MB becomes 30.9 MB |
  | The file, texts and by target | +15.5 MB, 3.2% (4.1% as the file grew in place) |
  | The file, by block too | +46.3 MB, 9.7% |
  | The analysis's time (texts and by target only; seven pairs of runs) | 11.1 to 14.8 s without, 14.9 to 20.9 s with: 2.2 to 3.3 s counting and 1.2 to 2.2 s storing, a quarter more at the very least |
  | The analysis's peak memory (an analysis of a crawl analysed before) | 155 MB becomes 242 MB |

  What it would buy: the count, the facets and the first page of a text filter in some 60 to 80 ms, from 320 to 530
  (the distinct texts are scanned in 9 to 18 ms and the summary's rows in 12), and every page of a rare one. **It
  doesn't pay**, by the measure set for it (the budget met for text `contains`, `equals`, "is empty" and search, for
  under a tenth more analysis time and a twentieth more file): the analysis takes a quarter to two fifths longer and
  half as much memory again; a page deep in a common text's links needs the ids by block, which alone are 6.5% of
  the file; and a search can't be answered from it at all, as it also matches the page a link is on, which no table
  by target or by text knows. "Is empty" was never slow (the summary holds whether a link has text). A real site's
  crawl would cost less (the garden crawl has 489 texts for 21,914 links: 0.3% of its file), so the cost is the
  benchmark site's, where every product's links carry its own name.

  **Known limitation, as measured:** on the 100k crawl a filter on a link's text takes 0.33 to 0.55 s for its first
  read and a search 0.85 s, once per filter set (a running crawl's, at most every 2 s), and their pages after that
  cost a page; on a million pages, about ten times that. (This belongs under the rebuild doc's Known limitations,
  which this work was told to leave alone.)

**After E3c**, on the same machine: the benchmark crawled its 100,000 pages in 36 to 44 s over four runs (36 to 45 s
before), peaked at 188 to 194 MB with no growth (188 to 196 MB), analysed in 18.1, 12.2, 11.8 and 11.4 s (10.6 to
14.8 s before; nothing in the analysis changed between the runs, and other work shares the machine), and found every
planted issue and nothing else each time. `si readbench --gate --runs 20` passed every class twice, the two new ones
included (`stats` 108 and 174 ms against 350, `stats-pass` 212 and 322 ms against 600), the links grid's slowest read
from the link summary at 98 and 90 ms (107 ms before). Every number added or changed here was checked against SQL
written for the check, through a running sidecar: on that crawl, on a copy of it with link ids missing, on the garden
crawl analysed again, and on a crawl run through the sidecar that was paused, stopped and continued (5 of its 728
URLs not checked, 1 a probe, 20.7 s spent crawling of 26.3 s on the clock). Nothing differed.

## Engine changes from the E3c review

A review of E3c found one defect of weight and six small ones; the Sitemaps screen asked for two counts beside them.
Each fix has a test that fails without it. Measured on the same 32-core Windows machine, shared with other work, so
every timing is a range over runs, and where two builds are compared their runs were interleaved.

- **A running crawl's write-ahead log stays small while its links are read.** A crawl's file is in WAL mode while it
  runs, and SQLite starts the log again from its beginning only at a moment when no reader is part-way through it.
  The links grid's reads of a running crawl are passes over the links, a new one for each filter set every 2 s, so
  with the Links screen open that moment never came, and the log grew by everything the crawl wrote: 106 to 160 MB on
  a 40,000-page crawl with one client reading (more than the file at the time), and without limit on a longer one.
  The writer now folds the log into the file between two batches once it passes 64 MB, and empties it
  (`CrawlDatabase.FoldLog`, a `TRUNCATE` checkpoint, from `CrawlWriter` as only `Storage/` writes).
  - **What a reader can observe: a read that waits. Never an error, and never another answer.** For a fold, the
    grids' new reads of that crawl wait while those in flight end by themselves, the log is folded, and the reads
    that waited go on (`CrawlReadGate.ExclusiveOnceIdle`). No read is stopped, and none is read again, so a running
    crawl's reads still answer for one moment exactly as they did. A read waits for the reads ahead of it, two
    seconds at most, and the fold itself (a quarter of a second at most for a reader the gate can't see; milliseconds
    otherwise). Measured over 40,000-page crawls with one and with six clients reading the links grid all the while,
    the slowest read took 745 to 1,103 ms where it took 644 to 1,103 ms with no fold at all: no longer, to within
    the spread of the runs.
  - **What the crawl pays.** The writer waits with the readers, and stores nothing meanwhile. Over those crawls it
    folded the log one to four times each, waiting 7 to 724 ms a time and at most 1.4 s in a crawl of 20 s; pages
    per second were 2,010 to 2,543 against 1,941 to 2,546 without the fold, the runs interleaved. With nobody reading
    there is no fold (the log restarts by itself, peaking at the largest batch: 22 MB at 40,000 pages, 38 MB at
    100,000), so the benchmark is as it was.
  - **The log's bound, honestly.** While folds succeed, the limit and the batch that took the log past it: 70 to
    87 MB on those crawls, against 92 to 160 MB before, at the thousands of pages a second a local test site answers.
    A real site answers tens of pages a second: a batch is a megabyte or two, the log takes minutes to reach
    64 MB, and a fold's wait is lost in the crawler's own queue (derived from the batch's size and the rate, not
    measured: no real site was crawled). **Where the bound doesn't hold:** a fold gives up when a read in flight
    outlasts two seconds (a link pass over a crawl of several hundred thousand pages) or another program is part-way
    through the file, and is tried again a second later, then 2, 4 … 32 s; with link passes that long following one
    another, most tries fail and the log grows as it did until one lands in a gap or the crawl closes. Nothing at
    that size was measured.
  - **Measured against stopping the reads.** The first design stopped the reads in flight and read them again, then
    left reads alone for twice as long as the longest it had stopped. Interleaved with this one, it threw work away
    (the slowest read 978 to 1,953 ms; with six clients 7,878 to 10,476 reads answered where this answers 11,856 to
    12,283) for a log no smaller (58 to 97 MB), and needed the rule about leaving reads alone. Dropped.
  - **The analysis** was measured too: its log is its two commits (the results, then the links' index on a first
    analysis), 26 MB on the 40,000-page crawl with nobody reading and 34 to 35 MB with one or six clients passing over
    the links throughout: a reader makes the two add up, and can't make it grow past what the analysis writes. Left
    as it is.
  - **Held by a gate.** A test crawls with two readers in relay, a read always part-way through the file and each
    lasting longer than a fold would wait by itself, and fails if the log passes its bound (without the fold it
    reached 207 MB beside a 16 MB file); the growing-crawl test runs again with a fold after every few pages and
    holds every answer to the finished file, not only to itself. `si bench --read-links --max-log-mb` does the same at
    the real limit, and CI runs it.
- **`/stats` without the link summary misses the budget, and says so.** [Above](#engine-changes-e3c-made): the gate
  of 600 hid it, and the kept read shared a gate it could never fail. `stats-pass` is held to 450 ms and `stats-kept`
  to 10 ms.
- **A kept `/stats` takes no read slot.** `/stats` queued for one of the grid reads' slots, one a core, shared by
  every crawl. With two slots (a two-core machine, emulated) and two clients asking for link passes of a running
  crawl, another crawl's kept numbers, a 2 ms read, took 30 ms at the median and up to 248 ms. The kept answer is now
  asked for first, without a slot (`CrawlStatsReader.ReadKept`: it opens the file and reads its stamp); numbers not
  yet read still queue as a pass does.
- **A crawl that hasn't crawled has spent no time.** A new crawl's first run can end having stored no stretch
  (paused before it began and then stopped, or its process died before its first batch). Continuing it took the
  empty table for a file from before the time was kept, and marked the crawl's time unknown for good. A file with no
  stretch and no response has been crawled by nobody, so nothing went untimed: its time is known, from zero.
- **Stretches in order; time asleep left out.** A stretch begins by the wall clock and lasts by a clock that only
  runs forward, so a wall clock set back between two (a time sync) had the later begin before the earlier ended,
  and a chart placing moments by them counted that time twice. They're read back in order, each beginning no earlier
  than the one before it ended, in one run or across two ([History](#history-e4)). And the forward clock is
  `Stopwatch`'s, which on Windows counts the time a machine spends asleep (`QueryPerformanceCounter` "including the
  time when the machine was in a sleep state such as standby, hibernate, or connected standby") and on Linux and
  macOS doesn't (`CLOCK_MONOTONIC`, `CLOCK_UPTIME_RAW`; dotnet/runtime#77945): a gap of over five minutes between two
  of the session's twice-a-second readings is now left out of the stretch on any of them. A sleep shorter than that
  still counts on Windows, and a stretch a sleep was left out of spans more wall time than its length.
- **Tests the review's mutations got past.** Each of the eight has one that fails without the code: a link row's
  `notChecked` outcome; a running crawl's item taking the live time and the live count of URLs crawled (with a URL
  not checked in the crawl, as only then do the two counts differ); a live first run's `/history` carrying its
  runs; the longest time heard of a stretch standing; the links grid reading the others through their targets only
  up to an eighth of the links, either side of it by one target's links, with the way each side was read asserted;
  the blocks' counts having to add up, on a file whose counts were changed; and a moment's total being the moment's
  when the read that took it never counted.
- **What the contract said loosely.** Which filter each figure of the progress event lands on
  ([Progress](#progress-e4): a table, checked figure by figure against a crawl with a block page and URLs not
  checked); that `/history`'s `crawlingMs` is the library item's at rest, and behind it by up to five seconds while a
  crawl runs; the `stats-pass` figures.
- **Two more counts for each sitemap** ([Sitemaps](#sitemaps-e3b)): `robotsDisallowed` and `redirectingOrBroken`, by
  the URL grid's `outcome`, each the rows of the sitemap's grid under its filter (tested on the test site, which now
  lists a URL its robots.txt refuses, with the URL summary and without, on six pages given every other end, and
  checked on the 100k benchmark crawl and three other files against SQL written for the check). With the URL
  summary a listed URL's response is looked up only where the summary's status says it may count, so the list of a
  100,000-URL sitemap takes 29 to 30 ms where it took 18 to 22 ms (31 to 34 ms at p95, from 20 to 29): 10 ms more, for
  the two sums over every listed URL, and inside its gate of 200.
- **robots.txt: `unanswered` with blocked URLs** ([robots.txt](#robotstxt-e3b)): said, with why. No code: the count
  already backs the UI's sentence.

**After the review**, on the same machine: the benchmark crawled its 100,000 pages in 45.2 s (2,237 pages a second),
peaked at 198 MB with no growth, analysed in 15.3 s, and found every planted issue and nothing else: 32,410 issues on
110,264 URLs, two issues and one URL more than before, which are the listed URL robots.txt refuses. Its log peaked at
36 MB while it crawled, so nothing was folded. With the links grid read all the while (`--read-links`) it crawled
them in 46.4 s (2,180 a second) with the log at 89 MB at most and the slowest of 9,419 reads at 1.5 s, and 40,000
pages in 16.5 s with the log at 76 MB; with the fold taken out, those 40,000 pages took the log to 580 MB, and the
benchmark failed on it. `si readbench --gate --runs 20` passed every class on the new 100k crawl: `stats` 107 ms
against 350, `stats-kept` 0.8 against 10, `stats-pass` 207 against 450, `sitemaps` 45 against 200, the links grid's
slowest read from the link summary 135 against 200, a pass over the links 1,068 against 2,500.

## Engine changes the score made

What Simple mode needs of the engine that isn't export ([the Simple design](v2-ui-simple.md#what-the-engine-needs)):
the score, the catalog's new fields, one new check, and a status code's name in details. Every addition to the API is
a new field; nothing is taken away. Each has its own tests.

- **The score, worked out by one class and stored by the analysis.** `ScoreCalculator` works a site's score out to
  the design's version 1 ([The score](#the-score)). The analysis does it as it commits, in `IssueWriter`, the only
  writer: from the counts it has just stored, the pages it read and the URLs crawled as it read them, with the file's
  lock keeping any crawl out, so they are all of one moment. It is stored in new columns of the `analysis` row
  (`score_version`, `score`, `score_partial`, and the whole of it as `score_json`), and every reader serves what was
  stored: the API, `si score`, and the report when it comes. **Why stored and not worked out when read:** a score
  worked out at read time would set an analysis's counts against the crawl's totals as they are now, and a crawl
  continued since has more URLs than its analysis saw. A score this engine can't read (a later engine's, a crafted
  file's) is no score, never an error.
  - Tests: every row of the design's "Worked examples", "What each mix gives" and "It can't say these lies"; 288
    cases scored by the design's reference code (`app/design/simple/build/lib/score.mjs`) and then by the engine,
    line for line (`ScoreReference/cases.json`, which `generate.mjs` beside it writes: the design's named sites, the
    mockups', every band edge, and 120 random sites); the catalog's site-wide and any-URL checks against the
    reference's own lists; and on 2,000 random sites, that the score stays within 0 to 100, never passes 89 with an
    error, is 100 with none but notices, and never rises when a problem is added or spreads.
  - With the ceiling taken out of the code, 7 tests fail; with the site-wide rule taken out, 7; with the stored URL
    count read from the crawl as it is now instead, the test that continues a stopped crawl after its analysis fails;
    with the count not stored, 3.
  - **The garden crawl scores 85, Good**, from the engine, on a copy analysed by it: 2 errors (a few, some), 4
    warnings (many, some, some, some), 13 notices; fixing the first five would make it 98. As the design has it.
- **The URLs crawled, as the analysis saw them.** A URL check's count is a share of the URLs crawled, and nothing
  kept that total with the analysis: the library's `crawledUrlCount` is the crawl's as it is now. The analysis now
  stores it (`crawled_urls`: `AnalysisInfo.CrawledUrlCount`), counted from the URL table it read, by the library's
  own definition (every URL with an outcome, less those never asked because their host had stopped answering), and
  serves it as the analysis's `crawledUrlCount`, beside `pagesAnalyzed`. Every end-to-end crawl of the synthetic site
  holds it to the library's count, the dead twin's unchecked URLs included.
- **"Fix first" is the engine's, and a fault of the whole site leads its severity.** `FixFirstOrder` is the one
  order, served as the analysis's `fixFirst`; the score's lines follow it, and `ifFixed` takes its first five errors
  and warnings away. Hidden checks are in it: hiding is the app's. It is the app's order (`rankChecksToFix`: the most
  severe, then the most URLs flagged, then catalog order) with one step added: within a severity, `siteWide` checks
  come first, in catalog order among themselves ([The score](#the-score) has the steps). By count alone a site-wide
  error, one issue, sorted after every error that flagged more, and on a site with five of those it was outside the
  first five and outside `ifFixed`, where the design's Summary has it lead.
  - The design's reference code has the same order (`fixFirstOrder` and `firstToFix` in `score.mjs`, which the
    mockups' data now uses), and the replayed cases take their order and `ifFixed` from it: two more named cases
    hold a site-wide error among six errors that flagged more, and three site-wide errors with two site-wide
    warnings.
  - Tests: a site-wide error on 1 URL leads six errors on 200 to 700, and is in `ifFixed`; site-wide checks lead in
    catalog order whatever their counts; with every check of the catalog counted, every site-wide check is before
    every other of its severity.
  - **The app's own sort no longer agrees where a check is site-wide.** `rankChecksToFix`
    (`app/src/screens/issues/checkList.ts`) still sorts by severity, count and catalog, so its list puts a site-wide
    fault by its count until a screen reads `fixFirst` instead. No screen code is changed here.
- **The score in the library, at no cost.** The headline is three columns of the row the summary already reads, so a
  list reads no more than it did, and it is kept in the summary cache (`summaries.json`, whose format moves to 4: a
  summary kept before the score is read again, once). Measured back to back on copies of the two development
  crawls, 40 reads each on a fresh connection, three runs, on a machine other work was loading: the garden crawl's
  summary 3.4 to 3.6 ms before and 3.1 to 3.6 ms now (median); the 100k crawl's 41.7 to 44.2 ms before and 40.9 to
  42.7 ms now.
- **Which checks are a fault of the whole site, and which are reported once.** `siteWide` and `reportedOnce` on a
  check, from the catalog (`IssueDefinition.SiteWide`, `ReportedOnce`), neither stored with an analysis nor part of
  the checks' fingerprint. Each was held against its rule:

  | Check | Its rule reports | `siteWide` | `reportedOnce` |
  | --- | --- | --- | --- |
  | `robots-txt-unreachable` | One issue on the home page; the detail lists the main origins failing | yes | yes |
  | `no-https` | One issue on the home page, when it is served over plain HTTP | yes | yes |
  | `site-hidden-from-search` | One issue on the home page | yes | yes |
  | `duplicate-site-version` | One issue for each other version of the site that answers (three at most), on that version's URL, not on the home page | yes | yes |
  | `robots-txt-unreachable-other-origin` | One issue on the home page; the detail lists the origins. It is a fault of another origin of the site, not of the main one: the design counts it as site-wide, at a warning's 4 points | yes | yes |
  | `robots-txt-missing` | One issue on the home page; the detail lists the origins | no | yes |
  | `sitemap-error` | One issue on the home page however many sitemaps failed: the detail lists the first three and counts the rest | no | yes |
  | `sitemap-missing` | One issue on the home page | no | yes |
  | `security-headers-missing` | One issue on the home page: only its response is checked | no | yes |

  A test puts each of the nine through its own rule on a site with the fault several times over, and every
  end-to-end crawl checks none is reported more than once (three times for the duplicate site). A change to
  `siteWide`, or to a check's `subject`, changes what a score is worked out from, so it comes with a new score
  version: a test holds the catalog to the reference's lists.
- **A new check, `site-hidden-from-search`** (Error, Indexability, site-wide): **more than half of the pages that
  stand for themselves are noindex.** ([The catalog's fixes](#engine-changes-the-catalogs-fixes-made) added a second
  condition, that the home page is noindex itself, and what follows is the check as the score built it.) Without it
  a site whose every page is noindex scored 100, noindex being a notice. Its detail gives the numbers: "212 of 240 pages are noindex" ("1 of 1 page is noindex").
  - **The first number is the noindex pages**, counted as `noindex-page` counts them: it is that check's count.
  - **The second is the pages the page pass audits (`pagesAnalyzed`), less those canonicalised to another URL.** A
    page whose canonical names another URL is a variant of that page, not a page that stands for itself: it counts
    **neither as hidden nor in the total**. So a shop whose crawl reads more filtered and sorted listings than main
    pages isn't flagged while its main pages can be indexed, and is when most of those are noindex. (It counted
    canonicalised pages as hidden at first, which flagged such a shop. No engine shipped with that, so the analysis
    version stays 7.)
  - **Which canonicals make a variant**: where the analysis has the page as canonicalised (it is flagged by
    `canonicalised-page`), so not one that leads back to the page or to a broken URL; and **not one that names the
    same page on the site's other version** (www or not, http or https): that page stands for itself, at its other
    address, and the other version answering is `duplicate-site-version`'s to report. Those pages stay in the total.
  - **A noindex page whose canonical names another URL is noindex**, and in the total: the noindex wins, as it does
    for the page's indexability (and `noindex-with-canonical` flags the contradiction). So a site most of whose own
    pages are deliberately noindex still fires, as a private site does: its `why` says to check the noindex pages and
    ignore it when they are meant.
  - **More than half** is a strict majority: exactly half doesn't count, and a site of one page is hidden when that
    page is. **It never fires with no page to judge**: a site whose every page read is a variant (a mirror that names
    another site throughout) reports nothing, never "0 of 0".
  - **A page** that moves on with an instant meta refresh is read and in the total, and never noindex: search engines
    treat it as a redirect, and `noindex-page` doesn't flag it either.
  - **A crawl stopped early** is judged on the pages it read, as every page check is; its score is partial.
  - **URLs robots.txt blocked** weren't read, so they are neither pages nor hidden. A site blocked whole reads no
    page, and has no score.
  - **Where**: the home page, which is a page, so its `subject` is `page`. (As first built it was reported on the
    start URL when no home page was read, and its `subject` was `url`:
    [the catalog's fixes](#engine-changes-the-catalogs-fixes-made) changed both.)
  - Tests, one an edge: a majority reported once on the home page with a variant left out of the total; exactly
    half; one page; **40 variants with indexable targets beside 3 pages: nothing**; **3 of 5 own pages noindex
    beside 40 variants: reported, "3 of 5 pages are noindex"**; nothing but variants; a noindex page that names
    another URL; blocked URLs; the same page on each other version of the site against another host or path; a
    canonical that leads back or nowhere; an instant refresh; no home page; a start URL that redirects; thousands
    grouped.

  It is planted on the synthetic site by a new option (`SyntheticSiteOptions.HiddenFromSearch`, `si testsite
  --hidden-from-search`: every product noindex), crawled end to end with the whole issues table compared, and is
  never flagged on the site without it, the benchmark's included. The catalog has **87 checks**, so the checks'
  fingerprint has changed and every earlier analysis is out of date (`engineChanged`).
- **Plain words.** A check's, a category's and a skip reason's, in files of their own beside the catalog
  (`IssuePlainTexts`, by check id; `PlainWords`), so words change without touching the catalog or a rule. **All 87
  checks, the 17 categories and the 17 skip reasons have theirs** (15 reasons then; [the catalog's fixes](#engine-changes-the-catalogs-fixes-made) added two).
  - **One data file, and a generator.** The words are written in
    `engine/tests/ShoutingIguana.Engine.Tests/Analysis/PlainWords/plain-words.json`, in the API's own shapes
    (`plainTitle`, `plainWhat` and `plainFix` as lists, `fixer`, `plainPassed`, `plainOneFix`, `plainLooksFor`; a
    category by its API id; a skip reason by its code, with `plainText` and `plainAction`), and `generate.mjs` beside
    it writes the two C# files from it. A text change is an edit to the data file and
    `node engine/tests/ShoutingIguana.Engine.Tests/Analysis/PlainWords/generate.mjs` from the repo root (`--check`
    writes nothing and says whether the files are current). It refuses a check, category or reason the engine
    doesn't have, one the data file leaves out, a field it doesn't know, and a brace that isn't a limit's. Nothing
    needed mapping: the data file's `fixer` values and field shapes were the API's already.
  - **Tests.** The compiled words are the data file's, field for field, so neither is changed without the other.
    Every check, category and skip reason has its words (the test that was written and skipped is on). Every text
    keeps to the template: a title of 60 characters with no number in it; 2 to 4 sentences of 30 words at most and
    70 in all, the first of 120 characters once its limits are filled; 1 to 4 steps of 25 words, a sentence each;
    good news of 60 characters; a noun phrase of 60. Sentences average 20 words or fewer. No text holds a word the
    rules ban (a technical term only where the check's words first give it in quotation marks, never in a title).
    The ten checks the design writes out are served as the document writes them, word for word, read from the
    document itself, with their category, severity and fixer; the six Notices it names say when they can wait; its
    examples of a category's title and a skip reason's words are the ones served.
  - **All 87 passed the template's tests as they stood.** One test of the engine's own didn't, and was changed: it
    held a category's title to three words, a shape the design never gave, and "Extras in search results" is four.
    It allows four. One rule of the design has no test, as nothing mechanical can judge it: "each step starts with a
    verb". 68 of the 263 steps start "If …,", as eleven steps of the design's own ten do.
  - **`site-hidden-from-search`'s words** were written when the check counted canonicals too. Two sentences were
    brought to the rule as it is, by taking words out and adding none: sentence 1 lost ", or to show another page
    instead" ("More than half of your pages tell search engines to keep them out of search results."), and step 3
    lost ", and to point each page's main address (its "canonical" link) at itself" ("Ask them to remove the
    "noindex" setting."). The rest of the data file is its writer's, byte for byte.
  - **`plainLooksFor`**: 12 checks have one, the ones whose title is a sentence about the site
    (`robots-txt-unreachable`: "A rules file that search engines can't read").
  - **Limits follow the settings.** A text names a threshold as `{limit:name}`, the name an analysis setting's as its
    JSON has it; `PlainLimits.Fill` puts the value there with its unit. All 12 numeric thresholds have a name
    (`titleMinPixels`, `titleMaxPixels`, `descriptionMinPixels`, `descriptionMaxPixels`, `minWordCount`,
    `duplicateMinWordCount`, `nearDuplicateMaxBits`, `imageAltMaxLength`, `largeImageBytes`, `largePageBytes`,
    `slowResponseMs`, `maxUrlLength`), a test holds the names to the settings, and a name no setting has is an error
    that a test looks for in every plain text the engine has. Ten checks name a limit, eleven times in all (ten of
    the twelve names; no text names `duplicateMinWordCount` or `nearDuplicateMaxBits`), and no text types a number a
    setting holds. A number that is a fixed rule stays a number: Google's 2 MB and 10 steps, the sitemap protocol's
    50,000.
  - Plain words are no part of the checks' fingerprint, nor of what an analysis stores.
- **A status code's name in details.** Where a detail said "HTTP 404" it says "404 Not Found": the standard reason
  phrase (RFC 9110), in every rule that writes a status (the response-code, redirect, robots.txt, sitemap and
  site-version rules, and the shared wording for where a link, canonical or image ends). A code with no standard
  name (a CDN's 520, LinkedIn's 999) stays "HTTP 520". What the crawl stored with a status in it ("Rate limited
  (HTTP 429)", a sitemap's "HTTP 500", "robots.txt unreachable: HTTP 503") is named the same way where a detail
  quotes it; **the crawl file keeps its own words**, and so do the sitemaps and robots.txt lists, alerts and notes,
  which read them. Details are stored and grouped on, so the analysis version is 7 (once, for all of this), and an
  analysis by an earlier engine is out of date until it is run again.
- **On the command line.** `si score <crawl>` prints the breakdown, and `si issues` leads with the score.

Nothing here changed what a crawl stores, so the crawl's schema version is as it was, and either engine reads and
continues either file. An analysis of the 100k development crawl took 12.9 s before and 12.9 s now (a second analysis
of each copy, back to back; the first of each, with other work loading the machine, 23.3 s and 20.7 s).

**After the score**, on the same machine, with other agents' builds and benchmarks loading it throughout: the
benchmark, run once, crawled its 100,000 pages in 79 s (36 to 58 s on a quieter machine; its pace fell to a fifth for
stretches, and nothing in the crawl changed), peaked at 198 MB with no growth (−15 MB over the crawl), analysed in
22.8 s, and found every planted issue and nothing else, `site-hidden-from-search` among the checks that must stay
silent. `si readbench --gate` on that crawl passed every class on its second run (`text` 75 ms against 190, `stats`
108 ms against 350, `stats-pass` 207 ms against 600, the links grid slowest read from the link summary 120 ms against
200). On its first, with the machine busier, one read alone was over its gate: a regular expression over every title,
219 ms against 190. Repeated by itself three times it took 94 to 102 ms, as it did for the engine before the score on
a 100k crawl of its own (71 to 124 ms): the load, not the change, and no read was changed here.

**After the three changes that followed** (a site-wide fault leads its severity in the order to fix;
`site-hidden-from-search` counts noindex only; every check has its plain words), the benchmark, run once on the same
machine, crawled its 100,000 pages in 56.8 s (1,779 pages/s), peaked at 193 MB with no growth (+1 MB over the crawl),
analysed in 19.6 s, and found every planted issue and nothing else. The analysis's working set peaked at 331 MB, against 253 to 327 MB in
the runs before it: one run, not repeated, and nothing these changes add is of that size (three counters in a rule,
and 87 checks' words held once). `si readbench --gate` wasn't run again: no read changed.

## Engine changes E5 made

Export, the workbook and the report. Each has its own tests. Measured on a copy of the 100k benchmark crawl (112,577
URLs, 5,053,909 links, an 879 MB file) and of the garden crawl (6,286 URLs found, 4,521 checked, 21,914 links), each
analysed by this engine, with `si export` on a 32-core Windows machine that other work was sharing; times are two
or three runs, memory is the process's peak working set, with its private bytes beside it. The report, the workbook
and the four whole-grid exports were measured again once the score and the plain words were merged in (the 100k
crawl then scores 0 with 75 checks finding something and 2 that couldn't run; the garden crawl 85 with 19 and 4).

- **An export is the grid's own read, a page at a time** (`Export/CrawlExporter`, `Storage/GridExportReader`). It asks
  the grid's reader for a page, writes it, and reads on by the grid's own cursor: the same query, compiled and run by
  the same code, so a file can't differ from the grid. Each page is a read of its own (`GridReadContext.Read`), on a
  snapshot let go of before the next, so an export's memory is a page of rows (5,000 URLs, 1,000 links), a writer here
  never waits on one for longer than on a grid's read, and nothing is held open for the export's length. What the
  grid's reads count is kept between the pages, in the sidecar's `ReadCache` or one of the export's own: without it,
  an export of the links by their target took over five minutes (each page ordering the targets again), and takes 39 s
  with it.
- **What it costs.**

  | Export | Rows | Time | Peak memory (private) | File |
  | --- | ---: | --- | --- | ---: |
  | 100k: URLs, 9 columns, CSV | 112,577 | 0.7 to 1.7 s | 70 to 75 MB (42 to 45) | 10.9 MB |
  | 100k: URLs, 9 columns, Excel | 112,577 | 0.8 to 1.1 s | 87 to 92 MB (58 to 63) | 8.3 MB |
  | 100k: URLs sorted by inlinks (no index), Excel | 112,471 | 1.4 s | 111 MB (57) | 4.0 MB |
  | 100k: every link, CSV | 5,053,909 | 18.7 to 32.8 s | 75 to 76 MB (41 to 42) | 402 MB |
  | 100k: every link, Excel (5 sheets) | 5,053,909 | 20.4 to 37.1 s | 91 to 93 MB (55 to 58) | 159 MB |
  | 100k: every link by its target's URL, CSV | 5,053,909 | 39.2 s | 88 MB (52) | 402 MB |
  | 100k: links whose text contains a word, CSV | 2,200,717 | 22.0 s | 243 MB (48) | 184 MB |
  | 100k: the workbook (76 sheets) | 32,402 | 0.9 to 1.1 s | 97 to 98 MB (44 to 47) | 1.3 MB |
  | 100k: the report | | 0.5 s | 97 MB (34) | 210 KB |
  | 100k: the report's summary cut | | 0.4 to 0.5 s | 97 MB (32 to 34) | 38 KB |
  | Garden: URLs, CSV and Excel | 6,286 | 0.3 s | 41 and 44 MB | 0.72 and 0.37 MB |
  | Garden: every link, CSV and Excel | 21,914 | 0.4 s | 58 MB | 3.6 and 0.63 MB |
  | Garden: the workbook (20 sheets) | 933 | 0.3 to 0.4 s | 46 to 55 MB | 61 KB |
  | Garden: the report | | 0.3 s | 42 to 45 MB | 94 KB |
  | Garden: the report's summary cut | | 0.3 s | 45 to 46 MB | 18 KB |

  **Memory is flat**: five million links are exported in the memory 112,577 URLs are, and the working set sampled
  through the links export read 70, 63, 64 and 64 MB at a quarter, half, three quarters and the end (71, 63, 63 and
  63 when measured again; the links' times vary with what else the machine is doing, their memory doesn't). The
  243 MB is the one pass over the links a text filter makes, as it makes it for the grid: the crawl file's own pages,
  mapped and read once, not memory the export holds (its private bytes stay at 48 MB).
- **Excel files are written by the engine, with no package** (`Export/Xlsx/`, some 550 lines over the
  framework's `ZipArchive`): a row at a time, straight into the ZIP. The alternatives hold the workbook (ClosedXML, in
  the old app) or write text inline in each cell, which the format allows and not every reader of it reads
  well. This writes what Excel itself writes: **shared strings**, every row and cell named by its
  place, numbers as numbers. The shared strings are a part of their own, after the sheets, so their items go to a
  file beside the workbook as the sheets are written and are copied in at the end; a text seen lately (the last
  65,536, in 4 MB) names the item it has, which takes the 5 million links' 19.9 million text cells to 3.45 million
  items. `DocumentFormat.OpenXml` 3.1.1 (MIT, published 2024-10-16) is in **the tests only**: it reads the workbooks
  back, and its validator holds every one to the format's schema.
- **Past a sheet's 1,048,576 rows** a table goes on to further sheets under the same header, and the export's result
  says how many; a table has 16 sheets (16,777,200 rows), and one with more is refused before anything is written
  (`export-too-large`), so no workbook is ever short of its rows. A million pages' 50 million links are for CSV.
- **Text a spreadsheet would run stays text** (`Export/SpreadsheetText`, `Tables/CsvTableWriter`, `XlsxCell.Text`),
  and a number in a numeric column is never touched: the guard is on text cells only. The tests write a site's worst
  through both files and through a whole export (titles and URLs changed in a copy of a crawl): formulas, DDE, a
  leading tab and carriage return, quotes, commas, line breaks, control characters, emoji, a surrogate with no
  partner, and text shaped like the workbook format's own escapes.
- **The file is whole or not there** (`Export/ExportTarget`): written beside its target, flushed, and moved into
  place. What can go wrong with it is told apart: the path, a place that can't be written to, a full disk, and a file
  open in another program, which is found before anything is read. The tests cancel and fail exports part-way (a
  disk that fills up is a stream that fails as Windows and Linux fail one; a real full disk wasn't used) and hold
  that the folder is as it was.
- **A crawl that's running** is exported as it stood when the export began: its highest URL and link ids are the
  bounds, and rows past them are left out. The tests export a crawl while a writer adds to it between every two
  pages, and hold the file to the grid as it stood at the start; a real crawl is exported over and over as it runs,
  and each file is held to the finished crawl's links up to its last.
- **What an export does to a running crawl's log.** Nothing lasting: between any two pages it has no read open, so
  the log can be folded into the file and emptied. The test folds it (`PRAGMA wal_checkpoint(TRUNCATE)`) after every
  page of an export and holds that it's emptied each time, having shown first that one reader part-way through the
  file is enough to keep it from being. Each page goes through the read gate as a grid's read does, so it also waits
  out a writer that wants the file for a moment; the fold a crawl's writer makes for itself when its log grows (on
  `ui/e3c`, not on this branch) holds new reads while those in flight end, which an export's pages are.
- **A crawl that changes under an export ends it** (`export-crawl-changed`): each page says what state of the crawl
  it read (the crawl row's stamp, the last analysis, how the grid reads), and a page that finds another isn't read.
- **The report** (`Reporting/`): `ReportReader` reads what it says through the engine's readers (the analysis's
  counts and skipped checks, the crawl's headline numbers and options, and each problem's first rows and largest
  group), `ReportDocument` writes the HTML, and the workbook's summary sheet is written from the same model. The
  score, the order problems are numbered in and each check's words reach both through one seam (`ReportSources`).
  - Its size follows the checks that found something, never the site's size. Printed by headless Chromium (Edge,
    and the app's own Playwright build: the same counts from both), which uses the paper it is told as the report
    names none:

    | Report | File | A4 | Letter |
    | --- | ---: | ---: | ---: |
    | Garden (19 checks found something), full | 94 KB | 12 pages | 13 |
    | Garden, summary cut | 18 KB | 1 | 2 |
    | 100k (75 checks found something), full | 210 KB | 33 | 34 |
    | 100k, summary cut (its score has 45 lines) | 38 KB | 3 | 3 |
    | The design's `report.html`, printed the same way | 82 KB | 11 | 13 |

  - **Against the design's 11 pages and 84 KB**, the garden report is a page and 12 KB more, with the same look page
    for page (each of its 12 pages was set beside the design's). The page breaks are the same through page 8; the
    catalog's plain words are about 5% longer than the mock-up's (24,859 characters of text against 23,717), and
    "About this check", which is kept whole, needs 41% of a page where 35% was left. Of the 12 KB, 10.4 KB is link
    markup (the design links nothing; here every http or https address is a link), 0.6 KB the larger sheet, the rest
    words. On Letter, 18 mm shorter, the first page's last two lines (the note under the score) go over to a page
    of their own, in the design's file as in the engine's: that is why the summary cut is two pages there.
  - Two things the pages showed were put right. The score's table said its sum at the foot of every page it ran to,
    under part of its lines (three times on the 100k crawl's): in print the sum is now the table's last row. A score
    of 0 drew a dot at the top of its ring: an arc of no length with round ends.
  - It takes 0.3 s for the garden crawl and 0.5 s for the 100k crawl, most of it the crawl's headline numbers
    (`/stats`'s one pass), which the sidecar keeps.
- **Each column has its words** (`Storage/ColumnWords`): a title, a unit, and its values' words, on `UrlColumnInfo`
  and on the two column lists. They are the words the app's screens already use, the unmerged Links and
  Sitemaps screens among them, wherever those had one; an export's header and a grid's can't differ once the grids
  read them from here.
- **Opened in Excel** (Microsoft 365, 16.0 build 20527, by automation, each file read-only): the garden and 100k URL
  and link workbooks, both workbooks of everything (20 and 76 sheets), one check's sheet, a workbook just past one
  sheet (1,100,000 rows: "Links" with 1,048,576 rows and "Links (2)" with the rest) and the CSVs all opened with no
  repair, the header frozen, bold and filtering; a workbook broken on purpose (a cell naming a string that isn't
  there) was refused, so the check can tell. Hostile titles came through as text in both files, no cell a formula:
  the workbook's formula-like ones with Excel's own text mark, the CSV's with their apostrophe. Opened again once the
  score was merged in (both workbooks of everything, a check's sheet, the garden's URLs and links, the hostile
  crawl's workbook, URLs and CSV): no repair; the summary sheet's two dates are dates and its counts numbers, a
  check's share a percentage, the score a number with its word beside it; the 23 hostile titles text as before. The
  5-million-row workbook (159 MB) was not opened in Excel, for the memory that takes; it was read part by part
  instead (every part reads back, 5 sheets, 5,053,909 rows). **Not opened in LibreOffice or Numbers**: neither is on
  this machine.

## Engine changes from the E5 review

Sixteen findings, each fixed with a test that was seen to fail without its fix (by writing the test first, or by
putting the old code back for a run). No endpoint, field, event or command was added or renamed; what an existing one
says changed where noted.

- **A CSV field is never longer than Excel reads as one cell** (`Tables/CsvTableWriter.MAX_FIELD_CHARS`, 32,758).
  Excel 16 ends the row where a field passes 32,758 characters and starts a new row with the rest, in column A,
  unguarded: a URL of 40,000 characters ending `=1+1`, exported as CSV, opened as 6,306 rows for 6,286 with `A24` a
  live formula. Every text field is now cut to 32,758 characters (the apostrophe that guards a formula among them,
  never half an emoji) and counted in `truncatedCells`; `si issues --csv` cuts and counts the same. Opened in Excel
  again, the URL grid's CSV, a check's CSV and `si issues --csv` of that crawl read as one row a URL with no formula.
- **The report** says where a problem is explained by its section ("under the same number in “What to fix”"), not
  "from page 2", which was false whenever page 1 ran over (the 100k crawl's "What to fix" starts on page 4); counts
  what the site's own robots.txt kept out, as the analysis did, not every refused address of every website (the
  garden crawl's "2 addresses the site's rules file keeps search engines out of" were two other sites'); says that a
  crawl that went on since its results is out of date whoever asks, and leaves out the figures of the crawl as it now
  stands ("4,064 of 3,113"); wraps a site's unbroken text in its cell (a 300-character detail widened its table to
  2,589 px on a 680 px page); says "1 address, which wasn't read"; and a check of addresses' "One fix" counts
  addresses, which "all lead to" the address they share. **The report's style sheet changed** (one rule), so the hash
  its policy names did too; it is still the one hash every report of a build carries.
- **An export** never says it has written more rows than it expects (a running crawl's count can be older than its
  rows: "30,770 of 30,757"); isn't ended by a pause, and no longer says it is; refuses a file name no file can have
  (`d.txt:x.csv`) as an invalid path; and tells an `IExportJournal` of its part files, which **the sidecar keeps in
  its state folder** (`exports-in-progress.json`) and clears at its next start, deleting what a killed run left noted.
- **`si export`** types a filter's value by its column (`--filter title:eq:404` is text), and refuses an option of
  another kind, by the list the API refuses a field by (`Export/ExportFields`).
- **Tests the review found missing**: a query with filters, a search and a sort together, row for row with the grid's
  read, on both grids; every column's title and value word written out in the test; an export ended by a crawl
  really continued and really finishing, and one not ended by a crawl paused and resumed; the too-large refusal and
  its promise of no file; in the sidecar, `export-crawl-changed` (an analysis between two pages), `export-disk-full`,
  `export-too-large`, progress at most every 250 ms, the last 64 ended exports, and shutdown cancelling an export whose
  file is open and part-written; the report's examples by order.
- **The workbook's sheet name** in this document read "3 Large images that slow pag…"; the engine writes "3 Large
  images that slow pages…", as its test always said. The document was wrong.

## Engine changes the catalog's fixes made

After the plain words' review, which read every check's rule against its texts and found one check too eager, three
rules that didn't do what their texts said, and texts that said other than their rules do. Each change has a test
that fails without it, and a planted case on the synthetic site where the rule needs one. The catalog still has
**87 checks**, with the same ids, categories and severities, so the checks' fingerprint is as it was.

- **`site-hidden-from-search` needs the home page to be noindex too.** It fired on the share of noindex pages alone,
  so a shop that keeps its many filtered listings out of search would have been told its whole site was hidden, at
  60 points of the score. It now reports when both hold:
  1. **The home page is noindex itself.** The home page is the page the crawl's start URL ends on, after its
     redirects (an instant meta refresh is one), and noindex is as `noindex-page` counts it.
  2. **More than half of the pages that stand for themselves are noindex**, as before: the noindex pages, of the
     pages the analysis read less the canonicalised ones; a strict majority; a noindex page is never a variant.

  With no home page read (the start URL failed or was refused, and a sitemap led to the pages) nothing says the site
  hides itself, and nothing is reported. So what it flags is always a page, and **its `subject` is `page`** (it was
  `url`): that changes no score, as a site-wide check is the whole site whatever its subject.

  **A crawl that didn't start at the site's root skips the check**, with a new reason, **`start-not-root`**: when the
  start URL has a path other than `/` (a section, `/shop/`; a page, `/home`), its first page isn't known to be the
  site's home page, and a crawl of a section whose pages are noindex (an account area) would have been called a site
  hidden from search. Not passed, and not fired: like every skipped check it has no count, and the analysis is
  partial. A start URL at the root is still the root with a query (`/?lang=en`) and where it redirects (to `/en/`,
  to the www host): the home page is the page it ends on, as above. Its detail is
  unchanged ("212 of 240 pages are noindex"). The synthetic site plants both states: `HiddenFromSearch` (`si testsite
  --hidden-from-search`) makes the home page and every product noindex, and must be reported; `NoindexProducts`
  makes every product noindex and leaves the home page alone, and must not be.
- **`charset-missing` passes a page that starts with a byte-order mark.** A UTF-8 or UTF-16 mark declares the
  encoding, and beats a meta tag and the header; the check looked at those two only, as its own text admitted. The
  decoded body has no mark left, so the fetch records which one the bytes began with and the page's facts keep it
  (`PageFacts.ByteOrderMark`: `utf-8`, `utf-16be` or `utf-16le`), beside the declared charset, from the one parse.
  **A crawl already on disk has no such fact**, and nothing left to work it out from: a marked page in one is still
  reported until the page is crawled again, and the check's `why` says so. The fact is one more optional member of a
  page's stored facts, so the crawl's schema version is as it was, and an earlier engine reads such a file. **The
  inspector's `charset`** is the meta tag's, or with no meta tag the mark's encoding, so a page that passes on its
  mark alone says which it is. The check also reads the header as the fetch does: **a `Content-Type` whose charset is
  empty (`text/html; charset=`) declares nothing**, and is reported, where the rule had looked only for "charset=".
- **The four checks that robots.txt decides are skipped where a crawl that ignored it couldn't judge them.** Such a
  crawl asks for every URL, so none is refused, and it reads robots.txt only where sitemap discovery asks: the start
  URL's origin, with sitemaps on. Each check read as passed on what was never read. Each is skipped exactly then,
  with a new reason, **`robots-txt-ignored`**:

  | Crawl | `blocked-by-robots` | `robots-txt-unreachable` | `robots-txt-missing` | `robots-txt-unreachable-other-origin` |
  | --- | --- | --- | --- | --- |
  | Obeying robots.txt | Runs | Runs | Runs, every origin of the site | Runs |
  | Ignoring it, sitemaps on | Skipped | Runs: the start origin's was read | Runs, for the start origin alone | Skipped |
  | Ignoring it, sitemaps off | Skipped | Skipped | Skipped | Skipped |

  The middle row's two checks are skipped as well when no robots.txt of a main origin (the start URL's, or the home
  page's) is on file, as on a crawl stopped before sitemap discovery asked. One note on the analysis says which
  ("robots.txt was ignored in this crawl and never read, so the URLs it blocks, and whether it is missing or
  unavailable, weren't looked for."), each check's reason has its own text, and the reason's plain words are true of
  all four. Like every skipped check they have no count (`total` is null, never 0) and store no rows. There are
  **17 skip reasons**, these two new.
- **`canonical-to-redirect` names where the redirect ends.** The fix is to name the redirect's final URL, and
  neither the detail nor the related URL gave it. The detail does now: "The canonical URL redirects (301 Moved
  Permanently) to https://example.com/page" after one redirect, "The canonical URL redirects (302 Found) and, after
  3 redirects, ends at https://example.com/page" after more. **Only a URL that answered is called the end.** One the
  crawl never asked for (links to other sites off, a limit, a stopped crawl) or was kept out of may redirect again,
  so the detail says where the redirects lead and why no more is known: "… to https://partner.example/landing, which
  wasn't checked", "… and, after 2 redirects, leads to https://partner.example/landing, which wasn't checked", "… to
  https://example.com/private/page, which is blocked by robots.txt". "The canonical URL redirects back to this page"
  is as it was, and the related URL is still the canonical itself, which the check groups by. The lookup of that URL
  is one statement compiled once (1.2 µs a lookup in the page pass, 2.1 µs before).
- **Texts that said other than their rules do.** Titles, descriptions, a fix and a why:

  | Check | It said | It says | Because the rule |
  | --- | --- | --- | --- |
  | `robots-txt-missing` | Title "No robots.txt" | "robots.txt missing or refused" | Fires on a firewall's 401 or 403 and on too many redirects, where a file may well exist, and for another origin of the site alone |
  | `image-missing-alt` | Title "Images without alt text" | "Images without an alt attribute" | Passes an empty `alt=""`, which has no text |
  | `orphan-page` | Title "Orphan page" | "Orphan page in a sitemap" | Tests only the pages a sitemap lists |
  | `redirect-broken` | Title "Redirect leads to a broken URL" | "Broken redirect" | Also reports a redirect with no usable Location, which leads to no URL |
  | `blocked-by-robots` | Description began "robots.txt disallows this internal URL" | "The crawler didn't fetch this internal URL, because robots.txt disallows it, or because robots.txt itself couldn't be read…", and that it isn't checked on a crawl told to ignore robots.txt | Also flags URLs refused because robots.txt couldn't be read |
  | `hreflang-missing-self` | Fix: "written exactly as its canonical URL" | "written exactly as the page is served" | Compares each entry with the page's URL as crawled |
  | `empty-anchor-text` | Why: "such as a logo or social media icons" | "such as a logo, a basket or a search icon" | Tests only links to the site's own URLs |
  | `near-duplicate-content` | Description named its threshold "the default" twice | Once, and that pages under the duplicate check's minimum word count aren't compared | Has one threshold, and skips short pages |
  | `charset-missing` | Why: "though this check doesn't look for one" | A marked page isn't reported; a crawl made before the mark was recorded still reports it | Looks for the mark now |
  | `canonical-to-redirect` | Description: "The detail says when the redirect leads back to this page" | "The detail gives the URL the redirects end at, or says that they lead back to this page" | Names the final URL now |
  | `site-hidden-from-search` | Description: more than half of the pages are noindex; a page whose canonical names another URL counts neither way; reported on the start URL without a home page | The home page is noindex, and so are more than half; a canonicalised page is one that isn't noindex; reported on the home page | Needs the home page; counts a noindex page with a canonical as noindex; reports nowhere else |

  The descriptions also say when a check isn't made: `site-hidden-from-search` on a crawl that didn't start at the
  site's root; `robots-txt-unreachable`, `robots-txt-unreachable-other-origin` and `robots-txt-missing` on a crawl
  told to ignore robots.txt, each as the table above has it. `canonical-to-redirect`'s description and fix say that
  a URL the crawl didn't check, or was refused at, may not be where the redirects end.

  A title is no part of the checks' fingerprint, and no text is stored with an analysis. The plain words that had to
  follow did: `site-hidden-from-search` (sentence 1 names the home page, and its good news and what it looks for
  no longer speak of "most of its pages" alone), and `canonical-to-redirect` (step 1 reads the address off the list
  again, which says when it couldn't be checked).
- **The analysis version is 8.** What an analysis stores differs for the first, third and fourth of these, so an
  analysis by an earlier engine is out of date (`engineChanged`) until it is run again. Nothing shipped at 8 before
  the review's fixes, which are part of it.

## Engine changes E6 made

Fast search and the site's icon, and what the Links, Sitemaps and robots.txt screens and their reviews found the
engine couldn't answer. Every addition to the API is a new field or a new call; nothing was renamed or taken away.
Each has its own tests, the reads are among those made while a crawl grows, and for each behaviour a test was seen
to fail with the behaviour taken out.

- **Search: no index, and what was measured before deciding.** The brief took the URL grid's `search` for a scan
  that missed its budget. It doesn't: on the 100k crawl a first page takes 26 to 50 ms at p95 for one letter, two,
  forty characters or text nothing matches, and it stayed the scan it is, with four `si readbench` cases added to
  hold it there. For the palette, an FTS5 index with a trigram tokenizer was built on a copy of that crawl and
  timed. With its content it adds 44 to 48 MB to a 479 MB file (9 to 10%) and takes 2.0 s to build, 17% of an
  11.8 s analysis, or 2.7 s spread over the crawl; contentless, 9.4 MB (2%) and 1.4 s. It answers rare text of three
  characters or more in 0.1 to 2 ms, but a common word in 28 ms (81 ms with each match's text, which ranking needs),
  and one or two characters not at all, which is where typing starts. So nothing is stored: `UrlSearchReader` reads
  a crawl's URLs and titles into memory once ([Search](#search-e6)), and a keystroke is a pass over that text. It
  costs the file nothing, the crawl nothing and the analysis nothing; it costs the sidecar about 106 bytes a URL
  (11.4 MB at 100k, measured twice), for two crawls at most. A keystroke takes 1.3 to 4.5 ms at p95 whatever is
  typed; the first search of a crawl, which reads the text, 55 to 89 ms (49 to 78 at p50, five runs), over the palette's 50, which the empty
  search as the palette opens keeps off the first keystroke. A crawl made by any engine is searched the same way.
  **Not done: letting the kept text go when a crawl sits idle.** A crawl of a million URLs holds about 110 MB in
  the sidecar until two other crawls are searched or it restarts. Nor was a search of a million URLs measured: the
  pass grows with the text, so some 20 to 30 ms a keystroke is to be expected, not known.
- **A pass over every link, split over connections** ([How the links are read](#how-the-links-are-read) has the
  table). `LinkGridReader`'s pass over the links is divided by link id over the read's own connection and up to
  seven more that the host lends (`ReadHelpers`; in the sidecar, read slots free at that moment, never the last:
  `ReadSlots`), for a crawl that stands still and has 500,000 links or more. Timed in turn with the engine from
  before E6: on one connection a pass takes what it took, on four a quarter, on eight a sixth, which puts the first
  page of a text filter, a search, a pattern and a filter on the source's depth inside the 300 ms budget at p50
  (113 to 228 ms where one connection takes 665 to 1,592), and near it at p95 (86 to 489 ms at worst over three
  rounds on a loaded machine). Nothing is stored for it; a running crawl's pass is never split, so its log goes
  unfolded no longer than it did; with no slot free a pass is as it was. It was built with three helpers and is
  shipped with seven, on that measurement. The race it had is closed: two passes, or a pass and a read, asking at
  the same moment could each count on the slot the other took, so a pass now looks again once it has a slot and
  gives it back if it was the last.
- **The site's icon** ([Favicon](#favicon-e6)). The parser keeps a page's `<link rel="icon">` elements as facts that
  aren't stored (`PageFacts.Icons`); the first page a run parses starts `FaviconFetcher`, a job beside the crawl that
  asks for one to four candidates by the crawl's own rules of politeness and has `CrawlWriter` keep the first that
  is an image by its bytes. **The crawl file gains a table**, `favicon`, created whenever the writer opens a file, as
  `notices` and `run_time` are. `FaviconReader` tells the bytes to be an image again as it reads, so neither a site
  nor a crafted file can have anything but a raster image served. The synthetic site serves `/favicon.ico`, which no
  page links, so the benchmark crawl keeps an icon and has no URL more than it had.
- **From a links view to its URLs** ([Link ends](#link-ends-e6)). `LinkGridReader.Ends` lists a view's targets, or
  the pages its links are on; `UrlQuery.LinkEnds` narrows the URL grids to them, to the list's own count. The
  targets cost what the view's count costs (20 to 58 ms from the link summary); the pages take a pass (475 to 740 ms
  on one connection, 148 to 157 at p50 on eight), as nothing stored counts links by the page they're on. **Not
  done:** a view of a few thousand links still takes that pass for its pages, where reading just those links would
  do; it is correct, and slower than it need be.
- **robots.txt and sitemaps.** The origins are searched and filtered, with the counts a filter bar shows
  (`RobotsOriginQuery`); `/robots/file` says whether the crawl obeyed robots.txt; `/robots/blocked` answers for one
  URL; the inspector's robots.txt test names the origin and the line that refused the URL, and the inspector names
  the sitemaps that list it; and a sitemap says how many sitemaps its own file lists. **The crawl file gains a
  column** for that last, `sitemaps.listed_sitemaps`, added by the writer as it opens a file, null for every sitemap
  an earlier engine read. They are small reads, each in `si readbench` under its screen's class.
- **A check's count has a true name**: `urls` beside `pages` on each group ([Issue groups](#issue-groups-e3a)).
- **The analysis's `inlinks` and the stored links.** The reviews found the two apart by one or two for about a tenth
  of URLs. On the 100k crawl they differ for exactly 24 of 112,590 URLs (the tenth was a sample's: 24 of 200), and
  for none on the garden crawl. The cause: the analysis counts the hyperlinks on the pages it audits, and it doesn't
  audit Cloudflare's own pages (a URL with `/cdn-cgi/` in it), which the crawl fetches, parses and stores the links
  of. The 24 are the 25 hyperlinks of `/cdn-cgi/l/email-protection`: a menu's targets, and the home page twice. The
  stored links are right, so the fix is in what's read: `linkTotals.hyperlinkInlinks`, which is the links grid's
  count ([the inspector](#the-inspector-e3a)). **Left for the owner:** whether the analysis should count links on
  pages it doesn't audit. It would move `inlinks`, `linkingPages` and the followed and nofollow counts for those
  URLs, and could clear an orphan result for a page linked only from such a page.
- **The analysis's limits** ([Analysis settings](#analysis-settings-e6)) are read, saved, checked and reset through
  the sidecar, kept in its state folder, and each described by `AnalysisLimits`: a test holds that every setting the
  engine has is described there. Nothing in the engine changed for it: an analysis already stored the limits it ran
  with, and one that ran with others was already out of date.
- **The crawl file.** One table and one column more, both added as a file is opened for writing; the schema version
  is as it was, and either engine reads and continues either file. The benchmark's file is the size it was: 477 and
  480 MB in two runs, 479 before E6, one run differing from the next by more than the 160 bytes of its icon.
- **In the app's API module**: typed calls and types for all of it (`api.search`, `api.favicon`, `api.linkEnds`,
  the analysis settings, `linkEnds` on a grid query), each new field optional on the answers that gained it. No
  screen uses them yet.

**Checked against the files themselves.** A script of its own started the Release sidecar on a fresh 100k benchmark
crawl and on a copy of the garden crawl (6,286 URLs, analysed by this engine), asked every endpoint E6 added or
changed, and held each number to SQL written for it over the same file: `/search` (13 texts each: `matching`,
`total`, every result's URL, title and status, that it holds the text, and the ranks in order) and the grid's
`search`; the origins, what each blocked, searched and filtered every way (`matching`, `counts`, the rows);
`/robots/file`'s `respected` and that its lines account for every blocked URL; `/robots/blocked` for one URL, and the
inspector's robots.txt test for 40 refused URLs (the origin, the reason, and a line that is the file's own and whose
rule matches the URL's path); `listedIn` and `hyperlinkInlinks` for 214 and 165 URLs, and the links grid's and
`/urls/inlinks`' counts for the most linked; every sitemap's `listedSitemaps`; 641 issue groups' `urls`; eleven
links views' ends from either end, a page in the middle and the last, and the URL grid and its facets narrowed to
them, alone and beside a filter; the icon's bytes and type, and `hasFavicon`; the analysis's limits, saved and reset,
turning both crawls' analyses out of date and back. **3,563 checks, none wrong.** The first run had 62 "wrong", every
one the script's own model of a check's groups (it grouped a `detail` check by detail alone, where the engine groups
by the detail and the other URL, and by the fault for the three checks that have one); corrected, they agree. On the
benchmark crawl the analysis's `inlinks` differ from the stored hyperlinks for 15 of the 214 URLs sampled, the menu's
targets, as above; on the garden crawl for none. Eight passes over the 5 million links at once, through the sidecar
with every one of 32 slots free, took 385 ms in all (104 to 384 ms each), with `/health` answered in 10 to 14 ms
throughout.

**The checks on the last commit's code.** The Release build has no warning; the engine's tests (1,229), the
sidecar's (200) and the CLI's (21) pass; the app's `typecheck`, `lint`, `format:check` and its 1,059 tests pass.
`si bench --pages 100000` completed and found every planted issue and nothing else: 38.7 s, 2,613 pages/s, a peak of
192 MB and +1 MB over the crawl, a 480 MB file, analysed in 11.3 s, its icon kept and no URL for it. `si bench
--pages 40000 --read-links --max-log-mb 128`, as CI runs it: every planted issue, the links read 4,397 times while the
crawl ran, the log at 82 MB at most. `si readbench --runs 20 --gate` passed every class, 205 reads: `search` 4.5 ms
against 25, `search-first` 73 against 200, `link-pass-split` 392 against 1,500, `link-pass` 1,806 against 2,500, the
links grid from the link summary 96 against 200, `robots` 59 against 200, `inspector` 2.2 against 200, `stats-pass`
325 against 450.

Not verified: anything on a crawl of a million URLs; any of the timings on a quiet machine (other agents' builds and
benchmarks loaded this one throughout, so each was repeated, and the passes were timed in turn with the engine from
before); the `link-pass-split` gate on CI's runner, as nothing was pushed.

## Engine changes from the E6 review

The review found no defect of high severity, one of medium and five of low; every number it checked against SQL of
its own agreed. All six are fixed, each with a test seen failing without its fix.

- **The read gate holds the split passes to being split** (medium). The `link-pass-split` gate (1,500 ms) passed a
  pass that had silently stopped splitting, as every one of them takes less than that on one connection too. With
  `--gate`, on a crawl of 500,000 links or more, each split case must now have had every pass reported split over
  more than one connection, and on 4 cores or more take at most 60% of its one-connection twin's time at p50, in the
  same run. On the 100k crawl with splitting switched off the gate failed on all 17 split cases; with it on it passed.
- **An icon is kept only when its structure holds to the end of its image data** (low): see [Favicon](#favicon-e6).
  The review's three proofs (a PNG header alone, a PNG header before a page, an ICO whose 16 px entry holds a PNG of
  60,000 px a side) and eighteen more hollow images are refused; a crawl file's icon is checked the same way as it's
  read.
- **A split pass's parts never use the read's own context** (low, latent). Each part sized its tally through the
  read's context from its own thread; it only ever read values already held, but the context isn't made to be
  shared. The sizes are now read first, on the read's thread, and a test hook says of any use from another thread.
- **A helper connection that can't be opened costs the pass its split, not the read** (low): the pass runs on the
  read's own connection, as for a file that changed under it. A read stopped meanwhile is still stopped.
- **Search's `start` rank is what the doc says** (low): never the scheme (`http` started every URL), never inside the
  query string or the fragment (text after a slash there did), and in a title after any character that isn't a
  letter or a digit, whatever the text begins with. The doc's figures are the review's and this branch's: about 106
  bytes a URL kept (11.4 MB at 100k, measured twice), and a first search of 55 to 89 ms at p95 over five runs.
- **The app's types and the mock** (low): `linkEnds` is on `UrlGridQuery` (the URL grids'), not on the links grid's
  query, which refuses it; `invalid-settings` is said to be keyed by a limit's name, `settings` or `limits[n]`; and
  the mock transport answers every E6 call (search, a links view's ends and `linkEnds`, the analysis's limits,
  robots.txt's origins searched and filtered, the synthetic crawl's icon), each a pure answer with tests of its own.
- **A status in the crawl's own words carries its name** (asked for after the review, for the Simple design's
  "404 Not Found" in both modes). The sentences the crawl stores with a status in them are now made by the helper an
  issue's detail uses (`HttpStatusNames.Describe`): a sitemap's `error` ("404 Not Found"), robots.txt's when it
  couldn't be read (the `robots-unreachable` alert's `reason` "503 Service Unavailable", and each URL it refused
  "robots.txt unreachable: 503 Service Unavailable", so `/robots/blocked`'s `error`), a robots.txt redirect that
  can't be followed ("301 Moved Permanently redirects to no http or https URL"), and a firewall's block reason
  ("Rate limited (429 Too Many Requests)"). A status with no standard name stays as it was: "HTTP 520". **Only crawls
  made from now on read the new way**: what an earlier engine stored keeps its words ("HTTP 404") in the file and
  in these answers, which serve what was stored; an issue's detail names it all the same, as before. One answer is
  worded when read, and so changes for every crawl: `/robots/blocked`'s `error` for a URL refused by an origin whose
  robots.txt couldn't be read, where the crawl stored no reason with the URL (an earlier engine's), now
  "503 Service Unavailable" from the origin's stored status.

## Engine changes E7 made

What the hand-over UI found the engine couldn't export, and one count the library and the analysis told apart.
Every addition is a new export kind, a new field or a new option; nothing was renamed or taken away. Each has its own
tests, and each was seen to fail without what it tests.

- **The list of sitemaps** (`sitemaps`) **and robots.txt's origins** (`robotsOrigins`) are export kinds, with the
  same job, events, problems and formats as every other ([Export](#export-e5)), and `si export --kind sitemaps` and
  `--kind robotsOrigins` (with `--search` and `--blocked true|false`). Each writes its screen's columns in its cells'
  words (`Export/ListExport`; the words in `Export/SitemapListWords`, `RobotsOriginWords` and `FailureWords`, the
  app's own `sitemapWords.ts`, `sitemapTree.ts`, `sitemapLinks.ts`, `robotsWords.ts` and `failure.ts` said again in
  C#), in the order its read lists them. Each list is small (a crawl reads at most 1,000 sitemaps; it asks some
  thousands of origins at most), so it is read whole, from one snapshot of the file
  (`SitemapListReader.ReadWithMoment`, `RobotsReader.ReadEveryOrigin`, both the screens' own reads): a running
  crawl's file is of one moment of it, and says so (`crawlWasRunning`); no crawl can change under it, so neither ends
  with `export-crawl-changed`. `robotsOrigins`' `search` is refused past 1,000 characters (`invalid-query` on
  `search`), before anything is written, as `/robots` refuses it. The tests read each file back, as CSV and as a
  workbook, row for row against the screen's read (the origins searched and filtered five ways), hold every word to
  a literal, and export a running crawl's list ("Not read yet" where a stopped crawl says "Never read").
- **`linkEnds` on a URL export**: `urls` takes the URL grid's `linkEnds` as the grid's read takes it, beside its own
  filters, search and sort (`Export/ExportFields` lists it as the `urls` kind's alone; another kind is refused
  `invalid-export` on `linkEnds`, and what the grid would refuse is `invalid-query` under `linkEnds.`). `si export
  --kind urls` takes `--link-ends from|to`, `--link-filter` (any number) and `--link-search`. The engine's export
  already read a `UrlQuery` whole, `LinkEnds` included: what was missing was the field in the API's body, its owner in
  `ExportFields` and the options. The tests export the grid narrowed to both ends, alone and beside a filter, a search
  and a sort, row for row against the grid's read, through the engine, the sidecar and `si export`.
- **The library's two counts of pages.** The seeded synthetic crawl's `pageCount` was 156 against the analysis's
  `pagesAnalyzed` of 155, the 100k crawl's 101,107 against 101,106, the garden crawl's 265 and 265. The page between
  them is `/cdn-cgi/l/email-protection`, the synthetic site's Cloudflare endpoint: an internal HTML page the crawler
  parsed, so every crawl count of pages has it (`CrawlSchema.PAGE`: `si stats`, a running crawl's `htmlPages`, the
  history, the benchmark's expectation `HtmlPages`), and one the analysis doesn't audit (`AnalysisReader.SITE_PAGE`,
  the synthetic site's `AnalysedPages`). Both counts are right by their definitions, so neither changed: **the library
  item gains `pagesAnalyzed`**, the last analysis's own (the score's `covers.pages`), null before the first analysis,
  for a screen to show beside the score; `pageCount` and the `page` subject now say which they count. Taking the
  endpoint out of the crawl's count instead would have moved the live progress, the history and the benchmark's
  expectations for a page that is the crawler's to have read. The test (`GridServingTests`) failed with the field
  missing, on a crawl of the synthetic site reading 176 against 175.
- **In the app**: the two lists and a URL view narrowed to a links view's ends are offered on Ctrl+E as every grid is
  (`sitemapsOffer`, `robotsOriginsOffer`, `urlGridOffer` with its `linkEnds`), through the shared dialog and toast,
  their files named "… sitemaps", "… robots.txt origins" (and "that blocked a URL", "that blocked nothing") and
  "… URLs link targets" or "… URLs pages with the links"; the "can't be exported" paths are gone. The types have the
  two kinds, `linkEnds` on a `urls` request and the library's `pagesAnalyzed`. The mock transport answers no export
  of any kind (export is the real sidecar's, end to end), so it gained no route; its simulated crawl's item gains
  `pagesAnalyzed` once analysed.
**Measured on the 100k benchmark crawl** (110,268 URLs fetched, 5,054,148 links), with `si export` from the Release
build: the list of sitemaps (13) as Excel in 0.22 s, its 13 rows as the list has them ("Read, 11 of its 12 sitemaps"
and no counts for the index, whose planted missing sitemap the crawl couldn't read); the origins (3) as CSV in 0.17 s;
the URLs the hyperlinks to 4xx URLs lead to (4,572, the link ends' `total` above) in 0.21 s, and the pages those links
are on (4,540, which takes the pass over every link) in 0.68 s; each the whole command, process start included.

**The checks on the last commit's code.** The Release build has no warning; the engine's tests (1,428), the sidecar's
(267) and the CLI's (28) pass. `si bench --pages 100000` completed and found every planted issue and nothing else:
101,115 of 101,115 pages in 38.3 s, 2,642 pages/s, a peak of 194 MB and +0 MB over the crawl, a 477 MB file, analysed
in 14.4 s. `si readbench --gate` on that crawl failed once, on the links grid's "not followed" half way down (p50
58.7 ms, p95 554 ms against a gate of 200, one slow run of ten on a machine other agents were loading; E7 changed no
link read), and passed every class when run again (that read 43 ms at p95). The app's `npm ci`, `typecheck`, `lint`,
`format:check` and its 3,212 tests (both projects) pass; `build-storybook`, and the 410 stories in both themes with
no axe violation, plainly, under forced colours and with reduced motion; `build:sidecar`; the end-to-end suite, 167
passed and the 2 measurements skipped as they are without `SI_E2E_MEASURE`.

Not verified: the exports' new words against every state a real site gives (the tests hold each word to a literal and
the synthetic crawls' rows to the screens' reads; the garden crawl's lists weren't exported); a crawl of a million
URLs; anything on CI's runners, as nothing was pushed.
## Engine changes round 5 made

Four parts built round 5 at once; this is their one account, merged on `ui/r5-integrate`.

- **Where a bare address starts** (part A; gap 1). `POST /api/crawl-options/start-url` (above), with
  `Fetching/StartAddress.ChooseAsync`: https and http asked at once, 10 s at most, https if it answers at all. Tests:
  `StartAddressTests`, `CrawlOptionsTests`.
- **A crawl the user stops is analysed** (part A; gap 2), as a completed one is, when it was started to be analysed
  (`CrawlManager`; only when the user asked for the stop, `LiveCrawl.StopRequested`, not when the crawl gave up on a
  dead site: R2-6, `CrawlLifecycleTests.ACrawlThatStoppedItselfIsNotAnalysed`): it says `stopped`, `analyzing`, then `stopped` again with its results "so far". One stopped by the
  app quitting isn't. While that analysis runs, continue, remove and delete answer `crawl-busy` ("This crawl is being
  analysed…"), and `POST /analyze/cancel` frees it; the app shows the state with Cancel the analysis. Tests:
  `CrawlLifecycleTests`.
- **One count of pages** (part B; the closing audit's gap 3). Home said 101,107 pages of the 100k crawl where Issues
  and Simple said 101,106: E7 kept the library's `pageCount` (every page the crawl parsed, Cloudflare's `/cdn-cgi/`
  endpoint among them) apart from the analysis's `pagesAnalyzed` (which leaves it out), each true by its own
  definition. The owner's word on the audit ("one number everywhere", `v2-ui-ia.md`) overrules that: a user can't tell
  two counts of pages apart, and Cloudflare's endpoint is Cloudflare's, not the site's. `CrawlSchema.PAGE` now leaves
  out a URL with `/cdn-cgi/` in it, ignoring the case of ASCII letters (`CrawlSchema.NOT_CLOUDFLARE`, which the
  analysis's `SITE_PAGE` uses too), and so do the live counter (`CrawlWriter.IsPage`) and the outcome a refetch takes
  back (`WriterStatements.ReadOutcome`). So `pageCount`, `si stats`'s pages and indexable pages, a running crawl's
  `htmlPages`, the history's and the benchmark's expectation (`HtmlPages`, which the synthetic site now counts without
  its endpoint) are the analysis's pages: the 100k crawl reads 101,106 everywhere, the seeded synthetic crawl 155, the
  garden crawl 265 as before. Existing crawl files read the new way at once: every count is taken from the file when
  read. The app's filter for the site's pages adds `url notContains /cdn-cgi/`, so a figure of pages opens exactly
  them. Tests: `GridServingTests.TheLibrarysPagesAreThePagesItsAnalysisAudited` (it asserted the difference of one
  before), `UrlSummaryAgreement` (indexable pages without the endpoint), and the app's `e2e/counts-agree.spec.ts`.
- **Hidden checks and the workbook's filters** (part C; gap 19 and the workbook parity item).
  `ReportOptions.HiddenChecks` and `ReportOptions.Checks`; `ReportReader.Read` takes them (`listing`) and leaves those
  checks out of the problems, the passed and the skipped, numbering the problems afresh; `ReportModel.HiddenProblems`
  and `Narrowed` say what was left out, and the report and the workbook's summary say it.
  `ExportFields`: `hiddenChecks` belongs to `report`, `workbook` and (round 5's review, R2-9) `check`, `checks` to `workbook`; the request's DTO, the
  `GET /report` query and `si export` (`--hidden-checks`, `--checks`) take them. Tested in `WorkbookExportTests`,
  `ReportDocumentTests` and `ExportEndpointsTests`.
- **The proxy test** (part D). `POST /api/proxy/test` (above), for the proxy test in the app's settings (gap 18 of
  step 3's closing audit). The engine gains `Fetching/ProxyTester` (public, with `ProxyTestResult` and `ProxyTestOutcome` in `Fetching/Models`); the
  sidecar `Api/ProxyEndpoints` and `ProxyTestRequest`. The proxy's address and credentials are made into a `WebProxy`
  by one `ProxyFactory`, moved out of `HttpFetcher`, so the test sends a password exactly as a crawl does.
  No crawl file, issue or count changes; the bench is untouched.
  Tests: `ProxyTesterTests` (engine, 9) against a proxy of the test's own, a loopback server that takes requests in
  absolute form: a site's answer through it (200, and a 404 that is still the proxy working), a 407 without and with
  wrong credentials, the right credentials sent, a closed port, a 502 from the proxy, addresses refused before any
  fetch. `ProxyEndpointTests` (sidecar, 7): a refused proxy as a 200 answer in plain words, and six bodies that are bad
  requests. Not tested: a proxy that never answers (`proxyUnreachable`, a 10 s wait) and a socks5 or https proxy.
- **Hidden checks in the grids** (round 5 review, also noted). Every count in the app left the hidden checks out but
  the URL grid's issue counts (`issues`, `errors`, `warnings`, `notices`, precomputed per URL in `url_summary`) and
  the grids' exports. `UrlQuery.HiddenChecks` (and `UrlFacetQuery`'s, `LinkEndQuery`'s) is now a field of every grid
  query, `hiddenChecks` in the API ([URLs](#urls-e3a)). `UrlQueryCompiler.Counting` reads each of the four as the
  summary's count less `(SELECT COUNT(*) FROM issues hi JOIN issue_types ht ON ht.id = hi.issue WHERE hi.url_id =
  s.url_id AND hi.issue IN ($hide0, …) [AND ht.severity = '…'])`, the join and the severity as `UrlSummaryBuilder`
  counts them, so the difference is exact; the column then has no index (`Indexed` false), so the planner scans and
  sorts rather than walking `ix_url_summary_issues`. The ids are parameters (`$hide0`…), sorted and each once, bound by
  `GridReadContext.Bind` to every statement of the read that names them (a sort key's SQL carries none of a filter's
  parameters), and to the reads alongside it. A filter on a count carries them among its parameters, so its key, and
  every count kept by it, is its own; the query's hash, the facets' keys, a links view's order and `linkEnds`' key
  take them too. The links grid passes them to the URL grid's compiler for each end's values, filters and the
  target's sort; a grid's export reads them with its query (`ExportFields`: `hiddenChecks` belongs to the four grids
  too; `si export --hidden-checks` puts them on the grid's query). With none hidden every statement is the same SQL
  as before. An unknown id is taken (`UrlQueryCompiler.ValidateHidden` refuses only more than 1,000 ids, a null, or
  one over 200 characters), so a stale id in the app's settings never breaks a grid; a grid export checks them so
  too, not by the report's limits. Tests: `HiddenChecksGridTests` (engine, 18: the values, sorts both ways by offset
  and cursor, five filters, counts and cursors never shared, the severity facet alone and in one pass, the links
  grid's ends and `linkEnds`, stale ids taken and hiding nothing, 1,000 of them, the limits), against `GridReference.WithoutChecks`; `GridExportTests.TheIssueCountsAreExportedWithoutTheHiddenChecks`
  (CSV and workbook, the URL grid and a check's rows); `GridEndpointsTests` and `ExportEndpointsTests` (sidecar).

## Engine changes found-on made

- `IssueDefinition.References` (`IssueReferences`: `None`, `Sources`, `Targets`) on the 30 checks that refer to other
  URLs; `GET /api/checks` gives it as `references`. Not stored, not in the fingerprint, so no analysis goes out of
  date. `FoundOnReaderTests.EveryCheckSaysWhereItsUrlsAreFound` holds the list, and
  `EveryFlaggedUrlIsFoundAndEveryFlaggedPageNamesItsTargets` shows each one's URLs on the synthetic site found
  somewhere (two are planted only on its HTTPS version: `redirect-downgrade`, `mixed-content`).
- `Storage/FoundOnReader`: `ReadFoundOn` and `ReadTargets`, reads only, in one snapshot; the two endpoints above
  (`GridData.FoundOnAsync`, `TargetsAsync`). `UrlInspectorReader`'s `ReadLinks`, `ListedIn`, `TotalLinks` and `OtherEnd`
  are `internal` now so the reader reuses them: a source is exactly an inlink.
- The report (`ReportReader`, `ReportDocument`): a `sources` check's examples get "Where to fix it" (the first three
  pages, then how many more; the sitemap; the start address); a `targets` page naming several, "and N others".
  `IssueRow` carries the URL's id (`UrlId`).
- A check's file (the workbook's sheets, `kind: 'check'`, `kind: 'issueRows'`): columns after its own from
  `Export/WhereFoundColumns`, read a page of rows at a time (`GridExport.WriteGrid`'s `extra`): "Linked from (pages)",
  "Linked from", "Listed in sitemaps", or "Links to (addresses)", "Links to". Tests: `WorkbookExportTests` (every
  check's sheet's header and its cells against the reader), `GridExportTests`,
  `ReportDocumentTests.AnAddressOtherPagesReferToSaysWhereItIsFound`,
  `GridEndpointsTests.FlaggedUrlsSayWhereTheyAreFoundAndPagesWhatTheyName`.

## Engine changes evidence makes

Step 3b's evidence work ([the per-check table](v2-evidence.md), whose "As built" says where the build differs from
it). Built on `ui/3b-evidence` (775c6a4, 48d6576, 439dbba): the API above is what it serves.

- **The catalog.** `IssueDefinition.Evidence` (URL column ids) and `IssueDefinition.TargetFacts` (target field
  names), served as `evidence` and `targetFacts` on `GET /api/checks` (`CheckDto`). Neither is stored with an
  analysis nor in `ChecksFingerprint`, as `Grouping` and `References` aren't. A test holds that every evidence id is a
  URL column `/rows` serves and every target fact a field of a target. `References`: `http-page`, `url-uppercase`,
  `url-underscores` become `Sources`; `image-missing-alt`, `image-alt-too-long`, `image-missing-dimensions` become
  `Targets` with `Grouping = RelatedUrl`. No id changes, no text of What, Why or Fix changes.
- **The rules.** `ImageRules` names the images at fault as its issues' targets: each `ImageFact`'s URL, normalised as
  the crawler normalised the link it stored for the same `<img>` (`OutLinkFactory`), is looked up among the crawl's URLs
  (`AnalysisReader.ReadUrlId`, one prepared statement), only for an image at fault and once a page; the same image shown
  twice on a page is counted twice and named once. On the three real crawls of the step 3b audit, every image at fault
  resolved, each to the URL of the page's own image link to it (63 of 63, 135 of 135, 4,153 of 4,153). `FragmentRules` keeps the first missing fragments of each page; `HreflangRules` and
  `CanonicalRules` name what they count (and the element that ended the head); the measurement rules give the limit.
  `HREFLANG_DUPLICATE_CODE` gets a `FaultOf` (the codes before each ": "), so its groups stay what they were. Shared
  wording in `RuleText`: a quote cut with "…", a detail fitted to 120 characters, "over" or "under" a limit.
- **`AnalysisFreshness.VERSION` 8 → 9**: what the analysis stores changes (three checks' grouping and targets, 21
  checks' details, `url_summary.used_on`). Every crawl analysed before reads out of date (`engineChanged`) until it's
  analysed again, and reads as it was stored meanwhile. Counts don't change, so the score's version doesn't. The check
  audit (step 3b, [v2-check-audit.md](v2-check-audit.md)) also raised it 8 → 9 on its own branch; merged, it is **10**, so
  an analysis stored by either branch's 9 reads out of date too. The polite crawl (step 3b) makes it **11**:
  `internal-blocked` is a notice for a firewall's refusal only, a 429 being held back and stored as not checked, and the
  notes tell those URLs, and ones on a private network address, apart from those whose site stopped answering.
- **The crawl file: two columns, no new schema version.** `responses.pixel_width` and `pixel_height` join
  `CrawlSchema.RESPONSE_COLUMNS`, which the writer adds whenever it opens a file, as it added the content
  fingerprint's lanes. `CrawlSchema.VERSION` stays 2 on purpose: `CrawlDatabase.CheckSchema` refuses a file of any
  other version ("Crawl the site again with this version"), so a new version would make every crawl already made
  unreadable. As with the lanes, either engine reads and continues either file: a crawl made before has the columns
  once it's continued, its URLs so far null; a reader checks the columns are there (`pragma_table_info`, once per read)
  and reads null without them. `AnalysisReader.HasFingerprintLanes` checks its own three columns, not the list's length.
- **Fetching.** For an image response that succeeded, `HttpFetcher` reads the first bytes of the body and
  `Fetching/ImageHeader` reads the size from them: PNG (IHDR), GIF (the logical screen), JPEG (the first SOF0-3, 5-7,
  9-11 or 13-15, walking segments by their lengths), WebP (VP8, VP8L, VP8X), AVIF (`ispe`, best effort); none for SVG
  or anything else. At most 256 KB is read for a JPEG (its EXIF and colour profile come first) and 64 KB for the rest,
  4 KB at a time, never buffered past that, and reading stops as soon as the size is known. An image sent without a
  length, which the crawler already reads whole to measure, is read for its size as it's counted. Stored through the
  page record with the response.
- **Parsing.** `PageFacts.HeadEnd` (`HeadEndFact(Element, Line, Implicit)`): what made the parser leave the head (the
  first node of a body the parser had to start, shown as its start tag cut to 60 characters, or text; or the `<body>`
  tag itself) and its line. `PageWalker` sets it only when a canonical or an hreflang link ends up outside the head, so
  every other page's facts are as they were. The line comes from AngleSharp (the parser's element callback or its
  source references, whichever the bench shows cheaper). A page crawled before has none, and its detail says less.
- **The URL summary.** `url_summary.used_on`, counted by `UrlTable` in the analysis's pass over each page's links:
  every page audited that links to the URL by any kind, once. The link summary is left as it is
  (`LINK_SUMMARY_VERSION` stays 1): it counts links by kind, position and flags, not the pages they're on, so it can't
  give a count of pages without a new table per target and page; the analysis already reads every link once.
- **The readers.**
  - `UrlColumns`: `usedOn`, `pixelWidth`, `pixelHeight`, `fetchError`, `redirectChain`, `robotsRule`;
    `UrlColumnType.Object`, a column computed for the rows of the page read (`UrlGridReader.Values`): the chain by
    `UrlInspectorReader`'s own walk, the rule by `RobotsReader.WhyRefused` with each origin's file parsed once a read.
    `ColumnWords` titles them, with the unit `px`.
  - `IssueGroupReader.ReadRows`: the order by importance when no sort is given (page or URL checks), and without a URL
    summary depth and URL. `ReadGroups`: `related`, `sharedText` (the first page's `responses.title`,
    `meta_description` or `h1`), `sort` (`pages`, `size`), and `likelyTemplate` for a check that doesn't group, by one
    function that `/analysis`' counts use too.
  - `FoundOnReader.ReadTargets`: `IssueTarget` gains the URL's facts (its end's response, `final`, `indexability`,
    `canonical`), the link's (`fragment`, `flags`, `links`) and `img` (the page's details read once per page, not per
    target).
  - Exports and the report read a check's rows through `ReadRows` (so in its order, with its evidence);
    `WhereFoundColumns` writes a target with its facts under the headers above; `ReportReader` and `ReportDocument`
    give the evidence and both kinds of "Where to fix it".
- **The sidecar.** `CheckDto` (`evidence`, `targetFacts`), the analysis counts (`likelyTemplate`), the groups request
  (`sort`) and answer (`sort`, `likelyTemplate`, each group's `related` and `sharedText`), the targets answer, and
  the new columns on `/api/url-columns`. The app's TypeScript types are written by hand (`app/src/api`), so they're
  the app's to follow.
- **Tests.** Each change is seen failing first: the catalog's lists against the columns and the target fields; each
  rule's new detail, and the image checks' targets and groups (the synthetic site's expected issues tables updated
  case by case, never loosened); the image header reader on real PNG, GIF, JPEG (with a large EXIF block), WebP and
  AVIF bytes, cut short and malformed; the head's end on pages with an early `<img>`, stray text and a canonical in
  the body; `usedOn` against a count from the links; the new columns in `/urls`, `/rows`, CSV and Excel; targets and
  groups against the inspector's own reads; the default order with and without a URL summary; a crawl file from
  before these columns opened, read and continued. The synthetic site serves real image bytes of known sizes for
  the planted images.
- **The bench.** `verify-engine.sh`: build, tests, `si bench --pages 100000` (every planted page and issue, nothing
  else, memory flat) and the readbench gate, with a new case "Rows of the biggest check, by importance" in the
  `check` class (75 ms at p95).
- **Measured** (`verify-engine.sh` on 5d4c3f0, 32 cores): the engine's 1,542 tests, the sidecar's 286 and the CLI's 28
  pass (the CLI's check-file header test was updated in 5d4c3f0, after the run's test step, and passes). `si bench
  --pages 100000`: completed, every planted issue found; 101,114 pages, 110,268 URLs, 5,054,148 links in 62.1 s
  (1,628 pages/s); peak 199 MB working set, flat (199 MB at 10-30% to 177 MB at 70-100%); analysis 20.0 s, 32,413
  issues, peak 253 MB. Readbench: the new "Rows of title-duplicate, by importance" (5,152 rows) 18.1 ms at p95
  (gate 75), its groups 16.1 ms. Two full gate runs each missed a few reads in classes this work doesn't touch, by
  different reads each time (run 1: the link pass without its summary by target URL, one 8.4 s outlier at p95 with
  p50 1.9 s; the crawl's facets of pages 258 ms against 245; run 2: three URL-grid filters at 117-182 ms against 110,
  the facets 246 ms), while another program's `si.exe` was running on the machine; each of those reads run alone
  passes its gate (1,352 ms against 2,500; 12, 27 and 23 ms against 110; 135 ms against 245).

## Engine changes the fix loop makes

Step 3b's fix loop ([the design](v2-fix-loop.md), with its table of parts under "Engine and storage"). This is the
plan the API above is written to. The engine's part (stages E1 to E4) is built on `ui/3b-fixloop-engine`, as below;
[the design's "As built"](v2-fix-loop.md#12-as-built-the-engine-stages-e1-to-e4) says where it differs, and what was
measured. The sidecar's part (S1, the comparison, and S2, the rechecks) is built on `ui/3b-fixloop-sidecar`, as the
"(fix loop)" sections above say where it differs; [the design's "S1 and S2 as built"](v2-fix-loop.md#s1-and-s2-as-built-the-sidecar)
has the rest.

- **Built: what the sidecar calls.**
  - The comparison: `CrawlComparisonReader.ReadSummary(current, other, cache, ct)` (a `CrawlComparison`),
    `ReadCleared(...)` (a `ClearedPage` of rows or causes), and `IssueGroupReader.ReadGroups` / `ReadRows` overloads
    taking the other crawl's path (`IssueGroupPage.Compared`, `IssueGroup.Change`; `IssueRowPage.Compared` and
    `Changes`, aligned with the rows). `CrawlComparability` gives the differences and `comparable`.
  - The plan: `RecheckPlanReader.Plan(path, RecheckAsk(checkId, group, urlIds), settings, limit = 1000)`, a
    `RecheckPlan` with the fields `/rechecks/plan` serves (`judged`, `phase1`, `phase2Max`, `limit`, `capped`,
    `notRechecked`, `estimateMs`, `suggestFullCheck`, `analysisFirst`) and `Refused` (`RecheckRefusal`: `CrawlBusy`,
    `NotAnalyzed`, `NotAnalysable`, `Unsupported`). The secrets are `CrawlOptions.PlanResume`'s, as Continue's; the
    sidecar adds `site-busy` and `recheck-not-latest` from its library.
  - The run: `CrawlEngine.Recheck(RecheckRequest(path, plan, options))`, a `CrawlJob` stopped as a crawl's is. Its
    `CrawlProgress.Recheck` is `{ phase, done, total }`; its `CrawlSummary.RecheckId` the recheck's number. The crawl
    must be analysed after it (`CrawlAnalyzer`), which settles the recheck.
  - The record: `RecheckReader.ReadAll(path)` (newest first), `Read(path, id)` and `ReadRows(path, id, outcomes, roles,
    offset, limit)`, as `/rechecks`, `/rechecks/{n}` and `/rechecks/{n}/rows` serve them. `CrawlRun.IsRecheck` marks a
    recheck's run in the history.
  - The catalog's `IssueDefinition.Recheck` (`RecheckKind`: `Urls`, `Sitemaps`, `RobotsTxt`, `SiteVersions`), served
    camelCase as `recheck`.
  - A new URL grid column, `skipReason` ("Why not read"), an enum of every skip reason, `recheck` ("Found by a recheck,
    not followed") and `noLongerLinked` ("No longer linked (a recheck let it go)") among them: the grids' label for both.
  - Added for the sidecar (S1): `CrawlFileSummary.CheckCounts` and `OptionsFingerprint` (read by `CrawlSummaryReader`),
    `CrawlComparability.OptionsFingerprint` and `FromSummaries` (a `SummaryComparison`: the check-level comparison from
    two summaries alone); `ReportOptions.CompareWith` and `NotCompared`, with the report's part and the workbook's
    columns and sheet. For S2, `CrawlFileSummary.RecheckCount` and `LastRecheck` (`RecheckReader.ReadLatest`).

- **Comparison, read only.** `Storage/CrawlComparisonReader`: `ReadSummary` (`/changes`), `ReadRowChanges` and
  `ReadGroupChanges` (`compareWith` on `/rows` and `/groups`, through `IssueGroupReader`), `ReadCleared` (`/fixed`).
  Each file is opened as the grids' readers open it, one snapshot each through its own `CrawlReadGate`, and they're
  joined in memory, keyed by `UrlKey.Compute` of the URL's text, a check at a time: no `ATTACH`, nothing written.
  `Analysis/CrawlComparability` (pure) gives the differences and `comparable` from the two crawls' stored options,
  `AnalysisInfo` and coverage. Models in `Storage/Models/`, one per file.
- **The catalog.** `IssueDefinition.Recheck` (`urls`, `sitemaps`, `robotsTxt`, `siteVersions`; empty for
  `orphan-page` and `no-https`), served as `recheck`. Not stored, not in `ChecksFingerprint`. A test holds every check
  to having one.
- **Rechecks.** `Storage/RecheckPlanReader` (the scope by `references`, read only); `CrawlEngine.Recheck` and
  `RecheckRequest`; a recheck mode in `CrawlSession` (the plan's URLs handed to the workers directly, never through
  the frontier; phase 2 after phase 1; redirects to their end; one step of new URLs; a minute's wait for a site that
  stops answering) and in `CrawlWriter` (`OpenForRecheck`: the recheck tables, the "before", records replacing a URL's
  response, details, HTML and outgoing links at once, the let-go walk at close, the crawl's status left as it was);
  `IssueWriter` settles every recheck not yet settled as it commits; `Storage/RecheckReader`; `CrawlRunReader` leaves
  recheck runs out of `TimeCrawling`.
- **The crawl file: three tables, no new schema version.** `rechecks`, `recheck_urls`, `recheck_targets`, created by the
  writer the first time a file is rechecked, as `notices` and `run_time` are; `CrawlSchema.VERSION` stays 2, and a
  reader finds none in a file never rechecked. `SkipReason` gains `Recheck` (7: found by a recheck, not fetched) and
  `NoLongerLinked` (8: let go). The stats leave let-go URLs out of "found"; the URL grids label both.
- **`AnalysisFreshness.VERSION` doesn't change**: a crawl that was never rechecked analyses to what it did.
- **The synthetic site** gets revisions (`SyntheticSiteOptions.Revision`, switched on a running server), with
  `GetExpectations(revision)` and `GetChanges(from, to)`, so the comparison is proven exactly, as the issues are.
- **The CLI.** `si compare <crawl> <other> [--check id] [--json]`; `si recheck <crawl> --check id [--group-url u |
  --group-detail d] [--urls file]`; `si bench --revisions`; `si readbench --compare-with <crawl>` and its class
  `compare`.
- **The sidecar.** `siteKey`, `previous`, `changes` and `rechecks` on `CrawlListItem`, from the summary cache, which
  keeps each check's count and an options fingerprint; `/changes`; `compareWith` on the groups and rows requests;
  `/fixed`; `/rechecks/plan`, `/rechecks` and its reads; the `rechecking` status and the `recheck` event; the flow
  (analyse first when out of date, recheck, analyse); `compareWith` on the report and the workbook. The app's types are
  written by hand (`app/src/api`), and `sites.ts` groups by `siteKey`.
