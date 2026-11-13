# E6 review (`ui/e6` @ ffd1bca): findings

Saved by the orchestrator from the reviewer's final message. No high-severity defect. Every number checked against
the reviewer's own SQL agreed. Proofs in this folder: `review_audit.py`, `review_hammer.py`, `review_search_mem.py`,
`review_outofdate.py`, `proof-*.cs`, `proof-*.diff`, `logs\`.

1. **Medium - `engine/src/ShoutingIguana.Cli/Benchmarking/ReaderBenchmark.cs:86` - CONFIRMED.** The `link-pass-split`
   gate is 1,500 ms, but every one of these passes on one connection already comes in under that. If splitting
   silently stopped (`BorrowHelpers` returning null, `SplitFrom` or the Live check going wrong, `ReadSlots` handing
   out none), the gate would still pass. Reviewer's run (`readbench --only "every link read" --runs 10`,
   `logs/readbench-links.log`): worst one-connection p95 1,450.9 ms; worst split p95 521 ms. On a 4-vCPU CI runner
   `HELPERS` is only 3.
   - Fix: gate each split case against its one-connection twin in the same run (e.g. split p50 at most 0.6x single
     p50 when there are 4+ cores), AND fail when the `Split` hook reports 1 connection for a split case.

2. **Low - `Favicons/FaviconSniffer.cs:48` (PNG) and `:142` (ICO) - CONFIRMED.** The sniffer reads only headers, so
   it keeps things that aren't drawable images (proof `proof-ReviewSnifferProofTests.cs`, 3 of 3 fail): a 24-byte
   PNG header with no image data; a PNG header followed by an HTML/script page; an ICO whose directory says 16x16
   but whose embedded PNG declares 60,000x60,000. A junk `/favicon.ico` gives the app a broken image instead of the
   letter fallback, and the size cap doesn't hold for an image inside an ICO. (Nothing can run: `nosniff` + CSP.)
   - Fix: check the embedded PNG's IHDR and the BMP header size against the directory entry and the 1,024 px cap,
     and require minimal structure (PNG: IDAT and IEND; GIF: a trailer), or decode for real.

3. **Low (latent) - `Storage/LinkGridReader.cs:888` - PLAUSIBLE.** Inside the split pass each helper thread calls
   `NewTally(context)` on the main read's context, which reads the main context's non-thread-safe `_once`
   Dictionary and would run SQL on the main connection if `urls|largest` or `urls|count` weren't already held.
   Instrumented (`proof-crossthread-instrument.diff`, `proof-crossthread-ReviewProofTests.cs`): 2,800-3,800
   cross-thread reads, 0 writes, 0 SQL today, only because `NofollowPages` / `UrlIds` load them first.
   - Fix: compute `LargestUrlId(context)` and `UrlCount(context)` once before the tasks start and build each part's
     tally from those values.

4. **Low - `Storage/LinkGridReader.cs:924` - PLAUSIBLE.** A helper connection that throws while opening fails the
   whole read; only a stamp mismatch falls back to one connection.
   - Fix: in `OpenAlongside` / the helper task, treat any non-cancellation exception like a stamp mismatch (null,
     so the pass runs again on one connection). Test it with an injected failure.

5. **Low (doc vs code) - `Storage/Models/SearchText.cs:261` and `:281`; `docs/v2-sidecar-api.md` Search -
   CONFIRMED by reading.** The doc's `start` rank isn't what the code does: text at the very start of the URL ranks
   `start`, so "http" ranks every URL `start`; text after any `/`, including inside a query string, counts as
   starting a path segment; in a title, text whose first character isn't a letter or digit is `start` only at
   position 0, where the doc says "the character before it isn't a letter or a digit".
   - ORCHESTRATOR'S DECISION: make the code do what is useful and the doc say it. A URL's `start` is the start of
     the host (after the scheme and `www.`) or the start of a path segment; never the scheme, and never inside the
     query string or fragment. A title's `start` is as the doc says. Tests for each case.

6. **Low (types) - `app/src/api/client.ts:323-331`, `types.ts:695-700` against
   `Sidecar/Options/AnalysisLimits.cs:111-120`, `AnalysisSettingsStore.cs:73` - CONFIRMED by reading.** The TS types
   say `invalid-settings` errors are keyed by limit id, but the sidecar also uses `settings` (whole-set) and
   `limits[n]` (on reset). Also: `GridQuery.linkEnds` is typed for `/links`, which refuses it with 400; and
   `mock/mockTransport.ts` has no handlers for the E6 routes.
   - Fix: widen the error-key type and document `limits[n]`; type `linkEnds` only where it is accepted; add mock
     handlers for every E6 route (search, link ends, favicon, analysis settings, robots search) so screens and
     stories can use them.

Reviewer's measurements (100k copy, machine ~19% loaded): split passes 87-279 ms p95 (one pattern case 521 ms half
way down) against 296-1,451 ms on one connection; kept keystroke 1.3-4.2 ms p95; first search 78 ms p50, 89 ms p95
(the doc says 48-55 / 61-72: update the doc's figure); kept text 11.4 MB on the bench crawl, ~106 bytes a URL (doc:
~95: update).
