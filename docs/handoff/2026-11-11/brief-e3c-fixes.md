# Brief: fix what the review found in engine E3c

A fresh-eyes review of engine E3c (Shouting Iguana v2 engine: C#/.NET 10, one SQLite file per crawl, a sidecar HTTP
API the desktop app reads) found 7 defects: one medium, six low. Fix them. Work ONLY in the worktree
`C:\Projects\ShoutingIguana\.claude\worktrees\e3c` (branch `ui/e3c`, HEAD `4400814`, clean). Small commits, one per
finding (`Engine: ...`, `Sidecar: ...`, `Cli: ...`, `Docs: ...`, saying what now holds), each ending with
`Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`. Do not push, merge or touch other worktrees or branches.
No bare `git stash`. Stay out of `app/` except `app/src/api/{types,grids,client,inspector}.ts`.

- S1 = `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\0dc66a3f-9b82-4a98-a271-d8ca3b28f098\scratchpad`
  (the earlier session's files: read-only for you; copy what you use).
- S2 = `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad`
  (yours: `S2\e3c-fixes\`).

## Read first

1. `S1\e3c-review\findings.md`: the 7 findings, each with where, the defect, a scenario, the proof and a suggested
   fix. The proofs are in `S1\e3c-review\` (`wal_growth.py`, `stats_first.py`, `ReviewProofTests.cs`, `mutate.py`,
   `rv.py`). Its crawl files are in `S1\e3c-review\data\` (`bench.sicrawl` is the 100k bench crawl, `garden.sicrawl`
   a real site, `bench-gaps.sicrawl` a copy with gaps in its link ids).
2. `CLAUDE.md` (repo root): the engine rules, and **never rename an existing public function**.
3. `docs/v2-sidecar-api.md`: "Conventions", "Engine changes E3c made". `engine/README.md`.
4. The build agent's notes, for what it measured: `S1\e3c\checkpoint.md`.

Copy a crawl file before opening it with anything that writes. Never touch
`%LOCALAPPDATA%\ShoutingIguana\dev-crawls`. Never crawl a real site (`si testsite` serves the synthetic one).

## How to fix each one

- Reproduce it first, with the review's proof or a test of your own that fails. Fix the cause. The suggested fix is a
  suggestion: if you see a better one, take it and say why.
- **Every fix gets a test that fails without it.** Prove that: with the fix in place the test passes; revert only the
  fix and see the test fail; restore the fix **with a fresh timestamp** (an incremental build otherwise keeps the
  mutated code). Note per finding the test's name and that you saw it fail.
- Look for the same mistake elsewhere before you close a finding.

Notes on particular findings:

- **1 (the WAL, medium): the one that matters.** The Links screen is about to merge, and it is exactly this client.
  - Only `Storage/` writes: the checkpoint belongs to `CrawlWriter`, under the exclusive read gate, past a size. It
    must not break what the E3b fix round built: a running crawl's links reads answer for one moment, and no read
    may fail, block for long or answer wrongly because of a checkpoint. Run the growing-crawl hammer test
    (`EveryReadOfACrawlAsItGrowsAgreesWithItself` and its neighbours) with the checkpoint firing often (a tiny
    threshold in the test) and assert no error and no wrong answer.
  - Measure before and after with the review's `wal_growth.py` rows (no reader, one links client, six links
    clients, two `/stats` clients): peak WAL, the crawl's pages per second, and the longest any read waited. The
    crawl must not get slower and the bench's memory must stay flat.
  - Make it something a gate catches: a test (and, if it is cheap, a `si bench` option CI can run) that crawls with a
    links reader attached and fails if the WAL passes a bound.
  - Check the analysis too (`IssueWriter`): can a reader hold its WAL open the same way while an analysis writes?
    Measure it; fix it the same way if so.
- **2**: bring `stats-pass` inside 300 ms at p95 if a cheap, safe way exists. If not, record the miss honestly under
  the API doc's known limitations with the measured numbers and set its gate to what it measures plus headroom, not
  600. Give the kept read its own class with a gate of a few ms.
- **3, 4**: fix both, with the review's two proof tests brought in, in the engine's test style. For the related
  note in 4: find out from Microsoft's documentation whether `Stopwatch` keeps counting through sleep on Windows
  (and what it does on Linux and macOS), and make a machine that sleeps mid-crawl not count the sleep as crawling,
  if that can be done simply (for instance, a gap between two samples far longer than the sample period is not
  crawling time). If it can't be done simply, document it as a known limitation, with what you found.
- **5**: add each missing test, and see each fail with its fix removed (the review's `mutate.py` lists the eight
  mutations). The live-crawl test must also check answers against the truth, not only against themselves, where
  that is cheap.
- **6**: correct the three sentences. Where "each figure lands on its count in the grid" needs filters, say which
  filters, per figure, as a table the UI can follow. Keep the TypeScript comments in `app/src/api/types.ts` in line.
- **7**: measure it first: run the sidecar with two read slots (see `SidecarOptions`) and two link passes on a
  running crawl, and time another crawl's `/stats`. If it waits, fix it (a kept answer needs no slot), with a test.
  If it doesn't, say so and leave it.

Out of scope: anything at 1M URLs; a table of distinct link texts; the parallel link pass; UI code.

## Verify

All green at the end and seen green by you: `dotnet build engine/ShoutingIguana.Engine.slnx -c Release` (warnings
are errors), `dotnet test engine/ShoutingIguana.Engine.slnx`, `si readbench --gate` on a fresh 100k bench crawl,
`si bench --pages 100000` (every planted issue found, nothing else flagged, memory flat; the build agent saw 36 to
42 s and 188 to 195 MB). Other agents are running UI test suites on this machine and memory is short (about 5 GB
free), so repeat a timing before you trust it, and never run two benches at once. If you touched `app/src/api/`:
from `app/`, `npm ci`, `typecheck`, `lint`, `test`. `AnalysisFreshness.VERSION` stays 6 and `ChecksFingerprint` must
not change. Update the API doc's "Engine changes E3c made" for what changed.

Stop only processes you started, by their own process id. Never run `dotnet build-server shutdown`: other agents
share the build servers. Your own files go under `S2\e3c-fixes\`; update a `checkpoint.md` there after each finding
(done, decisions, verified vs not, next step). Keep command output to summaries and failures.

## Report back

Short: per finding, the commit, the fix in a line, the test and "seen failing without the fix" (or why not fixed);
the WAL table before and after, with the crawl rate; readbench's classes with their timings; the final build, test
and bench numbers; anything else you found; what is not verified. No file dumps.
