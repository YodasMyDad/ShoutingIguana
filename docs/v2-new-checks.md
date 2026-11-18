# Step 3b: the new checks

Stage A of the new checks: the triage and the specification. Docs only; stage B builds them, batch by batch
([Build batches](#build-batches)).

**The owner's decision, 2026-11-17:** "All of them": every check in the old app's backlog and every Screaming Frog check
that needs only data we already fetch. Each one is built with a planted case on the synthetic site, its expected count,
and a test seen failing.

**Sources merged here:** [the backlog of the old app's checks](v2-ui-discovery.md#backlog-checks-from-the-old-app) (all
three lists); [the comparison](v2-comparison.md#2-checks) ("Theirs we lack: from the HTML and headers we already fetch
(100)", "In part (7)", "For the check audit"); the real-site audit's two ideas (`accuracy-indexing.md` in the step 3b
scratch folder: "robots.txt doesn't name your sitemap", "paginated pages canonicalised to page 1"); the old app's
plugins (`src/ShoutingIguana.Plugins/`) for its thresholds; the engine as it stands on `ui/step-3`; and
[the evidence contract](v2-evidence.md) on branch `ui/3b-evidence` (commit `a97c31f`), whose terms every entry
below uses.

## The answer

| | Count |
| --- | --- |
| New checks (new ids) | **84** |
| Changes to existing checks (no new id) | **8** (one, pixel widths, at the owner's word) |
| Not built (of the backlog and the 100) | **27**, each with its reason ([Not built](#not-built-and-why)) |
| Build batches | **9**, plus the pixel widths when the owner says |

- **Deduplicated.** The backlog and the comparison name 11 of the same checks; each is one entry here. Screaming
  Frog splits some faults over several issues ("Outside \<head>" six times, the 50,000 and 50 MB limits, OG and X
  images); we keep one check per cause, as the catalog does, and the [mapping](#where-each-source-item-went) says
  where each of theirs went.
- **Severity.** An Error costs the score most and holds it under 90, so we add only **1** (`sitemap-over-limit`, the
  same failure as `sitemap-error`). **18** are Warnings, things that break search or visitors. **65** are Notices,
  which cost the score nothing.
- **What the parse must add.** 17 small `PageFacts` additions, from the one parse
  ([What the parse adds](#what-the-parse-adds)). Two new crawl-file columns, no schema version. One crawl change: Open
  Graph and X images become links the crawler checks (a new link kind).
- **Verified.** Every standard cited was read on 2026-11-17 unless marked *(unverified)*. Screaming Frog's own pages
  refused the fetch (403), so their definitions are from the comparison's names, not their text: a rule here that
  rests only on their name says so.

## How to read an entry

Each check below gives, in this order:

- **Heading:** number, proposed id (kebab-case, new), and the Advanced title.
- **First line:** category · severity, with why · value to a site owner (High / Medium / Low) · S/G/R.
- **Simple:** the title in plain words (the catalog's plain-words template: sentence case, no count, 60 characters at most).
- **Rule:** the precise condition and threshold, and the detail it writes.
- **Standard:** what it rests on, with *(unverified)* where the source couldn't be read.
- **Data:** what it reads (an existing field, by name) and what's new.
- **Evidence:** per [the evidence contract](v2-evidence.md#words-used-in-the-table): evidence columns, target facts,
  rows and groups.
- **Plant:** the planted case on the synthetic site and the count it must come to.
- **From:** the backlog item or Screaming Frog issue(s) it covers.

S/G/R is the contract's shorthand: subject (P page, U URL) / grouping (N none, PD page detail, RD response detail, RU
related URL) / references (– none, S sources, T targets). Rows come by importance (`imp` for page checks, `use` for URL
checks) unless an entry says otherwise; checks that don't group get the template hint automatically.

## Rules every new check keeps

- **The template stays clean.** The synthetic site's ordinary pages pass every new check; where one would fire on the
  template, the template changes (listed in [What changes existing flags](#what-changes-existing-flags-and-the-bench)),
  never the rule.
- **Facts a crawl didn't record are skipped, not read as absent.** `PageFacts` gains `CollectedVersion` (an int, 0 for
  every page crawled before). A check that needs a fact added after a page's version is skipped with the existing
  `earlier-version` reason when any audited page is older, as `near-duplicate-content` is for its fingerprint lanes.
  Where an absent fact can only *hide* a flag (a list that would be empty), the check runs and the note says so.
- **What the crawl didn't fetch can't be judged.** Checks that read a resource's response are skipped with
  `resources-off` when images and files weren't fetched, and those that read other sites' responses with
  `external-links-off`, as `image-broken` and `broken-external-links` are.
- **Exclusive where one fault would otherwise show twice.** Named in the entry ("exclusive with …"), as
  `noindex-with-canonical` is with `canonicalised-page` today.
- **Words.** Each check gets its catalog What, Why, Fix and LearnMore (the catalog's style: what's wrong, then why in
  two to four sentences that say when it doesn't matter) and its plain words in `plain-words.json`, thresholds as
  `{limit:…}` tokens filled by `PlainLimits`.
- **Settings.** A threshold that is ours, not a standard's, is an `AnalysisSettings` value with the default given here,
  stored with each analysis like the others.
- **Ids are for ever.** None reuses an existing id; once built, none is renamed.

## What the parse adds

All from the single parse in `HtmlPageParser`/`PageWalker`, so no page is parsed twice. Each addition defaults to the
value an old crawl reads as (an empty list, false, null), and `CollectedVersion` says whether it was collected.

| Addition | What it holds | For | Batch |
| --- | --- | --- | --- |
| `PageFacts.CollectedVersion` | Which collection made these facts | Skipping checks on old crawls | 1 |
| `PageFacts.Pagination` | `PaginationFact(Rel, Url)`: each `rel="next"`/`"prev"`/`"previous"` link, resolved (the links table has them as kind Pagination but can't tell next from prev) | `canonical-to-first-page`, the 4 pagination checks | 1 |
| `CanonicalFact.Attributes` | Which of `hreflang`, `lang`, `media`, `type` the canonical `<link>` also has, or null | `canonical-invalid-attribute` | 1 |
| `MetaRefreshFact.FromHttpHeader` | The refresh came from a `Refresh` response header | `meta-refresh` (change C1) | 1 |
| `PageFacts.UncrawlableLinks` | Count, and up to 3 examples (text cut to 60, and what made it uncrawlable) | `uncrawlable-links` | 2 |
| `PageFacts.HeadEnd` | The evidence branch's `HeadEndFact(Element, Line, Implicit)`, now set on **every** page whose head ended early, not only when a canonical or hreflang landed in the body | `head-tags-in-body` | 3 |
| `PageFacts.HeadTagsInBody` | Names of head-only tags found in the body (`title`, `meta description`, `og:title` …), up to 20 | `head-tags-in-body` | 3 |
| `PageFacts.StructureErrors` | Codes of the parser's document-structure errors (second `<head>`, second `<body>`, `<html>` after content, `<head>` after content), from AngleSharp's `HtmlParser.Error` event | `html-structure-errors` | 3 |
| `PageFacts.CharsetDeclarations` | Every meta charset / http-equiv value in order (up to 5), and the character offset of the first | `charset-invalid` | 3 |
| `PageFacts.MobileAlternate` | The first `<link rel="alternate" media=…>` without hreflang, resolved | `mobile-alternate` | 3 |
| `PageFacts.Microdata` | Each top-level Microdata item as a JSON-LD-shaped string (`@type`, properties, nested items), up to 50, like `JsonLd` | Change C5 and the structured data checks | 5 |
| `ImageFact.IsDataUri`, `DataUriBytes`, `InPicture` | The `<img>` has a `data:` source and its length; it sits in a `<picture>` with a `<source srcset>` | `image-missing-src`, `image-inline-large`, `image-no-srcset` | 6 |
| `PageFacts.Forms` | `FormFact(ActionUrl, Method, HasPassword)`, the action resolved (the page itself when absent), up to 20 | `form-not-secure` | 7 |
| `PageFacts.ProtocolRelativeUrls` | Count of `href`/`src` values starting `//`, and the first | `protocol-relative-links` | 7 |
| `HeadingFact.FromImageAlt` | The heading's text is an image's alt (the walker's logo-heading rule) | `h1-image-only` | 8 |
| `PageFacts.PlaceholderText`, `SentenceCount`, `SyllableCount`, `TotalWordCount` | The first "lorem ipsum" in the main text, quoted and cut; sentence and English syllable counts of the main text; words of the whole body, template included | `placeholder-text`, `readability-low`, `boilerplate-heavy` | 8 |
| `MetaTagFact.IsProperty` | The tag was written with `property=` rather than `name=` (today both land under one name) | `x-card-incomplete` | 9 |

Facts not stored with the page (`[JsonIgnore]`) aren't needed: every addition is small and read back by the analysis.

## Storage, crawl and reader changes

- **Two crawl-file columns, no schema version**, added as `listed_sitemaps` was (the writer adds what's missing when it
  opens a file; `CrawlSchema.VERSION` stays 2, so every crawl stays readable and continuable by either engine):
  - `sitemaps.size_bytes`: the sitemap's uncompressed size as read (`sitemap-over-limit`, `sitemap-near-limit`).
  - `sitemap_urls.lastmod`: the entry's `<lastmod>` as Unix seconds, `-1` when it doesn't parse, null when absent
    (`sitemap-lastmod-invalid`). A new `SITEMAP_URL_COLUMNS` list beside `SITEMAP_COLUMNS`.
- **One crawl change: `LinkKind.SocialImage`** (value 12). `og:image`, `og:image:url`, `og:image:secure_url` and
  `twitter:image` become links the crawler checks, as images are, so their status, type, size and pixel size are known.
  A new kind is read by every place that names kinds (`LinkKindExtensions`, the link summary, the app's link-kind
  words); an older engine reading a newer file sees an unknown kind and must show it as "Other". Not an `Image` link:
  the evidence contract matches the n-th image link to the n-th `<img>`, which an `og:image` would break.
- **Analysis reads, no schema change:** `UrlStatus` gains `ResponseMs` (int) and a content class (byte: HTML, script,
  stylesheet, image, other, none, invalid) from `responses.response_ms` and `content_type`; `AnalyzedPage` gains
  `FetchedAt`; a reader for the stored headers of internal redirect responses (they're in `response_details` already,
  never parsed); the robots.txt text (`robots.content`) for its `Sitemap:` lines; and the count of sitemap entries per
  URL (`sitemap_urls`).
- **New URL columns** (the contract's `UrlColumns`): `language` (`responses.language`, the `<html lang>`) and
  `redirectKind` (change C2). **New target facts:** `responseMs` (a link's target's response time) and `img.inPicture`.
- **Engine data, not storage:** a bundled schema.org vocabulary (types, properties, the types each property belongs
  to, from schema.org's current release) and the ISO 4217 currency list, both regenerated with a script, for
  `structured-data-schema-errors`; a CDN host list for `image-hotlinked`.
- **Freshness.** Every new check changes `AnalysisFreshness.ChecksFingerprint`, so every crawl's analysis reads out of
  date ("checks changed") until analysed again, with no version bump. A batch that changes what an existing check stores
  (changes C1, C3 to C7) bumps `AnalysisFreshness.VERSION` once. `ScoreCalculator.VERSION` doesn't change: the formula
  is the same, though scores move as new Warnings and the one Error find things.

## The checks

By category; [Build batches](#build-batches) gives the order they're built in. Plant paths are under `/plants/`,
written without it. "Expected" is the number of URLs the check must flag on the 1,000-page synthetic site; where
the 100,000-page bench differs, the entry says so.

### Indexability

#### 1. `robots-directives-conflict`: Meta robots and X-Robots-Tag disagree

Indexability · **Warning**: the stricter rule silently wins, so a page its HTML says to index is dropped · High · P/PD/–

- **Simple:** "Pages that tell search engines two different things"
- **Rule:** on an analysed page, read the robots rules for the crawler's token and Googlebot separately from the meta
  tags and from the `X-Robots-Tag` headers. Flag when both are present and contradict: one says `noindex` or `none`
  while the other says `index` or `all`; or one says `nofollow` or `none` while the other says `follow` or `all`. A
  rule one source has and the other doesn't mention isn't a conflict: search engines combine them. Detail:
  `The HTML says "index, follow"; the X-Robots-Tag header says "noindex"` (each quote cut with "…", 120 characters in all).
- **Standard:** Google, robots meta tag: "In the case of conflicting robots rules, the more restrictive rule applies";
  every meta rule can also be an `X-Robots-Tag`.
- **Data:** `PageFacts.MetaTags` (robots metas, by `RobotsMetaParser.AppliesToMetaName`), `AnalyzedPage.Headers`
  (`x-robots-tag`). Nothing new.
- **Evidence:** columns `metaRobots`, `xRobotsTag`. Grouped by the detail, so a template's conflict is one group.
- **Plant:** `robots-conflict`: `<meta name="robots" content="index, follow">` and the header `X-Robots-Tag: noindex`;
  issues `noindex-page`, `robots-directives-conflict`; not in the sitemap. Expected **1**. (`Page()` gains a headers
  argument.)
- **From:** backlog, "X-Robots-Tag in conflict with meta robots".

#### 2. `noindex-near-home`: Noindex page close to the home page

Indexability · **Warning**: a noindex one or two clicks from home is usually a section left noindexed by mistake;
`noindex-page` stays the Notice that lists them all · High · P/N/–

- **Simple:** "Important pages kept out of Google"
- **Rule:** a noindex page (`PageFacts.Robots.NoIndex`) at depth 1 or 2 (`AnalyzedPage.Depth`), not the home page
  (`site-hidden-from-search` judges that), whose path has no segment (case-insensitive) that names a private page:
  cart, basket, checkout, account, my-account, login, log-in, signin, sign-in, register, signup, sign-up, logout,
  wishlist, search, thank-you, thanks, order-received, password, reset-password. Not exclusive: `noindex-page` still
  flags it. Detail: `2 clicks from the start`.
- **Standard:** none sets a depth. The old app's rule (depth ≤ 2, `Robots/RobotsTask.cs:148-164`); the private-page
  list is ours.
- **Data:** `Robots`, `Depth`, `Uri`. Nothing new.
- **Evidence:** columns `metaRobots`, `xRobotsTag`, `depth`, `inlinks`.
- **Plant:** none needed: the synthetic site already lists noindex products on its category pages, two clicks from
  home. Expected: **every noindex product a category page or the home page lists**, which the expectations compute
  from `IsNoindex` and the listings (all noindex products on the 1,000-page site; those under 1,000 at 100,000 pages;
  with `HiddenFromSearch`, every listed product). A rule test proves the private-page list.
- **From:** backlog, "noindex on pages two clicks or fewer from the start".

#### 3. `unavailable-after-passed`: unavailable_after date has passed

Indexability · **Warning**: Google stops showing the page after that date, as if it were noindex; usually a forgotten
event or offer date · Medium · P/N/–

- **Simple:** "Pages set to drop out of Google on a date that has passed"
- **Rule:** a page without noindex whose robots rules (meta or header, for the crawler or Googlebot) have
  `unavailable_after: <date>`, where the date parses as RFC 822, RFC 850 or ISO 8601 and is before the page was fetched.
  Read from the raw meta content and header values, not `Robots.Directives`, which splits on commas and so cuts RFC
  850 dates. A date that doesn't parse is ignored, as Google ignores it. A date still to come isn't flagged. Detail:
  `unavailable_after: 2025-06-30, passed`. The page's indexability isn't changed (that would be a wider change).
- **Standard:** Google, robots meta tag: the date "in a widely adopted format including, but not limited to RFC 822, RFC
  850, and ISO 8601"; the rule is ignored without a valid date.
- **Data:** `MetaTags`, `Headers`; `AnalyzedPage.FetchedAt` (new, from `responses.fetched_at`).
- **Evidence:** columns `metaRobots`, `xRobotsTag`.
- **Plant:** `unavailable-after` (`unavailable_after: 2020-01-01`): expected **1**. `unavailable-later`
  (`unavailable_after: 2099-01-01`): flags nothing.
- **From:** backlog ("unavailable_after"); Screaming Frog Directives "Unavailable_After".

#### 4. `media-preview-blocked`: Image or video previews blocked

Indexability · **Notice**: as often deliberate as not · Medium · P/PD/–

- **Simple:** "Pages that keep their pictures or videos out of Google"
- **Rule:** an indexable page whose robots rules include `noimageindex`, `max-image-preview:none` or
  `max-video-preview:0`. Detail: the rules found (`noimageindex`), grouped by it.
- **Standard:** Google, robots meta tag (all three rules; `max-image-preview` takes `none`, `standard` or `large`).
- **Data:** `Robots.Directives`. Nothing new.
- **Evidence:** columns `metaRobots`, `xRobotsTag`.
- **Plant:** `image-preview-none` (robots `max-image-preview:none`). Expected **1**.
- **From:** backlog ("noimageindex, max-image-preview, max-video-preview"); Screaming Frog "NoImageIndex".

#### 5. `translation-blocked`: Translation blocked (notranslate)

Indexability · **Notice** · Low · P/N/–

- **Simple:** "Pages Google won't offer to translate"
- **Rule:** an indexable page whose robots rules include `notranslate`.
- **Standard:** Google, robots meta tag (`notranslate`).
- **Data:** `Robots.Directives`. **Evidence:** `metaRobots`, `xRobotsTag`.
- **Plant:** `notranslate`. Expected **1**.
- **From:** Screaming Frog "NoTranslate".

#### 6. `indexifembedded-without-noindex`: indexifembedded without noindex

Indexability · **Notice**: it does nothing, and harms nothing · Low · P/N/–

- **Simple:** "A search setting that does nothing on its own"
- **Rule:** robots rules for Googlebot include `indexifembedded` but not `noindex`.
- **Standard:** Google, robots meta tag: `indexifembedded` has an effect only with `noindex`.
- **Data:** `Robots.Directives`. **Evidence:** `metaRobots`, `xRobotsTag`.
- **Plant:** `indexifembedded`. Expected **1**.
- **From:** backlog ("indexifembedded").

### Canonicals

#### 7. `canonical-to-first-page`: Paginated page canonicalised to page 1

Canonicals · **Warning**: what's listed only on page 2 onward is found only through pages that ask to be dropped, and
Google says not to do it · High · P/RU/T

- **Simple:** "Later pages of a list point Google at page 1"
- **Rule:** a page whose first honoured canonical names another URL (the `canonicalised-page` condition) and which is a
  later page of a series whose first page that canonical names:
  - *a later page:* it has a `rel="prev"`/`"previous"` link (`PageFacts.Pagination`), or its URL carries a page number of
    2 or more: a query parameter `page`, `p`, `pg`, `paged` or `pagenum` (any case) with an integer value, or a path
    ending `/page/N` or `/page-N`;
  - *its first page:* the canonical, normalised, is the page's URL with that parameter or path part removed (or set to
    1), or the URL its `rel="prev"` names when that page has no `rel="prev"` of its own.

  **Exclusive with `canonicalised-page`.** Detail: `Page 2 names page 1 as its canonical`.
- **Standard:** Google, "Pagination, incremental page loading": "Don't use the first page of a paginated sequence as the
  canonical page"; give each page its own canonical.
- **Data:** `Canonicals`, `Uri`, the canonical link's `ToId`, `PageFacts.Pagination` (new). The URL patterns work without
  it, so a crawl from before still catches `?p=2`.
- **Evidence:** target facts `statusCode`, `indexability`, `canonical`, `flags`; grouped by the first page, so a
  series is one group.
- **Plant:** `list/` (page 1: `rel="next"` and an `<a>` to page 2) and `list/page/2/` (`rel="prev"` and an `<a>` back,
  canonical `/plants/list/`, not in the sitemap). Expected **1** (page 2). On a real site: gardenfurnituresets.co.uk's
  `/garden-sheds/?p=2`, `/greenhouses/?p=2` and `/blog/?p=2`.
- **From:** backlog ("paginated pages canonicalised to page 1"); the real-site audit (proposed at Warning).

#### 8. `canonical-other-domain`: Canonical points to another site

Canonicals · **Warning**: the page asks to be dropped for a page on another site; right for a syndicated copy, a
disaster when a template points every canonical at a staging host · High · P/RU/T

- **Simple:** "Pages that tell Google another website has the real copy"
- **Rule:** a page canonicalised (the `canonicalised-page` condition) to a URL that isn't internal (`UrlStatus.IsInternal`,
  as the crawl's scope decides: the start host, its www twin, and subdomains when they're included). **Exclusive with
  `canonicalised-page`.** Detail: `On another host: staging.example.com`.
- **Standard:** cross-domain canonicals are accepted by Google for syndicated content *(unverified: Google's canonical
  page read on 2026-11-17 doesn't address them)*.
- **Data:** `Canonicals`, the canonical link's target status. Nothing new.
- **Evidence:** target facts `statusCode`, `final`, `indexability`, `canonical`.
- **Plant:** `canonical-elsewhere`: canonical on the partner site (`/ext/canonical-target`, a 200 page), not in the
  sitemap. Expected **1**.
- **From:** backlog ("canonicals to another domain").

#### 9. `canonical-loop`: Canonical loop

Canonicals · **Warning**: Google has to choose a page itself, and may choose neither one the owner meant · Medium · P/RU/T

- **Simple:** "Pages that point Google at each other in a circle"
- **Rule:** follow each analysed page's first honoured canonical to another URL, page to page and through redirects, at
  most 10 steps. If it comes back to a page already passed, every page on the cycle is flagged, related to the page its
  canonical names. **Exclusive with `canonical-to-non-indexable`** for those pages (which would say "the canonical URL is
  canonicalised"); `canonicalised-page` still flags them. Detail: `A loop of 2: /a/ → /b/ → back to /a/`.
- **Standard:** none names loops. The old app's rule (`Canonical/CanonicalTask.cs:417-481`, which depended on order;
  this doesn't).
- **Data:** a map of page id to canonical target id, built in `Observe` (8 bytes a canonicalised page).
- **Evidence:** target facts `statusCode`, `indexability`, `canonical`.
- **Plant:** `canonical-loop-a` and `canonical-loop-b`, each canonical to the other, not in the sitemap; each also
  `canonicalised-page`. Expected **2**.
- **From:** backlog ("canonical loops as their own check").

#### 10. `canonical-on-redirect`: Canonical header on a redirect

Canonicals · **Notice**: search engines follow the redirect and ignore the rest, so it only misleads people reading the
headers · Low · U/N/S

- **Simple:** "Redirects that also name a different address as the real one"
- **Rule:** an internal URL that redirected and sent a `Link: <…>; rel="canonical"` header naming a URL other than where
  its redirects end. Detail: `Redirects to https://x/c/; its canonical header names https://x/b/`.
- **Standard:** none; the old app's rule (`Canonical/CanonicalTask.cs:516-537`), narrowed to a canonical that disagrees.
- **Data:** the stored headers of internal redirect responses (new reader); `LinkHeaderParser`.
- **Evidence:** column `redirectChain`; its found-on gives the links to it.
- **Plant:** `redirect-with-canonical`: 301 to `/plants/thin` with `Link: <…/plants/trailing/>; rel="canonical"`, linked
  from the hub. Expected **1**.
- **From:** backlog ("canonicals on a redirect").

#### 11. `canonical-fragment`: Canonical contains a fragment (#)

Canonicals · **Notice**: Google drops the fragment, so the page usually canonicalises as meant; the markup is still
wrong · Medium · P/PD/–

- **Simple:** "Canonical tags with a # part Google ignores"
- **Rule:** an honoured canonical (head or HTTP header) whose href, as written, has a `#` followed by anything. Detail:
  the fragment (`#reviews`), grouped by it.
- **Standard:** Google, consolidating duplicate URLs: "Don't specify a URL fragment as canonical, as Google generally
  doesn't support URL fragments."
- **Data:** `CanonicalFact.Href`. Nothing new.
- **Plant:** `canonical-fragment`: canonical to itself plus `#top` (so it's still self-canonical). Expected **1**.
- **From:** Screaming Frog Canonicals "Contains Fragment URL".

#### 12. `canonical-invalid-attribute`: Canonical link with hreflang, lang, media or type

Canonicals · **Notice** · Low · P/PD/–

- **Simple:** "Canonical tags written in a way Google may ignore"
- **Rule:** an honoured canonical `<link>` that also has `hreflang`, `lang`, `media` or `type`. Detail: the attributes
  (`media`).
- **Standard:** *(unverified)*: no Google page read on 2026-11-17 says a canonical with these is ignored; the check rests
  on Screaming Frog's issue name ("Invalid Attribute In Annotation") alone.
- **Data:** `CanonicalFact.Attributes` (new).
- **Plant:** `canonical-media`: the canonical has `media="only screen and (max-width: 640px)"`. Expected **1**.
- **From:** Screaming Frog Canonicals "Invalid Attribute In Annotation".

#### 13. `canonical-unlinked`: Canonical URL that no page links to

Canonicals · **Notice** · Low · U/N/S

- **Simple:** "Main versions of pages that nothing links to"
- **Rule:** an internal indexable page that an analysed page names as its honoured canonical, that no analysed page
  links to with `<a href>` (`UrlTable.Inlinks` 0, after crediting redirects) and that no sitemap lists (`orphan-page`
  judges sitemap pages). Detail: `Only canonicals lead here`.
- **Standard:** none *(the Screaming Frog name "Unlinked" only)*.
- **Data:** canonical links from `Observe`, `UrlTable.Inlinks`, `InSitemap`.
- **Evidence:** columns `inlinks`, `linkingPages`; its found-on lists the pages whose canonical names it.
- **Plant:** `canonical-to-unlinked` (canonical to `unlinked-target`; `canonicalised-page`) and `unlinked-target` (not
  linked, not in the sitemap; `canonical-unlinked`, `not-in-sitemap`). Expected **1**.
- **From:** Screaming Frog Canonicals "Unlinked".

### Links

#### 14. `uncrawlable-links`: Links search engines can't follow

Links · **Warning**: what's reached only through them is invisible to search engines · High · P/N/–

- **Simple:** "Links Google can't follow"
- **Rule:** a page with any link-like element that has no crawlable URL: an `<a>` whose href is a `javascript:` URL or
  empty (`href=""`); an `<a>` with `onclick` and no href or a bare `#`; an `<a>` with `routerlink` and no href; any other
  element with an `href` attribute (`<span href>`, `<div href>`). An `<a>` with neither href nor onclick is a named
  anchor and doesn't count. Detail: `3 links: “Shop” (javascript:), “Next” (onclick) …`. *Changed by the real-site
  audit:* only links that lead somewhere count: a `javascript:` href or onclick whose script navigates (sets the location
  or an href, opens a window, calls navigate or goto, or quotes a path or URL), a routerLink, an href with a URL on another
  element. A review widget's `<span href="javascript:void(0)">` and a theme's `<a href="">Read more</a>` were flagged, and
  lead nowhere a search engine could go (an empty href is the page itself).
- **Standard:** Google, "Make your links crawlable": Google "can only crawl your link if it's an `<a>` HTML element with an
  href attribute"; `<a onclick>`, `<span href>`, `<a routerLink>` and `javascript:` hrefs are "not recommended".
- **Data:** `PageFacts.UncrawlableLinks` (new).
- **Evidence:** the detail; the template hint when most pages have them (a menu).
- **Plant:** `uncrawlable-links`: `<a onclick="location='/plants/thin'">Thin page</a>` and
  `<a href="javascript:void(0)">Open menu</a>`. Expected **1**.
- **From:** Screaming Frog Links "Pages With Uncrawlable Internal Outlinks" and "Pages With Empty Anchor Tags".

#### 15. `broken-resources`: Page uses a broken script, stylesheet, frame or media file

Links · **Warning**: a missing stylesheet or script can break the page for visitors and for Google's rendering, but a
broken third-party tag often changes nothing anyone sees, so not an Error like `image-broken` · High · P/RU/T

- **Simple:** "Pages that load files that are missing"
- **Rule:** a page with links of kind Script, Stylesheet, Iframe or Media whose target is broken through redirects
  (`UrlTable.IsBrokenThroughRedirects`, as broken links are, so another site's bot refusals don't count). Images stay
  `image-broken`'s. Detail: `2 broken files (the first returns 404 Not Found)` (`RuleText.BrokenTargets`).
- **Standard:** none needed: Google renders pages with their resources (JavaScript SEO basics).
- **Data:** `Links`, `UrlTable`. Nothing new. Skipped with `resources-off`.
- **Evidence:** target facts `statusCode`, `final`, `kind`, `position`, `links`.
- **Plant:** `broken-script`: `<script src="/plants/missing.js">`, a 404. Expected **1**; `/plants/missing.js` adds 1 to
  `internal-client-error`.
- **From:** backlog ("pages using broken CSS, JavaScript or iframes").

#### 16. `links-to-localhost`: Links to localhost or a private address

Links · **Warning**: every visitor gets a dead link; it's a development address left in content · Medium · P/RU/T

- **Simple:** "Links to a test copy that only works for whoever built it"
- **Rule:** a page linking (any kind) to a host that is `localhost`, ends `.localhost`, or is a loopback (127.0.0.0/8, ::1)
  or private (10/8, 172.16/12, 192.168/16, fc00::/7) address. Skipped, saying why, when the start URL's own host is one:
  a crawl of a development copy can't tell. Detail: `2 links, such as http://localhost:3000/shop`.
- **Standard:** none needed.
- **Data:** `Links`, the target's URL text (`readUrl`, for external targets only).
- **Evidence:** target facts `statusCode`, `text`, `position`.
- **Plant:** none: the synthetic site and its partner are on 127.0.0.1, so the rule skips there. A rule test with a page
  on `https://example.com` linking `http://localhost:3000/` proves it; the whole-table test proves the skip. Expected
  **0**. *For the crawler, not this check:* a link to a private address makes the crawler ask the user's own machine or
  network; whether it should fetch such links at all is worth deciding.
- **From:** Screaming Frog Links "Outlinks To Localhost".

#### 17. `few-internal-outlinks`: Page with few or no internal links

Links · **Notice**: a dead end for visitors and crawlers, though the page itself is fine · Medium · P/N/–

- **Simple:** "Dead-end pages that link to nothing else on your site"
- **Rule:** an indexable page that links with `<a href>` to fewer than 3 (setting `MinInternalOutlinks`) distinct internal
  URLs other than itself (followed or not; same-page fragments don't count). Detail: `No links to other pages`, or
  `1 link to another page, under 3`.
- **Standard:** none sets a number. Screaming Frog flags none at all; the old app, fewer than 3
  (`InternalLinking/InternalLinkingTask.cs:217`).
- **Data:** `Links`. Nothing new.
- **Evidence:** column `outlinks`.
- **Plant:** `dead-end`, with a new `PageSpec.Chrome = false` that leaves out the header, menu and footer. Expected **1**.
- **From:** backlog ("pages with no outlinks, or few"); Screaming Frog "Pages Without Internal Outlinks".

#### 18. `generic-anchor-text`: Links with generic text

Links · **Notice**: WCAG lets "read more" pass when the sentence around it says where it goes · Medium · P/N/T

- **Simple:** "Links that say “click here” instead of where they go"
- **Rule:** internal hyperlinks (not image links) whose text, lowercased with punctuation and spaces trimmed, is one of:
  click here, click, here, read more, more, learn more, find out more, see more, view more, more info, more information,
  continue, continue reading, details, link, this, this page, this link, go, website, article. Detail:
  `3 links: “Read more” ×2, “here”`.
- **Standard:** Google, "Make your links crawlable": "Click here", "Read more", "website" and "article" are "Bad (too
  generic)"; WCAG 2.1 SC 2.4.4 (purpose from the link or its context).
- **Data:** `Links` (`Text`). Nothing new.
- **Evidence:** target facts `text`, `position`, `statusCode` (its targets are the URLs linked that way).
- **Plant:** `generic-anchors`: `Click here` and `Read more…` to `/plants/thin`. Expected **1**.
- **From:** backlog ("generic anchor text"); Screaming Frog "Non-Descriptive Anchor Text In Internal Outlinks".

#### 19. `non-indexable-inlinks-only`: Linked only from non-indexable pages

Links · **Notice** · Medium · P/N/S

- **Simple:** "Pages only linked from pages Google won't show"
- **Rule:** an indexable page, not the start or home page, with at least one followed `<a href>` inlink (redirects
  credited), every one of them from a page that isn't indexable (noindex, canonicalised, or an instant meta refresh).
  Detail: `Linked from 3 pages, none of them indexable`.
- **Standard:** none *(Screaming Frog's name only)*.
- **Data:** a second inlink count beside `UrlTable`'s: followed inlinks from indexable pages (an int per URL).
- **Evidence:** columns `inlinks`, `linkingPages`; its found-on lists the linking pages and their indexability.
- **Plant:** `noindex-hub` (noindex; links `only-from-noindex`; not in the sitemap) and `only-from-noindex` (not linked
  otherwise, not in the sitemap; also `not-in-sitemap`). Expected **1**. **Bench:** plus any product every linking
  product of which is noindex or canonicalised, computed by the expectations from the generated links.
- **From:** Screaming Frog Links "Non-Indexable Page Inlinks Only".

#### 20. `deep-page`: Page many clicks from the home page

Links · **Notice** · Medium · P/N/–

- **Simple:** "Pages buried deep in your site"
- **Rule:** an indexable page more than 4 clicks (setting `MaxClickDepth`) from the start URL by the shortest path of
  followed links. Pages found only through sitemaps aren't measured (`orphan-page` covers them). Detail:
  `6 clicks from the start, over 4`. **Which depth:** if the crawl's recorded `depth` is the shortest path whatever the
  order pages were fetched in (the frontier orders by depth; a URL first found deep and later found shallower must keep
  the shallower), use it; if not, the analysis works it out by a breadth-first pass over the links it reads (4 bytes a
  link: about 12 MB at 100,000 pages, for the memory gate). The first build task is to find out which.
- **Standard:** none sets a number; Screaming Frog's threshold couldn't be read. 4 is ours.
- **Data:** `AnalyzedPage.Depth`, or the links. Nothing new in the parse.
- **Evidence:** columns `depth`, `inlinks`.
- **Plant:** `deep-1` (linked from the hub, 3 clicks) → `deep-2` → `deep-3` (5 clicks). Expected **1** on the
  1,000-page site. **Bench:** at 100,000 pages, every product more than 4 clicks deep, computed by the expectations
  from the generated links: likely thousands (the category pages list only the first 1,000 products; the rest hang off
  random and tree links).
- **From:** Screaming Frog Links "Pages With High Crawl Depth".

#### 21. `too-many-links`: Page with a very high number of links

Links · **Notice** · Low · P/N/–

- **Simple:** "Pages crowded with links"
- **Rule:** an indexable page with more than 1,000 internal or more than 100 external `<a href>` links (settings
  `MaxInternalLinks`, `MaxExternalLinks`), every link counted, repeats included. Links are kept to 10,000 a page, so
  that is the most it can count. Detail: `1,240 internal links, over 1,000`.
- **Standard:** none current *(Google's old advice to keep links to "a reasonable number" is no longer in its guides;
  unverified)*. Thresholds ours; Screaming Frog's couldn't be read.
- **Data:** `Links`. **Evidence:** column `outlinks`.
- **Plant:** `many-links`: 1,001 links to `/plants/thin`. Expected **1**.
- **From:** Screaming Frog "Pages With High Internal Outlinks" and "Pages With High External Outlinks".

#### 22. `slow-external-links`: Links to slow external pages

Links · **Notice**: the other site's server isn't the owner's to fix, only the link · Low · P/RU/T

- **Simple:** "Links to other websites that take a long time to load"
- **Rule:** a page linking with `<a href>` to external URLs that answered (success or redirect) after more than 3,000 ms
  (setting `SlowExternalLinkMs`) for the whole response. Skipped with `external-links-off`. Detail:
  `2 slow links, the slowest 7.4 s`.
- **Standard:** none; the old app's (5 s, which its own timeout made unreachable).
- **Data:** `UrlStatus.ResponseMs` (new).
- **Evidence:** target facts `statusCode`, `responseMs` (new), `text`, `position`.
- **Plant:** `slow-external`, linking the partner site's `/ext/slow` (it answers after 3.2 s). Expected **1**. Adds about
  3 s to a test crawl.
- **From:** backlog ("slow external links").

#### 23. `pagination-not-linked`: Pagination URL not linked with an `<a>`

Links · **Notice**: Google ignores `rel="next"`/`"prev"` and finds later pages only through ordinary links · Medium · P/RU/T

- **Simple:** "Next pages of a list that only hidden tags point to"
- **Rule:** a page with a `rel="next"` or `rel="prev"` to an internal page (fetched, 200, HTML) that it doesn't also link
  to with `<a href>` (fragment ignored). Detail: `rel="next" https://x/list/page/2/ has no link`.
- **Standard:** Google, pagination: "Google no longer uses these tags"; Google "can only discover your links if they
  are `<a>` HTML elements with an `href` attribute" (JavaScript SEO basics).
- **Data:** `PageFacts.Pagination`, `Links`.
- **Evidence:** target facts `statusCode`, `indexability`.
- **Plant:** `unlinked-series/` (`rel="next"` only) and `unlinked-series/page/2/` (`rel="prev"` only; not in the
  sitemap, so also `not-in-sitemap`). Expected **2**. The existing `series` and `series-2` plants gain `<a>` links to
  each other, or they'd be flagged.
- **From:** Screaming Frog Pagination "Pagination URL Not In Anchor Tag", "Unlinked Pagination URLs".

#### 24. `pagination-broken`: Pagination URL broken or redirected

Links · **Notice** · Low · P/RU/T

- **Simple:** "Next or previous page links that lead to an error"
- **Rule:** a `rel="next"`/`"prev"` target that is broken through redirects, or redirects. Detail:
  `rel="next" returns 404 Not Found`, `rel="prev" redirects (301)`. Exclusive with `pagination-not-linked` for that target.
- **Data:** `PageFacts.Pagination`, `UrlTable`. **Evidence:** target facts `statusCode`, `final`.
- **Plant:** `series-broken`: `rel="next"` to `/plants/gone`. Expected **1**.
- **From:** Screaming Frog "Non-200 Pagination URLs".

#### 25. `pagination-to-non-indexable`: Pagination URL not indexable

Links · **Notice** · Low · P/RU/T

- **Simple:** "Next or previous page links to pages Google won't show"
- **Rule:** a `rel="next"`/`"prev"` target that is a page but not indexable. Detail: `rel="next" is noindex`.
- **Evidence:** target facts `statusCode`, `indexability`, `canonical`.
- **Plant:** `series-noindex`: `rel="next"` and an `<a>` to `/plants/noindex`. Expected **1**.
- **From:** Screaming Frog Pagination "Non-Indexable".

#### 26. `pagination-sequence-error`: Pagination sequence error

Links · **Notice** · Low · P/PD/–

- **Simple:** "Next and previous page links that don't match up"
- **Rule:** any of: more than one distinct `rel="next"` or `rel="prev"`; one naming the page itself; page A's
  `rel="next"` is B but B (analysed) has no `rel="prev"` to A; following `rel="next"` comes back to a page already passed
  (at most 50 steps). Detail: the fault (`2 rel="next" URLs`, `rel="next" names the page itself`,
  `/list/page/2/ doesn't point back`, `rel="next" leads back to /list/`), grouped by its kind.
- **Data:** `PageFacts.Pagination`; the cross-page part needs a map of page to next and prev (8 bytes a paginated page).
- **Plant:** `series-self`: `rel="next"` to itself. Expected **1**.
- **From:** Screaming Frog "Multiple Pagination URLs", "Pagination Loop", "Sequence Error".

### Response codes, redirects and performance

#### 27. `content-type-mismatch`: File served with the wrong content type

Response codes · **Warning**: with `nosniff` browsers refuse a script or stylesheet sent as another type, and an
"image" that answers with a web page is a hidden error · High · U/RD/S

- **Simple:** "Files your server labels as the wrong kind"
- **Rule:** a URL that answered 2xx and that an analysed page uses as a script whose Content-Type isn't a JavaScript type
  (`text/javascript`, `application/javascript`, `application/x-javascript`, `application/ecmascript`,
  `text/ecmascript`); as a stylesheet not sent as `text/css`; or as an `<img>` not sent as `image/*`; or whose
  Content-Type is missing or isn't `type/subtype`. A URL used two ways is judged for each. Detail:
  `Used as a script, sent as text/html`, grouped by it. Skipped with `resources-off`.
- **Standard:** MDN, `X-Content-Type-Options`: with `nosniff`, script and style requests whose MIME type doesn't match
  are blocked. HTML: a stylesheet not sent as `text/css` is ignored in standards mode *(unverified: not re-read)*.
- **Data:** `UrlStatus` content class (new); the kinds each URL is used as, from `Observe` over `page.Links`.
- **Evidence:** columns `type`, `contentType`; its found-on gives the pages using it.
- **Plant:** `script-as-text` uses `<script src="/plants/not-a-script.js">`, which answers 200 `text/plain`. Expected
  **1** (the script's URL).
- **From:** Screaming Frog Security "Bad Content Type".

#### 28. `redirect-temporary-cached`: Temporary redirect cached for long

Redirects · **Notice** · Low · U/RD/S

- **Simple:** "Temporary redirects that browsers remember for days"
- **Rule:** an internal 302, 303 or 307 (the `redirect-temporary` condition) whose response lets it be cached for more
  than a day: `Cache-Control` `max-age` or `s-maxage` over 86,400, or `Expires` more than a day after `Date`, and no
  `no-store` or `no-cache`. Detail: `302 Found, cached for 30 days`, grouped by it.
- **Standard:** RFC 9111: a response with an explicit `max-age` is stored and fresh for that long *(read through a
  summary of the RFC)*. The one-day line is the old app's (`Redirects/RedirectsTask.cs:652-678`).
- **Data:** the stored headers of internal redirect responses (the reader `canonical-on-redirect` adds).
- **Evidence:** column `redirectChain`.
- **Plant:** `cached-302`: a 302 to `/plants/thin` with `Cache-Control: max-age=2592000`, linked from the hub; issues
  `redirect-temporary`, `redirect-temporary-cached`. Expected **1**.
- **From:** backlog ("temporary redirects cached for more than a day").

#### 29. `resource-past-crawl-limit`: Script or stylesheet over 2 MB

Performance · **Warning**: Googlebot reads only the first 2 MB of each file, so the rest of a larger script or
stylesheet is lost to the page as Google renders it · Medium · U/N/S

- **Simple:** "Code files too big for Google to read in full"
- **Rule:** a script or stylesheet (internal or external) an analysed page uses, answering 2xx, over 2 MB (2,097,152
  bytes) decoded (`responses.body_bytes`, else `size_bytes`). Detail: `2.4 MB, past the 2 MB Google reads`. Skipped with
  `resources-off`.
- **Standard:** Google, Googlebot: "each resource referenced in the HTML (such as CSS and JavaScript) is fetched
  separately", and "each resource fetch is bound by the same file size limit"; the first 2 MB.
- **Data:** `UrlStatus.SizeBytes`, the kinds a URL is used as.
- **Evidence:** columns `sizeBytes`, `contentType`.
- **Plant:** `huge-script` uses `/plants/huge.js` (2.2 MB of script). Expected **1** (the script).
- **From:** Screaming Frog Validation "Resource Over 2MB".

### Sitemaps

#### 30. `robots-txt-no-sitemap`: robots.txt doesn't name the sitemap

Sitemaps · **Notice** · Medium · U/N/– (reported once)

- **Simple:** "Your robots.txt doesn't say where your sitemap is"
- **Rule:** once, on the home page, when sitemaps were read: robots.txt was read on a main origin, none of the main
  origins' robots.txt has a `Sitemap:` line, and a sitemap was found anyway (the probe of `/sitemap.xml`, or the
  settings). With none found, `sitemap-missing` says so instead. Detail:
  `https://x/robots.txt has no Sitemap: line; the sitemap was found at https://x/sitemap.xml`.
- **Standard:** Google, robots.txt: a `sitemap` line takes a fully qualified URL, which can be on another host.
- **Data:** `robots.content`, `sitemaps.is_probe` (analysis reads).
- **Plant:** a loopback crawl test with a new `SyntheticSiteOptions.SitemapInRobots = false` (the main site's
  robots.txt must name its sitemap), as `robots-txt-missing` is proven. Expected **1** there, **0** on the default site.
- **From:** the real-site audit (gardenfurnituresets.co.uk: an empty robots.txt, its sitemap found by the probe).

#### 31. `sitemap-over-limit`: Sitemap over 50,000 URLs or 50 MB

Sitemaps · **Error**: a file over the limit isn't read, so nothing in it reaches search engines, the same failure as
`sitemap-error` · Medium · U/N/– (reported once)

- **Simple:** "A sitemap too big for Google to read"
- **Rule:** once, on the home page: a sitemap read with more than 50,000 URL entries (`sitemaps.url_count`), an index
  listing more than 50,000 sitemaps (`listed_sitemaps`), or a file over 50 MB (52,428,800 bytes) uncompressed
  (`sitemaps.size_bytes`, new). Detail: the files, `https://x/sitemap.xml (62,113 URLs)` and how many more.
- **Standard:** Google, build a sitemap: "All formats limit a single sitemap to 50MB (uncompressed) or 50,000 URLs";
  sitemaps.org, the same. That the whole file is then refused *(unverified: Search Console reports it as an error)*.
- **Data:** `url_count`, `listed_sitemaps`; `size_bytes` (new column). Check whether `url_count` counts entries or
  distinct URLs, and count entries.
- **Plant:** none: 50,001 entries would make the test site crawl 50,000 more URLs. A rule test over stored sitemap
  rows. Expected **0**.
- **From:** backlog ("sitemaps at the 50,000-URL limit"; "the sitemap file's size"); Screaming Frog "XML Sitemap With Over
  50k URLs", "XML Sitemap Over 50mb".

#### 32. `sitemap-near-limit`: Sitemap near the 50,000-URL or 50 MB limit

Sitemaps · **Notice** · Low · U/N/– (reported once)

- **Simple:** "A sitemap close to the size Google can read"
- **Rule:** as `sitemap-over-limit`, between 45,000 and 50,000 entries or 45 and 50 MB. Detail as there.
- **Standard, data:** as `sitemap-over-limit`; the 90% line is ours (the old app warned at 45,000).
- **Plant:** none, as above; a rule test. Expected **0**.
- **From:** backlog ("near the 50,000-URL limit").

#### 33. `sitemap-empty`: Empty sitemap

Sitemaps · **Notice** · Low · U/N/– (reported once)

- **Simple:** "A sitemap with nothing in it"
- **Rule:** once, on the home page: a sitemap read without error (2xx) with no URL and no sitemap entries (`url_count`
  0, `listed_sitemaps` 0). Detail: the files.
- **Standard:** none needed.
- **Data:** existing columns.
- **Plant:** `/sitemaps/empty.xml` (`<urlset>` with no entries), listed in the sitemap index. Expected **1** (the home page).
- **From:** backlog ("empty sitemaps").

#### 34. `sitemap-url-repeated`: URL listed in more than one sitemap

Sitemaps · **Notice** · Low · U/N/S

- **Simple:** "Addresses listed in more than one sitemap"
- **Rule:** an internal URL that more than one sitemap file lists. Detail: `In 2 sitemaps`.
- **Standard:** none (sitemaps.org allows it; it's housekeeping).
- **Data:** entries per URL from `sitemap_urls` (analysis read).
- **Evidence:** its found-on lists the sitemaps.
- **Plant:** `/plants/thin` is also listed in `pages-0.xml`. Expected **1**.
- **From:** Screaming Frog Sitemaps "URLs In Multiple Sitemaps".

#### 35. `sitemap-lastmod-invalid`: Sitemap lastmod invalid or in the future

Sitemaps · **Notice** · Low · U/N/– (reported once)

- **Simple:** "Sitemap dates that are wrong"
- **Rule:** once, on the home page: entries whose `<lastmod>` isn't a W3C Datetime, or is more than a day after the
  crawl. Detail: `1,204 entries: 1,200 in the future, 4 unreadable (https://x/sitemap.xml and 1 more)`.
- **Standard:** sitemaps.org: `lastmod` in W3C Datetime; Google uses `lastmod` "if it's consistently and verifiably
  accurate".
- **Data:** `sitemap_urls.lastmod` (new column); the sitemap reader parses `<lastmod>`.
- **Plant:** the extra sitemap gives `/plants/thin` `<lastmod>3000-01-01</lastmod>`. Expected **1** (the home page).
- **From:** backlog ("sitemap entries' lastmod").

### Structured data

#### 36. `structured-data-schema-errors`: Structured data that isn't valid schema.org

Structured data · **Warning**: what search engines can't read they ignore, and nothing says so · Medium · P/PD/–

- **Simple:** "Product or business details written in a way Google can't use"
- **Rule:** in each JSON-LD block that parses (and each Microdata item): no `@context`, or one that isn't schema.org
  (`http://` or `https://schema.org`, with or without the trailing slash, alone or in a list or object); a `@type` that
  isn't a schema.org type; a property schema.org doesn't define for that type or its parents; a Date or DateTime property
  (`datePublished`, `dateModified`, `uploadDate`, `startDate`, `endDate`, `priceValidUntil`, `validFrom`,
  `validThrough` and the rest the vocabulary types so) whose value isn't ISO 8601; a `priceCurrency` that isn't an ISO
  4217 code. Detail: the first three faults (`Organisation isn't a schema.org type; Offer: priceCurrency “Dollars”
  isn't ISO 4217`), grouped by it.
- **Standard:** schema.org Date and DateTime (ISO 8601) and `priceCurrency` (ISO 4217); Google merchant listings
  (ISO 4217); JSON-LD 1.1: a document without a context is still conformant JSON-LD, but its terms then aren't
  schema.org's.
- **Data:** `JsonLd`, `Microdata`; the bundled vocabulary and ISO 4217 list (engine data).
- **Plant:** `jsonld-unknown-type`: `{"@context":"https://schema.org","@type":"Organisation","name":"Synthetic Store"}`.
  Expected **1**.
- **From:** backlog ("a missing or invalid @context"; "invalid dates and currencies"); Screaming Frog Structured Data
  "Validation Errors".

#### 37. `structured-data-missing-recommended`: Structured data missing recommended properties

Structured data · **Notice**: the markup still works; it could do more · Medium · P/PD/–

- **Simple:** "Product or article details that could be fuller"
- **Rule:** an indexable page with a top-level entity (as `RichResultRequirements` finds them) missing what Google
  recommends:
  - Article, NewsArticle, BlogPosting: `headline`, `image`, `datePublished`, `author`;
  - Organization: `url`, `logo`;
  - Product: `image` (required for merchant listings); its Offer: `priceCurrency`, `availability`.

  Detail: `Article: lacks image, datePublished`, grouped by it. More types later, each from Google's page for it.
- **Standard:** Google, Article ("There are no required properties"; recommended include `author`, `datePublished`,
  `headline`, `image`), Organization (no required properties; recommended include `logo`, `url`), product snippets and
  merchant listings (`image` required for merchant listings; Offer recommends `availability`, `priceCurrency`).
- **Data:** `JsonLd`, `Microdata`. Nothing new.
- **Plant:** `jsonld-article-thin`: an Article with only a `headline`. Expected **1**. The products' JSON-LD gains
  `image` and `availability` so the template stays clean.
- **From:** backlog ("Article, Organization … requirements"); Screaming Frog "Validation Warnings", "Rich Result
  Validation Warnings".

#### 38. `structured-data-on-noindex`: Structured data on a noindex page

Structured data · **Notice**: harmless, but the markup can't do anything there · Low · P/PD/–

- **Simple:** "Product or article details on pages hidden from Google"
- **Rule:** a noindex page with a parseable JSON-LD block or Microdata item of a type that has rich results (the types
  `RichResultRequirements` knows, plus Article and Organization). Detail: the types (`Product, BreadcrumbList`).
- **Standard:** Google, structured data guidelines: "Don't block your structured data pages to Googlebot using
  robots.txt, noindex, or any other access control methods."
- **Data:** `JsonLd`, `Microdata`, `Robots`.
- **Evidence:** columns `metaRobots`, `xRobotsTag`.
- **Plant:** `jsonld-noindex`: noindex, with a complete Product; issues `noindex-page`, `structured-data-on-noindex`.
  Expected **1**. **Bench:** plus every noindex product that carries Product JSON-LD (every fifth product), computed by
  the expectations.
- **From:** backlog ("rich results on a noindex page").

### Images

#### 39. `image-missing-src`: Image without a source

Images · **Warning**: the image never shows; not an Error, because a lazy-loading script with attributes the crawler
doesn't know can look the same · Medium · P/N/–

- **Simple:** "Images with no picture file named"
- **Rule:** an `<img>` with no usable URL (no `src`, none of the lazy-loading attributes the walker reads, no `srcset`)
  that isn't a `data:` image. Detail: `2 images`.
- **Standard:** HTML: an `img` must have a `src` *(not re-read)*.
- **Data:** `ImageFact.Url`, `ImageFact.IsDataUri` (new).
- **Plant:** `image-no-src`: `<img alt="A photo with no address" width="400" height="300">`. Expected **1**.
- **From:** backlog ("a missing src").

#### 40. `image-no-srcset`: Large image without srcset

Images · **Notice** · Low · P/RU/T

- **Simple:** "Big images sent to phones at full size"
- **Rule:** an `<img>` whose file is at least 800 px wide (setting `ResponsiveImageMinWidth`), with no `srcset` and not in
  a `<picture>` with a `<source srcset>`. Needs the file's pixel size (the evidence branch reads it from the image's
  first bytes); skipped with `resources-off`. Detail: `2 images, the widest 2,400 px`.
- **Standard:** web.dev, responsive images: with several versions "the browser chooses the best one to use"; Google
  Images reads `srcset` and `<picture>`, with `src` as the fallback. 800 px is ours.
- **Data:** `ImageFact.HasSrcset`, `ImageFact.InPicture` (new), `responses.pixel_width` (evidence branch).
- **Evidence:** target facts `img` (`srcset`, `width`), `pixelWidth`, `pixelHeight`, `sizeBytes`.
- **Plant:** `image-wide` shows `/plants/wide.png`, a real 1,200 × 800 PNG. Expected **1**. If the evidence branch
  serves a real image for `/img/large.jpg` at 800 px or wider, the `image-large` plant gains this too.
- **From:** backlog ("no srcset").

#### 41. `image-legacy-format`: Image in an older format

Images · **Notice** · Low · P/RU/T

- **Simple:** "Images that could be smaller in a newer format"
- **Rule:** an `<img>` file sent as `image/jpeg`, `image/png`, `image/gif` or `image/bmp` and 50 KB or more (setting
  `LegacyImageMinBytes`). Skipped with `resources-off`. Detail: `2 images, the largest 312 KB`.
- **Standard:** Lighthouse's modern-formats audit reports an image when WebP or AVIF would save 8 KiB or more; Google
  Images supports WebP and AVIF. The 50 KB line is ours, about where those formats save 8 KiB.
- **Data:** `UrlStatus` content class and `SizeBytes`.
- **Evidence:** target facts `contentType`, `sizeBytes`, `pixelWidth`, `pixelHeight`.
- **Plant:** `image-legacy` shows `/plants/legacy.png` (100 KB). Expected **2**: it, and the existing `image-large`
  plant (a 300 KB JPEG).
- **From:** backlog ("legacy formats").

#### 42. `image-alt-unhelpful`: Alt text that's a file name or too short

Images · **Notice** · Low · P/RU/T

- **Simple:** "Image descriptions that don't describe anything"
- **Rule:** an `<img>` whose alt, trimmed, isn't empty (empty is right for a decorative image) and is 1 or 2 characters,
  has no letter or digit, or looks like a file name: ends in an image extension (`.jpg`, `.jpeg`, `.png`, `.gif`,
  `.webp`, `.avif`, `.svg`, `.bmp`), matches a camera or upload name (`IMG_1234`, `DSC01234`, `PXL_20230101…`,
  `image-3`, `photo_12`), or is the image file's own name. Detail: `2 images`.
- **Standard:** WCAG 2.1 SC 1.1.1: a text alternative serves the image's purpose *(not re-read)*. The lengths are ours
  (the old app said under 5 characters, which flags "Logo" and "Map").
- **Data:** `ImageFact.Alt`, `Url`. Nothing new.
- **Evidence:** target facts `img` (`alt`, `altLength`), `position`.
- **Plant:** `image-alt-file-name`: alt `IMG_0423.jpg`. Expected **1**.
- **From:** backlog ("alt text that is very short or looks like a file name").

#### 43. `image-hotlinked`: Image loaded from another website

Images · **Notice** · Low · P/RU/T

- **Simple:** "Images shown straight from other websites"
- **Rule:** an `<img>` whose host isn't the site's (the crawl's scope), isn't under the site's registrable domain, and
  isn't a host on the bundled CDN list (cdn.shopify.com, `*.cloudfront.net`, `*.akamaized.net`, `*.fastly.net`,
  res.cloudinary.com, `*.imgix.net`, `i0`–`i3.wp.com`, `*.wixstatic.com`, images.squarespace-cdn.com,
  images.ctfassets.net, `*.bigcommerce.com`, `*.googleusercontent.com`). Skipped, saying why, when the start URL is on a
  loopback or private host. Detail: `3 images from 2 sites`.
- **Standard:** none; the old app's rule, which counted CDNs and so flagged most shops.
- **Data:** `Links` (Image kind), the target's URL text; a registrable-domain rule (check whether the scope code has one).
- **Evidence:** target facts `statusCode`, `img`, `position`.
- **Plant:** none: the synthetic site and its partner share 127.0.0.1. A rule test. Expected **0**.
- **From:** backlog ("hotlinked images").

#### 44. `image-inline-large`: Large inline (data:) image

Images · **Notice** · Low · P/N/–

- **Simple:** "Pictures built into the page that slow it down"
- **Rule:** an `<img>` with a `data:` URI over 10 KB (setting `InlineImageMaxBytes`). Detail:
  `2 inline images, the largest 48 KB`.
- **Standard:** none; the old app's 10 KB. An inline image can't be cached apart from the page.
- **Data:** `ImageFact.DataUriBytes` (new).
- **Plant:** `image-inline`: a 12 KB `data:` PNG. Expected **1**.
- **From:** backlog ("data-URI images").

### HTML: the document and its head

#### 45. `head-tags-in-body`: Head tags in the body

HTML · **Warning**: search engines and social sites may ignore page settings outside the head (Google says so of
canonicals), and one stray element in the head usually moves every tag after it · High · P/PD/–

- **Simple:** "Page settings that ended up where they're ignored"
- **Rule:** an analysed page with any of these outside the head, in the tree the parser builds (as browsers and Google
  build it): `<title>`; a `<meta>` named description, robots, googlebot or the crawler's token, or viewport, or with
  `http-equiv` refresh or content-type; an Open Graph (`og:*`) or X (`twitter:*`) meta; a `<link rel="alternate">`
  without hreflang, or `rel="amphtml"`. Not canonical or hreflang links (their own checks report them), nor `<base>`,
  scripts and styles, which work anywhere. Detail: what's out and why:
  `title, meta description and 3 Open Graph tags are in the body: <div id="promo"> on line 12 ended the head early`,
  or `… are in the body, after <body> on line 30`. **Grouped by the cause** (a new `FaultOf`: the element that ended
  the head, or "after <body>"), so a template's stray element is one group.
- **Standard:** HTML: the head holds metadata content (`base`, `link`, `meta`, `noscript`, `script`, `style`,
  `template`, `title`); Google accepts `rel="canonical"` "only if it appears in the `<head>`". That the other tags are
  ignored in the body *(unverified)*: the Warning is by that analogy.
- **Data:** `PageFacts.HeadEnd` (the evidence branch's, set on every page whose head ended early) and
  `PageFacts.HeadTagsInBody` (new). Screaming Frog's "Invalid HTML Elements In \<head>" also flags a stray element after
  every head tag, which moves nothing; this doesn't.
- **Plant:** `head-ended-early`: the head ends with `<div id="promo"></div><meta property="og:locale" content="en_GB">`.
  Expected **1**. The existing `canonical-body`, `hreflang-body` and `base-in-body` plants stay clean.
- **From:** Screaming Frog "Page Titles: Outside \<head>", "Meta Description: Outside \<head>", "Directives: Outside
  \<head>", "OG Tags Outside \<head>", "X Cards Outside \<head>", "Invalid HTML Elements In \<head>".

#### 46. `html-structure-errors`: HTML document structure errors

HTML · **Notice**: browsers recover from these the same way every time · Low · P/PD/–

- **Simple:** "Pages whose basic HTML structure is broken"
- **Rule:** the parser's document-structure errors: a second `<head>` start tag, a second `<body>` start tag, an
  `<html>` start tag after content, or a `<head>` start tag after body content began. A missing `<head>` or `<body>` tag
  isn't flagged: HTML makes both optional. Detail: the faults (`2 <head> tags; <html> after content`), grouped by it.
- **Standard:** HTML, tree construction: each is a parse error, and the second tag's attributes are merged or dropped;
  start and end tags of `head` and `body` may be omitted *(neither re-read: the parser section wasn't reached)*.
- **Data:** `PageFacts.StructureErrors` (new), from AngleSharp's `HtmlParser.Error` event. Confirm the error codes
  (such as `HeadTagMisplaced`, `BodyTagMisplaced`, `HtmlTagMisplaced`) in AngleSharp 1.x first.
- **Plant:** `structure-errors`: the head ends `</head><head>`, and the content opens with `<body class="second">`.
  Expected **1**.
- **From:** Screaming Frog Validation "Multiple \<head> Tags", "Multiple \<body> Tags", "\<body> Element Preceding
  \<html>", "\<head> Not First In \<html> Element".

#### 47. `viewport-invalid`: Viewport that disables zoom or sets no width

HTML · **Warning**: no zoom fails people who need larger text; no width lays the page out at desktop width on phones,
which is `viewport-missing`'s Warning · High · P/PD/–

- **Simple:** "Pages people can't zoom, or that don't fit phones"
- **Rule:** a page with a viewport meta (without one, `viewport-missing`) where: `user-scalable` is `no` or `0`;
  `maximum-scale` is under 2; there's neither `width=` nor `initial-scale=`; or two viewport metas differ. Content read
  as comma- or semicolon-separated `key=value`, trimmed, any case. Detail: the faults
  (`user-scalable=no; maximum-scale=1`), grouped by it.
- **Standard:** axe `meta-viewport`: remove `user-scalable=no` and keep `maximum-scale` at 2 or more; WCAG 2.1 SC 1.4.4
  (text resizable to 200%); MDN: `user-scalable=no` stops people with low vision reading the page.
- **Data:** `PageFacts.MetaTags` (viewport). Nothing new.
- **Plant:** `viewport-no-zoom`: `width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no` (a new
  `PageSpec.ViewportContent`). Expected **1**.
- **From:** backlog ("several viewports, a viewport without a width, or one that disables zoom").

#### 48. `charset-invalid`: Character encoding declared wrongly

HTML · **Notice**: the page usually shows right; a wrong guess garbles accents · Low · P/PD/–

- **Simple:** "Pages that name the wrong text encoding"
- **Rule:** a page whose encoding, as declared (a byte-order mark, else the Content-Type header, else the meta), isn't
  UTF-8 (the WHATWG labels `utf-8`, `utf8` and `unicode-1-1-utf-8` all are); or with two meta declarations that differ;
  or whose meta declaration and header name different encodings (the header wins). Detail: one of
  `Declares windows-1252, not UTF-8`, `2 declarations: utf-8, iso-8859-1`,
  `The header says iso-8859-1; the page says utf-8`; grouped by it. *Built, then changed by the real-site audit:* a meta
  declaration after the first 1,024 bytes was flagged too when the header declared the encoding; it was on every page of
  two of the three audited sites (injected by the platform) and changes nothing a browser does, so it isn't.
- **Standard:** HTML: the encoding "must be UTF-8"; only one meta declaration; within the first 1,024 bytes *(read
  through a summary of the spec page)*.
- **Data:** `DeclaredCharset`, `ByteOrderMark`, the header; `PageFacts.CharsetDeclarations` (new: values and the first's
  offset; characters stand for bytes, close enough in the ASCII head).
- **Plant:** `charset-latin1`: header `text/html; charset=iso-8859-1`, meta `utf-8`. Expected **2**: it, and the
  existing `charset-bom-utf16` plant (UTF-16 isn't UTF-8).
- **From:** backlog ("a charset other than UTF-8"; "several charset declarations on one page").

#### 49. `lang-invalid`: Invalid lang attribute

HTML · **Notice**: as `lang-missing`, it's screen readers that suffer · Low · P/PD/–

- **Simple:** "Pages that name their language wrongly"
- **Rule:** a page whose `<html lang>` isn't a well-formed BCP 47 tag: hyphen-separated subtags, a primary language of 2
  or 3 letters (2-letter ones must be ISO 639-1, as `HreflangCodes` checks; 3-letter ones by shape only), then an
  optional 4-letter script, 2-letter or 3-digit region, and further subtags. `en_GB`, `english` and `uk-en` fail.
  Detail: the value, grouped by it.
- **Standard:** HTML: lang "must be a valid BCP 47 language tag, or the empty string"; W3C, choosing a language tag:
  subtags are separated by hyphens.
- **Data:** `PageFacts.Language`. **Evidence:** column `language` (new).
- **Plant:** `lang-invalid`: `<html lang="english">` (a new `PageSpec.LangValue`). Expected **1**.
- **From:** backlog ("an invalid lang").

#### 50. `mobile-alternate`: Separate mobile URL

HTML · **Notice**: Google still supports separate mobile URLs but recommends one responsive page · Low · P/N/–

- **Simple:** "Pages with a separate mobile version"
- **Rule:** a page with `<link rel="alternate" media="…">` (no hreflang) to another URL. Detail:
  `Mobile version: https://m.example.com/x`.
- **Standard:** Google, mobile-first indexing: for separate URLs "use the correct rel=canonical and rel=alternate link
  elements"; responsive design is recommended.
- **Data:** `PageFacts.MobileAlternate` (new).
- **Plant:** `mobile-alternate`. Expected **1**.
- **From:** Screaming Frog Mobile "Mobile Alternate Link".

### HTML: Open Graph and X cards

These ten are in the HTML category, beside `open-graph-missing`. A category of their own ("Social") would read better
in the Issues screen but is a new category for the app to name; it's an owner's choice
([Owner decisions](#owner-decisions)).

*As built:* all ten judge indexable pages only, as `open-graph-missing` does, so a canonicalised variant doesn't repeat
its canonical's faults (53 and 55 didn't say). A share image given as a full URL is a link of a new kind, `socialImage`,
which the crawler fetches as it does images (not with resources off); `MetaTagFact.IsProperty` and those links are page
collection 5, so a crawl parsed before skips `x-card-incomplete` and the three share image checks as earlier-version.

The template gains `<meta name="twitter:card" content="summary_large_image">`, or every page would be flagged.

#### 51. `x-card-missing`: X (Twitter) card missing

HTML · **Notice** · Low · P/N/–

- **Simple:** "Pages without a preview card for X"
- **Rule:** an indexable page with no `twitter:card` meta (by name or property).
- **Standard:** *(unverified: X's documentation refused the fetch, 402 and 404)*. X reads `twitter:card` to choose a card
  and falls back to Open Graph for its title, description and image.
- **Data:** `MetaTags`. **Plant:** `no-x-card` (a new `PageSpec.TwitterCard = false`). Expected **1**.
- **From:** backlog ("Twitter Card"); Screaming Frog "Missing X Cards".

#### 52. `x-card-incomplete`: X (Twitter) card incomplete

HTML · **Notice** · Low · P/PD/–

- **Simple:** "Preview cards for X that are missing parts"
- **Rule:** an indexable page with a `twitter:card` whose card can't be built: its type isn't `summary`,
  `summary_large_image`, `app` or `player`; it has no title (`twitter:title`, else `og:title`); a summary card has no
  image (`twitter:image`, else `og:image`); a player card has no `twitter:player`; or its `twitter:*` tags are written
  only with `property=` (X reads `name=`). Detail: the fault, grouped by it.
- **Standard:** *(unverified, as above)*.
- **Data:** `MetaTags`, `MetaTagFact.IsProperty` (new).
- **Plant:** `x-card-property` (`<meta property="twitter:card" content="summary">` only). Expected **2**: it, and the
  existing `no-open-graph` plant, which has the template's card and nothing to fill it.
- **From:** Screaming Frog "Incomplete X Cards", "X Cards Using Property".

#### 53. `social-image-relative`: Open Graph or X image URL isn't absolute

HTML · **Notice** · Low · P/PD/–

- **Simple:** "Share images given an incomplete address"
- **Rule:** an `og:image` (`og:image:url`, `og:image:secure_url`) or `twitter:image` whose content isn't an absolute
  `http(s)` URL. Detail: the value, grouped by it.
- **Standard:** the Open Graph protocol: URL values use `http://` or `https://` *(that they must be absolute: unverified)*.
- **Data:** `MetaTags`. **Plant:** `og-image-relative` (`og:image` `/img/0.jpg`). Expected **1**.
- **From:** Screaming Frog "Relative OG Image URL", "Relative X Card Image URL".

#### 54. `og-url-not-canonical`: og:url isn't the page's address

HTML · **Notice** · Low · P/PD/–

- **Simple:** "Share links that point to a different page"
- **Rule:** an indexable page (so its canonical is itself) whose `og:url`, normalised, isn't its own URL. Detail:
  `og:url is https://x/`, grouped by it, so a template pointing every `og:url` at the home page is one group.
- **Standard:** the Open Graph protocol: `og:url` is "the canonical URL of your object".
- **Data:** `MetaTags`. **Plant:** `og-url-home` (`og:url` the home page). Expected **1**.
- **From:** Screaming Frog "Non-Canonical OG URL".

#### 55. `social-tags-multiple`: Open Graph or X tag repeated

HTML · **Notice** · Low · P/PD/–

- **Simple:** "Share settings given twice with different values"
- **Rule:** `og:title`, `og:description`, `og:url`, `og:type`, `twitter:card`, `twitter:title` or `twitter:description`
  more than once with different content. `og:image` and the other array properties may repeat. Detail: `2 og:title`.
- **Standard:** the Open Graph protocol (arrays: a property repeated means several values, which only some properties
  take) *(the per-property rule is ours)*.
- **Data:** `MetaTags`. **Plant:** `og-title-twice`. Expected **1**.
- **From:** Screaming Frog "Multiple OG Tags", "Multiple X Cards".

#### 56. `og-title-duplicate`: Duplicate og:title

HTML · **Notice** · Low · P/RU/–

- **Simple:** "Pages that share the same title when shared"
- **Rule:** indexable pages sharing an `og:title` (or a `twitter:title` where set) while their `<title>`s differ: a
  shared `<title>` is `title-duplicate`'s, and the og:title usually follows it. Grouped by the shared text
  (`sharedText`), as `title-duplicate`.
- **Standard:** none needed.
- **Data:** `MetaTags`, in `DuplicateRules` (a hash a page, as for titles).
- **Evidence:** column `title`.
- **Plant:** `og-title-copy-a`, `og-title-copy-b`. Expected **2**. *As built:* revision 1 gives b a share title of its own, for the
  fix loop's tests: a recheck fixes b and can't say a is fixed (5c4aaa7). The share title and description are stored as
  `responses.og_title` and `og_description` for that, and for a cluster's shared text.
- **From:** Screaming Frog "Duplicate OG Title", "Duplicate X Card Title".

#### 57. `og-description-duplicate`: Duplicate og:description

HTML · **Notice** · Low · P/RU/–

- **Simple:** "Pages that share the same summary when shared"
- **Rule:** as `og-title-duplicate`, for `og:description` (or `twitter:description`) where the meta descriptions differ.
  *Changed by the real-site audit:* a page whose share text is its own (title, or meta description) isn't flagged, in
  either check: it's the one the others copied. jitsunami.com's home page was flagged with the six pages Shopify gave
  its description to, which had none of their own.
- **Evidence:** column `description`.
- **Plant:** `og-description-copy-a`, `og-description-copy-b`. Expected **2**.
- **From:** Screaming Frog "Duplicate OG Description", "Duplicate X Card Description".

#### 58. `social-image-broken`: Open Graph or X image broken

HTML · **Notice**: only link previews suffer · Low · P/RU/T

- **Simple:** "Share images that don't load"
- **Rule:** an `og:image` or `twitter:image` that is broken through redirects, or answers 2xx with a type that isn't
  `image/*`. Needs the new `SocialImage` links; skipped with `resources-off`. Detail: `og:image returns 404 Not Found`.
- **Data:** `Links` (kind `SocialImage`, new), `UrlTable`.
- **Evidence:** target facts `statusCode`, `final`, `contentType`.
- **Plant:** `og-image-broken`: `og:image` `/plants/missing-og.jpg`, a 404. Expected **1**; the image adds 1 to
  `internal-client-error`.
- **From:** Screaming Frog "Non-200 OG Image URL", "Invalid OG Image", "Non-200 X Card Image URL", "Invalid X Card Image".

#### 59. `social-image-too-large`: Open Graph or X image over 5 MB

HTML · **Notice** · Low · P/RU/T

- **Simple:** "Share images too big to show"
- **Rule:** a social image over 5 MB. Detail: `og:image is 6.2 MB, over 5 MB`.
- **Standard:** X's limit *(unverified)*; Facebook's is 8 MB.
- **Data:** `SocialImage` links, `UrlStatus.SizeBytes`. **Evidence:** target facts `sizeBytes`, `contentType`.
- **Plant:** `og-image-huge`: a 5.2 MB image, sent with its Content-Length. Expected **1**.
- **From:** Screaming Frog "OG Image Over 5MB", "X Card Image Over 5MB".

#### 60. `social-image-too-small`: Open Graph or X image too small

HTML · **Notice** · Low · P/RU/T

- **Simple:** "Share images too small to show well"
- **Rule:** an `og:image` under 200 × 200 px; or the image X uses (`twitter:image`, else `og:image`) under 300 × 157 for
  `summary_large_image` or 144 × 144 for `summary`. Needs the pixel size (the evidence branch's image reader). The
  aspect ratio isn't judged: both networks crop. Detail: `og:image is 100 × 100 px, under 200 × 200`.
- **Standard:** Facebook: "The minimum allowed image dimension is 200 x 200 pixels", 1,200 × 630 recommended. X's
  minimums *(unverified)*.
- **Data:** `SocialImage` links, `responses.pixel_width`, `pixel_height`. **Evidence:** target facts `pixelWidth`,
  `pixelHeight`.
- **Plant:** `og-image-small`: a real 100 × 100 PNG. Expected **1**.
- **From:** Screaming Frog "OG Image Incorrect Size or Ratio", "X Card Image Incorrect Size or Ratio" (size only).

### Hreflang

#### 61. `hreflang-inconsistent-code`: Hreflang return link uses another code

Hreflang · **Warning**: a pair of pages that disagree about which language each is may be ignored · Medium · P/RU/T

- **Simple:** "Language versions that disagree about which language they are"
- **Rule:** an indexable page A naming page B under code X, where B's entry for itself uses another code Y (any case).
  The return link exists; without one it's `hreflang-missing-return`. Detail:
  `Names /fr/ as “fr”; /fr/ calls itself “fr-ca”`.
- **Standard:** Google, localized versions: "Each language version must list itself as well as all other language
  versions". That the codes must agree *(unverified)*.
- **Data:** `HreflangRules`' sorted alternates carry the code (a code id beside each pair).
- **Evidence:** target facts `statusCode`, `text` (the code), `indexability`.
- **Plant:** `hreflang-code-a` (en itself, fr `hreflang-code-b`, x-default itself) and `hreflang-code-b` (en
  `hreflang-code-a`, fr-CA itself, x-default `hreflang-code-a`). Expected **1** (a).
- **From:** Screaming Frog Hreflang "Inconsistent Language & Region Confirmation Links".

#### 62. `hreflang-header-mismatch`: Hreflang in the HTML and the HTTP header disagree

Hreflang · **Notice** · Low · P/N/–

- **Simple:** "Language versions listed two different ways"
- **Rule:** a page with hreflang both in its head and in a `Link` header, whose sets (code and URL) differ. Detail:
  `The HTML names 3 versions, the header 2; only in the HTML: de`.
- **Standard:** Google: the head, the header and the sitemap are equivalent ways to give the same set *(not re-read)*.
- **Data:** `HreflangFact.FromHttpHeader`. Nothing new.
- **Plant:** `hreflang-header-a` and `-b`, a pair whose header on a leaves out x-default. Expected **1**.
- **From:** backlog ("the HTML and the HTTP header disagree").

#### 63. `hreflang-unlinked`: Hreflang URL that no page links to

Hreflang · **Notice** · Low · U/N/S

- **Simple:** "Language versions that nothing links to"
- **Rule:** an indexable page an analysed page names as an hreflang alternate, with no `<a href>` inlinks and no sitemap
  entry. Detail: `Only hreflang leads here`. *As built:* P, as `canonical-unlinked`; the detail is
  `No link or sitemap entry leads here`, as a redirect, canonical or refresh may reach it. The crawl doesn't follow hreflang
  by default, so a version only hreflang leads to is never read: where an hreflang target was left unread for that, the
  check is skipped with a new reason, `hreflang-off` (flagging the unread URL was tried, and broke the rule that what a
  check flags is a share of the URLs crawled).
- **Evidence:** columns `inlinks`, `linkingPages`; its found-on lists the hreflang entries naming it.
- **Plant:** `hreflang-only-a` (linked) and `hreflang-only-b` (not linked, not in the sitemap; also `not-in-sitemap`),
  naming each other. Expected **1**. *As built:* a linked URL, `hreflang-only-go`, redirects to b, so the default crawl
  reads it.
- **From:** Screaming Frog "Unlinked Hreflang URLs".

#### 64. `hreflang-lang-mismatch`: Hreflang code and lang attribute differ

Hreflang · **Notice**: Google reads neither for the language, so it's only a sign one of them is wrong · Low · P/PD/–

- **Simple:** "Pages that name two different languages"
- **Rule:** an indexable page whose self-referencing hreflang code and `<html lang>` (both present and valid) have
  different primary languages; x-default is ignored. Detail: `hreflang en, lang fr`, grouped by it.
- **Standard:** Google: "Google doesn't use hreflang or the HTML lang attribute to detect the language of a page".
- **Evidence:** column `language` (new).
- **Plant:** `hreflang-lang-a` (lang `fr`, itself `en`) and `hreflang-lang-b`, a pair. Expected **1**.
- **From:** Screaming Frog "Mismatch with HTML Lang Attribute".

#### 65. `hreflang-self-only`: Hreflang that names only the page itself

Hreflang · **Notice**: harmless, and does nothing · Low · P/N/–

- **Simple:** "Language settings that list no other language"
- **Rule:** a page whose honoured hreflang entries all name the page itself (x-default included).
- **Data:** `Hreflangs`. Nothing new.
- **Plant:** none new: the existing `hreflang-bad-code` and `hreflang-no-x-default` plants name only themselves. Expected **2**.
- **From:** Screaming Frog Hreflang "Self Reference Only".

### Security

#### 66. `security-headers-weak`: Security headers set too weakly

Security · **Notice**, as `security-headers-missing` · Low · P/N/– (reported once)

- **Simple:** "Security settings that are on but too weak"
- **Rule:** on the home page, as `security-headers-missing`: `Strict-Transport-Security` (on HTTPS) whose `max-age` is
  missing, unreadable, 0 or under 31,536,000 (a year); or `X-Content-Type-Options` other than `nosniff`. Detail:
  `HSTS max-age 86400 (1 day), under a year; X-Content-Type-Options “none”`.
- **Standard:** hstspreload.org: "The max-age must be at least 31536000 seconds (1 year)"; MDN: browsers ignore HSTS
  over HTTP; `nosniff` is the header's one directive.
- **Data:** `Headers`. Nothing new.
- **Plant:** none: the synthetic home page must pass. A rule test. Expected **0**.
- **From:** backlog ("weak HSTS"; "the nosniff value").

#### 67. `cookies-insecure`: Cookies without Secure or HttpOnly

Security · **Notice** · Low · P/PD/–

- **Simple:** "Cookies your site sets without basic protection"
- **Rule:** a page whose response sets a cookie that, on HTTPS, lacks `Secure`; has `SameSite=None` without `Secure`
  (browsers refuse it); or lacks `HttpOnly` while its name looks like a session or login cookie (contains `sess`, `sid`,
  `auth`, `token`, `login` or `jwt`). Detail: `sessionid: no Secure, no HttpOnly`, grouped by it.
- **Standard:** MDN, `Set-Cookie`: Secure, HttpOnly, and "The Secure attribute must also be set" with `SameSite=None`.
  The session-name list is ours.
- **Data:** `Headers` (`set-cookie`). First prove, by a test, that the fetcher's cookie handling leaves `Set-Cookie` in
  the headers it keeps.
- **Plant:** `insecure-cookie`: `Set-Cookie: sessionid=planted; Path=/`. Expected **1**.
- **From:** backlog ("insecure cookies").

#### 68. `form-not-secure`: Form sends data over HTTP

Security · **Warning**: what people type travels unencrypted, and browsers warn them · Medium · P/PD/–

- **Simple:** "Forms that send what people type without encryption"
- **Rule:** on an HTTPS page, a `<form>` whose action resolves to `http://`; on an HTTP page, a form with a password
  field. Detail: `Sends to http://x/subscribe`, or `A password form on an http:// page`; grouped by it.
- **Standard:** browsers' mixed-form warnings *(unverified)*.
- **Data:** `PageFacts.Forms` (new).
- **Plant (HTTPS site only, as `mixed-content`):** `form-http-action`: `<form action="http://…/plants/subscribe"
  method="post"><input type="email" name="e"></form>`. Expected **1** on HTTPS, **0** on HTTP.
- **From:** Screaming Frog Security "Form URL Insecure", "Form On HTTP URL".

#### 69. `protocol-relative-links`: Protocol-relative (//) URLs

Security · **Notice** · Low · P/N/–

- **Simple:** "Links written in an old shortcut style"
- **Rule:** a page with any `href` or `src` that starts `//`. Detail: `3 URLs, such as //cdn.example.com/app.js`.
- **Standard:** none read *(unverified)*: on an HTTPS page they load over HTTPS anyway, so the fault is only that an
  HTTP copy of the page would load them insecurely.
- **Data:** `PageFacts.ProtocolRelativeUrls` (new).
- **Plant:** `protocol-relative`: `<script src="//{host}/static/app.js">`. Expected **1**.
- **From:** Screaming Frog Security "Protocol-Relative Resource Links".

### URLs

All but two are page checks on indexable pages, like the three URL checks there are; they're `sources` checks, so their
found-on gives the links to change if a URL is renamed.

#### 70. `url-tracking-parameters`: Internal link with tracking parameters

URLs · **Notice** · Medium · U/N/S

- **Simple:** "Links between your own pages tagged for ad tracking"
- **Rule:** an internal URL an analysed page links to with `<a href>` whose query has `utm_source`, `utm_medium`,
  `utm_campaign`, `utm_term`, `utm_content`, `utm_id`, `gclid`, `fbclid`, `msclkid`, `mc_cid` or `_ga`. Detail: the
  parameters (`utm_source, utm_medium`).
- **Standard:** Google Analytics frames UTM parameters as for "referral links and ad campaigns"; that they shouldn't be on
  internal links isn't said there *(unverified)*.
- **Plant:** `utm-link` links `/plants/thin?utm_source=newsletter`, which canonicalises to `/plants/thin`. Expected
  **1**; it adds 1 to `canonicalised-page`.
- **From:** Screaming Frog URL "GA Tracking Parameters".

#### 71. `url-multiple-slashes`: URL with multiple slashes

URLs · **Notice** · Low · P/N/S

- **Simple:** "Addresses with a double slash"
- **Rule:** an indexable page whose path has `//`. **Standard:** none (it's a different URL from the single-slash one).
- **Plant:** `double//slash`. Expected **1**. Check the normaliser keeps `//` in a path.
- **From:** Screaming Frog URL "Multiple Slashes".

#### 72. `url-space`: URL with a space

URLs · **Notice** · Low · P/N/S

- **Simple:** "Addresses with spaces in them"
- **Rule:** an indexable page whose path, decoded, has a space. **Standard:** RFC 3986 (a space must be escaped) *(not re-read)*.
- **Plant:** `with space`. Expected **1**.
- **From:** Screaming Frog URL "Contains A Space".

#### 73. `url-parameters`: Indexable URL with parameters

URLs · **Notice** · Low · P/N/S

- **Simple:** "Pages whose address has a ? part"
- **Rule:** an indexable page whose URL has a query, unless `url-internal-search` flags it (exclusive). Detail: the
  parameter names. **Standard:** none (each combination of parameters is another URL to crawl).
- **Plant:** `params?sort=price`, whose canonical names itself with the query. Expected **1**.
- **From:** Screaming Frog URL "Parameters".

#### 74. `url-non-ascii`: URL with non-ASCII characters

URLs · **Notice** · Low · P/N/S

- **Simple:** "Addresses with accented or non-English letters"
- **Rule:** an indexable page whose path or query, decoded, has a character outside ASCII.
- **Standard:** RFC 3986 (URLs are ASCII; other characters are percent-encoded) *(not re-read)*. Search engines handle
  them; some tools and people don't.
- **Plant:** `café`. Expected **1**.
- **From:** Screaming Frog URL "Non ASCII Characters".

#### 75. `url-repetitive-path`: URL with a repeating path

URLs · **Notice**: usually a relative link that builds ever-longer URLs, which a crawler can follow without end · Medium · U/N/S

- **Simple:** "Addresses that repeat themselves"
- **Rule:** an internal URL (page or not) whose path has a segment 3 or more times, or a run of 1 to 3 segments repeated
  back to back (`/shop/shop/`, `/a/b/a/b/`).
- **Standard:** none *(Screaming Frog's name only)*.
- **Plant:** `shop/shop/`. Expected **1**.
- **From:** Screaming Frog URL "Repetitive Path".

#### 76. `url-internal-search`: Internal search results page

URLs · **Notice** · Medium · P/N/S

- **Simple:** "Your site's search results pages show up as pages"
- **Rule:** an indexable page whose path has a segment `search`, or whose query has `s`, `q`, `query`, `search`,
  `keyword` or `keywords`. Exclusive with `url-parameters`.
- **Standard:** *(unverified)*: Google's crawl-budget page read on 2026-11-17 doesn't mention search pages.
- **Plant:** `search?q=sofa`, whose canonical names itself. Expected **1**.
- **From:** Screaming Frog URL "Internal Search".

### Titles, headings and content

#### 77. `title-same-as-h1`: Title the same as the H1

Titles · **Notice**: an opportunity at most; Google even suggests the title's words for the first H1 · Low · P/N/–

- **Simple:** "Search titles that just repeat the main heading"
- **Rule:** an indexable page with one title equal to its first H1, trimmed, spaces collapsed, any case.
- **Standard:** none against it. Google lists the H1 among its title-link sources and suggests "putting the title text
  in the first visible `<h1>` element", so the words must say it's a chance to use the title for other words people
  search with, not a fault.
- **Data:** `Titles`, `Headings`. **Evidence:** columns `title`, `h1`.
- **Plant:** `title-same-as-h1`: title and H1 both "Garden sofas and chairs for every season". Expected **1**.
- **From:** backlog ("an H1 the same as the title"); Screaming Frog Page Titles "Same as H1".

#### 78. `heading-levels-skipped`: Heading levels skipped

Headings · **Notice** · Low · P/PD/–

- **Simple:** "Headings that jump levels"
- **Rule:** an indexable page with an H1 where a heading is more than one level deeper than the one before it (H2 then
  H4), or the first heading isn't an H1. Pages without an H1 are `h1-missing`'s. Detail: the first jump (`H2 → H4`, or
  `An H3 before the H1`), grouped by it. *Ruled after the real-site audit:* the "first heading isn't an H1" case is
  dropped; the sequence is judged from the first H1 on. The headings it caught before the H1 (a theme's hidden cart
  heading on 7 of jitsunami.com's pages; a mega-menu's column titles and a header line on 19 of
  gardenfurnituresets.co.uk's) skip no level of the page's outline, the HTML rule cited doesn't forbid them, and they
  gave the owner flags they can't act on. Detail: the first jump, `H2 → H4`.
- **Standard:** HTML: a heading after another "must have a heading level that is less than, equal to, or 1 greater
  than" the one before *(read through a summary of the spec page; its non-conforming example is an h1 then an h3)*.
- **Data:** `Headings` (in order, kept to 500).
- **Plant:** `heading-skip`: an `<h3>` straight after the H1. Expected **1**. *As built:* `heading-before-h1` (an `<h6>`
  before the H1, then an H2) is clean, for the ruling; still **1**.
- **From:** backlog ("skipped heading levels"); Screaming Frog H1 and H2 "Non-sequential".

#### 79. `h1-image-only`: H1 is an image

Headings · **Notice** · Low · P/N/–

- **Simple:** "Main headings that are pictures"
- **Rule:** an indexable page whose first H1 has no text of its own and takes its text from an image's alt. Detail:
  `The H1 is an image: “Synthetic Store logo”`.
- **Standard:** none *(Screaming Frog's name only)*.
- **Data:** `HeadingFact.FromImageAlt` (new). **Evidence:** column `h1`.
- **Plant:** `h1-image`: the H1 holds only `<img src="/img/14.jpg" alt="Synthetic Store logo">`. Expected **1**.
- **From:** Screaming Frog H1 "Alt Text in h1".

#### 80. `heading-too-long`: H1 or H2 over 70 characters

Headings · **Notice** · Low · P/N/–

- **Simple:** "Headings that run on"
- **Rule:** an indexable page whose first H1 or any H2 is over 70 characters (setting `HeadingMaxLength`). Detail:
  `H1 of 84 characters, over 70`.
- **Standard:** none; Screaming Frog's default of 70.
- **Evidence:** column `h1`. **Plant:** `heading-long` (an 80-character H1). Expected **1**.
- **From:** Screaming Frog H1 and H2 "Over 70 Characters".

#### 81. `h2-missing`: Long page without an H2

Headings · **Notice** · Low · P/N/–

- **Simple:** "Long pages with no subheadings"
- **Rule:** an indexable page with 500 or more main-content words (setting `H2MinWords`) and no H2. Detail:
  `640 words, no H2`.
- **Standard:** none; ours. Screaming Frog flags every page without one, which a short page doesn't need.
- **Evidence:** column `wordCount`.
- **Plant:** `no-subheadings` (600 words). Expected **1**. The existing `content-near-a` and `-b` plants (600 words each)
  gain the same `<h2>`, so they stay clean and still near copies.
- **From:** Screaming Frog H2 "Missing".

#### 82. `placeholder-text`: Placeholder text (lorem ipsum)

Content · **Warning**: every visitor sees it, and it reads as an unfinished site · Medium · P/N/–

- **Simple:** "Pages with dummy “lorem ipsum” text"
- **Rule:** an indexable page whose main content, title or meta description has "lorem ipsum" (any case). Detail: the
  quote (`“Lorem ipsum dolor sit amet…”`).
- **Standard:** none needed.
- **Data:** `PageFacts.PlaceholderText` (new), `Titles`, `MetaDescriptions`.
- **Plant:** `lorem-ipsum`. Expected **1**.
- **From:** Screaming Frog Content "Lorem Ipsum Placeholder".

#### 83. `readability-low`: Text that's very hard to read

Content · **Notice** · Low · P/N/–

- **Simple:** "Writing that's very hard to read"
- **Rule:** an indexable English page (`<html lang>` `en` or `en-*`) whose main text has at least 100 words and 5
  sentences, with a Flesch reading ease under 30 (setting `ReadabilityMinScore`): 206.835 − 1.015 × words per sentence
  − 84.6 × syllables per word, syllables by an English vowel-group count. Text without sentence ends (menus, product
  grids) isn't prose and isn't judged. Detail: `Reading ease 22, under 30 (very difficult)`.
- **Standard:** the Flesch formula (1948); Screaming Frog's bands ("Difficult", "Very Difficult") couldn't be read. 30,
  the line between "difficult" and "very difficult" on Flesch's scale, is ours.
- **Data:** `PageFacts.SentenceCount`, `SyllableCount` (new), `WordCount`, `Language`.
- **Plant:** `hard-to-read`: six long sentences of long words. Expected **1**.
- **From:** Screaming Frog Content "Readability Difficult", "Readability Very Difficult".

#### 84. `boilerplate-heavy`: Page mostly template text

Content · **Notice** · Low · P/N/–

- **Simple:** "Pages that are mostly menus and footers"
- **Rule:** an indexable page that `low-word-count` doesn't flag, whose main-content words are under 25% of all its
  words (menus, header, footer and sidebars included). Detail: `210 of 940 words are the page's own (22%)`.
- **Standard:** none; the 25% is ours.
- **Data:** `PageFacts.TotalWordCount` (new), `WordCount`.
- **Plant:** `boilerplate`: 210 words of content beside a 750-word `<nav>`. Expected **1**.
- **From:** backlog ("boilerplate ratio").

## Changes to existing checks

What the sources cover in part, or what a new check takes from an old one. No new id; each changes what an existing
check stores, so the batch that makes it bumps `AnalysisFreshness.VERSION`.

| # | Check | Change | Plant and count | From |
| --- | --- | --- | --- | --- |
| C1 | `meta-refresh` | Also a `Refresh` response header, read as the meta tag is (`MetaRefreshFact.FromHttpHeader`, new): detail `The Refresh header moves to https://x/ after 5 seconds`; an instant one makes the page a redirect, as an instant meta refresh does (`UrlTable`'s `instantRefreshes`). | `refresh-header` (header `Refresh: 0; url=/plants/thin`, not in the sitemap): `meta-refresh` **+1** | Screaming Frog "Internal Redirection (HTTP Refresh)" |
| C2 | Redirect checks and `links-to-redirects` | A new URL column and target fact, `redirectKind`: for a redirect, what it changes (HTTP to HTTPS, HTTPS to HTTP, adds www, removes www, adds a trailing slash, removes one, changes case, to another host, other). Shown in the redirect checks' rows, `links-to-redirects`' targets, and the URL grid. No flag changes. | None (a reader test over the existing redirect plants) | Backlog "a list of every redirect with its kind"; Screaming Frog "Internal Redirection (3XX)" (in part) |
| C3 | `orphan-page` | Also an indexable sitemap page whose only internal links are nofollow: detail `Only nofollow links lead here`. Google treats nofollow as a hint, so such a page is close to unlinked. | `nofollow-link` (links `nofollow-only` with `rel="nofollow"`; `internal-nofollow-links`) and `nofollow-only` (in the sitemap): `orphan-page` **+1**, `internal-nofollow-links` **+1** | Screaming Frog "Internal Nofollow Inlinks Only" |
| C4 | `sitemap-url-other-host` | Also a URL on another scheme than its sitemap (an `http://` URL in an `https://` sitemap), which sitemaps.org drops; detail `The URL is http://, the sitemap https://`. Clears a known limitation in [v2-rebuild.md](v2-rebuild.md#known-limitations). | None new: on the HTTPS site the existing `http-page` plant, listed by its `http://` URL, **+1** | v2-rebuild.md, known limitations |
| C5 | `structured-data-invalid`, `structured-data-missing-required` (and checks 36 to 38) | Read Microdata as well as JSON-LD: `PageWalker` turns each top-level `itemscope` item into a JSON-LD-shaped string (`PageFacts.Microdata`), so the rules are unchanged. A Microdata item has no syntax to break, so `structured-data-invalid` gains nothing from it. | `microdata-incomplete` (a Product in Microdata without offers, review or rating): `structured-data-missing-required` **+1** | Backlog "Microdata"; Screaming Frog Structured Data "with Microdata" |
| C6 | `canonicalised-page` | Pages `canonical-to-first-page` or `canonical-other-domain` flag leave it, as `noindex-with-canonical`'s do. | None on the synthetic site today; gardenfurnituresets.co.uk's three page-2s move | Checks 7 and 8 |
| C7 | `canonical-to-non-indexable` | Pages on a canonical loop leave it for `canonical-loop`. | None today | Check 9 |
| C8 | `title-too-long`, `title-too-short`, `description-too-long`, `description-too-short` | **At the owner's word.** Measure pixels instead of characters: a bundled table of Arial's advance widths, titles at 20 px and descriptions at 14 px, full-width characters and emoji at one em; limits over 561 px and under 200 px for titles, over 985 px and under 400 px for descriptions (Screaming Frog's numbers; Google publishes none and says only that it truncates). New settings in pixels replace the character ones, and the plain words' `{limit:…}` tokens follow. Every site's counts move. | The title and description plants are re-measured; `title-wide` and `title-wide-long` keep proving full-width text | Backlog "title and description widths in pixels"; Screaming Frog "Over 561 Pixels", "Below 200 Pixels", "Over 985 Pixels", "Below 400 Pixels" (in part) |

## Not built, and why

Of the backlog and the 100 from the HTML and headers: **27**. The other 170 of Screaming Frog's need what we don't have
and stay where [the comparison](v2-comparison.md#theirs-we-lack-needing-something-we-dont-have-170) put them: a browser
(113: step 4, then the 92 accessibility rules), Lighthouse, Google Analytics or Search Console (37: step 5), AMP (16:
not doing), an AI key (2: not doing) or a dictionary (2: later).

| Item | Source | Why not |
| --- | --- | --- |
| Unsafe Cross-Origin Links | Screaming Frog | Obsolete: browsers now treat `target="_blank"` as `noopener`, and their own text says it matters only to old browsers |
| NoODP | Screaming Frog | Obsolete: DMOZ, the directory it addressed, closed in 2017 |
| NoYDIR | Screaming Frog | Obsolete: the Yahoo Directory closed in 2014 |
| `noarchive` | Backlog | Google lists it under "historical and other unused rules": cached links no longer exist |
| Missing Alt Text (an empty alt) | Screaming Frog | Would flag correct markup: an empty alt is how a decorative image is marked. A missing alt is `image-missing-alt` |
| Missing Image Title; the image title attribute | Screaming Frog; backlog (parser changes) | Neither search engines nor screen readers rely on it; the old app's title heuristics are on the dropped list |
| H2 Multiple | Screaming Frog | Not a fault: a page has as many sections as it needs |
| H2 Duplicate | Screaming Frog | Not a fault, and a template's section headings ("Related products") would flag every page |
| Structured Data Missing | Screaming Frog | Most pages need none |
| High Carbon Rating | Screaming Frog | Needs a browser: it weighs every file a page loads, and no standard defines it |
| Missing \<head> Tag, Missing \<body> Tag | Screaming Frog | Valid HTML: both tags are optional, and the parser makes them |
| Hreflang Not Using Canonical | Screaming Frog | A duplicate: `hreflang-to-non-indexable` flags an alternate that's canonicalised elsewhere, with the canonical as its target fact; a page's own entries are judged only when it's indexable, so canonical |
| Follow & Nofollow Internal Inlinks To Page | Screaming Frog | A duplicate: `internal-nofollow-links` flags every nofollow internal link, grouped by the page linked to, and the URL grid's `followedInlinks` and `nofollowInlinks` give the mix |
| Canonicals Multiple (covered in part) | Screaming Frog | No change: `canonical-multiple` flags canonicals that differ; the same one twice is harmless |
| Incomplete OG Tags (covered in part) | Screaming Frog | No change: `open-graph-missing` checks the three that change a preview; `og:type` and `og:url` are inferred when absent (`og:url` is judged by check 54 when present) |
| Permissions-Policy | Backlog | MDN marks it experimental and "not Baseline"; nothing needs it, so its absence isn't a fault |
| SVGs with pixel dimensions | Backlog | Contradicts `image-missing-dimensions`: width and height on every `<img>`, SVG included, is what stops layout shift |
| A high share of 5xx responses | Backlog | Covered: `internal-server-error` shows its share of the URLs crawled ("% of Total", round 5) |
| HowTo requirements | Backlog | Google removed HowTo rich results in September 2023 *(read through a summary of its changelog)* |
| FAQ answers missing or too short | Backlog | Google ended FAQ rich results: "This feature will no longer appear in Google Search starting May 7, 2026" |
| Sitemap priority and changefreq | Backlog (parser changes) | "Google ignores `<priority>` and `<changefreq>` values". `lastmod` is built (check 35) |
| JavaScript redirects | Backlog (parser changes) | Needs a browser: step 4, as the backlog says |
| Title "action words" and separators | Backlog, dropped | Stays dropped |
| Meta keywords | Backlog, dropped | Stays dropped |
| "Potentially decorative" images, redundant title attributes, alt-text "bad patterns" | Backlog, dropped | Stays dropped. Check 42 takes only what the backlog kept: alt text that's very short or a file name |
| Heuristics for contact details, NAP and author markup | Backlog, dropped | Stays dropped |
| An H1 "unrelated" to the title | Backlog, dropped | Stays dropped |

## Where each source item went

### The backlog

| Backlog item | Ours |
| --- | --- |
| Insecure cookies | 67 `cookies-insecure` |
| Weak HSTS; the nosniff value | 66 `security-headers-weak` |
| Permissions-Policy | Not built |
| X-Robots-Tag in conflict with meta robots | 1 `robots-directives-conflict` |
| noindex two clicks or fewer from the start | 2 `noindex-near-home` |
| noarchive | Not built |
| noimageindex, max-image-preview, max-video-preview | 4 `media-preview-blocked` |
| unavailable_after | 3 `unavailable-after-passed` |
| indexifembedded | 6 `indexifembedded-without-noindex` |
| Temporary redirects cached for more than a day | 28 `redirect-temporary-cached` |
| Every redirect with its kind | C2 `redirectKind` |
| Canonicals to another domain | 8 `canonical-other-domain` |
| Paginated pages canonicalised to page 1 | 7 `canonical-to-first-page` |
| Canonical loops | 9 `canonical-loop` |
| Canonicals on a redirect | 10 `canonical-on-redirect` |
| Several viewports, no width, zoom disabled | 47 `viewport-invalid` |
| A charset other than UTF-8; several charset declarations | 48 `charset-invalid` |
| An invalid lang | 49 `lang-invalid` |
| Twitter Card | 51 `x-card-missing` (and 52) |
| Title and description widths in pixels | C8 (owner's word) |
| An H1 the same as the title | 77 `title-same-as-h1` |
| Skipped heading levels | 78 `heading-levels-skipped` |
| A missing src | 39 `image-missing-src` |
| No srcset | 40 `image-no-srcset` |
| Legacy formats | 41 `image-legacy-format` |
| Hotlinked images | 43 `image-hotlinked` |
| SVGs with pixel dimensions | Not built |
| Alt text very short or a file name | 42 `image-alt-unhelpful` |
| No outlinks, or few | 17 `few-internal-outlinks` |
| Generic anchor text | 18 `generic-anchor-text` |
| Slow external links | 22 `slow-external-links` |
| Broken CSS, JavaScript or iframes | 15 `broken-resources` |
| A high share of 5xx | Not built (covered) |
| Hreflang: HTML and header disagree | 62 `hreflang-header-mismatch` |
| Empty sitemaps | 33 `sitemap-empty` |
| Sitemaps at or near 50,000 | 31 `sitemap-over-limit`, 32 `sitemap-near-limit` |
| Article, Organization, HowTo requirements | 37 `structured-data-missing-recommended` (HowTo not built) |
| Rich results on a noindex page | 38 `structured-data-on-noindex` |
| Missing or invalid `@context`; invalid dates and currencies | 36 `structured-data-schema-errors` |
| FAQ answers missing or short | Not built |
| Microdata | C5 |
| Sitemap lastmod; priority and changefreq; file size | 35 `sitemap-lastmod-invalid`; not built; 31 and 32 |
| Data-URI images; the image title attribute | 44 `image-inline-large`; not built |
| Boilerplate ratio | 84 `boilerplate-heavy` |
| JavaScript redirects | Not built (step 4) |
| The real-site audit: robots.txt doesn't name the sitemap | 30 `robots-txt-no-sitemap` |

### Screaming Frog's 100

| Their group | Their issue → ours |
| --- | --- |
| Response Codes | Internal Redirection (HTTP Refresh) → C1 |
| Security | Form URL Insecure, Form On HTTP URL → 68; Bad Content Type → 27; Protocol-Relative Resource Links → 69; Unsafe Cross Origin Links → not built |
| URL | Multiple Slashes → 71; Contains A Space → 72; Parameters → 73; GA Tracking Parameters → 70; Non ASCII Characters → 74; Repetitive Path → 75; Internal Search → 76 |
| Page Titles | Outside \<head> → 45; Same as H1 → 77 |
| Meta Description | Outside \<head> → 45 |
| H1 | Non-sequential → 78; Alt Text in h1 → 79; Over 70 Characters → 80 |
| H2 | Missing → 81; Non-sequential → 78; Over 70 Characters → 80; Multiple, Duplicate → not built |
| Content | Lorem Ipsum Placeholder → 82; Readability Difficult, Very Difficult → 83 |
| Images | Missing Alt Text, Missing Image Title → not built |
| Canonicals | Invalid Attribute In Annotation → 12; Contains Fragment URL → 11; Unlinked → 13 |
| Pagination | Not In Anchor Tag, Unlinked Pagination URLs → 23; Non-200 → 24; Non-Indexable → 25; Multiple, Loop, Sequence Error → 26 |
| Directives | Outside \<head> → 45; NoImageIndex → 4; Unavailable_After → 3; NoTranslate → 5; NoODP, NoYDIR → not built |
| Hreflang | Inconsistent Language & Region Confirmation Links → 61; Self Reference Only → 65; Unlinked Hreflang URLs → 63; Mismatch with HTML Lang Attribute → 64; Not Using Canonical → not built (duplicate) |
| Links | Pages Without Internal Outlinks → 17; Non-Indexable Page Inlinks Only → 19; Internal Nofollow Inlinks Only → C3; High Crawl Depth → 20; Non-Descriptive Anchor Text → 18; Uncrawlable Internal Outlinks, Empty Anchor Tags → 14; Outlinks To Localhost → 16; High External Outlinks, High Internal Outlinks → 21; Follow & Nofollow Internal Inlinks → not built (duplicate) |
| Structured Data | Validation Errors → 36; Validation Warnings, Rich Result Validation Warnings → 37; with Microdata → C5; Missing → not built |
| Social | Missing X Cards → 51; Incomplete X Cards, X Cards Using Property → 52; Non-200 and Invalid OG and X images → 58; Relative OG and X image URLs → 53; Non-Canonical OG URL → 54; OG and X images over 5 MB → 59; OG Tags and X Cards Outside \<head> → 45; Duplicate OG and X titles → 56; Duplicate OG and X descriptions → 57; Multiple OG Tags, Multiple X Cards → 55; OG and X Image Incorrect Size or Ratio → 60 (size, not ratio) |
| Sitemaps | Over 50k URLs, Over 50mb → 31 (and 32); URLs In Multiple Sitemaps → 34 |
| Validation | Multiple \<head>, Multiple \<body>, \<body> Preceding \<html>, \<head> Not First → 46; Invalid HTML Elements In \<head> → 45; Missing \<head>, Missing \<body> → not built; Resource Over 2MB → 29; High Carbon Rating → not built |
| Mobile | Mobile Alternate Link → 50 |

And the comparison's "In part (7)": Internal Redirection (3XX) → C2; the four pixel widths → C8; Canonicals Multiple and
Incomplete OG Tags → not built, covered.

## Build batches

Nine batches, the most valuable first, each grouped by the rules files it touches. **Run them one after another**, not
side by side: every batch adds to `IssueCatalog.cs`, `plain-words.json` (and the generated `IssuePlainTexts.cs`),
`SyntheticSite.Plants.cs` and the expectations, and most add to `PageWalker`; in sequence each rebases on a clean tree.

**Before batch 1:** `ui/3b-evidence` is merged. It brings `IssueDefinition.Evidence` and `TargetFacts`, `HeadEnd`, the
image pixel sizes and the synthetic site's real image bytes, the image checks' targets, and the `RuleText` helpers
these entries use. The accuracy branch's words should be in too, so the plain words aren't written twice.

**Each batch, for each check:** the catalog entry (What, Why, Fix, LearnMore, `Grouping`, `Subject`, `References`,
`Evidence`, `TargetFacts`), its plain words, its skip reasons, the plant and its expectation, a rule test seen failing
first and then passing, the whole-table test, `verify-engine.sh` (the 100,000-page bench, memory flat), a row in
[v2-evidence.md](v2-evidence.md)'s per-check table, and the status in [v2-rebuild.md](v2-rebuild.md). Local only:
nothing is pushed.

| # | Batch | Checks | Files | Parse and storage | Value |
| --- | --- | --- | --- | --- | --- |
| 1 | **Indexing and canonicals** | 1 `robots-directives-conflict`, 2 `noindex-near-home`, 3 `unavailable-after-passed`, 4 `media-preview-blocked`, 5 `translation-blocked`, 6 `indexifembedded-without-noindex`, 7 `canonical-to-first-page`, 8 `canonical-other-domain`, 9 `canonical-loop`, 10 `canonical-on-redirect`, 11 `canonical-fragment`, 12 `canonical-invalid-attribute`, 13 `canonical-unlinked`; changes C1, C6, C7 | `IndexabilityRules`, `CanonicalRules`, a new site rule `CanonicalGraphRules` (9, 13), `HtmlPageParser` (the Refresh header) | `CollectedVersion`, `Pagination`, `CanonicalFact.Attributes`, `MetaRefreshFact.FromHttpHeader`; `AnalyzedPage.FetchedAt`; the redirect-headers reader | High |
| 2 | **Links and crawlability** | 14 `uncrawlable-links`, 15 `broken-resources`, 16 `links-to-localhost`, 17 `few-internal-outlinks`, 18 `generic-anchor-text`, 19 `non-indexable-inlinks-only`, 20 `deep-page`, 21 `too-many-links`, 22 `slow-external-links`, 23 to 26 the pagination checks; change C3 | `LinkRules`, `InlinkRules`, a new site rule `LinkGraphRules` (19, 20), a new `PaginationRules` | `UncrawlableLinks`; `UrlStatus.ResponseMs`; the partner site's slow page; `PageSpec.Chrome` | High |
| 3 | **The head and served files** | 45 `head-tags-in-body`, 46 `html-structure-errors`, 47 `viewport-invalid`, 48 `charset-invalid`, 49 `lang-invalid`, 50 `mobile-alternate`, 27 `content-type-mismatch`, 28 `redirect-temporary-cached`, 29 `resource-past-crawl-limit` | `HtmlRules`, `ResponseCodeRules`, `RedirectRules`, `PerformanceRules`, `HtmlPageParser` (the parser's error event) | `HeadEnd` on every page, `HeadTagsInBody`, `StructureErrors`, `CharsetDeclarations`, `MobileAlternate`; `UrlStatus` content class; the `language` column | High to Medium |
| 4 | **Sitemaps and robots.txt** | 30 `robots-txt-no-sitemap`, 31 `sitemap-over-limit`, 32 `sitemap-near-limit`, 33 `sitemap-empty`, 34 `sitemap-url-repeated`, 35 `sitemap-lastmod-invalid`; changes C2, C4 | `SitemapRules`, `RobotsTxtRules`, the sitemap reader, `UrlColumns` | `sitemaps.size_bytes`, `sitemap_urls.lastmod`; the `redirectKind` column; `SyntheticSiteOptions.SitemapInRobots` | Medium |
| 5 | **Structured data** | 36 `structured-data-schema-errors`, 37 `structured-data-missing-recommended`, 38 `structured-data-on-noindex`; change C5 | `StructuredDataRules`, `RichResultRequirements`, `PageWalker` (Microdata) | `Microdata`; the schema.org vocabulary and ISO 4217 data with their generator | Medium |
| 6 | **Images** | 39 `image-missing-src`, 40 `image-no-srcset`, 41 `image-legacy-format`, 42 `image-alt-unhelpful`, 43 `image-hotlinked`, 44 `image-inline-large` | `ImageRules` (after the evidence branch's targets) | `ImageFact.IsDataUri`, `DataUriBytes`, `InPicture`; the CDN list | Medium to Low |
| 7 | **Security and URLs** | 66 `security-headers-weak`, 67 `cookies-insecure`, 68 `form-not-secure`, 69 `protocol-relative-links`, 70 to 76 the URL checks | `SecurityRules`, `UrlRules` | `Forms`, `ProtocolRelativeUrls` | Medium to Low |
| 8 | **Text and languages** | 77 `title-same-as-h1`, 78 `heading-levels-skipped`, 79 `h1-image-only`, 80 `heading-too-long`, 81 `h2-missing`, 82 `placeholder-text`, 83 `readability-low`, 84 `boilerplate-heavy`, 61 to 65 the hreflang checks | `TitleRules`, `HeadingRules`, `ContentRules`, `HreflangRules`, `TextAccumulator` | `HeadingFact.FromImageAlt`, `PlaceholderText`, `SentenceCount`, `SyllableCount`, `TotalWordCount` | Medium to Low |
| 9 | **Open Graph and X cards** | 51 to 60 | A new `SocialRules`, `DuplicateRules` (56, 57), the crawler (the new link kind) | `MetaTagFact.IsProperty`; `LinkKind.SocialImage` | Low |
| – | **Pixel widths, at the owner's word** | Change C8 | `DisplayWidth`, `TitleRules`, `DescriptionRules`, `AnalysisSettings` | An Arial width table | Medium |

Sizes: 13 checks and 3 changes, then 13 and 1, 9, 6 and 2, 3 and 1, 6, 11, 13, 10: 84 checks and 7 changes, and C8.

## What changes existing flags and the bench

The whole-table tests compare every issue the analysis stores, and the bench must find every planted page and issue
and nothing else. These change what they expect.

- **Existing plants that gain a flag** (each a correct flag, written into their `Issues`):
  - `charset-bom-utf16` → `charset-invalid` (UTF-16 isn't UTF-8);
  - `image-large` → `image-legacy-format` (a 300 KB JPEG), and `image-no-srcset` if the evidence branch makes it a
    real image 800 px or wider;
  - `no-open-graph` → `x-card-incomplete` (the template's card with no title or image to fall back on);
  - `hreflang-bad-code`, `hreflang-no-x-default` → `hreflang-self-only`;
  - `http-page` (HTTPS site) → `sitemap-url-other-host` (change C4).
- **Existing pages changed so they stay clean:** the template gains `twitter:card`; the products' JSON-LD gains `image`
  and `availability`; `series` and `series-2` gain `<a>` links to each other; `content-near-a` and `-b` gain the same
  `<h2>`.
- **Flags that aren't planted but computed** (the expectations work them out from the generated site, as they do the
  orphans):
  - `noindex-near-home`: every noindex product a category page or the home page lists (all of them on the 1,000-page
    site; with `HiddenFromSearch`, every listed product);
  - `structured-data-on-noindex`: every noindex product with Product JSON-LD;
  - `deep-page`: at 100,000 pages, every product more than 4 clicks deep, likely thousands; none on the 1,000-page site,
    whose category pages list every product;
  - `non-indexable-inlinks-only`: any product linked only from noindex or canonicalised products (perhaps none).
- **Checks that lose flags to a new one:** `canonicalised-page` (C6) and `canonical-to-non-indexable` (C7). Neither has
  a synthetic plant that moves; on the real sites gardenfurnituresets.co.uk's three page-2s move.
- **Counts that move:** about 80 new plant URLs (pages and files: `HtmlPages`, `AnalysedPages`, the hub's links and the
  extra sitemap all follow the plant list); one more sitemap (`empty.xml`) and one more entry in `pages-0.xml`; one more
  link on every page once `og:image` is a link (batch 9: `outlinks`, the link summary, `si stats`' link totals and any
  test that counts them).
- **Time and memory:** the slow partner page adds about 3 s to a test crawl; the 2.2 MB script and the 5.2 MB image
  (sent with its length) are fetched once; the canonical and pagination maps are 8 bytes a page that has one; `deep-page`
  by a breadth-first pass, if it needs one, about 12 MB at 100,000 pages.
- **Analyses already stored** read out of date ("checks changed") from the first batch on, and the catalog's
  `IssueCatalogTests` and `FixFirstOrderTests` follow `IssueCatalog.All` without change.

## Owner decisions

1. **Pixel widths (C8):** switch the four length checks from characters to pixels, using Screaming Frog's limits. Every
   site's counts move.
2. **The not-built list:** in particular what's obsolete (Unsafe Cross-Origin Links, NoODP, NoYDIR, `noarchive`), what
   would flag correct markup (an empty alt, SVG dimensions), Permissions-Policy, and the two retired Google features
   (FAQ, HowTo).
3. **A "Social" category** for checks 51 to 60 (and `open-graph-missing`), or keep them in HTML.
4. **Our own thresholds**, each a setting: depth over 4 clicks; 1,000 internal and 100 external links; 3 s for an external
   link; fewer than 3 internal links; 50 KB for an older image format; 800 px for srcset; 10 KB for an inline image;
   Flesch 30; 25% of a page's own words; 500 words without an H2; the private-page paths of `noindex-near-home`.
5. **One Error** (`sitemap-over-limit`) and 18 Warnings, of which these are a judgement more than a rule:
   `noindex-near-home`, `placeholder-text`, `image-missing-src`, `head-tags-in-body`.

## Least sure

The ten rules most likely to need changing once they meet real sites:

1. `deep-page`: which depth (the crawl's, or a shortest path the analysis works out), and thousands of computed flags
   in the bench.
2. `noindex-near-home`: the private-page list is a guess, and real sites link noindex pages from their menus.
3. `readability-low`: Flesch on HTML text; sentence ends and syllables are heuristics, and only English is judged.
4. `title-same-as-h1`: Google suggests the very thing it flags, so it rests on its words calling it an opportunity.
5. `image-hotlinked`: the CDN list and the registrable domain decide every flag.
6. `x-card-missing`, `x-card-incomplete`, `social-image-too-small`: X's documentation couldn't be read.
7. `canonical-invalid-attribute`: no Google source found.
8. `html-structure-errors`: AngleSharp's error codes are unconfirmed.
9. `structured-data-schema-errors`: how far to check properties per type, and keeping the vocabulary current.
10. `sitemap-over-limit` as an Error: that the whole file is refused wasn't confirmed.

## Sources

Read on 2026-11-17.

- Google Search Central: [robots meta tag](https://developers.google.com/search/docs/crawling-indexing/robots-meta-tag),
  [pagination](https://developers.google.com/search/docs/specialty/ecommerce/pagination-and-incremental-page-loading),
  [build a sitemap](https://developers.google.com/search/docs/crawling-indexing/sitemaps/build-sitemap),
  [robots.txt](https://developers.google.com/search/docs/crawling-indexing/robots/robots_txt),
  [consolidate duplicate URLs](https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls),
  [localized versions](https://developers.google.com/search/docs/specialty/international/localized-versions),
  [crawlable links](https://developers.google.com/search/docs/crawling-indexing/links-crawlable),
  [JavaScript SEO basics](https://developers.google.com/search/docs/crawling-indexing/javascript/javascript-seo-basics),
  [Googlebot](https://developers.google.com/search/docs/crawling-indexing/googlebot),
  [redirects](https://developers.google.com/search/docs/crawling-indexing/301-redirects),
  [mobile-first indexing](https://developers.google.com/search/docs/crawling-indexing/mobile/mobile-sites-mobile-first-indexing),
  [title links](https://developers.google.com/search/docs/appearance/title-link),
  [snippets](https://developers.google.com/search/docs/appearance/snippet),
  [Google Images](https://developers.google.com/search/docs/appearance/google-images),
  structured data: [Article](https://developers.google.com/search/docs/appearance/structured-data/article),
  [Organization](https://developers.google.com/search/docs/appearance/structured-data/organization),
  [product snippets](https://developers.google.com/search/docs/appearance/structured-data/product-snippet),
  [merchant listings](https://developers.google.com/search/docs/appearance/structured-data/merchant-listing),
  [guidelines](https://developers.google.com/search/docs/appearance/structured-data/sd-policies),
  [documentation updates](https://developers.google.com/search/updates) (FAQ, HowTo),
  [crawl budget](https://developers.google.com/crawling/docs/crawl-budget).
- Standards: HTML Living Standard ([the head](https://html.spec.whatwg.org/multipage/semantics.html#the-head-element),
  [charset](https://html.spec.whatwg.org/multipage/semantics.html#charset),
  [lang](https://html.spec.whatwg.org/multipage/dom.html#the-lang-and-xml:lang-attributes),
  [headings](https://html.spec.whatwg.org/multipage/sections.html#headings-and-outlines)),
  [W3C language tags](https://www.w3.org/International/questions/qa-choosing-language-tags),
  [WCAG 2.1 SC 1.4.4](https://www.w3.org/WAI/WCAG21/Understanding/resize-text.html),
  [SC 2.4.4](https://www.w3.org/WAI/WCAG21/Understanding/link-purpose-in-context.html),
  [RFC 9111](https://www.rfc-editor.org/rfc/rfc9111.html), [JSON-LD 1.1](https://www.w3.org/TR/json-ld11/),
  [sitemaps.org](https://www.sitemaps.org/protocol.html), [the Open Graph protocol](https://ogp.me/),
  schema.org [Date](https://schema.org/Date), [DateTime](https://schema.org/DateTime),
  [priceCurrency](https://schema.org/priceCurrency).
- MDN: [Strict-Transport-Security](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Strict-Transport-Security),
  [Set-Cookie](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Set-Cookie),
  [X-Content-Type-Options](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/X-Content-Type-Options),
  [Permissions-Policy](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Permissions-Policy),
  [viewport](https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/meta/name/viewport).
- Others: [hstspreload.org](https://hstspreload.org/),
  [axe meta-viewport](https://dequeuniversity.com/rules/axe/4.10/meta-viewport),
  [Facebook sharing images](https://developers.facebook.com/docs/sharing/webmasters/images/),
  [web.dev responsive images](https://web.dev/articles/serve-responsive-images),
  [Lighthouse modern image formats](https://developer.chrome.com/docs/lighthouse/performance/uses-webp-images),
  [Google Analytics UTM](https://support.google.com/analytics/answer/10917952).
- **Couldn't be read:** Screaming Frog's tabs guide and issues library (403), X's card documentation (402 on
  developer.x.com, 404 on docs.x.com). Rules that rest on them say so.
- Ours: `docs/v2-ui-discovery.md`, `docs/v2-comparison.md`, `docs/v2-evidence.md` (`ui/3b-evidence`), the step 3b
  real-site audit, the old app's `src/ShoutingIguana.Plugins/`, and the engine on `ui/step-3`.
