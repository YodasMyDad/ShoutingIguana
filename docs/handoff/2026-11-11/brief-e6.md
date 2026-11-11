# Brief: fast search, the favicon, and what round 2's screens found the engine lacks (engine E6, round 3)

Shouting Iguana v2 is a desktop SEO crawler: a C#/.NET 10 engine (one SQLite file per crawl), a sidecar HTTP API,
a CLI (`si`), and a React app that reads the API. The Links, Sitemaps and robots.txt screens have just been built
and reviewed, and they and their reviews left a list of things the engine can't answer yet. E6, the last engine
stage the UI's plan names, is fast search and the site's favicon. You build both lists. UI agents take them up
afterwards, so the contract you write is what they build on.

## Where you work

- Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\e6`, branch `ui/e6` (cut from `ui/step-3` with engine E3c
  and its review's fixes merged). Work ONLY there. Small commits (`Engine: ...`, `Sidecar: ...`, `Cli: ...`,
  `Docs: ...`, `Tests: ...`, saying what now holds), each ending with
  `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`. Never push, merge or touch another worktree (one
  exception at the end, when told). No bare `git stash`. **Never rename an existing public function, an API field
  or an issue id**: every contract change is additive.
- Your scratch folder:
  `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad\e6\`.
  Keep a `checkpoint.md` there, updated after each item (done, decided and why, measured, verified vs not, next).
- Other agents are building, testing and benchmarking on this machine, which is close to its memory limit. One
  heavy thing at a time; never two benches at once; repeat a timing before you trust it, and time base and new
  interleaved. Stop only processes you started, by their own process id. Never run `dotnet build-server shutdown`.
  Never crawl a real site (`si testsite` serves the synthetic one). Never edit
  `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`: copy a crawl before opening it. The dev crawls are a real site
  ("garden", 4,521 URLs) and a 100k synthetic crawl with 5 million links; analyse your copies first.
- Two other engine agents work in sibling worktrees on other branches: `score` (the catalog, the analysis's score)
  and `e5` (export and the report). Stay out of what they are changing: `Analysis/IssueCatalog.cs`, the rules,
  `IssueWriter`, anything named export or report. If one of your items needs a change there, say so in your report
  instead.

## Read first

- `CLAUDE.md` (repo root): the engine rules (only `Storage/` writes a crawl file: `CrawlWriter` while a crawl runs,
  `IssueWriter` for analysis; HTML is parsed once) and the conventions. `engine/README.md`.
- `docs/v2-sidecar-api.md`: "Conventions"; "URLs" (its `search`, "a plain scan for now"), "Lookup", "The
  inspector", "Issue groups", "Links" with "How the links are read", "Sitemaps", "robots.txt"; "Engine changes E3b
  made", "Engine changes from the E3b review" and "Engine changes E3c made" (how a running crawl's reads answer
  for one moment, the link summary, the fold of the log: your reads must keep those properties).
- `docs/v2-ui-ia.md` (the shell's top bar: favicon, search and the palette; "Go to URL"), `docs/v2-ui-brief.md`
  ("Performance budgets": first page of a grid 300 ms, a key to a visible response 100 ms; "Favicon").
- What the screens' builds and reviews asked for (the earlier session's scratchpad, read-only):
  `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\0dc66a3f-9b82-4a98-a271-d8ca3b28f098\scratchpad\links\report.md`
  and `...\sitemaps-robots\report.md` ("Left out, and engine findings"), and `...\e3c\checkpoint.md` (what E3c
  measured about link passes and why it didn't build a table of link texts).

## What to build

Each item: design it, write its contract section, build it, test it, measure it on the 100k crawl against the
budget, and say what a running crawl's read of it does.

1. **Fast search of a crawl's URLs.** Today the URL grid's `search` (URL or title contains the text) is a scan.
   - Make it meet the grid's budget on the 100k crawl (first page 300 ms at p95, typed text of 1 to 40
     characters, including one-letter and no-match searches), and say what it costs: the file's size, the crawl's
     speed, the analysis's time. Measure the alternatives before you choose (an FTS5 index with a trigram
     tokenizer, built while crawling or by the analysis; something smaller): the bench's memory must stay flat and
     its speed within noise, and a file made by an earlier engine must still search (slowly is fine, wrongly is
     not).
   - **`/search`, for the palette**: given a few typed characters, the best few matches among the crawl's URLs, by
     path and by title, ranked so the page a person means comes first (exact path, then the start of a path
     segment or a title word, then anywhere), each with what the palette needs to show and open it. 50 ms at p95 on
     the 100k crawl, on every keystroke, and correct while a crawl runs.
2. **Faster link reads.** A links text filter takes 0.5 to 0.8 s and a new search 0.9 to 1.9 s on the 100k crawl;
   a filter on the source's depth takes 1.46 s. E3c's idea: run the pass over every link on several connections.
   Measure it (and anything better you see) for those reads and adopt it if they come inside or near the 300 ms
   budget with no cost to the file or the analysis, without holding more read slots than the sidecar's rule
   allows other work, and without lengthening the time a running crawl's log goes unfolded. If it doesn't pay,
   say so with the numbers and leave it.
3. **The favicon.** The site's icon, fetched once by the crawl (at its start, politely, after robots.txt allows
   it), kept in the crawl file by `CrawlWriter`, and served by the sidecar for the library, the top bar and the
   sidebar. Decide and document: where it is looked for (the home page's `<link rel="icon">` candidates, then
   `/favicon.ico`), which one is kept (the best size for a 16 to 32 px tile at 2x), the size cap, the types
   accepted, and what is served for a crawl that has none (nothing: the app draws a letter). It is content from
   the site, so it is hostile: sniff the bytes rather than trust the header, never serve SVG as anything a
   browser could run script from, cap the size, and send headers that keep it an image. A continued crawl and a
   crawl made before this have none and don't fetch one late unless that is trivially safe. The fetch is not one
   of the site's URLs: it appears in no grid and no count (see how the site-version probes are kept out).
4. **robots.txt origins can be searched and filtered**: `POST /robots` takes text to find an origin by, and a
   filter for the origins that blocked something, with the counts a filter bar shows.
5. **The inspector can link exactly.** `/urls/inspect` names the sitemaps that list the URL (ids and URLs, capped
   with a total) and, for a URL robots.txt refused, the origin and the line that refused it, so the app can open
   that sitemap and that line. If `/robots/blocked` needs to answer for one URL to do that, add it.
6. **From links to URLs.** The URL grid can be filtered to "the sources (or the targets) of the links matching
   these link filters", so the Links screen can offer "Show these pages in URLs", landing on a count the links
   view can state. And a links view's **distinct ends** can be read as a list (for "Copy the target URLs" on a
   view of more than 10,000 links), paged, in a stated order, inside the budget or honestly slow with progress.
7. **A check's count has a true name.** `/issues/{checkId}/groups` calls its count `pages` even for a check on
   URLs. Add a rightly named field beside it and keep `pages`.
8. **The analysis's `inlinks` and the stored links disagree by 1 or 2 for about a tenth of URLs** on the 100k
   crawl (the inspector's "N hyperlinks" can't be linked to Links because of it). Find the cause. If the stored
   links are right and the fix is in the reader, fix it; if the fix is in the analysis (which another agent is
   changing), write up exactly what is wrong and what the fix is, and stop there.
9. **The analysis's limits can be changed.** Settings is about to gain an Analysis section ("every threshold"),
   and today the sidecar's analysis settings are the engine's defaults with no way to change them
   (`CrawlManager.AnalysisSettings`). Add what Settings needs, modelled on the crawl options' defaults
   (`GET` and `PUT /api/crawl-options/defaults`, `validate`): read the settings in force with each limit's default,
   unit, allowed range and one-line "what this does"; save new ones (kept in the state folder, validated field by
   field); reset one or all. An analysis that ran with other limits is already told as out of date: check that
   holds after a change, for a crawl open at the time and for the library's items, and that a running analysis
   isn't changed under its feet. This is the sidecar's side only: don't change the rules or the settings class's
   existing names (another agent reads them by name).
10. **Docs and types**: a contract section per item, a new "Engine changes E6 made" section, additive TypeScript
   types in `app/src/api/types.ts` with a typed client function for each new call beside the existing ones (no
   screen code), `engine/README.md`, and `si readbench` cases with gates for the new reads, so the gate would
   catch a regression.

## Verify

All green at the end and seen green by you: `dotnet build engine/ShoutingIguana.Engine.slnx -c Release` (warnings
are errors), `dotnet test engine/ShoutingIguana.Engine.slnx`, `si bench --pages 100000` once at the end (every
planted issue found, nothing else flagged, memory flat, the speed beside the base's measured the same hour),
`si bench --read-links --max-log-mb` as CI runs it, and `si readbench --gate`. If you touched `app/src/api/`: from
`app/`, `npm ci`, `typecheck`, `lint`, `format:check`, `test`. Every number an endpoint returns is checked against
SQL of your own on the bench crawl and the garden crawl, and every read is exercised while a crawl of the test
site runs (the growing-crawl hammer tests show how). Every behaviour gets a test; for each item, show one test
fails with the behaviour removed (restore a mutated file with a fresh timestamp).

## At the end

Report first. The orchestrator will then ask you to merge `ui/step-3` into your branch (the other two engine
branches will have landed there) and re-run the checks.

## Report back

Short: the commits; per item, what you built or why not, the contract's names exactly, and its measured numbers
against its budget (before and after); what the search index costs (file size, crawl speed, analysis time); the
final build, test, bench and readbench numbers; anything you found in the engine; what is not verified. No file
dumps.
