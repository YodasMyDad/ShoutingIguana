# Brief: the catalog's fixes (engine, step 3, item 4)

Shouting Iguana v2 is an SEO crawler: a .NET 10 engine (`engine/`) and a React app (`app/`) that reads it through a
sidecar API. The catalog has 87 checks. A review of their plain-language texts on 2026-11-11 confirmed faults in the
technical texts and in three rules, and one new check is too eager. You fix them.

## Where you work

- Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\catalog-fixes`, branch `ui/catalog-fixes` (cut from
  `ui/step-3` at a782baf). Work ONLY there. Small commits (`Engine: ...`, `Tests: ...`, `App: ...`, `Docs: ...`,
  each saying what now holds), each ending with `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`. Never
  push, merge or touch another worktree or branch. No bare `git stash`. **Never rename an existing function, type or
  issue id** (issue ids are stored in crawl files).
- Your scratch folder (logs, `tmp`, `checkpoint.md`):
  `C:\Users\LeeMessenger\AppData\Local\Temp\claude\e2e5beaddde4c070\8e7045a4-9ede-460a-be29-971bc406a6c0\scratchpad\catalog-fixes\`.
  Update `checkpoint.md` there at each phase boundary (done, decisions, verified vs not, next step).
- Other agents work in sibling worktrees at the same time: `e5` (export and the report: it prints the catalog's
  texts), `e6` (search, link reads, the analysis settings' API), `sitemaps-robots` (UI). Stay out of anything named
  export, report, search or `Sidecar/Options/AnalysisLimits.cs`. Keep your edits to shared files small.
- Ports for anything you run in `app/`: `SI_DEV_SERVER_PORT=63874 SI_DEV_SIDECAR_PORT=63875 SI_PREVIEW_PORT=63876
  SI_E2E_SIDECAR_PORT=63877 SI_STORYBOOK_PORT=7106`, with `TEMP` and `TMP` set to a `tmp` folder inside your scratch
  folder. Stop only processes you started, by their own process id or your own ports, never by a command-line
  match. Never run `dotnet build-server shutdown`. One heavy thing at a time (a build, a test suite, the bench).

## Read first

- `CLAUDE.md` (repo root): the engine rules and the conventions. A changed check needs its planted case on the
  synthetic site with its expected count, because the tests compare the whole issues table.
- `docs/v2-rebuild.md`, section "Where step 3 stands", item 4 of "To resume, in this order" (your job, in short).
- `docs/handoff/2026-11-11/plain-words/notes.md`: section "Technical texts I think are wrong or misleading (not
  changed)" and the numbered notes on `site-hidden-from-search`, `charset-missing`, `blocked-by-robots`,
  `canonical-to-redirect`.
- `docs/handoff/2026-11-11/reviews/plain-words-review.json`: the second agent's 57 findings; the ones about the
  technical texts and the rules are yours (the plain words' own findings were already corrected).
- `docs/handoff/2026-11-11/checkpoint-score.md`: how `site-hidden-from-search` was built and scored.
- `engine/src/.../Analysis/IssueCatalog.cs`, `Analysis/Rules/`, `Analysis/AnalysisFreshness.cs`, and the plain words'
  one data file `engine/tests/ShoutingIguana.Engine.Tests/Analysis/PlainWords/plain-words.json` with its generator.
- `docs/v2-sidecar-api.md`: "The score", `GET /api/checks`.

## What to do

1. **`site-hidden-from-search` must also require the home page to be noindex.** As built it counts noindex pages
   only: a shop with many noindexed filter pages could be told its whole site is hidden, which costs 60 points of
   the score. It should fire only when the start URL's page (after its redirects, i.e. the page the crawl's start
   URL ends on) is itself noindex AND the share it already requires holds. Read the rule first and keep whatever
   else it requires. Say in your report exactly what the rule is now. Planted cases: one site state where it must
   fire, and one where many pages are noindex but the home page is not, which must not fire. Check its technical
   and plain texts still say what the rule does.
2. **The faults in the technical texts** that the review confirmed (twelve, by the status doc's count: make your
   own list from the two files, and list in your report each one, what it said and what it says now; if you find
   the count differs, say so). A text must say what its rule does: read the rule before you change its text. Don't
   restyle texts that are right. Where a technical text changes meaning, check the check's plain words still agree
   and correct them in `plain-words.json` (regenerate; never hand-edit generated output).
3. **Three behaviours:**
   - `charset-missing` ignores a byte-order mark: a page whose bytes start with a UTF-8 (or UTF-16) BOM declares
     its encoding and must not be flagged. If the parser doesn't keep that fact, add it to `PageFacts` where HTML
     is parsed once (never re-parse); if the stored body no longer has the bytes, find where the fact has to be
     captured (the crawl) and say what that means for crawls already on disk.
   - `blocked-by-robots` reads as passed, where it should be skipped with a reason, when the crawl was told to
     ignore robots.txt. Use the existing mechanism for checks a crawl can't support (a skip reason with its plain
     words: there are 15 skip reasons with plain texts; a new reason needs both texts and whatever lists them).
   - `canonical-to-redirect` gives no final URL: its detail should name where the canonical's redirect ends.
4. **`AnalysisFreshness.VERSION`**: raise it (7 to 8) if any stored analysis would now differ, and say so. Follow
   how the last bump was done (the app's tests and the e2e setup know about stale analyses).
5. **Docs**: `docs/v2-sidecar-api.md` where it describes any of this, `engine/README.md` if it lists checks, and
   the check count stays 87 unless you say why not.

Every fix gets a test you have seen fail without it (revert the fix, see the test fail, restore: a mutation script
must restore files with a fresh timestamp, or incremental builds keep the mutated code).

## Checks, all seen green by you at the end

- `dotnet build engine/ShoutingIguana.Engine.slnx -c Release` (warnings are errors), `dotnet test
  engine/ShoutingIguana.Engine.slnx -c Release`.
- `si bench --pages 100000` once: every planted page and issue found, nothing else flagged, memory flat.
- The app shows these texts: from `app/`, `npm ci`, `typecheck`, `lint`, `format:check`, `test`,
  `node --test "scripts/**/*.test.mjs"`, `build:sidecar`, `e2e` (copy the git-ignored fixtures first:
  `cp -r C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3/app/design/data/fixtures app/design/data/`). Fix
  app tests, stories or mocks that quote a text you changed; change nothing else in `app/`.
- Run the garden crawl's copy through the new analysis (copy
  `%LOCALAPPDATA%\ShoutingIguana\dev-crawls\www.gardenfurnituresets.co.uk-20261031.sicrawl` into your scratch
  folder; never edit the original) and report what changed in its issues and its score (85 before).

## Report back

Short: commits (hash + one line); the rule `site-hidden-from-search` now is; each text fault, before and after;
the three behaviours, each with its test; the version; the garden crawl's differences; test counts; the bench's
result; what you left out or doubt, with evidence. State plainly what was verified and what was not. No file dumps.
