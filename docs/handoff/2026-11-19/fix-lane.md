# UX fix round, 2026-11-19: the fix lane (`ui/ux-fix`)

Branch `ui/ux-fix`, worktree `.claude/worktrees/ux-fix`, cut from `ui/step-3` at `f2ae0922`. Nothing is pushed.

## Why

The owner read Issues > "Internal links without anchor text" (`empty-anchor-text`) on their own site and took the
check for wrong. It was right: their header logo is `<a href="/"><svg …></svg></a>`, with no aria-label and no
`<title>` in the SVG, on all 260 pages. The app never made the fix obvious: "How to fix it" was one paragraph at the
bottom of the pane, and its logo sentence spoke of alt text, which an inline SVG has none of. The owner: "if there's an
obvious fix, it needs to be very easy to read. In a brilliant UI and UX, so the user understands how to fix things."

## What the lane adds

**Engine** (`222aeccc`)

- `Analysis/Models/FixExample.cs` and `FixLanguage.cs`: a check's fix as code. `After` (the corrected code), `Before`
  (null when the fix adds something that isn't there), `When` (which case), `Where` (where the code goes, written to
  follow a label: "in the page's <head>"), `Language` (`Html`, `HttpHeader`, `RobotsTxt`, `Xml`, `Json`), `Case` (a
  stable key, such as `icon-link`).
- `IssueDefinition.FixExamples`, empty by default. Not stored with an analysis, not in the checks' fingerprint: no
  version bump, no new planted case (catalog data, not a new check).
- 67 of the 170 checks have examples, 78 in all. `empty-anchor-text` has three, likeliest first: `icon-link`
  (aria-label on the link), `image-link` (alt on the image), `empty-link`. Its `Fix` now says what works for an inline
  SVG. Editorial and structural fixes keep their prose alone.
- `GET /api/checks` sends `fixExamples` on every check (`FixExampleDto`; `[]` for a check without). Documented in
  `docs/v2-sidecar-api.md`.
- `FixExampleTests`: how an example is written (one to four lines, `…` for elided content, a `When` and a unique `Case`
  on every example of a check with several), and for 64 examples of 54 checks that a page holding the code as it is
  gets the check and the same page with the fixed code doesn't, through `HtmlPageParser` and every page rule the
  analysis runs. An SVG's `<title>` is proved to name its link too. `content-type-mismatch` and
  `sitemap-lastmod-invalid` are held to what judges their values. Eleven checks' examples are written from their rules
  and not proved by a test, as their rules read more than a page: `blocked-by-robots`, `broken-fragment-links`,
  `canonical-to-first-page`, `hreflang-missing-return`, `not-in-sitemap`, `pagination-not-linked`,
  `redirect-temporary-cached`, `robots-txt-missing`, `robots-txt-no-sitemap`, `url-repetitive-path`,
  `url-tracking-parameters`.

**App** (`752f0119` and the commits after it)

- `ui/CodeExample.tsx` (+ `codeDiff.ts`, `codeTokens.ts`): one example in one frame. With a `before`: "Now" over
  "Fixed", the part that goes in a `<del>` and the part that's new in an `<ins>`; without: "Add", with `where` after the
  label. Syntax colours from the `--color-code-*` tokens by a small tokenizer by hand (no CodeMirror, no Lezer grammar:
  `@lezer/html` and the grammars it nests are only in the inspector's source viewer chunk, and would cost more than the
  screen). Long lines wrap; never a sideways scroller; at most 100 characters wide. "Copy" copies the corrected code.
- `screens/issues/HowToFix.tsx`: the heading, the fix's words, then the example. Several examples show one at a time
  as tabs (Radix Tabs, in the segmented control's look; arrow keys), named by each example's `when`; `preferredCase`
  shows its case first and marks its tab "On this site". A check without examples is the heading and the paragraph
  alone.
- `screens/issues/fixCase.ts`: `preferredFixCase(checkId, links)`, given the flags of each link the check named:
  `image-link` when most are `imageLink`, else `icon-link`; undefined for any other check or with no link to go by.
- `api/types.ts`: `FixExample`, `FixLanguage`, `Check.fixExamples?` (optional, as a sidecar from before it sends none).
- Stories: `Design system/Code example` (5) and `Screens/Issues how to fix` (7, with `InACheck`: the block beside
  "What's wrong" and "Why it matters" at 1920 px and over, stacked under them below that).
- Styles: one appended block at the end of `styles/components.css` (`.si-code-example*`, `.si-how-to-fix*`,
  `.si-fix-cases*`, with its own forced-colours rules) and one at the end of `styles/tokens.css`
  (`--how-to-fix-columns`).

## Mounting it in the pane, once `ui/ux-issues` has landed

Nothing here is mounted in the live screen: the pane is that lane's. The change is one element.

1. In `app/src/screens/issues/CheckDetail.tsx` (on the base, the `Explain` function: its `parts` entry
   `{ title: 'How to fix it', text: fix }`, drawn as `<h3 className="mb-1.5 text-13 font-semibold">` and a
   `<p className="max-w-(--measure-explain) …">`), replace the "How to fix it" heading and paragraph with

   ```tsx
   <HowToFix check={check} preferredCase={preferredCase} />
   ```

   `HowToFix` draws the same `<h3>` with the same classes, so the pane's outline doesn't change. Keep "Why it matters"
   as it is. `HowToFix` returns null for a check with no fix and no examples, as `Explain` drops an empty part.

2. `preferredCase` comes from what the pane has already read, never a request of its own:

   ```tsx
   const preferredCase = preferredFixCase(check.id, targets.map((target) => target.flags));
   ```

   where `targets` are the `IssueTarget`s (`api/inspector.ts`, `flags?: LinkFlag[]`) of the rows whose references the
   pane has loaded (`/issues/{checkId}/targets`: on the base `useWhereFound`, in the Issues lane `useRowReferences`).
   A group's `related` facts hold no flags (they are the page's own link's), so the flags come from rows. Until some
   are loaded, pass `undefined`: the first case shows, which for `empty-anchor-text` is the icon case, with no "On this
   site" marker.

3. Layout: where the pane sets "Why it matters" and "How to fix it" side by side, give "How to fix it" the wider
   column: the code wants up to 100 characters, the prose 62 (`--measure-text`; the base's paragraph is 46,
   `--measure-explain`). `--how-to-fix-columns` (`minmax(0, 46ch) minmax(0, 1fr)`) is what the `InACheck` story uses.
   Where the pane folds "why / how" away at narrow sizes, the fold holds `HowToFix` whole.

4. Tests and words that then need a look:
   - `app/src/screens/IssuesScreen.test.tsx`: the test "puts what the check flags ahead of why it matters and how to fix
     it" finds the fix by its text, which stays. Add one that a check with examples shows
     `getByRole('figure', { name: 'The fix as code' })`, and one for the "On this site" tab on `empty-anchor-text`.
   - `app/e2e/issues.spec.ts:50` (`heading 'How to fix it', level 3`) and `app/scripts/smoke-shell.mjs:438` (the text
     "How to fix it") stay true as they are.
   - `app/src/test/issuesSidecar.ts` and `evidenceSidecar.ts` build checks by hand without `fixExamples`: they compile
     (the field is optional) and show prose only. Give the fixture for `empty-anchor-text` the catalog's examples
     (`SIMPLE_CHECKS` has them) to see the block in the screen's stories.
   - Simple's "How to fix it" (`simple/ProblemScreen.tsx`) is untouched: it shows `plainFix`. `CodeExample` is in
     `ui/`, so Simple can show the same examples under its steps if the owner wants that; not done here.

## Files that may conflict on merge

- `app/src/styles/components.css` and `app/src/styles/tokens.css`: one block each, appended at the very end. Another
  lane appending at the end conflicts textually: keep both.
- `app/src/test/simple/engine-checks.json`: captured again from this branch's sidecar (`GET /api/checks`, written with
  two-space indents, then `prettier --write`). The diff is `fixExamples` on every check and the reworded fix of
  `empty-anchor-text`. Any lane that changes the catalog captures it too: **capture it once more from the merged
  sidecar** rather than merge the two by hand. There is no script for it: run the sidecar
  (`SI_SIDECAR_TOKEN=… dotnet engine/src/ShoutingIguana.Sidecar/bin/Release/net10.0/si-sidecar.dll --data-dir … --state-dir … --port …`),
  `curl -H "Authorization: Bearer …" http://127.0.0.1:<port>/api/checks`, write it indented, format it.
- `engine/src/ShoutingIguana.Sidecar/Api/Models/CheckDto.cs` (a new init property and one more assignment in `From`)
  and `Serialization/SidecarJsonContext.cs` (one converter line).
- `engine/src/ShoutingIguana.Engine/Analysis/IssueCatalog.cs`: `FixExamples = […]` is the first line inside the
  initializer of 67 checks, and one sentence of `empty-anchor-text`'s `Fix` changed. A lane that edits a check's
  texts or severity touches lines beside these.
- `app/src/api/types.ts` (`Check` gains a field at its end; two new types after it), `docs/v2-sidecar-api.md`,
  `docs/v2-ui-architecture.md` (one row in the components table).

## Verified, and not

See the lane's report for the commands and their results. In short: the engine builds with no warnings and its three
test projects pass; the app's typecheck, lint, format check and unit tests pass; the built Storybook's stories pass axe
in light and dark, under forced colours and with reduced motion; the stories were looked at, at 2560 × 1380 and
1280 × 720, light and dark, and under forced colours.

Not run, as nothing here is mounted in the real pane: the e2e specs, `smoke:shell`, a Tauri build, the Linux Docker
check. Not done: the mount itself, and the pane's tests for it.

## Found on the way

- `image-legacy-format`: the catalog's `Fix` offers "keeping the old format as a fallback in a `<picture>`", but the
  rule (`Analysis/Rules/ImageRules.cs`, the loop over image links that fills `legacy`) flags every `<img>` whose file
  is a JPEG, PNG, GIF or BMP of 50 KB or more, in a `<picture>` with a WebP source or not. A site that follows the fix
  as written keeps the notice. The example here therefore serves the WebP file from the `<img>` itself. Either the rule
  should pass an image whose `<picture>` offers a modern source (`ImageFact.InPicture` knows), or the words should stop
  offering the fallback: the engine lane's to decide.
- The plain words of `empty-anchor-text` (`plainFix`) already tell an image from an icon; nothing to change there.
