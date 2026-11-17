# Lane "views": the screen and settings fixes from the owner's UX audit (step 3b, item 3)

Read `lanes-common.md` in this folder first. It covers the base, the HOLD on builds, the rules, the verify and the report.

- Worktree: `C:/Projects/ShoutingIguana/.claude/worktrees/3b-views`
- Branch: `ui/3b-views`
- Ports: verify-app base **51100**, Storybook **51106**
- Logs and checkpoint: `scratchpad/views/`

## Why
The owner (2026-11-17): "some screens don't show the correct information for the user to be able to make actionable
decisions … For every single view / UI it must be actually useful, and the UI&UX must be world class, so it's easy to
scan, understand and action and the user fix their site."

The audit is `docs/handoff/2026-11-17/ux-views.md`. Read section 1 and its ranked "top 12" first, then the view
sections for the screens below.

Some of the 12 are already built or belong to other lanes:
- X2, X3, item 4 and X4 landed with the evidence work. Check, don't redo.
- X1 and X7 are the fix loop's lane.
- X8 was refused by the owner (no manual "mark fixed").

## Build (these are yours)
1. **Issues: the check list sorted by what matters.** Sort options: Fix first (default), count, points, severity.
   Remember the choice per mode. On a check's page, move the affected list above Why / How (top 12, item 6).
2. **Related checks.** On a check, say "Fixing this also clears …", for example a redirect chain that also clears
   its "links to redirects". Nest the related checks in Simple's What to fix (item 7, X5).
   - Where the relation comes from: decide with the engine's catalog. Prefer one declared relation per check in
     `IssueCatalog` (data, with a test that every relation names a real id), not app-side guesses.
   - The count it clears is computed, not claimed: only the pages the two checks share.
3. **Headline counts as things to fix** (X6, item 12):
   - lead with causes, the separate things to change, and how many are in the template;
   - make Simple's Fix-first denominators match.
4. **Links:**
   - group by target: one row per target, with its link count, the pages linking to it and its status, expandable to
     the links;
   - an **Images** preset: the image, its size in bytes and pixel size, its alt text, and the pages it's used on (top
     12, item 10).
5. **Inspector: the page's images with their sizes.** An Images tab or section: thumbnail if cheap, else the address,
   plus bytes, pixel size, alt, and a flag when it's a large image. Clicking one opens it in Links or URLs.
6. **robots.txt: the blocked URLs with "Linked from" (count, clickable to the pages) and "In a sitemap"**, plus a
   conflict flag when a blocked URL is in a sitemap or linked from the menu (item 11).
7. **Settings fixes** (the status doc's "Settings against the old app"):
   - **Consent before saving a secret** (the proxy password, a header secret) to Windows Credential Manager. Nothing
     is stored unless the owner ticks a clear "Remember in Windows Credential Manager" option, which is off until they
     choose it.
     - Without it, the secret lives for this session or crawl only, and the words say so.
     - Places: `client.ts:179-182`, `:238`; `secrets.ts:152-155`.
   - **Truthful words:** fix the "aren't kept" text (`words.ts:62`, the toast in `NewCrawlScreen.tsx:231`) so it says
     what happens in each case.
   - **SOCKS5 with a login, tested:** an engine or sidecar test against an in-process SOCKS5 server that requires
     username and password auth. If .NET can't do SOCKS5 auth, say so in the words and the doc, and don't fake it.

For each item:
- engine and sidecar where the data needs it, with the API doc updated;
- app types, words (Advanced and Simple's plain words), unit tests and stories, plus an e2e case for every new view
  state;
- a screenshot of each changed view in both themes, saved under `scratchpad/views/shots/` for the orchestrator to
  review.

Mind the large real-world shapes. Garden furniture has 2,000 pages and image-heavy templates:
- group and paginate, never materialise everything;
- measure every new sidecar query on the 100k bench crawl if it reads links or URLs. readbench has a pattern for this:
  add a class if needed.

## Out of scope
- the fix loop (compare, recheck);
- new checks;
- pixel widths (done);
- the release.
