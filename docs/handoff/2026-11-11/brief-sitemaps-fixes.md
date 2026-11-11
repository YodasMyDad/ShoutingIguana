# Brief: fix what the review found in the Sitemaps and robots.txt slice

A fresh-eyes review of the Sitemaps and robots.txt slice of the Shouting Iguana v2 desktop app (React + TypeScript +
Tailwind v4 + Radix + TanStack Router/Query in a Tauri 2 shell, a .NET sidecar behind a localhost API) found 17
defects. Fix them. Work ONLY in the worktree `C:\Projects\ShoutingIguana\.claude\worktrees\sitemaps-robots` (branch
`ui/sitemaps-robots`, HEAD `8d77343`, clean). Small commits, one per finding where they separate cleanly
(`App: ...`, saying what now holds, not "fix review item 3"), each ending with
`Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`. Do not push, merge or touch other worktrees or branches.
No bare `git stash`. Stay out of `engine/`.

- S1 = `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\0dc66a3f-9b82-4a98-a271-d8ca3b28f098\scratchpad`
  (the build's files, read-only).
- S2 = `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad`
  (yours: `S2\sitemaps-fixes\`, create it; all your own files, and `tmp`, go there).

## Read first

1. `S2\sitemaps-review\findings.md`: the 17 findings (where, defect, scenario, proof, suggested fix), the verdict
   on "Crawled", and what the review didn't check. Proofs, and three crawls of the test site that show the faults
   (`crawls\`), are beside it (read-only; copy what you use, and copy a crawl before opening it).
2. `S2\ui-slice-rules.md`: the rules every slice follows and the kinds of defect earlier reviews found.
3. `CLAUDE.md` (repo root). **Never rename an existing exported function or component.**
4. `S1\brief-sitemaps-robots.md` and `S1\sitemaps-robots\report.md` (what was built and why),
   `S1\sitemaps-robots\checkpoint.md`.
5. `docs/v2-ui-architecture.md` (the grid, what this slice added), `docs/v2-ui-ia.md` (Sitemaps, robots.txt,
   Keyboard), `docs/v2-sidecar-api.md` (`GET /sitemaps`, `POST /sitemaps/urls`, `POST /robots`, `/robots/file`,
   `/robots/blocked`, the robots.txt states).

## How to fix each one

- Reproduce it first (the review's proof, or a test of your own that fails). Fix the cause, not the symptom the
  scenario happened to show. The suggested fix is a suggestion: if you see a better one, take it and say why.
- **Every fix gets a test that fails without it.** Prove that: with the fix in place the test passes; revert only
  the fix and see the test fail; restore the fix. Note per finding the test's name and that you saw it fail.
- Look for the same mistake elsewhere before you close a finding: a "known" figure built from a bucket that can be
  `null` (finding 1); a cursor kept as a row number in any list that changes under it (3); words that assume the
  crawl obeyed robots.txt (5); a banner that mounts late or announces itself during a crawl (7); a field keyed on
  its own value (8); the engine's raw text in a sentence (12).

Decisions already made (by the orchestrator), and notes on particular findings:

- **2 ("Crawled")**: one meaning per word, and the meaning a person expects on this screen.
  - "Crawled" means the crawl requested the URL: `fetched` less the URLs robots.txt refused. A URL that was
    requested and didn't answer is crawled, and its Outcome says what happened.
  - The list's columns become Listed, Crawled, Blocked by robots.txt, Not crawled, Non-indexable, and Listed is the
    sum of the middle three. One sitemap's quick filters gain "Blocked by robots.txt", with its count.
  - The Outcome cell uses the inspector's words (a URL that answered says "Answered", not "Crawled").
  - On one sitemap's page the UI can count this today with a read filtered by `outcome`. The list can't, cheaply:
    the engine is being asked to add each sitemap's count of listed URLs robots.txt refused to `GET /sitemaps`
    (and a count of its redirecting or broken URLs, for the quick filter that has no number today). **Build the
    list against those two fields now**, in the mock and the types, as optional fields in one place
    (`urls.robotsDisallowed` and `urls.redirectingOrBroken` are the working names: the orchestrator will confirm
    the engine's names in a follow-up message). While a sidecar doesn't send them, the list shows "Crawled" as a
    dash with a tooltip saying this crawl's engine doesn't say, never the misleading number.
- **3**: fix it in `GridCore` as an opt-in row identity, used by both lists. `ui/links` also changed `GridCore`
  (the activated column, the cursor's column, a header that says why it doesn't sort) and will merge before you:
  keep your `GridCore` edits small and local.
- **4**: the UI half only: a state that refuses nothing, with blocked URLs, says they were refused earlier in the
  crawl (true for a continued crawl) or by an earlier engine. The engine half goes to the engine agent.
- **6**: an index's counts are known only when its whole subtree was read. Otherwise a dash and "the crawl has N
  of its M sitemaps" (check what the API gives for M: the index's own count of entries).
- **10**: Ctrl+F reaches the file's search from anywhere on the origin's page, and the palette has the command.
  Don't change how the inspector's Source search works.
- **12**: plain words per kind of failure (the DNS name wasn't found, the connection was refused, it timed out, a
  secure connection couldn't be made, something else), with the engine's text in a tooltip and in the accessible
  description. See how Overview's alerts word the same failures, and share one function.
- **13**: normalise through `new URL(...).origin`, and test an internationalised domain against the sidecar on a
  crawl of the test site if the test site can serve one; otherwise unit-test both spellings.
- The review's "not checked" list is yours to check: the slice's e2e specs and its sweep re-run; the inspector's
  Source section in the app on a crawl that stored HTML (garden); a robots.txt near the size limit while a crawl
  runs (if the file is re-read each second, read it once and only its blocked counts each tick).

## Then: take in `ui/step-3`

When your fixes are done and green, the orchestrator will send you a message to merge `ui/step-3` into your branch
(Links and the engine's E3c will have landed there; expect real conflicts in `app/src/grid/GridCore.tsx`,
`app/src/api/queries.ts`, `app/src/keyboard/registry.ts`, `app/e2e/overview.spec.ts` and both UI docs). Don't merge
anything before that message. Report first.

## Checks

From `app/`, all green at the end and seen green by you: `npm ci`, `typecheck`, `lint`, `format:check`, `test`
(three runs, one under load), `build-storybook`, `test:stories` (also `-- --forced` and `-- --reduced-motion`),
`node --test "scripts/**/*.test.mjs"`, `build:sidecar`, `e2e` (your three specs also with `--repeat-each 10`). Your
ports: `SI_DEV_SERVER_PORT=58874 SI_DEV_SIDECAR_PORT=58875 SI_PREVIEW_PORT=58876 SI_E2E_SIDECAR_PORT=58877`,
`SI_STORYBOOK_PORT=6606` (never 6406). Run `npm run dev` and `e2e` with `TEMP` and `TMP` set to
`S2\sitemaps-fixes\tmp`. Other agents are running suites on this machine and memory is short: one heavy thing at a
time. **Stop only processes you started, by their own process id or your own ports, never by a command-line
match.** Never run `dotnet build-server shutdown`. Never edit `%LOCALAPPDATA%\ShoutingIguana\dev-crawls`; never
crawl a real site. After the last e2e run, retake the screenshots the fixes change into
`app/test-results/sitemaps-robots/`. Keep command output to summaries and failures. Update
`S2\sitemaps-fixes\checkpoint.md` after every few findings (done, verified, next).

## Report back

Short: per finding, the commit, the fix in a line, the test and "seen failing without the fix" (or why not fixed);
anything else you found; engine asks; the final check results with counts; what is not verified. No file dumps.
