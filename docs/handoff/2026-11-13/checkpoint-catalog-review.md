# Checkpoint: catalog-fixes review (read-only)

Brief: `..\brief-catalog-review.md`. Worktree `C:\Projects\ShoutingIguana\.claude\worktrees\catalog-review` (detached 5f5b83d).
Remove it when done (`git worktree remove --force`), from `C:\Projects\ShoutingIguana`.

## Done
- Read the whole diff (engine src, tests, docs stat). Built Release, 0 warnings.
- Probes: `ReviewProbes.cs` (copy here; removed from the worktree), output `probes-out.txt`, run log `probes-run.log`.
- Full `dotnet test` slnx running in background -> `full-test.log`.

## Findings so far (verified)
1. robots-txt-* checks on a crawl that ignored robots.txt: CONFIRMED same fault. sitemaps off: robots.txt asked 0 times,
   all three "ran, 0 rows" (even with robots.txt 500 on the start origin). sitemaps on: only the start origin's is read
   (for Sitemap lines): http origin's 404 not listed, other-origin never looked at.
2. charset-missing: `Content-Type: text/html; charset=` (empty) passes; text now says "no charset in the header". Pre-existing rule, low.
3. canonical-to-redirect: multi-hop to an unfetched URL says "ends at" (by reading). Low.
4. ReadUrl cost: 32 us standalone, ~2 us inside the page pass: 100k flagged pages = 0.2 to 3 s. Fine.
5. site-hidden: 6 end-to-end cases behave as the rule says (header none, googlebot, bingbot, refresh, delayed refresh, deep start).
6. BOM: utf8/16le/16be/truncated/contradicting header pass; space-then-mark, none, utf32be flagged; utf32le reads as utf-16le (as browsers).

## Left
- Full test result; final report; remove worktree.

## FINAL
- Full suite green at 5f5b83d: Engine 1172, Sidecar 176, Cli 21 (`full-test.log`). Bench 100k green on my build (`bench.log`).
- 100k analysis: 9.1-9.5 s as committed; 9.8-10.6 s with a URL lookup forced on every page (probe patch, reverted).
- `ReviewProbes2.cs`: canonical chain ending at an unfetched URL says "ends at".
- Worktree removed; bench crawl file deleted. Report given in the final message.
