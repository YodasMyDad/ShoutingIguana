# Hand-over UI review (`ui/handover` @ fc7d35d): findings

Saved by the orchestrator from the reviewer's final message, with the orchestrator's decision under 4. No high. The
shell's two new commands were judged safe (they take only an export id; the shell asks the engine and opens only a
`done` job's `.csv`/`.xlsx`/`.html`). Proofs in `proofs\` beside this file.

1. **Medium.** `app/src/screens/settings/sections/Analysis.tsx:171` (`shownError` shows errors only for touched
   fields). The engine reports a shortest-above-longest problem on the LONGEST limit, so when the user changed the
   SHORTEST, the reason is never shown: set "Shortest title" to 70 (longest 60), nothing saves, the form says only
   "Not saved until it's put right". CONFIRMED (`proofs/analysis-untouched-error.test.tsx`). Fix: show every limit
   error the engine sends whatever was touched (an untouched limit's error also in the callout above the form).

2. **Medium.** `app/src/export/offers.ts:264-314` (`issuesOffer`), from `IssuesScreen.tsx:422`. A check that doesn't
   group shows a sortable grid (`IssueRowsGrid`, route `sort`), but its export is `kind: 'check'` with no sort and
   other columns: sort by URL descending, export "This check", the file comes out ascending. CONFIRMED
   (`proofs/reviewproof.spec.ts` "Issues: a flat check…", `e2e-proof.log`). Fix: for a flat check offer
   `kind: 'issueRows'` with the grid's `sort` and columns (`url`, `detail`), as the API doc's issueRows row allows.

3. **Low.** `app/src/export/useExportCommand.ts:42-53`. The Export command has no `inInputs`, so Ctrl+E does nothing
   while a text field has focus (every grid's search or filter field). CONFIRMED (`e2e-proof3.log`). Fix:
   `inInputs: true`.

4. **Low.** `UrlsScreen.tsx:333` (`useSavedViews`) and `openSaved` (:335). Saved views ignore the `links` narrowing:
   a narrowed view matches a saved view with the same filters (shows its name, "Save this view" disabled); saving
   while narrowed drops the narrowing silently. PLAUSIBLE. DECISION: include `links` in what is saved and matched
   (it is part of the view's route, so it is part of the view), within the saved views' byte budget.

5. **Low.** `app/src/export/ExportDialog.tsx:106-110`. In the browser build, "Export as CSV…" calls
   `start({format:'csv'})` with the stale `.xlsx` path. CONFIRMED (`proofs/dialog-ascsv.test.tsx`). Fix:
   `start({ format: 'csv', path })` with the swapped path.

6. **Low.** `ExportDialog.tsx:95`. The refusal's wording uses the stale `path` state, not `target` (the native
   dialog's answer): picking `\\server\share\x.csv` gives the engine's sentence, not the app's network-folder one.
   PLAUSIBLE. Fix: `exportErrorWords(error, { path: target ?? path, … })`.

7. **Low.** `Analysis.tsx:104`. The out-of-date-save guard compares `changeOf(values)` with `saving`, both from the
   same draft, so it never fires; a slow save answer after more typing still runs `took()` and says "saved".
   PLAUSIBLE. Fix: compare against the current draft key (a ref).

8. **Low.** `offers.ts:191,194,200` (`robotsBlockedOffer`). Narrowed by reason, the title, description and file name
   still say "The blocked URLs" / "every URL it blocked". PLAUSIBLE. Fix: name the reason in all three.

9. **Low.** `exportWords.ts:241-246`. The `export-busy` sentence always says "Four exports are running already, or
   one is writing this file now", but the engine also sends busy for "The engine is shutting down."
   (`ExportManager.cs:82`). PLAUSIBLE. Fix: fall back to the engine's title when it's neither case.

10. **Low.** `UrlsScreen.tsx:233` (`backToLinks`). "Back to the links" restores the links view's filter and q but not
    its sort or columns, though `routing/search.ts` says it "lands on the same view". PLAUSIBLE. Fix: carry `sort`
    and `cols` in `links` and restore them.

11. **Low, tests.** `app/src-tauri/tests/capabilities.rs:160-171` doesn't list `reveal_export_file` and
    `open_export_file`; `app/e2e/export.spec.ts:88-94` checks only the URL column and that each header title exists
    somewhere, not the grid's columns in order; nothing tests the flat-check sort (2) or Ctrl+E from a text field
    (3). Fix: add the two commands; assert the header equals the shown columns' titles in order; tests for 2 and 3.

Not verified by the reviewer: `cargo test` and `smoke:shell` (Open file / Open folder in the release build).
