# Brief: fresh-eyes review of engine E6 (search, the favicon, the limits' API, link reads), read-only

Review engine E6 of Shouting Iguana v2 (a desktop SEO crawler: a .NET 10 engine in `engine/` with a sidecar API, and
a React app in `app/`). It is branch `ui/e6` at the head named in the message that launched you. Review
`git diff $(git merge-base ui/step-3 ui/e6)..ui/e6` and the code it touches. You did not write it and nobody has
reviewed it.

- S3 = `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\8e7045a4-9ede-460a-be29-971bc406a6c0\scratchpad`
  (yours: `S3\e6-review\`, with a `tmp` folder for `TEMP` and `TMP`, and a `checkpoint.md` you keep as you go).
- The builders' scratch folder, read-only (their measurements, crawl copies, scripts):
  `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad\e6\`.

Work in a worktree of your own: from `C:\Projects\ShoutingIguana`,
`git worktree add --detach C:\Projects\ShoutingIguana\.claude\worktrees\e6-review ui/e6`. **Edit, commit or push
nothing in any other worktree.** In your own you may build, test and run anything. Copy crawls before you open
them; never edit `%LOCALAPPDATA%\ShoutingIguana\dev-crawls` (a real site of 4,521 URLs, "garden", and a 100k
synthetic crawl with 5 million links). The engine's synthetic test site (`si testsite`) is the only site you may
crawl: never a real one. Other agents are at work on this machine: one heavy thing at a time; a timing that fails
is run again alone before you believe it. **Stop only processes you started, by their own process id**, never by a
command-line match. Never run `dotnet build-server shutdown`.

## Read first

- `CLAUDE.md` (repo root): the engine rules (one SQLite file per crawl; only `Storage/` writes it, `CrawlWriter`
  while a crawl runs and `IssueWriter` for analysis, never both; HTML parsed once) and the conventions.
- `docs/handoff/2026-11-11/brief-e6.md` (the twelve items asked for), and the builders' `checkpoint.md` in their
  scratch folder (what they say they did, measured and left out).
- `docs/v2-sidecar-api.md`: "Engine changes E6 made" and each endpoint it names; Conventions; "Live events".

## What to look for

Real defects, each with a concrete failure scenario, in order of weight:

1. **Every number against SQL of your own**, on the synthetic, garden and 100k crawls, for each new or changed
   read: `/search` (`matching` equal to the URL grid's under the same `search`; ranks; ties; the limit),
   robots origins' search and filter, the inspector's exact links to a sitemap and a robots.txt line, `urls` beside
   `pages` on a check's groups, `linkTotals.hyperlinkInlinks`, a sitemap index's `listedSitemaps`, `respected` on
   `/robots/file`, "links to URLs" and a view's distinct ends (counts, duplicates, order, paging, cursors).
2. **Search**: text with `%`, `_`, quotes, backslashes, non-ASCII, case and accents (what "matches" means, and
   that it is the grid's meaning), a zero character, 1,000 characters, an IDN host, percent-encoded paths; the
   kept text in the sidecar's memory (how much, when it is released, two crawls, a crawl deleted or replaced on
   disk while its text is kept, a crawl that is running and growing: answers no more than the stated age old and
   never a row that doesn't exist); races between a first search and a second; the warm-up.
3. **The split link pass**: the same rows in the same order as one connection for every sort, filter, cursor and
   page size, including the boundaries between the slices and a page that ends on one; never split for a running
   crawl; helpers given back on an exception and on cancellation; never the last read slot; what happens when a
   helper connection fails to open; the log's fold untouched.
4. **The favicon**: fetched politely (robots.txt, the crawl's delay and user agent, redirects, a size cap, a
   content-type and a real decode before it is served), never holding up or failing a crawl, stored through
   `Storage/` only, served safely (an SVG or a hostile file from a crawled site must not run in the app's window:
   content type, sniffing, `Content-Security-Policy`), a site with none, `<link rel="icon">` forms, a crawl made
   before the feature, a stopped crawl.
5. **The analysis settings' API**: validation at the boundary (types, ranges, unknown fields, NaN, huge numbers),
   what a change does to a stored analysis (stale? which crawls?), reset, concurrency with a running analysis,
   every setting described in `AnalysisLimits`, and the limits the plain words print agreeing with these.
6. **A running crawl** against every new read: no 500, answers for one moment, no write path opened by a read.
7. **Performance**: each budget and gate the docs claim, measured by you on the 100k crawl on a machine as quiet
   as you can make it; gates set so loosely they'd pass a regression; memory.
8. **Code and tests**: the engine rules kept; `.ConfigureAwait(false)` on every await in the engine; models one
   per file in `Models/`; SQL built from user text (injection through a filter, a sort or a column name);
   tests that actually test; anything weakened elsewhere to get green; the TypeScript types and client functions
   in `app/src/api/` matching what the sidecar really sends.
9. **The docs** say what the code does.

Verify each finding before reporting it: read the code path and, where you can, prove it (a failing test or a
script). Mark each CONFIRMED (shown) or PLAUSIBLE (reasoned). No style preferences, and no speculation reported as
a defect.

## Report back

A ranked list, most severe first, at most 30: severity (high / medium / low), file:line, one-sentence defect, the
failure scenario, CONFIRMED or PLAUSIBLE, and a one-line fix. Then a short "looks right" note on what you checked
and found sound, and your own measurements. Write your proof tests and scripts to `S3\e6-review\` and name them.
**Give the full list in your final message** (the orchestrator saves it for the fixer; you can't write a file
named `findings.md`). No file dumps. When done, stop your processes and remove your worktree
(`git worktree remove --force` on your own folder only).
