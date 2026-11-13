# E6 review checkpoint (reviewer, read-only)
Worktree C:\Projects\ShoutingIguana\.claude\worktrees\e6-review (detached ffd1bca). Base a782baf.
Release build OK 0 warn.
## Findings so far
- LOW latent: LinkGridReader.Scan helpers call NewTally(context) on MAIN context from helper threads (reads main `_once` Dictionary).
  Proof: proof-crossthread-*.{diff,cs} -> 2800 cross-thread reads, 0 writes, 0 SQL in flows tried. Fix: hoist NewTally args before tasks.
## Checked OK
- SearchText ranking merge correctness (top-L by min score), LinkIdSet/LinkTally merge (same mode), Bounds, Within only Live, From/inOrder equiv.
- Gate deadlocks: Exclusive cancels via linked token; FoldLog only Live (no split).
## Next
- copy crawls to e6-review/crawls (bench.sicrawl = bench-final, garden.sicrawl); SQL audits via sidecar.
## Progress 2 (after audit-1)
- review_audit.py (search ref ranking 61 texts incl non-ASCII, edges, settings validation, ends cursor paging, robots search): 441/441 agree. logs/audit-1.log
- TS types agent: no hard mismatch; soft: invalid-settings keys `settings`/`limits[n]`; linkEnds typed on links grid; mock transport lacks E6 routes.
- Findings candidates: MED gate link-pass-split 1500 vs ~150-280 measured (single ~470-530 passes it) -> wouldn't catch split broken.
  LOW favicon header-only sniff (no decode; ICO embedded PNG dims unchecked).
- readbench --only "every link read" --runs 10 running -> logs/readbench-links.log
- review_hammer.py written (running-crawl hammer via sidecar + si testsite); run after readbench.
## Progress 3
- readbench links (logs/readbench-links.log): every single-conn p95 <= 1,451 ms < split gate 1,500 -> gate CONFIRMED can't catch split loss. split worst 521 (pattern, deep pages).
- proof-ReviewSnifferProofTests.cs: 3/3 FAIL (header-only PNG, PNG+HTML polyglot, ICO w/ 60000px PNG all accepted) CONFIRMED.
- hammer-1.log: 20k testsite crawl via sidecar, 10k reads, 0 problems.
- Full test suite running -> logs/test-all.log; then review_search_mem.py; then rerun audit (library outOfDate check added); remove worktree.
## Final
- Full suite green on ffd1bca: 1229 engine / 200 sidecar / 21 CLI (logs/test-all.log).
- Search kept text 11.4 MB on bench (106 B/URL) in-process; readbench search 1.3-4.2 ms p95, first 89 ms p95 (logs/readbench-search.log).
- outOfDate list vs item consistent (garden copy already out of date before; my audit's expectation was wrong).
- Worktree removed. Report sent.
