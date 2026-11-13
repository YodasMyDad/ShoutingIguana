## Review fixes (2026-11-12 night; findings in S2\e6-review\findings.md)

- 72aaec3 findings 3+4 (split part never uses read's context; helper open failure -> one connection). Tests seen failing.
- 2b0d7e5 finding 2 (sniffer walks structure; TestImages whole). Seen failing on old sniffer.
- 5bec50e finding 5 (search start ranks; doc figures 106 B/URL, first search 55-89 ms p95). Seen failing (6 tests).
- 19f321f finding 1 (SplitGate in readbench --gate). Unit tests + real run: no-split build fails all 17 cases; normal passes.
- 890754f doc: invalid-settings keys.
- 5ec1724 MERGE ui/step-3 be294da (E5 export/report, catalog fixes v8). Conflicts: JSON context, client.ts, client.test.ts, doc end (E5 sections before E6, E6 last).
- d416329 finding 6 (mock e6.ts routes + tests; UrlGridQuery; AnalysisSettingsErrors). App green (1,326 tests).
- 280b368 docs: Engine changes from the E6 review. FINAL CANDIDATE.
- Final checks running on 280b368: build+test (S2\logs\test-review-final.log), then bench, read-links, readbench gate, app.
- Checks on 280b368 all green (1,382 engine + 252 sidecar + 28 CLI; bench, read-links, readbench gate incl. SplitGate).
- Item 7 (coordinator, after the review): be69ac9 engine (named statuses stored: sitemap error, robots reason/URL error,
  robots redirect, rate-limit reason; read-time blocked error), ffe354f app (failureOf/statusWords; tests/stories). Seen failing.
- FINAL = ffe354f. Final checks running: S2\logs\test-final2.log, then bench, read-links, readbench, app.
- 667924e readbench blocked-row cases from the origin that blocked the most (+ diagnostic); 38bdcae flaky running-crawl test made data-independent.
- FINAL = 38bdcae, all checks green: Release 0 warnings; 1,382 engine + 252 sidecar + 28 CLI; bench 100k; read-links 40k (log 81 MB);
  readbench --runs 20 --gate (205 reads, SplitGate passing); app npm ci/typecheck/lint/format:check/test (1,326). DONE, nothing pushed.

## Final stage (2026-11-12 evening, resumed after a usage stop; commits now Co-Authored-By Claude Opus 5.5)

- ffd1bca Docs: audit + checks paragraph in "Engine changes E6 made". HEAD = ffd1bca (final unless the reruns differ).
- Audit `S2\logs\audit-2.log`: 3,563 checks, 0 wrong (audit-1's 62 were the script's own groups model; fixed in audit.py).
- Pre-final on identical code: bench-2 OK (38.7 s, 2,613 p/s, 192 MB, +1 MB, 480 MB, analysis 11.3 s);
  readbench-2 --gate passed all classes; read-links bench (40k) OK, log 82 MB peak.
- Final reruns on ffd1bca: build+tests (S2\logs\test-final.log), then bench, read-links bench, readbench --gate, app checks.
- FINAL on ffd1bca, all green: Release build 0 warnings; tests 1,229 engine + 200 sidecar + 21 CLI; bench 100k Completed,
  every planted issue, 44.7 s, 2,263 p/s, 196 MB peak, -4 MB, 479 MB, analysis 11.6 s; read-links 40k Completed, log
  82 MB peak (memory +103 MB, unlimited in CI); readbench --runs 20 --gate passed all 205 reads (link-pass 2,390 of
  2,500: one-connection search, unchanged path, load); app npm ci/typecheck/lint/format:check/test (1,059) green.
- DONE. Report sent. Nothing pushed. Processes started: si-sidecar for the audit (shut down via its API).

# RESUMED 2026-11-12 by the finishing agent (this section is the newest; the HANDOFF below is the first agent's)

S2 = `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\8e7045a4-9ede-460a-be29-971bc406a6c0\scratchpad`
(my specs, mutations, logs, crawls). Brief: `S2\brief-e6-finish.md`. This section's source is `S2\resumed.md`.

## Done so far (all committed on ui/e6 unless marked, never pushed)

- 28c7287 MERGE of ui/step-3 (a782baf). One conflict (SidecarJsonContext converters: kept both). No AnalysisSettings
  limit was missing. Whole suite on the merged tree: 1,194 engine + 187 sidecar + 21 CLI, all passed. Release build 0 warnings.
- Item 6 DONE: 760c088 (engine), dc48531 (sidecar), ce7875d (cli readbench cases), 1f0713f (docs).
  Contract as the HANDOFF intended: `linkEnds { end: from|to, filters, search }` on /urls, /urls/facets, a check's
  /rows, /sitemaps/urls; `POST /links/ends { end, filters, search, offset, limit }` ->
  `{ end, total, links, offset, rows:[{id,url,links}], summary }`. /links and /links/facets refuse linkEnds (sidecar
  bad-request; engine field error `linkEnds`).
  Tests: LinkEndsTests (5), sidecar AViewOfTheLinksListsItsEndsAndTheUrlGridsNarrowToThem, the hammer (ends held to the
  finished file), CliTests readbench. Mutations seen failing: `S2\mutations\ends-source.py` (2 tests), `ends-grid.py` (4).
  Measured (`S2\logs\ends-1.log`, bench-1.sicrawl 100k, 20 runs, machine shared): targets from the summary 20-58 ms p95;
  pages (a pass) 475-740 ms on one connection, 155-280 ms split; targets of a text view 505-565 / 170-222.
- Item 3 favicon DONE: 57fcf5c (engine + test site), c9a2722 (sidecar). Design decisions:
  - first internal page a run parses (Crawl mode, HTML) names the icons (PageFacts.Icons, [JsonIgnore], rel=icon only);
    rank: smallest declared >= 64 px, then largest declared below, then undeclared in page order; 3 of the page's at
    most, then /favicon.ico at that page's origin; SVG never asked for.
  - kept: PNG/ICO/GIF/JPEG/WebP by signature, size from header <= 1,024 px a side, <= 512 KB. Sniffed again on read.
  - politeness: robots (when respected), host gate, pause, request timeout, 3 redirects, external only if
    CheckExternalLinks. Never a URL of the crawl. Completed crawl gives it 5 s (FINISH_GRACE), stopped/failed none.
  - storage: `favicon` table created on open; foreign shape -> none fetched; constraint failure -> logged, crawl goes on.
  - sidecar: `GET /api/crawls/{id}/favicon` 200 bytes / 204; `hasFavicon` on the library item; summaries.json FORMAT 5.
  - Tests: Favicons/FaviconSnifferTests (2), FaviconCandidatesTests (16 incl. theory rows), FaviconCrawlTests (12),
    sidecar FaviconEndpointTests (7), hammer reads the icon. Mutations seen failing (S2\mutations\favicon-*.py):
    robots, external, dimension, svg, first-page (HasFavicon), reader-sniff, grace (2 min run).
- bf32ad4: ReadSlots.TryTakeHelper looks again after taking (a race could take the last slot); ReadSlotsTests (4);
  mutation slots-last seen failing (the look-again itself under contention has no deterministic failing test).
- 7d974f3 App: TS types + client functions + tests (npm ci, typecheck, lint, format:check, test all green: 1,059 passed).
- 511aab8 PassHelpers 3 -> 7 (decided on the interleaved measurement), gate comment, LibraryTests format 5 + icon test.
- 4536ca0 Docs: Favicon section, split pass table, "Engine changes E6 made", README.
- Whole suite at 7d974f3 + PassHelpers 7: 1,229 engine + 21 CLI passed; sidecar 198 of 199 (the summaries format test,
  fixed in 511aab8; LibraryTests 13 pass after).

## Measurements

- `S2\logs\bench-1.log` (merged tree + item 6): Completed, every planted issue, 1,790 pages/s (loaded), 193 MB, +2 MB,
  DB 477 MB, analysis 19.5 s.
- `S2\logs\interleave-1.log`: base (S1\base-si) vs new x1 vs split x4 (3 helpers) vs x8 (7 helpers), 3 rounds of 10 runs,
  script `S2\interleave.sh`; binaries `S2\si-new` (as committed) and `S2\si-h7` (PassHelpers 7, built from a temp edit,
  reverted). Machine at ~16% CPU from other agents when started.

## Interleaved result (S2\logs\interleave-1.log), first page p50 over 3 rounds, ms

text common: base 778-914, new x1 665-934, x4 172-187, x8 113-123. rare: 268-283 / 273-281 / 98-101 / 64-66.
search: 1,301-1,615 / 1,233-1,592 / 289-297 / 183-211. facets: 847-1,156 / 815-968 / 191-196 / 127-145.
pattern: new x1 853-1,284, x4 240-249, x8 146-161. from depth 4: 819-1,536 / 357-606 / 210-228.
link ends pages: 694-1,239 / 222-256 / 148-157. Worst p95: x4 139-532 (one 1,295), x8 86-489.

## Pre-final runs on 4536ca0's code

- bench-2 (S2\logsench-2.log): Completed, every planted issue; 38.7 s, 2,613 pages/s, peak 192 MB, +1 MB, DB 480 MB,
  analysis 11.3 s; favicon kept (PNG 32x32, 160 bytes), no URL for it.
- readbench --runs 20 --gate on bench-2: RUNNING (S2\logseadbench-2.log).

## Still to do (in order)

1. readbench result; then `bench --pages 40000 --read-links --max-log-mb 128`.
2. Audit: `python -I S2udit.py <sidecar exe> S2udit-data S2udit-state` (garden copy analysed is there; link or
   copy bench-2 in). Fix what it finds.
3. Add the final numbers paragraph to "Engine changes E6 made"; commit.
4. Full checks again on the final commit; final report.

---

# HANDOFF (written 2026-11-11 when the owner paused all work; resume from this section and `brief-e6.md`)

Paths: S = `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad`.
Brief `S\brief-e6.md`. Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\e6`, branch `ui/e6`, cut from 483c077.
**HEAD 87e3ff5, tree clean, never pushed, nothing merged.** Everything below the HANDOFF section is the working log
(older, more detail; where it disagrees with this section, this section is right).

## What was run on the final tree (and what was not)

- `dotnet build engine/ShoutingIguana.Engine.slnx -c Release --no-incremental`: 0 warnings, 0 errors.
- `dotnet test engine/ShoutingIguana.Engine.slnx` (whole suite, once): 1,042 engine + 177 sidecar + 17 CLI, all passed (run on the tree as committed at 87e3ff5).
- NOT run: `si bench --pages 100000`, `si bench --read-links --max-log-mb`, `si readbench --gate` (whole), the audit of
  every endpoint's numbers against SQL of my own on the bench and garden crawls, anything in `app/` (no TS written).
  No CI run. Nothing at 1M URLs.

## The 12 items

| # | Item | State | Commits |
| --- | --- | --- | --- |
| 1 | Fast search, `/search` | DONE but for types/README/audit | eb8cb0e, fe77b13, fac4f9b, a37fd16 |
| 2 | Faster link reads (pass split over connections) | DONE but for a quiet measurement, a sidecar slot test | cbaa5c4, 9b16319, 2ccd678, 87e3ff5 |
| 3 | Favicon | NOT STARTED | |
| 4 | robots origins search and filter | DONE but for types/audit | 1af37ca, 86b75bc, 3983689 |
| 5 | Inspector links exactly (sitemaps, robots line) | DONE but for types/audit | same three |
| 6 | Links to URLs; distinct ends list | PART-DONE: written, NOT in the tree, never compiled | (none) |
| 7 | A check's count has a true name | DONE but for types | a1a6da4 |
| 8 | inlinks vs stored links | DONE: cause found, reader fixed | 88846ba |
| 9 | Analysis limits API | DONE but for types | 73a29a5, 59ddec2 |
| 10 | Docs, TS types, README, readbench cases | PART-DONE | (with each item) |
| 11 | A sitemap index's own count of sitemaps | DONE but for types/audit | same as 4 and 5 |
| 12 | `respected` on `/robots/file` | DONE but for types | same as 4 and 5 |

### 1. Search: done

- Decision (measured, `S\e6\logs\search2.log`): **no index in the crawl file.** FTS5 trigram with content costs +44 to
  48 MB (9 to 10% of the 479 MB file) and 2.0 s of analysis (11.8 s: +17%) or 2.7 s spread over the crawl; contentless
  +9.4 MB (2%), 1.4 s; it answers rare text of 3+ characters in 0.1 to 2 ms but a common word in 28 ms (81 ms with the
  text ranking needs) and can't answer 1 or 2 characters at all. Costs chosen instead: file 0, crawl 0, analysis 0;
  memory about 95 bytes a URL in the sidecar (10 MB at 100k), two crawls' kept (`ReadCache.TEXT_CAPACITY`).
- The grid's `search` was ALREADY inside its budget (the brief assumed otherwise): 26 to 50 ms p95 first page at 100k
  for one letter, two letters, no match, forty characters. Left as the scan it is; four readbench cases hold it (class `text`).
- Contract: `POST /api/crawls/{id}/search` body `{ text, limit }` (limit 1..50, default 10) ->
  `{ results: [{ id, url, title, isInternal, statusCode, outcome, rank, matchedIn }], matching, total }`.
  `rank`: `exact | start | anywhere`; `matchedIn`: `url | title`. `matching` == the URL grid's `matching` under the same
  `search`. Empty `text` = warm-up: `results: []`, `matching == total`. Errors: `invalid-query` on `text` (missing, over
  1,000 characters, a zero character) and `limit`; wrong JSON type = `bad-request`.
- Engine: `UrlSearchReader.Search` / `SearchKept`, `Storage/Models/SearchText.cs`. Kept search takes no read slot.
- Measured (100k, 20 runs): kept keystroke 1.5 to 2.8 ms p95 whatever is typed (budget 50); FIRST search of a crawl 48 to
  55 ms p50, 61 to 72 ms p95: **over the 50 ms budget**, which is why the empty text exists (palette asks it on open).
  Gates: `search` 25 ms, `search-first` 200 ms.
- Running crawl: answered for one moment at most 2 s old (text re-read by the next search after that); statuses live.
- Not verified: audit vs own SQL on bench + garden (only the test site's reference); 1M; idle release of the kept text
  is NOT built (a 1M crawl's ~100 MB stays until two other crawls are searched): say so as a limitation.

### 2. Faster link reads: done (adopted)

- `LinkGridReader.Scan` splits a pass over every link by link id over 1 + up to 3 connections (`PassHelpers`), when the
  crawl is not running (Live), the pass is in id order, the crawl has 500,000+ links (`SplitFrom`) and the host lends
  helpers. New public `ReadHelpers` (engine) and `ReadCache.Helpers`; sidecar `Crawls/ReadSlots.cs` lends free read
  slots, never the last free one. A running crawl's pass is never split, so its log's fold is untouched.
- Measured ONCE, 20 runs, on a machine other agents were loading (`S\e6\logs\split-pass-1.log`), p50 first page,
  1 connection -> 4: text common 870 -> 294 ms; rare text 478 -> 175; pattern 1,253 -> 396; from depth 3 934 -> 476;
  from depth 4 1,478 -> 333; search 897 -> 294; search common 1,156 -> 416; facets of a text filter 907 -> 181.
  Pure SQL scaling (`logs\parallel-pass.log`): 207 / 104 / 53 / 38 / 25 ms at 1 / 2 / 4 / 8 / 16 connections.
  That morning, less loaded, base single was 524 (text common), 288 (rare), 928 (search).
- Gate `link-pass-split` 1,500 ms is PROVISIONAL. NEXT STEP: on a quiet machine run
  `si readbench <100k crawl> --runs 20 --only "every link read"` with base (`S\e6\base-si\si.exe`) and new interleaved,
  confirm one-connection passes didn't slow, try `PassHelpers` 7 against 3, then set the gate and the doc's numbers.
- Tests: `LinkPassSplitTests` (6). Mutation seen failing: `split-merge`.
- Not verified / not done: no sidecar test that helpers never take the last slot (`ReadSlots.Helpers`); through the
  real sidecar under several clients; whether one-connection passes are unchanged vs base (interleaved run not done).
- Trap: a to.* filter on a small crawl is index-driven, not a pass, so it never splits (tests allow for it).

### 3. Favicon: NOT STARTED

Nothing designed in code. Notes for whoever starts: the fetch must not be one of the site's URLs (see how site-version
probes are kept out: `urls.source = 3`, `DiscoverySource`), kept by `CrawlWriter` in a table created on open (as
`NOTICES` / `RUN_TIME` are; `AddMissingColumns` now exists in `CrawlWriter` for columns), sniff bytes, no SVG served as
image, cap the size, `X-Content-Type-Options: nosniff` is already on every answer.

### 4, 5, 11, 12: done (one set of commits)

- 4: `POST /robots` request gains `search` (text in the origin's name, case ignored, <= 1,000 characters) and `blocked`
  (true | false); response gains `matching` and `counts { all, blocked, notBlocked }` (of the origins the search finds).
  `total` and top-level `blocked` stay the crawl's. Engine `RobotsOriginQuery`, `RobotsReader.ReadOrigins(path, query, ...)`.
- 12: `/robots/file` response gains `respected`.
- 5: `/urls/inspect` gains `listedIn { total, sitemaps: [{ id, url }] }` (first 20, sitemap id order) and its `robotsTxt`
  test gains `origin`, `reason` (`rule | unreachable | unknown`), `line`, `rule` (left out when they don't apply;
  `origin` whenever the crawl has a robots row for the URL's origin, always for a refused URL).
  `/robots/blocked` request gains `urlId` (that URL's row, or none).
- 11: `/sitemaps` each sitemap gains `listedSitemaps` (int | null). Crawl file: new nullable column
  `sitemaps.listed_sitemaps`, added by `CrawlWriter.Open`; schema VERSION unchanged; null in older files and for a file
  whose body was never read. Over the cap: the index file is read whole, so it's every entry; `sitemaps` is what the crawl has.
- Tests: `RobotsAndSitemapsReaderTests` (+5), `SitemapDiscoveryTests`, sidecar `LinksSitemapsRobotsEndpointsTests`,
  `GridEndpointsTests`, the growing-crawl hammer. Mutations seen failing: robots-filter, robots-respected,
  inspector-line, inspector-listedin, sitemaps-listed.
- Not measured (no readbench case added for them; all are small reads). Not audited against bench/garden SQL.

### 6. Links to URLs and distinct ends: PART-DONE, NOT IN THE TREE

- Written but never applied or compiled: `S\e6\specs\link-ends-engine.py`, `S\e6\specs\link-ends-sidecar.py`, and the
  tests `S\e6\LinkEndsTests.cs.pending`.
- EXACT NEXT STEP: from `engine/`: `python -I S\e6\edit.py S\e6\specs\link-ends-engine.py`, then
  `... link-ends-sidecar.py`; copy `LinkEndsTests.cs.pending` to
  `tests/ShoutingIguana.Engine.Tests/Storage/LinkEndsTests.cs`; build; fix what doesn't compile (it was written
  against the code as of 88846ba; item 2 then changed `LinkGridReader.Scan`'s signature region, so the last EDIT of
  the engine spec, which adds `bySource` to `Scan`, WILL NOT APPLY as written: add `bool bySource = false` to `Scan`
  by hand and use `bySource ? "l.from_id" : "l.to_id"` as the tally's second argument). Then sidecar tests, hammer,
  readbench cases, docs.
- Intended contract (NOT settled until built): URL grids (`/urls`, `/urls/facets`, a check's `/rows`, `/sitemaps/urls`)
  take `linkEnds: { end: "from" | "to", filters: [link filters], search }`; `POST /api/crawls/{id}/links/ends` body
  `{ end, filters, search, offset, limit }` -> `{ end, total, links, offset, rows: [{ id, url, links }], summary }`,
  ends in URL id order; `total` == the URL grid's `matching` under the same `linkEnds`. Targets come from the tally the
  grid already has; sources need a pass (splits as item 2's does).

### 7. Done

`/issues/{checkId}/groups`: each group gains `urls` (== `pages`, which stays). Engine `IssueGroup.Urls`.

### 8. Done: cause found, reader fixed, analysis untouched

- CAUSE: the analysis's `inlinks` count hyperlinks on the pages it audits, and `AnalysisReader.SITE_PAGE` leaves out
  parsed pages whose URL contains `/cdn-cgi/` (Cloudflare's own); the crawl stores those pages' links. On the 100k crawl
  exactly 24 of 112,590 URLs differ (the menu's targets, +1 each, the home page +2: the 25 hyperlinks of
  `/cdn-cgi/l/email-protection`). "A tenth of URLs" was the review's sample (24 of 200 sampled). Garden crawl: 0 differ.
  Same cause as `pageCount` and `pagesAnalyzed` differing by one. The stored links are right.
- Reader fix: `/urls/inspect` `linkTotals.hyperlinkInlinks` == the links grid's `matching` for `to.id` eq, `kind` in
  [hyperlink], `selfLink` false == `/urls/inlinks` with `kinds: ["hyperlink"]`. Use it for "N hyperlinks".
- For the owner / the `score` agent (NOT done, a decision): the analysis could count unaudited pages' links in `inlinks`
  (`UrlTable.CountInlinks` for every parsed internal page); that changes `inlinks`, `linkingPages`, the followed and
  nofollow counts, and possibly orphan results for pages linked only from `/cdn-cgi/` pages.

### 9. Done

`GET` / `PUT /api/analysis-settings`, `POST /api/analysis-settings/validate`, `POST /api/analysis-settings/reset`
(`{ limits: [ids] }`, none = all). Answer `{ settings: { id: value }, limits: [{ id, value, default, unit, min, max,
description }], isDefault }`. PUT replaces: a limit left out or null takes the engine's. Problem `invalid-settings`
(422) keyed by limit; an unknown limit = `bad-request`. Kept in `<state>/analysis-settings.json` (only what differs).
An analysis under way keeps the limits it began with (tested with the hook `CrawlManager.AnalysisStarting`).
**Trap for the merge:** `AnalysisSettingsTests.EveryLimitIsDescribedWithItsDefaultUnitAndRange` holds that every
public property of `AnalysisSettings` has a limit: if the `score` branch adds a setting, that test fails until its
descriptor is added to `Sidecar/Options/AnalysisLimits.cs` (and the table in the API doc).

### 10. Docs, types, README, readbench: part-done

- Done: a contract section per built item in `docs/v2-sidecar-api.md` (`/search`, analysis settings, robots, sitemaps,
  inspector, groups, the split pass); readbench cases and gates for search (`search`, `search-first`), the grid's
  search (class `text`) and the split passes (`link-pass-split`).
- NOT done: the "Engine changes E6 made" section; the "Built in stages" E6 line; `app/src/api/types.ts` and typed client
  functions (NOTHING in `app/` was touched); `engine/README.md` (search text, `ReadHelpers`, `analysis-settings.json`
  in the state folder, `sitemaps.listed_sitemaps`, `link-pass-split`); readbench cases for the robots/inspector reads.

## What the next person must know

- Mutations are in `S\e6\mutations\*.py`, run with `S\e6\mutate.py` from `engine/`; every built item has one seen failing.
- Edits to repo files: Write a spec to `S\e6\specs\`, run `python -I S\e6\edit.py <spec>` from `engine/` (keeps CRLF).
  Bash heredocs holding C# break in this shell. `Assert.Equal` on tuples holding collections doesn't compare them.
- Crawls in `S\e6\crawls`: `bench-base.sicrawl` (100k, made by the base engine, pristine), `garden-analysed.sicrawl`,
  `bench-proto.sicrawl` (changed by prototypes: not for audits). Base CLI: `S\e6\base-si\si.exe`.
- Base numbers that morning: bench crawl 44.0 s, 2,300 pages/s, peak 193 MB, file 479 MB, analysis 11.8 s.
- Still owed before this branch is finished: items 3 and 6; item 10's remainder; the audit; the full verify of the
  brief (bench, read-links bench, readbench --gate, app checks); then the merge of `ui/step-3` when asked.

---

# WORKING LOG (older; the HANDOFF above supersedes it)

# E6 checkpoint

Brief: `S\brief-e6.md` (S = ...\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad). Worktree
`C:\Projects\ShoutingIguana\.claude\worktrees\e6`, branch `ui/e6`, base HEAD 483c077. Never push/merge. Additive only.
Stay out of IssueCatalog, rules, IssueWriter, export/report. Scratch: `S\e6\`.

## Tools / files (in S\e6)

- `base-si\si.exe`: the CLI built at 483c077 (base), for interleaved timing.
- `crawls\bench-base.sicrawl`: 100k bench made by base (112,590 URLs, 5,054,060 links, 479 MB). Keep pristine.
- `crawls\bench-proto.sicrawl`: copy, CHANGED by proto scripts (fts tables etc.). Not for audits.
- `crawls\garden.sicrawl`: copy of the dev garden crawl (not yet analysed by this engine).
- `proto\search1.py`, `search2.py`: SQL-level measurements. `logs\`: outputs.
- NOTE: bash heredocs here collapse backslashes; write scripts with the Write tool. `sed -i` strips CRLF: never on repo files.
- New si.exe: `engine/src/ShoutingIguana.Cli/bin/Release/net10.0/si.exe`.

## Base numbers (measured 2026-11-11 ~09:50, machine shared)

- bench 100k c16 (base): crawl 44.0 s, 2,300 pages/s, peak 193 MB, growth -12 MB, DB 479 MB, analysis 11.8 s (peak 284 MB), 32,410 issues.
- readbench base, 20 runs: URL grid search rare 39.9/51.1 ms (p50/p95 first), common 40.9/43.1 (worst p95 60.7);
  without summary 38.6/64.4; links search 928/976 (worst 985); links text common 524/543 (worst 584); rare 288/310;
  "from pages at depth 2" 43 ms (few sources: NOT the 1.46 s case; need a depth matching many sources).

## Item 1 (search): measurements so far

- SQL scans on 100k (python, warm conn): urls LIKE 4-6 ms (covering autoindex), titles LIKE 7-12 ms (responses scan).
  => the grid's search ALREADY meets 300 ms at 100k (40-60 ms p95 incl. page). Scan stays for the grid.
- FTS5 trigram (logs\search2.log): with content +44-48 MB (9.2-10%), build 2.0 s (analysis 11.8 s: +17%), or 2.66 s
  filled in batches of 500 during a crawl; contentless detail=none +9.4 MB (2%), 1.4 s. MATCH rare 0.1-2 ms; common
  'product' 28 ms count, 81 ms with each match's text (ranking needs it); 1-2 chars not possible (falls back to a scan
  12-19 ms). Narrow text table: +9.0 MB (1.9%), 0.16 s, scans 5-13 ms.
- DECISION (pending C# numbers): no index in the file. `/search` reads URL + title text once into memory
  (kept in ReadCache per crawl stamp, 2 s live), then each keystroke is an in-memory scan with ranking.

## Status

- [x] 1 search: DONE, committed eb8cb0e (engine), fe77b13 (sidecar), fac4f9b (cli), a37fd16 (docs). See "Item 1" below.
      STILL OWED for item 1: TS types + client fn (item 10), README, "Engine changes E6 made" section, audit vs own SQL
      on bench + garden, final gate numbers; idle release of the kept text NOT built (limitation to report).
- [ ] 2 link reads on several connections (measure)
- [ ] 3 favicon
- [x] 4 robots origins search/filter: DONE (1af37ca engine, 86b75bc sidecar, 3983689 docs)
- [x] 5 inspector sitemaps + robots line: DONE (same three commits)
- [ ] 6 links -> URLs filter; distinct ends list
- [x] 7 groups count name: DONE (a1a6da4): `urls` beside `pages` on each group
- [ ] 8 inlinks vs stored links disagreement
- [ ] 9 analysis settings API
- [ ] 10 docs, TS types, README, readbench cases
- [x] 11 DONE (same three commits as 4/5) (ADDED by orchestrator mid-task, do with 4/5): `GET /sitemaps`: a sitemap index says how many sitemaps its FILE
      lists (as read; null when the file wasn't read), additive; say what it is for an index over the crawler's cap.
- [x] 12 DONE (same three commits) (ADDED, do with 4/5): `/robots/file` gains `respected` (as the origins list has), so the file's page needn't
      read the origins' first row. Both documented, typed, each with a test.
- [ ] Verify

## Item 1 progress (2026-11-11)

- BUILT (uncommitted): Storage/Models/{UrlSearchRank,UrlSearchField,UrlSearchResult,UrlSearchResults,SearchHit,SearchHits,SearchText}.cs,
  Storage/UrlSearchReader.cs; ReaderFunctions si_search_url/si_search_title; GridReadContext.SearchText + CachedText;
  ReadCache text store (TEXT_CAPACITY 2, TextCount); ReaderBenchmark: 4 more grid search cases (class text) and
  Search() (classes `search` gate 50, `search-first` gate 200, provisional).
- Design: no index in the file. URLs+titles read once into memory (ASCII-lowercased bytes, zero byte between, a bit a
  byte for capitals), kept per crawl stamp (live: 2 s). Match set == grid's `search` (LIKE semantics). Rank: exact
  (text is URL / URL sans scheme / path, slashes around ignored) > start (path segment, host start incl. after www.,
  title word) > anywhere; then internal first, URL match before title, shorter first, lower id. Empty text = warm-up:
  results [], matching = total.
- MEASURED on bench-base (20 runs): kept keystroke 1.5-2.8 ms p95 for every text ('p' 112,590 matches = 2.8);
  first keystroke (text read) 47.5-54.6 ms p50, 60.8-72 ms p95 (urls pass ~20 ms, titles ~24 ms).
  Grid search new cases: one letter 39/50 ms worst p95, two letters 46, no match 27, forty chars 30.
- TODO: engine tests (reference ranking, == grid matching in both modes, cache kept/re-read, limits, live crawl),
  sidecar POST /api/crawls/{id}/search {text, limit}, docs, TS types, mutation check, commit.

## Item 1 DONE (committed)

- Contract: `POST /api/crawls/{id}/search` body `{ text, limit }` (limit 1..50, default 10) ->
  `{ results: [{ id, url, title, isInternal, statusCode, outcome, rank, matchedIn }], matching, total }`;
  rank `exact|start|anywhere`, matchedIn `url|title`. Empty text = warm-up: results [], matching == total.
  Errors: invalid-query on `text` (missing, >1000 chars, zero char) / `limit`; bad-request for wrong JSON types.
- Engine: `UrlSearchReader.Search` / `SearchKept` (no file pass when text kept -> sidecar takes no read slot).
- Tests: UrlSearchReaderTests (26: reference ranking over ~hundreds of texts, == grid matching with/without summary,
  kept/re-read, 2 texts kept, live moment, limits), SearchEndpointsTests (5), searches added to the growing-crawl
  hammer (RunningCrawlReadsTests, answers held to the finished file), CliTests readbench kinds.
- Mutations SEEN FAILING (S\e6\mutations\search-*.py via S\e6\mutate.py): segment-start rank dropped (4 tests fail),
  text never kept (4), title double-count (1).
- Gates: `search` 25 ms (measured 1.5-2.8), `search-first` 200 (measured 61-72 p95, 48-55 p50: OVER the palette's 50).
- Verified: targeted tests + Release build (0 warnings) only. Full suite NOT yet re-run. No audit yet.
- mutate.py usage (from engine/): python -I S\e6\mutate.py <file> <spec.py> <test proj dir> "<filter>"
- Repo edits: use the Edit tool or the python `edit()` helper pattern (reads/writes newline='' and maps 
 to CRLF).

## Items 4, 5, 7, 11, 12 DONE (committed; HEAD 3983689)

Contract names (all additive):
- 7: `/issues/{checkId}/groups` each group gains `urls` (== `pages`). Engine: IssueGroup.Urls.
- 4: `POST /robots` request gains `search` (text in the origin's name, case ignored, <= 1000 chars) and `blocked`
  (true/false); response gains `matching` and `counts { all, blocked, notBlocked }` (of the origins the search finds).
  `total` and top-level `blocked` stay the crawl's. Engine: RobotsOriginQuery, RobotsReader.ReadOrigins(path, query, ..).
- 12: `/robots/file` response gains `respected`.
- 5: `/urls/inspect` gains `listedIn { total, sitemaps: [{ id, url }] }` (first 20, in sitemap id order) and the
  `robotsTxt` test gains `origin`, `reason` (rule|unreachable|unknown), `line`, `rule` (left out when not applicable;
  `origin` whenever the crawl has a robots row for the URL's origin, always for a refused URL).
  `/robots/blocked` request gains `urlId` (one URL's row, or none).
- 11: `/sitemaps` each sitemap gains `listedSitemaps` (int or null). Crawl file: `sitemaps.listed_sitemaps` column,
  added by CrawlWriter.Open (AddMissingColumns), written by the UPDATE in WriterStatements; schema VERSION unchanged.
  Over the cap: the index file is read whole, so listedSitemaps is every entry; `sitemaps` is what the crawl has.
Tests: RobotsAndSitemapsReaderTests (+5 tests), SitemapDiscoveryTests (cap test + parse-error test assert the count),
  sidecar LinksSitemapsRobotsEndpointsTests + GridEndpointsTests, growing-crawl hammer (origin query, inspector of a
  blocked URL, listedIn, urlId, listedSitemaps >= sitemaps). Sidecar suite 171 green at 86b75bc.
Mutations SEEN FAILING (S\e6\mutations): groups-urls, robots-filter, robots-respected, inspector-line,
  inspector-listedin, sitemaps-listed.
NOT yet done for these: TS types/client (item 10), README mention, audit vs own SQL on bench + garden, full suite run.

## Tooling notes (IMPORTANT)

- Heredocs with C#/quotes in them break in this shell. Use: Write tool -> spec file in S\e6\specs\*.py, then
  `python -I S\e6\edit.py <spec>` from engine/ (EDITS = [(path, old, new)], WRITES = [(path, text)]; keeps CRLF).
- Assert.Equal on tuples that hold collections doesn't compare the collections: split them.

## Next (in order)

9 analysis settings API (sidecar only) -> 8 inlinks disagreement (investigate on bench-base) -> 6 links->URLs filter and
distinct ends -> 2 link pass on several connections (measure) -> 3 favicon -> 10 docs/types/README/readbench -> verify.

## Items 9 and 8 DONE (committed)

- 9 (73a29a5 sidecar, 59ddec2 docs): `GET/PUT /api/analysis-settings`, `POST /api/analysis-settings/validate`,
  `POST /api/analysis-settings/reset` (body `{ limits: [ids] }`, none = all). Response
  `{ settings: {id: value}, limits: [{ id, value, default, unit, min, max, description }], isDefault }`.
  PUT replaces: a limit left out or null takes the engine's value. Problem `invalid-settings` (422) keyed by limit;
  unknown limit / non-object body = bad-request. Kept in `<state>/analysis-settings.json` (only what differs).
  CrawlManager.AnalysisSettings now reads AnalysisSettingsStore.Current; analysis captures it at start
  (test hook CrawlManager.AnalysisStarting). Tests: AnalysisSettingsTests (6). Mutations seen failing: settings-underfoot,
  settings-range, settings-inforce. A reflection test holds "every AnalysisSettings property has a limit": WILL FAIL
  after the merge if the score agent adds a setting -> add its descriptor to Options/AnalysisLimits.cs then.
- 8: CAUSE FOUND. url_summary.inlinks = hyperlinks on the pages the analysis audits; AnalysisReader.SITE_PAGE leaves out
  parsed pages whose URL contains '/cdn-cgi/' (Cloudflare's). The crawl stores those pages' links. Bench (bench-base):
  exactly 24 of 112,590 URLs differ (menu targets, +1 each, home +2 = the 25 hyperlinks of /cdn-cgi/l/email-protection).
  "A tenth of URLs" was the review's sample (24 of 200 sampled). Garden: 0 differ. Same cause as pageCount vs
  pagesAnalyzed differing by 1. Stored links are right. READER FIX DONE: `linkTotals.hyperlinkInlinks` on /urls/inspect
  (== links grid matching for to.id eq X, kind in [hyperlink], selfLink false; == /urls/inlinks kinds [hyperlink] total),
  counted from link_summary when current. Analysis NOT changed. For the report: the analysis-side alternative would be
  UrlTable.CountInlinks over every parsed internal page (audited or not): an owner decision, changes inlinks,
  linkingPages, followed/nofollow counts and possibly orphan results for pages linked only from /cdn-cgi/ pages.
  Logs: S\e6\logs\inlinks.log. Mutation seen failing: inlinks-hyperlinks.

## IN FLIGHT (uncommitted) at ~HEAD 88846ba: item 2 (split pass) and item 6 (link ends)

Item 2, BUILT but NOT tested/committed:
- Engine: Storage/ReadHelpers.cs (public; host lends connections; `ReadHelpers.UpTo(n)`), `ReadCache.Helpers` (init),
  GridReadContext.BorrowHelpers / OpenAlongside (+ nested class Alongside), LinkIdSet.Merge, LinkTally.Merge,
  LinkGridReader.Scan split by rowid over 1 + helpers connections when: pass in id order (NOT INDEXED), crawl not Live,
  largest link id >= SplitFrom (500,000), helpers free. Knobs: LinkGridReader.PassHelpers (3), SplitFrom, Split hook.
- Sidecar: Crawls/ReadSlots.cs (the slot semaphore + Helpers that never take the last free slot), GridData takes ReadSlots,
  ReadCache registered with Helpers.
- Cli: ReaderBenchmark link-pass cases doubled: "(every link read)" class link-pass and
  "(every link read, on several connections)" class `link-pass-split` (gate provisional 2,500), new cases: text matches
  a pattern, from pages three/four clicks, search for a common word.
- Tests WRITTEN, NOT RUN: tests/ShoutingIguana.Engine.Tests/Storage/LinkPassSplitTests.cs.
- Measured so far (S\e6\logs\split-pass-1.log, 20 runs, machine heavily loaded: single numbers ~1.7x this morning's):
  text common 870 -> 294 ms; rare 478 -> 175; pattern 1253 -> 396; from depth 3: 934 -> 476; from depth 4: 1478 -> 333;
  search 897 -> 294 (p50 first page, 1 connection -> 4). Pure-SQL scaling (logs\parallel-pass.log): 207 -> 104 -> 53 ->
  38 -> 25 ms at 1/2/4/8/16 connections.
- TODO: finish measuring (interleave base-si single vs new single vs split 3 vs 7), decide gate for link-pass-split,
  run LinkPassSplitTests, sidecar test that helpers never take the last slot, docs, commit.

Item 6, SPECS WRITTEN, NOT APPLIED: S\e6\specs\link-ends-engine.py and link-ends-sidecar.py (apply with edit.py from engine/).
- Engine: LinkEnd enum, UrlLinkEnds, LinkEndQuery/Row/Page; LinkGridReader.Ends / EndIds / ValidateEnds / EndTally
  (to: Tally; from: Scan bySource); UrlQuery.LinkEnds + UrlFacetQuery.LinkEnds; UrlQueryCompiler.LinkEnds placeholder
  predicate (GridPredicate.LinkEnds) resolved by UrlGridReader.WithLinkEnds via GridPlanner.InSet.
- Sidecar: GridQueryRequest.linkEnds / FacetsRequest.linkEnds ({ end: from|to, filters, search }),
  POST /links/ends { end, filters, search, offset, limit } -> { end, total, links, offset, rows: [{ id, url, links }], summary }.
- TODO: apply, build, tests (ends vs SQL; URL grid matching == ends total, with/without summary; live; errors), hammer,
  readbench cases, docs, commit.
