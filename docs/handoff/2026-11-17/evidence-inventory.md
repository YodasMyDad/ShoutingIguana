# Evidence inventory: can each check be fixed from what it shows?

Repo: `C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3` (branch `ui/step-3`, at aa2802b). Read-only audit, 2026-11-17.
All 87 checks in `engine/src/ShoutingIguana.Engine/Analysis/IssueCatalog.cs` (`All`, lines 1045-1064).

The bar: from the check's own views (Advanced Issues, Simple's problem page), a site owner can say (a) WHAT is wrong
(actual value vs limit), (b) WHERE to fix it (page; every page a resource is on; linking page + anchor + position;
template vs one-off), (c) HOW, with the catalog's fix matching what is shown. The inspector and the source viewer count
as "other tools" here: the owner's own example (image-too-large: size is in the image's inspector, two clicks away) is
judged failing.

Severity:
- **Blocks fixing**: the thing to change (which image, which #fragment, which of two canonicals/titles, which element)
  or the value the fix is computed from (image size for "Large images") is not in the check's views.
- **Slows fixing**: the thing is identified, but evidence needed to decide or do the fix (the text being judged, the
  final URL to swap in, where a directive comes from, the hops) is missing, or shown in one mode only.
- **Nice to have**: polish.
- **None**: the views answer (a)(b)(c).

Abbreviations. Subject: P = page, U = URL. Grouping: N = none, PD = page detail, RD = response detail, RU = related URL.
References: – = none, S = sources (found-on), T = targets (what the page names).

---

## 1. What the views show today (rendering patterns, cited once)

**Engine: what an issue carries.** `IssueHit(Issue, UrlId, Detail, RelatedUrlId){Targets}`
(`Analysis/Models/IssueHit.cs`). Detail is one short string per page, written by the rule; its formats are quoted in
the tables. Shared formatters: `Analysis/Rules/RuleText.cs` (`Count` "3 images", `Width` "78 characters", `Status`,
`BrokenStatus`, `BrokenTargets` "2 broken URLs (the first redirects to a URL that returns 404 Not Found)", `Size`).

**Engine: what a target carries.** `IssueTarget(Id, Url, StatusCode, Fetched, Outcome, Kind, Text, Position)`
(`Storage/Models/IssueTarget.cs`), read by `FoundOnReader.Targets` / `WithLink` (`Storage/FoundOnReader.cs:157-219`):
only `kind, text, position` of the page's FIRST link to the target. No size, content type, fragment, flags
(nofollow / image-link / from-HTTP-header), indexability, redirect final URL or canonical target.

**Engine: what a source carries.** `UrlFoundOn` (`Storage/FoundOnReader.cs:96-113`): links count, pages count, first N
`LinkRow`s (linking page URL + status, kind, anchor/alt text, position, flags, followed, fragment), sitemaps, start URL.

**Engine: groups.** `Storage/IssueGroupReader.cs:96-107`: RU checks with `issue_targets` group by EVERY URL a page
names (a page with 3 broken links is in 3 groups); PD with `FaultOf` by fault; else by detail + related URL.
Template flag `IsLikelyTemplate` (`IssueGroupReader.cs:189-190`): >= 10 URLs and >= half the check, and only for RU
(shared URL) or PD groups. Grouping-N checks never get a template signal.

**Advanced, flat (grouping N)** - `app/src/screens/issues/IssueRowsGrid.tsx:70` asks `/rows` for `['url','detail']`
only; columns `:89-127`: URL | Detail | (S or T only) a one-line "Found on 3 pages · /x +2" / "Links to /x (404) +2"
(`WhereFoundLine.tsx`, words `whereFound.ts:41-69`). No expansion; right-click offers Links (S only).
-> Pattern **A** (refs none), **A-S** (refs sources).

**Advanced, grouped** - group rows `IssueGroups.tsx:461-538` (Related URL column for RU, else Detail [+ Related URL])
+ count + share bar + "One fix" callout when likely template (`TemplateCallout`, `IssueGroups.tsx:829-867`, place from
`templateLinkWords`, `whereFound.ts:210-223`). Opening a group reads `/rows` with `['url','detail','relatedUrl']`
(`app/src/api/queries.ts:313-323`); page row `IssueGroups.tsx:545-577` = URL + one-line where (S/T) + the row's detail
when it differs from the group's. Expanding a page (S/T only), `WhereCell` `IssueGroups.tsx:725-790`:
- S: "Linked from" page URL + `linkPlaceWords` (kind in position · "anchor/alt", `whereFound.ts:87-97`) + followed;
  "Listed in the sitemap" URL; "The URL the crawl started from"; "Show all N links in Links".
- T: "Names" target URL + status code + `linkPlaceWords`; first 5, then "All N in the inspector".
-> Pattern **B** (PD/RD, refs none), **C** (RD/PD/RU, sources), **D** (RU, targets), **E** (RU, refs none = duplicate
clusters: group = the cluster's first page).

**Simple, problem page** - `app/src/simple/ProblemScreen.tsx`: plain what/why (`plainWhat`, `:290`), numbered fix steps
(`plainFix`), fixer, One-fix callout (likely-template group, or `useSharedSource` = one page linking to most of the
first 25 addresses, S only). "Where it is" `app/src/simple/WhereItIs.tsx`, reads `/rows` with
`['url','title','detail','relatedUrl','relatedStatusCode']` (`:26`):
- grouping N: rows = page title + address (`PageLine`, `:60`) + `FoundOnWords` (S only) | detail (`:180`).
- grouped (`GroupRow`, `:220-315`): left = related URL (+ status only when >= 400, `:252`) or, without one, the detail;
  right = pages by title + address, + `FoundOnWords` for S checks (`:242`). **The row detail is never shown for a
  grouped check**, and for T checks nothing about the targets past the group's own URL is shown (no anchor, position,
  status < 400, size, other targets).
- `FoundOnWords` (`app/src/simple/FoundOnWords.tsx`, words `whereFound.ts:161-178`): "Linked from /a, /b and 3 more" -
  no anchor text, no position, no sitemap name when pages also link to it.

**Exports.** Workbook / per-check sheet: Address | Page title | What was found | Other address
(`Export/WorkbookExport.cs:26-28`) + `Export/WhereFoundColumns.cs`: S = pages count, "Linked from" (page (kind,
position, "anchor")) x10, sitemaps; T = count, "Links to" = URL (status) x10, headed "Links to" even for images,
canonicals, hreflang, mixed content; no anchor/position/size. HTML report (`Reporting/ReportDocument.cs:625-700`): Page |
What was found (detail -> first related URL "and N others", `Found` `:685`) | Where to fix it (S only: linking page URLs,
no anchor/position, `WhereFound` `:707`).

**What the engine already stores (beyond what's shown)**
- Per URL `responses` (`Storage/CrawlSchema.cs:60-90`): status, content_type, **size_bytes** (Content-Length or counted
  for chunked images, `Fetching/HttpFetcher.cs:84-110`, `Crawling/PageProcessor.cs:185`), body_bytes, ttfb/response ms,
  redirect_url_id, block_reason, error, title (first), meta_description (first), h1 (first) + counts, canonical_url_id,
  noindex/nofollow, indexability(+reason), word_count, simhash (+ lanes 1-3), language.
- Per page `response_details` blob (`CrawlSchema.cs:94`, `Storage/Models/ResponseDetails.cs`): all headers + `PageFacts`
  (`Parsing/Models/PageFacts.cs`): ALL titles, ALL descriptions, meta tags, ALL canonicals (href, url, inHead,
  fromHttpHeader), ALL hreflang (code, href, url, inHead, fromHeader), headings (level + text), **images**
  (`ImageFact(Url, Alt, HasAlt, Width, Height, HasSrcset, Loading)`, `Parsing/PageWalker.cs:550-557`), JSON-LD blocks,
  anchor ids, meta refresh, robots directives, charset, BOM.
- `response_html` (`CrawlSchema.cs:95`): raw HTML when kept.
- `links` (`CrawlSchema.cs:98-105`): from, to, kind, **flags** (nofollow, image-link, outside-head, from-HTTP-header),
  **position** (content/nav/header/footer/sidebar/head), **text** (anchor / alt / hreflang code), **fragment**.
- robots.txt files + the matching rule per blocked URL (`Storage/RobotsReader.cs:332-338`, Robots screen).
- sitemaps with parent index and error (`CrawlSchema.cs:119-133`).
- Already on `/rows` as URL columns but not asked for by Issues: `title`, `titleLength`, `description`,
  `descriptionLength`, `h1`, `h1Count`, `wordCount`, `sizeBytes`, `contentType`, `metaRobots`, `xRobotsTag`,
  `canonicalUrl`, `redirectTarget`, `finalUrl`, `indexability`, `relatedStatusCode` (`Storage/UrlColumns.cs:67-190`).
- **Not collected at all**: image intrinsic pixel dimensions (the fetcher reads no image body); which element closed
  `<head>` early; a breakdown of HTML size (inline script/style/base64), except by re-reading stored HTML.

---

## 2. Per-check inventory

Columns: **Shows today** (Adv = Advanced Issues; Simple = problem page) | **Owner needs (a)(b)(c)** | **Gap** |
**Stored?** | **Severity**.

### Response codes

| id | S/G/R | Shows today | Owner needs | Gap | Stored? | Severity |
|---|---|---|---|---|---|---|
| internal-client-error | U/RD/S | Pattern C. Group "404 Not Found" -> addresses + "Found on 3 pages · /x +2" -> each linking page, position · "anchor", followed; sitemaps. Simple: cause "404 Not Found" -> addresses + "Linked from /a, /b and 3 more". | Status; every linking page with anchor + position; sitemap entries; template link (footer). | Simple (and HTML report) drop anchor text + position, so the link can't be found on a long page; Advanced's expansion describes only the first link per linking page. Advanced has no template callout for RD groups (Simple's shared-source callout only). | Yes: `links.text/position` (CrawlSchema.cs:98-105), already in `LinkRow`. | Slows |
| internal-server-error | U/RD/S | As above, "500 Internal Server Error". | Status; when/how often; linking pages (secondary: fix is server-side). | Simple lacks anchor/position; the stored error/response time isn't shown. | Yes (`responses.error`, `response_ms`). | Nice to have |
| internal-unreachable | U/RD/S | As above, detail "Timed out" / "Connection failed". | Which failure (DNS, reset, TLS, timeout) and how long it waited. | Generic detail; stored error text not shown. | Yes (`responses.error`, `response_ms`). | Nice to have |
| internal-blocked | U/RD/S | As above, detail names the protection (Cloudflare...) or "429 Too Many Requests". | Protection name/status. | - | - | None |
| soft-404 | P/N/– | Pattern A: URL \| "The title or H1 says the page wasn't found". Simple: page title + address \| same detail. | Which text matched (title or H1, quoted), word count vs 300. | Advanced shows neither the title/H1 nor the word count; Simple shows the title (as the page's name) but not the H1 or words. | Yes (`title`, `h1`, `wordCount` columns, UrlColumns.cs:73,150,155). | Slows |

### Redirects

| id | S/G/R | Shows today | Owner needs | Gap | Stored? | Severity |
|---|---|---|---|---|---|---|
| redirect-chain | U/RU(final)/S | Group = final URL -> redirecting addresses + found-on + "3 redirects" -> linking pages w/ anchor. Simple: cause = final URL -> addresses + "Linked from..."; hop count hidden. | Every hop (URL + status) so each rule can point straight at the final URL; the final URL; links + sitemap entries to update. | Intermediate hops are not shown in either mode (inspector only). Simple hides "3 redirects". | Yes (`responses.redirect_url_id`, CrawlSchema.cs:71; `RedirectChain` model used by the inspector). | Slows |
| redirect-loop | U/N/S | Pattern A-S: URL \| "3 hops" \| "Found on ..." (no expansion). Simple: address + "Linked from" \| "3 hops". | The loop's URLs and status codes in order (which rule sends it back, e.g. slash added/removed). | The loop itself isn't shown: catalog fix says "follow the hops", the view gives only a count. | Yes (redirect_url_id per hop). | Blocks fixing |
| redirect-broken | U/RD/S | Group "Ends at a URL that returns 404 Not Found" + final URL (or "302 Found without a usable Location header"). Simple: cause = final URL + status (>= 400). | Final URL + its status, linking pages. | Intermediate hops not shown; Simple drops the detail wording. | Yes. | Nice to have |
| redirect-temporary | U/RD/S | Group "302 Found" + target URL -> addresses + found-on. Simple: cause = target URL only (the 302/307 isn't shown: detail hidden, target's 200 hidden). | Code (302/303/307) + target. | Simple doesn't say which code. | Yes. | Nice to have |
| redirect-downgrade | U/RU/S | Group = the http:// target -> addresses + found-on. Simple: cause = http target. | The http target (https twin implied); linking pages. | - | - | None |
| meta-refresh | P/PD/– | Group "Moves to https://x/ after 5 seconds" / "Reloads the page after 30 seconds" -> pages. Simple: same as cause. | Target + delay. | - | - | None |

### Links

| id | S/G/R | Shows today | Owner needs | Gap | Stored? | Severity |
|---|---|---|---|---|---|---|
| broken-internal-links | P/RU/T | Pattern D. Group per broken URL (+ "One fix" callout naming "a link in the footer") -> pages + "Links to /old/ (404) +2" + "2 broken URLs (the first redirects to ...)" -> expand: each target + status + position · "anchor". Simple: cause = broken URL + status (only if >= 400) -> pages by title. | Each broken target with why (incl. at the end of redirects), linking page, anchor text + position, template vs one-off. | Simple has no anchor or position, and for a target broken only after redirects shows no status at all (301 < 400, detail hidden). Per target, "where it goes wrong" exists only for the first (in the detail). Only the page's first link to a target is described (nav + content duplicates collapse). Workbook "Links to" has no anchor/position. | Yes (links.text/position/flags; redirect trace). | Slows |
| broken-external-links | P/RU/T | Same as above. | Same. | Same. | Yes. | Slows |
| links-to-redirects | P/RU/T | Group per redirecting URL -> pages + "Links to /old (301) +2" -> target + 301 + position · "anchor". Simple: cause = redirecting address. | The FINAL URL to link to instead (that's the fix), plus anchor/position. | Final URL not shown in either mode; Simple has no anchor/position. Plain fix says "the list shows the address it points at" (true) but the fix needs where it should point. | Yes (redirect chain; `finalUrl` exists as a URL column, UrlColumns.cs:173, not for targets). | Slows |
| internal-nofollow-links | P/RU/T | Group per target -> pages -> target + status + position · "anchor". Simple: cause = target address. | Target, anchor, position. | Simple lacks anchor/position. | Yes. | Nice to have |
| empty-anchor-text | P/RU/T | Group per target -> pages + "Links to N URLs" -> target + status + position (text is empty by definition). Simple: cause = target address only. | What the empty link IS: an image link whose img has no alt (which image), an icon/SVG link, its position (logo in header = one template fix). | Image-link flag not surfaced (target carries no flags); the image inside the link isn't recorded, only its (empty) alt; Simple shows no position at all. | Partly: `links.flags` ImageLink + position yes (CrawlSchema.cs:102-103); the wrapped image's src: no (new collection in PageWalker). | Slows |
| broken-fragment-links | P/RU/T | Group per TARGET PAGE -> pages + "N links" -> target page URL + 200 + position · "anchor". Links to a missing section of the page itself name no URL (`NO_OTHER_URL`) so they're only a count. Simple: cause = target page address. | The `#fragment` that's missing, the anchor text/position of the link, the ids that do exist on the target (near-miss). | The fragment is never shown (target carries none), so the owner can't tell which link on the page is wrong; same-page fragment links are invisible. | Yes: `links.fragment` (CrawlSchema.cs:105), already read into `LinkRow.Fragment` (UrlInspectorReader.cs:221) but not into `IssueTarget`; target ids in `PageFacts.AnchorIds`. | Blocks fixing |
| orphan-page | P/N/S | Pattern A-S: URL \| "Only found in a sitemap" \| "In a sitemap · /sitemap.xml". Simple: title + address + "Listed in your sitemap" \| detail. | Which sitemap; (optional) candidate pages to link from. | Simple doesn't name the sitemap (minor). | Yes. | None |

### Indexability

| id | S/G/R | Shows today | Owner needs | Gap | Stored? | Severity |
|---|---|---|---|---|---|---|
| noindex-page | P/N/– | Pattern A: URL \| "–" (no detail). Simple: title + address, blank detail. | Where the noindex comes from: robots meta tag (and its exact content) or X-Robots-Tag header (server/CDN) - the catalog fix says "remove the noindex from the meta tag or header". | Source not shown; the row is just a URL. | Yes (`metaRobots`, `xRobotsTag` columns, UrlColumns.cs:140-141). | Slows |
| nofollow-page | P/N/– | Same as noindex-page. | Same (meta vs header). | Same. | Yes. | Slows |
| blocked-by-robots | U/RD/S | Group (detail null, or "robots.txt couldn't be read") -> addresses + found-on -> linking pages. Simple: addresses + "Linked from". | The Disallow line (line no. + text) that matches, so it can be changed. | Matching rule not shown here (the Robots screen has it). | Yes: `RobotsReader.cs:338` (`RobotsBlockedUrl.Line/Rule`). | Slows |
| snippet-blocked | P/PD/– | Group "nosnippet" / "max-snippet:0" -> pages. Simple: same. | Directive + whether it's a meta tag or header. | Source (meta vs header) not shown. | Yes. | Nice to have |
| robots-txt-unreachable | U/N/– (once) | Home page \| "https://x/robots.txt (503 Service Unavailable)". | Origin + status. | - | - | None |
| robots-txt-unreachable-other-origin | U/N/– (once) | Home page \| list of origins + status. | Same. | - | - | None |
| robots-txt-missing | U/N/– (once) | Home page \| list of origins + status. | Same. | - | - | None |
| site-hidden-from-search | P/N/– (once) | Home page \| "212 of 240 pages are noindex". | Counts + where the home page's noindex comes from (meta/header/CMS setting). | Source of the noindex not shown (noindex-page lists the pages). | Yes. | Nice to have |

### Canonicals

| id | S/G/R | Shows today | Owner needs | Gap | Stored? | Severity |
|---|---|---|---|---|---|---|
| canonicalised-page | P/RU/T | Group per canonical target (+ callout "It's their canonical, in each page's head") -> pages -> target + status. Simple: cause = target address. | Target + its status; HTML vs Link header. | HTML-vs-header not surfaced (flag stored). | Yes (`links.flags` FromHttpHeader). | Nice to have |
| canonical-missing | P/N/– | URL only. | Page. | - | - | None |
| canonical-multiple | P/N/– | Pattern A: URL \| "2 different URLs". Simple: same. | Each canonical URL and where it's declared (head tag vs HTTP Link header) - the fix is "turn off the wrong one". | Neither the URLs nor their sources are shown anywhere in the check. | Yes: `PageFacts.Canonicals` (Href, Url, InHead, FromHttpHeader) in `response_details`; also `links` kind Canonical. | Blocks fixing |
| canonical-broken | P/RU/T | Group per canonical target -> pages + "The canonical URL returns 404 Not Found" -> target + status. Simple: cause = target + status if >= 400. | Target + why broken. | Simple: a canonical broken only after redirects shows no reason (301 hidden, detail hidden). | Yes. | Nice to have |
| canonical-to-redirect | P/RU/T | Adv page row: "The canonical URL redirects (301) to https://x/final" (the URL to use). Simple: cause = the canonical's address only. | The final URL to put in the canonical. | Simple hides the detail, so it never shows the URL the fix says to use (its plain fix says "the list names the address"). | Yes (in the detail). | Slows |
| canonical-to-non-indexable | P/RU/T | Adv page row: "The canonical URL is noindex / is canonicalised to another URL / is blocked by robots.txt". Simple: cause = address only. | Target + why it isn't indexable + (for a chain) where it points next. | Simple hides the why (plain fix: "and why it can't appear in search"); the next hop of a canonical chain isn't shown in either. | Yes (detail; `canonical_url_id`, CrawlSchema.cs:81). | Slows |
| canonical-outside-head | P/N/– | URL only, no detail. | The canonical tag, and the element that ended `<head>` early (catalog fix: "remove the element that ends the head early"). | Nothing shown; the culprit element isn't collected. | Canonical: yes (`PageFacts.Canonicals.InHead`). Culprit: no - new PageWalker fact (first body-forcing element + source line). | Blocks fixing |
| canonical-relative | P/PD(fault)/– | Group by fault -> pages; detail = the href as written ("/page"). | The href. | - | - | None |
| canonical-invalid | P/PD(fault)/– | Detail = the href ("{{url}}", empty). | Href + whether it's the HTML or a Link header (fix differs: template vs server). | Source not shown. | Yes. | Nice to have |
| noindex-with-canonical | P/RU/T | Group per canonical target -> pages -> target + status. | Target + where the noindex comes from. | Noindex source not shown. | Yes. | Nice to have |

### Sitemaps

| id | S/G/R | Shows today | Owner needs | Gap | Stored? | Severity |
|---|---|---|---|---|---|---|
| sitemap-non-indexable | U/RD/S | Group "The URL redirects (301 Moved Permanently)" / "is noindex" / "is canonicalised to another URL" / "returns 404" -> addresses -> sitemaps (Adv expansion). Simple: cause = that wording -> addresses + "Listed in your sitemap" (no name; replaced by "Linked from" when pages also link to it). | The replacement URL the fix asks for (redirect's final URL, the canonical target), and which sitemap. | Replacement URL not shown in either mode; Simple may not mention the sitemap at all. | Yes (`redirect_url_id`, `canonical_url_id`). | Slows |
| not-in-sitemap | P/N/– | URL only. | Page. | - | - | None |
| sitemap-error | U/N/– (once) | Home page \| up to 3 "sitemap URL (error)" + "and N more". | Every failing sitemap + where it's referenced (robots.txt line / parent index). | Past 3 not listed here; the reference point isn't shown (Sitemaps screen has it). | Yes (`sitemaps.parent_id`, robots content). | Nice to have |
| sitemap-missing | U/N/– (once) | Home page, no detail. | - | - | - | None |
| sitemap-url-other-host | U/RD/S | Group "The URL redirects (301...)" / "returns 200 OK" / "wasn't checked" -> addresses -> sitemaps. | The URL on the sitemap's own host to list instead (usually where it redirects). | Redirect target not shown. | Yes (`redirect_url_id`). | Slows |

### Titles

| id | S/G/R | Shows today | Owner needs | Gap | Stored? | Severity |
|---|---|---|---|---|---|---|
| title-missing | P/N/– | URL only. Simple: address. | Page. | - | - | None |
| title-multiple | P/N/– | Pattern A: URL \| "2 titles". Simple: first title as the page's name \| "2 titles". | Every title's text (to see which system adds which). | The other titles are never shown. | Yes (`PageFacts.Titles`, all of them; inspector lists them). | Blocks fixing |
| title-too-long | P/N/– | Adv: URL \| "78 characters" (no title text, no limit). Simple: title text + address \| "78 characters"; limit in plain words. | Title text, its length, the limit. | Advanced never shows the title (grid asks only url + detail, IssueRowsGrid.tsx:70); limit only in the explanation. | Yes (`title`, `titleLength` columns). | Slows |
| title-too-short | P/N/– | Same. | Same. | Same. | Yes. | Slows |
| title-duplicate | P/RU(cluster)/– | Pattern E: group = the cluster's first page URL -> pages + "3 other pages". Simple: cause = first page's address ("What it's about"); pages listed by title, so the title shows there. | The shared title text, and its pages. | Advanced never shows the shared title; Simple shows it only as each page's name, under a cause that is a page address. | Yes (`responses.title`). | Slows |

### Meta descriptions

| id | S/G/R | Shows today | Owner needs | Gap | Stored? | Severity |
|---|---|---|---|---|---|---|
| description-missing | P/N/– | URL only. | Page. | - | - | None |
| description-multiple | P/N/– | URL \| "2 meta descriptions". | Each description's text. | Never shown. | Yes (`PageFacts.MetaDescriptions`). | Blocks fixing |
| description-too-long | P/N/– | URL \| "180 characters". Simple: page TITLE + address \| "180 characters". | Description text, length, limit. | The description itself isn't shown in either mode. | Yes (`description`, `descriptionLength` columns). | Slows |
| description-too-short | P/N/– | Same. | Same. | Same. | Yes. | Slows |
| description-duplicate | P/RU(cluster)/– | Pattern E: first page URL -> pages + "N other pages". Simple: first page's address -> pages by title. | The shared description text. | Not shown in either mode. | Yes (`responses.meta_description`). | Slows |

### Headings

| id | S/G/R | Shows today | Owner needs | Gap | Stored? | Severity |
|---|---|---|---|---|---|---|
| h1-missing | P/PD/– | Groups: none / "The H1 has no text" -> pages. | Page; whether an H1 exists but is empty. | - | - | None |
| h1-multiple | P/N/– | URL \| "3 H1s". | Each H1's text (which is the logo / sidebar title to demote). | Not shown. | Yes (`PageFacts.Headings`, level + text). | Blocks fixing |
| h1-duplicate | P/RU(cluster)/– | Pattern E. Simple shows page titles, not H1s. | The shared H1 text. | Not shown in either mode. | Yes (`responses.h1`, `h1` column). | Slows |

### Content

| id | S/G/R | Shows today | Owner needs | Gap | Stored? | Severity |
|---|---|---|---|---|---|---|
| low-word-count | P/N/– | URL \| "120 words"; Simple adds title; limit in plain words. | Count vs limit. | - | - | None |
| duplicate-content | P/RU(cluster)/– | Pattern E: cluster's first page -> pages + "2 other pages". | The twin pages (shown) + which to keep (indexability, canonical, inlinks). | Twin pages shown; decision facts not. | Yes (`indexability`, `canonicalUrl`, `inlinks` columns). | Nice to have |
| near-duplicate-content | P/RU(cluster)/– | Pattern E: lowest-id page -> pages + "2 similar pages". | Twins + how alike. | Similarity % not shown. | Yes (simhash lanes, computable). | Nice to have |
| duplicate-site-version | U/RD/– (once per version) | "Answers 200 OK instead of redirecting to https://www.x/" or "Redirects to X, which answers ...". | Version + what to redirect to. | - | - | None |

### Images

| id | S/G/R | Shows today | Owner needs | Gap | Stored? | Severity |
|---|---|---|---|---|---|---|
| image-missing-alt | P/N/– | Pattern A: URL \| "3 images". Simple: title + address \| "3 images". (Inspector: only "12, 3 without alt text".) | WHICH images (src, preview), position, and whether one image (a logo) is on every page = one template fix. | The images are never listed; no template signal (grouping N). | Yes: `PageFacts.Images` (Url, HasAlt) in `response_details` (PageWalker.cs:550-557); position via `links` kind Image. | Blocks fixing |
| image-alt-too-long | P/N/– | URL \| "2 images". | Which images, their alt text, its length vs limit. | Not listed. | Yes (`ImageFact.Alt`). | Blocks fixing |
| image-missing-dimensions | P/N/– | URL \| "5 images". | Which images and which attribute is missing (width / height / both); template vs one-off. | Not listed. | Yes (`ImageFact.Width/Height`). | Blocks fixing |
| image-broken | P/RU/T | Group per broken image (+ callout "It's an image in the header") -> pages + "Shows /img/x.jpg (404)" -> image + status + "Image in the content · alt "Sofa"". Simple: cause = image URL + status. | Image, status, pages, position. | Simple lacks position/alt. | Yes. | Nice to have |
| image-too-large | P/RU/T | Group per large image -> pages + "Shows /img/x.jpg (200) +1" + "2 images over 200 KB" (the LIMIT, not the size) -> image URL + 200 + position · alt. Simple: cause = image URL (status hidden), pages by title. Report: "2 images over 200 KB -> /img/x.jpg and 1 other". Workbook: "Links to" = URL (200). | Each image's file size in KB (sortable, worst first), format, pixel dimensions, the size it's displayed at (width/height attrs, srcset), and every page it's on (shown). | Size, format, displayed size and pixel size appear nowhere in the check; the detail repeats the threshold. | Size + format: yes (`responses.size_bytes`, `content_type`, CrawlSchema.cs:65-66). Displayed size: yes (`ImageFact.Width/Height/HasSrcset`). Pixel dimensions: no - new collection (read the image header; the fetcher reads no image body today, HttpFetcher.cs:84-110). | Blocks fixing |

### Hreflang

| id | S/G/R | Shows today | Owner needs | Gap | Stored? | Severity |
|---|---|---|---|---|---|---|
| hreflang-invalid-code | P/PD/– | Group "en_UK, english" -> pages. | Codes (+ which entry). | - | - | None |
| hreflang-missing-self | P/N/– | URL only. | The page's hreflang list, to spot the near-miss (http vs https, slash). | Entries not shown. | Yes (`PageFacts.Hreflangs`; inspector Hreflang section). | Slows |
| hreflang-missing-return | P/RU/T | Group per alternate -> pages + "2 alternates don't link back" -> alternate + status + "Hreflang in the head · "de"". Simple: cause = alternate. | Alternate + what it lists instead. | The alternate's own list isn't shown. | Yes. | Nice to have |
| hreflang-broken-target | P/RU/T | Group per target -> pages -> target + status + code. | Target, status, code. | Simple lacks the code (minor). | - | None |
| hreflang-to-non-indexable | P/RU/T | Group per target -> pages + "2 URLs" -> target + status (200 for noindex/canonicalised, 301 for redirect) + code. | WHY it's non-indexable and the URL to use instead (final / canonical). | Reason invisible when the status is 200; replacement URL never shown. | Yes (`indexability_reason`, `canonical_url_id`, `redirect_url_id`, CrawlSchema.cs:71-85). | Slows |
| hreflang-duplicate-code | P/PD/– | Group "en-gb" -> pages. | The two or more URLs each code points to. | URLs not shown. | Yes (`PageFacts.Hreflangs`). | Blocks fixing |
| hreflang-missing-x-default | P/N/– | URL only. | Page. | - | - | None |
| hreflang-outside-head | P/N/– | URL only. | Which entries are in the body, and the element that ended `<head>` early. | Nothing shown; culprit not collected. | Entries: yes (`HreflangFact.InHead`). Culprit: no (as canonical-outside-head). | Blocks fixing |

### Structured data

| id | S/G/R | Shows today | Owner needs | Gap | Stored? | Severity |
|---|---|---|---|---|---|---|
| structured-data-invalid | P/PD(fault)/– | Group by fault; detail = parser's first error with its line/byte position. | Which block (if several) and the code around the error. | Block/excerpt not shown. | Yes (`PageFacts.JsonLd`). | Nice to have |
| structured-data-missing-required | P/PD/– | Group "Product: needs name; Offer: needs price". | Type + missing properties. | - | - | None |

### Security

| id | S/G/R | Shows today | Owner needs | Gap | Stored? | Severity |
|---|---|---|---|---|---|---|
| no-https | P/N/– (once) | Home page. | - | - | - | None |
| http-page | P/N/– | URL only (references none, so no found-on). | The http:// page + the pages linking to it (catalog fix: "update the links that point at http://") + whether its https twin answers. | Linking pages not offered. | Yes (`links`). | Slows |
| mixed-content | P/RU/T | Group per http resource -> pages + "Loads http://x/a.js" -> resource + status + "Script in the head". | Resource, kind, position. | (Whether the https:// version exists: nice.) | - | None |
| security-headers-missing | P/N/– (once) | Home page \| "Missing X-Content-Type-Options, Referrer-Policy". | Which headers. | - | - | None |

### URLs

| id | S/G/R | Shows today | Owner needs | Gap | Stored? | Severity |
|---|---|---|---|---|---|---|
| url-uppercase | P/N/– | URL. | URL; if renamed, the pages linking to it. | No found-on for the links to update. | Yes. | Nice to have |
| url-underscores | P/N/– | URL. | Same. | Same. | Yes. | Nice to have |
| url-too-long | P/N/– | URL \| "132 characters". | Length vs limit. | - | - | None |

### Performance

| id | S/G/R | Shows today | Owner needs | Gap | Stored? | Severity |
|---|---|---|---|---|---|---|
| slow-response | P/N/– | URL \| "1,250 ms (first byte after 900 ms)". | Total + TTFB. | - | - | None |
| large-page | P/N/– | URL \| "1.4 MB". | Size + what makes it big (inline scripts/styles/JSON/base64). | No breakdown. | Derivable from `response_html` when kept; else new parse fact. | Nice to have |
| html-past-crawl-limit | P/N/– | URL \| "2.3 MB". | Same + whether content/links fall past 2 MB. | Same. | Same. | Nice to have |

### HTML

| id | S/G/R | Shows today | Owner needs | Gap | Stored? | Severity |
|---|---|---|---|---|---|---|
| viewport-missing | P/N/– | URL. | Page (template signal helps). | - | - | None |
| lang-missing | P/N/– | URL. | Page. | - | - | None |
| charset-missing | P/N/– | URL. | Page. | - | - | None |
| open-graph-missing | P/PD/– | Group "Missing og:image; empty og:description" -> pages. | Which tags. | - | - | None |

---

## 3. Counts

87 checks. **Blocks fixing 13 · Slows fixing 24 · Nice to have 21 · None 29.**

- Blocks (13): redirect-loop, broken-fragment-links, canonical-multiple, canonical-outside-head, title-multiple,
  description-multiple, h1-multiple, image-missing-alt, image-alt-too-long, image-missing-dimensions, image-too-large,
  hreflang-duplicate-code, hreflang-outside-head.
- Slows (24): internal-client-error, soft-404, redirect-chain, broken-internal-links, broken-external-links,
  links-to-redirects, empty-anchor-text, noindex-page, nofollow-page, blocked-by-robots, canonical-to-redirect,
  canonical-to-non-indexable, sitemap-non-indexable, sitemap-url-other-host, title-too-long, title-too-short,
  title-duplicate, description-too-long, description-too-short, description-duplicate, h1-duplicate,
  hreflang-missing-self, hreflang-to-non-indexable, http-page.
- 11 of the 13 Blocks gaps are facts the engine already stores; only the "element that closed `<head>`" (2 checks) and
  image pixel dimensions (part of image-too-large) need new collection.

---

## 4. Cross-cutting patterns (the few changes that close most gaps)

1. **Per-check evidence columns on the check's rows (biggest win, no engine work).** `/rows` already serves `title`,
   `titleLength`, `description`, `descriptionLength`, `h1`, `wordCount`, `metaRobots`, `xRobotsTag`, `canonicalUrl`,
   `finalUrl`, `indexability`, `sizeBytes`. Issues asks only for `url, detail` (`IssueRowsGrid.tsx:70`,
   `queries.ts:313-323`). Add a catalog field (e.g. `EvidenceColumns`) per check and request/show them in Advanced rows,
   group page rows, Simple "Where it is", workbook and report: title-* -> title + length + limit; description-* ->
   description + length; h1-duplicate -> h1; soft-404 -> title, h1, words; noindex/nofollow-page, site-hidden ->
   metaRobots / xRobotsTag; sitemap-non-indexable / sitemap-url-other-host -> finalUrl / canonicalUrl. Closes ~14 Slows.
   Duplicate clusters (title/description/h1-duplicate) should name the group by the shared TEXT, not the first page.

2. **Richer targets: `IssueTarget` should carry what the target is, typed by kind.** Today: URL, status, first link's
   kind/text/position (`FoundOnReader.cs:157-219`). Add: `sizeBytes` + `contentType` (images/resources), `fragment`
   (fragment links), link `flags` (image-link, nofollow, from-HTTP-header), `indexabilityReason` + `finalUrl` /
   `canonicalUrl` (redirect, canonical and hreflang targets), and every link from the page to that target (not just the
   first). Then show them per target type in `WhereCell` (`IssueGroups.tsx:725-790`), in the group row for RU checks
   (image size next to the image URL, sortable by size), in Simple's `GroupRow` cause cell, and in `WhereFoundColumns`
   (which today writes only "URL (status)" under a "Links to" header). Closes image-too-large (bar pixels),
   broken-fragment-links, links-to-redirects, hreflang-to-non-indexable, empty-anchor-text, canonical/hreflang chains.

3. **Turn page-level element checks into element lists.** Checks whose detail is just a count over things the parser
   already stored in `PageFacts` (images missing alt / long alt / missing dimensions; multiple titles, descriptions,
   H1s, canonicals; conflicting hreflang codes; hreflang/canonical outside head) should list the elements: a "page
   elements" read (new FoundOnReader-style reader over `response_details`) returning e.g. image src + alt + width/height,
   each title/description/H1 text, each canonical with head/header source, each hreflang code -> URL. For the image
   checks, better still: name each image as a target (`IssueReferences.Targets`, grouping RU) so a logo without alt on
   every page becomes ONE group with the template callout, exactly as image-broken works. Closes 10 of the 13 Blocks.
   (Changing a check's grouping/targets changes what's stored: new `AnalysisFreshness.VERSION`; ids unchanged.)

4. **Simple should show what Advanced shows about place.** Simple drops the row detail for every grouped check
   (`WhereItIs.tsx:220-315`), shows a status only when >= 400 (`:252`), and its found-on words have no anchor text or
   position (`whereFound.ts:161-178`); for targets checks it shows nothing past the group's own URL. Show, per page under
   a cause: the row detail, and "in the footer · "Old range"" (the `LinkRow` / `IssueTarget` already has both).
   Closes the Simple halves of internal-client-error, broken links, canonical-to-redirect/non-indexable, links-to-redirects.

5. **Show the chain, not its length.** Redirect checks (chain, loop, broken) and canonical-to-redirect should list
   hops inline (URL + status each) from the stored `redirect_url_id`s (the inspector's `RedirectChain` already builds
   them). Closes redirect-loop (Blocks) and redirect-chain.

6. **Cross-references the app already computes elsewhere.** blocked-by-robots -> the matching robots.txt line
   (`RobotsReader.cs:338`, today only on the Robots screen); sitemap-error -> all failing sitemaps + where referenced
   (Sitemaps screen); http-page / url-uppercase / url-underscores -> found-on (make them `References = Sources`).

7. **New collection (small).** (a) Image pixel dimensions: read the first few KB of each image response and parse the
   header (JPEG SOF / PNG IHDR / GIF / WebP / AVIF ispe) in `HttpFetcher`, store width/height on `responses`; then
   "1,840 KB · 4000×3000 px, shown at 400×300" makes the fix obvious. (b) The element that ended `<head>` early, with
   its source line, as a `PageFacts` field from `PageWalker` (HTML is parsed once): needed by canonical-outside-head and
   hreflang-outside-head. (c) Optional: HTML size breakdown for large-page / html-past-crawl-limit.

8. **Template signal for ungrouped checks.** `IsLikelyTemplate` (`IssueGroupReader.cs:189-190`) never fires for
   grouping N, so "every page lacks viewport/lang/canonical/x-default" or "the same 3 images lack alt on 400 pages" gets
   no "one fix" callout. Either regroup (pattern 3) or add a share-of-pages hint (e.g. >= 90% of pages flagged ->
   "probably the template").

9. **Exports follow the screens.** Workbook check sheets: Address | Page title | What was found | Other address + where
   columns; HTML report has no "where" for targets checks and no anchor text for sources. Whatever evidence columns
   (1-3) a check gets on screen should go to its sheet and its report table too.
