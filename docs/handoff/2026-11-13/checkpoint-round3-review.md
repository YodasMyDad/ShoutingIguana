# round3-review checkpoint
Worktree C:\Projects\ShoutingIguana\.claude\worktrees\round3-review detached at 12f43ef. Read-only review.
Ports 56874-56877, SB 6456. TEMP/TMP = round3-review/tmp.
## Phase: setup, reading frame code
## Findings so far
(none)
## Findings (confirmed so far)
- HIGH 12f43ef deleted 11 unrelated e2e tests from states.spec.ts (design-system states describe + library error wrap); `fixture` now dead.
- HIGH/MED filling report row breaks mode.test.tsx 2 tests (asserts no Report link) -> round 4 must edit outside row. (log unit-filled.log)
- MED overview validateNoSearch strips ?score=1 -> Summary row needs router/search edits.
- MED CrawlSettings UsedSettings shows proxy "None" when creds redacted (formFromOptions blanks it).
- MED NewCrawl focus param in Simple for non-simple field: switches to Advanced, early return, focus never placed; toast lacks Back to Simple / id.
- MED ContinuePanel: NaN numbers -> JSON null -> silently unchanged; no validation.
- LOW/MED New crawl sends saved/copied header names with '' values (Home strips them) -> engine sends empty headers.
- LOW Simple form errors (speed, maxUrls) not linked to fields (role=none p); header error not linked.
- LOW Home & Forward `hidden` at give-way -> lost to SR.
- LOW pre-mode settings file -> Simple.
- LOW Ctrl+Enter global command stale `starting` -> double start (plausible).
Mock vite on 56874 (bg task bqs985but). stub screens in app/src/simple (untracked), filled table saved in scratch.
- CONFIRMED top bar at 200% zoom at min window 960 (480 css) running: section gone, switch cut "Advan"; 520 css: section "S". shots/window-200pct-*.png, sweep.json
- CONFIRMED ?score=1 stripped on overview. Rows merge cleanly in git (tested).
- CONFIRMED New crawl from=<missing crawl> -> only ErrorState, can't start. shots/new-from-missing-crawl.png
- CONFIRMED Simple focus=userAgent/headers/robots -> focus BODY. Simple limit error aria-invalid null, no describedby, engine words "Max URLs".
- CONFIRMED round-trip all 27 options ok; maxHtmlBytes 1e6 -> 1000342 drift; proxy 'None' (proof-review.test.ts)
- /settings/analysis in Simple turns Advanced on + toast "Analysis" while General shown; dead crawl /urls also turns Advanced on.
- Shortcut sheet in Simple lists "Open a crawl file…", "Close the inspector", group "Crawl" (words).
Next: write final report; cleanup (stop vite bqs985but by port 56874, remove worktree).
