# Brief: the score, and what the catalog gains for Simple (engine, round 3)

Shouting Iguana v2 is a desktop SEO crawler: a C#/.NET 10 engine (one SQLite file per crawl, 86 checks in a
catalog, an analysis that writes issues), a sidecar HTTP API, a CLI (`si`), and a React app that reads the API. The
app is gaining a Simple mode for non-experts, designed in `docs/v2-ui-simple.md`. You build the engine half of that
design that isn't export: the score, the catalog's new fields, one new check, and a wording change in details.
Another agent builds export and the report in a sibling worktree (`e5`) and will build on what you make.

## Where you work

- Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\score`, branch `ui/score` (the step-3 branch with the
  reviewed engine build E3c merged in). Work ONLY there. Small commits (`Engine: ...`, `Sidecar: ...`, `Cli: ...`,
  `Docs: ...`, `Tests: ...`, saying what now holds), each ending with
  `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`. Never push, merge or touch another worktree. No bare
  `git stash`. **Never rename an existing public function, or an issue id** (ids are stored in crawl files).
- Your scratch folder:
  `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad\score\`.
  Keep a `checkpoint.md` there, updated at each phase (done, decided and why, verified vs not, next).
- Other agents are building, testing and benchmarking on this machine, and memory is short. One heavy thing at a
  time; run the 100k bench once, at the end; repeat a timing before you trust it. Stop only processes you started,
  by their own process id. Never run `dotnet build-server shutdown`. Never crawl a real site (`si testsite` serves
  the synthetic one). Never edit `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`: copy a crawl before opening it.

## Read first

- `CLAUDE.md` (repo root): the engine rules and conventions. `engine/README.md`.
- `docs/v2-ui-simple.md`: all of "The score" (formula, bands, ceiling, what it covers, the lies it can't tell, the
  worked examples), "Plain words" (the template and the fields), "The report" (to know what will read your work),
  and "What the engine needs" (the table: your rows are plain texts, plain category titles, plain skip reasons,
  `siteWide`, the new check, the score, the score in the library, the score on the command line, a code's name in
  details, out-of-date for the score).
- The design's reference code for the score: `app/design/simple/build/lib/score.mjs`, and its words:
  `app/design/simple/build/lib/words.mjs`.
- `docs/v2-sidecar-api.md`: "Conventions", "Checks", "Analysis", "Library", and the latest "Engine changes"
  sections, for how a contract change is written up.
- How "Fix first" is ordered today, in the app: `app/src/screens/issues/checkList.ts` and
  `app/src/screens/overview/answer.ts` (`rankChecksToFix`).

## What to build

1. **`siteWide` on a check**, in the catalog and on `GET /api/checks`: true for `robots-txt-unreachable`,
   `no-https`, `site-hidden-from-search`, `duplicate-site-version` and `robots-txt-unreachable-other-origin`.
   Check each of those really is one fault for the whole site as its rule reports it, and say so if one isn't.
2. **The new check `site-hidden-from-search`**: Error, Indexability, site-wide, reported once on the home page
   (or the start URL when the home page wasn't read): more than half of the pages read can't appear in search
   results because the site says so (noindex, or a canonical to another URL). Detail: "212 of 265 pages: 200
   noindex, 12 canonicalised". Settle the edges and write them down: what counts as a page here, a site of one
   page, a crawl stopped early, pages robots.txt blocked (not read, so not counted), a canonical to the same page
   on the site's other version. It gets full technical texts (title, description, `why`, how to fix, learn more)
   in the catalog's style, and its planted case with its expected count, as the repo's rule asks. It must not
   fire on the bench site or the existing test crawls unless you plant it: find how the other site-wide checks are
   planted and tested, and follow that. The catalog then has 87 checks.
3. **Plain-text fields** on every check, served by `GET /api/checks` beside the technical texts: `plainTitle`,
   `plainWhat` (sentences), `plainFix` (steps), `fixer`, `plainPassed`, `plainOneFix` (optional). Also
   `plainTitle` on each category, and `plainText` and `plainAction` for each skip reason, by reason code.
   - **Keep the plain texts in a file of their own, keyed by check id** (and one for categories and skip reasons),
     so a later agent can write all 87 without touching `IssueCatalog.cs` or the rules.
   - Put in now only the ten checks the design document writes out in full ("Ten checks, written out"), word for
     word. Every other check's plain fields are null until round 4 writes them; the API says null, never an empty
     string or the technical text.
   - A test holds the shape: a check has all its required plain fields or none; sentence 1 of `plainWhat` is at
     most 120 characters; no plain text holds a word the design's rules ban. Write the test that will demand all
     87 too, marked as skipped with the reason "round 4 writes the texts", so turning it on is one line.
   - Plain texts are words, not results: they must not change `ChecksFingerprint` or make an analysis out of date.
     See what the fingerprint covers for the technical texts today and follow it.
4. **The score.** One engine class works it out, to the design's version 1, exactly: the bands, the site-wide
   60 and 4, the ceiling of 89 while a Serious problem stands, notices at 0, the four words, what it covers, no
   score when there is nothing honest to say, a partial score for a crawl that didn't finish.
   - **It is worked out by the analysis and stored with it**, from that analysis's own counts, so it can never be a
     count from one moment over a total from another. That needs the URL count as it was when the analysis ran:
     store it with the analysis and serve it on the analysis (the app needs it too, for a URL check's share:
     "of the N URLs found" must be the analysis's N). Only `IssueWriter` writes it, as the rules say.
   - On `GET /api/crawls/{id}/analysis`: the shape the design's table gives (`score: { version, value, band, lost,
     beforeCeiling, heldBySerious, partial, covers: {...}, lines: [...], ifFixed: {...} }`, or `score: null` with
     `noScoreReason`). Settle the names against the contract's conventions and write the section.
   - **"Fix first" order becomes the engine's**: port the app's order exactly (severity, then how much it affects:
     read the app's code for the tie-breaks), expose it on the analysis (the ordered check ids), and work out
     `ifFixed` from the first five. Hidden checks are the app's preference: the engine's order and score ignore
     hiding, and the contract says so.
   - On the library item: `score: { value, band, partial }` or null, kept in the summary cache, with no slower
     library read (measure before and after on a library of the dev crawls' copies).
   - `scoreVersion`: an analysis scored by another version is out of date with `engineChanged`.
   - `si score <db>` prints the breakdown; `si issues` prints the score in its head.
   - Tests: every case in the design's "Worked examples" and "What each mix gives" tables and every row of "It
     can't say these lies", as test vectors, plus the garden crawl's real numbers on a copy of the garden crawl
     (85). Port `score.mjs`'s own cases. A property test: the score never rises when a problem is added, never
     passes 89 with a Serious problem, is 100 with none but notices, and is within 0 to 100.
5. **A code's name in details**: where an issue's detail says "HTTP 404" it reads "404 Not Found" (the status
   code's standard reason phrase; a code with no standard name stays "HTTP 599"). Find every rule that writes a
   status into a detail. Details are stored and grouped on, so this changes the analysis's output: bump
   `AnalysisFreshness.VERSION` once for all of this work, and update the expected issues tables.
6. **Docs**: the API contract ("Checks", "Analysis", "Library", and a new "Engine changes" section for this work),
   the TypeScript types in `app/src/api/types.ts` (additive, optional where an older sidecar wouldn't send it),
   `engine/README.md` for the new commands.

## The app's tests after your change

The catalog growing to 87 and the analysis version changing will break app tests, fixtures and mocks that
hard-code 86, the fingerprint, or "HTTP 404" in a detail. Those are yours: after the engine is green, from `app/`
run `npm ci`, `typecheck`, `lint`, `format:check`, `test`, `build:sidecar` and `e2e` (ports
`SI_DEV_SERVER_PORT=59874 SI_DEV_SIDECAR_PORT=59875 SI_PREVIEW_PORT=59876 SI_E2E_SIDECAR_PORT=59877`,
`SI_STORYBOOK_PORT=6706`; `TEMP` and `TMP` set to a `tmp` folder in your scratch folder; copy the git-ignored
fixtures first: `cp -r C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3/app/design/data/fixtures
app/design/data/`), and fix what your change broke: tests, fixtures, mocks and the API types only. No screen code:
no screen shows the score or the plain words yet. Say in your report what you changed there and why.

## Verify

All green at the end and seen green by you: `dotnet build engine/ShoutingIguana.Engine.slnx -c Release` (warnings
are errors), `dotnet test engine/ShoutingIguana.Engine.slnx`, `si bench --pages 100000` (every planted issue found,
nothing else flagged, memory flat: compare with 36 to 58 s and 188 to 195 MB, allowing for a loaded machine),
`si readbench --gate` on that bench crawl, and the app checks above. Every behaviour gets a test; for the score's
ceiling, the site-wide rule and the stored URL count, show the test fails with the behaviour removed (restore a
mutated file with a fresh timestamp, or the incremental build keeps the mutated code).

## Report back

Short: the commits; each of the six parts in a line or two with its tests; the names you settled for the API
(the other engine agent and the UI agents will build on them, so list every new field and command exactly); the
decisions you made on the new check's edges; the garden crawl's score from the engine; what you changed in the
app's tests; the final build, test, bench and readbench numbers; what is not verified. No file dumps.
