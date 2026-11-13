# handover-review checkpoint
Worktree C:\Projects\ShoutingIguana\.claude\worktrees\handover-review (detached fc7d35d). Review 0a79de0..fc7d35d. Read-only.
Ports dev 53874 sidecar 53875 preview 53876 e2e 53877 storybook 6156.

## Done
- Shell security read: commands.rs exported_file/exported_path, api.rs export(), notify export_notice, model ExportJob,
  capabilities. Engine side ExportTarget.Check (ext per format, ADS refused, UNC/network refused in sidecar TargetPath),
  File.Move overwrite commit, report CSP no script, CSV formula guard. Looks right mostly.
  Minor: tests/capabilities.rs the_webview_still_gets_what_it_uses doesn't list the 2 new commands.

## Findings so far
- CONFIRMED (vitest app/src/screens/settings/reviewproof.test.tsx): Analysis.tsx:171 errors shown only for touched
  fields; engine keys min>max on titleMaxLength -> raising Shortest title to 70 shows no reason.
- CONFIRMED (e2e/reviewproof.spec.ts): Issues flat check sorted desc exports in natural order (offers.ts:264 issuesOffer no sort; kind check).
- CONFIRMED low (vitest app/src/export/reviewproof.test.tsx): ExportDialog.tsx:106 asCsv in browser sends .xlsx path w/ csv (too-large is raised mid-run so rarely reachable).
- code: Analysis.tsx:104 stale-save guard compares values to itself (dead).
- code: saved views ignore `links` (UrlsScreen.tsx:333).
- code: ExportDialog.tsx:95 error context uses stale `path` not `target` (Tauri: network sentence not used).
- Links export + sitemap export row-for-row OK (e2e proof passes).
- tests/capabilities.rs the_webview_still_gets_what_it_uses lacks new commands (low).
- code: robotsBlockedOffer reason narrowing words "every URL it blocked" (offers.ts:191,200).
- code: backToLinks drops links sort/cols (UrlsScreen.tsx backToLinks).
- code: export-busy sentence wrong for "engine is shutting down" (exportWords.ts:241).
- Pending proofs in e2e/reviewproof.spec.ts: Ctrl+E from search box; All N in URLs.
## Running
- storybook build + test:stories --forced (bg bvvsmatly) on 6156.

## Next
app side: shell/os.ts, export/*, palette search, favicons, Analysis, linkEnds; then run unit tests/stories forced.
