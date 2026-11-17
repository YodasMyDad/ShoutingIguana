# Shouting Iguana engine (v2)

The cross-platform crawl engine that replaces the WPF app in `src/`. It is headless .NET 10: today it runs from
the command line, and it becomes the backend for the desktop app (Tauri + React) and the Search Console /
Analytics / Ads integrations later.

## Projects

| Project | What it is |
| --- | --- |
| `src/ShoutingIguana.Engine` | Crawler library: fetching, robots.txt, sitemaps, parsing, storage, SEO analysis |
| `src/ShoutingIguana.Cli` | `si` command-line tool: `crawl`, `analyze`, `issues`, `score`, `export`, `report`, `stats`, `testsite`, `bench`, `readbench` |
| `src/ShoutingIguana.Sidecar` | `si-sidecar`: the engine as a localhost HTTP API for the desktop app (see [Sidecar](#sidecar)) |
| `src/ShoutingIguana.TestSite` | Deterministic synthetic shop with planted SEO issues, for tests and benchmarks |
| `tests/ShoutingIguana.Engine.Tests` | xUnit: unit tests plus end-to-end crawls and analyses of the synthetic site |
| `tests/ShoutingIguana.Cli.Tests` | xUnit: the `si` commands run in-process against the synthetic site, exit codes and output included |
| `tests/ShoutingIguana.Sidecar.Tests` | xUnit: the API hosted in-process against the synthetic site, plus the real `si-sidecar` process |

## Build, test, run

```bash
cd engine
dotnet test ShoutingIguana.Engine.slnx

dotnet run --project src/ShoutingIguana.Cli -- crawl https://example.com --db example.sicrawl   # crawls, then analyses
dotnet run --project src/ShoutingIguana.Cli -- issues example.sicrawl                           # what was found
dotnet run --project src/ShoutingIguana.Cli -- issues example.sicrawl title-too-long            # the URLs for one issue
dotnet run --project src/ShoutingIguana.Cli -- issues example.sicrawl --csv issues.csv          # everything, for Excel
dotnet run --project src/ShoutingIguana.Cli -- score example.sicrawl                            # the site's score, and how it was worked out
dotnet run --project src/ShoutingIguana.Cli -- export example.sicrawl --kind urls --out urls.xlsx   # any grid, as CSV or Excel
dotnet run --project src/ShoutingIguana.Cli -- export example.sicrawl --kind workbook --out all.xlsx # a sheet for each check
dotnet run --project src/ShoutingIguana.Cli -- report example.sicrawl --out report.html           # the report
dotnet run --project src/ShoutingIguana.Cli -- analyze example.sicrawl                          # re-run the checks
dotnet run --project src/ShoutingIguana.Cli -- stats example.sicrawl
dotnet run --project src/ShoutingIguana.Cli -- crawl --resume --db example.sicrawl              # after Ctrl+C
dotnet run --project src/ShoutingIguana.Cli -c Release -- bench --pages 100000
dotnet run --project src/ShoutingIguana.Cli -c Release -- bench --pages 100000 --db bench.sicrawl              # keeps the crawl
dotnet run --project src/ShoutingIguana.Cli -c Release -- readbench bench.sicrawl --max-p95-ms 300            # the grids' reads
```

`si crawl --help` lists every option: concurrency and rate cap, user agent presets, extra headers (for firewall
allowlisting; they are only sent to the site being crawled, and never over plain HTTP when it starts on HTTPS),
proxy, depth and URL limits, robots.txt, subdomains, start-folder scope, nofollow, stored HTML, and `--no-analyze`.

The start URL and any `--sitemap` can't carry a user name or password (`https://user:pass@host/`): the crawler never
sends them, and the URL is stored in the crawl file, listed and logged as it is, so they would leak. Send credentials
in an `Authorization` header (`--header`), whose value a crawl file doesn't keep. A `--proxy` may carry credentials.

Exit codes, for scripts and schedulers:

| Code | Meaning |
| --- | --- |
| 0 | Done |
| 1 | The crawl or analysis failed |
| 2 | Bad arguments, or a file that can't be used |
| 3 | The crawl stopped itself because the site stopped answering; continue it with `--resume` |
| 130 | Stopped with Ctrl+C |

## Sidecar

`si-sidecar` serves the engine to the desktop app over HTTP on `127.0.0.1`. The Tauri shell starts it, reads the one
line it writes to stdout, and calls it with a bearer token. The contract (endpoints, events, errors and security) is
[`docs/v2-sidecar-api.md`](../docs/v2-sidecar-api.md).

```bash
dotnet run --project src/ShoutingIguana.Sidecar -- --data-dir ./crawls --state-dir ./si-state
# {"event":"ready","port":53123,"version":"2.0.0-alpha.1","token":"<made up, as no SI_SIDECAR_TOKEN was set>"}
curl -H "Authorization: Bearer <token>" http://127.0.0.1:53123/api/health
```

| Flag | Default | Does |
| --- | --- | --- |
| `--data-dir <folder>` | `Documents/Shouting Iguana` | Where new crawls are saved. The library lists the `.sicrawl` files at its top level, plus files opened from elsewhere |
| `--state-dir <folder>` | `<local app data>/ShoutingIguana` | Holds `library.json`, `defaults.json`, `analysis-settings.json` (the analysis's limits the user changed, and only those), `summaries.json` and `logs/`. One sidecar at a time may use it |
| `--port <number>` | `0`, a free port | A fixed port, for development. The ready line gives the port either way |
| `--stdin-lifeline` | Off | Shut down on a `shutdown` line on stdin, or when stdin ends because the shell has gone |
| `--dev` | Off | Development only: without `SI_SIDECAR_TOKEN`, make a token up and give it in the ready line |

| Variable | Is |
| --- | --- |
| `SI_SIDECAR_TOKEN` | The bearer token, at least 32 characters. The shell makes 32 random bytes as hex for each launch. The sidecar removes it from its environment once read. Required, unless `--dev` is given, when the sidecar makes one up and adds it to the ready line |
| `SI_SIDECAR_ORIGINS` | Comma-separated origins allowed to call the API from a webview: `http://tauri.localhost` (Windows), `tauri://localhost` (macOS, Linux), `http://localhost:<port>` (`tauri dev`). None by default |
| `SI_PARENT_PID` | The shell's process. When it ends, the sidecar shuts down |

- Every request needs the token (401), a `Host` of `127.0.0.1:<port>` or `localhost:<port>` (421), and an allowed
  `Origin` if it sends one (403). Bodies are capped at 1 MB.
- Logs go to stderr and to `<state-dir>/logs/sidecar-<date>.log` (14 days kept), never to stdout.
- A shutdown (`POST /api/shutdown`, Ctrl+C, SIGTERM, the stdin lifeline or the parent ending) stops every crawl,
  leaving it resumable, cancels every analysis, which then stores nothing, and exits 0. It exits 2 for arguments it
  can't use, 1 when it can't start (a port already taken, a state folder it can't create), and 3 when another sidecar
  is already using the state folder.

## How a crawl works

1. The start URL (and every sitemap URL) goes into the `urls` table of the crawl's SQLite file. So does the start URL's
   path on each of the site's other versions (bare domain or `www`, over http or https), claimed right behind it, so even
   a crawl stopped early knows how they answer.
   - These probes are status checks only: never parsed, never followed, and not counted toward the URL limit. Where one
     redirects is checked the same way. So a version that answers with the whole site never gets it crawled twice.
   - They go through robots.txt and the rate gate like any URL, but never hold up a crawl. A probe gets a single try,
     and isn't tried again at the end. Where its origin's robots.txt timed out, it isn't sent at all: it is recorded
     dead with that error.
   - Once the site itself links to one, lists it in a sitemap or redirects to it, it is one of the site's own URLs.
2. A dispatcher claims queued URLs breadth-first and hands them to two worker pools: one for the site itself and
   one for status checks on other sites.
3. Each worker checks robots.txt and waits on a per-site gate. `example.com` and `www.example.com` are one site,
   on http and https alike, so they share one: sites serve one host and redirect the other.
   - The gate keeps to the pace the host's answers teach (`Fetching/HostPace`); the crawl's concurrency and rate are
     its ceiling, never its starting pace. Every request to the host goes through it: pages, assets, robots.txt,
     sitemaps and the icon. A sitemap answered 429 is asked again like a page, up to four times, after the wait its host
     or section asked for, and its deadline starts with each try's turn at the gate.
   - Slow start: one request at a time at one a second. While answers stay healthy, the rate and the requests at once
     double at most once a second, so a site sees 1, 2, 4 requests a second in its first seconds; on a host that answers
     within 100 ms (a local or nearby server), once every ten of its round trips but no more than every 100 ms, so the
     100k bench is at full speed in about two and a half seconds. Answers that come back much slower than the host's
     first ones hold the pace where it is, for eight answers at most.
   - A limit on one section (`Fetching/SectionPace`): a 429 is put down to a folder of its URL, a section already
     limited if the URL is in one, else its last folder (so /products/belt and /collections/belts/products/belt are both
     in /products/; a URL in no folder is in the root's). That section alone is limited: its URLs are held back from the
     workers and asked one at a time (`Crawling/SectionTrickle`), during the crawl and after it, an interval apart that
     starts at the first Retry-After, doubles at each 429 of a page the section let go (never sooner than the
     Retry-After, and past five minutes up to 20, so a limit that each refused try renews gets a long quiet spell before
     the last tries), and shortens by a tenth each time a page gets through. The rest of the host keeps its pace. Only 429s
     from three sections within ten minutes are the host's own limit and slow the whole host, as below. Nothing is given
     up while pages get through: a section is given up on after three 429s in a row at its slowest (one page every 20
     minutes), and every section 20 minutes after the rest of the crawl is over; what is left is not checked. Progress
     says how many are left and when the next is tried.
   - A host that asks to slow down as a whole (429s from three sections, or 503 with Retry-After) is paused, all of it, for its Retry-After (or 10 s,
     doubling while it keeps asking, up to two minutes), then resumes one request at a time at half the pace that
     tripped it (never below one every five seconds), so nothing queued bursts out after the pause. 429s to requests sent before that slowdown don't slow
     it again. Two failures in a row (502, 504, a 503 without Retry-After) slow it the same way without a pause, at
     most once every 30 s and never below one a second. A firewall's block page says nothing about pace, and nor does
     no answer at all: whether an origin is down is judged per origin (below), so a dead www twin never slows the
     site.
   - It recovers additively: every 30 s without a slowdown, a tenth of the pace it slowed from and one more request at
     once; and for ten minutes after a host asked to slow down, never above nine tenths of the pace that tripped it.

   The worker then fetches over plain HTTP without following redirects (every hop is its own URL) and parses the
   HTML once into `PageFacts`.
   - A robots.txt that can't be read (5xx, 429, no answer) is retried like a page, then means "disallow
     everything" for two minutes, as RFC 9309 requires. An origin other than the start URL's that doesn't answer
     at all (another site, a dead www twin, a closed http port) gets no verdict, so links to it record their real
     network error. That departs from RFC 9309 on purpose, as `docs/v2-rebuild.md` explains.
   - Whether an origin answers is counted per scheme, host and port. After three URLs in a row on one get no
     response, another site's remaining URLs there fail at once ("Not checked"). On the site being crawled,
     robots.txt is asked first: if it answers, those URLs failed for reasons of their own. An origin that has never
     answered, while the site's last answering origin still does, is dead (a www twin, a closed port) and fails fast
     like another site; the URLs it was asked for keep their real error. Any other origin is down: it is waited out
     with one-minute pauses and a notice, never written off, and after ten minutes down (time paused aside) the
     crawl stops, to be resumed.
   - The site's URLs that got no response, or were asked to slow down (429, 503), are tried once more when nothing
     else can be claimed (one answered 429 again, up to three times, each after the pause the host asked for) (at most 10,000 are held back for it), handed to the workers as they have room, at the pace
     the host has taught (which doesn't recover during those last tries, so it never probes upward into the limit
     again) and after any pause it asked for. A 429 or 503 isn't retried on the spot: that would renew a
     rate limit's penalty. That comes before the URLs only sitemaps list. URLs refused because robots.txt couldn't be
     read wait for its two-minute cool-down first, as do the sitemaps it declares. Sitemaps aren't read while the
     crawl is paused.
   - A 429 says nothing about the URL. One the site still answers with 429 at its last try (another site's, after its
     retries) is stored as not checked ("Not checked: the site asked the crawler to slow down"), like a URL whose host
     stopped answering: no check judges it, and the analysis says how many there were. A firewall's block page (403, a
     challenge) is still a block.
   - A public site's crawl never asks the user's own machine or network (`Fetching/PrivateNetworkGuard`): a link to
     localhost, 127.x, ::1 or a private range (10/8, 172.16/12, 192.168/16, 100.64/10, fc00::/7, link-local) is stored
     as not checked ("Not checked: private network address; not checked from a public site"), with no request, not
     even for its robots.txt, and judged by no check. They are asked when the start URL is itself on such an address,
     or its name resolves to one (a staging or intranet site, the synthetic test site on 127.0.0.1). A name that
     resolves only to private addresses is refused as the crawler connects; through a proxy, which resolves names
     itself, only addresses written into the URL are.
4. A single writer applies results in batched transactions: the response row, a compressed detail blob, the
   links, and newly discovered URLs. It also owns the frontier, so "nothing queued and nothing in flight" is an
   exact completion test.
5. Stop at any point with Ctrl+C, then continue with `--resume`. Progress counts what was actually read:
   `BytesDownloaded` is the bodies fetched, not the sizes that status checks were told.

Beside all that, once, the crawl fetches the site's icon: the first page it parses names the candidates, and one to
four polite requests later the icon is in the crawl file, or the site has none ([the site's icon](#the-sites-icon)).
It is no URL of the crawl, and the crawl neither waits for it nor fails with it.

Progress (`CrawlProgress`, twice a second) costs the same at a million URLs as at ten. The writer keeps running totals
as it stores each response, and publishes them as one record after each batch it stores, so one report's numbers are
all of one moment and agree with each other, and a crawl's last counts are what its file holds. The rest is read from
the site's host gates:

- every URL fetched by status class (2xx to 5xx, another status, no response, blocked) and by status code, a block
  page's status being the firewall's rather than the site's, and why the URLs without a status have none;
- the part of each of those that is the crawler's own probes of the site's other versions (`SiteVersions`): they are
  counted in every figure, as the crawl file has them, and said apart, so the https probe of a site served over http
  only reads as the crawler's question and not as the site failing to answer. A probe the site turns out to lead to
  becomes one of the site's URLs and leaves that part, and a crawl continued has the part it had;
- the time spent crawling over every run of the crawl (`TimeCrawling`): the earlier runs' as the file has it, and this
  run's without its pauses, where `Elapsed` is this run's with them; null when an earlier run wasn't timed
  ([Time spent crawling](#time-spent-crawling));
- the URLs crawled (`Crawled`): those fetched but for the URLs never asked, their host having stopped answering, which
  have an outcome and are done with, but weren't crawled. It's the URL grid's count of fetched URLs, and
  `CrawlFileSummary.CrawledUrlCount` for a crawl at rest;
- the site's own URLs (internal, not the probes of its other versions) found and fetched, which a URL limit counts and
  they never pass, and the limit itself;
- the phase: starting, reading robots.txt (nothing else can be fetched), crawling, reading sitemaps (nothing else is
  left), retrying (the last tries of URLs that got no response, until they're over), finishing;
- what holds it up now: the site's URLs waiting for its robots.txt and other sites' (link checks) waiting for theirs, a
  crawl-delay, a pace the site's answers have slowed (`backoff`, with the time between requests now) or a Retry-After,
  an origin that's down, robots.txt's cool-down. At most five for each reason,
  the longest first, with a count of the rest; none once the crawl is finishing.

Progress and notices name one of the site's hosts the same way: the start URL's host for it and its www twin, which are
one site with one budget, and a subdomain without its `www.` (`CrawlScope.HostName`). Origins (scheme, host and port)
are named as they are.

Every notice (the site blocking the crawler, or asking it to slow down, robots.txt unreadable, the site down, the
sitemaps read) is kept in the
crawl file, numbered across runs, with the facts it's about (which host, how many responses were blocked), so any
program that opens the crawl can show it (`CrawlNoticeReader`). `blocked` (a firewall's block page or challenge) and
`rate-limited` (a 429: the pause asked for and the pace the crawler goes on at) are said once per host, and
`robots-unreachable` once per origin, in each run. `rate-limited-unchecked` is said at the end of a run whose site
still answered 429 at the last try, with how many of its URLs weren't checked and what to do. Read back, a `blocked` notice counts the host's blocked responses in
the whole crawl, every run's. Ids carry on from the file's highest, so the last ones heard before a crash can be given
again: a notice is its id and its time. `CrawlHistoryReader` counts a crawl's fetching over time back from the stored
fetch times, for a chart of a crawl continued later, and the site's own URLs found, each from when the page that first
linked it was fetched; the other found and queued counts can't be told, as when each URL was found isn't stored.

Memory stays flat because the only thing in memory that grows with the crawl is a 128-bit key per URL (about
40 bytes). The frontier, results and links all live on disk.

Sites that block crawlers are reported, not hidden. Cloudflare/Akamai/DataDome/Imperva/Sucuri/CAPTCHA
responses and rate limits are recorded as `Blocked`, with the reason, and raise a notice. Only challenge and block
pages count: a genuine 403 from a site behind one of those firewalls stays a 403.

## SEO analysis

When a crawl completes, `CrawlAnalyzer` runs 87 checks over what was stored and writes the issues into the same
file. It never refetches or reparses anything, so any crawl can be analysed again with new rules or thresholds
(`si analyze`). It refuses a crawl that is still running. A rule that fails on one page (page content is untrusted)
is logged with the URL and skipped for that page, and the analysis notes say which rule failed on how many pages.

- **One pass.** Page rules see each page's stored facts, headers and outgoing links, with the status of every
  link target looked up from an in-memory table indexed by URL id (a few dozen bytes per URL). Site rules
  collect what they need during the same pass (hashes, alternates, fragments) and report at the end:
  duplicates, orphans, redirect chains, hreflang return links, sitemaps, robots.txt.
- **Catalog.** `Analysis/IssueCatalog.cs` defines every issue: a stable id (`title-too-long`), category,
  severity (Error, Warning or Notice), title, what is wrong, why it matters, how to fix it, and an optional
  learn-more link. Ids are stored in crawl files, so never change one. The checks fingerprint is the id, category
  and severity only, so wording doesn't expire an analysis. Each check also says how its issues group
  (`IssueDefinition.Grouping`) and what the URLs it flags are (`Subject`): always pages, so its count is a part of the
  pages crawled, or any URL, as a missing image is flagged for answering 404. Every end-to-end analysis holds the page
  checks to it, and each any-URL check is shown flagging something that isn't a page by its own rule.
  - Two more things a check says of itself, neither stored nor part of the fingerprint. `SiteWide`: it finds one fault
    of the whole site (robots.txt search engines can't read, no HTTPS, a site hidden from search, a duplicate version
    of the site), which [the score](#the-score) counts as the whole site. `ReportedOnce`: its rule reports one issue
    for the whole site, on the home page, or one for each other version of the site, so a host says "checked once"
    and not "1 of 265 pages": the site-wide checks, and `robots-txt-missing`, `sitemap-error`, `sitemap-missing` and
    `security-headers-missing`. A test puts each through its rule with the fault several times over.
- **Plain words.** Beside the catalog's texts, for readers who aren't SEOs (the app's Simple mode, the report):
  `Analysis/IssuePlainTexts.cs`, by check id (a title, what's wrong as sentences, how to fix it as steps, who fixes
  it, the good news when nothing is found, and optionally what a template fault usually is and what the check looks
  for), and `Analysis/PlainWords.cs` (a plain title for each category; a sentence and what would let the check run for
  a skip reason, by its code). `IssueDefinition.Plain` reads a check's. All 87 checks, the 17 categories and the 17
  skip reasons have theirs. No analysis goes out of date when one changes, as the words are no part of the
  fingerprint.
  - **Both files are generated: don't edit them.** The words are written in
    `tests/ShoutingIguana.Engine.Tests/Analysis/PlainWords/plain-words.json`, in the API's shapes. To change a text,
    or to give a new check, category or skip reason its words, edit that file and run
    `node engine/tests/ShoutingIguana.Engine.Tests/Analysis/PlainWords/generate.mjs` from the repo root, then commit
    the data file and the two C# files together (`--check` writes nothing and says whether they are current). The
    generator refuses an id the engine doesn't have, one the data file leaves out, and a field it doesn't know.
  - Tests hold the compiled words to the data file, every text to the design's template and banned words, and the
    ten checks [the Simple design](../docs/v2-ui-simple.md#ten-checks-written-out) writes out to the document, word
    for word: change one of those ten in both places. A check with no entry would say null, never the technical
    text: a test demands an entry for every check and every skip reason.
  - A text names a threshold as `{limit:name}` (`{limit:titleMaxPixels}`, `{limit:largeImageBytes}`), never as a
    number. `PlainLimits.Fill` fills it from an `AnalysisSettings` with the value and its unit ("200 words", "200 KB",
    "1 second"; a title's or description's width in pixels as the ordinary characters it holds, "about 60
    characters"): the settings a crawl's analysis ran with (`AnalysisInfo.Settings`) for that crawl's words, the
    current ones otherwise. Every numeric threshold has a name, and a name no setting has is an error a test looks for
    in every text.
- **Scope.** Content checks (titles, descriptions, headings, word count, duplicates, Open Graph, URL style,
  rich-result requirements, hreflang) look only at indexable pages. Technical checks look at every page the
  crawler parsed; status-checked URLs (outside the start folder, iframes) and Cloudflare's `/cdn-cgi/`
  endpoints aren't audited as pages. Site-wide settings (HTTPS, security headers, robots.txt, sitemap files) are
  reported once, on the home page, rather than on every URL.
- **Coverage.** A check the crawl can't support is skipped, not reported clean. Skipped checks are left out of
  `issue_types` and the analysis is partial (`analysis.is_partial`). Each skipped check is stored in `skipped_checks`
  with why: a stable code (`no-sitemap`, `depth-limit`, ... in `SkippedCheckReasons`) and a sentence to show beside
  it ("No sitemap could be read."), the first reason that applied. The analysis notes (`analysis_notes`,
  `AnalysisSummary.Notes`) say it for the analysis as a whole.
  - The orphan check needs every page that could link to a sitemap page, and a sitemap to compare with. So a
    stopped, depth- or URL-limited, or folder-scoped crawl skips it, as does one with sitemaps off or none read.
  - Pages missing from the sitemaps need every sitemap read. So that check is skipped while a declared sitemap
    fails for a reason that says nothing about its contents (a 5xx, 429, 403, timeout or bad XML), or is left
    unread at the crawler's limits. A 404 or 410 sitemap simply doesn't exist, so it doesn't stop the check, and
    neither failure counts as a sitemap error.
  - Checks of what the crawl was told not to fetch are skipped: broken external links with `--no-external`, broken
    and oversized images with `--no-resources`, and every sitemap check with `--no-sitemaps`.
  - What robots.txt decides is skipped with `--ignore-robots` wherever the crawl couldn't judge it
    (`robots-txt-ignored`). It asked for every URL, so none was refused: what robots.txt blocks is skipped. It read
    no other origin's robots.txt: that check is skipped. And it read the site's own only for sitemap discovery, so
    with `--no-sitemaps` as well, whether robots.txt is missing or unavailable is skipped too.
  - Whether the site as a whole is hidden from search is judged by its home page, so a crawl that didn't start at
    the site's root (a start URL with a path other than `/`) skips it (`start-not-root`): a section's first page
    isn't the site's.
  - The duplicate site version check needs to know how every version answered, and a home page to redirect to. A
    crawl stopped before the probes were answered, a crawl file from before the crawler probed, or a home page that
    isn't a page skips it.
  - Near-duplicate content needs every page's whole fingerprint, which engines before it didn't record: a crawl one of
    them made, or continued, skips it (see [Near duplicates](#near-duplicates)).
  - A skipped check stores no rows, whatever its rule found on the way.
  - A cross-page rule that fails once every page is read leaves its checks with no results at all, so they're skipped
    as `rule-failed` rather than shown as run and clean. Each such rule declares the checks it reports
    (`ISiteRule.Checks`), and one that reports any other counts as failed. A rule that fails on single pages loses only
    those pages' results, which the notes count: one crafted page can't switch a check off for the whole site.
- **Not checked isn't broken.** A URL the crawler gave up on without asking ("Not checked", because its host had
  stopped answering) counts as never fetched. Links to it aren't broken, and the notes give the number, as `si stats`
  does (`CrawlStats.NotCheckedCount`). The stats' indexability breakdown leaves such URLs out, and the probes of the
  site's other versions too, as the checks do, so it agrees with the URL summary.
- **As search engines see it.** An instant (0-second) meta refresh is a redirect, as Google treats it. A canonical
  whose target leads back to the page (through a redirect) or nowhere doesn't make the page non-indexable. Links and
  images are judged by where their redirects end, so a link to a redirect that ends at a 404 is broken. Where this
  differs from what the crawl recorded, the analysis stores it in `effective_indexability`, which `si stats` reads.
  A sitemap entry must be on the host of a sitemap that lists it, or on the start URL's or home page's host, so `www`
  pages are the site's own on a crawl that starts at the bare domain. A link without text takes its name the way
  screen readers name it (an image's alt, a labelled icon, `aria-labelledby`, `aria-label`, `title`), and a fragment
  matches an id as written or decoded, as browsers do. The pages of a paginated series (`rel=next/prev`) may share an
  H1; their titles and descriptions must still differ.
- **Other sites.** A status many sites only send crawlers (401, 403, 405, 429, 503, 999) doesn't make an external
  link, canonical, alternate or image "broken".
- **Site versions.** The bare domain and `www`, over http and https, are one site to the crawler. Google treats the
  versions as duplicate URLs and picks one, preferring HTTPS. Each version of the start URL must redirect to the main
  one, the home page's origin.
  - One that answers with a page, directly or at the end of its redirects on another version, is flagged
    `duplicate-site-version` (a Warning). Probes aren't parsed, so a canonical on the other version isn't seen.
  - One that is dead, fails, is blocked or is disallowed isn't: a dead `www` twin is normal.
  - The probes aren't the site's URLs, so the response-code checks leave them out, and so do the robots.txt checks
    for an origin only probes went to. Once the site links there, they count like any URL.
  - The redirect checks still look at them: a version that redirects temporarily, to http, in a loop or to a broken
    URL is a problem.
- **Thresholds.** `AnalysisSettings` holds every limit (titles 200–580 px wide, descriptions 400–985 px, 200
  words, images over 200 KB, pages slower than 1 s, near duplicates within 24 of 256 fingerprint bits...). Each
  analysis stores the values it used.
- **Title and description widths.** The four length checks judge how wide the text is in Google's desktop results,
  in pixels (`Rules/SearchResultWidth`): Arial at 20 px for titles and 14 px for descriptions, by Liberation Sans
  2.1.5's advance widths (metric-compatible with Arial, SIL OFL 1.1), with full-width characters and emoji one em.
  Details read `612 px, over 580 (64 characters)`. The table, `Rules/ArialAdvanceWidths.cs`, is generated: don't edit
  it. Run `node engine/tests/ShoutingIguana.Engine.Tests/Analysis/ArialWidths/generate.mjs
  <path>/LiberationSans-Regular.ttf` from the repo root with the TTF from the 2.1.5 release (`--check` writes
  nothing and fails when the table isn't what the font gives); the font's licence is `Rules/LiberationSans-OFL.txt`,
  beside it. Limits saved in characters by an earlier engine (`titleMaxLength`) are read converted
  (`LegacyAnalysisSettings`). The design as built: [pixel widths](../docs/v2-pixel-widths.md).
- **Atomic.** The issues are replaced in one transaction, so a failed or cancelled analysis keeps the
  previous results.
- **Details name their status codes.** Where a rule writes a status into a detail it carries its standard name: "404
  Not Found", "301 Moved Permanently" (`Rules/HttpStatusNames`, RFC 9110's reason phrases). A code with no standard
  name stays "HTTP 599". The crawl stores its own sentences the same way, through the same helper: a sitemap's
  "404 Not Found", a robots.txt's "503 Service Unavailable" (as the reason of its alert and of the URLs it refused),
  a firewall's "Cloudflare challenge". A crawl an earlier engine stored keeps its words ("HTTP 404", or a 429 stored as
  blocked, "Rate limited (429 Too Many Requests)"),
  which a detail still names when it quotes them; the crawl file is never rewritten.
- **Where to fix it.** Each check says how its URLs stand to the references between URLs
  (`IssueDefinition.References`, not stored): `Sources` when each is something other URLs refer to (a 404, a redirect,
  a URL robots.txt blocks, a sitemap entry), so what's fixed is where it's found; `Targets` when each is the page holding
  the bad references (it links to broken URLs, its canonical redirects), so what's fixed is on it; `None` otherwise.
  13 checks are `Sources`, 17 `Targets` (the list: [the IA](../docs/v2-ui-ia.md#where-its-found-uifound-on)).
  `Storage/FoundOnReader` reads, for up to 100 URLs in one snapshot, where each is found (its links of every kind, the
  pages they're on, the first of them as the inspector's inlinks, the sitemaps listing it, whether it's the start URL)
  and what each flagged page names (every URL in `issue_targets`, with its status and the page's link to it). The
  pages are counted over a URL's first 5,000 links, a floor past them. The report and a check's file say it too
  ([below](#export-and-the-report)).
- **What an issue names.** An issue is one check on one URL, with a short detail and the other URL involved. Where a
  page has several (it links to three broken URLs, shows two large images), `issues` keeps the first and
  `issue_targets` every one, so the Issues screen puts the page in the group of each and the inspector counts each as
  named by it. A duplicate is stored under its cluster's first page, so a cluster is one group. Three checks whose
  detail is each page's own instance of a fault (a relative canonical's path, where a JSON parser stopped) store the
  fault in `issue_faults`, and group by it. The 100k benchmark's 32,406 issues name 19,000 URLs this way, the 1M
  crawl's 323,000 issues 189,000.
- **Progress.** Given an `IProgress<AnalysisProgress>`, an analysis says how far it has got, in order on its own
  thread: reading the URLs, the page rules (pages), the site rules, the URL summary (rows), then writing. Each phase is
  reported as it starts and ends, and at most every 250 ms between.
- **URL summary.** Each analysis rebuilds `url_summary`, the URL grids' table: one row per URL with what the analysis
  judged and the crawl's numbers the grids sort and filter on (see [Crawl file](#crawl-file-sicrawl)). It comes from
  the same URL table the checks use, so the grid and the checks never disagree, and it's written in one streamed pass
  after the issues: about 0.7 s and 14 MB (130 bytes per URL, indexes included) on the 100k crawl.
  `UrlSummaryReader` reads it, only while it describes the crawl (`IsAvailable`): the last analysis stored it with this
  `AnalysisFreshness.VERSION`, as its columns change with it, and the crawl hasn't changed since. A crawl continued
  after its analysis (while it runs, and once it stops again), or analysed by another engine version, has none until
  it's analysed again. An analysis out of date only because the checks or thresholds changed keeps its summary.
- **Out of date.** `AnalysisFreshness.Check` (or `CrawlFileSummary.OutOfDate`) says why an analysis no longer stands:
  - `CrawlChanged`: the crawl row was stamped again (`updated_at`) since the stamp the analysis kept, because the
    crawl was continued. Exact, whatever the clocks did: a stamp only moves forward (`MAX(now, updated_at + 1)`), so a
    clock set back can't repeat the one an analysis kept.
  - `EngineChanged`: the analysis ran with another `AnalysisFreshness.VERSION`, or with other checks (the catalog's
    ids, categories and severities, hashed). Bump `VERSION` with any change to what analysing the same crawl stores: a
    rule that judges differently, a default threshold, a column of the URL summary. Every analysis by an engine from
    before the summary is out of date this way.
  - `SettingsChanged`: the stored settings differ from the ones an analysis would run with now. A threshold the
    analysing engine didn't have takes its default: one that changes what the checks find comes with a new `VERSION`.
    The title and description limits an engine before 12 stored in characters are read in pixels
    (`LegacyAnalysisSettings`: the old default as the new, any other at 9.4 px a title character and 6.2 a
    description's), so an analysis at the old defaults isn't `SettingsChanged`.
  - A score worked out by another `ScoreCalculator.VERSION` is `EngineChanged` too.

Categories: response codes, redirects, links, indexability, canonicals, sitemaps, titles, meta descriptions,
headings, content, images, hreflang, structured data, security, URLs, performance, HTML.

### The score

One number for a site, 0 to 100, to the design's version 1 ([the Simple design](../docs/v2-ui-simple.md#the-score)).
`Analysis/ScoreCalculator` is the one place it is worked out:

- It starts at 100. Each check that found something takes points off by its severity and how much of the site it
  affects: a few (under 1 in 100), some (1 in 100 or more), many (1 in 10 or more) or most (half or more) cost an
  error 2, 4, 7 or 10 points and a warning 1, 2, 3 or 4. A site-wide fault costs 60 (an error) or 4 (a warning)
  whatever its count. A notice costs nothing.
- A page check's count is a share of the pages the analysis read; any other check's, of the URLs crawled.
- While an error check has issues the score can't be above 89. 90 and over is very good, 70 and over good, 50 and
  over needs work, under 50 poor.
- No page read is no score. A crawl that was stopped or failed, or whose URL or depth limit kept internal URLs out,
  gives a partial score: of what was read so far.

**The analysis works it out and stores it** as it commits (`IssueWriter`, the only writer), from the counts it has
just stored, the pages it read and the URLs crawled as it read them, so every reader gives the same number and no
count is ever set against a total from another moment. The `analysis` row keeps the URLs crawled (`crawled_urls`),
the formula's version, the number, whether it is partial, and the whole score as JSON: what it covers, a line for
every check that found something, and the score with the first five problems fixed. `IssueReader.ReadResults` reads
it back (`AnalysisResults.Score`, with `NoScoreReason` and `FixFirst`), and `AnalysisInfo.Score` is its headline, for
a list of crawls. `si score` prints the breakdown.

`Analysis/FixFirstOrder` is the order to fix checks in, for every host: the most severe; within a severity, a fault of
the whole site (`SiteWide`) before any other, those in catalog order among themselves; then the most URLs flagged;
then catalog order. A site-wide fault is one issue, so its count would put it last of its severity: it leads however
small its count. The design's reference code has the same order (`fixFirstOrder` in `score.mjs`).

The tests hold it to the design's tables row by row, and to the design's reference code: `generate.mjs` in
`tests/ShoutingIguana.Engine.Tests/Analysis/ScoreReference` scores 288 sites with
`app/design/simple/build/lib/score.mjs` and writes `cases.json`, which the engine then scores line for line. Run it
again (`node engine/tests/ShoutingIguana.Engine.Tests/Analysis/ScoreReference/generate.mjs`, from the repo root)
whenever the reference or the catalog's checks change. A changed formula is a new `ScoreCalculator.VERSION`; so is a
change to which checks are `SiteWide`, or to a check's `Subject`, which a test catches.

### Near duplicates

`near-duplicate-content` flags indexable pages of 50 words or more whose main content is almost the same as another's.
It compares fingerprints taken when the page was parsed, never the text.

- **Fingerprint.** A 256-bit SimHash of the main content's 3-word shingles: four 64-bit lanes, each hashing the words
  with a seed of its own (`ContentFingerprint`). Lane 0 is the engine's original SimHash (`responses.simhash`); lanes 1
  to 3 have columns of their own (`simhash_1` to `_3`). Exact copies are left to `duplicate-content`.
- **Threshold.** Each bit of two pages' fingerprints differs with probability θ/π, where cos θ is how alike their
  shingles are, so pages d bits apart are about cos(πd/256) alike. They are near copies within
  `NearDuplicateMaxBits`, 24 of 256 by default: about 96% alike, as 6 bits of 64 meant before, measured with a quarter
  of the variance. A one-word edit to a 300-word page is about 12 bits. Pages 98% alike are found 98% of the time, and
  96% alike half the time.

  | Bits apart, of 256 | 8 | 16 | 24 | 32 | 64 | 128 |
  | --- | --- | --- | --- | --- | --- | --- |
  | About this alike | 99.5% | 98% | 96% | 92% | 71% | Unrelated |

  The threshold was `nearDuplicateMaxDistance`, in bits of 64, until this engine. It has a new name so that an
  analysis's stored 6 is never read as 6 of 256: such settings read as the default.
- **Precision.** Text shared through a template makes pages alike too. The chance that two pages come within 24 bits,
  by how much of their text they share, and the pairs that makes on a site whose every page shares that much. For any
  one template the rate is usually far lower, so these are ceilings:

  | Pages share | One pair in | Pairs at 100k pages | At 1M | At 10M | The 64-bit check: one pair in |
  | --- | --- | --- | --- | --- | --- |
  | Nothing | 3×10^43 | 0 | 0 | 0 | 2×10^11 (2 pairs at 1M) |
  | 50% | 5×10^18 | 0 | 0 | 0.00001 | 130,000 |
  | 60% | 2×10^14 | 0 | 0.002 | 0.2 | 10,000 |
  | 70% | 10^10 | 0.4 | 44 | 4,400 | 800 |
  | 80% | 800,000 | 6,500 | 650,000 | 65 million | 70 |

  Through the real fingerprint and rule, on generated 400-word pages: nothing was flagged up to 70% shared at 1M
  pages, nor were 50 one-word edits among them missed; at 80% shared, 3,664 of 100k pages were flagged, being 80%
  alike. Between about 70% and 96% alike the estimate can't always tell, on a large enough site.
- **Finding every pair.** Pages within 24 bits of 256 are within 6 bits of 64 in at least one lane, and a lane within 6
  bits agrees exactly on 2 of its 8 blocks of 8 bits. So the pages are counting-sorted by each pair of blocks of each
  lane in turn (28 pairs × 4 lanes: 112 keys of 16 bits, holding one key's arrays at a time), compared within a
  bucket when that lane is within 6 bits, and joined into groups when all 256 bits are within 24. Every pair within the
  threshold shares a bucket, whatever the crawl's size or order. Unrelated pages share a key one time in 65,536, about
  15 to a bucket at 1M pages and 150 at 10M. Another threshold k uses k / 4 bits per lane and k / 4 + 2 blocks.
- **Dense clusters.** A bucket fuller than unrelated pages ever make one (more than 128 pages, or past 2 million pages
  four times the average) holds pages that share much of their text: near copies of one another, or a heavy template.
  Its pages are sorted by the lane and each is compared with the next 64 (half the limit), and a bucket whose pages are
  one group already is passed over. Near copies there still form one group, each joined through its neighbours. A
  near copy could be missed only if every key it shares is that crowded and puts it further away. So the search makes
  at most 112 × pages × max(64, 2 × pages / 65,536) comparisons; 100,000 copies of one page take 0.2 s.
- **Cost.** About 70 bytes a page while it runs, and 30 more a page in the crawl file. At 1M pages the search takes
  about 2 s on unrelated content, and 6 to 13 s when 30% to 70% of every page is one template (one thread). Analysing
  the 1M-page synthetic site's crawl took 78–80 s, against 84–88 s for the single-lane check before it, on the same
  file. The four lanes cost the parse 28 µs a page, less than the one lane did before, now that a shingle's weights are
  added eight bits at a time.
- **Crawls without the whole fingerprint.** Engines before this one recorded lane 0 only. A crawl one of them made, or
  pages one fetched while continuing a crawl, leave the check skipped (`earlier-version`), with a note: "This crawl was
  made by an earlier version, which didn't record what this check needs. Crawl again to check it." It is skipped for
  any such page, not run on the rest, as any page could be the near copy of one the check can't compare.

## Crawl file (`.sicrawl`)

One SQLite file per crawl. It is a single self-contained file once the crawl closes.

- **One writer at a time.** A crawl or an analysis holds `<crawl file>-lock` while it writes. A second one is
  refused, and an analysis also refuses a crawl that is running or was interrupted. Readers can open the file at
  any time, a running crawl included. Windows deletes the lock file with the lock. On Linux and macOS it stays:
  deleting it there would let two writers lock different files. While someone is part-way through reading a closed
  crawl (a single file, in rollback-journal mode, so it can be copied and read from anywhere), a writer can't switch
  it to WAL: it gives up as readers do, after a couple of seconds, with a `CrawlFileInUseException` saying the file is
  being read, rather than after Microsoft.Data.Sqlite's 30 s.
- **The write-ahead log stays small while a crawl runs.** A running crawl's file is in WAL mode, and SQLite starts
  the log again from its beginning only at a moment when no reader is part-way through it. With nobody reading that
  is every batch (the log peaks at the largest batch: 38 MB on the 100,000-page benchmark). With the links grid read
  all the while, each read a pass over the links, that moment never came, and the log grew by everything the crawl
  wrote, to more than the file. So once the log passes 64 MB (`CrawlDatabase.LOG_LIMIT_BYTES`) the writer folds it
  into the file between two batches and empties it (`CrawlDatabase.FoldLog`, a `TRUNCATE` checkpoint): the grids' new
  reads of that file wait while those in flight end by themselves, for two seconds at most, the log is folded, and
  they read on (`CrawlReadGate.ExclusiveOnceIdle`). No read is stopped, fails or answers differently; a read can wait.
  The writer waits with them and stores nothing meanwhile, so the log is held to the limit and the batch that took it
  past. A fold that couldn't be done (a read that outlasted the two seconds, or another program part-way through the
  file, which is waited for a quarter of a second) is tried again a second later, then twice as long after each one
  that fails, to half a minute: only then can the log grow past its bound, and it's folded when the crawl closes as
  ever. The writer says once, as it closes, how often it folded the log and how long the crawl waited for it.
- **Read back with today's defaults.** Options and thresholds are stored as JSON. One that the engine which stored
  them didn't have reads as its default (`StoredJson`), never 0 or false, so a new option can't change how a resumed
  crawl runs or what an analysis checks.
- **No secrets.** People share crawl files, so the stored options keep header names without their values, and the
  proxy without its credentials. Resuming asks for them again.

| Table | Holds |
| --- | --- |
| `crawl` | Start URL, options (so a resume behaves the same), status, engine version |
| `urls` | Every URL seen once: scope, depth, crawl state, why it was skipped, where it was found (`source` 3 marks a probe of the site's other versions) |
| `responses` | One row per fetched URL: status, timings, sizes, title, description, H1, canonical, indexability, content hashes, and the four lanes of the content fingerprint (`simhash`, `simhash_1` to `_3`) |
| `response_details` | Brotli-compressed JSON of the headers and full `PageFacts` (headings, meta tags, images, hreflang, JSON-LD, ids) |
| `response_html` | Compressed HTML, only with `--store-html` |
| `links` | Every link: source, target, kind (link/image/script/canonical/hreflang/redirect...), rel flags, page region, anchor text |
| `robots`, `sitemaps`, `sitemap_urls` | robots.txt per origin, sitemaps read (under the URL they were found at, which may redirect elsewhere; `is_probe` marks a guess at `/sitemap.xml` when nothing declared one; the first one left unread, past 1,000 sitemaps or nested more than 3 deep, has an error starting "Not read"), and which URLs each listed |
| `favicon` | The site's icon, fetched once near the crawl's start: one row at most, the image's bytes as the site sent them, where they came from and when, with the media type and size the engine read from the bytes ([below](#the-sites-icon)) |
| `notices` | What the crawl told the person running it, every run's, in order: an id numbered across runs, when, the level as text, a stable code, the message, and JSON of the facts it's about (host, origin, URL, reason, counts, a duration) |
| `run_time` | The time the crawl spent crawling: a row for each stretch of it, from a run's start (or its resuming after a pause) to its next pause or its end, with the run it belongs to, when it began, how long it went on, whether it has ended, and the crawl row's stamp it was stored under |
| `issues` | One row per flagged URL and issue: a short detail, and the other URL involved (broken target, duplicate, canonical): the first, where there are several |
| `issue_targets` | Every other URL each issue is about (each broken target of a page's links, the canonical, a duplicate cluster's first page), which the Issues screen groups by and the inspector's "named by" counts; `0` stands for no other URL |
| `issue_faults` | The fault an issue of `canonical-relative`, `canonical-invalid` or `structured-data-invalid` is an instance of, which its groups are by |
| `issue_types` | Every check that ran: its title, category, severity, description, fix, and how many URLs it flagged |
| `analysis`, `analysis_notes` | When the last analysis ran, with which engine version and settings, whether it was partial, what it ran against (the crawl's `updated_at`, the analysis version, the checks' fingerprint), the URLs crawled as it read them, its [score](#the-score) (the formula's version, the number, whether it is partial, and the whole of it as JSON), and what it couldn't do and why |
| `skipped_checks` | Each check the last analysis skipped, with a reason code and a sentence |
| `effective_indexability` | Indexability as analysed, for URLs where it differs from the crawl's (an instant meta refresh, a canonical leading back to the page) |
| `link_summary`, `link_blocks`, `link_summary_state` | The links counted for the links grid, by the first analysis of the crawl as it stands: by target, and by block of 16,384 consecutive link ids, each by kind, position, flags, whether the link leads to its own page, has text, and sits on a nofollow page; and the crawl row's stamp they were counted under, with the number of links, their highest id, the size of a block and the version of the counting |
| `url_summary` | One row per URL for the grids, rebuilt by each analysis: how it was found, indexable or not and why, where redirects end and the status there, the type (page, image, script, stylesheet, font, media, PDF, other), in a sitemap, inlinks (all, followed, nofollow, linking pages), outlinks, issues by severity, the first title's and meta description's characters (`title_length`, `description_length`) and their widths in pixels as the checks measure them (`title_width`, `description_width`), and copies of the URL's depth, status, size, time and word count |

URLs that only appear in sitemaps keep depth `1000000` (`CrawlDepth.UNLINKED`) in `urls`, which is how orphan pages
show up. The URL summary has no depth for them.

The analysis tables are created when a crawl is first analysed, so a crawl file from an earlier engine opens as it
always did and gets what it lacks on its next analysis. That first analysis also adds an index of the links by target
and kind, `ix_links_to_kind`, which the inspector's links of some kinds read (a crawl continued since keeps it up to
date). It's built after the analysis commits, sorting in four threads with about 100 MB of memory and spilling to
temporary files. It costs 13 s and 0.66 GB (15%) on the 1M crawl's 50M links (85 s to analyse it becomes 98 s), and
about 2 s and 50 MB on the 100k benchmark, whose analysis then peaks at 278 MB rather than 195 MB.

The analysis's tables carry on growing the same way: `link_summary` and `link_blocks` (below), and an index of
`issue_targets` by the URL an issue is on (`ix_issue_targets_url`), which the inspector reads to say which group an
issue is in. A file analysed before them reads without them, and gets them when it's analysed again.

`notices` is created whenever the crawl writer opens a file,
so an earlier engine's crawl gets it when it's continued, and reads as having none until then. `run_time` is created
the same way ([below](#time-spent-crawling)), and so is `favicon` ([below](#the-sites-icon)). The writer adds the
fingerprint's `responses.simhash_1` to `_3` the same way: pages fetched before then, and any an earlier engine fetches
into a newer file, have none. And `sitemaps.listed_sitemaps` (E6): how many sitemaps a sitemap's own file lists, as
the crawl read it, so an index says how many it names beside how many of them the crawl has; null for a file whose
body was never read, and for every sitemap an earlier engine read. The crawl's own schema version doesn't change, so either engine reads and continues
either file. Files are shared, so a notice another program wrote oddly is read as well as it can be (one
without a code or a message is left out, a missing level reads as a warning and a missing time as the crawl's start),
and a `notices` table of another shape reads as none; the writer won't continue a crawl whose table it couldn't store
a notice in, and says so.

### The site's icon

A crawl fetches the site's icon once and keeps it in its file, for the app's library, top bar and sidebar
(`Favicons/`; [the API doc](../docs/v2-sidecar-api.md#favicon-e6) has the contract).

- **Whose page names it.** The first page of the site that a run parses: in a new crawl, the start URL's page or the
  page it redirects to, as nothing else is known until it has been read. The parser keeps that page's
  `<link rel="icon">` elements as facts (`PageFacts.Icons`: URL, `sizes`, `type`), which aren't stored: the crawl file
  holds what it held, and the links table has each icon as a link of kind `icon`, as before.
- **Which is tried** (`FaviconCandidates`): the smallest the page declares as 64 px or more (a 32 px tile on a dense
  screen), else the largest it declares below that, then those with no size, in page order; three of the page's at
  most, then `/favicon.ico` at the page's origin. An SVG is never asked for.
- **What is kept** (`FaviconSniffer`): the first candidate whose bytes are a PNG, an ICO, a GIF, a JPEG or a WebP, by
  its signature, with a size its header gives of at most 1,024 px a side, in at most 512 KB. Whatever the site calls
  it. An ICO's directory must hold up: every image it lists inside the file, the largest a PNG or a bitmap.
- **How it's asked for** (`FaviconFetcher`): beside the crawl, as a job of its own. Each request waits for its
  origin's robots.txt and obeys it when the crawl does, takes its turn at the host's gate, stops while the crawl is
  paused, has the crawl's request timeout, and goes to another site only when the crawl checks other sites' links. It
  is no URL of the crawl: nothing is discovered, recorded or counted for it, and a host that fails it is not a site
  that is down. The crawl never hears of its failures. A crawl that completes first gives it 5 s; one that is
  stopped, or fails, none.
- **Where it's kept**: `favicon`, one row, written by `CrawlWriter` like everything a crawl stores, the first one
  offered and no other. A table of another shape is another program's: no icon is then fetched or kept, and the crawl
  goes on. A crawl that has its icon asks for none when it's continued; one that has none looks once more, from the
  first page that run parses.
- **How it's read** (`FaviconReader`): a file is trusted no more than the site was. At most 512 KB are read, and the
  bytes are told to be an image again, so what a reader gives out is an image the engine keeps, under the type its
  bytes are, or nothing. `CrawlSummaryReader` says whether a file has one (`HasFavicon`), in two lookups.

### Time spent crawling

How long a crawl took isn't its end less its start: it may have been paused, or stopped and continued a day later.
`run_time` keeps the time it spent crawling, a stretch at a time, and `CrawlRunReader` reads it back as the crawl's
runs, each with its stretches, and their total (`CrawlFileSummary.TimeCrawling`, `CrawlHistory.Runs`; live,
`CrawlProgress.TimeCrawling`, which a run ends on to the millisecond the file has).

- **A stretch** begins when a run starts or resumes after a pause, and ends when it's paused or the run ends, so paused
  time and the time between runs are in none. The session times each by a clock that only runs forward (`RunClock`)
  and hands it to the writer as it begins, as it ends, and every five seconds between, so a run cut short (the process
  died) loses at most those: its last stretch is left going, read as ending where it was last stored, and ended there
  when the crawl is continued. Nothing is written per response, and no stamp moves: a pause costs one small row.
- **Known whole, or not known.** The total is given only when every run was timed. A file from before the table gets
  it when it's continued, with one row without a time standing for all its earlier runs; and since every run stamps
  the crawl row (`updated_at`) whichever engine makes it, the last stretch stored takes the stamp with it whenever this
  engine moves it (as a run opens the file, in the same commit, so the time stays known while a big crawl is read back
  in; and as the run ends): when the last stretch's stamp isn't the crawl row's own, an engine that keeps no time ran
  the crawl since, and the next run here adds the row without a time for it. With such a row, or such a stamp, the crawl's time reads as not known, for good: never a part
  of it given for the whole. A table of another shape (another program's) is neither written to nor read, and times
  that can't be true (negative, ten years long) read as not known.
- **A crawl that hasn't crawled has spent no time.** A run can end without a stretch (paused before it began and
  then stopped, or its process died before its first batch was stored). A stretch is stored with the first batch that
  stores a response, so a file with no stretch and no response has been crawled by nobody: continuing it adds no row
  without a time, and its time is known from zero. A file with responses and no stretch was crawled by an engine
  that keeps no time, and gets the row.
- **In order.** A stretch begins by the wall clock and lasts by the clock that only runs forward, so a wall clock set
  back between two (a time sync) would have the later begin before the earlier ended. `CrawlRunReader` gives each as
  beginning no earlier than the one before it ended, in one run or across two, each with its length: none overlaps,
  no run ends before it started, and the total is as stored.
- **Time asleep isn't crawling.** The clock that only runs forward is `Stopwatch`'s. On Windows it's
  `QueryPerformanceCounter`, which counts the time a machine spends in standby or hibernation; on Linux
  (`CLOCK_MONOTONIC`) and macOS (`CLOCK_UPTIME_RAW`) it stops while the machine sleeps (dotnet/runtime#77945). The
  session reads the run's clock twice a second while it crawls, so `RunClock` takes a gap of more than five minutes
  between two readings for the machine asleep (or the process held still) and leaves it out of the stretch, on any
  system. A sleep shorter than that still counts on Windows, and the stretch it's left out of then spans more wall
  time than its length says: it isn't split in two.

### URL summary

What its columns mean:

- **Null is unknown**:
  - every value the crawl would have given, for a URL it never fetched, or never asked for because its host had
    stopped answering;
  - indexability and outlinks of a page whose HTML was never read, being only status-checked (outside the start
    folder, an iframe's, another site's), unless its response alone rules it out (an X-Robots-Tag noindex, an error,
    a redirect);
  - `in_sitemap` for a URL no sitemap lists, unless every sitemap was read (the not-in-sitemap check ran);
  - `depth` of a URL only a sitemap lists.
- **Source** is how the crawl found the URL (`DiscoverySource`). 3 is the crawler's probe of the site's other versions,
  which nothing on the site leads to: the grids hide or label it, and the stats leave it out, as the checks do.
- **Indexability** is the analysis's (`effective_indexability` over the crawl's): `indexable` is 1 when the URL
  answered 2xx and nothing keeps it out of search, otherwise `indexability_reason` says why. Every page called
  indexable had its HTML read, so `si stats` and the summary count the same indexable pages.
- **Inlinks** are hyperlinks to the URL on the pages audited, each one counted, a page's links to itself left out.
  Nofollow ones are marked rel=nofollow, or are on a page whose robots directives say nofollow. `linking_pages`
  counts the pages.
  Nothing is credited through redirects: these are the links as found. `outlinks` is every hyperlink on a page whose
  HTML was read.
- **Type** is by the content type of a successful response, otherwise by the extension, as the checks tell files from
  pages.

Nine indexes serve the grids' sorts and selective filters: status, indexability reason, type, depth, linking pages,
issues, response time, size and word count. Every index ends with the URL id, which keyset paging needs. The rest
are cheap to scan in a table this narrow.

Measured on the 100k crawl's summary, and on 1M rows made by copying it nine times, at p95, for the first page of
100 rows and for a page half way down (with SQLite 3.49; the engine ships 3.53):

| Query | 100k | 1M |
| --- | --- | --- |
| Sorted by an indexed column, a common filter kept off its index (slowest pages, thinnest pages, most issues) | under 1 ms | under 3 ms |
| A selective filter's index, then sorted (4xx by linking pages, status 500 by response time) | about 1 ms | 6–22 ms |
| Sorted by a column without an index (followed inlinks, outlinks) | 13–24 ms | 100–210 ms |
| Counting the rows a filter matches | up to 15 ms | up to 130 ms |
| Overview breakdowns (indexability, depth, status, response times) | 21–40 ms | 200–390 ms |
| Left to the planner: a common value's index, then sorted (slowest internal pages) | 12–25 ms | 110–330 ms |
| Left to the planner: a rare match found by walking another index (over 500 ms, by size) | 110–220 ms | 1.2–2.0 s |

This SQLite has no STAT4 histograms, so its planner can't tell a common value from a rare one, and stats from
`ANALYZE` didn't change that. So the grids' readers count the matching rows first (the grid shows that count anyway)
and choose. When few rows match, they sort them, keeping the sort's index out of the plan (`ORDER BY +size_bytes`).
Otherwise they walk the sort's index, keeping a common value's index out (`+resource_type = 0`). Both brought the last
two rows under 1 ms.

### Link summary

A crawl stores some 50 links a page, so the links grid can't count them, nor find a page among them, by reading
them: a pass over the 100k benchmark's 5 million takes 0.3 s at best, and a million pages' 50 million ten times that.
The analysis of a crawl therefore counts them once, in one pass, into two small tables, both keyed by what a link is
(kind, position, flags, self, has text, nofollow page):

- `link_summary`, by target: 216,000 rows for the 100k crawl (2 a URL), 3.8 MB. From it come the count of any filter
  on what a link is and on its target, the facets, each target's matching links (which a sort by the target adds up
  to find a page), and the targets a filter's links lead to.
- `link_blocks`, by block of 16,384 consecutive link ids: 3,400 rows, 70 KB. It says where in crawl order a filter's
  links are, so a page anywhere is read from the blocks that hold it. The size of a block is stored with the counts
  (`link_summary_state.block_shift`) and a read goes by the file's, so the size can change without a file being read
  wrongly.

They're counted in hash tables by a function SQLite calls per link, then stored in key order: grouping the links in
SQL sorts every one of them, 18 s on the 100k crawl. The pass takes 1.5 s there (5 million links); what its tables
add to the analysis's peak is inside the spread between runs ([below](#synthetic-site-and-benchmark)). They count the links and which pages say nofollow, which only a run of the crawl
changes, so `link_summary_state` keeps the crawl row's stamp (`updated_at`) they were counted under: an analysis
finds them standing and leaves them (analysing again, with other thresholds or a newer engine's checks, doesn't pay
for the pass again), an engine from before them analysing the file doesn't make them wrong, and once a run of the
crawl has moved the stamp on the readers pass them over until the next analysis counts afresh. Tables of another
shape (a later engine's) are passed over too, and built again. `AnalysisFreshness.VERSION` didn't change for them:
nothing an analysis already stored did, and the links grid reads without them, only slower.

A summary is trusted in one place (`LinkSummaryBuilder.ReadCurrent`), which takes it only when:

- its tables have this engine's columns, and `link_summary_state.version` is this engine's
  (`CrawlSchema.LINK_SUMMARY_VERSION`): the version says how the links were counted, so a fix to the counting that
  keeps the columns moves it on, and the next analysis counts again though the crawl hasn't changed. A summary
  without a version is one from before it, and is counted again too;
- its stamp is the crawl row's;
- its numbers can be true of the file, as far as that costs nothing: the highest link id it gives is the links' own,
  and the count is no more than the ids from the lowest to the highest, nor than a file of this size can hold (a
  stored link takes 14 bytes at the least), with none only where there are no links. The grid sizes what it keeps by
  those two numbers, so a file's word for them isn't enough: changed by hand to a billion, they made each filter
  set's ids 128 MB.

## Grid reads

The desktop app's grids read a crawl file through three readers in `Storage/`. Each read opens the file as readers do
(read-write, with `query_only` on, so it works in WAL mode) and holds one snapshot for that one page. Every reader
method takes a `CancellationToken`, which stops the read within milliseconds.

- `UrlGridReader`: a page of URLs with the columns a view shows (`Query`), the counts behind the filter chips and
  Overview's breakdowns (`Facets`), and a URL found by its text (`Lookup`).
- `UrlInspectorReader`: one URL in full (`Read`), its links in and out (`ReadLinks`), and its stored HTML
  (`ReadSource`).
- `IssueGroupReader`: a check's issues grouped by their cause (`ReadGroups`), and its rows as a URL grid
  (`ReadRows`).

`FoundOnReader` (`ReadFoundOn`, `ReadTargets`) reads where a page of a check's URLs is found, and what its pages name,
for the Issues screen's rows, Simple's problem page, the report and a check's file ([above](#seo-analysis)).

Three more read what the Links, Sitemaps and robots.txt screens show (see [below](#links-sitemaps-and-robotstxt)):
`LinkGridReader`, `SitemapListReader` (with `UrlGridReader.QuerySitemap`) and `RobotsReader`. `CrawlStatsReader.Read`,
the crawl's headline numbers (`si stats`, the API's `/stats`), reads the same way: one snapshot, stopped by its token,
and kept in the `ReadCache` it's given while the crawl stands still.

**Search as it's typed** (E6) is `UrlSearchReader.Search`, for the app's palette: the best few of a crawl's URLs whose
URL or title holds the text, the page a person means first (the text is the URL or its path; then it starts the host
or a segment of the path, never in the scheme, the query string or the fragment, or follows a character in the title
that isn't a letter or a digit; then anywhere). Nothing is stored for it. An FTS5 index with a trigram
tokenizer was measured on the 100k crawl and left out: with its content it adds 44 to 48 MB to a 479 MB file (9 to
10%) and 2.0 s to an analysis of 11.8 s (or 2.7 s spread over the crawl); without, 9.4 MB and 1.4 s; it answers rare
text of three characters or more in 0.1 to 2 ms, but a common word in 28 ms (81 ms with the text that ranking
needs), and one or two characters not at all. Instead the first search of a crawl as it stands reads its URLs and
titles into memory, once (`SearchText`: about 106 bytes a URL, the letters lowercased, a bit a letter for the
capitals), the `ReadCache` keeps it (two crawls': `TEXT_CAPACITY`; a running crawl's for 2 s), and every keystroke
after is a pass over that text: 1.3 to 4.5 ms at p95 at 100k whatever is typed, against the palette's 50 ms. The
first search, which reads the text, takes 55 to 89 ms (49 to 78 at p50, five runs), which is why an empty text is a search too:
the palette asks it as it opens. What matches is what the grid's `search` matches, so the count is the grid's. **The
kept text isn't let go when a crawl sits idle**: a crawl of a million URLs holds about 110 MB in the sidecar until
two other crawls are searched; that, and a search of a million URLs, weren't measured. The grid's own `search`
stayed the scan it was: 26 to 50 ms at p95 for a first page at 100k, one letter or forty characters.

A query (`UrlQuery`) has typed filters per column, up to three sort keys with unknown values last, a quick search of
the URL and title, the columns to read, and `offset` plus `limit` (up to 1,000) to reach any row, or the `after` cursor
a page gives to read on. `LinkEnds` (E6) narrows it to the URLs at one end of the links a view of the links grid
matches ([below](#links-sitemaps-and-robotstxt)). The API, with every column and operator, is in
[`docs/v2-sidecar-api.md`](../docs/v2-sidecar-api.md#urls-e3a).

- **With or without the URL summary.** While a summary describes the crawl, sorts and filters read it. Without one (a
  crawl running, never analysed, or continued since), they read the crawl's own tables, and a filter or sort on a
  column only the summary has throws `NeedsAnalysisException` with the reason and the columns, rather than being
  ignored. The `check` filter needs only the last analysis's issues, as a check's rows do. A query that can't run
  throws `UrlQueryException`, its messages keyed by field.
- **Choosing a plan.** SQLite can't tell a rare value from a common one here (no STAT4), so `GridPlanner` counts each
  condition that could drive the query, up to 50,000 rows, and keeps the count. A rare condition (under 50,000 rows
  and under a tenth of the URLs) drives through its index and its rows are sorted; otherwise the sort's index is
  walked; otherwise (a sort without an index) the summary is scanned and sorted. A unary plus keeps the other
  conditions off their indexes (`+s.status_code = 404`). A page past half way is read backwards from the far end, and
  a page far from both ends of a sort without an index is picked out in memory: the matching rows' keys are kept in
  one scan, the page's first and last rows found in place, and only the page sorted, which takes a third of the time
  SQLite's sorter takes to sort every row up to it.
- **Text conditions are read once into sets.** A condition on the crawl's own tables (a title, a URL's text, a
  regular expression, a search, a check's URLs) is read into the set of URL ids it matches (`UrlIdSet`: a bit per
  URL, 140 KB a million), kept per filter set. All the conditions on one table (a title and an H1 count, say) are read
  in one scan of it. The set gives the condition's count, so no count scans again, and the counts of several sets are
  bit operations. A set of up to 50,000 ids can drive the plan through them (`json_each`); a larger one is tested row
  by row (`si_ids`, a function at about 60 ns a call), which a walk of the sort's index stops on once its page is full:
  half way down a million rows, 25 ms. Functions the SQL calls for every row (`si_ids`, `si_regex`,
  `si_resource_type`, and those that fill sets and tallies) go through SQLite's own interface, which costs a third of
  what Microsoft.Data.Sqlite's wrapper does a call.
- **Regular expressions** run in .NET's non-backtracking mode, each match stopped after 100 ms and a read's matches
  after 3 s in all: nested counted repeats (`((.{0,9}){0,9}){0,9}`) build an automaton that can take minutes on one
  URL, so such a pattern is refused as too complex for the crawl (and kept refused while the crawl stands still).
- **Stopping a read.** Cancelling a read's token interrupts the statement it's running (`sqlite3_interrupt`), and
  each statement checks it before it starts, so an abandoned read frees its core within milliseconds, a regular
  expression part-way through its rows included.
- **Writers here never wait on a grid.** A closed crawl is a single file in rollback-journal mode, and a read holds its
  shared lock until it ends, which would keep a writer's switch to WAL mode waiting, then refused. So a writer of this
  process about to switch (an analysis, a continue, a crawl closing back to one file) stops new grid reads of that
  file, stops those in flight, switches, and lets them read again (`CrawlReadGate`): it waits milliseconds. Another
  program reading the file isn't seen, and can still keep it out.
- **A running crawl's writer waits on them once in a while, and they on it.** To fold its write-ahead log
  ([above](#crawl-file-sicrawl)) it needs a moment with no read part-way through the file, and nothing forces that
  one: it holds new reads of the file, lets those in flight end by themselves, folds, and lets the held ones go.
  Stopping the reads instead and reading them again was measured against it and dropped: it threw work away (with
  six clients on the links grid of a 40,000-page crawl the slowest read took up to 1.95 s, where it takes 1.1 s
  this way or with no fold at all), and it needed a rule for leaving reads alone long enough to end.
- **Cursors** carry the last row's URL id and a hash of its sort values, with a hash of the query: under 100
  characters, whatever the row holds. Reading on reads the row's sort values again in the new snapshot, and a row
  that has gone or changed since is refused (start from an offset again) rather than read on from a place that has
  moved. They read on as ranges of the first key's index, its unknown values a range of their own: reading on costs a
  page however deep.
- **Counts** (matching rows, facets, a check's groups, id sets) are kept in a `ReadCache` by crawl file, crawl stamp,
  analysis and conditions, so scrolling never counts again; a running crawl's for 2 s, then counted again. Requests
  that want what another is counting wait for that count, and count it themselves only if its read stops. Past its
  capacity, the entries used least recently go; a long key is kept by its hash.
- **Facets.** Filtered, or without the URL summary, three facets or more are counted in one pass: each row's values
  are packed into one number, counted as the rows are read (`si_tally`) rather than grouped by SQLite, which sorts
  every row to group them. Otherwise each facet is counted on its own, most of them from their own index.
- **Values this engine doesn't know** (a newer engine's link kind or outcome, a crafted file's numbers) read as
  unknown, null, in every reader, never as an error.
- **Issue groups.** Each check says how its issues group (`IssueDefinition.Grouping`): by detail and the other URL
  (`PageDetail`, `ResponseDetail`), by the other URL alone (`RelatedUrl`), or not at all (`None`, as its detail is a
  measurement). A check grouped by the other URL is grouped by every URL its issues name (`issue_targets`, keyed by
  check and URL named), so a page linking to three broken URLs is in three groups, and a group's share is of the
  check's pages, so the shares can add up to more than the whole. A check whose detail is each page's own instance of
  a fault (`IssueDefinition.FaultOf`) is grouped by the fault (`issue_faults`). A group is a likely template fault when
  it holds at least half of the check's URLs, and at least 10, and they share the other URL or the detail names a
  fault in the page's own HTML. The rest are grouped on the index `ix_issues_group`, on
  `issues (issue, detail, related_url_id)`. A crawl analysed by an earlier engine is grouped as it was.
- **"Named by"**, the issues on other URLs naming one, is read on `issue_targets`' index by the URL named, so it costs
  what that URL is named by, not every issue in the crawl.
- **A URL's links in all** (`UrlDetail.LinkTotals`: every kind, to it and from it, its links to itself left out) are
  counted once and kept, for the inspector and for its lists of links alike: the links to it from the link summary
  where that counts the crawl as it stands, else on the links' index by target. Each link row, and each URL the
  inspector names, carries what became of the request for it (`UrlOutcome`) beside its status, which is null for a
  URL never fetched and for one that gave no HTTP answer alike.
- **Links of some kinds to a URL** (the hyperlinks to a nav target, a stylesheet's links) are counted on the analysis's
  index of the links by target and kind, and a page of them anywhere is found there too: each kind's in crawl order,
  merged as they're read, so no link is read to be skipped. Links from a page are few, and read as they are.
- **Lookup** takes a path on the origin the start URL's redirects end at, as a crawl from `example.com` lives on
  `https://www.example.com/`, takes text without a scheme, and finds a URL the crawl has only on another of the site's
  versions (www or not, http or https), saying what was asked for.
- **The inspector's "Indexable, and why"** lists four tests in the order they decide it (it answered 2xx, robots.txt
  allowed it, no noindex, its canonical is itself), each passed, failed or not checked, beside the analysis's verdict,
  and follows redirects hop by hop as the analysis does, instant meta refreshes included, up to 20.

### Links, sitemaps and robots.txt

- **`LinkGridReader`** (`Query`, `Facets`, `Columns`): every stored link a row, in the order the crawl stored them.
  A link's own columns say what it is (kind, text, position, the rel flags, whether it's followed); the URL grid's
  columns for either end come as `from.<column>` and `to.<column>`, and a filter on them is compiled by the URL grid's
  own compiler into the set of URL ids it matches, which each link's end is tested against: one meaning for every
  column, in both grids. It sorts by the link's id, or by a column of the target, which keeps a target's links
  together.
  - **A page is read the cheapest way its filters allow**, each bounded. No filter: the ids are the places. Filters
    on what a link is: the blocks that hold the page, found by adding up `link_blocks`' counts, at most a block a link
    of the page. Filters on the target go by the links' index by target (`ix_links_to`, or `ix_links_to_kind` when
    one kind of link is asked for too), where each target's links are in crawl order:
    - through the targets, all their links read once and kept, when they're at most 20,000 with few links in all,
      which `link_summary` says before anything is read;
    - a page at a time when they have many (a menu's few dozen targets hold half a crawl's links): the index counts
      the targets' links between two ids without reading them, so the id the page starts after is closed in on
      (first where an even spread would put it, then by halves) and the page read from the ids after it. No link is
      read to be passed over, at any depth, and nothing is kept;
    - more targets than that: a walk from the nearer end when the page is near one; deeper in, their links' ids read
      through them once, on the index, a bit per link (`LinkIdSet`), and kept.

    One target named outright is walked on the index at any depth, whatever else is asked.
  - **A filter on the target that most of the links it could match pass** (the links to the site's own URLs, the
    navigation links to the pages a click from the start) is read from the blocks too, deep in. `link_blocks` counts
    links by what they are, so it counts the links the rest of the filters match; the others among those lead to the
    targets that fail, are the fewer, and are read once through those targets (an index entry each when a kind is all
    that's asked of a link, else the rows of the links to those targets, when the summary says they're at most an
    eighth of the crawl's). A block's matching links are the difference (`ButTheOthers`), and the page is read from
    the blocks that hold it: nothing per matching link, nothing kept but the blocks' counts, and link ids missing
    change nothing, where keeping every id but the others' (`AllBut`, which is what's left when the others are too
    many) needs a crawl with none missing.
  - **A filter on the target with something the index doesn't hold** (a link's position, its flags, more than one
    kind) that the fewer links pass: through the targets when they have few links in all; otherwise a walk of the
    links from the nearer end when the page is near one, remembered when it ran long (a filter on deep targets has
    its links bunched on the pages crawled last); otherwise a set of the matching ids read once in one pass over the
    links, and kept.
  - **A sort by the target** puts the targets with matching links in order (every URL's sort value is read once), adds
    up their counts to find the page's first target, and reads each target's links in crawl order on the index, from
    the nearer end of a target with many.
  - **Without the link summary** (a crawl running, never analysed, continued since, or analysed before it), and for
    a filter it doesn't hold (the link's text or fragment, the page it's on, a search), one pass over the links gives
    the count, the facets and the set of ids at once, kept per filter set. Filters that name few source pages read
    only their links, through `ix_links_from`: a page's links are stored together.
  - **That pass is split over connections** (E6) for a crawl that stands still and has 500,000 links or more, when
    the host lends them (`ReadHelpers`, given on the `ReadCache`; the sidecar lends the read slots free beside the
    read's own, never the last one: `ReadSlots`). The links are divided by id into as many stretches as there are
    connections, eight at most (`PassHelpers`); each is read on a thread and a connection of its own
    (`GridReadContext.OpenAlongside`: the read's rules, and stopped with it), and what each kept and counted is put
    together (`LinkIdSet.Merge`, `LinkTally.Merge`). The answer is the one connection's, to the link; a part that
    finds the file changed under it has the pass run again on the read's own connection. A running crawl's pass is
    never split: it stays on the one connection and the one snapshot that are the moment it answers for, so the time
    its log goes unfolded is what it was. Nothing is stored for it, and with no helper free the pass is as it was.
  - **`Ends`** (E6) lists the URLs at one end of the links a view matches, each once with its links, in URL id order:
    the view's targets, or the pages its links are on. The targets come from the tally the grid has of the view (the
    link summary's rows, or the pass that gave the view its count); the pages take a pass of their own, counted by
    source (`LinkTally` again), kept per filter set and split as any pass is. **`UrlQuery.LinkEnds`** narrows the URL
    grid, its facets, a check's rows and a sitemap's URLs to those URLs: the links grid gives their set (`EndIds`),
    which the grid tests each row against (`GridPlanner.InSet`), so the grid's count is the list's.
  - What's kept per filter set (ids, counts by target, a sort's order) is megabytes on a large crawl, so the cache
    keeps 12 of them (`ReadCache.HUGE_CAPACITY`).
  - **A running crawl is answered for one moment of it** (`LinkMoment`). What's counted is kept between reads, and
    each read's snapshot is later than the last, so counts kept under clocks of their own would put two moments in
    one answer. The links and URLs are counted, with their highest ids, in one statement, kept for 2 s
    (`ReadCache.LIVE_FOR`); whatever a read counts for that moment it counts within those ids and keeps under the
    moment, so every read adds the same links to it, a crawl only ever adding links: an answer's count, total, rows,
    facets and cursor are of the same links. A pass's ids and tally are kept as one, a sort's order with the tally it
    was put in order from, and a read holds what it read first whatever the cache lets go of meanwhile. A read whose
    own snapshot is older than the moment kept takes one of its own. A page fetched again has its links stored
    again, which is the one way links go: a read that finds links its moment counted gone starts over on a moment
    of its own snapshot.
- **`SitemapListReader.Read`**: each sitemap with how the crawl came by it (worked out from the stored robots.txt, by
  the crawler's parser, and the crawl's options, as its discovery reads them), what became of it, and the URLs it
  lists counted as the URL grid's `fetched` and `indexable` columns judge them. An index counts every sitemap under
  it, a URL two of them list once: added up when no URL is listed twice, which one pass over `sitemap_urls` tells.
  **`UrlGridReader.QuerySitemap`** is the URL grid narrowed to what a sitemap lists (`GridScope.SitemapId`), so every
  count opens its rows there.
- **`RobotsReader`** (`ReadOrigins`, `ReadFile`, `ReadBlocked`): each origin's robots.txt as last read, the file line
  by line, and the URLs it blocked with the line that refused each. The crawl stores only that robots.txt refused a
  URL, so the line is worked out when read, by the crawler's own code: `RobotsTxtParser.Parse` can keep every line as
  it parses (`RobotsLine`: what it is, whether its group applies to the crawler, why nothing was made of it), each
  rule knows its line, and `RobotsTxt.Match` gives the rule that decides a URL, which `IsAllowed` goes by. An
  origin's answer, its lines and its blocked URLs are read and kept as one, each URL matched once, so what one answer
  says of an origin is of one moment of a running crawl. A URL the stored file doesn't refuse says so: a crawl
  continued later keeps each origin's last answer. An origin's state is what its answer meant to the crawl, as the
  crawler's `RobotsCache` took it: read, missing (no rules), unreachable (a 5xx, a 429, or no answer from the start
  URL's origin: its URLs were refused) or unanswered (no answer from any other origin: no verdict, and the crawl
  went ahead). E6: `ReadOrigins` takes a `RobotsOriginQuery` (text in the origin's name, and whether it blocked
  anything) and counts what the text finds each way, for a filter bar; `ReadBlocked` answers for one URL
  (`urlId`); and the inspector's robots.txt test names the origin, the reason and the line that refused the URL
  (`RobotsReader.WhyRefused`, the same match), so it opens the file at that line.
- **The inspector** (E6) also names the sitemaps whose own file lists the URL (`InspectedUrl.ListedIn`, the first 20
  and how many), from `sitemap_urls`, and counts the hyperlinks to the URL as the crawl stored them
  (`UrlLinkTotals.HyperlinkInlinks`: the links grid's count for the same URL, so "N hyperlinks" lands). The
  analysis's `inlinks` can be less: it counts links on the pages it audits, and it doesn't audit Cloudflare's own
  pages (`/cdn-cgi/`), whose links the crawl stores like any other's. On the 100k benchmark that is 24 URLs of
  112,590 (a menu's targets, by one or two links each).

`si readbench <crawl>` times the grids' reads as the UI makes them, each on a fresh connection with nothing cached, as
a filter set's first read is: the first page, the page half way down and the one just past it (read from the far end),
for every sort, filters common and rare (up to three at once, the slowest walk among them), text filters, regular
expressions and searches common and rare (up to three), facets, the inspector (the URL most issues name included), the
links of the most linked URLs by kind, a check's groups and rows, and again without the URL summary, as a running crawl
is read (`GridReadContext.ReadWithoutSummary`, which leaves the file as it is). `--only` runs the reads whose name has
the text (none is an error), `--max-p95-ms` fails a read over one budget, and `--gate` holds each class of read to its
own p95 at 100k (`ReaderBenchmark.GATE_MS`), which is what CI runs on the 100k benchmark crawl.

**The gates.** The budgets are 300 ms for 100k URLs and 1 s for 1M, but a read grows 8–27 times from 100k to 1M where
the budget grows 3.3 times, so a gate of 300 ms at 100k would let through reads that miss at 1M. Each class's gate is
1 s ÷ its growth (at least 10, linear, as a read that regresses to a scan grows) × 2, as CI's 4-core runner is taken to
be twice as slow a core as the desktop measured here: a read at its gate on CI would take about 1 s on 1M URLs. That
factor is an estimate; the first CI runs' `readbench.json` should set it. Two classes over 1 s at 1M today are held to
their own figures so they can't get slower: the facets without the summary (a ceiling of 1.5 s), and links of one kind to
a URL with a million of them (2 s). `ix_links_to_kind` has since brought that read under the gate, which stays so it can't
regress. Worst p95 of each class in ms (first page,
half way or just past it), on a 32-core Windows machine (20 runs at 100k, 5 at 1M; the 1M crawl is
`si bench --pages 1000000`, 1.1M URLs and 50M links):

| Class | 100k URLs (112,584) | 1M URLs (1,123,028) | Growth | Gate at 100k |
| --- | --- | --- | --- | --- |
| Sorted by an indexed column (`indexed`) | 5 | 43 | 8.5 | 200 |
| Sorted by a column without an index (`scan`) | 26 | 241 | 9.3 | 200 |
| Filtered, common and rare, up to three, the slowest walk (`filter`) | 24 | 435 | 18.3 | 110 |
| Text filters, regular expressions and searches, up to three (`text`) | 58 | 610 | 10.5 | 190 |
| A check: its filter, groups and rows (`check`) | 7 | 183 | 26.5 | 75 |
| Facets: of every URL, the pages, a common title filter (`facets`) | 80 | 713 | 8.9 | 200 |
| A URL looked up, inspected (`inspector`) | 2 | 17 | 8.0 | 200 |
| Links of every kind, to and from the home page (`links`) | 8 | 69 | 8.6 | 200 |
| Links of one kind to the most linked URLs (`links-kind`) | 111 | 1,782 | 16.1 | 240 |
| Without the summary: pages, sorted and filtered (`crawl`) | 87 | 920 | 10.6 | 185 |
| Without the summary: facets (`crawl-facets`) | 111 | 1,353 | 12.2 | 245 |
| The links grid, from the link summary: pages in crawl order and by the target, filters, a few targets with many links, facets (`link-grid`) | 69 | not measured | | 200 |
| The links grid, through many targets' links on their index, once per filter set (`link-targets`) | 237 | not measured | | 550 |
| The links grid, one pass over the links: text and search filters, and everything without the link summary (`link-pass`) | 1,232 | not measured | | 2,500 |
| Sitemaps: the list, a sitemap's URLs (`sitemaps`) | 31 | not measured | | 200 |
| robots.txt: origins, a file, its blocked URLs (`robots`) | 34 | not measured | | 200 |
| The crawl's headline numbers: read, and read again from what was kept (`stats`) | 174 | not measured | | 350 |
| The same without the link summary: the links counted, each sitemap page looked up among them (`stats-pass`) | 322 | not measured | | 600 |
| The links grid's passes over the links, and a view's pages, split over the connections a host lends (`link-pass-split`) | 489 | not measured | | 1,500 |
| Search as it's typed, the crawl's text kept (`search`) | 2.8 | not measured | | 25 |
| The first search of a crawl, which reads its text (`search-first`) | 72 | not measured | | 200 |

The facets of the pages without the summary take 1.35 s at 1M: they read and judge every row of the crawl's own tables.
Every other read of 1M in the table is under 1 s apart from `links-kind`, which was measured before its index (below);
the slowest of the rest is the pages by response time without the summary (0.92 s).

The last five classes (E3b) were measured on the 100k benchmark crawl only (5,054,060 links, 20 runs, twice, the slower figures given): their gates
take the least growth the others have, tenfold. `link-pass` is over the budget at 100k by what it is, one pass over
every link (0.3 s for a rare text filter, 0.5 s for a common one, 0.8–0.9 s for a search, 0.7–1.2 s for a common
filter or a sort by the target without the link summary), and is held to 2.5 s so it can't get slower; a million
pages' links would take ten times that. Of the rest, the slowest are a filter on "not followed" (69 ms: the pages
that say nofollow are read first), the facets (49–51 ms), a sort by the target (42–55 ms at any place) and links to a
few thousand targets (27–47 ms); a page of any filter on what a link is, anywhere in 4.4 million hyperlinks, takes
3 ms.

`stats` and `stats-pass` (E3c) are `CrawlStatsReader.Read`, Overview's headline numbers. It reads the URLs once and the
responses once, counting each row every way it's wanted as it's read (`si_tally(slot, value)`), where it ran a statement
for each number (0.96 s, 1.16 s at p95): 98 to 126 ms (108 to 174 at p95, over six runs), and 1 ms from what's kept
once read. With the link summary no link is read: it has the number of links, and counts each URL's links from other
pages apart from a page's links to itself, which says which pages a sitemap led to have none. Without it the links
are counted and each of those pages is looked up among them (181 to 244 ms, 189 to 322 at p95). The gates are about
twice the desktop's worst p95, for the runner. Both grow with the URLs: a first
read of a million would take about a second, which wasn't measured.

The E3b review added the links to a few targets with many links between them, a menu's, to `link-grid`: the twelve most
linked pages, URLs under `/c/`, URLs with over 50,000 inlinks, pages a click from the start (1.3 to 2.7 million links,
to 12 to 77 targets), and the hyperlinks among the last. A page of them is found on the links' index by target: 6 to
30 ms for a first page and 36 to 74 ms half way down, on a machine other work was sharing, where the same run's
slowest `link-grid` read was a sort by the target's status (116 ms; 69 ms was a quiet machine's). Before the review
they walked the links to each page, 0.7 to 3 s half way down, which the gate now fails. `link-targets` is its two
reads through many targets' links on that index, once per filter set and then kept: the links to pages four clicks
from the start (1.4 million links to 74,000 targets, 237 ms half way down) and the hyperlinks to the site's own URLs
(4.4 million, kept as every link but the rest, 158 ms). They're inside the 100k budget and grow with the links, so
they'd miss at 1M; the gate is twice the desktop's worst, for the runner, so they can't get slower. `link-pass` gained
the read the index can't place: a filter on the target with one on a link's position, deep among many links (0.57 s).

E3c took the filters most links pass out of both: deep in, they're read from the blocks but for the others
(`ButTheOthers`), so the hyperlinks to the site's own URLs moved from `link-targets` to `link-grid` (107 ms half way
down, from 249), and the navigation links to pages a click from the start from `link-pass` (16 ms, from 565). On a
copy of the crawl with one link id in eleven missing, which no read was timed on before, the links to the site's own
URLs took 407 to 459 ms half way down and take 97 to 132. `link-pass` keeps a filter on the target with a position
that the fewer links pass (the content links to pages four clicks from the start, 455 ms), and the text filters and
searches: a table of the distinct link texts was measured for those and not built
([the API doc](../docs/v2-sidecar-api.md#engine-changes-e3c-made) has the numbers).

E6 split the passes of `link-pass` over connections (`link-pass-split`), and `si readbench` times each both ways: on
one connection, as a running crawl's are read and as any is when no read slot is free, and with the helpers a host
with every core free lends (`ReadHelpers.UpTo`, every other core, of which a pass takes seven at most). Timed in turn
with the engine from before E6 on the 100k crawl, three rounds of ten runs on a machine other work was loading: on
one connection a pass takes what it took (a common text filter 665 to 934 ms at p50 for 778 to 914 before, a search
1,233 to 1,592 for 1,301 to 1,615); on four connections a quarter (172 to 187, 289 to 297); on eight a sixth (113 to
123, 183 to 211), which puts every one of them inside the 300 ms budget at p50, a filter on the source's depth (210
to 228 ms, from 819 to 1,536) and a pattern over every link's text (146 to 161, from 853 to 1,284) included. Their
worst p95 over the rounds was 489 ms on eight connections and 532 on four (1,295 once, as the machine was busiest).
The gate is 1,500 ms: CI's 4-core runner has three helpers to lend and is taken as twice as slow a core, so the gate
is over twice the worst with four connections, and above the one busy run. No CI run has yet confirmed it. That gate
alone would let a pass that stopped splitting through, as every one of them takes less on one connection, so `--gate`
also holds each split case to what it is for (`SplitGate`), on a crawl of 500,000 links or more: every pass it read
was split (`LinkGridReader.Split` reports the connections), and, on 4 cores or more, its slowest page took at most
60% of its one-connection twin's at p50, in the same run. With splitting switched off, the 100k crawl's read gate
fails on every split case. `link-pass` holds
the same reads on one connection to the 2.5 s it had. A view's ends are timed with them: its targets from the link
summary in 20 to 58 ms (`link-grid`), the pages its links are on in a pass (475 to 740 ms on one connection, 148 to
157 at p50 on eight).

The three reads the Sitemaps, robots.txt and inspector screens gained in E6 are timed in their classes: the origins
searched and filtered and one blocked URL's row (`robots`), and the inspector of a URL robots.txt refused and of one
sitemaps list (`inspector`).

The `links-kind` cell, and the inspector's, were measured before `ix_links_to_kind` and `issue_targets`. With those, on
the same 1M crawl, links of one kind half way down take 94–241 ms (they were 1.7–3.1 s) and "named by" is 2 ms, 8 ms for
a URL 112,000 issues name (it was 35 ms, reading every issue; 100 ms with 3.5M issues). On 4 cores those link rows take
109, 60 and 139 ms. The gates are unchanged.

## Export and the report

**Where to fix each URL.** A check whose URLs other pages refer to (`References.Sources`) has, after its sheet's own
columns (in the workbook, its own file and a group's or its rows' export), "Linked from (pages)", "Linked from" (the
first ten pages, each with where on it the link is and its text, then "and N more") and "Listed in sitemaps"; a check
whose pages hold the bad references (`Targets`) has "Links to (addresses)" and "Links to" (the first ten with their
status). They're read a page of rows at a time (`Export/WhereFoundColumns`). The report's list of a `Sources` check's
first addresses has "Where to fix it" (the first three pages linking, then how many more, the sitemap, the start
address), and a `Targets` page naming several says how many others.

`si export` and `si report` write what the app's Export writes, by the same code (`Export/CrawlExporter`); the
sidecar runs the same as a job ([the API doc](../docs/v2-sidecar-api.md#export-e5)).

```bash
si export example.sicrawl --kind urls --out pages.csv --columns url,statusCode,title,inlinks \
    --filter type:in:page --filter statusCode:between:200..299 --sort inlinks:desc
si export example.sicrawl --kind links --out broken-links.xlsx --filter to.statusClass:in:4xx,5xx
si export example.sicrawl --kind issueRows --check title-too-long --out long-titles.csv
si export example.sicrawl --kind workbook --out everything.xlsx     # a summary sheet, and a sheet for each check
si report example.sicrawl --out report.html                         # the report; --cut summary for its first page
si export example.sicrawl --kind sitemaps --out sitemaps.xlsx       # the list of sitemaps, as the Sitemaps screen lists them
si export example.sicrawl --kind robotsOrigins --out origins.csv --blocked true --search example
si export example.sicrawl --kind urls --out broken-targets.csv --link-ends to --link-filter kind:in:hyperlink \
    --link-filter to.statusClass:in:4xx                             # the URLs the broken hyperlinks lead to
```

| `--kind` | Writes | As |
| --- | --- | --- |
| `urls` (`--link-ends from\|to`, with `--link-filter` and `--link-search`, to narrow it to a links view's ends) | The URL grid | `.csv`, `.xlsx` |
| `links` | The links grid | `.csv`, `.xlsx` |
| `sitemaps` (E7) | The list of sitemaps, in the Sitemaps screen's words | `.csv`, `.xlsx` |
| `robotsOrigins` (E7; `--search`, `--blocked true\|false` to narrow it) | The origins' robots.txt, in the robots.txt screen's words | `.csv`, `.xlsx` |
| `sitemapUrls` (`--sitemap <id>`) | One sitemap's URLs | `.csv`, `.xlsx` |
| `robotsBlocked` (`--origin`, with `--line` or `--reason` to narrow) | The URLs robots.txt kept the crawl from on an origin | `.csv`, `.xlsx` |
| `issueRows` (`--check <id>`) | A check's rows, with any of the grid's columns | `.csv`, `.xlsx` |
| `check` (`--check <id>`) | One check's sheet of the workbook, as a file of its own (with where to fix each URL, below) | `.xlsx`, `.csv` |
| `workbook` | The spreadsheet of everything | `.xlsx` |
| `report` | The report (also `si report`) | `.html` |

The format comes from the file's name unless `--format` gives it. A grid takes the grid's own query: `--columns` (by
id; the grid's own when left out), `--filter column:operator[:value]` any number of times (`in` and `notIn` take
`a,b,c`, `between` takes `min..max`, `isEmpty` takes nothing), `--search`, and `--sort column[:desc]`. A filter's
value is of its column's type, not of what it looks like: `title:eq:404` is the text "404", `statusCode:eq:404` the
number, `isInternal:is:false` a boolean. An option that belongs to another kind (`--check` for `urls`, `--cut` for
anything but the report) is refused, never ignored, by the same list the API refuses a field of another kind by
(`Export/ExportFields`).

**A URL export narrowed to a links view's ends** (`--link-ends`) is the URL grid's `linkEnds`: `to` for the URLs the
view's links lead to, `from` for the pages they're on, where `--link-filter` (the links grid's filters, as `--filter`
takes them, any number of times) and `--link-search` (its search) are the view, and none of either is every link. It
narrows beside `--filter` and `--search`, as one more filter. `--link-filter` and `--link-search` without
`--link-ends` are refused.

**The two lists** (E7) aren't grids, and take no `--columns`, `--filter` or `--sort`: their columns are their screens'
(`Export/ListExport`), and their words are the screens' cells' (`Export/SitemapListWords`, `Export/RobotsOriginWords`),
"Read in part: 404 Not Found", "Couldn’t be read: URLs refused". `sitemaps` writes every sitemap the crawl looked for,
in the order it found them, with the index that lists it ("Listed by"), how it was found, what became of it and its URLs
counted (empty where the list shows a dash: an index until every sitemap under it is read). `robotsOrigins` writes the
origins in the list's order, each with what its answer meant, its status or error, the URLs it blocked, when it was read
(a date, in this computer's time) and its size; `--search` and `--blocked` narrow it as the list's search and filter do.
Each is read whole, from one snapshot of the file, so a running crawl's file is of one moment of it.

**How an export reads.** A page of rows at a time, each page the grid's own read (`Storage/GridExportReader` calls
the readers the grids call, with the grid's cursor), on a snapshot it lets go of before the next:

- the file holds exactly what the grid shows, in its order, whatever the filters and the sort;
- memory is a page of rows, whatever the crawl's size: the 100k benchmark crawl's 5 million links are exported in
  76 MB (CSV) and 91 MB (Excel), as its 112,577 URLs are in 70 and 88;
- nothing is held between two pages: a writer here (an analysis, a continue) waits no longer than for a grid's read,
  and a running crawl's write-ahead log can be folded into the file between any two pages;
- a crawl that changes under it (analysed again, continued or finished) ends the export, which says so
  (`ExportFailure.CrawlChanged`) rather than write the rows of two crawls into one file; a crawl paused or resumed is
  the same crawl, and the export goes on;
- a crawl that's running is exported as it stood when the export began (its highest URL and link ids are the bounds),
  in crawl order or by URL only;
- what the grid's reads count is kept between the pages (the sidecar's own `ReadCache`, or the export's): without it
  a filtered or sorted export would count its rows, and order its targets, once a page.

**The file** is written beside its target under another name and moved into place once whole (`Export/ExportTarget`),
so an export that's cancelled or fails leaves nothing, and a file it was to replace is as it was. Its name must end in
the format's extension, and hold no character a file's name can't (on Windows `\ / : * ? " < > |`). A place that
can't be written to, a disk that fills up and a file open in another program (as Excel holds a workbook it's showing:
found out before anything is read) are each told apart. An `IExportJournal` in `ExportSettings` is told of each part
file before it's created and once it has gone: the sidecar keeps that note in its state folder and, when it next
starts, deletes the part files a killed run left (those it names, and no others). `si export` keeps none: Ctrl+C
deletes its part file, and one that is killed leaves it.

**CSV** is RFC 4180: UTF-8 with a byte-order mark, CRLF rows, quotes doubled, and no field longer than the 32,758
characters Excel reads as one cell (a longer one ends the row in Excel, and its rest starts a new row in column A,
unguarded): longer text is cut, and counted in `TruncatedCells`, as `si issues --csv` cuts and counts it too.
**Excel** files are written by the
engine itself (`Export/Xlsx/XlsxWriter`), a row at a time into the ZIP, with no package: shared strings (streamed to a
file beside the workbook as the sheets are written, a repeat naming the item it already has), every cell named by its
place, numbers as numbers, a bold header row that stays in view and filters, and column widths from the first 200
rows. That is the plain form of the format, as Excel writes it, for Excel, LibreOffice and Numbers alike. A sheet holds
1,048,576 rows, so a longer table goes on to further sheets ("Links (2)"…), up to 16 of them (16,777,200 rows); more is
refused before anything is written, never cut short.

**Text from a site can't run.** A CSV field that starts with `=`, `+`, `-`, `@`, a tab or a carriage return gets an
apostrophe before it; a number is written as its digits, so a negative one stays a number. In a workbook every text is
a text cell, and one that looks like a formula is also marked as Excel marks text typed after an apostrophe, so
editing the cell leaves it text. In the report every string is escaped where it's written, a URL is a link only when
it's http or https, and the document's own Content-Security-Policy allows no script, no network and no style but its
one sheet.

**The report** (`Reporting/`) is one self-contained HTML document to [the design](../docs/v2-ui-simple.md#the-report):
`ReportReader` reads what it says (counts, and a few rows a check, so its size follows the checks that found
something, never the site's), `ReportDocument` writes it, and the workbook's summary sheet is written from the same
model, so the two can't differ. The score and each check's words reach both through `ReportSources`, and neither
works out anything of its own:

- the score, its lines and "Fixing these five takes the score from 85 to 98" are the score the analysis stored
  (`AnalysisResults.Score`, or why there is none: `NoScoreReason`), and the addresses are the URLs crawled when the
  analysis ran;
- problems are numbered in `AnalysisResults.FixFirst`'s order, in the report's first list, its sections and the
  workbook's sheets alike;
- a check's words are the catalog's plain ones (`IssueDefinition.Plain`), with any `{limit:…}` filled from the
  thresholds the analysis ran with (`PlainLimits.Fill`), and its technical texts where it has no plain ones; a check
  that couldn't run is named by what it looks for (`LooksFor`);
- a check reported once for the whole site (`ReportedOnce`) says "Checked once, for the whole site" where another
  says its share.

`si report` writes it in 0.3 s for the garden crawl (19 checks found something: 94 KB, 12 pages of A4, 13 of Letter)
and 0.5 s for the 100k benchmark crawl (75 checks: 210 KB, 33 pages of A4); `--cut summary` is its first page (18 KB)
or, with a score of 45 lines, its first three (38 KB). A crawl never analysed has no report: `si report` says so,
with the `si analyze` command to run, and exits 2.

## Synthetic site and benchmark

The synthetic site is clean by construction: every page passes every check unless an issue is planted on it.
Products carry issues at fixed intervals (duplicate titles, broken links, redirect chains, noindex,
canonicalised pages...). `/plants/` holds a test page for each check that a product can't carry (missing title,
conflicting canonicals, broken hreflang, invalid JSON-LD, a slow page, a firewall block, a redirect loop...).
`GetExpectations()` reports exactly what a correct crawl and analysis must find, down to the URLs for every issue id.

The e2e tests also crawl these variants:

- **HTTPS**, with a self-signed certificate. Plain HTTP answers on the same port, as port 80 does on a real site. It
  serves the home page there instead of redirecting: the planted duplicate version of the site.
- **No sitemaps**, where the checks that need them are skipped.
- **Hidden from search** (`HiddenFromSearch`, `si testsite --hidden-from-search`). The home page and every product
  are noindex, as on a site launched with its staging setting left on, so the home page is noindex and so are more
  than half of the pages that stand for themselves (a canonicalised page is a variant of another, and no part of the
  count): `site-hidden-from-search` is reported once, on the home page. Off everywhere else, the benchmark included,
  where the check must flag nothing.
- **Noindex products** (`NoindexProducts`). Every product is noindex and the home page is not, as on a shop that
  keeps its many listings out of search on purpose: more than half of the pages are noindex, and
  `site-hidden-from-search` must not be reported, as the home page can be indexed.
- **Dead twin** (`DeadTwin`, plain HTTP only). The site links its https:// version on its own port, which doesn't
  answer: once from the plants hub, a URL the crawler asks for and finds unreachable, and five times from the first
  orphan. Only the sitemap leads there, so the crawler reads it once no linked URL is in flight, by when it has
  written the twin off: those five aren't checked. The benchmark plants it, so the stats and the URL summary are
  proven to count such URLs alike. Elsewhere it's off: a resumed crawl forgets which origins it wrote off, so it may
  ask for some of them, and the dead URL's retries add seconds to a small crawl.

A second origin serves a partner site, so links, images and sitemap entries on another host are tested too.

The one allowance is for response times, which depend on the machine. Up to 5% of pages (at least 25) may be
flagged slow besides the planted one, and the e2e test checks the stored times directly.

To add a check: write the rule, add the issue to the catalog, and plant it on the synthetic site. The end-to-end
test and the benchmark compare the whole issues table row by row, so a check that flags anything unplanted fails
them.

`si bench` serves the synthetic site from a child process, with the dead twin, crawls it, analyses it, and checks
both against the expectations, the URL summary against the stats too. It can fail on thresholds, and CI runs it on
every push (`.github/workflows/engine.yml`), keeping the crawl (`--db`) for `si readbench`, which then holds the
grids' reads of it to their budget (see [Grid reads](#grid-reads)).

On a 32-core Windows machine, at concurrency 16, against the local synthetic site:

| Pages | URLs | Links | Crawl | Throughput | Peak memory | Growth over the crawl | Analysis | File |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 101,107 | 110,251 | 5.05 M | 35 s | 2,916 pages/s | 168 MB | none (−1 MB) | 7 s, 157 MB peak | 400 MB |

This was measured on `68d55a2`. Since then, the crawl also probes the site's other versions, which adds one URL,
and the benchmark plants the dead twin, six more (110,258). With the link summary (E3b) the same machine, shared
with other work, measured over three runs a 36–42 s crawl (2,437–2,813 pages/s), a 188–195 MB peak with no growth (−9
to +1 MB), an 11 s analysis peaking at 276, 313 and 315 MB (283 MB in the one run before; the link summary's pass is
1.5 s of it by its own clock), and a 487–494 MB file (474 MB before), the link summary and the new index 4.5 MB of it.
The rest is in the links and their two indexes, which E3b doesn't write, and moves from run to run: probably the order
the URLs were found in, as larger ids take more bytes.

With E3c (the time spent crawling kept a stretch at a time, a second set of counters for the part of the counts that
is the crawler's own probes) four runs on the same machine, still shared, measured a 36–44 s crawl (2,294–2,812
pages/s), a 188–194 MB peak with no growth (−9 to 0 MB), an 11.4, 11.8, 12.2 and 18.1 s analysis peaking at
253–327 MB, and a 478–495 MB file: all within the spread of the runs before it.

After the E3c review (the write-ahead log folded once it passes 64 MB, which a crawl nobody reads never reaches: its
log peaks at 36 to 38 MB; one more planted URL, listed in a sitemap and refused by robots.txt, with its two issues)
one run measured a 45 s crawl (2,237 pages/s), a 198 MB peak with no growth, a 15.3 s analysis peaking at 276 MB, and
a 494 MB file. `si bench --read-links` crawls with the links grid read all the while, a read always part-way through
the file, and `--max-log-mb` fails the run if the write-ahead log passes a size: 100,000 pages in 46 s (2,180
pages/s) with the log at 89 MB at most, 40,000 pages with it at 76 MB, where without the fold it reached 580 MB. CI
runs the 40,000-page one with a limit of 128 MB.

With the score (one more cross-page rule, the score worked out as the analysis commits, five more columns of the
analysis row; nothing in the crawl itself changed) one run on the same machine, heavily shared at the time (the
crawl's pace fell to a fifth for stretches of it), measured a 79 s crawl (1,273 pages/s), a 198 MB peak with no growth
(−15 MB), a 22.8 s analysis peaking at 261 MB, and a 479 MB file. The analysis of a 100k crawl, run back to back
before and after the score on copies of one file, took 12.9 s both times.

After the three changes that followed the score (a site-wide fault leads its severity in the order to fix, the
hidden-site check counts noindex only, every check has its plain words) one run measured a 56.8 s crawl (1,779
pages/s), a 193 MB peak with no growth (+1 MB), a 19.6 s analysis peaking at 331 MB, and a 479 MB file.

Every planted page and issue was found, and nothing else was flagged. The planted slow page, firewall block and
unreachable URL make the crawler back off briefly, as it would on a real site. On real sites, speed is set by the
network and your rate settings, not by the crawler.

## Not yet

- The desktop UI (step 3).
- JavaScript rendering (Playwright, with HTTP / Rendered / Auto modes) and browser fallback for blocked sites,
  plus custom extraction, list mode and include/exclude rules (step 4). `Blocked` responses are already detected.
- Search Console, Analytics, Bing and Ads data (step 5).
