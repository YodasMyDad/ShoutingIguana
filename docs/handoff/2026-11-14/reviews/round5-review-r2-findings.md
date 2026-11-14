# Round 5 review, R2 (engine, sidecar API, shell, security): findings

`ui/r5-integrate` at `008b4b2`, diff `8ec31e6..008b4b2`. 11 findings, none high. Proofs in the session scratchpad
`a539aa23-…/scratchpad/r5-review-2/`.

Checks in the reviewer's worktree: Sidecar 279/279, Cli 28/28, Engine 1,445/1,446 (one failure under load,
`SkippedCheckCrawlTests.TheChecksOfRobotsTxtAreSkippedWhereACrawlThatIgnoredItNeverReadIt`, untouched by round 5,
then 3/3 alone); `cargo clippy -D warnings` clean, `cargo test` 134 + 3. Private key: the whole branch history
searched for minisign/rsign secret-key headers (raw and base64), `PRIVATE KEY`, `.key`/`.pem`/`.pfx`: none; the
`tauri.conf.json` pubkey decodes to a minisign public key.

1. Medium. `Engine/Fetching/StartAddress.cs:69`, `Sidecar/Api/OptionsEndpoints.cs:47`: any HTTP status through a
   proxy counts as the site answering, and Home and Simple's quick start call `startUrlFor(address)` without options
   (`HomeScreen.tsx:75`, `SimpleHome.tsx:78`), so the sidecar probes with `defaults.Read()` (credentials masked `***`).
   A proxy with a password: https tunnel refused, http gets the proxy's 407 = "answered", so every bare address starts
   on http, even a non-existent site; a proxy answering 502 the same. CONFIRMED (`no-such-site.invalid` -> Http for 407
   and 502). Fix: 407, the proxy's own 5xx and tunnel errors are no answer; the app sends the defaults with secrets
   restored (`api.crawlOptionDefaults()`).
2. Medium. `app/src/api/client.ts:194` (`deleteCrawlFile`), store `app/src-tauri/src/secrets.rs`: a crawl's secrets
   (`crawl:<id>`: proxy password, header values such as Authorization) are never removed. CONFIRMED (no caller forgets
   the key). Fix: after a successful delete-file, `keepSecrets(\`crawl:${id}\`, {})`; also when a Remove's Undo
   expires.
3. Medium. `app/src-tauri/src/updates.rs:116`: "nothing running" checked once before the download; after it the
   engine is stopped without a re-check. Install, start a crawl during a slow download: the crawl is stopped and the
   installer runs. PLAUSIBLE (needs a signed release). Fix: re-check `shell.live.kinds().any()` under the quit lock
   before `QuitPhase::Stopping`; error if anything runs.
4. Medium/low. `app/src-tauri/src/live/notify.rs:73`, `:120` (unchanged in round 5): with analysis on stop, the
   hidden-window notification never says the crawl stopped (Advanced "Analysis finished"; Simple "The results for X
   are updated", no Continue). CONFIRMED. Fix: a `stopped` + `crawled` + `was_analysing` branch: "Crawl stopped" with
   the results so far and Continue, Simple's stopped words.
5. Low. `Engine/Reporting/ReportDocument.cs:405-432`: with hidden checks, the score table drops their rows but the
   footer still says `100 − score.Lost`, and the site-wide "60" and "Held at 50" sentences can cite an unshown check
   (hide `no-https`: rows sum 66, footer "100 − 126 = 0"). CONFIRMED. Fix: a "Hidden checks: −N" row, or keep hidden
   checks in the score table.
6. Low. `Sidecar/Crawls/CrawlManager.cs:668`: a crawl that stopped itself (site-down give-up, `CrawlSession.cs:559`)
   is analysed too; `crawl.StopRequested` isn't checked; Continue refused as busy meanwhile; contradicts the comment
   and the brief. PLAUSIBLE. Fix: `analyse = (Completed || (Stopped && crawl.StopRequested)) && …`, or document it.
7. Low. `Sidecar/Api/OptionsEndpoints.cs:47` (`ToCrawlOptions(out _)`): an unusable proxy reaches `HttpFetcher`: 500
   "Invalid URI" (`proxyUrl: "not a proxy"`, or `ftp://…`), logged as an engine error; the app silently falls back to
   https. CONFIRMED. Fix: on mapping or `ValidateFields()` errors, return the address as typed (or 400), no probe.
8. Low. `Engine/Storage/UrlInspectorReader.cs:397`, `:893`: case-sensitive `instr(u.url,'/cdn-cgi/')` while
   `CrawlSchema.NOT_CLOUDFLARE` and `SITE_PAGE` use `lower()`; a `/CDN-CGI/` URL is not a page and not audited yet the
   inspector calls it audited. PLAUSIBLE. Fix: use `CrawlSchema.NOT_CLOUDFLARE` in both.
9. Low. `Engine/Export/WorkbookExport.cs:57` (`WriteCheck`) vs `ReportReader.cs:64`: report and workbook renumber
   after hiding checks, a single check's export numbers against the full list (report "4", its export's sheet "5 …").
   PLAUSIBLE. Fix: `hiddenChecks` for `check` exports too, or never renumber.
10. Low. `app/src-tauri/src/data_folder.rs:92`, `updates.rs:116`: "nothing running" ignores exports; a big workbook
    export fails when the folder is switched or an update installs. PLAUSIBLE. Fix: refuse while an export job runs.
11. Low. `app/scripts/smoke-shell-simple.mjs:188`: the tray-words step reads only the first "Tray icon shown" line
    (the tooltip at creation); later tooltips and the menu labels are never checked. PLAUSIBLE. Fix: log each tooltip
    change and check the last, or read it through `shell-window.ps1`.

Looks right: one page definition everywhere (`CrawlSchema.PAGE`, live `IsPage`, `ReadOutcome`, stats, history,
summary, resume recount, `SITE_PAGE`, the bench); no new write path outside `Storage/`, one writer, no second parse;
analysis on stop skipped on shutdown, failure and `analyzeWhenDone: false`, cancel stores nothing, busy refusals via
`ThrowIfLive`; the proxy test behind `RequestGuard`, no body, no redirects, no system proxy, no kept credentials,
10 s / 15 s limits (adds only a port probe the page could already make by crawling); the updater (signature before
install, fixed endpoint, override needs `SI_APP_PROFILE_DIR` and is still signature-checked, no downgrades, installs
only a found version, notes as plain text); secrets under validated keys, a 1,200-byte JSON object, never in
settings, crawl files or logs; the data folder only from the shell's own dialog, refused while running or under an
env override, write-probed, a vanished folder falls back; drops give paths only, opening still goes through the
sidecar's `.sicrawl` check; the window title capped at 200 characters; every new command named, none takes a path;
the licences generator's sources; the shell's Simple words; G1's re-measure and its smoke step fail when the bar
overflows.
