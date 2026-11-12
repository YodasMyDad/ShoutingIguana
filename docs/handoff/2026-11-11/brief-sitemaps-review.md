# Brief: fresh-eyes review of the Sitemaps and robots.txt slice (read-only)

Review the Sitemaps and robots.txt slice of the Shouting Iguana v2 desktop app (React + TypeScript + Tailwind v4 +
Radix + TanStack Router/Query, in a Tauri 2 shell, with a .NET sidecar it reads over a localhost API). It is branch
`ui/sitemaps-robots` at `8d77343`, cut from `ui/step-3` at `17b340d`: review `git diff 17b340d..8d77343` (20
commits, 69 files, about 8,950 lines). You did not write it and nobody has reviewed it.

- S1 = `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\0dc66a3f-9b82-4a98-a271-d8ca3b28f098\scratchpad`
  (the build's files, read-only).
- S2 = `C:\Users\LEEMES~1\AppData\Local\Temp\claude\e2e5beaddde4c070\f209576d-c3b5-4cef-9d6c-2666276ff82b\scratchpad`
  (yours: `S2\sitemaps-review\`).

Work in a worktree of your own: from `C:\Projects\ShoutingIguana`,
`git worktree add --detach C:\Projects\ShoutingIguana\.claude\worktrees\sitemaps-review 8d77343`. **Edit, commit or
push nothing in any other worktree.** In your own you may run `npm ci` and every check (`app/`), on ports
`SI_DEV_SERVER_PORT=58874 SI_DEV_SIDECAR_PORT=58875 SI_PREVIEW_PORT=58876 SI_E2E_SIDECAR_PORT=58877`, Storybook on
`SI_STORYBOOK_PORT=6606` (never 6406). Copy the git-ignored fixtures first:
`cp -r C:/Projects/ShoutingIguana/.claude/worktrees/sitemaps-robots/app/design/data/fixtures app/design/data/`.
`npm run build:sidecar` builds the sidecar. `npm run dev` runs the app against copies of the real dev crawls (a real
site of 4,521 URLs, "garden", and a 100k synthetic crawl); run it and `e2e` with `TEMP` and `TMP` set to
`S2\sitemaps-review\tmp`, as every worktree shares the default temp folder. Analyse the copies, never the files in
`%LOCALAPPDATA%\ShoutingIguana\dev-crawls`. The engine's synthetic test site (`si testsite`, see
`app/e2e/global-setup.ts`) is the only site you may crawl.

Other agents are at work on this machine (`.claude/worktrees/e3c`, a `links-review` worktree) and memory is short
(about 5 GB free): run one heavy thing at a time. **Stop only processes you started, by their own process id or your
own ports**: never by a command-line match, which has killed other agents' runs twice. Never run
`dotnet build-server shutdown`.

## Read first

- `S1\ui-slice-rules.md`: the rules the slice was built to, and the eight kinds of defect earlier reviews found.
- `S1\brief-sitemaps-robots.md` (what was asked) and `S1\sitemaps-robots\report.md` (what the build says it did,
  measured and left out).
- `docs/v2-ui-brief.md` ("Every view has every state", "Showing data", "The grid", "Navigation and the keyboard",
  "Accessibility", "Performance budgets", "Definition of done"), `docs/v2-ui-ia.md` (Routes, Sitemaps, robots.txt,
  Keyboard), `docs/v2-ui-design.md`, `docs/v2-ui-architecture.md` (the grid, "Rules every slice follows", what this
  slice added), `docs/v2-sidecar-api.md` (`GET /sitemaps`, `POST /sitemaps/urls`, `POST /robots`, `/robots/file`,
  `/robots/blocked`, the robots.txt states including `unanswered` and `unreachable`). `CLAUDE.md` for conventions.

Already known, don't report again: `POST /robots` has no search or filter; `/urls/inspect` names no sitemap and no
robots.txt line, so the inspector links only to the origin's file and to the Sitemaps list; a sitemap has no count
of redirecting or broken URLs; at 200% zoom the origins grid's Blocked column starts off-screen; nothing was checked
in the real Tauri shell. The engine branch `ui/e3c` is not merged into this branch yet: don't report the lack of
its fields.

## What to look for

Real defects, each with a concrete failure scenario, in order of weight:

1. **Every number lands on itself, on real data.** Listed, Crawled, Not crawled and Non-indexable for each sitemap;
   the pages no sitemap lists; each quick filter; "Blocked N URLs" per rule and per origin; the origins' order. On
   the synthetic, garden and 100k crawls, each count against where its link lands.
2. **Words that are true for the state.** "Crawled" counts URLs robots.txt refused, because the engine's `fetched`
   counts them: read the engine and the contract, say what an honest column would be (which `outcome`s it should
   count, or what the words should say), and whether the UI can compute it from what the API gives today. Each
   robots.txt state has its own sentence: found, no file (404), `unanswered` (no answer, the crawl went ahead),
   `unreachable` (no answer, the origin's URLs were refused), an error status, redirected, too large, not text.
   Each sitemap state too: not read, an error, not XML, an index and its children, compressed, too large, listed
   twice, found in robots.txt or at the default path.
3. **robots.txt shown truthfully.** The marked "obeyed group" and the line each blocked URL is put against must be
   what the engine actually applied (read the engine's robots parser: user-agent matching, the longest match, Allow
   against Disallow, `*` and `$`, a group split over several `User-agent` lines, comments, CRLF and non-UTF-8
   bytes, a byte-order mark). Does the UI work any of this out itself, and can it then disagree with the engine?
   `?line=` and `?reason=` in the route: a line past the end, a reason that doesn't apply. A file of several
   megabytes. The cursor in the blocked list marking its line; Ctrl+F; copy.
4. **The garden contradiction**: `https://www.copperfieldshop.co.uk` is `unanswered` yet has `blocked: 1` with
   reason `unknown`, in a crawl an older engine made. Find the cause if you can (can today's engine produce it?),
   and say what the screen shows for it.
5. **Routes and identity.** An origin in the path: an internationalised domain, a port, upper case, a reload, Back
   and Forward, an origin or sitemap id the crawl doesn't have, a crawl still running that hasn't found it yet.
6. **A running crawl.** Sitemaps and origins appearing and their counts moving while a crawl runs: no layout jump,
   the cursor and scroll kept, no loading frame over content, at most 4 updates a second. One sitemap's own grid
   updating live was never verified by the build: verify it against a real crawl of the test site.
7. **What the slice changed outside its own folder.** `GridCore` (a grid can open by cell; a tree's groups open as
   rows): is the Issues treegrid's, the URL grid's and the inspector lists' behaviour unchanged? The source viewer
   split into `SourcePane` with plain text and marked lines: is the inspector's Source section unchanged (its
   search, its keys, a large document)? `e2e/support.ts`, the mock transport and the new fixture extract step,
   `errors.ts`, the registry and palette (shortcut clashes; a command shown where it can't run), the
   `sitemaps-not-read` alert's new target on Overview.
8. **Accessibility and the keyboard**: treegrid semantics and names; the file viewer for a screen reader (the
   obeyed group and the blocking lines told by more than colour); focus after each action; announcements (a
   changed view once, a running crawl never); forced colours; reduced motion; 200% zoom; 1280x720; six-digit
   counts, very long sitemap URLs and rule lines.
9. **Performance**: first page 300 ms and click or key to a visible response 100 ms, on the 100k crawl; the number
   of requests per view (duplicates, one request per row).
10. **Design-system and slice rules**: tokens only, components extended in `app/src/ui/` rather than restyled
    locally, words from the API and glossary only, UK English, no raw engine ids or error codes shown, every state
    in Storybook, and tests that actually test (nothing weakened elsewhere to get green).

Verify each finding before reporting it: read the code path and, where you can, prove it (a failing test,
Playwright against `npm run dev` or Storybook, axe, a timing). Mark each CONFIRMED (shown) or PLAUSIBLE (reasoned).
No style preferences, and no speculation reported as a defect.

## Report back

A ranked list, most severe first, at most 30: severity (high / medium / low), file:line, one-sentence defect, the
failure scenario, CONFIRMED or PLAUSIBLE, and a one-line fix. Then your verdict on "Crawled", and a short "looks
right" note on what you checked and found sound. **Write the full list to `S2\sitemaps-review\findings.md`** (a
fixer works from it) with your proof tests and scripts beside it, named in the report, and give the same list in
your final message. Keep a `checkpoint.md` there as you go. No file dumps. When done, stop your processes and
remove your worktree (`git worktree remove --force` on your own folder only).
