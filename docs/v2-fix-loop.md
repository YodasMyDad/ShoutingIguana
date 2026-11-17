# The fix loop: compare with the last crawl, recheck a problem's pages (step 3b)

The owner, 2026-11-17: **"Compare + recheck"**. Every check, the score and every list compares with the site's
previous crawl (New, Fixed, Fewer, the score up or down). A problem gets **"Recheck these N pages"**, which fetches just
those pages again and says "4 of 5 fixed". There is no manual "mark as fixed": a recheck proves it.

Why: the step 3b view audit found that the loop breaks at its last step (`ux-views.md`, X1, X7 and the flow-break
section). Simple's problem page ends with "Fixed it? … Check again" (`app/src/simple/ProblemScreen.tsx:364`). The new
crawl then replaces the site's row and the list starts over, so nothing shows what was fixed, what is new or what is
still there. Confirming one footer link means crawling the whole site again: 37 minutes for the garden site's 4,521
URLs.

This is the design (stage A). Nothing here is built. The API it adds is in
[the sidecar API](v2-sidecar-api.md), marked "(fix loop)", and what the engine changes is under
[Engine changes the fix loop makes](v2-sidecar-api.md#engine-changes-the-fix-loop-makes). It supersedes "crawl
comparison: later" in [the rebuild plan](v2-rebuild.md) (step 3b now), in [the IA](v2-ui-ia.md#room-for-what-comes-later)
and in [Simple mode](v2-ui-simple.md#one-problem) ("Later, crawl comparison turns that into 'fixed since last
time'").

It builds on in-flight work and contradicts none of it:

- **`ui/3b-evidence`** ([v2-evidence.md](v2-evidence.md) there). It provides rows by importance, a group's `related`
  facts and `sharedText`, and the image checks grouped by image. It also sets `AnalysisFreshness.VERSION` 9, so
  every crawl analysed before reads out of date: the comparison's like-for-like rule (below) is built for exactly
  that.
- **`ui/3b-polite`**. It adds slow start, a learned pace per host and an honest "the site asked the crawler to slow
  down; not checked" outcome. A recheck is a crawl run, so it gets all three, and that outcome reads as "not
  checked", never "fixed".
- **`ui/3b-accuracy`**. It changes catalog wording and maybe what some checks flag. The comparison treats a changed
  check as the analyses' versions differing, which is the case it already handles.

## The decisions in brief

| Question | Decision | Why, in a line |
| --- | --- | --- |
| Which crawl is "previous" | The newest earlier crawl of the **same site** (the library's own grouping: the host without `www.`, plus a port of its own) that has results and can be read. Prefer one with the **same start URL** | It is the row Home already folds under the site, so the comparison and the screen agree |
| Unfair comparisons | Detected and named: a different start URL, scope, limits, user agent or headers; either crawl stopped early; very different sizes; results judged by other rules | The comparison is still shown where it helps, with a plain line saying what differs |
| Results judged by other rules | **Like for like**: when the two analyses differ (engine version, checks, limits), the previous crawl is analysed again first. Simple does it by itself; Advanced asks | Otherwise every engine update reads as dozens of "new" and "fixed" problems |
| Pick another crawl | Advanced: **Compare with…** any analysed crawl of the same site, or none. Remembered per crawl in the app's preferences | Comparing two sites means nothing |
| What is compared | Three levels: **check** (count then and now), **cause** (each group), **URL** (each row). URLs are matched by their text; causes by the group's key; duplicate clusters by the pages they share | These are the three things the screens list |
| Not checked isn't fixed | A URL counts as fixed only if this crawl read it again and the check passed it, or the crawl found nothing that leads to it any more. Anything not read this time is **Not checked this time** | The engine's rule that "not checked is not 0", applied across crawls |
| Where it's computed | In the engine: a read-only `CrawlComparisonReader` that reads both files (two snapshots) and joins them in memory, keyed by URL. The sidecar serves and caches it | One answer for the app, the report and `si compare`. No writer is opened |
| Recheck: same file or new | **Update the same crawl file in place**: a recheck is a short crawl run (`CrawlWriter`) that fetches only the problem's URLs, followed by a full analysis (`IssueWriter`) | Every screen, count and score moves at once. It has precedent in Screaming Frog's Re-Spider (from memory, not verified). A separate file would need overlays on every screen and a new partial analysis |
| What a recheck fetches | The problem's URLs and what is needed to judge them: what they name, or what refers to them, with redirects followed to their end. New URLs they now link to are fetched one step, and no further. At most 1,000, the most important first | Enough to settle the check for those URLs, and never a hidden full crawl |
| What a recheck can't settle | `orphan-page` and `no-https` need the whole site read again, and say so. Every other check can be rechecked. Pages not rechecked are taken as the crawl found them | Only those two depend on pages nobody can name in advance |
| "4 of 5 fixed" | Before fetching, the recheck snapshots the check's issues on its URLs, inside the crawl file. The analysis that follows settles each URL as Fixed, Still, New or Couldn't recheck, and keeps the result | A recheck is a record ("rechecked 5 pages on 17 Nov") that later analyses don't rewrite |
| Concurrency | One writer, as ever. A recheck is refused while the crawl is live or in use elsewhere, and while another crawl of the same site runs here. It works only on the site's latest crawl | Two runs on one host would double the load on it. Rechecking an old crawl would rewrite history |
| Pacing | The crawl's own speed settings, through the same host gate (slow start, learned pace). robots.txt is read again | A recheck is a crawl run, polite as any |
| No previous crawl | Say nothing: no zeros, no "+0". A first crawl's rechecks still close the loop | The loop must work on the first day |

## 1. Which crawl is "previous"

### The same site

The library groups crawls by site in the app (`app/src/simple/home/sites.ts`, `siteGroups`). The key is the start
URL's host without a leading `www.` (the sidecar's `site`, `CrawlManager.SiteOf`), plus `:port` when the start URL has
a port of its own. The scheme doesn't count: `http://` and `https://` crawls of a site are one site, as the engine
treats them.

The sidecar takes over that rule as **`siteKey`** on each library item, so the app's grouping and the sidecar's choice
of "previous" can't drift apart. The app's `siteGroups` reads `siteKey` instead of computing it.

### The default previous crawl

For a crawl C, the candidates are the library's other crawls with C's `siteKey` that:

- started before C (`startedAt`, then id, the order Home uses);
- have an analysis that read a page (a score, or `noScoreReason` other than `noPages`);
- aren't live here, in use elsewhere, interrupted (it can't be analysed), missing or unreadable.

The previous crawl is the newest candidate **with the same start URL** (as stored, compared exactly). If there is
none, it is the newest candidate of the site, flagged as a different start URL. That way a site crawled in two scopes
(the blog alone, and the whole site) compares each scope with its own.

A crawl removed from the library isn't a candidate. A file deleted from disk drops out, and the next older one takes
its place. Advanced's picker lists the missing one as disabled ("File not found").

### Fair or not: what differs

`CrawlComparability` (engine, pure) compares the two crawls' stored options, their analyses and their size. Each
difference is named with its code, so the UI can say it in its own words:

| Code | What differs | Effect on the comparison |
| --- | --- | --- |
| `startOrigin` | The start URL's scheme or host (`http` to `https`, `www` or bare) | **Counts only.** Every URL text differs, so URL-level matching is meaningless. The score and each check's count then and now are still compared, and a move to HTTPS shows as `no-https` fixed |
| `startPath` | The start URL's path (`/` against `/blog/`) | Partial |
| `scope` | `stayInStartFolder`, `includeSubdomains`, `followNofollow`, `crawlCanonicals`, `crawlHreflang`, `crawlPagination` | Partial |
| `limits` | `maxUrls`, `maxDepth` | Partial |
| `fetching` | `checkResources`, `checkExternalLinks`, `discoverSitemaps` or `extraSitemaps`, `respectRobotsTxt` | Partial. The checks these switch off are skipped on one side, and read as **not compared** |
| `client` | User agent (preset or custom), `acceptLanguage`, header names, `useCookies` | Noted. A site can answer another client differently. Header values aren't stored, so a changed value can't be seen |
| `timeouts` | `requestTimeoutSeconds`, `maxRetries` | Noted. They change what counts as "didn't respond" |
| `stoppedEarly` | Either crawl was stopped, failed, or kept internal URLs out at a limit (`score.covers.complete` false) | Partial, and says which ("this check", "the last check") |
| `coverage` | One read fewer than half the pages the other did | Partial: most rows will read "not checked" |
| `rules` | The two analyses differ in `analysisVersion`, `checksFingerprint`, `scoreVersion` or settings, and the previous couldn't be analysed again | Counts only. No score change is shown, since the two were scored differently |

Speed (`concurrency`, `maxRequestsPerSecond`, `respectCrawlDelay`) and `storeHtml` aren't differences. They change how
long a crawl took, not what it found.

`comparable` is `full` (no difference but notes), `partial` (anything marked partial), or `countsOnly`. Simple shows a
`partial` comparison with a one-line caution, and a `countsOnly` comparison as a score change and problem counts with
no marks on rows. Advanced shows every difference in the comparison's header, folded.

### Like for like: analysing the previous crawl again

Two analyses are comparable when they have the same analysis version, checks fingerprint, score version and settings
(`AnalysisInfo`). After an engine update (evidence alone sets the version to 9), or after a limit changes in Settings,
the previous crawl's analysis won't match the new crawl's. Compared as they stand, every rule change would read as
"new" or "fixed".

So when they differ, the comparison answers `previousNeedsAnalysis` (its out-of-date reasons). The app then analyses
the previous crawl again, which is the same "Analyse again" the library already offers:

- **Simple** does it by itself, behind the comparison's skeleton. It takes a second on a small site and 7 to 20 s at
  100k. Meanwhile the Summary shows the score without a change line, and then adds it. The line's tooltip and the
  report say "Your last check was worked out again with today's checks."
- **Advanced** shows "The 3 Nov crawl was analysed by an earlier version: Analyse it again to compare like for like"
  with the action, and offers the counts-only comparison until then.
- If it can't be analysed (in use, read-only, failed), the comparison stays counts-only with `rules`.
- If it's **this** crawl whose analysis is the older one (the previous was analysed again since, by a newer engine),
  this crawl's own out-of-date banner ("Update the results") comes first. Once that is done, the two match.

This rewrites the previous crawl's stored analysis, so its score in the library can move ("71" becomes "73"). That is
what Analyse again already does. The new score is today's judgement of that crawl, which is the right baseline for a
trend.

### Comparing with another crawl (Advanced)

A **Compare with…** control sits on Overview and Issues: "Compared with 3 Nov, 09:12 ▾". It opens a menu of the site's
analysed crawls, newest first, each with its date, status, URLs and score. A crawl that can't be compared is shown
disabled with the reason (live, missing, interrupted, never analysed). Then "Don't compare". The choice is the app's
preference, kept per crawl id, as hidden checks are. A crawl compared with a later crawl reads "since" as "until", with
the arrows the other way: allowed, as rarely as it's wanted.

## 2. What is compared, and where

### Three levels

| Level | What | Read from | Shown on |
| --- | --- | --- | --- |
| **Check** | Each check's count then and now, and how the rows moved (fixed, still, new, not checked) | `issue_types` and the row pass below | Summary, What to fix, Overview, the Issues list, Home, the report |
| **Cause** | Each group of a check (the broken URL, the oversized image, the shared title), its URLs then and now | The groups' keys in each file | Where it is (Simple), a check's groups (Advanced) |
| **URL** | Each flagged URL: new, still there, fixed, no longer found, not checked | `issues` in each file, matched by URL text | A group's pages, a check's rows, the "Fixed since" lists |

### Matching across two files

URL ids are per file, so nothing is matched by id. Matching uses:

- **A URL** by its stored text, which the engine normalises the same way in every crawl. In memory it is keyed by
  `UrlKey.Compute` (XxHash128 of the text, the writer's own key). An engine that changes normalisation would show those
  URLs as gone and new. That is said under Known limits, not hidden.
- **A row** by (check id, URL).
- **A cause** by its group's key, which follows the check's `grouping`:

  | Grouping | Cause key across files |
  | --- | --- |
  | `relatedUrl` (not a duplicate) | The related URL's text: the broken target, the image, the canonical. The group without a related URL matches its counterpart |
  | `relatedUrl` duplicate clusters (`title-duplicate`, `description-duplicate`, `h1-duplicate`, `duplicate-content`, `near-duplicate-content`) | **By the pages they share**: each cluster now matches the cluster then with which it shares the most pages (ties: the one named first). A cluster is named after its first page, which can change when that page is fixed, so the name can't be the key. Unmatched clusters are fixed (then) or new (now) |
  | `detail` (`ResponseDetail`, `PageDetail`) | The detail, or the fault (`issue_faults`), with the related URL's text |
  | `none` | No causes: rows only |

### States

**A check** (`state`):

| State | When | Simple says | Advanced shows |
| --- | --- | --- | --- |
| `new` | It ran then and found nothing, and finds something now | "New since 3 Nov" | `New` |
| `fixed` | It found something then, finds nothing now, and nothing it flagged then went unchecked this time | In "Fixed since your last check" | `Fixed` |
| `fewer` | Fewer now (including a would-be `fixed` with some rows not checked) | "9 on 3 Nov" | `−5` |
| `more` | More now | "4 on 3 Nov" | `+5` |
| `same` | The same count. Its rows may still have moved: `fixed` 2, `new` 2 | Nothing, or "2 fixed, 2 new" | `0`, with the split in the tooltip |
| `newCheck` | It didn't run then: a check newer than the previous analysis (absent from its `issue_types` and `skipped_checks`), or skipped then | "Not looked for last time" | `New check` |
| `notCheckedNow` | It ran then and is skipped now | Its "couldn't check" reason, as today | `Not checked` |

Each check carries its counts: `then`, `now`, and the row split `fixed`, `gone`, `notChecked`, `still`, `new`, `newUrl`,
`notComparedThen`. So "Fewer: 9 to 4 (3 fixed, 2 not checked this time)" can be said exactly.

**A row flagged now** (`change` on a current row):

| State | When |
| --- | --- |
| `still` | The check flagged this URL then too |
| `new` | The URL was in the previous crawl, read by it, and not flagged |
| `newUrl` | The previous crawl didn't find this URL: a new page, or one newly linked |
| `notComparedThen` | The previous crawl found it but didn't read it (a limit, stopped early, robots.txt couldn't be read), or the check didn't run then |

**A row flagged then and not now** (from the other crawl, listed by `/fixed`):

| State | When | Words |
| --- | --- | --- |
| `fixed` | This crawl read the URL again and the check didn't flag it | "Fixed" |
| `gone` | This crawl didn't find the URL at all | For a `sources` check (a 404, a redirect, a blocked URL): "No longer linked", which is fixed. For a page check: "No longer found" (the page was removed or unlinked). Both count as fixed in the totals, and are told apart |
| `notChecked` | This crawl found it but didn't read it, or read it and got no usable page (an error, a block, a rate limit, robots.txt refusing it), or the check didn't run this time | "Not checked this time", with the reason. **Never "fixed"** |

The one exception: a URL a response-code check flagged then (`internal-client-error`, `internal-server-error`,
`internal-unreachable`, `internal-blocked`) that now answers with another failure has moved checks. It is `fixed` for
the old check and `new` for the new one ("404 then, 500 now").

### The score

The score's `then` and `now` come from each analysis's stored score: value, band, partial, and per line (by check id)
the points then and now. A line only then is fixed ("Broken links: −4 then, gone now"), and a line only now is new.
`ifFixed` isn't compared.

There is no change when the two were scored by different `scoreVersion`s (code `rules`). A partial score on either
side gives a change marked "so far".

### Where it's computed

**Engine, `Storage/CrawlComparisonReader`** (read-only; opens each file as the grids' readers do, one snapshot each
through each file's `CrawlReadGate`):

- `ReadSummary(current, previous, cache, ct)` returns the comparability and differences, the score then and now, and
  every check that ran in either with its state and counts.
  - The check-level counts come from `issue_types` and `skipped_checks` (milliseconds).
  - The row split comes from one streamed pass per check over `issues ⋈ urls` in both files, in `(issue, url_id)`
    order. For each check, the previous rows' URL keys go into a set and the current rows are streamed against it.
  - Then point lookups (`urls.url` is UNIQUE, so it is an index seek) settle each no-longer-flagged URL's state now
    (found? read? its outcome) and each newly flagged URL's state then.
  - Memory is the largest check's previous rows, at about 40 bytes a URL (4 MB for a check on 100k URLs).
- `ReadRowChanges(...)`: the `change` of each row on one page of `/rows`, by point lookups in the other file (at most
  `limit` of them, so 100).
- `ReadGroupChanges(...)`: each group on one page of `/groups` with its URLs then. For a `relatedUrl` check that is
  one seek for the related URL's id in the other file, then a count on `ix_issue_targets_named`. Duplicate clusters
  are matched by membership over the check's rows.
- `ReadCleared(...)`: the rows (or causes) flagged then and not now, paged by the other file's row order, each with
  its state. The pass behind a page is the same per-check join, kept in the cache.

**Engine, `Analysis/CrawlComparability`** (pure): the differences and `comparable`, from both crawls' stored options
(`CrawlDatabase.ReadOptions`), `AnalysisInfo` and coverage.

**Sidecar** picks the previous crawl from the library (in memory), serves the reads, and keeps the comparison
summary in the read cache, keyed by both files' stamps and both analyses. The library's per-crawl summary cache
(`summaries.json`) grows two things: each check's count (87 small integers) and a fingerprint of the options that
matter (above). With those, Home's and the library's changes (`previous`, `changes` on each item) are worked out from
cached summaries without opening a file.

## 3. Recheck these N pages

### In place, in the same crawl file

A recheck **updates the crawl it was asked from**. The owner can see the effect everywhere: the problem's count
drops, the score moves, the report changes, and a later full crawl compares with the rechecked state.

- **How.** It is a run of the crawl in a recheck mode. `CrawlWriter` opens the file as Continue does (it holds
  `<file>-lock`) and stores the URLs it fetches over their old records. `CrawlAnalyzer` then analyses the whole crawl,
  as after any run. One writer at a time, and only `Storage/` writes, as the rules require.
- **The crawl stays what it was.** Its status (completed, or stopped with its queue) doesn't change, and nothing in
  its queue is fetched: a stopped crawl's frontier is left exactly as it was. The crawl row is stamped
  (`updated_at`), which makes the analysis out of date (`crawlChanged`) until the analysis that follows.
- **Pages not rechecked are taken as the crawl found them.** The file is no longer one moment: it is the crawl, with
  some pages read again later. A 37-minute crawl was never one moment either. The UI says it where it matters ("Other
  pages are as the check found them on 3 Nov").

Rejected: **a new small crawl file per recheck, compared with the original.** The original would stay one moment, but:

- every screen would need an overlay ("5, of which 4 rechecked fixed");
- the score and counts wouldn't move until a full crawl;
- the analysis would need a partial mode, since site rules over a five-page file report nonsense, with target
  statuses read across files;
- the library would hold hidden child files, which must move, share and delete with their parent;
- and a shared crawl file would lose its rechecks.

### What a recheck fetches

Asked for a check, or one of its causes (a group), or a list of URLs (Advanced's selection), the planner
(`Storage/RecheckPlanReader`, read-only) works out the scope from the check's `references`:

| The check is | Phase 1 fetches | Phase 2 fetches, only for URLs still failing after phase 1 |
| --- | --- | --- |
| `sources` (the flagged URL is what others refer to: a 404, a redirect, a blocked URL, a sitemap entry) | The flagged URLs, with redirects followed to their end | The pages that refer to each one (every kind of link, distinct pages), most important first. Where a sitemap lists it, the sitemaps that list it are read again (needs the `sitemaps` kind, stage E4) |
| `targets` (the flagged page holds the bad reference: a broken link, a canonical to a redirect) | The flagged pages **and** every URL their issues name (`issue_targets`), together. Either end may hold the fix | (none) |
| `none` (the fault is in the page itself: a long title, a duplicate cluster's page) | The flagged pages | (none) |

Then for every page fetched:

- **Redirects** are followed to their end, each hop recorded, as the crawl records them.
- **New URLs**, ones the page links to that the crawl doesn't have, are fetched the way the crawl would fetch them,
  one step. An internal page is parsed and its links stored; a resource or an external URL is status-checked. The
  URLs *they* link to are recorded and not fetched: skip reason `Recheck`, "found by a recheck: check the site again
  to follow it". Without this step, a link changed to a new page that 404s would read as fixed.
- **URLs the crawl already has** keep their stored records unless they are in scope. So a recheck of a page with 100
  links fetches its new links, not all 100.
- **robots.txt** is read again for every origin a fetch goes to (each run reads it fresh, and that is how
  `blocked-by-robots` gets fixed).

**The cap** is 1,000 URLs a recheck. Past it, the plan takes the flagged URLs most important first (the evidence
branch's row order: `inlinks`, or `usedOn`, then depth). It says "Rechecks the 1,000 most important of 4,000. The
other 3,000 stay as the crawl found them". When the scope is more than half the crawl's URLs, the plan suggests
checking the whole site again instead, which takes about as long and settles every check.

### What each check's recheck re-reads

A new catalog property, **`IssueDefinition.Recheck`** (served as `recheck` on `GET /api/checks`). It isn't stored and
isn't part of the checks' fingerprint, like `references`. It lists what a recheck of the check reads again:

| `recheck` | Checks | What it reads again |
| --- | --- | --- |
| `["urls"]` | Every check not listed below (76 of 87): response codes, redirects, links, page indexability, canonicals, titles, descriptions, headings, content and duplicates, images, hreflang, structured data, `http-page`, `mixed-content`, `security-headers-missing` (the home page), `site-hidden-from-search` (the home page), URLs, performance, HTML | The scope above |
| `["urls", "sitemaps"]` | `sitemap-non-indexable`, `sitemap-url-other-host`, `not-in-sitemap` | The scope, and every sitemap the crawl read. URLs a sitemap newly lists are recorded, not fetched |
| `["sitemaps"]` | `sitemap-error`, `sitemap-missing` | Every sitemap the crawl read, and the site's conventional `/sitemap.xml` where it probed for one |
| `["robotsTxt"]` | `robots-txt-unreachable`, `robots-txt-unreachable-other-origin`, `robots-txt-missing` | The robots.txt of each origin the issue names. When the start origin's robots.txt answers again, the result adds "Check the site again to read the pages it kept the crawler out of" |
| `["siteVersions"]` | `duplicate-site-version` | The probes of the site's other versions (`SiteVersions.Of`) and where they redirect |
| `[]` | `orphan-page`, `no-https` | Nothing. An orphan is a page that *nothing* links to, so only a full crawl can show that something now does. Moving to HTTPS changes every address. The UI offers "Check the site again" in place of Recheck |

Duplicates (`title-duplicate` and the rest) are settled against the other pages as the crawl stored them. If page A
gets a title of its own, A is fixed. If that new title matches an unrechecked page Z's stored title, A and Z are a new
cluster, which is what the stored data says.

### How a recheck runs

```text
Recheck asked (check, cause or URLs)
 ├─ the crawl's analysis is out of date? → analyse first (Simple: "Updating the results")
 ├─ plan the scope (read-only); refuse what can't run (below)
 ├─ CrawlWriter opens the file in recheck mode
 │    · creates the recheck tables if missing; stamps the crawl row
 │    · stores the recheck row and the snapshot: the check's issues (and issue_targets) on the scope's URLs,
 │      the check's count, the score, the URLs and their roles
 ├─ CrawlSession, recheck mode: hands the scope to the workers directly (never through the frontier)
 │    · phase 1, then phase 2 for what still fails; redirects to their end; new URLs one step
 │    · each record replaces the URL's old response, details, HTML and outgoing links atomically
 ├─ let go: URLs the recheck left with no way in (below)
 ├─ CrawlWriter closes: the crawl's status as it was; the recheck row ended (completed or stopped)
 ├─ CrawlAnalyzer analyses the whole crawl
 └─ IssueWriter, as it commits, settles every recheck not yet settled: each URL's outcome, the counts, the score after
```

**Let go.** Suppose the owner fixes a broken footer link by removing it. The 404 URL stays in the file with its 404,
and `internal-client-error` (which flags every fetched internal URL) would still flag it, though no fresh crawl would
find it.

- So, after fetching, each URL that lost an incoming link in the recheck is looked at. It is let go if no URL the
  crawl still has links to it (any kind of link: a redirect, a canonical, an image), no sitemap lists it, and it isn't
  the start URL or a probe.
- Being let go means its response, details, HTML and outgoing links are deleted, and it is marked skipped with
  reason `NoLongerLinked`. The row stays, so ids stay stable.
- What only it led to is looked at the same way, in turn. Pages that lead only to each other are let go together:
  the walk covers the part of the link graph the recheck changed, from what still leads into it. Past 10,000 URLs it
  falls back to a walk of the whole graph from the start URL and the sitemaps.
- The URL grids label a let-go URL "No longer linked (recheck 17 Nov)", and the stats leave it out of "found".

**Depths** aren't worked out again: a recheck can make a URL shallower (the writer already lowers depths), never
deeper. Depth is shown, and no check judges by it.

**Time.** A recheck's run is stored in `run_time` as a run, and `rechecks.run` names it. The crawl's time spent
crawling (`TimeCrawling`) leaves recheck runs out, so "crawled in 37 minutes" stays true, and each recheck says how
long it took. The history chart marks a recheck's run. A rechecked URL's fetch time moves to the recheck, as any URL
fetched twice counts from its last fetch.

### Pacing, secrets, concurrency

- **Pacing.** The crawl's stored options (concurrency, rate, crawl-delay, user agent, timeouts) through the same
  `HostGate`. It gets `ui/3b-polite`'s slow start and learned pace once that branch is merged: a 5-URL recheck at
  slow start takes 5 to 10 s. If polite keeps a learned pace in the crawl file, a recheck starts from it.
- **A site that stops answering** ends a recheck after one minute, not ten. Its unfetched URLs are "couldn't
  recheck", and the analysis still runs.
- **Secrets.** Header values and proxy credentials aren't in the file, so a recheck of a crawl that had them answers
  `needs-secrets`, exactly as Continue does, and the UI asks with the same dialog. "Continue without" is offered as it
  is there.
- **Refused** (each a problem type, and on the plan as `refused`):
  - the crawl is live here (running, paused, stopping, analysing, rechecking) or in use elsewhere (`crawl-busy`);
  - another crawl of the same `siteKey` is live here (`site-busy`): "A check of this site is running. Its results will
    show what's fixed." Each run paces its hosts alone, so two would double the load on the site;
  - it isn't the site's latest crawl (`recheck-not-latest`): "Recheck works on the site's latest check." Rechecking an
    older crawl would rewrite what the newer one compares with;
  - the check's `recheck` is empty (`recheck-unsupported`), or the check was skipped;
  - the crawl was never analysed (`not-analyzed`), or is interrupted (`not-analysable`).
- **One at a time per crawl.** The buttons are disabled while one runs. Rechecks of two different sites can run at
  once, beside crawls of other sites.
- **Stop** is the crawl's Stop (the top bar, `POST /stop`). What was fetched stays, the analysis runs, and the result
  says "Stopped: 3 of 5 rechecked".
- **The app quits mid-recheck.** The file's status is unchanged and the recheck row is left unended. The crawl reads
  out of date (`crawlChanged`) with Update the results. That analysis settles the recheck as `interrupted`, with what
  it fetched.
- **Exports** running on the crawl end with `export-crawl-changed`, as they do on Continue.

### The result: "4 of 5 fixed"

The analysis that follows settles each URL the recheck set out to judge (the check's flagged URLs, or the group's):

| Outcome | When |
| --- | --- |
| `fixed` | The recheck read the URL (or, for a `sources` check, let it go as no longer linked, or it now answers properly) and the check no longer flags it |
| `still` | The check still flags it |
| `notChecked` | The recheck couldn't read it: no answer, an error that isn't the check's own fault, blocked, rate-limited, robots.txt now refuses it, it now redirects elsewhere ("now forwards to /new/"), or the check was skipped by the analysis. Never `fixed` |
| `notRechecked` | Past the cap, or the recheck was stopped before it |

Also counted: **new**, URLs in scope the check didn't flag before and flags now ("and 1 page has it now that didn't:
/y"), and the **score** before and after.

The result is kept (`recheck_urls.outcome`, the counts on `rechecks`) and isn't worked out again by later analyses:
it records what that recheck found. What the crawl says *now* is in its issues as always.

### What the crawl file keeps

Crawl-time tables the writer creates when it first rechecks a file, as it creates `notices` and `run_time`. The
schema version stays 2, so either engine reads and continues either file:

```sql
CREATE TABLE IF NOT EXISTS rechecks (
    id INTEGER PRIMARY KEY,              -- 1, 2, ... in the file
    run INTEGER NOT NULL,                -- the run_time run it was made in
    started_at INTEGER NOT NULL, ended_at INTEGER,
    status INTEGER NOT NULL,             -- running, completed, stopped, failed, interrupted
    check_id TEXT,                       -- null for a list of URLs
    group_detail TEXT, group_related_url_id INTEGER,
    judged INTEGER NOT NULL,             -- URLs whose result it gives (the flagged ones)
    fetched INTEGER NOT NULL DEFAULT 0,  -- requests made
    count_before INTEGER, score_before INTEGER, score_after INTEGER,
    fixed INTEGER, still INTEGER, not_checked INTEGER, not_rechecked INTEGER, new INTEGER,
    settled_at INTEGER                   -- when an analysis settled it; null until then
);
CREATE TABLE IF NOT EXISTS recheck_urls (
    recheck_id INTEGER NOT NULL, url_id INTEGER NOT NULL,
    role INTEGER NOT NULL,               -- judged, named, referring, new (one step), letGo
    flagged_before INTEGER NOT NULL, detail_before TEXT, related_before INTEGER,
    outcome INTEGER,                     -- set when settled
    PRIMARY KEY (recheck_id, url_id)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS recheck_targets (   -- the check's issue_targets on the scope, before
    recheck_id INTEGER NOT NULL, url_id INTEGER NOT NULL, related_url_id INTEGER NOT NULL,
    PRIMARY KEY (recheck_id, url_id, related_url_id)
) WITHOUT ROWID;
```

`SkipReason` gains `Recheck = 7` (found by a recheck, not followed) and `NoLongerLinked = 8` (let go). An earlier
engine reads them as skipped URLs, which they are.

### Afterwards

- **The crawl's own screens** show its new state: counts, groups, score and report.
- **The library item** carries `rechecks: { count, last: { id, endedAt, checkId, judged, fixed } }`.
  - Simple's row: "265 pages · rechecked 5 on 17 Nov".
  - Advanced's status cell tooltip, and Overview's "crawl in a line": "Rechecked 3 times, last on 17 Nov".
- **The comparison with the previous crawl** includes what rechecks fixed. For a first crawl, with nothing to
  compare with, What to fix's "Fixed" group is filled from its rechecks: checks whose count before a recheck was more
  than 0 and is 0 now.

## 4. The UI

The owner's look holds throughout: the framed workspace, the current place as a neutral fill and bold text (no
accent rule), and strong segmentation (cards with a 1 px edge and a ruled header). Changes are **text with a glyph,
never colour alone**: "▲14", "▼3", a tick for fixed. Red, amber and green keep their meaning (severity and status).
Score movement is neutral text: not green, not red. Simple keeps its rule of no chips and at most two labels a row,
so a change is a line of text, not a third label.

### Words

To add to [the glossary](v2-ui-discovery.md#glossary) before the app uses them (the glossary's rule):

| Term | Meaning | Not |
| --- | --- | --- |
| Previous crawl (Simple: your last check) | The crawl a crawl is compared with: the site's last before it, or the one picked | baseline, last run |
| Compare with | Advanced's control that picks it | diff |
| Recheck | Fetch some of a crawl's URLs again and update its results | re-crawl, re-spider, verify, retest |
| New | Flagged now, not then | added |
| Fixed | Flagged then; read again now and not flagged | resolved, removed, closed |
| No longer linked / No longer found | Flagged then; this crawl didn't find the URL (for a 404 or redirect, the fix; for a page, it's gone) | missing |
| Still there | Flagged then and now | remaining, open |
| Not checked this time | Flagged then; not read now, so not known | unknown, skipped |
| Fewer, More | A check's count went down or up | improved, worse |
| Not looked for last time | A check that didn't run on the previous crawl (Advanced: New check) | new problem |

### Simple

**Home, Your sites** (`simple/home/SiteRow.tsx`). Beside the ring, under its word: "▲14 since 3 Nov". Under "To
fix": "3 fixed · 1 new since 3 Nov". A first crawl, or a counts-only comparison without a score change, shows
neither. A stopped crawl's line adds "so far".

```text
 [◔ 85 Good ]  gardenfurnituresets.co.uk                2 serious, 4 worth fixing      Checked 17 Nov  ›
   ▲14 since   265 pages · rechecked 5 on 17 Nov       3 fixed · 1 new since 3 Nov
   3 Nov
```

**The Summary** (`simple/SummaryScreen.tsx`). One line under the sentence, in the score's card, each part a link:

```text
 ( 85 )  Good    Your site is in good shape. There are 2 serious problems and 4 problems worth fixing.
                 Since your last check on 3 Nov: score 71 → 85, 3 problems fixed, 1 new.  See what changed
                 [ 2 serious ] [ 4 worth fixing ] [ 13 worth a look ] [ 63 nothing wrong ] [ 4 couldn't check ]
```

- "3 problems fixed" opens What to fix at its Fixed group. "1 new" opens What to fix with the new problem
  highlighted (`.si-current`, as J/K does). "See what changed" opens What to fix at the top, where the change lines
  are.
- A partial comparison adds one line: "Your last check covered more of the site (265 pages against 50 now), so some
  of it couldn't be compared." Counts-only: "This check started at https://…, the last at http://…, so only the
  totals are compared."
- **Fix these first** rows show the change in their count cell's second line: "5 of 265 pages", then "9 on 3 Nov".
- **How the score is worked out** (`ScoreBreakdownDialog.tsx`). Each line gets its points then ("−4 · was −7"), and a
  folded "Fixed since 3 Nov" list of the lines that took points then and none now.
- **The finished toast** and notification: "gardenfurnituresets.co.uk is checked. Score 85 (▲14 since 3 Nov): 3
  problems fixed, 1 new", with See the summary and Open the report.

**What to fix** (`simple/WhatToFixScreen.tsx`). Rows keep their two labels at most (severity, One fix). The change
goes in the count column's second line:

```text
 Serious (2)
 ⬣  Links that lead to broken pages                                   5 of 265 pages
    Some links on your site go to pages that don't work.               9 on 3 Nov
 ⬣  Your sitemap can't be read                                       checked once
    Search engines can't use the list of your pages.                  New since 3 Nov
 Worth fixing (4)
 ▲  Images that are too large                                       12 of 265 pages
    …                                                                 same as 3 Nov
 …
 ▸ Fixed since your last check on 3 Nov (3)                                         ✓
     ✓ Pages that don't work · 9 addresses on 3 Nov
     ✓ Your site isn't secure (no HTTPS) · whole site on 3 Nov
     ✓ Titles that are too long · 14 pages on 3 Nov
 ▸ Show the 63 checks that found nothing wrong
```

- **Fixed** is folded below the severities and above Nothing wrong, with its count. Each opens the problem page,
  which says "Fixed since 3 Nov" and lists what was fixed.
- "Not looked for last time" replaces "New since" for a `newCheck`. Such a check is never counted in "1 new".
- Hidden checks stay out of every count here, as they do everywhere.
- **Copy this list** adds "(new since 3 Nov)" or "(was 9)" after a count.

**One problem** (`simple/ProblemScreen.tsx`, `WhereItIs.tsx`).

- **Head.** The count line adds "was 9 on 3 Nov". For a fixed check, the head reads "Fixed since your last check on 3
  Nov" with the good-news line.
- **Where it is.** Each cause gets a muted line, "Still there since 3 Nov" or "New since 3 Nov". Below the causes, a
  folded "Fixed since 3 Nov (4)" lists the fixed causes, each with a tick and "Fixed" or "No longer linked from any
  page". There is no strikethrough, since screen readers don't announce it and it reads poorly. "Not checked this
  time (2)" is folded beside it, with the reason.
- **The Fixed it? card becomes the recheck**, in place of "Check the site again":

```text
 ┌ Fixed it? ───────────────────────────────────────────────────────────────────────────────┐
 │ Recheck these 5 pages to see whether it's gone. It reads only them and the addresses     │
 │ they link to: about 20 seconds. Other pages stay as the check found them on 3 Nov.       │
 │ [ Recheck these 5 pages ]    Check the whole site again                                  │
 └──────────────────────────────────────────────────────────────────────────────────────────┘
 running
 │ Rechecking… 3 of 8 addresses read                                ▓▓▓▓▓▓░░░░   [ Stop ]  │
 │ then: Working out the results…                                                          │
 result, some fixed
 │ 4 of 5 fixed. 1 still has it:                                                            │
 │   Garden sofas · /sofas/ · still links to /old-range/ (not found)        [Open]          │
 │ Score 85 → 88.  Rechecked today at 14:02.                    [ Recheck again ]           │
 result, none fixed
 │ None of the 5 is fixed yet. If you changed it in the last few minutes, your site may      │
 │ be showing a saved copy (a cache): try again in a few minutes.          [ Recheck again ] │
```

- **The button's words** follow the check's `subject`: "Recheck these 9 addresses". On a group: "Recheck this one (264
  pages)". Past the cap: "Recheck the 1,000 most important pages (about 9 minutes)", with "or check the whole site
  again (about 37 minutes)".
- **The time** comes from the crawl's own pace (URLs crawled over time spent crawling), at least the slow start's,
  plus the last analysis's time. It is rounded: "about 20 seconds", "about 2 minutes".
- **Every result:**
  - all fixed: "All 5 fixed. This problem is gone from the list." The page then turns into the fixed state;
  - stopped: "Stopped: 3 of 5 rechecked. 2 fixed, 1 still has it.";
  - couldn't recheck: "3 of 5 fixed. 2 couldn't be rechecked: the site didn't answer.";
  - new ones: "… and 1 page has it now that didn't before: /y".
- **A check that can't be rechecked** (`recheck` empty): "This one needs the whole site read again to see a fix:
  Check the site again." A skipped check has no card.
- **An earlier check** (not the site's latest): "This is an earlier check. Open the latest check of this site to
  recheck it."
- **While another check of the site runs:** the button is disabled, with "A check of this site is running. Its
  results will show what's fixed."
- **Secrets:** the Continue dialog, worded for a recheck.
- **The top bar's status pill** reads "Rechecking", and Stop is there as for a check. The sidebar's "Checking now"
  lists it ("Rechecking gardenfurnituresets.co.uk"). Leaving the page doesn't stop it, and a toast says the result
  when it ends: "Recheck done: 4 of 5 fixed", with See it.

**The report.** See [The report and the workbook](#the-report-and-the-workbook).

### Advanced

**Home, the library** (`screens/home/LibraryTable.tsx`). A **Score** column (the audit's library gap), "85 ▲14", and
the folded older crawls each with their own score. The crawl menu gains **Compare with…**, which opens the latest
crawl's Overview with that crawl picked. **The crawl switcher** (`layout/CrawlSwitcher.tsx`) adds the score to each
row.

**Overview** (`OverviewScreen.tsx`). A "Since 3 Nov" card row under the totals, with the Compare with… control at its
right:

```text
 ┌ Since 3 Nov, 09:12 (4,480 URLs, completed) ─────────────────────── Compared with 3 Nov ▾ ┐
 │ Score 71 → 85  ▲14 │ Errors 3 → 2   ▼1 │ Warnings 5 → 4   ▼1 │ Notices 13 → 13          │
 │ 5 checks fixed · 2 new · 6 fewer · 1 more · 1 new check                                   │
 │ 41 issues fixed · 6 new · 3 not checked this time                    Differences (2) ▸    │
 └───────────────────────────────────────────────────────────────────────────────────────────┘
```

- Each figure opens Issues filtered to it (New, Fixed, Fewer, More).
- "Differences (2)" unfolds the comparability lines.
- Fix first's rows gain "was 9".
- A first crawl has no card.
- While the previous crawl needs analysing: the card is in counts-only form, with "Analyse the 3 Nov crawl again to
  compare like for like" and the action.

**Issues, the checks list** (`screens/issues/ChecksList.tsx`, `checkList.ts`).

- A **Since 3 Nov** column: "−5", "+2", "0", "New", "New check", "Not checked". The tooltip carries the split ("5
  fixed, 0 new, 2 not checked this time").
- **Filter chips** beside the severity filters: New, Fixed, Fewer, More, Changed (any of them). Each is a chip that
  removes itself, as the category chip does.
- Checks fixed since then (now 0) aren't in the default list, since they passed. They sit in their category under
  "Fixed since 3 Nov" with a tick, and in the Fixed filter.
- The sort the audit asked for (Fix first, count, points, severity) gains **Change** (largest drop first).

**Issues, a check's detail** (`CheckDetail.tsx`, `IssueGroups.tsx`, `IssueRowsGrid.tsx`).

- **The head** adds "9 on 3 Nov" beside the count, and **Recheck 5 URLs** beside Export. Its menu (▾) holds "Recheck
  this group" for the open group and "Recheck selected URLs" for the grid's selection.
- **Groups** gain a "3 Nov" column (URLs then) and a state cell: "New", or "−3", "+1", "0". A tab pair over the groups
  reads "Now (12) · Fixed since 3 Nov (4)". The Fixed tab lists the fixed causes with their URLs then.
- **Rows** gain a **Since 3 Nov** column (`change`: New, New URL, Still, Not compared). The Fixed tab lists the rows
  flagged then and not now, with their state (Fixed, No longer linked, No longer found, Not checked this time), the
  detail then, and Open in browser. A fixed row whose URL the crawl still has opens the inspector.
- **A recheck** shows its progress as a banner over the detail (the analysis-progress banner's shape), then its
  result: "Rechecked 17 Nov 14:02: 4 of 5 fixed, 1 still (/sofas/), 0 new. Score 85 → 88". Each count filters the
  rows to it.

**Overview's Rechecks card** (only when there are any): each recheck with when, what (check and group, or "6
selected URLs"), how long, and its result. Each opens its rows (`/rechecks/{id}/rows`).

**URLs and the inspector.** The URL grid's selection menu and the inspector's menu get **Recheck** (the list scope,
`urlIds`, at most 1,000). Let-go URLs are labelled and hidden by the default view, as probes are.

**Crawl settings.** A recheck uses these settings, and the screen says so: "Rechecks use these settings."

### The report and the workbook

- **The report** (`Reporting/ReportDocument.cs`) gains part **2a, "Since your last check on 3 November"**, after the
  Summary and before Fix these first, when a comparison exists:
  - the score then and now;
  - "Fixed" (each problem, with its count then);
  - "New" (each, with its count now);
  - "Fewer" and "More" (title, then and now);
  - the rechecks since the crawl ("Rechecked on 17 Nov: 5 pages, 4 fixed").

  The **summary cut** has the score line and the three counts. A partial comparison says what differs in one
  sentence, and a counts-only one gives the score and the counts. Part 8, About this check, gains "Compared with the
  check of 3 November 2026", and the rechecks. `GET /report?compareWith=previous|none|{id}`, `previous` by default.
- **The workbook**'s summary sheet gains "3 Nov" and "Change" columns, and a sheet "Fixed since 3 Nov" (check, URL,
  state, detail then).

### The states, at a glance

| State | Simple | Advanced |
| --- | --- | --- |
| First crawl of a site | Nothing: no line, no zeros | No card, no column; Compare with… shows "No other crawl of this site" |
| Comparison loading | The Summary without the change line; it appears without moving what's above | The card's skeleton; the column's cells blank |
| Previous needs analysing | Simple analyses it by itself; the line appears when done | Counts-only card with Analyse it again |
| Counts only (start origin, rules) | Score change and problem counts, one caution line; no row marks | Card with Differences open; no row column |
| Partial | Lines as usual, one caution line; "not checked this time" where it applies | Card with Differences; row states as usual |
| This crawl is live | Nothing until its results are worked out | Same |
| Previous file deleted | The next older one is used, silently | The picker shows it disabled: "File not found" |
| Recheck running | The card's progress, the pill, Checking now | The banner, the pill, the sidebar |
| Recheck needs secrets | The Continue dialog | Same |
| Recheck refused | The button disabled with the reason as its line | Same, as the tooltip and the line |

## 5. Edge cases

| Case | What happens |
| --- | --- |
| First crawl of a site | No previous: `previous` null. Nothing is shown. Its rechecks still give "4 of 5 fixed", and What to fix's Fixed group |
| The previous crawl was stopped early | A candidate, compared as `partial` with `stoppedEarly`. Its unread URLs are `notComparedThen` on new rows, so "new" is never claimed for a URL it didn't read |
| This crawl was stopped early | `partial`. Rows it didn't read are "not checked this time", never fixed. A check reaches `fixed` only with nothing left unchecked. Lines say "so far" |
| Interrupted crawl | Can't be the previous (it can't be analysed). As this crawl, it has no comparison until continued and analysed |
| Failed crawl | A candidate only if it has an analysis that read pages |
| An older engine's analysis | `previousNeedsAnalysis` with `engineChanged`, then analysed again (Simple by itself, Advanced on a click). An analysis from before the score (no score) can't give a score change until then |
| A check that didn't exist last time | `newCheck`: "Not looked for last time". Never counted as a new problem |
| A check skipped then, run now (or the reverse) | `newCheck` (then skipped), `notCheckedNow` (now skipped), each with its reason |
| Thresholds changed in Settings since | `settingsChanged` on the previous: analysed again, like for like |
| Previous crawl deleted or removed | The next older candidate. None: no comparison |
| Very different sizes | `coverage` difference. Most rows read "not checked", so nothing is claimed as fixed |
| Start URL moved to HTTPS or www | `startOrigin`: counts only. `no-https` reads as fixed at the check level |
| Two scopes of one site, alternating | The same-start-URL rule pairs each scope with its own |
| The site is crawled while a recheck runs | Refused (`site-busy`), whichever comes second |
| A recheck on a crawl at its URL limit | Allowed. Its one-step URLs aren't held to the limit; they are few and recorded as the recheck's |
| A rechecked page now redirects or errors | `notChecked` for the check, with what it does now. The response-code check flags it as new if it should |
| The fix is still in a cache (CDN) | The recheck sees the old page: `still`. The none-fixed line suggests trying again in a few minutes |
| Hidden checks | Left out of every count and list in the app, the change counts included. The engine's answer has them all |
| Same second | Two crawls of a site started in the same second are ordered by id, as Home orders them |

## 6. Engine and storage

| Part | Where | What |
| --- | --- | --- |
| Comparison reader | `Storage/CrawlComparisonReader.cs` | `ReadSummary`, `ReadRowChanges`, `ReadGroupChanges`, `ReadCleared`. Read-only, two snapshots, a cancellation token, a `ReadCache` keyed by both stamps. Models in `Storage/Models/` (`CrawlComparison`, `CheckChange`, `RowChange`, `GroupChange`, `ClearedIssue`, `ComparisonDifference`), one per file |
| Comparability | `Analysis/CrawlComparability.cs` | Pure: the differences and `comparable` from two crawls' options, analyses and coverage |
| Grid readers | `IssueGroupReader.ReadGroups`, `ReadRows` | An optional `compareWith` path: each group or row gains its change, by `CrawlComparisonReader`. The order and columns are as without it |
| Catalog | `IssueDefinition.Recheck` | What a recheck re-reads (table above). Not stored, not fingerprinted. A test holds every check to having a value, and `orphan-page` and `no-https` to `[]` |
| Recheck plan | `Storage/RecheckPlanReader.cs` | Read-only. The scope by `references`, phases, roles, the cap by importance, and the estimate. Models `RecheckPlan`, `RecheckScopeUrl` |
| Recheck run | `CrawlEngine.Recheck(RecheckRequest, observer)` returns a `CrawlJob` | `RecheckRequest(path, plan, options with secrets)`. `CrawlSession` in recheck mode: no seeding, no sitemap discovery unless the plan's kinds include `sitemaps`, the plan's URLs handed to the workers directly, phase 2 fed as phase 1 settles, one-step URLs, redirects to the end, site-down after one minute |
| Writer | `CrawlWriter.OpenForRecheck` | Lock; the recheck tables; the snapshot; stamp; records replace atomically; `Recheck` and `NoLongerLinked` skips; the let-go walk at close; the status left as it was; the recheck row ended |
| Settling | `IssueWriter` | As it commits, each unsettled recheck gets its outcomes and counts, `score_after` and `settled_at`. An interrupted one is settled as `interrupted` |
| Readers | `Storage/RecheckReader.cs` | The rechecks (`ReadAll`) and one recheck's URLs (`ReadRows`, paged) |
| Run time | `CrawlRunReader` | Leaves recheck runs out of `TimeCrawling` and lists them as `Recheck` runs |
| Stats and grids | `CrawlStatsReader`, `UrlColumns` | `NoLongerLinked` URLs left out of "found", and labelled. `Recheck` skips labelled "Found by a recheck" |
| CLI | `si compare <crawl> <previous> [--check id] [--json]`, `si recheck <crawl> --check id [--group-url u \| --group-detail d] [--urls file]` | For tests, agents and scripts |
| Freshness | `AnalysisFreshness.VERSION` | **Unchanged.** A crawl without rechecks analyses to what it did. Only rechecked files hold the new skip reasons, and settling is new rows, not new judgements |

Rules kept: only `Storage/` writes; `CrawlWriter` during the recheck run and `IssueWriter` for the analysis, never at
once; nothing parses HTML twice (`HtmlPageParser` once per fetched page); issue ids unchanged.

## 7. API

The contract is in [the sidecar API](v2-sidecar-api.md), marked "(fix loop)". In brief:

| Call | What |
| --- | --- |
| `GET /api/crawls` items | `siteKey`, `previous`, `changes`, `rechecks` |
| `GET /api/checks` | `recheck` on each check |
| `GET /api/crawls/{id}/changes?with=` | The comparison: comparability, score, every check's change |
| `POST …/issues/{checkId}/groups`, `…/rows` | `compareWith` in the body adds `change` to each group and row |
| `POST …/issues/{checkId}/fixed` | What the check flagged then and not now: rows or causes, with their states |
| `POST …/rechecks/plan` | What a recheck would fetch, how long it would take, and whether it can run |
| `POST …/rechecks` | Start one (202) |
| `GET …/rechecks`, `GET …/rechecks/{n}`, `POST …/rechecks/{n}/rows` | The crawl's rechecks and their URLs |
| Events | Crawl status `rechecking`; `progress` with phase `rechecking` and `recheck`; a `recheck` event when it is settled |
| `GET …/report?compareWith=` | The report's "Since your last check" part |

## 8. Performance

Budgets ([the UI brief](v2-ui-brief.md#performance-budgets)): first page of a grid 300 ms; opening a 100k crawl to
Overview 1 s; sort or filter 300 ms at 100k and 1 s at 1M (p95).

| Read | Cost | Budget it must meet | How |
| --- | --- | --- | --- |
| Library and Home changes | None: from cached summaries | Listing as today | Per-check counts and an options fingerprint in `summaries.json` |
| `/changes`, check level | Two small tables per file | Inside Overview's 1 s | `issue_types`, `skipped_checks`, `analysis` |
| `/changes`, row split | One streamed pass per file over `issues ⋈ urls`, then point lookups for rows that moved: 32,406 issues a side on the 100k benchmark, 323,000 on the 1M crawl | 300 ms p95 at 100k; 1 s at 1M | Measured by `si readbench --compare-with`, new class `compare`. Cached by both stamps; Overview draws the check level first |
| `/groups`, `/rows` with `compareWith` | At most 2 seeks a group or row of the page (100) | Within the grid's 300 ms with it | Point lookups on `urls.url` (UNIQUE) and the issues' keys |
| `/fixed`, first page | The check's join (cached), then a page | 300 ms p95 for the biggest check at 100k | The pass kept in the read cache for the two most recent comparisons |
| Recheck plan | The check's rows by importance, `issue_targets`, inlinks of the flagged URLs (`ix_links_to`) | 300 ms for 1,000 URLs | Indexed reads |
| Recheck run | The scope at the site's pace, plus opening the file as Continue does (it reads every URL key: about 1 s at 100k, 10 s at 1M) | Said as an estimate | Slow start, learned pace |
| Analysis after | A full analysis: under 1 s for a small site, 7 to 20 s at 100k | As today | Unchanged |
| Let-go walk | The changed part of the link graph; the whole graph past 10,000 URLs (seconds at 1M) | Part of the recheck's close | `ix_links_from`, `ix_links_to` |
| Memory | The largest check's previous rows at about 40 bytes a URL, per comparison read; two comparisons cached | Sidecar flat over a session | Streamed by check, with no whole-file materialisation |

Writes: the snapshot is at most 1,000 rows plus their targets, in one transaction. Settling is one statement per
recheck.

## 9. Test plan

### The synthetic site gets revisions

`SyntheticSiteOptions.Revision`: 0 (as today) and **1, "after the fixes"**.

- It can be switched on a running server: `SyntheticSiteServer.Revision` in process, and for a CLI-run site,
  `si testsite --revision 1`, plus a loopback control route `POST /__si/revision/{n}`, guarded by a token printed with
  the ready line. The app's e2e tests can then switch it between two crawls.
- `GetExpectations(revision)` gives each revision's exact issues table, as today.
- **`GetChanges(0, 1)`** gives the exact comparison: each check's state and counts, and each row's state. The tests
  compare the whole changes table, as they compare the whole issues table.

Revision 1 plants every state, each on its own URLs and planted at fixed intervals as the products are:

| Planted in revision 1 | Proves |
| --- | --- |
| Some broken link targets answer 200 | Fixed at the target (`internal-client-error` and `broken-internal-links` fixed, both levels) |
| Some pages drop their link to a 404 that nothing else links to | `gone` as "no longer linked". A recheck lets the 404 go |
| Some product titles shortened | `title-too-long`: `fewer`, rows `fixed` |
| One page gains a second H1, where no page had one | Check-level `new` (`h1-multiple`) |
| One new product page with a long title | `newUrl` |
| One product removed and unlinked | `gone`: "no longer found" |
| One page with a long title answers 503 | `notChecked` for `title-too-long`, `new` for `internal-server-error`. Not fixed |
| An oversized image replaced by a small one | `image-too-large`'s cause fixed |
| A redirect chain shortened | `redirect-chain` fixed. A recheck follows the chain to the end |
| A noindex removed | `noindex-page` fixed |
| A duplicate title cluster's first page retitled | The cluster matched by its pages, not its name: `fewer` |
| An hreflang return link added on the alternate | A `targets` recheck fetches the named alternate |
| robots.txt unblocks one URL | `blocked-by-robots` fixed. The recheck reads robots.txt again |
| A page's link changed to a new URL that 404s | The one-step fetch: the recheck reads `still`, not fixed |

Cases that can't be planted on a running site are built on copies of crawl files:

- a previous analysis without a check (`newCheck`), made by removing it from `issue_types` and `skipped_checks`;
- an older analysis version (`previousNeedsAnalysis`, then like for like);
- other settings (`settingsChanged`);
- a stopped previous or current crawl (`partial`);
- a URL limit (`limits`);
- the HTTPS variant against plain HTTP (`startOrigin`, counts only);
- a deleted previous file (the next older one used).

### Engine tests (each seen failing first)

- **Comparison.** Revision 0 against revision 1, compared exactly with `GetChanges`. Every row in the states tables
  above has a case. Duplicate clusters are matched by their pages. Group-level changes are tested against `/groups`
  on each file. "Not checked isn't fixed" is a property over every check: no row whose URL the current crawl didn't
  read is ever `fixed`.
- **Recheck.**
  - Crawl revision 0, switch to 1, recheck each check revision 1 fixes. Assert the result exactly ("k of n fixed"),
    and that the file's issues for the rechecked URLs are revision 1's while every other URL's page-check issues are
    revision 0's.
  - The let-go walk, including two pages linking only to each other.
  - One-step URLs fetched, and the second step recorded and not fetched.
  - A stopped crawl's frontier untouched, and its status kept.
  - `updated_at` stamped: out of date, then fresh after the analysis.
  - Settled once, and not rewritten by a later analysis.
  - Stopped mid-way; interrupted (the process killed: the next analysis settles it as `interrupted`).
  - Refused while live, while the site is live, when not the latest, for `orphan-page`, and with secrets missing.
  - Each `recheck` kind (`robotsTxt`, `siteVersions`, `sitemaps`) on its planted case.
  - The cap, by importance.
  - Polite pacing, counted from `SyntheticRequestLog`: never more than the crawl's own rate.
  - Recheck runs left out of `TimeCrawling`.
- **Catalog.** Every check has a `Recheck`. A test fails a new check without one.
- **Compatibility.**
  - A rechecked file is read, continued and analysed by this engine.
  - Its new tables and skip reasons are read by a reader that predates them (the readers check for the tables, as
    with `notices`).
  - A crawl without rechecks analyses to exactly what it did before (the 100k bench's issues table, unchanged).

### Sidecar tests

- Shapes and errors of every new call.
- `siteKey` agrees with the app's old grouping on the library fixtures, ports included.
- The previous crawl chosen, including same start URL first, a missing file and a removed crawl.
- The library's `changes` worked out from cached summaries alone, with no file opened (a counting reader).
- The recheck flow end to end on the in-process synthetic site: out of date, analysed first, then rechecked and
  settled, with the events in order.
- Stop. Concurrency refusals.

### App tests

- **Unit tests for the words:**
  - "▲14 since 3 Nov", "9 on 3 Nov", "New since 3 Nov", "Not looked for last time";
  - "4 of 5 fixed. 1 still has it", "All 5 fixed", "None of the 5 is fixed yet";
  - the time estimates;
  - every state in [the states table](#the-states-at-a-glance), in both modes.
- **Stories** for every state, in both themes, plain, forced colours and reduced motion. axe clean.
- **e2e** (Playwright, on the synthetic site with revisions):
  - crawl, switch to revision 1, crawl again. Then the Summary's line, What to fix's Fixed group, the problem page's
    causes, Overview's card, the Issues column and filters, and the Fixed tab, each against `GetChanges`;
  - recheck from the problem page, to "k of n fixed" and the page's new count;
  - recheck from Issues with a group and a selection;
  - a first crawl showing nothing.

### Bench and gates

- `si bench --pages 100000 --revisions` crawls revisions 0 and 1 and holds the comparison to `GetChanges`.
- `si readbench --compare-with` gates the `compare` class (summary, rows with `compareWith`, the first page of
  `/fixed`).
- **Run locally only** (never push):
  - `verify-engine.sh` and `verify-app.sh` (`docs/handoff/2026-11-13/tools/`);
  - `smoke:shell`;
  - the Linux Docker check (`docs/handoff/2026-11-14/tools/`) if `app/src-tauri/` changes. It shouldn't.

## 10. Implementation plan

Stages sized for one agent each, in worktrees off `ui/step-3`, each merged with its checks green before the next that
depends on it. Never push.

| Stage | Scope | Depends on | Done when |
| --- | --- | --- | --- |
| **E1 Test site revisions** | `SyntheticSiteOptions.Revision`, revision 1's plants, `GetExpectations(1)`, `GetChanges(0, 1)`, live switching (in process, `--revision`, the control route), and e2e expectations for revision 1 alone | `ui/3b-evidence` and `ui/3b-accuracy` merged (they change expected issues) | Revision 1 crawls to exactly its expectations; revision 0 unchanged; the bench unchanged |
| **E2 Comparison reader** | `CrawlComparability`, `CrawlComparisonReader` (summary, rows, groups, cleared), `compareWith` in `IssueGroupReader`, `si compare`, readbench class `compare`, `si bench --revisions` | E1 | `GetChanges` met exactly; every case in the test plan's comparison list; budgets met at 100k |
| **E3 Recheck engine (URLs)** | `IssueDefinition.Recheck` for all 87; `RecheckPlanReader`; recheck mode in `CrawlEngine`, `CrawlSession` and `CrawlWriter` (snapshot, atomic replace, one step, redirects, let go, status kept); `IssueWriter` settling; `RecheckReader`; `CrawlRunReader`; the skip reasons in stats and grids; `si recheck`. The `urls` kind | E1; `ui/3b-polite` merged (both change `CrawlSession` and the host gates) | Every `urls` check rechecked on revision 1 to its exact result; the run tests above |
| **E4 Recheck kinds** | `robotsTxt`, `siteVersions`, `sitemaps` (reading sitemaps again in place: the sitemap tables replaced for those read, newly listed URLs as `Recheck` skips) | E3 | Each kind's planted case |
| **S1 Comparison in the sidecar** | `siteKey`; the previous crawl chosen; summary cache fields; `previous` and `changes` on items; `/changes`; `compareWith` on `/groups` and `/rows`; `/fixed`; `recheck` on `/checks`; report and workbook `compareWith` (engine `ReportDocument` part 2a and the workbook's sheet) | E2 | Sidecar tests; the report's part on revision 0 against 1 |
| **S2 Rechecks in the sidecar** | `/rechecks/plan`, `/rechecks`, the reads, `rechecking` status, events, the flow (analyse first, recheck, analyse, settle), refusals, secrets, Stop | E3 (E4 for its kinds), S1 | Sidecar tests; events in order |
| **U1 Simple** | Home, Summary, score breakdown, toast, What to fix's lines and Fixed group, the problem page's causes and the recheck card in every state; `sites.ts` on `siteKey`; app types by hand (`app/src/api`) | S1, S2 | Unit tests, stories and axe, e2e for Simple |
| **U2 Advanced** | Library score column and Compare with…, the switcher's score, Overview's card and Rechecks card, Issues column, filters, sort and Fixed tabs, rows' column, Recheck in Issues, URLs and the inspector, the picker and its preference | S1, S2 | The same for Advanced |
| **V Verify and show** | The full local gates; bench and readbench before and after; the garden crawl rechecked for real only with the owner's word (it is the owner's site); gallery shots of every new state; the docs' status updated | All | Green, measured, shown |

U1 and U2 can run side by side once S1 and S2 are merged (Simple in `app/src/simple/`, Advanced in
`app/src/screens/`). They share a `changes` helper (words and states) written first in U1. E2 and E3 can run side by
side after E1: E2 is in `Storage/` readers, E3 in `Crawling/`, the writers and the catalog. They meet only in the
catalog, where E3 adds `Recheck`.

## 11. Known limits

- **URL matching is by text.** If an engine changes how it normalises URLs, those URLs read as gone and new across
  that change.
- **A rechecked crawl isn't one moment.** Its other pages are as the crawl found them. The screens say so where it
  matters.
- **Depths after a recheck** only ever get shallower.
- **A rechecked URL's fetch time moves** to the recheck, so the history chart counts it there.
- **Header values** aren't stored, so a crawl compared with one that sent other values can't tell.
- **Two orphans linking only to each other** aren't orphans to a recheck either: it can't confirm `orphan-page`, by
  design.
