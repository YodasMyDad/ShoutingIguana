# Lane recheckfix: what the real-site run found in recheck and the polite crawl (2026-11-18)

## Setup
Read `C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/a539aa23-e856-44c1-bd21-ca484e8740e2/scratchpad/audit3b/lanes-common.md`
and follow it, with these exceptions:
- There is no GO-BUILD hold.
- Base `ui/step-3` at 8cbd8c24:
  `git -C C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3 worktree add -b ui/3b-recheck-fixes C:/Projects/ShoutingIguana/.claude/worktrees/3b-recheckfix 8cbd8c24`.
- Ports: 51230 for the app and 51236 for Storybook.
- Notes go in `scratchpad/recheckfix/`, with a `checkpoint.md`.
- `AnalysisFreshness.VERSION` is 14. If stored analysis output changes, set 15, with a test that 14 is stale.
- An app-only lane, `ui/3b-classes`, is editing these files. Don't touch them:
  - `app/src/screens/rechecks/RecheckControl.tsx`;
  - `app/src/screens/issues/CheckDetail.tsx`;
  - `app/src/styles/tokens.css`;
  - the files that lane's brief names.
  If you need one of them, say so in your report instead.
- Never crawl or fetch a real site. Use the synthetic test site and test servers.

## The evidence
Read `C:/Users/LeeMessenger/AppData/Local/Temp/claude/e2e5beaddde4c070/a539aa23-e856-44c1-bd21-ca484e8740e2/scratchpad/realsites/ready.md`
first, the "Bugs" section above all. It has the `file:line` references and the error messages.
- The damaged real file is `realsites/garden/www.gardenfurnituresets.co.uk.sicrawl`, and `before-recheck.sicrawl` is
  its snapshot.
- Read them only through copies, for diagnosis.
- Never commit real-site data. The tests use synthetic files.

## Fix, each with a test seen failing first
1. **B1, schema upgrade on every path that writes.** A recheck on a file made before `sitemap_urls.lastmod` fails.
   - Don't add one more column list to `CrawlWriter.Recheck.cs`. Make one shared upgrade that brings any older file up
     to the current schema: every table and column added since the first v2 file. Resume, recheck and anything else
     that opens an existing file for writing must call it.
   - The test builds an older file synthetically: the current schema minus each column added since, or the real
     earlier DDL if the repo keeps it. It runs every recheck kind (urls, robotsTxt, sitemaps, siteVersions) and a
     resume against that file.
   - Also add a guard: a test that fails when a column is added to the schema without the upgrade knowing it.
2. **B2, a recheck is all or nothing.** A failure at any point leaves the crawl file as it was before. No analysis runs
   and nothing is stored after a failed recheck; the run ends "failed" with its reason.
   - Prove it with injected failures at each stage: after the robots rewrite, after the sitemap delete, mid-fetch, and
     mid-store. Row counts and the stored analysis must be unchanged, and `si compare` must show nothing changed.
   - If one transaction can't span the fetches, stage the writes and apply them in one transaction at the end. Keep
     the engine rule: only `Storage/` writes.
3. **B3, honest recheck summaries, in the CLI and wherever the sidecar and app show them.**
   - Say how many URLs were asked for, how many were read, how many were refused again (and why), and how many were
     never asked.
   - A URL-list recheck that has no check still settles each URL: read, refused again, or not asked.
   - Count requests correctly: robots.txt and sitemap fetches count; URLs never asked don't.
   - A recheck whose output is redirected still prints its progress lines.
   - Check what the app shows for a recheck that was refused again (Simple's recheck card, Advanced's banner).
     "Refused again" must be visible in the user's words. Change app words only in files the classes lane doesn't own.
4. **B4.** The 429 warning describes what actually happened, page by page. Pages that were never asked say so. Don't
   claim "three times" for pages that weren't tried three times.
5. **B5.** The next try never lands past the cap. Clamp the last try to the cap, so one more try happens at the end and
   the cap's last minutes aren't idle. The progress words must never promise a try that won't happen.
6. **B6, Microdata `content`.** An `itemprop` element with a `content` attribute takes its value from that attribute,
   whatever the element: schema.org's own examples put it on a `<span>`. Keep the existing rules for `meta`, `link`,
   `a`, `img`, `time` and so on, in the order the Microdata spec and schema.org give them.
   - This is a parser change, so `PageFacts` and `HtmlPageParser` own it.
   - Plant a case on the synthetic site, and keep the whole issues table's expected counts right.
   - Bump `AnalysisFreshness` if analysis output can change.
7. **B7.** One note per reason, not one per fact collection: "N checks weren't run: this crawl was made by an earlier
   version…", with N totalled.
8. **Cookies on a recheck.** A refetch starts with an empty cookie jar, so a site can send a fresh session cookie, and
   `cookies-insecure` then counts it as new (2 → 3 on the garden site).
   - The rule: a recheck never calls a cookie problem "new" when the crawl already flagged the same cookie name on
     another page. It never calls one "fixed" just because a session cookie wasn't sent again.
   - Choose the cleanest way to get there: compare cookie names site-wide, or send the cookies the crawl had been
     given if the file keeps them, or make that check's recheck "check the site again". Explain the choice.

## Verify
- Engine:
  - Release build and test;
  - `verify-engine.sh`: the bench must find every planted page and issue, flag nothing else, and keep memory flat;
  - readbench under load: note any misses, don't chase them.
- App: typecheck, lint, format and Vitest, if you touched the app. Also the e2e fix-loop specs:
  `npm run build:sidecar` first, and delete `app/.engine-build/crawls/e2e-synthetic-full.sicrawl` before you run them.
- **Last, a real-shaped check without a real site.** Copy `realsites/garden/before-recheck.sicrawl` to your
  scratchpad. Rerun recheck c (`--check robots-txt-no-sitemap`) against a local test server that serves that site's
  robots.txt and sitemap content from the copy's stored data. Do this only if it's cheap. Otherwise, the synthetic
  older-schema tests cover it.

## Ready note
Write `scratchpad/recheckfix/ready.md`: the commit to merge, each fix with its test, the counts, the verify lines and
anything left. Then report in under 450 words.
