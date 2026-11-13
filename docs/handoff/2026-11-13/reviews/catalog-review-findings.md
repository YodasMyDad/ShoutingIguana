# Review of `ui/catalog-fixes` at `5f5b83d`: findings

Saved by the orchestrator from the reviewer's final message, with the orchestrator's decisions. Paths are under
`engine/src/ShoutingIguana.Engine/` unless they start with `docs/` or `app/`. The reviewer's probes are in this
folder: `ReviewProbes.cs`, `ReviewProbes2.cs`, `probes-out.txt`.

1. **Medium, CONFIRMED. The three `robots-txt-*` checks read as passed on a crawl that ignored robots.txt.**
   - Where: `Analysis/CrawlAnalyzer.cs:158-165` skips only `blocked-by-robots`; `Analysis/Rules/RobotsTxtRules.cs:26-54`
     works from whatever rows exist; `Crawling/PageProcessor.cs:71` is the only per-URL read of robots.txt and is
     off when ignoring.
   - Scenario: `--ignore-robots --no-sitemaps` with robots.txt answering 500 on the start origin. robots.txt was
     requested 0 times, and `robots-txt-unreachable` (Error, site-wide) ran with 0 rows.
   - What each reads as:

     | Crawl | `robots-txt-missing` | `robots-txt-unreachable` | `...-other-origin` |
     | --- | --- | --- | --- |
     | Obeying robots.txt | Lists both origins | Checked | Checked |
     | Ignoring, sitemaps on | Lists the start origin only | Checked for the start origin only | Never looked at; reads as passed |
     | Ignoring, sitemaps off | Never looked at; reads as passed | Never looked at; reads as passed (also with a 500) | Never looked at; reads as passed |

     With sitemaps on, the start origin's robots.txt is still read, because sitemap discovery asks for it
     (`Sitemaps/SitemapDiscoverer.cs:51`).
   - Fix: when `!options.RespectRobotsTxt`, skip `robots-txt-unreachable-other-origin` always, and the other two
     when no robots row exists for a main origin, with reason `robots-txt-ignored`. Its plain words name blocked
     addresses only and need widening.

2. **Low, CONFIRMED. `canonical-to-redirect` says "ends at" for a URL the crawl never fetched.**
   `Analysis/Rules/CanonicalRules.cs:99-104` (`EndsAt` doesn't look at `Fetched`); the fix text at
   `Analysis/IssueCatalog.cs:360` says to use "the redirect's final URL, which the detail gives". Scenario:
   canonical -> `/first` (301) -> `/second` (301) -> `https://partner.example/landing`, not fetched (external links
   off, URL limit, or stopped crawl): the detail reads "...and, after 2 redirects, ends at
   https://partner.example/landing", though that URL may redirect again. Fix: when `!urls[trace.FinalId].Fetched`,
   say "leads to X, which wasn't checked", and make the fix text true of that case.

3. **Low, CONFIRMED. `charset-missing` passes an empty `charset=`, and its rewritten text now claims otherwise.**
   `Analysis/Rules/HtmlRules.cs:25-26` tests `Contains("charset=")`. Scenario: `Content-Type: text/html; charset=`
   with no meta tag and no mark is not flagged. Fix: use `ContentTypeHeader.CharsetOf(header) is not null`.

4. **Low, CONFIRMED. A crawl started in a section is called a site hidden from search.**
   `Analysis/Rules/SiteVisibilityRules.cs:65`; plain words in `plain-words.json:403`. Scenario: start URL `/home`
   (noindex) with three noindex pages under it fired "4 of 5 pages are noindex" and scored 0. A crawl of a
   deliberately noindexed area such as `/account/` would do the same.
   - ORCHESTRATOR'S DECISION (under the owner's delegation; the owner can overturn it): a crawl that did not start
     at its site's root can't say whether the site is hidden. When the start URL as the user gave it has a path
     other than `/` (a query alone doesn't count as a section; say what you do with one), the check is SKIPPED with
     a reason (an existing reason if one truly fits, else a new one with technical and plain texts), not passed and
     not fired. A start URL of `/` that redirects to `/en/` or to the www host is still the root: the home page is
     the page it ends on, as now. The test `TheHomePageIsThePageTheStartUrlEndsOn` changes to match.

5. **Low, CONFIRMED. The final-URL lookup compiles a new SQL statement per flagged page.**
   `Storage/AnalysisReader.cs:213`, called from `Analysis/Rules/CanonicalRules.cs:99`. About +1 s per 100k flagged
   pages. Fix: one prepared command with an `$id` parameter.

6. **Info. Docs.** In `docs/v2-sidecar-api.md`, the history sections at lines 2254 and 2269 still say "start URL
   when no home page was read" and "15 skip reasons". (`docs/v2-rebuild.md` is the orchestrator's.)

7. **Info, CONFIRMED. The inspector shows no "Character set" row for a page that passes on its mark.**
   `Storage/UrlInspectorReader.cs:643` reads `DeclaredCharset` only. Fix: fall back to `ByteOrderMark` there (and
   say in the API doc what the field then holds; check what the app's inspector prints for it).

8. **Info, CONFIRMED. Two design mock pages still say "Orphan page".**
   `app/design/simple/advanced-overview.html:106` and `app/design/simple/build/pages/screens.mjs:217`.

Also noted, not a defect of the branch: UTF-32 LE reads as `utf-16le` (it is declared either way: leave it, or name
it rightly if that is one line).
