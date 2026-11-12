# HANDOFF (written 2026-11-11 when the owner paused all work; resume from this section and `brief-e6.md`)

Paths: S = `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad`.
Brief `S\brief-e6.md`. Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\e6`, branch `ui/e6`, cut from 483c077.
**HEAD @HEAD@, tree clean, never pushed, nothing merged.** Everything below the HANDOFF section is the working log
(older, more detail; where it disagrees with this section, this section is right).

## What was run on the final tree (and what was not)

- `dotnet build engine/ShoutingIguana.Engine.slnx -c Release --no-incremental`: 0 warnings, 0 errors.
- `dotnet test engine/ShoutingIguana.Engine.slnx` (whole suite, once): @TESTS@
- NOT run: `si bench --pages 100000`, `si bench --read-links --max-log-mb`, `si readbench --gate` (whole), the audit of
  every endpoint's numbers against SQL of my own on the bench and garden crawls, anything in `app/` (no TS written).
  No CI run. Nothing at 1M URLs.

## The 12 items

| # | Item | State | Commits |
| --- | --- | --- | --- |
| 1 | Fast search, `/search` | DONE but for types/README/audit | eb8cb0e, fe77b13, fac4f9b, a37fd16 |
| 2 | Faster link reads (pass split over connections) | DONE but for a quiet measurement, a sidecar slot test | @ITEM2@ |
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
